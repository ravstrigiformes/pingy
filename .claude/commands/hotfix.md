# /hotfix - Critical Production Fix

> **Mirror:** `.kris/commands/hotfix.md` is a byte-for-byte copy of this file. `.claude/commands/` is the harness-loaded canonical; `.kris/commands/` is the checked-in mirror. After editing this file, run: `cp .claude/commands/hotfix.md .kris/commands/hotfix.md`

Fast-track workflow for critical production issues. Creates a hotfix branch from `main` (production), validates dependencies and build, merges directly to `main`, then backports to `beta`, `staging`, and `dev`.

> **When to use `/hotfix`:**
> - Production is broken and users are affected
> - Security vulnerability discovered
> - Build is failing in production
> - Critical functionality completely unusable

> **When NOT to use `/hotfix`:**
> - Normal bug fixes (use `/fix` instead)
> - Features that can wait for next release
> - Non-critical improvements

---

## Branch contract

> **Why this exists:** `/hotfix` chains FOUR sequential checkouts in the main repo (`main` → `beta` → `staging` → `dev`), each followed by a merge and push. Without a contract, any of those checkouts can collide with parallel-agent WIP on the target branch.

- **Starts on:** any (typically `main` or current production branch)
- **Ends on:** `dev` (after backport chain) — this is the ONE skill where the contract permits a different end ref, because the backport chain is the skill's purpose
- **Touches branches:** `hotfix/*`, `main`, `beta`, `staging`, `dev`
- **Worktree-first:** REQUIRED for backport hops (see "Worktree-first for backports" below)

**Self-check (every run):**

```bash
# Branch contract: record start branch+HEAD
_SKILL_START_BRANCH=$(git rev-parse --abbrev-ref HEAD)
_SKILL_START_HEAD=$(git rev-parse HEAD)                # captures starting HEAD branch
# ...skill body... (branch changes are expected; no # Branch contract end: assert unchanged
if [[ "$(git rev-parse --abbrev-ref HEAD)" != "$_SKILL_START_BRANCH" || "$(git rev-parse HEAD)" != "$_SKILL_START_HEAD" ]]; then
  echo "ERROR: branch contract violated" >&2; exit 1
ficall)
```

Because `/hotfix` intentionally lands on `dev`, it does NOT call `# Branch contract end: assert unchanged
if [[ "$(git rev-parse --abbrev-ref HEAD)" != "$_SKILL_START_BRANCH" || "$(git rev-parse HEAD)" != "$_SKILL_START_HEAD" ]]; then
  echo "ERROR: branch contract violated" >&2; exit 1
fi`. The starting branch is captured and printed at end so the user can manually return if desired.

---

## Worktree-first for backports

Sequential checkouts in the main repo collide with parallel-agent WIP. Use a dedicated worktree per backport target:

```bash
# Instead of: git checkout beta && git merge origin/main && git push
git worktree add ../.worktrees/hotfix-backport-beta beta
    --dir=../.worktrees/hotfix-backport-beta \
    --strategy=fail-on-conflict \
    --skill=hotfix \
    --message="Backport hotfix to beta"
git -C ../.worktrees/hotfix-backport-beta push origin beta
git worktree remove ../.worktrees/hotfix-backport-beta
```

The orchestrator's main-repo HEAD never moves. Repeat for `staging` and `dev`.

---

## Sync merge policy

Hotfix backports are the inverse of normal sync — the upstream (`main`) IS the source of truth and the target branches must accept the hotfix wholesale. Use `--strategy=fail-on-conflict` so the human is forced to review any conflict (a hotfix conflict almost always means another commit landed on the target between branches, which deserves human attention).

```bash
```

Bare `git merge origin/main --no-edit` is forbidden — it accepts whatever the merge tool produces, the same failure mode that produced PR #538.

---

## Phantom-merge detection

