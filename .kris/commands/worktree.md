# /worktree - Autonomous Parallel Agent Workspace

Create, access, or resume worktrees for parallel task implementation.

> **You are the ORCHESTRATOR.** Your job is to:
> 1. **Detect mode:** New worktree (description) or existing worktree (issue number)
> 2. **Pre-flight:** Gather ALL information needed BEFORE launching the sub-agent
> 3. **Setup:** Create issue, worktree, task file (new mode) OR load context (existing mode)
> 4. **Brief:** Build a complete, unambiguous prompt for the sub-agent
> 5. **Launch:** Send the sub-agent to work with everything it needs
> 6. **Report:** Relay results to the user
>
> The sub-agent works autonomously. It NEVER asks questions — not to you, not to the user.
> ALL ambiguity must be resolved BEFORE the agent launches.

---

## Quick Start

```bash
# CREATE new worktree
/worktree "add user logout button"           # Full autonomous flow
/worktree --no-issue "quick config tweak"    # Skip issue creation
/worktree --issue 123                        # Attach to existing issue
/worktree --setup-only "add dark mode"       # Create worktree but don't launch agent
/worktree --fusion "add payee autocomplete"  # F-Thread planning (4 agents, default)
/worktree --f 6 "complex RBAC overhaul"      # --f is alias for --fusion
/worktree --nf "large but simple task"       # Skip F-Thread planning even for L/XL
/worktree --interactive "complex refactor"   # Old interactive mode with checkpoints

# ACCESS existing worktree (by issue number or keywords)
/worktree 287                                # Access by issue number
/worktree payee                              # Access by keyword (matches worktree slug/issue title)
/worktree "physical copy"                    # Access by keyword phrase
/worktree 287 "fix the edge case"            # Access + specific instructions
/worktree 287 --review                       # Review current state only
/worktree 287 --info                         # Just show status, don't act
```

---

## Mode Detection (FIRST STEP — CRITICAL)

Before anything else, determine whether the user wants to ACCESS an existing worktree or CREATE a new one. This requires intelligent analysis, not just pattern matching.

### Step 1: Inventory Existing Worktrees

**Always do this first.** List all worktrees and their metadata so you can match against the input:

```bash
GIT_ROOT=$(git rev-parse --show-toplevel)
WORKTREES_DIR="${GIT_ROOT}/.worktrees"
MAIN_BACKEND="${GIT_ROOT}/backend"
TASKS_DIR="${MAIN_BACKEND}/.kris/tasks"

# List all worktree directories
# Each is named: {type}-{issue_num}-{slug}
# e.g., feature-287-add-payee-validation, bugfix-290-fix-login-timeout
WORKTREE_LIST=$(ls -1 "$WORKTREES_DIR" 2>/dev/null)
```

Build an inventory with: directory name, issue number, slug, branch name, issue title (from TASK.md or GitHub).

### Step 2: Classify Input

Analyze the input against multiple signals:

```
CLASSIFICATION RULES (evaluated in order):

1. EXPLICIT FLAGS → always deterministic
   --issue N "description"     → CREATE (attach to existing issue)
   --no-issue "description"    → CREATE
   --setup-only "description"  → CREATE
   --review                    → ACCESS (combine with target resolution below)
   --info                      → ACCESS (combine with target resolution below)

2. BARE NUMBER → ACCESS (by issue number)
   /worktree 287               → ACCESS issue #287
   /worktree 287 "fix ..."     → ACCESS issue #287 with instructions

3. KEYWORD MATCH → check against existing worktrees
   For each existing worktree, score the input against:
   - Worktree slug (directory name)      weight: HIGH
   - Issue title (from TASK.md/GitHub)   weight: HIGH
   - Branch name                         weight: MEDIUM
   - Task file name                      weight: MEDIUM
   - Issue body/description              weight: LOW

   Match types (descending confidence):
   a. EXACT slug match         → ACCESS (confidence: 100%)
      /worktree payee-validation  matches  feature-287-payee-validation
   b. Substring in slug        → ACCESS candidate (confidence: 80%)
      /worktree payee             matches  feature-287-add-payee-validation
   c. Keyword in issue title   → ACCESS candidate (confidence: 70%)
      /worktree "physical copy"   matches  #298 "Add physical copy tracking"
   d. Partial keyword overlap   → ACCESS candidate (confidence: 50%)
      /worktree copy              matches  feature-298-physical-copy-tracking

4. NO MATCH + LOOKS LIKE DESCRIPTION → CREATE
   If no worktree matches AND the input reads like a task description
   (contains verbs like "add", "fix", "implement", "create", "update",
   "remove", "refactor", or is a multi-word phrase describing work to do):
   → CREATE mode

5. AMBIGUOUS → ASK
   If the input could be either (e.g., a single word that partially
   matches a worktree but could also be a new task description):
   → Present matches with confidence scores and ask user
```

### Step 3: Resolve Matches

**Single match with high confidence (>=70%):**
```
Found worktree: feature-287-add-payee-validation (#287)
Accessing...
```
→ Jump to ACCESS mode with this worktree.

**Multiple matches:**
```
Multiple worktrees match "payee":

  1. [90%] feature-287-add-payee-validation (#287)
           "Add payee validation for bank accounts"
           Status: running (3/5 criteria met)

  2. [60%] bugfix-291-fix-payee-search (#291)
           "Fix payee search returning stale results"
           Status: pending (0/3 criteria met)

Which worktree? [1/2] or [n] to create new: _
```
→ List in order of confidence. Let user pick or create new.

**No matches but input is ambiguous:**
```
No existing worktree matches "copy tracking".

  [c] Create new worktree: "copy tracking"
  [l] List all worktrees

Your choice [c]: _
```

**No matches and input is clearly a description:**
→ Proceed to CREATE mode silently (no prompt needed).

### Step 4: Distinguish ACCESS-with-instructions from CREATE

This is the trickiest case: `/worktree "fix payee edge case"` — is this creating a new task or targeting the existing payee worktree?

**Resolution strategy:**

1. Check if ANY existing worktree matches keywords in the input
2. If match found with confidence >= 60%:
   - Strip the matching keywords to isolate the "instruction" portion
   - Present: `"Did you mean to work on feature-287-add-payee-validation with instruction 'fix edge case'? [Y] or create new? [n]"`
3. If no match or confidence < 60%:
   - Treat as CREATE mode
4. If `--new` flag is present:
   - Always CREATE, skip matching entirely

### Summary Table

| Input | Worktree Exists? | Mode |
|-------|-----------------|------|
| `287` | Yes | ACCESS #287 |
| `287` | No | Error + suggest `--issue 287` |
| `287 "fix edge case"` | Yes | ACCESS #287 + instructions |
| `payee` | 1 match (>=70%) | ACCESS that match |
| `payee` | Multiple matches | Show ranked list, ask |
| `payee` | No match | Ask: create or list? |
| `"add dark mode"` | No match | CREATE |
| `"fix payee edge case"` | "payee" matches | Ask: access existing or create new? |
| `--new "payee improvements"` | (skip matching) | CREATE |
| `--issue 287 "description"` | (irrelevant) | CREATE (new worktree for issue) |

---

## Access Existing Worktree

When the mode is ACCESS (resolved by Mode Detection above), load the worktree context and prepare a session.

### A.1 Locate Worktree

At this point, mode detection has already identified the target worktree. Set up path variables:

```bash
# WORKTREE_MATCH is already resolved by mode detection
WORKTREE_NAME=$(basename "$WORKTREE_MATCH")
WORKTREE_PATH="$WORKTREE_MATCH"
WORKTREE_BACKEND="${WORKTREE_PATH}/backend"

# Parse issue number from worktree name
ISSUE_NUM=$(echo "$WORKTREE_NAME" | grep -oE '[0-9]+' | head -1)
```

### A.2 Gather Status

Collect all context about the worktree's current state. This builds a complete picture from **three sources**: the task file (kanban status), the worktree itself (code state), and GitHub (issue/PR state).

