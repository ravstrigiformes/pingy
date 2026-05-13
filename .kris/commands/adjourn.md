# /adjourn - End-of-Session Audit

Verify all session artifacts are in order before closing. Audit-only by default — reports status without modifying anything.

---

## Quick Start

```bash
/adjourn                  # Audit current session
/adjourn --fix            # Audit + auto-remediate what's possible
```

---

## How It Works

1. **Detect context** — issue number, branch, worktree status
2. **Run scoped checks** — only artifacts related to this session's issue
3. **Report status** — clear pass/warn/fail for each check
4. **Mark incomplete sessions** — prepend `*` to session title if task not done

---

## Phase 1: Context Detection

Determine the current session's scope. All subsequent checks are scoped to this issue number.

```bash
# Get branch info
BRANCH=$(git branch --show-current 2>/dev/null)

# Parse issue number from branch name
# feature/347-escape-close-dialogs → 347
ISSUE_NUM=$(echo "$BRANCH" | grep -oE '[0-9]+' | head -1)

# Parse slug from branch name
# feature/347-escape-close-dialogs → escape-close-dialogs
SLUG=$(echo "$BRANCH" | sed "s|^[^/]*/[0-9]*-||")

# Parse branch type
BRANCH_TYPE=$(echo "$BRANCH" | cut -d'/' -f1)

# Detect worktree
WORKTREE_INFO=$(git rev-parse --git-common-dir 2>/dev/null)
if [[ "$WORKTREE_INFO" != ".git" && -d "$WORKTREE_INFO" ]]; then
  IN_WORKTREE=true
  MAIN_REPO=$(dirname "$WORKTREE_INFO")
fi

# Determine task file paths
# Task files live in backend/.kris/tasks/ (relative to git root)
GIT_ROOT=$(git rev-parse --show-toplevel)
TASKS_DIR="${GIT_ROOT}/backend/.kris/tasks"
TASK_PATTERN="*_${ISSUE_NUM}-*.md"
```

**If no issue number is detected** (e.g., `dev` branch, ad-hoc session):
- Skip issue-scoped checks (issue, PR, task file, changelog)
- Only run global checks (uncommitted changes, `public/hot`, session title)
- Report as "ad-hoc session" in output header

---

## Phase 2: Scoped Checks

Every check is either **scoped to the current issue number** or **global** (applies regardless).

### 2.1 Git — Uncommitted Changes (Global)

```bash
# Check for uncommitted changes in current repo/worktree
git status --porcelain
```

| Result | Status |
|--------|--------|
| Clean working tree | PASS |
| Uncommitted changes exist | WARN — list changed files |

### 2.2 Git — Unpushed Commits (Scoped)

```bash
# Check if local branch is ahead of remote
git log origin/${BRANCH}..HEAD --oneline 2>/dev/null
```

| Result | Status |
|--------|--------|
| No unpushed commits | PASS |
| Commits exist that aren't pushed | WARN — list commit subjects |
| No remote tracking branch | WARN — branch was never pushed |

### 2.3 GitHub Issue (Scoped)

```bash
# Verify issue exists
gh issue view ${ISSUE_NUM} --json number,title,state -q '.state' 2>/dev/null
```

| Result | Status |
|--------|--------|
| Issue exists and is OPEN | PASS |
| Issue exists and is CLOSED | PASS (with note) |
| Issue not found | FAIL |

### 2.4 Pull Request (Scoped)

```bash
# Check for PR from this branch targeting dev
gh pr list --head ${BRANCH} --base dev --json number,title,state,url -q '.[0]' 2>/dev/null
```

| Result | Status |
|--------|--------|
| PR exists — MERGED | PASS |
| PR exists — OPEN | PASS (with reminder: "awaiting review") |
| PR exists — DRAFT | WARN — "still in draft" |
| No PR found | WARN — "/fix not run or PR not created" |

### 2.5 Task File Location (Scoped)

Search **only** for this session's issue number across task folders.

