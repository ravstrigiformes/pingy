# /stage - Promote Changes Between Branches

Safely promote changes from one environment branch to the next with a summary of notable changes.

> **Run this from the main repo** (not from inside a worktree)

---

## Branch contract

> **Why this exists:** `/stage` does the actual checkout-and-merge for every promotion hop. It's the lowest-level branch-mutating skill in the promotion chain and must restore the starting branch even if the merge fails.

- **Starts on:** any environment branch
- **Ends on:** start branch (restored at end, including on failure)
- **Touches branches:** source ref (read) + target branch (checkout + merge + push)
- **Worktree-first:** no — operates in-place on the main repo

**Self-check (every run):**

```bash
# Branch contract: record start branch+HEAD
_SKILL_START_BRANCH=$(git rev-parse --abbrev-ref HEAD)
_SKILL_START_HEAD=$(git rev-parse HEAD)                # captures starting branch
trap 'git checkout "$_BC_START_BRANCH" 2>/dev/null; # Branch contract end: assert unchanged
if [[ "$(git rev-parse --abbrev-ref HEAD)" != "$_SKILL_START_BRANCH" || "$(git rev-parse HEAD)" != "$_SKILL_START_HEAD" ]]; then
  echo "ERROR: branch contract violated" >&2; exit 1
fi' EXIT
# ...skill body (checkout target, merge source, push)...
```

The `trap` ensures the starting branch is restored even on `set -e` exit or merge abort. `# Branch contract end: assert unchanged
if [[ "$(git rev-parse --abbrev-ref HEAD)" != "$_SKILL_START_BRANCH" || "$(git rev-parse HEAD)" != "$_SKILL_START_HEAD" ]]; then
  echo "ERROR: branch contract violated" >&2; exit 1
fi` then asserts and fails loud if HEAD doesn't match the captured start.

---

## Sync merge policy

Promotions cross environment boundaries (`dev` → `staging`, `staging` → `beta`, `beta` → `main`). A conflict at this level is rare but always significant — it almost always means the target branch diverged from upstream and needs human inspection. Use:

```bash
    --strategy=fail-on-conflict \
    --skill=stage \
    --message="stage: $SOURCE → $TARGET"
```

Bare `git merge origin/$SOURCE --no-ff` is forbidden in this skill. The pre-existing dry-run conflict pre-check stays — it predicts conflicts before staging — but the actual merge step now goes through `sync-safe-merge.sh` so phantom-detect runs.

---

## Phantom-merge detection

`sync-safe-merge.sh` runs phantom-detect automatically. A phantom at the `/stage` level usually means: the source branch has the work, but a manual conflict resolution earlier in the chain dropped the code. `/stage` halts on phantom-detect and writes `.kris/audits/sync-aborted-<date>-stage.md`. The chain (if invoked from `/promote`) does not advance.

---

## Quick Start

```bash
/stage                           # Interactive: choose source and target
/stage dev staging               # Promote dev → staging
/stage staging beta              # Promote staging → beta
/stage beta main                 # Promote beta → main (release)
/stage --dry-run                 # Preview what would be promoted
```

---

## Branch Workflow

> **Canonical promotion reference:** see `.kris/context/git-promote.md` for the full branch roles table, regular and hotfix flows, and how `/stage` / `/promote` / `/hotfix` relate. `/stage` is the one-hop primitive used by all three.

---

## How It Works

### 1. Fetch Latest State

```bash
git fetch origin dev staging beta main
```

### 2. Analyze Changes

```bash
# Get commits between source and target
git log origin/{source}..origin/{target} --oneline --reverse

# Get summary statistics
git diff origin/{target}...origin/{source} --stat
```

### 3. Generate Change Summary