`sync-safe-merge.sh` runs phantom-detect automatically. For hotfix backports specifically, a "phantom" looks like: target branch's merge commit landed but no code-extension lines moved — meaning the hotfix's actual fix bytes were dropped by conflict resolution. When phantom-detect fires during a backport hop, the chain HALTS at that hop. Subsequent hops are not attempted. The user must resolve and re-run from that hop forward.

---

## ⚠️ CRITICAL: Dependency Validation

> **THE #1 CAUSE OF FAILED HOTFIXES: Missing dependencies**
>
> A committed file imports another file that doesn't exist in the repo.
> Build passes locally (file exists), but FAILS in production (file missing).
>
> **Real example:** `useAdminNavigation.ts` imported `RoutingTemplatesPage.vue`,
> but `RoutingTemplatesPage.vue` was never committed (only existed locally).
> Production build failed with "Cannot find module" error.

**BEFORE pushing ANY hotfix, you MUST:**
1. Check that ALL imports in changed files point to files that EXIST IN THE REPO
2. Run `npm run build` and verify it succeeds
3. If build fails, DO NOT PUSH - fix the issue first

---

## Hotfix vs Fix

`/hotfix` bypasses the normal promotion queue: branch from `main`, merge to `main` first, then backport the same commit downstream through `beta → staging → dev`. `/fix` flows the opposite direction through `/stage`.

> **Promotion flow reference:** see `.kris/context/git-promote.md` — branch roles, regular downstream promotion, hotfix backport chain, and skill relationships.

Use `/hotfix` only for true production emergencies (see "When to use" above). For normal bug fixes, use `/fix`.

---

## Phase 1: Pre-flight (Stash-Aware — Parallel Agent Safe)

> **Why stash-aware?** Parallel agents may have uncommitted changes on dev.
> Switching to main would fail or destroy their work. See `/fix` → "Stash-Aware Sync Pattern".

```bash
# Verify authentication
gh auth status

# Stash any uncommitted work (parallel agents' changes on dev)
STASH_MSG="hotfix-pre-flight"
git stash push --include-untracked -m "$STASH_MSG" 2>/dev/null
STASHED=$?

# Switch to main (production) and update
git checkout main
git pull origin main
```

> **Note:** The stash is restored AFTER the hotfix completes and we return to dev (see Phase N: Cleanup).

---

## Phase 2: Dependency Validation (MANDATORY)

> **This phase is NON-NEGOTIABLE. Skipping it can break production.**

### 2.1 Identify ALL Changed Files

```bash
# List everything that will be committed
git diff --name-only
git diff --cached --name-only
git status --porcelain
```

### 2.2 Check Imports in Changed Files

For EACH changed TypeScript/Vue file, verify ALL imports exist:

```bash
# Extract and verify all imports
for file in $(git diff --name-only -- '*.ts' '*.vue' '*.tsx'); do
  echo "=== Checking: $file ==="

  # Check @/ alias imports
  grep -oE "from ['\"]@/[^'\"]+['\"]" "$file" 2>/dev/null | while read import; do
    IMPORT_PATH=$(echo "$import" | sed "s/from ['\"]@\///;s/['\"]//g")

    # Try common extensions
    FOUND=false
    for ext in "" ".ts" ".vue" ".tsx" "/index.ts" "/index.vue"; do
      if [ -f "resources/js/${IMPORT_PATH}${ext}" ]; then
        FOUND=true
        break
      fi
    done

    if [ "$FOUND" = false ]; then
      # Check if it's in git (committed)
      if ! git ls-files --error-unmatch "resources/js/${IMPORT_PATH}"* 2>/dev/null; then
        echo "❌ MISSING IN REPO: @/${IMPORT_PATH}"
        echo "   File may exist locally but is NOT committed!"
      fi
    fi
  done
done
```

### 2.3 Check for Phantom Imports (CRITICAL)

**Phantom imports** = imports of files that exist locally but aren't in the repo.

