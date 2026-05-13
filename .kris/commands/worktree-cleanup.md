# /worktree-cleanup - Clean Up Completed Worktrees

Remove worktrees that have completed work (PR merged or ready for cleanup).

> **Run this from the main repo** (not from inside a worktree)

---

## Quick Start

```bash
/worktree-cleanup                    # Scan all, clean up completed
/worktree-cleanup 224                # Clean up worktree for issue #224
/worktree-cleanup --all              # Clean all completed without prompting
/worktree-cleanup --list             # Just list status, don't remove
```

---

## How It Works

### Completion Markers

When `/fix` completes successfully inside a worktree, it creates a marker file:

```
.worktrees/feature-224-slug/.worktree-complete
```

**Marker contents:**
```json
{
  "issue": 224,
  "pr": 225,
  "branch": "feature/224-doc-register-dialog-dismiss",
  "completed": "2026-03-09T14:30:00Z",
  "commit": "abc1234"
}
```

### Cleanup Process

1. **Scan** `.worktrees/` for directories
2. **Check** each for `.worktree-complete` marker
3. **Verify** PR status (merged = safe to remove)
4. **Safety net** — verify version update and changelog entry exist; remediate if missing
5. **Move task** to shipped (if not already)
6. **Remove** worktree and optionally delete branch

---

## Usage Modes

### Interactive (Default)

```bash
/worktree-cleanup
```

**Output:**
```
📂 WORKTREE STATUS
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

✅ READY TO CLEAN (2):

   #224 feature-224-doc-register-dialog-dismiss
        PR #225: MERGED
        Completed: 2026-03-09 14:30

   #220 bugfix-220-fix-session-timeout
        PR #221: MERGED
        Completed: 2026-03-09 10:15

⏳ IN PROGRESS (1):

   #230 feature-230-add-user-avatars
        No completion marker
        (Still being worked on)

⚠️  STALE (1):

   #215 refactor-215-extract-auth
        PR #216: CLOSED (not merged)
        Completed: 2026-03-07 09:00
        → May need manual review

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Clean up completed worktrees?

  [Y] Remove all completed (2 worktrees)
  [s] Select which to remove
  [n] Cancel

Your choice [Y]: _
```

### By Issue Number

```bash
/worktree-cleanup 224
```

**Output:**
```
📂 WORKTREE: #224
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Path:      ../.worktrees/feature-224-doc-register-dialog-dismiss
Branch:    feature/224-doc-register-dialog-dismiss
Issue:     #224 [Feature]: Enhance document registration dialog
PR:        #225 - MERGED ✅
Completed: 2026-03-09 14:30

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Remove this worktree?

  [Y] Yes, remove worktree (recommended)
  [b] Also delete the branch
  [n] Cancel

Your choice [Y]: _
```

### Auto Mode

```bash
/worktree-cleanup --all
```

Removes all worktrees with `.worktree-complete` markers where PR is merged.
No prompts.

### List Only

```bash
/worktree-cleanup --list
```

Shows status without removing anything.

---

## Phase 1: Scan Worktrees

```bash
# Get all worktree directories
WORKTREE_DIR="../.worktrees"

# List all worktrees
for dir in "$WORKTREE_DIR"/*/; do
  WORKTREE_NAME=$(basename "$dir")

  # Parse issue number from name (e.g., feature-224-slug → 224)
  ISSUE_NUM=$(echo "$WORKTREE_NAME" | grep -oE '[0-9]+' | head -1)

  # Check for completion marker
  if [ -f "$dir/.worktree-complete" ]; then
    MARKER=$(cat "$dir/.worktree-complete")
    PR_NUM=$(echo "$MARKER" | jq -r '.pr')
    COMPLETED=$(echo "$MARKER" | jq -r '.completed')
    STATUS="complete"
  else
    STATUS="in_progress"
  fi

  # Check PR status if we have a PR number
  if [ -n "$PR_NUM" ]; then
    PR_STATE=$(gh pr view "$PR_NUM" --json state -q '.state')
  fi
done
```

---

## Phase 2: Categorize

| Category | Criteria | Action |
|----------|----------|--------|
| **Ready to clean** | Has marker + PR merged | Safe to remove |
| **In progress** | No marker | Skip (work ongoing) |
| **Stale** | Has marker + PR closed (not merged) | Warn, ask user |
| **Orphaned** | Has marker + PR not found | Warn, ask user |