```bash
# 1. Branch info
BRANCH=$(git -C "$WORKTREE_PATH" branch --show-current)
TASK_TYPE=$(echo "$BRANCH" | cut -d'/' -f1)

# 2. Task file status (check ALL kanban columns — critical for determining lifecycle stage)
TASK_PATTERN="*_${ISSUE_NUM}-*.md"
TASK_STATUS="unknown"
TASK_FILE=""
TASK_CONTENT=""

for stage in pending running shipped; do
  FOUND=$(find "${TASKS_DIR}/${stage}" -maxdepth 1 -name "$TASK_PATTERN" 2>/dev/null | head -1)
  if [ -n "$FOUND" ]; then
    TASK_STATUS="$stage"
    TASK_FILE="$FOUND"
    TASK_CONTENT=$(cat "$FOUND")
    break
  fi
done

# 3. Completion marker
HAS_MARKER=false
if [ -f "${WORKTREE_PATH}/.worktree-complete" ]; then
  HAS_MARKER=true
  MARKER_DATA=$(cat "${WORKTREE_PATH}/.worktree-complete")
fi

# 4. TASK.md in worktree (has criteria progress — the agent's working copy)
HAS_TASKMD=false
CRITERIA_DONE=0
CRITERIA_TOTAL=0
CRITERIA_LIST=""
if [ -f "${WORKTREE_BACKEND}/TASK.md" ]; then
  HAS_TASKMD=true
  CRITERIA_DONE=$(grep -c '\- \[x\]' "${WORKTREE_BACKEND}/TASK.md" || echo 0)
  CRITERIA_TOTAL=$(grep -c '\- \[.\]' "${WORKTREE_BACKEND}/TASK.md" || echo 0)
  # Extract the actual criteria lines for display
  CRITERIA_LIST=$(grep '\- \[.\]' "${WORKTREE_BACKEND}/TASK.md")
fi

# 5. Git diff stats (what's changed vs origin/dev)
FILES_CHANGED=$(git -C "$WORKTREE_PATH" diff origin/dev --stat --shortstat 2>/dev/null)
FILES_CHANGED_LIST=$(git -C "$WORKTREE_PATH" diff origin/dev --name-only 2>/dev/null)
UNCOMMITTED=$(git -C "$WORKTREE_PATH" status --porcelain 2>/dev/null | wc -l)

# 6. GitHub issue info
ISSUE_JSON=$(gh issue view "$ISSUE_NUM" --json number,title,body,state,labels 2>/dev/null)
ISSUE_TITLE=$(echo "$ISSUE_JSON" | jq -r '.title')
ISSUE_STATE=$(echo "$ISSUE_JSON" | jq -r '.state')

# 7. PR status (if exists)
PR_INFO=$(gh pr list --head "$BRANCH" --json number,state,url --jq '.[0]' 2>/dev/null)
PR_STATE=$(echo "$PR_INFO" | jq -r '.state // empty' 2>/dev/null)
```

### A.3 Display Status

The display adapts based on task lifecycle stage:

```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
WORKTREE: #{ISSUE_NUM}
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Issue:     #{ISSUE_NUM} — {ISSUE_TITLE} ({ISSUE_STATE})
Branch:    {BRANCH}
Path:      ../.worktrees/{WORKTREE_NAME}

Task lifecycle:
  Task file:  {TASK_STATUS} (.kris/tasks/{TASK_STATUS}/{TASK_BASENAME})
  Marker:     {complete | not complete}
  {IF PR_INFO:}
  PR:         #{PR_NUM} — {PR_STATE}
              {PR_URL}

Criteria:   {CRITERIA_DONE}/{CRITERIA_TOTAL} met
  {each criterion line from TASK.md, showing [x] or [ ]}

Changes vs origin/dev:
  {FILES_CHANGED summary}
  {UNCOMMITTED} uncommitted files

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

### A.4 Determine Action

The action depends on: (1) explicit flags/instructions, (2) task lifecycle stage, (3) worktree code state.

**If `--info` flag:** Display status and EXIT. Do not prompt or launch agents.

**If `--review` flag:** Jump directly to [A.5.2 Review](#a52-review).

**If instructions provided** (e.g., `/worktree 287 "fix the edge case"`):
- The instructions describe WHAT to do — skip the action prompt
- Jump to [A.5.4 Targeted Work](#a54-targeted-work) with the instructions as the brief
- This works regardless of task status (pending, running, OR shipped)

**If no flags or instructions** — present action menu filtered by lifecycle stage:

#### Lifecycle-Aware Action Menu

The menu options and their availability change based on the combined state of: task file location, completion marker, PR status, and criteria progress.

**PENDING** (task in `pending/`, no or few changes):
```
Task #287 is pending — work hasn't started yet.

  [c] Continue  — start implementation (recommended)
  [e] Extend    — add/modify criteria before starting
  [i] Info      — just show status

Your choice [c]: _
```

**RUNNING, criteria incomplete** (task in `running/`, work in progress):
```
Task #287 is in progress — {CRITERIA_DONE}/{CRITERIA_TOTAL} criteria met.

  Remaining:
    [ ] Criterion A
    [ ] Criterion B

  [c] Continue  — resume implementation (recommended)
  [r] Review    — review current changes
  [e] Extend    — add new criteria
  [f] Fix       — address specific bugs or feedback
  [i] Info      — just show status

Your choice [c]: _
```

**RUNNING, all criteria met** (task in `running/`, all done, no marker):
```
Task #287 — all {CRITERIA_TOTAL} criteria met! Ready to ship.

  [s] Ship      — run /fix to commit, push, create PR (recommended)
  [r] Review    — launch review agent first
  [e] Extend    — add more criteria before shipping
  [i] Info      — just show status

Your choice [s]: _
```

**COMPLETE, no PR** (marker exists, not yet pushed/PR'd):
```
Task #287 is marked complete but has no PR yet.

  [s] Ship      — run /fix to commit, push, create PR (recommended)
  [r] Review    — review before shipping
  [e] Extend    — add last-minute changes
  [i] Info      — just show status

Your choice [s]: _
```

**COMPLETE, PR open** (marker exists, PR awaiting review/merge):
```
Task #287 — PR #{PR_NUM} is open ({PR_STATE}).

  [e] Extend    — add changes to address PR feedback
  [f] Fix       — fix issues found during review
  [r] Review    — re-review current state
  [i] Info      — just show status

Your choice: _
```

**SHIPPED, PR merged** (task in `shipped/`, PR merged):
```
Task #287 is shipped — PR #{PR_NUM} merged.

  [e] Extend    — add follow-up changes (new commit on same branch)
  [f] Fix       — address post-merge bug or feedback
  [r] Review    — review what was shipped
  [w] Cleanup   — remove this worktree (/worktree-cleanup {ISSUE_NUM})
  [i] Info      — just show status

  Note: Extend and Fix will create new work on top of the merged changes.
  Consider creating a new issue if the scope is significant.

Your choice: _
```

**SHIPPED, PR merged, issue closed** (everything done):
```
Task #287 is fully complete — issue closed, PR merged.

  [e] Extend    — reopen and add follow-up changes
  [f] Fix       — reopen and fix a post-release issue
  [r] Review    — review what was shipped (read-only)
  [w] Cleanup   — remove this worktree (recommended)
  [i] Info      — just show status

Your choice [w]: _
```

#### State Detection Logic

```
DETERMINE_STATE:
  IF TASK_STATUS == "pending" AND (no changes OR few changes):
    → PENDING
  IF TASK_STATUS == "running" AND CRITERIA_DONE < CRITERIA_TOTAL:
    → RUNNING_INCOMPLETE
  IF TASK_STATUS == "running" AND CRITERIA_DONE == CRITERIA_TOTAL:
    → RUNNING_COMPLETE
  IF HAS_MARKER AND PR_STATE == "" (no PR):
    → COMPLETE_NO_PR
  IF HAS_MARKER AND PR_STATE == "OPEN":
    → COMPLETE_PR_OPEN
  IF TASK_STATUS == "shipped" AND PR_STATE == "MERGED" AND ISSUE_STATE == "OPEN":
    → SHIPPED_ISSUE_OPEN
  IF TASK_STATUS == "shipped" AND PR_STATE == "MERGED" AND ISSUE_STATE == "CLOSED":
    → SHIPPED_FULLY_DONE
  ELSE:
    → Show full menu (all options available)
```

#### Extend/Fix on Shipped Tasks

When a user selects **Extend** or **Fix** on a shipped task, special handling is needed:

1. **Check if branch still exists** — if deleted by `/worktree-cleanup --branch`, need to recreate
2. **Rebase on latest dev** — the branch may be behind if other work merged since
3. **Move task back to running** — `shipped/` → `running/` to reflect active work
4. **Reopen issue** (if closed) — `gh issue reopen $ISSUE_NUM`
5. **Remove completion marker** — delete `.worktree-complete` so `/fix` treats it as fresh
6. Proceed to [A.5.4 Targeted Work](#a54-targeted-work) or [A.5.1 Continue](#a51-continue)

```bash
# Shipped task reactivation
if [ "$TASK_STATUS" = "shipped" ]; then
  # Move task back to running
  TASK_BASENAME=$(basename "$TASK_FILE")
  mv "${TASKS_DIR}/shipped/${TASK_BASENAME}" "${TASKS_DIR}/running/"
  TASK_STATUS="running"

  # Reopen issue if closed
  if [ "$ISSUE_STATE" = "CLOSED" ]; then
    gh issue reopen "$ISSUE_NUM"
  fi

  # Remove completion marker
  rm -f "${WORKTREE_PATH}/.worktree-complete"
  HAS_MARKER=false

  # Rebase on latest dev
  git -C "$WORKTREE_PATH" fetch origin dev
  git -C "$WORKTREE_PATH" rebase origin/dev