```bash
# Find all imports pointing to non-existent (in git) files
git diff --name-only -- '*.ts' '*.vue' | xargs grep -h "from '@/" 2>/dev/null | \
  sed "s/.*from '@\///;s/'.*//;s/\".*//;s/;.*//" | sort -u | while read import; do

  # Check if file exists in git index
  EXISTS=$(git ls-files "resources/js/${import}"* 2>/dev/null | head -1)

  if [ -z "$EXISTS" ]; then
    # Check if it exists locally but isn't tracked
    LOCAL=$(ls "resources/js/${import}"* 2>/dev/null | head -1)
    if [ -n "$LOCAL" ]; then
      echo "⚠️  PHANTOM IMPORT: $import"
      echo "   EXISTS locally: $LOCAL"
      echo "   NOT in git repo - will fail in production!"
    else
      echo "❌ MISSING: $import (not found anywhere)"
    fi
  fi
done
```

### 2.4 Build Validation (REQUIRED)

```bash
# This MUST pass before pushing
npm run build

# Check exit code
if [ $? -ne 0 ]; then
  echo "❌ BUILD FAILED - DO NOT PUSH"
  echo "Fix the build errors first!"
  exit 1
fi

echo "✅ Build passed"
```

**If build fails locally, it WILL fail in production. Fix it first.**

---

## Phase 3: Create Issue & Branch

> **Follow `/issue` style guide** for issue creation — even hotfixes get proper project integration.
> Use Bug.yml template with Severity: Critical. See `/issue` for full style guide.

```bash
# Create critical issue (following /issue style guide)
# Title: [Bug]: prefix, sentence case, <80 chars
# Body: Bug.yml template sections (Description, Steps, Expected/Actual, Severity=Critical)
# Project fields: Status=Backlog, Priority=P0, System=auto-detected, Assignee=ravstrigiformes
ISSUE_URL=$(gh issue create \
  --title "[Bug]: $DESCRIPTION" \
  --label "bug" \
  --body "$ISSUE_BODY")

ISSUE_NUM=$(echo "$ISSUE_URL" | grep -oE '[0-9]+$')

# Issue number cascades into: branch, task file, session title
git checkout -b "hotfix/${ISSUE_NUM}-${SLUG}"
```

---

## Phase 3.5: Set Session & Terminal Title

After issue and branch creation, set the session name and pane title:

```bash
TITLE="#${ISSUE_NUM} ${SLUG}"
/rename $TITLE
echo -ne "\033]0;${TITLE}\007"
```

---

## Phase 3.7: Session-Scoped Backend Sweep (Warn-Only — Hotfix Gate)

> *Hotfixes go straight to `main`. False positives ship wrong code to production. The sweep runs in `warn-only` mode and ALWAYS requires interactive `[y/N]` confirmation before staging anything — even when `AUTO_MODE=true`.*

### 3.7.1 Resolve MAIN_GIT_ROOT and source the helper (gated)

```bash
SBS_HF_SKIP=false
GIT_COMMON_DIR=$(git rev-parse --git-common-dir)
MAIN_GIT_ROOT="${GIT_COMMON_DIR%/.git}"
# Round-trip through `cd && pwd` to absolutize relative results like `../.git`.
MAIN_GIT_ROOT="$(cd "$MAIN_GIT_ROOT" 2>/dev/null && pwd)" || MAIN_GIT_ROOT=""
if [ -z "$MAIN_GIT_ROOT" ] || [ ! -d "${MAIN_GIT_ROOT}/.kris" ]; then
  echo "🚫 Backend sweep: MAIN_GIT_ROOT resolved to '${MAIN_GIT_ROOT}' but .kris/ not found"
  echo "   Skipping hotfix sweep to avoid silent miss. Inspect manually."
  SBS_HF_SKIP=true
fi

if [ "$SBS_HF_SKIP" != "true" ]; then
fi
```

### 3.7.2 Run the sweep (warn-only) — gated on SBS_HF_SKIP

```bash
if [ "$SBS_HF_SKIP" != "true" ]; then
  RESULT_FILE="/tmp/sbs-${ISSUE_NUM}-$$"
  session_backend_sweep \
    --issue "$ISSUE_NUM" \
    --mode warn-only \
    --repo-root "$MAIN_GIT_ROOT" \
    --result-file "$RESULT_FILE"
  SWEEP_RC=$?

  source "$RESULT_FILE"
  rm -f "$RESULT_FILE"
fi
```