---

## Phase 3: Remove

### 3.1 Move Task to Shipped (Safety Net)

This is a **safety net** - `/fix` should have already moved the task, but we check again in case:
- `/fix` was skipped or failed
- Task file was manually created
- Edge cases

```bash
# Find task file (format: YYYY-MM-DD_<issue#>-<slug>.md)
TASKS_DIR=".kris/tasks"
TASK_PATTERN="*_${ISSUE_NUM}-*.md"

# Ensure shipped directory exists
mkdir -p "${TASKS_DIR}/shipped"

# Check running first (expected location after /do)
TASK_FILE=$(find "${TASKS_DIR}/running" -maxdepth 1 -name "$TASK_PATTERN" 2>/dev/null | head -1)

if [ -n "$TASK_FILE" ] && [ -f "$TASK_FILE" ]; then
  TASK_BASENAME=$(basename "$TASK_FILE")
  mv "$TASK_FILE" "${TASKS_DIR}/shipped/"
  echo "📋 Task moved: running → shipped"
  echo "   File: $TASK_BASENAME"
else
  # Check pending (if /do was skipped)
  TASK_FILE=$(find "${TASKS_DIR}/pending" -maxdepth 1 -name "$TASK_PATTERN" 2>/dev/null | head -1)

  if [ -n "$TASK_FILE" ] && [ -f "$TASK_FILE" ]; then
    TASK_BASENAME=$(basename "$TASK_FILE")
    mv "$TASK_FILE" "${TASKS_DIR}/shipped/"
    echo "📋 Task moved: pending → shipped (skipped running)"
    echo "   File: $TASK_BASENAME"
  elif ls "${TASKS_DIR}/shipped/"$TASK_PATTERN 1>/dev/null 2>&1; then
    echo "📋 Task already in shipped/ (moved by /fix)"
  else
    echo "ℹ️  No task file found for issue #${ISSUE_NUM}"
    echo "   Task tracking was not used for this worktree"
  fi
fi
```

### 3.2 Version & Changelog Safety Net (CRITICAL)

**Before removing the worktree, verify that `/fix` completed the version and changelog steps.** If either is missing, remediate here.

This is a **safety net** — `/fix` should have already done these, but we double-check:

#### 3.2.1 Check Version in Commit Message

```bash
# Get the commit hash from the marker (or from the branch tip)
COMMIT_HASH=$(echo "$MARKER" | jq -r '.commit')
BRANCH_NAME=$(echo "$MARKER" | jq -r '.branch')

# Check if commit message contains a version string (vW.X.Y.Z)
COMMIT_VERSION=$(git log "$BRANCH_NAME" -1 --format=%B | grep -oE 'v[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+' | head -1)

if [ -z "$COMMIT_VERSION" ]; then
  echo "⚠️  VERSION MISSING from commit message"
  echo "   /fix did not include a version. Remediating..."

  # Determine version (same logic as /fix Phase 7.1)
  # Search origin/dev commit messages for latest version, then auto-increment
  LAST_VERSION=$(git log origin/dev --format=%B -20 | grep -oE 'v[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+' | head -1)
  # If not found, fall back to tags
  if [ -z "$LAST_VERSION" ]; then
    LAST_TAG=$(git describe --tags --abbrev=0 2>/dev/null || echo "v0.0.0")
    LAST_VERSION="${LAST_TAG}.0"
  fi

  # Parse and increment based on branch type (feature → minor, bugfix → patch, etc.)
  # ... (same version increment logic as /fix Phase 7.1)

  COMMIT_VERSION="$NEXT_VERSION"
  echo "   Determined version: $COMMIT_VERSION"
  # Note: Cannot amend a pushed commit. Version is recorded in changelog instead.
else
  echo "✅ Version found in commit: $COMMIT_VERSION"
fi
```

#### 3.2.2 Check Changelog Entry