fi
```

### A.5 Execute Action

#### A.5.1 Continue

Resume implementation. This is essentially a `/do` flow but launched from the main repo via a sub-agent:

1. Read `TASK.md` from the worktree to get remaining criteria
2. Run Phase 0 pre-flight (research codebase for remaining work)
3. Build a brief focused on **uncompleted criteria only**
4. Launch implementation agent targeting the worktree

```
Agent(
  description: "continue: {slug} (#{issue_num})",
  prompt: <implementation agent prompt — same as Phase 5.3 but:
    - Working directory: {WORKTREE_BACKEND}
    - Focus on unchecked criteria only
    - Note: "This is a CONTINUATION. Some criteria are already met.
      Do NOT redo completed work. Start from where the previous
      agent left off. Read TASK.md for progress so far."
    - Include any new context from orchestrator research
  >,
  subagent_type: "general-purpose"
)
```

After implementation, launch review agent (Phase 6) and follow iteration loop (Phase 7) as normal.

#### A.5.2 Review

Launch a review agent against the current worktree state. Read-only, no modifications:

```
Agent(
  description: "review: {slug} (#{issue_num})",
  prompt: <review agent prompt — same as Phase 6.2 but:
    - Working directory: {WORKTREE_BACKEND}
    - Criteria from TASK.md (noting which are already checked)
  >,
  subagent_type: "general-purpose"
)
```

Display review results to the user. Do NOT auto-launch fix agents — the user decides what to do next based on the review. Present findings and suggest:

```
Review complete. {VERDICT}

Options:
  [f] Fix — launch agent to address findings
  [m] Manual — fix these yourself in the worktree
  [s] Ship — ignore findings and proceed to /fix
```

#### A.5.3 Extend

Add new acceptance criteria or features to an existing task. Works at ANY lifecycle stage — pending, running, or shipped.

**If task is shipped:** Run shipped task reactivation first (see A.4 "Extend/Fix on Shipped Tasks") — moves task back to `running/`, reopens issue, removes completion marker, rebases on latest dev.

1. **Ask the user** what to add (this is the one action that REQUIRES user input beyond the initial command)
2. Read current `TASK.md` from worktree
3. Append new criteria to the Acceptance Criteria section (unchecked `- [ ]`)
4. Write updated `TASK.md` back to worktree
5. Update the task file in the current kanban column (`pending/` or `running/`)
6. Update the GitHub issue body with new criteria
7. Ask: "Launch agent to implement the new criteria? [Y/n]"
   - If yes → follow [A.5.1 Continue](#a51-continue) flow (agent focuses on new + unchecked criteria)
   - If no → display manual instructions:
     ```
     New criteria added to TASK.md.
     To implement manually:
       cd ../.worktrees/{WORKTREE_NAME}/backend
       claude
       /do
     ```

#### A.5.4 Targeted Work

When the user provides specific instructions (e.g., `/worktree 287 "fix the edge case on empty input"`). Works at ANY lifecycle stage.

**If task is shipped:** Run shipped task reactivation first (see A.4 "Extend/Fix on Shipped Tasks").

1. Read `TASK.md` for full context
2. Run Phase 0 pre-flight with the instructions as the focus
3. Build a brief that includes:
   - Full task context from TASK.md (for background)
   - The specific instructions as the PRIMARY objective
   - Current state of criteria (for awareness, not necessarily the focus)
4. Launch implementation agent:

```
Agent(
  description: "targeted: {slug} (#{issue_num})",
  prompt: <implementation agent prompt with:
    - Working directory: {WORKTREE_BACKEND}
    - PRIMARY OBJECTIVE: "{user's specific instructions}"
    - BACKGROUND: Task context from TASK.md
    - Note: "You have a SPECIFIC objective from the user.
      Focus on this first. If it relates to existing criteria,
      check them off. If it's new work, add it to TASK.md
      under Implementation Notes."
  >,
  subagent_type: "general-purpose"
)
```

After implementation, launch review agent focused on the targeted changes only.

#### A.5.5 Ship

This action simply tells the user to run `/fix` inside the worktree:

```
To ship this worktree:

  cd ../.worktrees/{WORKTREE_NAME}/backend
  claude
  /fix

/fix will: validate code, commit, push, create PR targeting dev.
```

**Do NOT attempt to run `/fix` from the main repo** — it must run inside the worktree where it can detect the branch context.

### A.6 Access Mode Report

After any agent action completes, display a summary similar to Phase 8 but adapted:

```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
/worktree {ACTION} COMPLETE — #{ISSUE_NUM}
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Issue:     #{ISSUE_NUM} — {TITLE}
Action:    {continue | review | extend | targeted}
Worktree:  ../.worktrees/{WORKTREE_NAME}

{IF continue/targeted:}
Criteria:  {DONE}/{TOTAL} met
Iterations: {N}
Verdict:   {PASS | PASS_WITH_NOTES | FAIL}

{IF review:}
Verdict:   {PASS | PASS_WITH_NOTES | FAIL}
Findings:  P0:{n} P1:{n} P2:{n} P3:{n}

{IF extend:}
New criteria added: {count}
Agent launched: {yes | no}

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Next Steps:
  {context-appropriate next steps}

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

---

## Workflow Overview (CREATE Mode)

```
/worktree "description"
        |
        v
+===================================+
| PHASE 0: PRE-FLIGHT (CRITICAL)   |  << Orchestrator resolves ALL ambiguity
|  0.1 Codebase scan (~5-10s)      |  << Find relevant files, patterns, gaps
|  0.2 Auto-suggest improvements   |  << 2-4 grounded suggestions (non-blocking)
|  0.3 Complexity assessment        |  << Four-signal classifier → tier (0/3/4)
|  0.4 Clarifying question (max 1) |  << Only if answer changes fundamental approach
|  0.5 Present & confirm            |  << User: Enter/edit/override agent count
+================+=================+
                 v
+---------------------------+
| 1. Analyze description    |  << Orchestrator decides autonomously
|    -> Type, slug          |
|    -> System detection    |
+-------------+-------------+
              v
+---------------------------+
| 2. Create GitHub Issue    |  << Auto-created, no preview prompt
|    -> #123 created        |
+-------------+-------------+
              v
+---------------------------+
| 3. Create Worktree        |  << Branch from origin/dev
|    -> feature-123-slug    |
|    -> Install deps        |
|    -> Create TASK.md      |
+-------------+-------------+
              v
+==========================================+
| 4. F-Thread Planning (adaptive)          |  << Scaled by complexity tier
|    L tasks:  3 agents (sufficient depth) |
|    XL tasks: 4 agents (max exploration)  |
|    XS/S/M:   skip (direct to Phase 5)   |
|    --f N:    force N agents (override)   |
+==================+======================+
                   v
+---------------------------+
| 5. Implement Agent        |  << Uses fused plan as brief
|    -> All context baked in |
|    -> No Q&A              |
+-------------+-------------+
              v
+---------------------------+
| 6. Review Agent           |  << DIFFERENT agent, unbiased
|    -> Check criteria met  |
|    -> Find gaps & issues  |
|    -> Classify severity   |
+-------------+-------------+
              v
+=============================+
| 7. Iteration Loop           |  << Orchestrator decides
|    -> Iter 2: ALL findings  |
|    -> Iter 3+: P0 only     |
|    -> Max 3 iterations      |
+==============+==============+
               v
+---------------------------+
| 8. Report to User         |  << Final summary with review
+---------------------------+
```

---

## Phase 0: Pre-Flight (CRITICAL — runs BEFORE everything else)

The goal of pre-flight is to ensure the sub-agent has ZERO reasons to ask questions AND to right-size the planning effort.

Pre-flight serves three purposes:
1. **Resolve ambiguity** — so agents don't plan the wrong thing
2. **Auto-suggest improvements** — catch gaps the user didn't think of
3. **Assess complexity** — determine how many planning agents (0/3/4) to launch

### 0.1 Codebase Scan

**Always do this first** (~5-10s). A single exploration pass that feeds both auto-suggest and complexity assessment:

```
SCAN the codebase for:
  1. Files matching the task description (Glob/Grep)
  2. Existing patterns/sibling implementations to follow
  3. Integration surfaces — what else does this code touch?
  4. Potential edge cases visible from the code structure

RECORD findings as:
  - FILES_IN_SCOPE: list of files to modify/create
  - PATTERNS_FOUND: existing patterns the agent should follow
  - INTEGRATION_POINTS: other files/systems affected
  - EDGE_CASES: gotchas visible from code structure
```

### 0.2 Auto-Suggest Improvements

**Always runs.** Generate 2-4 suggestions grounded in the codebase scan findings. Auto-suggest is ADDITIVE — it enhances scope, not interrogates.

**Suggestions must be:**
- Grounded in actual code findings (not generic advice like "consider accessibility")
- Actionable and specific (reference file names, component APIs, existing patterns)
- Limited to 2-4 items (more triggers wall-of-text blindness)

**Example of GOOD auto-suggest:**
```
Suggestions (will include unless you say otherwise):
  [x] Arrow key scrolling when selected item is off-screen
      (KSelectorPanel uses v-virtual-scroll — items not in DOM)
  [x] Focus ring styling consistent with existing --k-focus-ring CSS variable
  [ ] Vim-style keys (j/k) — probably overkill?
```

**Example of BAD auto-suggest (do NOT do this):**
```
Before I can proceed, I need clarification:
  1. Which admin panel list? (there are 12)
  2. What keyboard shortcuts?
  3. Single selection or multi-select?
```

**When to SKIP auto-suggest:**
- Task description references specific files AND has clear acceptance criteria
- Task is purely mechanical (rename, delete, config change)
- `--nf` or `--no-suggest` flag is passed

### 0.3 Complexity Assessment (Four-Signal Classifier)

Using the codebase scan findings, classify the task on four signals. This replaces the old keyword-based size estimation as the mechanism for determining F-Thread agent count.