Extract from commit messages:
- Issue references (#123)
- Commit types (feat, fix, refactor, etc.)
- Files affected
- Breaking changes (if any)

### 4. Create Merge (Stash-Aware — Parallel Agent Safe)

> **Why stash-aware?** Parallel agents may have uncommitted changes in the main working tree.
> Branch switching would fail or destroy their work. See `/fix` → "Stash-Aware Sync Pattern".

```bash
# Stash any uncommitted work (ours + parallel agents')
STASH_MSG="stage-${SOURCE}-to-${TARGET}"
git stash push --include-untracked -m "$STASH_MSG" 2>/dev/null
STASHED=$?

# Checkout target branch
git checkout {target}
git pull origin {target}

# Merge source into target
git merge origin/{source} --no-ff -m "chore(release): promote {source} to {target}"

# Push
git push origin {target}

# Return to dev (where parallel agents expect to be)
git checkout dev

# Restore stashed work
if [ "$STASHED" -eq 0 ]; then
  git stash pop 2>/dev/null || git stash drop
fi
```

---

## Output Format

```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
📦 STAGING: dev → staging
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

📊 SUMMARY
   Commits: 7 new commits
   Issues:  #260, #258, #255
   Files:   23 changed (+1,842 / -512)

── NEW FEATURES ─────────────────────────────────────

🚀 feat(sa): implement Organization module pages (#257)
   - OrgUnitsPage with two-panel layout
   - LocationsPage with cascading dropdowns
   - HierarchyPage with tree visualization

🚀 feat(build): add enhanced build banner (#255)
   - Display APP_ENV in build output
   - Colorized success banner

── BUG FIXES ────────────────────────────────────────

🐛 fix(admin): resolve OrgChartCanvas temporal dead zone (#260)
   - Fixed ReferenceError in Offices Hierarchy
   - Created shared OrganizationTreeView component

🐛 fix(cds): remove branch code field from bank branches (#259)
   - Cleaned up unused fields
   - Simplified address handling

── OTHER CHANGES ────────────────────────────────────

📝 chore(deps): add explicit rollup windows dependency
🔧 refactor(theme): change default theme to winter

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Promote these changes to staging? [Y/n]: _
```

---

## Phase 1: Pre-flight Checks

### 1.1 Verify Branch State

```bash
# Check we're in main repo (not worktree)
WORKTREE_INFO=$(git rev-parse --git-common-dir 2>/dev/null)
if [[ "$WORKTREE_INFO" != ".git" ]]; then
  echo "⚠️  Run this from the main repo, not inside a worktree"
  exit 1
fi

# Fetch latest
git fetch origin --prune

# Check source has changes target doesn't have
AHEAD=$(git rev-list --count origin/{target}..origin/{source})
if [ "$AHEAD" -eq 0 ]; then
  echo "✅ {target} is already up to date with {source}"
  exit 0
fi
```

### 1.2 Check for Conflicts

```bash
# Dry-run merge to check for conflicts
git checkout {target}
git merge origin/{source} --no-commit --no-ff
CONFLICT_COUNT=$(git diff --name-only --diff-filter=U | wc -l)

if [ "$CONFLICT_COUNT" -gt 0 ]; then
  echo "⚠️  Merge conflicts detected in $CONFLICT_COUNT files"
  git merge --abort
  # Show conflicting files
  git diff --name-only --diff-filter=U
fi
```

---

## Phase 2: Analyze Changes

### 2.1 Gather Commits

```bash
# Get all commits being promoted
git log origin/{target}..origin/{source} \
  --pretty=format:"%h|%s|%an|%ai" \
  --reverse
```

### 2.2 Parse and Categorize

**Category detection from commit messages:**
- `feat:` or `feature:` → Features
- `fix:` or `bugfix:` → Bug Fixes
- `refactor:` → Refactors
- `docs:` → Documentation
- `style:` or `chore:` → Maintenance
- `perf:` → Performance
- `test:` → Tests
- `BREAKING:` or `!:` → Breaking Changes

**Extract issue references:**
```bash
# Find all #123 patterns
git log origin/{target}..origin/{source} --pretty=format:"%s %b" | \
  grep -oE '#[0-9]+' | sort -u
```

### 2.3 Generate Statistics

```bash
# Files changed
git diff origin/{target}...origin/{source} --stat --stat-count=100

# Insertions/deletions
git diff origin/{target}...origin/{source} --shortstat
```

---

## Phase 3: Present Summary

Display a formatted summary (see Output Format above) including:
1. Commit count
2. Issue references
3. File statistics
4. Changes grouped by category
5. Breaking changes (highlighted if any)

---

## Phase 4: Execute Promotion

### 4.1 Confirm and Merge

```bash
# Interactive checkpoint
echo "Promote these changes to {target}? [Y/n]: "

# Execute merge
git checkout {target}
git pull origin {target}
git merge origin/{source} --no-ff -m "$(cat <<EOF
chore(release): promote {source} to {target}

Commits: {commit_count}
Issues: {issue_list}

Changes:
{change_summary}
EOF
)"
```

### 4.2 Push

```bash
git push origin {target}
```

### 4.3 Return to Dev

```bash
# Return to dev branch
git checkout dev
```

---

## Phase 5: Versioning & Changelog (for beta → main only)

When promoting to production (beta → main), handle versioning and changelog:

### 5.1 Version Increment

**Versioning scheme:** `vW.X.Y.Z`
- W = Major (breaking changes, architectural shifts)
- X = Minor (new features, API changes)
- Y = Patch (bugfixes, small improvements)
- Z = Iteration (formatting, docs, config)

**Auto-detect increment based on commits being promoted:**

```bash
# Get current version from last tag
LAST_TAG=$(git describe --tags --abbrev=0 origin/main 2>/dev/null || echo "v0.0.0.0")

# Parse commits to determine increment level
COMMITS=$(git log origin/main..origin/beta --pretty=format:"%s")

# Check for breaking changes (W increment)
if echo "$COMMITS" | grep -qE "BREAKING|!:"; then
  INCREMENT="major"
# Check for features (X increment)
elif echo "$COMMITS" | grep -qE "^feat"; then
  INCREMENT="minor"
# Check for fixes (Y increment)
elif echo "$COMMITS" | grep -qE "^fix"; then
  INCREMENT="patch"
# Default to iteration (Z increment)
else
  INCREMENT="iteration"
fi

# Calculate new version
NEW_VERSION=$(increment_version "$LAST_TAG" "$INCREMENT")

# Sync version to package.json and .env
npm pkg set version="$(echo $NEW_VERSION | tr -d 'v')"
sed -i "s/^APP_VERSION=.*/APP_VERSION=$(echo $NEW_VERSION | tr -d 'v')/" .env
git add package.json
git commit -m "chore: sync package.json version to $NEW_VERSION"
```

### 5.2 Create Release Tag

```bash
# Create annotated tag
git tag -a "$NEW_VERSION" -m "Release $NEW_VERSION

Promoted: beta → main
Date: $(date +%Y-%m-%d)

Issues: {issue_list}

Changes:
{change_summary}
"

# Push tag
git push origin "$NEW_VERSION"
```

### 5.3 Update Changelog

```bash
# Only for production releases
if [ "{target}" = "main" ]; then
  # Update changelog summary with release version and date
  # Add release marker
fi
```

### Changelog Entry for Releases

```markdown
## Release: v{VERSION} (YYYY-MM-DD)

**Promoted:** beta → main
**Tag:** v{VERSION}

### Included Issues
- #260, #258, #255, #257

### Summary
- 3 features, 2 bug fixes, 2 maintenance items

### Version Increment
- Previous: v0.0.2.0
- New: v0.0.3.0 (minor - new features)
```

---

## Flags

### `--dry-run` or `-d`

Preview without making changes:

```bash
/stage dev staging --dry-run
```

Shows:
- What commits would be promoted
- Conflict detection
- Statistics

### `--force` or `-f`

Skip confirmation prompt:

```bash
/stage dev staging --force
```

### `--no-changelog`

Skip changelog update (for beta → main):

```bash
/stage beta main --no-changelog
```

---

## Valid Promotions

| From | To | Notes |
|------|-----|-------|
| `dev` | `staging` | Most common - after features are ready for QA |
| `staging` | `beta` | After QA approval - testing with live data |
| `beta` | `main` | Production release - after beta validation |

**Invalid promotions (blocked):**
- `staging` → `dev` (use backport instead)
- `main` → anything (use hotfix flow)
- Skipping stages (dev → main directly)

---

## Backport Flow

When you need to backport changes (e.g., after hotfix):

```bash
# Hotfixes flow: main → beta → staging → dev
/stage main beta        # Backport to beta
/stage beta staging     # Backport to staging
/stage staging dev      # Backport to dev
```

Or use the `/hotfix` skill which handles backporting automatically.

---

## Examples

### Promote Dev to Staging

```
> /stage dev staging

📦 STAGING: dev → staging
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

📊 SUMMARY
   Commits: 5 new commits
   Issues:  #260, #261, #262
   Files:   15 changed (+892 / -123)

🚀 feat: add user avatar upload (#262)
🐛 fix: resolve OrgChart temporal dead zone (#260)
📝 docs: update API documentation

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Promote these changes to staging? [Y/n]: Y

✅ Merged dev → staging
   Pushed to origin/staging

📍 Returned to dev branch
```

### Dry Run

```
> /stage staging beta --dry-run

📦 DRY RUN: staging → beta
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Would promote 5 commits:
  abc1234 feat: add user avatar upload (#262)
  def5678 fix: resolve OrgChart temporal dead zone (#260)
  ...

No conflicts detected.
Files: 15 changed (+892 / -123)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Run without --dry-run to execute.
```

### Production Release

```
> /stage beta main

📦 RELEASE: beta → main
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

⚠️  PRODUCTION DEPLOYMENT

📊 SUMMARY
   Commits: 12 new commits
   Issues:  #255, #257, #258, #259, #260, #261
   Files:   45 changed (+2,341 / -892)

🚀 FEATURES (3):
   - Organization module pages (#257)
   - Enhanced build banner (#255)
   - User avatar upload (#262)

🐛 BUG FIXES (2):
   - OrgChart temporal dead zone (#260)
   - Bank branches cleanup (#259)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

⚠️  This will deploy to PRODUCTION.
Promote these changes to main? [Y/n]: Y

✅ Merged beta → main
   Pushed to origin/main

📓 Changelog updated with release marker

📍 Returned to dev branch
```

---

## Error Handling

| Error | Resolution |
|-------|------------|
| "Nothing to promote" | Source and target are already in sync |
| "Merge conflicts" | Resolve conflicts manually, then retry |
| "Invalid promotion" | Check valid promotion paths |
| "Not on main repo" | Run from main repo, not worktree |
| "Uncommitted changes" | Commit or stash local changes first |

---

## Integration with Other Skills

| Skill | Relationship |
|-------|-------------|
| `/fix` | Creates commits in dev → use `/stage` to promote |
| `/hotfix` | Goes directly to main → backport with `/stage main beta` etc. |
| `/commit` | Creates commits in dev → use `/stage` to promote |
| `/worktree-cleanup` | Clean up after PR merged to dev |

---

## Quick Reference

```bash
# Standard promotion flow
/stage dev staging       # QA testing
/stage staging beta      # Beta testing with live data
/stage beta main         # Production release

# Check what would be promoted
/stage dev staging --dry-run

# Quick promotion (skip confirmation)
/stage dev staging --force

# Backport after hotfix
/stage main beta && /stage beta staging && /stage staging dev
```