```bash
# Determine which changelog file should contain this entry
# Use the commit date to find the right week
COMMIT_DATE=$(git log "$BRANCH_NAME" -1 --format=%ci | cut -d' ' -f1)
DOW=$(date -d "$COMMIT_DATE" +%u)
MONDAY=$(date -d "$COMMIT_DATE -$((DOW-1)) days" +%Y-%m-%d)
SUNDAY=$(date -d "$MONDAY +6 days" +%Y-%m-%d)
CHANGELOG_FILE=".kris/changelogs/${MONDAY}_to_${SUNDAY}.md"

# Check if an entry for this issue exists in the changelog
if [ -f "$CHANGELOG_FILE" ] && grep -q "#${ISSUE_NUM}" "$CHANGELOG_FILE"; then
  echo "✅ Changelog entry found for #${ISSUE_NUM}"
else
  echo "⚠️  CHANGELOG MISSING for #${ISSUE_NUM}"
  echo "   /fix did not update the changelog. Remediating..."

  # Get PR number from marker
  PR_NUM=$(echo "$MARKER" | jq -r '.pr')
  COMMIT_SHORT=$(echo "$COMMIT_HASH" | cut -c1-8)

  # Get commit title for the entry
  COMMIT_TITLE=$(git log "$BRANCH_NAME" -1 --format=%s)

  # Determine area from changed files
  CHANGED_FILES=$(git diff origin/dev..."$BRANCH_NAME" --name-only)
  # ... (same area detection logic as /fix Phase 10)

  # Create changelog file from template if it doesn't exist
  # Add entry under the appropriate day
  # Update summary statistics (total commits, issues resolved, features/bugs, by area)

  echo "   Added changelog entry to $CHANGELOG_FILE"
  echo "   Entry: $COMMIT_TITLE"
fi
```

#### 3.2.3 Back-fill Changelog Placeholders

> **Context:** `/fix` Phase 7.4 creates changelog entries with `(pending)` placeholders for commit hash and PR #.
> Phase 7.6 back-fills the commit hash (pre-push amend) and Phase 7.9 back-fills the PR # (post-push amend).
> If either back-fill step was skipped or failed, this safety net catches it.

```bash
# Find the changelog entry for this issue
if [ -f "$CHANGELOG_FILE" ] && grep -q "#${ISSUE_NUM}" "$CHANGELOG_FILE"; then

  # Back-fill commit hash if still placeholder
  if grep -q "(pending)" "$CHANGELOG_FILE" && grep -B1 "#${ISSUE_NUM}" "$CHANGELOG_FILE" | grep -q "(pending)"; then
    COMMIT_HASH=$(echo "$MARKER" | jq -r '.commit')
    PR_NUM=$(echo "$MARKER" | jq -r '.pr')

    # Replace commit placeholder
    # Use the marker's commit hash (most reliable source)
    sed -i "/#${ISSUE_NUM}/,/^####/{s/\*\*Commit\*\*: \`(pending)\`/**Commit**: \`${COMMIT_HASH}\`/}" "$CHANGELOG_FILE"

    # Replace PR placeholder
    if [ -n "$PR_NUM" ] && [ "$PR_NUM" != "null" ]; then
      sed -i "/#${ISSUE_NUM}/,/^####/{s/\*\*PR\*\*: (pending)/**PR**: #${PR_NUM}/}" "$CHANGELOG_FILE"
    fi

    echo "📓 Changelog back-filled for #${ISSUE_NUM}"
    echo "   Commit: $COMMIT_HASH, PR: #$PR_NUM"
  else
    echo "✅ Changelog placeholders already resolved for #${ISSUE_NUM}"
  fi
fi
```

#### 3.2.4 Safety Net Summary

```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
🔍 PRE-CLEANUP VERIFICATION: #${ISSUE_NUM}
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

✅ Task file: shipped/ (moved by /fix)
✅ Version: v0.3.1.0 (in commit message)
✅ Changelog: 2026-03-17_to_2026-03-23.md (entry exists)
✅ Placeholders: all resolved (Commit=abc1234, PR=#43)

OR

✅ Task file: shipped/ (moved by /fix)
⚠️  Version: MISSING → remediated (v0.3.1.0 added to changelog)
⚠️  Changelog: MISSING → remediated (entry added)
⚠️  Placeholders: back-filled (Commit=(pending)→abc1234, PR=(pending)→#43)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

**Key points:**
- Version and changelog checks run BEFORE worktree removal (data still accessible)
- If version is missing from commit, it's recorded in the changelog entry instead (can't amend pushed commits)
- If changelog entry is missing, it's created with full details (commit hash, issue #, PR #, area, files, changes)
- The changelog entry format follows `/fix` Phase 7.4.2 exactly, including both issue # and PR # for traceability
- Placeholder back-fill uses the `.worktree-complete` marker as the source of truth for commit hash and PR #
- Summary statistics in the changelog header are updated to include the remediated entry

### 3.3 Remove Worktree

**Note:** Each worktree has its own isolated `vendor/` and `node_modules/` (not symlinked). Removal is safe and won't affect the main repo's dependencies.

```bash
# Remove worktree (safe — deps are isolated, not symlinked)
git worktree remove "$WORKTREE_PATH"