**Signal 1 — Scope Clarity:**
| Level | Description | Example |
|-------|-------------|---------|
| Clear | One file/component, obvious implementation | "Add disabled prop to KButton" |
| Ambiguous | Goal defined but approach open | "Improve document forwarding UX" |
| Open-ended | Creative latitude is the point | "Design routing template graph builder" |

**Signal 2 — Solution Convergence:**
| Level | Description |
|-------|-------------|
| Convergent | One obviously correct approach (adding a column, fixing a type error) |
| Divergent | Multiple valid approaches with meaningful tradeoffs (architecture, state management) |

**Signal 3 — Domain Breadth:**
| Level | Description |
|-------|-------------|
| Narrow | Touches 1 component/service/area |
| Cross-cutting | Spans frontend + backend, or multiple systems (DOTS + CDS), or many coordinated files |

**Signal 4 — Failure Cost:**
| Level | Description |
|-------|-------------|
| Low | Bad approach easily revised, change is isolated |
| High | Bad approach creates tech debt, breaks APIs, requires migration |

**Mapping Signals to Tiers:**

```
TIER 0 (0 agents — direct to implementation):
  Scope=Clear AND Solution=Convergent AND Domain=Narrow
  → Regardless of work volume. A 50-file rename is still Tier 0.

TIER L (3 agents):
  (Solution=Divergent AND Domain=Narrow) OR
  (Scope=Ambiguous AND FailureCost=Low)
  → Most features with design decisions land here.

TIER XL (4 agents):
  Scope=Open-ended OR
  (Solution=Divergent AND Domain=Cross-cutting) OR
  FailureCost=High
  → Getting the approach wrong is expensive.
```

**NOTE:** Size (XS/S/M/L/XL) is still estimated for the issue body and task file, but it no longer drives F-Thread agent count. Complexity tier does.

### 0.4 Clarifying Question (Max 1)

**Ask the user ONLY if** the answer would change the fundamental approach:
- Task has multiple valid interpretations that would fork agents into different plans
- A critical scope decision can't be inferred from the codebase

**Do NOT ask if:**
- You can make a reasonable inference from the description
- The ambiguity is about implementation details (let the agent decide)
- The codebase scan resolved the ambiguity

**Hard limit: 1 question maximum.** If you need 2+ clarifying questions, the task isn't ready for /worktree — tell the user to refine the description.

### 0.5 Present & Confirm

Display the pre-flight findings as a single cohesive output:

```
═══════════════════════════════════════════════════
  TASK: {Generated title from description}
  COMPLEXITY: {Tier 0 | L | XL} — {one-line reasoning}
  PLAN: {Direct implementation | F-Thread with 3 agents | F-Thread with 4 agents}
═══════════════════════════════════════════════════

  Files in scope:
    • {file1}
    • {file2}

  Suggestions:
    1. {Grounded suggestion from codebase scan}
    2. {Grounded suggestion from codebase scan}
    3. {Optional: less certain suggestion, marked as such}

  {IF clarifying question needed:}
  Question: {single clarifying question}

  [Enter] proceed  |  [e] edit scope  |  [f] force fusion  |  [n] skip fusion
═══════════════════════════════════════════════════
```

**Override keys:**
- `Enter` — proceed with assessed tier
- `e` — edit scope/suggestions before proceeding
- `f` or `--f` — force F-Thread (use 4 agents regardless of tier)
- `f3` — force F-Thread with exactly 3 agents
- `n` — skip fusion (Tier 0 regardless of assessment)

**After user confirms**, proceed to Phase 1 (type/slug/system detection) with the enriched context.

### 0.6 Build Complete Agent Brief

Compile everything into a single, self-contained brief:
- Task description and acceptance criteria (including adopted suggestions)
- Relevant file paths discovered during codebase scan
- Specific patterns to follow (from context files)
- Edge cases and integration points from the scan
- Clear scope boundaries (what to change, what NOT to change)

**The brief must be detailed enough that a developer could implement the task without asking a single question.**

---

## Orchestrator Decision Authority

For decisions that DON'T require user input, resolve autonomously:

| Decision | How to Resolve |
|----------|---------------|
| Task type unclear | Pick the most likely type from description |
| System unclear | Use `bgh-katalyst` (generic) |
| Size unclear | Default to `M` |
| Complexity tier unclear | Default to Tier 0 (err toward speed, user can override with `f`) |
| Issue body content | Generate from description |
| Acceptance criteria | Generate reasonable criteria from description + adopted suggestions |
| Branch name conflicts | Add `-v2` suffix automatically |
| Worktree path conflicts | Remove stale worktree and recreate |

---

## Phase 1: Task Analysis (Autonomous)

### 1.1 Type Detection

Analyze the description to determine task type:

| Keywords/Patterns | Type | Branch Prefix |
|-------------------|------|---------------|
| `fix`, `bug`, `broken`, `error`, `crash`, `wrong` | bugfix | `bugfix/` |
| `add`, `new`, `feature`, `implement`, `create` | feature | `feature/` |
| `refactor`, `clean`, `restructure`, `reorganize` | refactor | `refactor/` |
| `docs`, `readme`, `documentation`, `comment` | docs | `docs/` |
| `chore`, `update`, `upgrade`, `config`, `deps` | chore | `chore/` |
| `perf`, `performance`, `optimize`, `speed` | perf | `perf/` |
| `test`, `spec`, `coverage` | test | `test/` |

### 1.2 Slug Generation

Generate a kebab-case slug from the description:

```
"add user logout button" -> add-user-logout-button
"Fix login timeout bug" -> fix-login-timeout-bug
"[Feature]: Add avatar upload" -> add-avatar-upload
```

Rules:
- Lowercase
- Replace spaces with hyphens
- Remove special characters except hyphens
- Strip type prefixes (`[Bug]:`, `[Feature]:`, etc.)
- Max 50 characters

### 1.3 System Detection

| Pattern | System |
|---------|--------|
| `payee`, `fund`, `bank`, `check`, `disbursement`, `finance` | `bgh-katalyst:cds` |
| `document`, `inbox`, `archive`, `routing`, `docstep` | `bgh-katalyst:dots` |
| `user`, `role`, `ability`, `access`, `admin`, `auth` | `bgh-katalyst:sa` |
| `uacs`, `fund-source`, `object-code`, `location` | `bgh-katalyst:uacs` |
| `utils`, `calculator`, `generator` | `bgh-katalyst:utils` |
| Multiple/unclear | `bgh-katalyst` |

### 1.4 Size Estimation (for issue body / task file only)

Size is used for labeling and estimation. It does **NOT** determine F-Thread agent count — that is driven by the complexity tier from Phase 0.3.

| Signal | Size |
|--------|------|
| `quick`, `simple`, `small`, `minor`, `typo` | XS |
| `add`, `update`, `fix` (single thing) | S |
| `implement`, `create`, `feature` | M |
| `refactor`, `redesign`, `overhaul` | L |
| `architectural`, `major`, `complete rewrite` | XL |

---

## Phase 2: Issue Creation (Delegate to `/issue`)

> **CRITICAL: Issue-first workflow.** The issue MUST be created BEFORE F-Thread planning (Phase 4.5)
> so the issue number is available for the **naming cascade**: task file, branch, worktree, session title.

### 2.1 Delegate to `/issue` Logic

**DO NOT use a simplified issue creation.** Follow the full `/issue` skill's style guide, templates, and project integration. This ensures:
- Consistent issue format across all workflows
- Full project field integration (Status, Priority, Size, System, Start/End dates)
- Proper org template adherence (Bug.yml, Feature.yml, technical_maintenance.yml)
- Issue number available for all downstream naming

```bash
# Determine issue type from task type
if [ "$TASK_TYPE" = "bugfix" ]; then
  ISSUE_TYPE="Bug"
elif [ "$TASK_TYPE" = "feature" ]; then
  ISSUE_TYPE="Feature"
else
  ISSUE_TYPE="Tech"
fi

# Follow /issue style guide for:
# 1. Title conventions: [Bug]: / [Feature]: / [Tech]: prefix, sentence case, <80 chars
# 2. Body template: Use org templates (Bug.yml, Feature.yml, technical_maintenance.yml)
# 3. Labels: bug/enhancement/tech,maintenance
# 4. Project integration: Status=Backlog, Priority, Size, System, Start/End dates
# 5. Assignee: @me
#
# See /issue command for full style guide, section templates, and project field logic.
```

### 2.2 Issue Body Templates

Follow `/issue` **Section Templates** exactly. Use the org ISSUE_TEMPLATE structure:

| Issue Type | Template | Required Sections |
|------------|----------|-------------------|
| Bug | Bug.yml | Description, Steps to Reproduce, Expected/Actual Behavior, Severity |
| Feature | Feature.yml | Problem/Use Case, Proposed Solution, Impact, Acceptance Criteria, Estimated Size |
| Tech/Chore | technical_maintenance.yml | Context/Motivation, Planned Changes, Impact, Risks/Notes, Acceptance Criteria, Estimated Size |

### 2.3 Project Integration

Follow `/issue` **Project Integration** section. Set all project fields:

