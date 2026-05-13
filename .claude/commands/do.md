# /do - Execute Worktree Task

Automatically load task context from the GitHub issue and start implementation.

> **Smart dispatch:** runs inline when invoked from inside a worktree; spawns an isolated Windows Terminal tab when invoked from the main repo with an active worktree session; for genuine quick-fixes on main repo, use `/do --here` to force inline.

---

## Quick Start

```bash
/do                    # Load context and implement (auto-detects target)
/do --plan             # Load context, plan first before implementing
/do --info             # Just show task info, don't implement
/do --here             # Force inline run in cwd (skip worktree dispatch)
/do --wt <issue#>      # Force tab spawn into the matching worktree
```

---

## How It Works

### 1. Detect Context

```bash
# Get current branch
BRANCH=$(git branch --show-current)
# Example: feature/224-doc-register-dialog-dismiss

# Parse issue number from branch
ISSUE_NUM=$(echo "$BRANCH" | grep -oE '[0-9]+' | head -1)
# Example: 224

# Detect task type from branch prefix
TASK_TYPE=$(echo "$BRANCH" | cut -d'/' -f1)
# Example: feature, bugfix, refactor, docs, chore
```

### 2. Load Task Context

**Source priority:** session `brief.md` → `TASK.md` → GitHub issue.

When `/wt` creates a worktree, it also writes a session workspace at `<main-backend>/.kris/wt-sessions/<issue#>-<slug>/brief.md` that contains the full task context (problem, solution, criteria, files in scope, scope boundaries, adopted suggestions, etc.). `/do` prefers this richer source when it exists — falls back to `TASK.md`, then to the GitHub issue, so it stays compatible with older worktrees.

```bash
# Locate main backend so we can resolve the session dir
GIT_ROOT=$(git rev-parse --show-toplevel)
MAIN_BACKEND="${GIT_ROOT}/backend"
BRANCH=$(git branch --show-current)
ISSUE_NUM=$(echo "$BRANCH" | grep -oE '/[0-9]+' | tr -d '/')
SLUG=$(echo "$BRANCH" | sed "s|^[^/]*/[0-9]*-||")
SESSION_BRIEF="${GIT_ROOT}/.kris/wt-sessions/${ISSUE_NUM}-${SLUG}/brief.md"

if [ -f "$SESSION_BRIEF" ]; then
  TASK_SOURCE="session-brief"
  # Read brief.md — it contains Problem, Proposed Solution, Acceptance Criteria,
  # Files in Scope, Patterns to Follow, Scope Boundaries, and more.
  # Parse sections as needed for the display (Phase 3).
elif [ -f "TASK.md" ]; then
  TASK_SOURCE="local-task"
  TITLE=$(grep -m1 "^# Task:" TASK.md | sed 's/^# Task: //')
  PROBLEM=$(extract_section "TASK.md" "Problem / Use Case")
  SOLUTION=$(extract_section "TASK.md" "Proposed Solution")
  CRITERIA=$(extract_section "TASK.md" "Acceptance Criteria")
else
  TASK_SOURCE="github"
  gh issue view "$ISSUE_NUM" --json title,body,labels,state
fi
```

**Why prefer `brief.md` over `TASK.md`?**

- **Richer.** Brief contains Files in Scope, Patterns to Follow, Scope Boundaries, adopted suggestions, and clarifications — things `TASK.md` doesn't carry.
- **Same source as the executor agent.** Using the same file the `/wt` executor used keeps `/do`'s manual work aligned with what autonomous agents would produce.
- **Unchanged after work starts.** `TASK.md` gets checkmarks flipped as criteria are met; `brief.md` stays stable so you always see the original scope.

**Fallback: `TASK.md`** — used when no session brief exists (older worktrees, `--setup-only` without session setup, manually-created worktrees).

**Fallback: GitHub Issue** — used when neither file exists. Always has the canonical issue data.

### 3. Load Context and Implement

Present the task context, then start implementation based on:
- The problem/use case
- The proposed solution
- The acceptance criteria as a checklist

---

## Output Format