### 3.7.3 Conflict halt — gated on SBS_HF_SKIP

```bash
if [ "$SBS_HF_SKIP" != "true" ]; then
  if [ "$SWEEP_RC" -eq 1 ] || [ "${#SBS_CONFLICTS[@]}" -gt 0 ]; then
    echo "🚫 /hotfix halted: backend sweep found conflicting session claims."
    echo "   Cannot auto-resolve overlaps in a hotfix — production target is too sensitive."
    echo "   Resolve manually then re-run."
    exit 1
  fi
fi
```

### 3.7.4 Interactive confirmation (CRITICAL — AUTO_MODE does NOT bypass this)

```
⚠️  HOTFIX BACKEND SWEEP — straight-to-main; verify nothing missing
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

These files would be staged if confirmed:
  - app/Modules/Foo/Bar.php
  - app/Modules/Foo/BarRequest.php

Orphan candidates (NOT claimed; review manually if any belong to this hotfix):
  - app/Modules/Other/Unrelated.php

Stage the listed backend files? [y/N]: _
```

> **AUTO_MODE override (mandatory):** The `[y/N]` prompt blocks for interactive input regardless of `AUTO_MODE`. Hotfixes target main with zero tolerance for false positives — auto-staging without an explicit ack would defeat the safety rationale. Document this exception inline; do NOT default to `Y` in AUTO_MODE.

The prompt is suppressed when `SBS_TO_STAGE` is empty — there's nothing to stage and asking the user to confirm a no-op is bad UX.

```bash
if [ "$SBS_HF_SKIP" != "true" ] && [ "${#SBS_TO_STAGE[@]}" -gt 0 ]; then
  read -r -p "Stage the listed backend files? [y/N]: " ANS
  case "$ANS" in
    y|Y|yes|YES)
      for path in "${SBS_TO_STAGE[@]}"; do
        git -C "$MAIN_GIT_ROOT/backend" add -- "$path" \
          || echo "    ⚠️  git add failed for: $path"
      done
      echo "📦 Staged ${#SBS_TO_STAGE[@]} backend file(s) for hotfix #${ISSUE_NUM}"
      ;;
    *)
      echo "ℹ️  Hotfix proceeding without staging swept backend files."
      ;;
  esac
elif [ "$SBS_HF_SKIP" != "true" ]; then
  echo "ℹ️  Hotfix sweep: no session-scoped backend files to confirm — continuing."
fi
```

### 3.7.5 Continue to Phase 4

After this phase, Phase 4's `git add $FILES` line will pick up any user-confirmed sweep additions plus the hotfix's existing file list.

---

## Phase 4: Commit & Push

```bash
# Stage ONLY the specific files needed
git add $FILES

# Commit with conventional format
git commit -m "fix($SCOPE): $DESCRIPTION (#$ISSUE_NUM)

$DETAILS

Resolves #$ISSUE_NUM

Co-Authored-By: Claude <noreply@anthropic.com>"

# Push branch
git push -u origin "hotfix/${ISSUE_NUM}-${SLUG}"
```

---

## Phase 5: PR to Main (Production) → Merge

```bash
# Create PR directly to main (production)
gh pr create \
  --base main \
  --title "fix($SCOPE): $DESCRIPTION (#$ISSUE_NUM)" \
  --body "## Summary

🚨 **HOTFIX** - Critical production fix

- $SUMMARY

## Test plan

- [x] Dependency validation passed
- [x] npm run build succeeds
- [ ] Verified fix locally

Resolves #$ISSUE_NUM

🤖 Generated with [Claude Code](https://claude.com/claude-code)"

# Merge to main
gh pr merge $PR_NUM --merge --auto

# Wait for merge and update local
gh pr view $PR_NUM --json state,mergedAt
git checkout main
git pull origin main
```