```bash
# Search for task file matching this issue number
# Pattern: *_${ISSUE_NUM}-*.md
FOUND_IN=""

if ls ${TASKS_DIR}/shipped/${TASK_PATTERN} 1>/dev/null 2>&1; then
  FOUND_IN="shipped"
elif ls ${TASKS_DIR}/running/${TASK_PATTERN} 1>/dev/null 2>&1; then
  FOUND_IN="running"
elif ls ${TASKS_DIR}/pending/${TASK_PATTERN} 1>/dev/null 2>&1; then
  FOUND_IN="pending"
fi
```

| Result | Status |
|--------|--------|
| Found in `shipped/` | PASS |
| Found in `running/` | WARN — "task not marked complete" |
| Found in `pending/` | WARN — "task never started (no /do)" |
| Not found anywhere | INFO — "no task file (task tracking not used)" |

### 2.6 Changelog Entry (Scoped)

```bash
# Determine this week's changelog file
# Use Monday-Sunday boundaries
DOW=$(date +%u)
MONDAY=$(date -d "-$((DOW-1)) days" +%Y-%m-%d 2>/dev/null || date -v-$(($(date +%u)-1))d +%Y-%m-%d)
SUNDAY=$(date -d "${MONDAY} +6 days" +%Y-%m-%d 2>/dev/null || date -v+6d -j -f "%Y-%m-%d" "${MONDAY}" +%Y-%m-%d)
CHANGELOG_FILE=".kris/changelogs/${MONDAY}_to_${SUNDAY}.md"

# Check for this issue's entry
grep -q "#${ISSUE_NUM}" "${CHANGELOG_FILE}" 2>/dev/null
```

| Result | Status |
|--------|--------|
| Entry found with resolved placeholders | PASS |
| Entry found but `(pending)` placeholders remain | WARN — "PR # or commit hash not back-filled" |
| No entry found | WARN — "no changelog entry for this issue" |

### 2.7 Version in Commit (Scoped)

```bash
# Check last commit on this branch for version string
git log ${BRANCH} -1 --format=%B | grep -oE 'v[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+' | head -1
```

| Result | Status |
|--------|--------|
| Version string found | PASS — show version |
| No version string | WARN — "version missing from commit message" |

### 2.8 `public/hot` (Global)

```bash
# Check if stale hot file exists
ls public/hot 2>/dev/null
```

| Result | Status |
|--------|--------|
| File does not exist | PASS |
| File exists | WARN — "stale public/hot will break production" |

### 2.9 Code Quality — /fix Run Check (Scoped)

Infer whether `/fix` was run by checking for its artifacts:

```bash
# If in worktree, check for .worktree-complete marker
if [ "$IN_WORKTREE" = true ] && [ -f ".worktree-complete" ]; then
  FIX_RAN=true
fi

# Or check if PR exists (PR creation is the final /fix step)
if [ -n "$PR_NUMBER" ]; then
  FIX_RAN=true
fi
```

| Result | Status |
|--------|--------|
| `/fix` artifacts found | PASS |
| No `/fix` artifacts | WARN — "code quality validation may not have run" |

---

## Phase 3: Task Completion Assessment

Determine if the session's task is complete or incomplete.

**Task is COMPLETE if ALL of these are true:**
- PR exists (any state: open, merged, draft)
- All changes committed (clean working tree)
- All commits pushed

**Task is INCOMPLETE if ANY of these are true:**
- No PR exists
- Uncommitted changes remain
- Unpushed commits exist
- Task file is in `pending/` or `running/` (not `shipped/`)

---

## Phase 4: Session Title

### 4.1 Determine Title

```bash
if [ "$TASK_COMPLETE" = true ]; then
  # Complete session — normal title
  TITLE="#${ISSUE_NUM} ${SLUG}"
else
  # Incomplete session — prepend asterisk for easy find/resume
  TITLE="*#${ISSUE_NUM} ${SLUG}"
fi
```