```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
📋 TASK LOADED
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Source:   session brief           ← or "TASK.md (local)" / "GitHub Issue #224"
Worktree: feature-224-doc-register-dialog-dismiss
Branch:   feature/224-doc-register-dialog-dismiss
Issue:    #224
Type:     Feature
Size:     Small

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

## [Feature]: Enhance document registration dialog
             with click-outside dismiss animation

### Problem / Use Case

Users need the registration dialog to dismiss smoothly when clicking
outside, providing clear visual feedback that the action was cancelled.

### Proposed Solution

Add click-outside detection to the dialog component with a smooth
fade-out animation. The dialog should:
- Detect clicks outside the dialog area
- Play a subtle dismiss animation
- Return focus to the previous element

### Acceptance Criteria

- [ ] Dialog dismisses on click-outside
- [ ] Smooth fade-out animation (200-300ms)
- [ ] Focus returns to trigger element
- [ ] ESC key also dismisses with same animation
- [ ] No dismiss when clicking inside dialog

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

🚀 STARTING IMPLEMENTATION...

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

Then Claude proceeds to:
1. Explore the codebase to understand existing patterns
2. Identify files to modify
3. Implement the solution
4. Check off acceptance criteria as completed

---

## Flags

### `--plan`

Enter plan mode before implementing:

```bash
/do --plan
```

- Loads task context
- Enters plan mode to design implementation approach
- Presents plan for approval before writing code
- Good for larger or complex tasks

### `--info`

Just show task info without implementing:

```bash
/do --info
```

- Fetches and displays issue details
- Does NOT start implementation
- Useful to review what needs to be done

### `--refresh`

Re-fetch issue details from GitHub and update TASK.md:

```bash
/do --refresh
```

### `--sync`

Sync TASK.md progress back to GitHub issue:

```bash
/do --sync
```

- Updates acceptance criteria checkboxes on GitHub
- Adds implementation notes as issue comment
- Useful before creating PR

---

## Phase 0: Smart Dispatch (CRITICAL — runs FIRST)

> *Decide whether to run inline (cwd is correct) or spawn an isolated Windows Terminal tab pinned to a worktree. Same dispatch shape as `/fix` Phase 0.*

### 0.1 Detect cwd + intent

Evaluate signals in order:

```
SIGNAL A — Inside a worktree:
  GIT_COMMON_DIR=$(git rev-parse --git-common-dir 2>/dev/null)
  IF "$GIT_COMMON_DIR" != ".git" → RUN_INLINE (this is already the right cwd)

SIGNAL B — Explicit override flag:
  /do --here                  → RUN_INLINE in main repo (true quick fix)
  /do --wt <issue#>           → SPAWN_TAB targeting that worktree
  /do --setup-only            → preserved from /wt invocation; respect it

SIGNAL C — Active worktree session:
  Resolve TARGET_ISSUE from prompt args or recent context.
  Search .worktrees/ for *-${TARGET_ISSUE}-*:
    - Exactly one match: SPAWN_TAB
    - Multiple matches: ASK USER which
    - Zero matches: continue to D

SIGNAL D — Conversation/file context infers a session:
  Within the last hour, did this session run /wt or /worktree?
  Does any .kris/wt-sessions/<N>-<slug>/ have brief.md AND mtime < 1h
    AND no matching shipped/ task file?
  IF yes with single inferred TARGET_ISSUE → SPAWN_TAB