---

## Phase 6: Backport to Beta, Staging, and Dev

```bash
# Backport to beta
git checkout beta
git pull origin beta
git merge origin/main --no-edit
git push origin beta

# Backport to staging
git checkout staging
git pull origin staging
git merge origin/beta --no-edit
git push origin staging

# Backport to dev
git checkout dev
git pull origin dev
git merge origin/staging --no-edit
git push origin dev

# Restore stashed work from Phase 1 (parallel agents' changes)
if [ "$STASHED" -eq 0 ]; then
  git stash pop 2>/dev/null || git stash drop
fi
```

**Alternative: Create backport PRs (for audit trail):**

```bash
# Create PR from main to beta
gh pr create \
  --base beta \
  --head main \
  --title "chore: backport hotfix #$ISSUE_NUM to beta" \
  --body "## Summary

Backporting hotfix from main to beta.

Original PR: #$MAIN_PR_NUM

🤖 Generated with [Claude Code](https://claude.com/claude-code)"

gh pr merge --merge --auto

# Create PR from beta to staging
gh pr create \
  --base staging \
  --head beta \
  --title "chore: backport hotfix #$ISSUE_NUM to staging" \
  --body "## Summary

Backporting hotfix from beta to staging.

Original PR: #$MAIN_PR_NUM

🤖 Generated with [Claude Code](https://claude.com/claude-code)"

gh pr merge --merge --auto

# Create PR from staging to dev
gh pr create \
  --base dev \
  --head staging \
  --title "chore: backport hotfix #$ISSUE_NUM to dev" \
  --body "## Summary

Backporting hotfix from staging to dev.

Original PR: #$MAIN_PR_NUM

🤖 Generated with [Claude Code](https://claude.com/claude-code)"

gh pr merge --merge --auto
```

---

## Phase 7: Cleanup

```bash
# Delete local hotfix branch
git branch -D "hotfix/${ISSUE_NUM}-${SLUG}"

# Verify fix is on all branches
git log --oneline origin/main -1
git log --oneline origin/beta -1
git log --oneline origin/staging -1
git log --oneline origin/dev -1
```

---

## Complete Example