# Optionally delete branch
if [ "$DELETE_BRANCH" = true ]; then
  git branch -D "$BRANCH_NAME"
  git push origin --delete "$BRANCH_NAME"
fi

# Prune stale entries
git worktree prune
```

---

## Flags

### `--all` or `-a`

Remove all completed worktrees without prompting:

```bash
/worktree-cleanup --all
```

Only removes worktrees where:
- `.worktree-complete` marker exists
- PR is merged

### `--list` or `-l`

List status without removing:

```bash
/worktree-cleanup --list
```

### `--force` or `-f`

Remove even if PR not merged (use with caution):

```bash
/worktree-cleanup 224 --force
```

### `--branch` or `-b`

Also delete the remote branch after removing worktree:

```bash
/worktree-cleanup 224 --branch
```

---

## Integration with /fix

When `/fix` completes inside a worktree, it:

1. Creates `.worktree-complete` marker file
2. Outputs cleanup one-liner:

```
✅ /fix COMPLETE

📌 CLEANUP (copy-paste after exiting):

   exit && cd /path/to/backend && /worktree-cleanup 224

   Or manually:
   git worktree remove ../.worktrees/feature-224-slug
```

---

## Marker File Format

**Location:** `{worktree}/.worktree-complete`

**Contents:**
```json
{
  "issue": 224,
  "pr": 225,
  "branch": "feature/224-doc-register-dialog-dismiss",
  "commit": "abc1234f",
  "completed": "2026-03-09T14:30:00Z",
  "title": "[Feature]: Enhance document registration dialog"
}
```

**Created by:** `/fix` skill when running inside a worktree

---

## Examples

### Clean All Merged

```
> /worktree-cleanup

📂 Found 3 worktrees, 2 ready to clean

✅ #224 feature-224-doc-register (PR #225 MERGED)
✅ #220 bugfix-220-session-timeout (PR #221 MERGED)
⏳ #230 feature-230-avatars (in progress)

Remove 2 completed worktrees? [Y/n]: Y

Removing feature-224-doc-register... ✓
Removing bugfix-220-session-timeout... ✓

✅ Cleaned up 2 worktrees
```

### Clean Specific Issue

```
> /worktree-cleanup 224

📂 #224: feature-224-doc-register-dialog-dismiss
   PR #225: MERGED ✅

Remove? [Y/n]: Y

✅ Removed worktree for #224
```

### List Only

```
> /worktree-cleanup --list

📂 WORKTREE STATUS
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

✅ Ready:  #224, #220
⏳ Active: #230
⚠️  Stale:  #215

Total: 4 worktrees (2 ready to clean)
```

---

## Error Handling

| Error | Resolution |
|-------|------------|
| Not in main repo | "Run this from the main repo, not inside a worktree" |
| Issue # not found | "No worktree found for issue #224" |
| PR still open | "PR #225 is still open. Use --force to remove anyway" |
| Worktree has changes | "Worktree has uncommitted changes. Commit or stash first" |

---

## Safety Checks

Before removing, verify:

1. **Not currently inside** the worktree being removed
2. **No uncommitted changes** in the worktree
3. **PR is merged** (unless --force)
4. **Branch exists** (warn if already deleted)
5. **Version recorded** — commit message or changelog has version string (remediate if missing)
6. **Changelog entry exists** — weekly changelog has entry for this issue + PR (remediate if missing)

---

## Quick Reference

```bash
# From main repo:

/worktree-cleanup              # Interactive cleanup
/worktree-cleanup 224          # Clean specific issue
/worktree-cleanup --all        # Auto-clean all merged
/worktree-cleanup --list       # Just show status

# Manual git commands:
git worktree list              # Show all worktrees
git worktree remove <path>     # Remove specific worktree
git worktree prune             # Clean stale entries
```