SIGNAL E — No active session AND heavy-task signal:
  Heavy-task keywords in prompt: "refactor", "rewrite", "implement", "build",
    "design", "architecture", "migrate", "audit", multi-file scope, >2 paragraphs
  IF heavy → RECOMMEND /wt (don't auto-spawn — /wt does its own setup)
  Format: "This looks like a /wt-class task. Run /wt instead? [Y/n/h]"
    Y = run /wt with the same prompt
    n = continue with /do inline (user accepts main repo as target)
    h = same as --here, force inline

SIGNAL F — Quick fix on main repo:
  Bare /do from main repo, no session match, prompt is short / focused →
  RUN_INLINE in main repo. Implementation lands in main repo (no PR step;
  user runs /commit or /fix afterward).
```

### 0.2 SPAWN_TAB execution

When dispatch resolves to SPAWN_TAB:

```bash
SESSION_ID="${TARGET_ISSUE}-${SLUG}"
GIT_ROOT=$(git rev-parse --show-toplevel)
MAIN_BACKEND="${GIT_ROOT}/backend"
WORKTREE_BACKEND="${GIT_ROOT}/.worktrees/${WORKTREE_NAME}/backend"
REPORT_FILE="${GIT_ROOT}/.kris/wt-sessions/${SESSION_ID}/spawn-report.json"

PROMPT=$(cat <<EOF
You are the /do delegate for session ${SESSION_ID}.

Read these files first (absolute paths):
  1. ${GIT_ROOT}/.claude/commands/do.md
  2. ${GIT_ROOT}/.kris/wt-sessions/${SESSION_ID}/brief.md  (if present)
  3. ${GIT_ROOT}/.kris/wt-sessions/${SESSION_ID}/final-plan.md  (if present)
  4. ./TASK.md
  5. ./.preview-state  (if present)

Then execute /do from Phase 1 onward (Signal A short-circuits to inline).
Implement the acceptance criteria, update TASK.md as you complete each one.
EOF
)

  --issue "${TARGET_ISSUE}" \
  --slug "${SLUG}" \
  --worktree "${WORKTREE_PATH}" \
  --prompt "${PROMPT}" \
  --report-file "${REPORT_FILE}"
```

After spawning: orchestrator prints tab title + report path, then exits this skill (does not poll by default — user can switch to the tab to watch, or invoke `/do` again later to read the report).

### 0.3 RUN_INLINE on main repo (true quick fix)

When SIGNAL F resolves: skip Phase 1.1's worktree assertion. Treat main repo as the implementation target. After implementation, the user runs `/commit` (small change) or `/fix` (issue-tracked) to finalize. `/do` itself does not commit when running on main repo.

---

## Phase 1: Context Detection

### 1.1 Verify Worktree (skipped if RUN_INLINE on main repo)

```bash
# Check if we're in a worktree
WORKTREE_INFO=$(git rev-parse --git-common-dir 2>/dev/null)

if [[ "$WORKTREE_INFO" == ".git" ]]; then
  if [[ "$DO_HERE" != "true" ]]; then
    echo "⚠️  Not in a worktree. Phase 0 dispatch should have caught this."
    echo "   Pass --here to run inline on main repo, or --wt <issue#> to dispatch."
    exit 1
  fi
  # --here mode: continue, treat main repo as target
fi
```

### 1.2 Parse Branch Info

```bash
# Get branch name
BRANCH=$(git branch --show-current)

# Parse components
# feature/224-doc-register-dialog-dismiss
#   ↓      ↓   ↓
# TYPE   NUM  SLUG

TASK_TYPE=$(echo "$BRANCH" | cut -d'/' -f1)
ISSUE_NUM=$(echo "$BRANCH" | grep -oE '/[0-9]+' | tr -d '/')
SLUG=$(echo "$BRANCH" | sed "s|^[^/]*/[0-9]*-||")
```

### 1.3 Check for Existing Marker

```bash
# Check if .worktree-complete exists (task already done)
if [ -f ".worktree-complete" ]; then
  echo "⚠️  This worktree is marked complete."
  echo "   Run /fix if you haven't pushed, or /worktree-cleanup to remove."
  exit 1
fi
```

### 1.4 Move Task to Running

```bash
# Find task file in pending (format: YYYY-MM-DD_<issue#>-<slug>.md)
# Task files live in .kris/tasks/, NOT at the git root
GIT_ROOT=$(git rev-parse --show-toplevel)
TASKS_DIR="${GIT_ROOT}/.kris/tasks"
TASK_PATTERN="*_${ISSUE_NUM}-*.md"

# Ensure task directories exist
mkdir -p "${TASKS_DIR}/pending" "${TASKS_DIR}/running" "${TASKS_DIR}/shipped"

# Find and move task file
TASK_FILE=$(find "${TASKS_DIR}/pending" -maxdepth 1 -name "$TASK_PATTERN" 2>/dev/null | head -1)

if [ -n "$TASK_FILE" ] && [ -f "$TASK_FILE" ]; then
  TASK_BASENAME=$(basename "$TASK_FILE")
  mv "$TASK_FILE" "${TASKS_DIR}/running/"
  echo "📋 Task moved: pending → running"
  echo "   File: $TASK_BASENAME"
elif [ -f "${TASKS_DIR}/running/${TASK_PATTERN}" ]; then
  # Already in running - task was resumed
  echo "📋 Task already in running/ (resuming work)"
elif [ -f "${TASKS_DIR}/shipped/${TASK_PATTERN}" ]; then
  # Already shipped - warn user
  echo "⚠️  Task already in shipped/ - work may have been completed"
  echo "   Check if this is intentional before continuing"
else
  # No task file found - create one or warn
  echo "ℹ️  No task file found for issue #${ISSUE_NUM}"
  echo "   Task tracking will be skipped (task file is optional)"
fi
```

This tracks the task status centrally:
```
.kris/tasks/
├── pending/     ← Task was here
├── running/     ← Task moves here when /do starts
└── shipped/     ← Task moves here after /fix completes
```

---

## Phase 2: Fetch Issue

### 2.1 Get Issue Data

```bash
# Fetch full issue details
ISSUE_JSON=$(gh issue view $ISSUE_NUM --json number,title,body,labels,state,assignees,createdAt)

# Parse fields
TITLE=$(echo "$ISSUE_JSON" | jq -r '.title')
BODY=$(echo "$ISSUE_JSON" | jq -r '.body')
STATE=$(echo "$ISSUE_JSON" | jq -r '.state')
LABELS=$(echo "$ISSUE_JSON" | jq -r '.labels[].name' | tr '\n' ', ' | sed 's/,$//')
```

### 2.2 Parse Issue Sections

Extract structured sections from the issue body:

```bash
# For Features:
PROBLEM=$(extract_section "$BODY" "Problem / Use Case")
SOLUTION=$(extract_section "$BODY" "Proposed Solution")
CRITERIA=$(extract_section "$BODY" "Acceptance Criteria")
SIZE=$(extract_section "$BODY" "Estimated Size")

# For Bugs:
DESCRIPTION=$(extract_section "$BODY" "Description")
STEPS=$(extract_section "$BODY" "Steps to Reproduce")
EXPECTED=$(extract_section "$BODY" "Expected Behavior")
ACTUAL=$(extract_section "$BODY" "Actual Behavior")
SEVERITY=$(extract_section "$BODY" "Severity")

# For Tech/Chore:
CONTEXT=$(extract_section "$BODY" "Context / Motivation")
CHANGES=$(extract_section "$BODY" "Planned Changes")
RISKS=$(extract_section "$BODY" "Risks / Notes")
```

### 2.3 Detect Issue Type

```bash
# From title prefix
if [[ "$TITLE" == "[Bug]:"* ]]; then
  ISSUE_TYPE="bug"
elif [[ "$TITLE" == "[Feature]:"* ]]; then
  ISSUE_TYPE="feature"
elif [[ "$TITLE" == "[Tech]:"* ]] || [[ "$TITLE" == "[Chore]:"* ]]; then
  ISSUE_TYPE="tech"
else
  # Infer from branch type
  ISSUE_TYPE="$TASK_TYPE"
fi
```

---

## Phase 3: Present Task

### 3.1 Display Context

Show the parsed issue in a clear format:

```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
📋 TASK LOADED
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Worktree: {WORKTREE_NAME}
Branch:   {BRANCH}
Issue:    #{ISSUE_NUM} ({STATE})
Type:     {ISSUE_TYPE}
Labels:   {LABELS}
Size:     {SIZE}

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

## {TITLE}

### {Problem Section Header}

{PROBLEM_OR_DESCRIPTION}

### {Solution Section Header}

{SOLUTION_OR_CHANGES}

### Acceptance Criteria

{CRITERIA as checklist}

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

### 3.2 Set Session & Terminal Title

After loading task context, set the session name and pane title:

```bash
TITLE="#${ISSUE_NUM} ${SLUG}"
/rename $TITLE
echo -ne "\033]0;${TITLE}\007"
```

### 3.3 Confirm Implementation

```
Ready to implement?

  [Y] Yes, start implementing (recommended)
  [p] Plan first (enter plan mode)
  [i] Just info (don't implement)

Your choice [Y]: _
```

**With `--plan` flag:** Skip confirmation, go directly to plan mode.
**With `--info` flag:** Skip confirmation, just display and exit.

---

## Phase 4: Implementation

### 4.1 Create Internal Task List

Convert acceptance criteria to internal tasks:

```javascript
// Parse acceptance criteria checkboxes
const criteria = parseCriteria(CRITERIA);
// [
//   { text: "Dialog dismisses on click-outside", done: false },
//   { text: "Smooth fade-out animation", done: false },
//   ...
// ]

// Create task list for tracking
TaskCreate({
  subject: "Dialog dismisses on click-outside",
  description: "Implement click-outside detection for dialog",
  activeForm: "Implementing click-outside detection"
});
```

### 4.2 Explore Codebase

Before implementing, understand the existing code:

```
🔍 EXPLORING CODEBASE...

Looking for:
  - Existing dialog components
  - Click-outside patterns in use
  - Animation utilities
  - Related components

Found:
  - resources/js/components/k/KDialog.vue
  - resources/js/composables/useClickOutside.ts
  - resources/css/animations.css
```

### 4.3 Implement Solution

Work through each acceptance criterion:

```
📝 IMPLEMENTING...

[1/5] Dialog dismisses on click-outside
      → Modifying: KDialog.vue
      → Using: useClickOutside composable
      ✅ Done

[2/5] Smooth fade-out animation
      → Adding: CSS transition
      → Duration: 250ms ease-out
      ✅ Done

[3/5] Focus returns to trigger element
      ...
```

### 4.4 Update TASK.md

As work progresses, update the local TASK.md file:

```bash
# Check off completed criteria
sed -i 's/- \[ \] Dialog dismisses on click-outside/- [x] Dialog dismisses on click-outside/' TASK.md

# Add to Files Modified section
sed -i '/## Files Modified/a - resources/js/components/k/KDialog.vue' TASK.md

# Add implementation notes
sed -i '/## Implementation Notes/a - Used existing useClickOutside composable' TASK.md
```

**TASK.md tracks:**
- ✅ Completed acceptance criteria (checked off)
- 📁 Files modified during implementation
- 📝 Implementation notes and decisions
- 🧪 Testing instructions

### 4.5 Update GitHub Issue (Optional)

Optionally sync criteria back to GitHub:

```bash
# Update acceptance criteria checkboxes on GitHub
gh issue edit $ISSUE_NUM --body "$UPDATED_BODY"
```

---

## Phase 5: Completion

### 5.1 Summary

```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
✅ IMPLEMENTATION COMPLETE
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Issue: #224 [Feature]: Enhance document registration dialog

Acceptance Criteria:
  ✅ Dialog dismisses on click-outside
  ✅ Smooth fade-out animation (200-300ms)
  ✅ Focus returns to trigger element
  ✅ ESC key also dismisses with same animation
  ✅ No dismiss when clicking inside dialog

Files Modified:
  • resources/js/components/k/KDialog.vue
  • resources/js/composables/useClickOutside.ts
  • resources/css/components/dialog.css

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

📌 NEXT STEPS:

   1. Test the changes locally
   2. Run /compact (frees the exploration transcript — diff is on disk)
   3. Run /fix to validate, commit, and push
   4. After PR merged, run /worktree-cleanup 224

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

> **Why `/compact` before `/fix`:** Phase 4 implementation generates heavy Grep/Read/Edit tool history. Once the diff is committed to the worktree, that transcript is dead weight — the diff itself is the source of truth `/fix` operates on. Compacting here preserves the task summary while dropping the exploration noise. Skip if the user is ending the session anyway.

---

## Error Handling

| Error | Resolution |
|-------|------------|
| Not in worktree | "Run /worktree first to set up a task" |
| No issue number in branch | "Could not detect issue # from branch name" |
| Issue not found | "Issue #X not found. Check if it exists." |
| Issue closed | "Issue #X is closed. Create new issue or reopen." |
| Already complete | "Worktree marked complete. Run /fix or /worktree-cleanup" |

---

## Examples

### Feature Implementation

```
> /do

📋 TASK LOADED
   #224 [Feature]: Enhance document registration dialog

   Problem: Users need smooth dismiss animation...
   Solution: Add click-outside with fade-out...
   Criteria: 5 items

🚀 Starting implementation...

[Explores codebase, implements solution, checks off criteria]

✅ Complete! Run /fix to commit and push.
```

### Bug Fix

```
> /do

📋 TASK LOADED
   #225 [Bug]: Login timeout ignoring configuration

   Description: Session expires after 30min regardless of config...
   Expected: Session lasts for configured duration
   Actual: Always 30 minutes
   Severity: Medium

🚀 Starting implementation...

[Investigates issue, implements fix]

✅ Complete! Run /fix to commit and push.
```

### Plan Mode

```
> /do --plan

📋 TASK LOADED
   #226 [Feature]: Add bulk document export

   [Shows task details]

📐 Entering plan mode...

[Explores codebase, creates implementation plan]

Plan ready. Approve to start implementing.
```

### Info Only

```
> /do --info

📋 TASK INFO
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Issue:  #224
Title:  [Feature]: Enhance document registration dialog
State:  Open
Labels: enhancement
Size:   Small

Problem:
  Users need the registration dialog to dismiss smoothly...

Solution:
  Add click-outside detection with animation...

Criteria:
  - [ ] Dialog dismisses on click-outside
  - [ ] Smooth fade-out animation
  ...

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

(Info only - not implementing)
```

---

## Integration with Other Skills

| After `/do` | Use |
|-------------|-----|
| Implementation done | `/fix` - validate, commit, push, PR |
| Need to pause | Just exit - work is saved in worktree |
| Task changed | `/do --refresh` - reload issue details |
| PR merged | `/worktree-cleanup 224` - clean up |

---

## Complete Workflow

```bash
# 1. Setup (main repo)
/worktree add user logout button
# Creates issue #224, worktree

# 2. Start work (new terminal)
cd ../.worktrees/feature-224-add-user-logout-button
claude

# 3. Implement
/do
# Loads context, implements automatically

# 4. Commit & PR
/fix
# Validates, commits, pushes, creates PR

# 5. Cleanup (after merge, main repo)
/worktree-cleanup 224
```