```
> /hotfix "build failing - missing RoutingTemplatesPage import"

🚨 HOTFIX MODE
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

📊 Analyzing changes...
   Found 1 file to fix:
   • resources/js/composables/admin/useAdminNavigation.ts

🔍 DEPENDENCY VALIDATION (mandatory)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Checking imports in useAdminNavigation.ts...

  ✅ @/pages/app/admin/app_manager/AppManagerPage.vue (in repo)
  ✅ @/pages/app/admin/documents/DocumentTypesPage.vue (in repo)
  ⚠️  @/pages/app/admin/documents/RoutingTemplatesPage.vue
      └─ EXISTS locally but NOT in repo!
      └─ This will FAIL in production!

Action: Commenting out the import to fix build.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

🔨 BUILD VALIDATION (mandatory)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Running: npm run build
   ✅ Build succeeded

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

📋 Creating issue...
   #180: [Bug]: CRITICAL - Build failing - missing RoutingTemplatesPage import

🌿 Creating branch...
   hotfix/180-disable-routing-templates-import

💾 Committing...
   fix(admin): disable routing templates import to fix build (#180)

🚀 Pushing...
   origin/hotfix/180-disable-routing-templates-import

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

📝 PR #181: hotfix → main
🔀 Merging to main (production)...
   ✅ PR #181 merged

🔄 Backporting to staging...
   ✅ staging updated

🔄 Backporting to dev...
   ✅ dev updated

🧹 Cleaning up...
   Deleted local branch

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
✅ HOTFIX DEPLOYED TO PRODUCTION
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

📋 Issue: #180 (closed)
💾 Commit: abc1234

📝 Pull Request:
   #181: hotfix → main (MERGED ✓)

🔄 Backported:
   main → beta → staging → dev (all synced)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

---

## Flags

### `--auto` or `-a`

Skip prompts, auto-merge both PRs. **Still runs dependency/build checks.**

```
/hotfix --auto "database connection timeout"
```

### `--no-issue`

Skip issue creation:

```
/hotfix --no-issue "already tracked in #123"
```

### `--dry-run`

Show what would happen without making changes:

```
/hotfix --dry-run "session expiry bug"
```

---

## Interactive Checkpoints

| # | Checkpoint | Default | `--auto` | Can Skip? |
|---|------------|---------|----------|-----------|
| 1 | Confirm critical | [Y] | Skip | Yes |
| 2 | **Dependency check** | **REQUIRED** | **REQUIRED** | **NO** |
| 3 | **Build validation** | **REQUIRED** | **STOP on fail** | **NO** |
| 4 | Issue content | Auto | Skip | Yes |
| 5 | Commit message | Auto | Skip | Yes |
| 6 | PR to staging | [Y] | Auto | No |
| 7 | Merge staging | [Y] | Auto | No |
| 8 | PR to main | [Y] | Auto | No |
| 9 | Merge main | [Y] | Auto | No |
| 10 | **Changelog update** | **Auto** | **Auto** | **NO** |

**Dependency and build validation are MANDATORY.** Even `--auto` stops on failure.
**Changelog is ALWAYS updated** - documents the hotfix for future reference.

---

## Dependency Validation Checklist

Before pushing ANY hotfix:

- [ ] Run `git status` - understand what's changed vs what's untracked
- [ ] Check imports in changed files point to COMMITTED files
- [ ] Look for "phantom imports" - local files not in repo
- [ ] `npm run build` passes
- [ ] `npx vue-tsc --noEmit` passes (TypeScript check)

### Common Dependency Mistakes

| Mistake | What Happens | How to Detect |
|---------|--------------|---------------|
| Import uncommitted file | "Cannot find module" in prod | `git ls-files` doesn't show it |
| Import untracked local file | Build works locally, fails remotely | File in `.gitignore` or just not added |
| New component imports missing dep | Cascading import errors | Trace full import chain |
| Type imports missing interface | TS compilation fails | Check `.d.ts` and type files |

### The Phantom Import Problem

```typescript
// useAdminNavigation.ts (COMMITTED to repo)
import RoutingTemplatesPage from '@/pages/app/admin/documents/RoutingTemplatesPage.vue';
//     ^^^^^^^^^^^^^^^^^^^ This file is NOT committed!
//     Exists locally, so build works on dev machine
//     Does NOT exist on server, so production build FAILS
```

**Detection:**
```bash
# Check if imported file is actually in repo
git ls-files resources/js/pages/app/admin/documents/RoutingTemplatesPage.vue
# No output = file is NOT in repo = WILL FAIL in production
```

---

## Error Handling

| Error | Resolution |
|-------|------------|
| Dependency check fails | Add missing files OR remove imports |
| Build fails | Fix build errors BEFORE pushing |
| PR merge conflict | Resolve conflict, verify fix present |
| Not on main | `git checkout main && git pull` |
| Backport conflict | Resolve conflict during backport, ensure fix in all branches |

---

## Post-Hotfix Checklist

- [ ] Verify fix in production
- [ ] Confirm ALL branches have the fix (main, beta, staging, dev)
- [ ] Close issue if not auto-closed
- [ ] Notify team of deployment
- [ ] Consider adding test coverage
- [ ] Schedule post-mortem if needed

---

## Changelog Auto-Update (MANDATORY — INCLUDED IN COMMIT)

> **CRITICAL:** The changelog entry MUST be created BEFORE the commit and included in the same commit.
> Use `(pending)` placeholders for PR numbers (back-filled after PRs are created via amend + force-push).
> This prevents changelog entries from being "left behind" as uncommitted local changes.

Update the weekly changelog at `.kris/changelogs/`.

### Changelog File

```bash
# Calculate current week's changelog file (Monday to Sunday)
TODAY=$(date +%Y-%m-%d)
DOW=$(date +%u)  # 1=Monday, 7=Sunday
MONDAY=$(date -d "$TODAY -$((DOW-1)) days" +%Y-%m-%d)
SUNDAY=$(date -d "$MONDAY +6 days" +%Y-%m-%d)
CHANGELOG_FILE=".kris/changelogs/${MONDAY}_to_${SUNDAY}.md"
```

### Hotfix Entry Format

Hotfixes are marked with 🚨 to indicate urgency.

**Before commit** (with placeholders):
```markdown
#### 🚨 fix: {description} (v{VERSION})
- **Commit**: `(pending)`
- **Issue**: #{NUM}
- **Severity**: Critical
- **Area**: {backend|frontend|fullstack}
- **Files**: `{file1}`, `{file2}`, ...
- **Root Cause**: {Brief description of what was broken}
- **Resolution**: {Brief description of how it was fixed}
- **PRs**: (pending)
```

**After back-fill** (commit hash via amend pre-push, PRs via amend post-merge):
```markdown
#### 🚨 fix: {description} (v{VERSION})
- **Commit**: `{HASH}`
- **Issue**: #{NUM}
- **Severity**: Critical
- **Area**: {backend|frontend|fullstack}
- **Files**: `{file1}`, `{file2}`, ...
- **Root Cause**: {Brief description of what was broken}
- **Resolution**: {Brief description of how it was fixed}
- **PRs**: #{staging_pr} → staging, #{main_pr} → main
```

### Update Summary Statistics

1. **Version Range**: Update end version (increment BUILD segment)
2. **Total Commits**: Increment by 1
3. **Bug Fixes**: Increment by 1 (hotfixes are always bug fixes)
4. **By Area**: Increment appropriate area counter

### Example Hotfix Entry

```markdown
### Tuesday, March 4