| Field | Detection Method | Values |
|-------|------------------|--------|
| **Status** | Always | `Backlog` |
| **Priority** | From severity/impact | `P0`, `P1`, `P2` |
| **Size** | From scope | `XS`, `S`, `M`, `L`, `XL` |
| **System** | From file paths/context | `bgh-katalyst:cds`, `:dots`, `:sa`, `:uacs`, `:misis`, `:utils` |
| **Start Date** | Today | Date |
| **End Date** | Calculated from size | Date (weekends skipped) |
| **Assignee** | Always | `ravstrigiformes` |

### 2.4 Naming Cascade

Once the issue is created, the issue number flows into ALL downstream names:

```
Issue #365 "Add payee autocomplete"
  ↓
Task file:    .kris/tasks/pending/2026-03-31_365-add-payee-autocomplete.md
Branch:       feature/365-add-payee-autocomplete
Worktree dir: .worktrees/feature-365-add-payee-autocomplete
Session title: #365 add-payee-autocomplete
```

**Without `--no-issue`:** This cascade is mandatory — every artifact gets the issue number.

**With `--no-issue`:** Task file uses `YYYY-MM-DD_<slug>.md` (no issue number). A warning is shown:
```
⚠️  No issue created — task file will lack issue number for tracking.
    Consider running /issue first for better traceability.
```

---

## Phase 3: Create Local Task File

### 3.0 Task File Locations

1. **Worktree:** `${WORKTREE_BACKEND}/TASK.md` - Working copy for the sub-agent (inside `backend/`)
2. **Pending:** `${MAIN_BACKEND}/.kris/tasks/pending/{date}_{issue#}-{slug}.md` - Central tracking (in main backend)