**Why prepend `*`?** Terminal title truncation cuts from the right. `*#347 escape-close-dialogs` stays visible even when ellipsized to `*#347 esc...`. Appending would be invisible after truncation.

### 4.2 Apply Title

```bash
# Set Claude Code session name
/rename ${TITLE}

# Set terminal pane title
echo -ne "\033]0;${TITLE}\007"
```

**For ad-hoc sessions (no issue):**
- If session already has a title, keep it (prepend `*` if incomplete work exists)
- If no title set, generate from conversation context per `/title` rules

---

## Phase 5: Output

### Complete Session

```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
 SESSION ADJOURN — #347 escape-close-dialogs
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

 Git
  ✅ Issue #347 exists (open)
  ✅ Branch: feature/347-escape-close-dialogs
  ✅ All changes committed
  ✅ All commits pushed
  ✅ PR #352 → dev (open, awaiting review)

 Artifacts
  ✅ Changelog: 2026-03-23_to_2026-03-29.md
  ✅ Version: v1.4.2.0
  ✅ Task file: shipped/

 Quality
  ✅ /fix was run
  ✅ public/hot does not exist

 Session
  ✅ Session: #347 escape-close-dialogs

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
 0 warnings · 0 blockers · Safe to adjourn
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

### Incomplete Session

```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
 SESSION ADJOURN — *#347 escape-close-dialogs
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

 Git
  ✅ Issue #347 exists (open)
  ✅ Branch: feature/347-escape-close-dialogs
  ⚠️  3 uncommitted files:
       M resources/js/components/k/KDialog.vue
       M resources/js/composables/useClickOutside.ts
       A resources/css/components/dialog.css
  ✅ No unpushed commits
  ⚠️  No PR found

 Artifacts
  ⚠️  No changelog entry for #347
  ⚠️  No version in commit
  ⚠️  Task file in: running/

 Quality
  ⚠️  /fix not run
  ✅ public/hot does not exist

 Session
  ⚠️  Incomplete — title set to: *#347 escape-close-dialogs
  ℹ️  Resume with: /do (in worktree) or find session by *#347

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
 5 warnings · 0 blockers · Incomplete session
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

### Ad-hoc Session (No Issue)

```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
 SESSION ADJOURN — ad-hoc
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

 Git
  ⚠️  2 uncommitted files:
       M .claude/commands/adjourn.md
       A .claude/commands/adj.md
  ✅ No unpushed commits

 Global
  ✅ public/hot does not exist

 Session
  ⚠️  No title set — generating: *config: adjourn skill
  ℹ️  Ad-hoc session (no issue context)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
 2 warnings · 0 blockers
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

### Worktree Reminder

When running inside a worktree, append to the Session section:

```
 Session
  ✅ Session: #347 escape-close-dialogs
  ℹ️  In worktree — run /wc from main repo after PR merges
```

---

## Phase 6: Remediation (`--fix`)

When `--fix` is passed, auto-remediate **only safe, reversible actions**:

| Check | Remediation |
|-------|-------------|
| Task file in `running/` or `pending/` + PR merged | Move to `shipped/` |
| Changelog `(pending)` placeholders | Back-fill PR # and commit hash from GitHub/git |
| Session title missing or wrong | Set/update title |
| `public/hot` exists | Delete it |

**What `--fix` does NOT do:**
- Commit or push code (that's `/fix`)
- Create issues or PRs (that's `/fix` or `/issue`)
- Create changelog entries from scratch (that's `/commit` or `/fix`)
- Remove worktrees (that's `/wc`)

### Remediation Output

When `--fix` remediates something, show it inline:

```
 Artifacts
  ⚠️  Task file in: running/ → FIXED: moved to shipped/
  ⚠️  Changelog PR # was (pending) → FIXED: back-filled #352
  ✅ Version: v1.4.2.0