#### 🚨 fix: disable routing templates import to fix build (v0.2.8.1)
- **Commit**: `2d505cfb`
- **Issue**: #180
- **Severity**: Critical
- **Area**: Frontend
- **Files**: `useAdminNavigation.ts`
- **Root Cause**: Import referenced uncommitted file (phantom import)
- **Resolution**: Commented out import until feature is ready
- **PRs**: #181 → staging, #182 → main
```

### Hotfix in Example Output

The complete example output should show changelog update:

```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
✅ HOTFIX DEPLOYED TO PRODUCTION
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

📋 Issue: #180 (closed)
💾 Commit: 2d505cfb
🏷️  Version: v0.2.8.0 → v0.2.8.1

📝 Pull Requests:
   #181: hotfix → staging (MERGED ✓)
   #182: staging → main (MERGED ✓)

📓 Changelog Updated:
   .kris/changelogs/2026-03-03_to_2026-03-09.md
   Entry added under Tuesday, March 4

🔄 Backported:
   main → beta → staging → dev (all synced)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

---

## Safety Rules

1. **NEVER skip dependency validation** - broken prod is worse than delayed fix
2. **ALWAYS run `npm run build`** - catches missing imports
3. **Both branches get the fix** - staging AND main, no exceptions
4. **Minimal changes only** - fix one thing, resist scope creep
5. **Verify imports** - if a file imports another, BOTH must be committed
6. **Check `git ls-files`** - confirms file is actually in the repo

---

## Version Increment (Optional)

Hotfixes can increment the **BUILD** segment (4th number):

| Current Version | After Hotfix | Notes |
|-----------------|--------------|-------|
| `0.2.8.0` | `0.2.8.1` | First hotfix on 0.2.8 |
| `0.2.8.1` | `0.2.8.2` | Second hotfix |
| `0.3.0.0` | `0.3.0.1` | Hotfix on new minor |

**Why BUILD instead of PATCH?**
- PATCH (`Y`) is for planned bug fixes in normal release cycle
- BUILD (`Z`) indicates an out-of-band emergency fix
- Makes it clear this was a production emergency