**IMPORTANT:** Task tracking files go to `${MAIN_BACKEND}/.kris/tasks/` (the main repo's `backend/.kris/tasks/`), NOT the repo root's `.kris/tasks/` and NOT inside the worktree's `.kris/tasks/`.

**Naming Convention:** `YYYY-MM-DD_<issue#>-<slug>.md`

### 3.1 Task File Template

```markdown
# Task: {Title without prefix}

**Issue:** #{ISSUE_NUM}
**Type:** {feature|bugfix|refactor|docs|chore}
**Branch:** {BRANCH_NAME}
**Created:** {YYYY-MM-DD}
**Status:** pending

---

## Problem / Use Case

{From issue body}

---

## Proposed Solution

{From issue body}

---

## Acceptance Criteria

- [ ] {Criterion 1}
- [ ] {Criterion 2}
- [ ] {Criterion 3}

---

## Implementation Notes

<!-- Add notes as you work -->

---

## Files Modified

<!-- Updated automatically by /do or manually -->

-

---

## Testing

<!-- How to test the changes -->

-
```

---

## Phase 4: Worktree Creation

### 4.1 Directory Structure & Path Resolution (CRITICAL)

**The git root is the repo root, NOT `backend/`.** The orchestrator runs from `backend/` (the cwd), but worktrees mirror the full repo. Always resolve `$GIT_ROOT` via `git rev-parse --show-toplevel` — **never** from `pwd`, `cwd`, hardcoded folder names, or `../..`. The repo root may be nested under other directories (e.g., a monorepo inside a parent workspace) — the only authoritative source is `git rev-parse --show-toplevel`.

```
<GIT_ROOT>/                             # = git rev-parse --show-toplevel (do not assume the folder name)
  .git/
  .kris/tasks/                          # Task tracking (at repo root)
  .worktrees/                           # Worktrees live here (at repo root)
    {type}-{issue#}-{slug}/             # Worktree = full repo mirror
      backend/                          # <-- Sub-agent cwd (BACKEND_DIR)
        .claude/commands/               # Skills available
        .kris/                          # Context files, scripts, etc.
        TASK.md                         # Task file (created by orchestrator)
  backend/                              # Main repo backend (orchestrator cwd)
    .claude/commands/
    .kris/
```

**Path variables (compute ONCE at the start):**
```bash
# Absolute paths — no relative path ambiguity. Abort on failure.
GIT_ROOT=$(git rev-parse --show-toplevel 2>/dev/null)
if [ -z "$GIT_ROOT" ]; then
  echo "ERROR: Not inside a git repo (git rev-parse --show-toplevel failed). Aborting." >&2
  exit 1
fi

WORKTREES_DIR="${GIT_ROOT}/.worktrees"               # Always at repo root
MAIN_BACKEND="${GIT_ROOT}/backend"                    # Main repo backend (orchestrator cwd)
TASKS_DIR="${MAIN_BACKEND}/.kris/tasks"               # Task tracking lives in backend/.kris/tasks/

# Sanity check — catches the two known failure modes:
#  (a) WORKTREES_DIR under backend/ (cwd-relative slippage)
#  (b) WORKTREES_DIR outside the actual git root (hardcoded/guessed folder name)
case "$WORKTREES_DIR" in
  */backend/.worktrees) echo "ERROR: WORKTREES_DIR is under backend/ ($WORKTREES_DIR). GIT_ROOT resolution failed." >&2; exit 1 ;;
esac
if [ "$(git -C "$WORKTREES_DIR/.." rev-parse --show-toplevel 2>/dev/null)" != "$GIT_ROOT" ]; then
  echo "ERROR: WORKTREES_DIR ($WORKTREES_DIR) is not directly inside git root ($GIT_ROOT). Aborting." >&2
  exit 1
fi

WORKTREE_NAME="${TASK_TYPE}-${ISSUE_NUM}-${SLUG}"
WORKTREE_PATH="${WORKTREES_DIR}/${WORKTREE_NAME}"     # Full worktree root
WORKTREE_BACKEND="${WORKTREE_PATH}/backend"           # Sub-agent working directory
```

**IMPORTANT safeguards:**
- **ALWAYS resolve `$GIT_ROOT` via `git rev-parse --show-toplevel`** — never hardcode a folder name, never use `pwd`, never use `../..`. The diagram above is structural; the actual folder name depends on the machine.
- **NEVER use `../` relative paths** — always compute from `$GIT_ROOT`
- **NEVER create `.kris/` at worktree root** — it belongs inside `backend/` (and is part of the repo, so it's already there in the worktree)
- **Task files go to `${MAIN_BACKEND}/.kris/tasks/`** (backend dir), NOT `${WORKTREE_BACKEND}/.kris/tasks/`
- **The sub-agent's cwd is `${WORKTREE_BACKEND}`** (the `backend/` subfolder), NOT the worktree root
- **TASK.md is created in `${WORKTREE_BACKEND}/TASK.md`** (where the sub-agent runs)

**Known failure modes to avoid (both observed in the past, see `.worktrees/` cleanup audits):**
- Worktree created at `<repo>/backend/.worktrees/...` — caused by using `pwd`/relative paths instead of `$GIT_ROOT`. The sanity check above fails fast on this.
- Worktree created at `<parent-of-repo>/.worktrees/...` — caused by treating the diagram's `<GIT_ROOT>` placeholder as a literal folder name and walking up one extra level. The sanity check above fails fast on this too.

### 4.2 Naming Convention

**With issue number (default):**
```
{type}-{issue#}-{slug}
```

**Without issue number (`--no-issue`):**
```
{type}-{slug}
```

### 4.3 Branch Naming

```
{type}/{issue#}-{slug}
```

### 4.4 Create Worktree

```bash
# Use absolute paths
GIT_ROOT=$(git rev-parse --show-toplevel)
WORKTREES_DIR="${GIT_ROOT}/.worktrees"

mkdir -p "$WORKTREES_DIR"
git fetch origin dev

WORKTREE_NAME="${TASK_TYPE}-${ISSUE_NUM}-${SLUG}"
WORKTREE_PATH="${WORKTREES_DIR}/${WORKTREE_NAME}"
BRANCH_NAME="${TASK_TYPE}/${ISSUE_NUM}-${SLUG}"

git worktree add "$WORKTREE_PATH" -b "$BRANCH_NAME" origin/dev
```

**Note:** Git worktrees mirror the full repo structure. The worktree will contain `backend/` as a subfolder — this is expected and unavoidable. The orchestrator handles this transparently by pointing the sub-agent to `${WORKTREE_PATH}/backend/`.

### 4.5 Install Dependencies (Isolated)

Each worktree gets its own `vendor/` and `node_modules/`. **Do NOT symlink or junction to the main repo** — this causes the main repo's dependencies to be corrupted or deleted when worktrees are cleaned up.

```bash
WORKTREE_BACKEND="${WORKTREE_PATH}/backend"

cd "$WORKTREE_BACKEND"
composer install --no-dev --no-interaction --quiet
npm ci --silent
cd -
```

**Why isolated installs instead of symlinks?**
- Symlinks/junctions on Windows cause mutual interference — `npm install` in a worktree modifies the main repo's `node_modules` through the link
- Worktree cleanup (`git worktree remove`) can delete the junction target, nuking the main repo's dependencies
- Agents occasionally replace symlinks with real directories, creating inconsistent state
- The extra disk space (~200MB per worktree) is worth the reliability

### 4.6 Create TASK.md in Worktree

Write the task file template (from Phase 3) into `${WORKTREE_BACKEND}/TASK.md` (where the sub-agent runs).

**Do NOT create TASK.md at the worktree root** — it must be inside `backend/`.

---

## Phase 4.5: F-Thread Planning (Fusion)

**When active:** Automatically for complexity Tier L (3 agents) and Tier XL (4 agents), or any task with `--fusion` / `--f` flag. Skippable with `--no-fusion` / `--nf`.

### 4.5.1 Purpose

Four independent planning agents analyze the same task in parallel, each producing its own implementation plan. The orchestrator then fuses the best ideas from all four into an "ultimate plan" that becomes the implementation brief.

**Why this works:**
- Different agents notice different edge cases, consider different architectural approaches
- Plans are text — easy to compare, cherry-pick, and merge (unlike code)
- The fused plan covers more ground than any single plan could
- Zero merge conflict risk (it's just text analysis)

### 4.5.2 Planning Agent Prompt Template

Each of the 4 agents receives this **identical** prompt. They run in **parallel** using the Agent tool:

```
You are a planning agent. Your job is to analyze a task and produce a detailed
implementation plan. You are ONE OF {N} agents receiving this same prompt.
Your plan will be compared against {N-1} other independent plans, and the best
ideas from all {N} will be fused into the final implementation plan.

Be creative. Be thorough. Consider approaches the other agents might miss.

## Task
- Issue: #{ISSUE_NUM} — {TITLE}
- Working directory: {WORKTREE_BACKEND}
- System: {SYSTEM}
- Size: {SIZE}

## Problem / Use Case
{DETAILED_PROBLEM}

## Acceptance Criteria
{CRITERIA_LIST}

## Relevant Files (from orchestrator research)
{LIST_OF_FILES}

## Your Planning Process

1. **Read CLAUDE.md and relevant context files** — understand project conventions
2. **Explore the codebase** — read existing files related to this task
3. **Identify ALL files that need modification** — be specific with paths
4. **Consider edge cases** — what could go wrong? what's easy to miss?
5. **Design the approach** — step-by-step implementation order

## Output Format (STRICT)

### Approach Summary
{2-3 sentences describing your overall approach}

### Architecture Decisions
{Key decisions and WHY — e.g., "Use composable over Pinia store because X"}

### Implementation Steps
{Ordered list of specific steps, each with:}
1. **Step title**
   - Files: {exact file paths to create/modify}
   - What: {specific changes — not vague "implement feature"}
   - Why: {rationale for this approach}
   - Gotchas: {edge cases, pitfalls, dependencies}

### Edge Cases & Risks
{What could go wrong, what's easy to miss, security concerns}

### Testing Strategy
{How to verify the implementation works}

### Files Inventory
{Complete list of all files to create/modify with one-line description of changes}

## Rules
- You are AUTONOMOUS. Do NOT ask questions.
- Do NOT write any code. ONLY plan.
- Do NOT modify any files. You are read-only — research and plan only.
- Be SPECIFIC — file paths, function names, component names. No vague "update the relevant files".
- Consider the project's existing patterns (CLAUDE.md, context files).
- Think about what the OTHER three agents might miss. Differentiate your analysis.
```

### 4.5.3 Launch Planning Agents

Determine the number of planning agents (N):

```
FUSION_AGENTS resolution (checked in order):
  IF --fusion/--f N provided:   N = provided value (min 2, if 1 → skip fusion)
  IF --fusion/--f (no number): N = 4 (default)
  IF --nf or Tier 0:           N = 0 (skip fusion)
  IF Tier L (auto-enabled):    N = 3
  IF Tier XL (auto-enabled):   N = 4
```

Launch all N planning agents **in parallel** using the Agent tool:

```
# All N agents launch simultaneously in a SINGLE message with N Agent tool calls
# Example with N=4 (adjust count based on resolved N):

Agent(
  description: "plan-1: {slug} (#{issue_num})",
  prompt: <planning agent prompt from 4.5.2>,
  subagent_type: "general-purpose"
)
Agent(
  description: "plan-2: {slug} (#{issue_num})",
  prompt: <planning agent prompt from 4.5.2>,
  subagent_type: "general-purpose"
)
Agent(
  description: "plan-3: {slug} (#{issue_num})",
  prompt: <planning agent prompt from 4.5.2>,
  subagent_type: "general-purpose"
)
Agent(
  description: "plan-4: {slug} (#{issue_num})",
  prompt: <planning agent prompt from 4.5.2>,
  subagent_type: "general-purpose"
)
# ... up to plan-N
```

**CRITICAL:** All N must be launched in a **single message** (parallel tool calls). Do NOT launch sequentially.

### 4.5.4 Fusion (Orchestrator)

After all 4 agents return, the orchestrator performs fusion **itself** (no additional agent needed — the orchestrator has all 4 plans in context):

**Fusion Process:**

1. **Compare approaches** — identify where plans agree and where they diverge
2. **Score convergence** — if 3+ agents independently chose the same approach, it's likely correct
3. **Cherry-pick unique insights** — each plan will have ideas the others missed
4. **Resolve conflicts** — when plans disagree, pick the approach with better rationale
5. **Synthesize** — combine into a single, comprehensive implementation plan

**Fusion Output Template:**

```markdown
## Fused Implementation Plan
(Synthesized from 4 independent planning agents)

### Consensus Points
{Approaches where 3+ agents agreed — high confidence}

### Unique Insights Adopted
{Best ideas from individual plans that others missed}
- From Plan {N}: {insight} — adopted because {reason}

### Conflicts Resolved
{Where plans disagreed and which approach was chosen}
- {topic}: Plan {N}'s approach chosen over Plan {M}'s because {reason}

### Final Implementation Steps
{Ordered, specific steps — the definitive plan}

### Edge Cases & Risks (merged)
{Combined edge cases from all 4 plans — deduplicated}

### Files Inventory (merged)
{Complete list from all plans — deduplicated, conflicts resolved}
```

### 4.5.5 Plan Handoff

The fused plan replaces the `## Proposed Solution` section in the implementation agent's brief (Phase 5.3). The implementation agent receives:
- Original acceptance criteria (unchanged)
- Fused implementation plan (replaces generic solution)
- Merged file inventory (specific targets)
- Combined edge cases (comprehensive risk awareness)

### 4.5.6 User Report (Planning Phase)

After fusion, briefly report to the user before proceeding to implementation:

```
F-Thread Planning Complete ({N} agents)

Consensus: {agreed}/{N} agreed on core approach
Unique insights adopted: {count}
Conflicts resolved: {count}

Proceeding to implementation with fused plan...
```

---

## Phase 5: Launch Implementation Agent (CRITICAL)

### 5.1 Pre-Launch Checklist

Before launching, verify the orchestrator has gathered:

- [ ] **Clear task description** — unambiguous problem and solution
- [ ] **Acceptance criteria** — specific, testable items (including adopted auto-suggestions)
- [ ] **Relevant file paths** — from Phase 0.1 codebase scan
- [ ] **Patterns to follow** — which context files apply, key conventions
- [ ] **Scope boundaries** — what to change and what NOT to change
- [ ] **User answers** — any clarifying question resolved in Phase 0.4
- [ ] **Complexity tier** — Phase 0.3 assessment (determines if fused plan exists)
- [ ] **Fused plan** (if F-Thread active) — Phase 4.5 fusion output available

If any item is missing, go back to the relevant phase and resolve it BEFORE launching.

### 5.2 Agent Launch

Use the **Agent tool** to launch an autonomous implementation agent:

```
Agent(
  description: "impl: {slug} (#{issue_num})",
  prompt: <see 5.3 below>,
  subagent_type: "general-purpose"
)
```

**NOTE:** The implementation agent runs in the **foreground** because the review agent needs its results before it can start. The orchestrator waits for completion, then launches the review agent.

### 5.3 Implementation Agent Prompt Template

The prompt must be a **complete, self-contained brief**. The agent should be able to implement the task using ONLY this prompt + the codebase. No questions allowed.

**If F-Thread Planning was active (Phase 4.5):** Replace `## Proposed Solution` with the fused plan output. The fused plan contains the implementation steps, file inventory, and edge cases — which is far more detailed than a generic solution description.

```
You are an autonomous implementation agent working in a git worktree.

## Your Task
- Issue: #{ISSUE_NUM} — {TITLE}
- Branch: {BRANCH_NAME}
- Working directory: {WORKTREE_PATH}/backend

## Problem / Use Case
{DETAILED_PROBLEM — not a placeholder, the actual problem statement}

## Implementation Plan
{IF F-THREAD ACTIVE: Insert the FULL fused plan from Phase 4.5.4 here.
 This includes: consensus points, unique insights, resolved conflicts,
 ordered implementation steps, edge cases, and complete file inventory.

 IF F-THREAD NOT ACTIVE: Insert DETAILED_SOLUTION — specific approach,
 not "to be determined"}

## Acceptance Criteria
{CRITERIA_LIST — each criterion on its own line with checkbox}

## Relevant Files (from orchestrator research)
{IF F-THREAD ACTIVE: Use the merged "Files Inventory" from the fused plan —
 it will be more comprehensive than Phase 0 research alone.

 IF F-THREAD NOT ACTIVE: LIST_OF_FILES from Phase 0 pre-flight}
Example:
- resources/js/components/k/KDialog.vue — existing dialog component
- resources/js/composables/useClickOutside.ts — click-outside pattern
- resources/js/modules/dots/components/... — related module components

## Patterns & Context
{APPLICABLE_CONTEXT — which CLAUDE.md context files to read, specific patterns to follow}
Example:
- Read .kris/context/frontend-patterns.md for TanStack Query conventions
- Follow existing component structure in resources/js/components/k/
- Use Vuetify 3.9 components where applicable

## Scope Boundaries
- MODIFY: {specific files/directories that should be changed}
- DO NOT MODIFY: app/*, database/* (backend library — read-only)
- DO NOT MODIFY: {any other files that should be left alone}

## Instructions
1. Read TASK.md in your working directory for additional context
2. Read relevant CLAUDE.md context files as listed above
3. Explore the codebase starting from the relevant files listed above
4. Implement the solution following the Implementation Plan step by step
5. Pay special attention to Edge Cases & Risks identified in the plan
6. Update TASK.md with:
   - Checked-off criteria as you complete them
   - Files modified
   - Implementation notes and decisions made
7. Run lint/format: `npm run lint && npm run format` (frontend) or `vendor/bin/pint --dirty` (PHP)
8. When done, create a `.worktree-complete` marker file

## Rules
- You are AUTONOMOUS. Do NOT ask questions. You have everything you need.
- If something is ambiguous, pick the most reasonable interpretation and note your decision in TASK.md under Implementation Notes.
- Follow all patterns in CLAUDE.md and context files.
- Do NOT run git checkout, git switch, or git worktree commands.
- Do NOT modify backend library files (app/*, database/*).
- Do NOT commit or push — the user will run /fix to handle that.
- Do NOT run `npm run dev` — use one-time test builds if needed.
- Do NOT create new files unless absolutely necessary — prefer editing existing files.
```

### 5.4 Brief Quality Gate

The orchestrator MUST NOT launch the agent if the prompt contains:
- `(To be determined)` or `(To be filled)` placeholders
- Empty sections
- Vague instructions like "implement the feature" without specifics
- No file paths in the "Relevant Files" section

If the brief is incomplete, go back to Phase 0 and gather more information.

---

## Phase 6: Review Agent (CRITICAL — Unbiased Verification)

After the implementation agent completes, launch a **separate, independent review agent**. This agent has NO knowledge of the implementation agent's thought process — it only sees the code changes and the original requirements. This eliminates bias.

### 6.1 Review Agent Launch

```
Agent(
  description: "review: {slug} (#{issue_num})",
  prompt: <see 6.2 below>,
  subagent_type: "general-purpose"
)
```

**Run in FOREGROUND** — the orchestrator needs the review results to decide on iteration.

### 6.2 Review Agent Prompt Template

```
You are an independent code review agent. Your job is to verify that a task
was implemented correctly, find gaps, and suggest improvements.

You are NOT the agent that wrote this code. You are a fresh pair of eyes.
Be thorough but fair — flag real issues, not stylistic preferences.

## Task Being Reviewed
- Issue: #{ISSUE_NUM} — {TITLE}
- Branch: {BRANCH_NAME}
- Working directory: {WORKTREE_PATH}/backend

## Original Requirements
### Problem / Use Case
{DETAILED_PROBLEM}

### Acceptance Criteria
{ORIGINAL_CRITERIA_LIST}

## Your Review Process

1. **Read TASK.md** — check which criteria the implementation agent marked as done
2. **Read the CLAUDE.md and relevant context files** — understand project conventions
3. **Examine ALL modified files** — use `git diff` to see what changed:
   ```bash
   git diff origin/dev --stat        # List changed files
   git diff origin/dev               # Full diff
   ```
4. **Verify each acceptance criterion** — independently confirm it was met (don't trust checkmarks)
5. **Check for issues** across these categories:
   - Correctness: Does the code actually solve the problem?
   - Completeness: Are all criteria met? Any edge cases missed?
   - Conventions: Does it follow CLAUDE.md patterns? (context files, naming, structure)
   - Quality: Any bugs, race conditions, memory leaks, N+1 queries?
   - Security: Any OWASP top 10 concerns? (especially for API-facing code)
   - Scope: Did the implementation agent modify files it shouldn't have?

## Output Format (STRICT — follow exactly)

Return your review as a structured report. Use EXACTLY this format:

### VERDICT: {PASS | FAIL | PASS_WITH_NOTES}

### Criteria Verification
For each acceptance criterion:
- [x] or [ ] {criterion text} — {brief verification note}

### Findings

List each finding as:

**[P0] {title}** — {description}
  File: {file_path}:{line_number}
  Fix: {specific fix instruction}

Severity levels:
- **P0 (Critical):** Broken functionality, security vulnerability, data loss risk,
  crashes, or violates CRITICAL rules from CLAUDE.md. MUST be fixed.
- **P1 (High):** Acceptance criterion not met, significant bug, wrong pattern used,
  will cause issues in production. SHOULD be fixed.
- **P2 (Medium):** Code quality issue, missing edge case handling, inconsistency
  with codebase patterns, minor bug. NICE to fix.
- **P3 (Low):** Stylistic suggestion, minor optimization, documentation gap.
  OPTIONAL.

### Summary
- Total findings: {count} (P0: {n}, P1: {n}, P2: {n}, P3: {n})
- Criteria met: {x}/{total}
- Overall assessment: {1-2 sentence summary}

## Rules
- You are AUTONOMOUS. Do NOT ask questions.
- Be objective — judge the code, not the approach (unless the approach is fundamentally wrong).
- Do NOT modify any files. You are read-only. Report findings only.
- Do NOT run git checkout, git switch, or git worktree commands.
- A PASS verdict means: all criteria met, no P0/P1 findings.
- A PASS_WITH_NOTES verdict means: all criteria met, only P2/P3 findings.
- A FAIL verdict means: criteria not met OR P0/P1 findings exist.
```

### 6.3 Orchestrator Processes Review

After the review agent returns, the orchestrator:

1. **Parses the verdict**: PASS, PASS_WITH_NOTES, or FAIL
2. **Extracts findings** by severity (P0, P1, P2, P3)
3. **Decides next action** based on the iteration rules (see Phase 7)

---

## Phase 7: Iteration Loop (Implement → Review → Fix)

The orchestrator manages an iteration loop between the implementation and review agents.

### 7.1 Iteration Rules

| Iteration | What Gets Fixed | Approval |
|-----------|----------------|----------|
| **1** | Initial implementation | Automatic |
| **2** | ALL findings (P0 + P1 + P2 + P3) | Automatic |
| **3** | P0 ONLY (critical) | Automatic |
| **4+** | P0 ONLY (critical) | **Requires user approval** |

**Soft cap: 3 iterations** (default). Beyond 3, the orchestrator asks the user whether to continue. This is NOT a hard limit — just a checkpoint to ensure token spend is intentional.

**Approval prompt (iteration 4+):**
```
Review iteration 3 complete. Remaining findings:
  [P0] {title} — {description}
  [P1] {title} — {description}  (would not be fixed — P0 only)

Continue to iteration 4? (P0 fixes only)
  [Y] Yes, continue
  [n] No, stop here — I'll review manually

Your choice [Y]: _
```

### 7.2 Loop Logic

```
iteration = 1
soft_cap = 3  # ask user for approval beyond this

LOOP:
  IF iteration == 1:
    Launch Implementation Agent (Phase 5)
  ELSE:
    Launch Fix Agent (Phase 7.3) with findings from review

  Wait for agent to complete

  Launch Review Agent (Phase 6)
  Wait for review to complete

  Parse verdict and findings

  IF verdict == PASS or verdict == PASS_WITH_NOTES:
    EXIT LOOP → Phase 8 (Report)

  IF iteration >= 2:
    Filter findings to P0 only
    IF no P0 findings:
      EXIT LOOP → Phase 8 (Report with remaining P1-P3 as notes)

  IF iteration >= soft_cap:
    ASK USER: "Iteration {iteration} complete. {N} P0 findings remain. Continue?"
    IF user says no:
      EXIT LOOP → Phase 8 (Report with remaining findings)

  iteration += 1
  GOTO LOOP
```

### 7.3 Fix Agent Prompt Template (Iterations 2+)

For subsequent iterations, launch a fix-focused agent (NOT the review agent — a new implementation agent with targeted instructions):

```
You are an autonomous fix agent working in a git worktree.
A review agent has identified issues with the implementation. Your job is
to fix them precisely and efficiently.

## Task Context
- Issue: #{ISSUE_NUM} — {TITLE}
- Branch: {BRANCH_NAME}
- Working directory: {WORKTREE_PATH}/backend
- Iteration: {ITERATION_NUMBER} of {MAX_ITERATIONS}

## Review Findings to Fix

{FORMATTED_FINDINGS_LIST — from review agent output, filtered by iteration rules}

Each finding includes:
- Severity (P0/P1/P2/P3)
- File and line number
- Specific fix instruction

## Iteration Rules
- Iteration 2: Fix ALL findings listed above (P0 + P1 + P2 + P3).
  This is your best chance to resolve everything. Be thorough.
- Iteration 3+: Fix ONLY P0 (critical) findings. Ignore P1/P2/P3.

## Instructions
1. Read each finding carefully
2. Navigate to the specified file and line
3. Apply the fix as described (or a better fix if the suggestion is suboptimal)
4. After ALL fixes, run lint/format:
   - Frontend: `npm run lint && npm run format`
   - PHP: `vendor/bin/pint --dirty`
5. Update TASK.md Implementation Notes with what you fixed

## Rules
- You are AUTONOMOUS. Do NOT ask questions.
- Fix ONLY the listed findings. Do NOT refactor unrelated code.
- Do NOT introduce new features or changes beyond the fixes.
- Follow all patterns in CLAUDE.md and context files.
- Do NOT run git checkout, git switch, or git worktree commands.
- Do NOT modify backend library files (app/*, database/*).
- Do NOT commit or push.
```

### 7.4 Iteration Tracking

The orchestrator tracks iteration state internally:

```
Iteration 1: impl agent → review agent → FAIL (3 P1, 2 P2)
Iteration 2: fix agent (all 5 findings) → review agent → FAIL (1 P1)
Iteration 3: fix agent (0 P0 findings) → EXIT (P1 reported as note)
```

### 7.5 Exit Conditions

The loop exits when:

| Condition | Action |
|-----------|--------|
| Verdict is PASS | Exit — all criteria met, no issues |
| Verdict is PASS_WITH_NOTES | Exit — all criteria met, only P2/P3 notes |
| Iteration >= 2 and no P0 | Exit — remaining issues are non-critical |
| Iteration >= soft_cap and user declines | Exit — user chose to stop |

---

## Phase 7.5: Set Session & Terminal Title

After worktree creation (before launching agents, or after setup if `--setup-only`), set the session name and terminal pane title:

```bash
# Title format: "#<issue_num> <slug>" (e.g., "#224 dialog-dismiss")
TITLE="#${ISSUE_NUM} ${SLUG}"

# Rename Claude Code session
/rename $TITLE

# Set Windows Terminal pane title (ANSI escape — harmless on unsupported terminals)
echo -ne "\033]0;${TITLE}\007"
```

This helps identify which terminal pane belongs to which task when running parallel agents.

---

## Phase 8: Report to User

### 8.1 Final Output

```
---
/worktree COMPLETE

Issue
   #{ISSUE_NUM}: {TITLE}
   {ISSUE_URL}

Worktree
   Path:   ../.worktrees/{WORKTREE_NAME}
   Branch: {BRANCH_NAME}

Version
   Current: {CURRENT_VERSION} (from latest commit/tag)
   Next:    {NEXT_VERSION} (auto-incremented for this change type)

Complexity
   Tier: {0 | L | XL} — {one-line reasoning from Phase 0.3}
   Suggestions: {N} offered, {N} adopted

{IF F-Thread Planning was active:}
Planning
   Mode: F-Thread ({N} parallel planning agents)
   Consensus: {agreed}/{N} agreed on core approach
   Unique insights adopted: {count}
   Conflicts resolved: {count}

Implementation
   Iterations: {N} (impl: 1, review: {N}, fix: {N-1})
   Final verdict: {PASS | PASS_WITH_NOTES | FAIL}

Criteria
   [x] Criterion 1
   [x] Criterion 2
   [x] Criterion 3

Review Summary
   P0 (critical): {n} found, {n} fixed
   P1 (high):     {n} found, {n} fixed
   P2 (medium):   {n} found, {n} fixed
   P3 (low):      {n} found, {n} fixed

{IF remaining findings:}
Remaining Findings (not fixed — non-critical)
   [P2] {title} — {file}:{line} — {description}
   [P3] {title} — {file}:{line} — {description}

Next Steps
   cd ../.worktrees/{WORKTREE_NAME}/backend
   claude
   /fix    # Validate, commit (as {NEXT_VERSION}), push, create PR

Cleanup (after PR merged)
   /worktree-cleanup {ISSUE_NUM}
---
```

### 8.2 Verdict-Specific Guidance

| Verdict | User Guidance |
|---------|--------------|
| PASS | Ready for `/fix`. All criteria met, no issues found. |
| PASS_WITH_NOTES | Ready for `/fix`. Minor notes listed above — review at your discretion. |
| FAIL (max iterations) | Review remaining P0/P1 findings. You may want to fix manually or re-run `/worktree` with more specific instructions. |

---

## Flags

### `--no-issue`

Skip issue creation:

```bash
/worktree --no-issue "quick config update"
```

### `--issue <num>`

Attach to existing issue:

```bash
/worktree --issue 123
```

- Fetches issue details from GitHub
- Uses issue title/body for TASK.md
- Skips issue creation

### `--type <type>`

Force task type:

```bash
/worktree --type=feature "implement dark mode"
```

### `--fusion [N]` (alias: `--f [N]`)

Enable F-Thread Planning (Phase 4.5) with N parallel planning agents. Default: **4 agents**.

```bash
/worktree --fusion "add autocomplete to payee search"       # 4 agents (default)
/worktree --fusion 6 "complex RBAC overhaul"                 # 6 agents
/worktree --fusion 2 "moderate complexity task"              # 2 agents (lighter)
```

**Default behavior without flag:** F-Thread Planning activates automatically based on the complexity tier assessed in Phase 0.3:
- **Tier 0** (clear scope, convergent solution): No fusion — direct to implementation
- **Tier L** (divergent solutions, moderate complexity): **3 agents**
- **Tier XL** (cross-system, open-ended, high failure cost): **4 agents**

Use `--fusion` (or `--f`) to force fusion on Tier 0 tasks, or `--f N` to set an exact agent count.

**Minimum:** 2 agents (need at least 2 for meaningful comparison). If N=1, treated as `--nf` (no fusion).

### `--nf`

Shorthand for `--no-fusion`. Skip F-Thread Planning even for L/XL tasks:

```bash
/worktree --nf "refactor all admin panels to use new layout"
```

### `--setup-only`

Create worktree but do NOT launch agents. Use when you want to manually start a Claude session in the worktree:

```bash
/worktree --setup-only "add dark mode"
```

Output includes manual instructions:
```
cd ../.worktrees/{WORKTREE_NAME}/backend
claude
/do
```

### `--soft-cap <n>`

Override the default soft cap for automatic iterations (default: 3). Beyond this, the orchestrator asks for user approval before continuing:

```bash
/worktree --soft-cap 1 "simple typo fix"     # Ask after first review
/worktree --soft-cap 5 "complex refactor"    # Allow more automatic cycles
```

- `--soft-cap 1`: Review once, then ask before any fix iteration
- `--soft-cap 3`: Default — 3 automatic iterations, then ask
- Higher values: More autonomy for complex tasks

### `--no-review`

Shorthand for `--max-iterations 1`. Skip review entirely:

```bash
/worktree --no-review "update version number"
```

### `--interactive`

Restore the old interactive mode with confirmation checkpoints. Use for complex or ambiguous tasks where you want to review before proceeding.

Interactive checkpoints:
1. Confirm analysis (type, system, size)
2. Create issue? (yes/no)
3. Issue preview (review body before creation)
4. Launch agent? (yes/setup-only)

### `--new`

Force CREATE mode — skip worktree matching entirely. Use when your description happens to contain keywords that match an existing worktree:

```bash
/worktree --new "improve payee search performance"  # Don't match existing payee worktree
```

### `--review` (ACCESS mode)

Launch review agent against an existing worktree without making changes:

```bash
/worktree 287 --review
/worktree payee --review
```

### `--info` (ACCESS mode)

Display worktree status without launching agents or prompting for action:

```bash
/worktree 287 --info
/worktree payee --info
```

---

## Error Handling

| Error | Auto-Resolution |
|-------|-----------------|
| Branch already exists | Append `-v2` suffix and retry |
| Worktree path exists | Check if stale; prune and recreate |
| Issue creation fails | Log error, continue with `--no-issue` flow |
| Not on dev branch | Auto-fetch dev, create from `origin/dev` |
| `gh` not authenticated | **ASK USER** — cannot auto-resolve |
| Empty description | **ASK USER** — need at least a description |
| Dependency install fails | Warn but continue (agent can retry `composer install && npm ci`) |
| Impl agent fails/crashes | Report error, suggest `--setup-only` for manual work |
| Review agent returns invalid format | Retry review once; if still invalid, skip review |
| Fix agent introduces new P0 | Counts toward iteration limit; orchestrator warns user |

---

## Integration with Other Skills

| Skill | Relationship |
|-------|-------------|
| `/do` | Alternative manual flow — used inside worktree when `--setup-only` |
| `/fix` | Run manually in worktree after agents complete — validates, commits, pushes, creates PR |
| `/worktree-cleanup` | Removes worktree after PR merged |
| `/issue` | Shares issue creation logic (DRY) |

---

## Safety Notes

1. **Main repo stays on dev** — `/worktree` never changes the main repo's branch
2. **Worktrees are isolated** — Changes in one worktree don't affect others
3. **Branch from origin/dev** — Always creates branch from latest remote dev
4. **Issue provides traceability** — PR will reference and auto-close issue
5. **Agents cannot push** — Only `/fix` handles git operations
6. **Review agent is read-only** — It cannot modify files, only report findings
7. **Iteration soft cap** — 3 automatic iterations; beyond that, asks user for approval (not a hard limit)
8. **Escalating strictness** — Iteration 2 fixes everything; iteration 3+ fixes P0 only (token conservation)