```

---

## Flags

### `--fix`

Audit + auto-remediate safe issues:

```bash
/adjourn --fix
```

---

## Summary Line Logic

The final summary line reflects the overall session state:

```
# Counting
WARNINGS = count of ⚠️ items
BLOCKERS = 0  # /adjourn never blocks, only warns

# Determine closing phrase
if TASK_COMPLETE and WARNINGS == 0:
  "Safe to adjourn"
elif TASK_COMPLETE and WARNINGS > 0:
  "Safe to adjourn (minor warnings)"
elif not TASK_COMPLETE:
  "Incomplete session"
```

---

## Scoping Rules (CRITICAL)

**Every check is scoped to the current session's issue number.** Do NOT check:

- Task files for other issues (even if in `running/` or `pending/`)
- Changelog entries for other issues
- PRs for other branches
- Larapass prompts for other issues
- Any artifact not directly associated with `${ISSUE_NUM}`

**The only global checks are:**
- Uncommitted changes (affects current worktree/repo)
- `public/hot` existence (affects production)

This prevents noise from unrelated in-progress work across parallel sessions.

---

## Integration with Other Skills

| Skill | Relationship |
|-------|-------------|
| `/fix` | Does the work — `/adjourn` verifies it was done |
| `/wc` | Cleans up worktrees — `/adjourn` reminds you to run it |
| `/title` | Sets session title — `/adjourn` adjusts it (adds/removes `*`) |
| `/do` | Implements task — `/adjourn` checks task file moved from `running/` |
| `/commit` | Creates commits — `/adjourn` checks changelog was created |

### Recommended Workflow Position

```
/worktree → /do → /fix → /adjourn → /wc
                    |        ↑
                    |  You are here
                    |  (verify before closing)
                    |
                    └→ /fix --dev → /adjourn → /wc
                       (PR + merge    (verify)
                        in one step)
```

---

## Error Handling

| Error | Resolution |
|-------|------------|
| `gh` not authenticated | Skip GitHub checks, warn: "GitHub CLI not authenticated — skipping issue/PR checks" |
| Not on a feature branch | Run as ad-hoc session (global checks only) |
| Network unavailable | Skip remote checks (issue, PR), run local checks only |
| No git repo | Error: "Not inside a git repository" |

---

## Examples

### After Completed /fix (Happy Path)

```
> /adjourn

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
 SESSION ADJOURN — #347 escape-close-dialogs
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

 Git
  ✅ Issue #347 (open)
  ✅ All changes committed
  ✅ All commits pushed
  ✅ PR #352 → dev (open)

 Artifacts
  ✅ Changelog entry
  ✅ Version v1.4.2.0
  ✅ Task: shipped/

 Quality
  ✅ /fix completed
  ✅ No public/hot

 Session
  ✅ #347 escape-close-dialogs

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
 0 warnings · Safe to adjourn
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

### Mid-Work Pause

```
> /adjourn

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
 SESSION ADJOURN — *#347 escape-close-dialogs
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

 Git
  ✅ Issue #347 (open)
  ⚠️  3 uncommitted files
  ⚠️  No PR found

 Artifacts
  ⚠️  No changelog entry
  ⚠️  Task: running/

 Quality
  ⚠️  /fix not run
  ✅ No public/hot

 Session
  ⚠️  Incomplete → *#347 escape-close-dialogs
  ℹ️  In worktree — resume later with /do

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
 5 warnings · Incomplete session
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

### With --fix Remediation

```
> /adjourn --fix

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
 SESSION ADJOURN — #348 scrollable-dialogs
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

 Git
  ✅ Issue #348 (closed)
  ✅ All changes committed
  ✅ All commits pushed
  ✅ PR #349 → dev (merged)

 Artifacts
  ⚠️  Changelog PR # was (pending) → FIXED: #349
  ✅ Version v1.4.1.0
  ⚠️  Task in running/ → FIXED: moved to shipped/

 Quality
  ✅ /fix completed
  ✅ No public/hot

 Session
  ✅ #348 scrollable-dialogs

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
 2 warnings (2 fixed) · Safe to adjourn
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```