```bash
# Get current version and increment BUILD
CURRENT=$(git describe --tags --abbrev=0 origin/main 2>/dev/null | sed 's/^v//' || echo "0.0.0.0")
IFS='.' read -r MAJOR MINOR PATCH BUILD <<< "$CURRENT"
BUILD=${BUILD:-0}
BUILD=$((BUILD + 1))
NEW_VERSION="${MAJOR}.${MINOR}.${PATCH}.${BUILD}"

# Create tag after merge to main
git tag -a "v${NEW_VERSION}" -m "Hotfix v${NEW_VERSION} - $DESCRIPTION"
git push origin "v${NEW_VERSION}"

# Sync version to package.json and .env
npm pkg set version="${NEW_VERSION}"
sed -i "s/^APP_VERSION=.*/APP_VERSION=${NEW_VERSION}/" .env
git add package.json

# Optionally sync .env.example
sed -i "s/^APP_VERSION=.*/APP_VERSION=${NEW_VERSION}/" .env.example
```

---

## Example: Dependency Check Failure

```
> /hotfix "payment processing crash"

🔍 DEPENDENCY VALIDATION (mandatory)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Checking imports in changed files...

PaymentService.php imports:
  ✅ App\Modules\Finance\Models\Payment (exists in repo)
  ❌ App\Modules\Finance\DTOs\PaymentResultDto (NOT FOUND!)
     └─ File does not exist in repo
     └─ File is not in current changes
     └─ git ls-files shows: (empty)

usePaymentForm.ts imports:
  ✅ @/composables/useApiClient (exists in repo)
  ⚠️  @/types/payment-result.type (PHANTOM IMPORT!)
     └─ File EXISTS locally: resources/js/types/payment-result.type.ts
     └─ File is NOT tracked by git
     └─ Production build WILL FAIL

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
❌ DEPENDENCY CHECK FAILED
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Missing dependencies will cause production build to FAIL!

You must either:
  [a] Add the missing files to this hotfix
  [b] Remove/comment out the imports if not needed
  [c] Abort and investigate

Your choice: _
```

---

## Automated Dependency Check Script

Save as `scripts/check-hotfix-deps.sh`:

```bash
#!/bin/bash
# Pre-hotfix dependency validator

echo "🔍 Checking dependencies for hotfix..."
ERRORS=0

# Get changed TypeScript/Vue files
CHANGED_FILES=$(git diff --name-only -- '*.ts' '*.vue' '*.tsx' 2>/dev/null)

for file in $CHANGED_FILES; do
  [ -f "$file" ] || continue

  echo "Checking: $file"

  # Extract @/ imports
  grep -oE "from ['\"]@/[^'\"]+['\"]" "$file" 2>/dev/null | while read import; do
    IMPORT_PATH=$(echo "$import" | sed "s/from ['\"]@\///;s/['\"]//g")

    # Check if file exists in git index
    FOUND=false
    for ext in "" ".ts" ".vue" ".tsx" "/index.ts" "/index.vue"; do
      CHECK="resources/js/${IMPORT_PATH}${ext}"
      if git ls-files --error-unmatch "$CHECK" 2>/dev/null; then
        FOUND=true
        break
      fi
    done

    if [ "$FOUND" = false ]; then
      # Check if exists locally (phantom import)
      if ls "resources/js/${IMPORT_PATH}"* 2>/dev/null | head -1; then
        echo "  ⚠️  PHANTOM: @/${IMPORT_PATH} (local only, not in git!)"
      else
        echo "  ❌ MISSING: @/${IMPORT_PATH}"
      fi
      ERRORS=$((ERRORS + 1))
    else
      echo "  ✅ @/${IMPORT_PATH}"
    fi
  done
done

if [ $ERRORS -gt 0 ]; then
  echo ""
  echo "⚠️  Found dependency issues!"
  echo "   Fix these before pushing or production build will FAIL."
  exit 1
else
  echo "✅ All dependencies verified"
fi
```

Usage:
```bash
chmod +x scripts/check-hotfix-deps.sh
./scripts/check-hotfix-deps.sh
```
