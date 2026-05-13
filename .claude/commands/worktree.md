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

## Branch contract

> **Why this exists:** `/worktree` runs `git worktree add` (no main-repo checkout) for new tasks, but its "shipped reactivation" path issues `git -C $WORKTREE rebase origin/dev` against an existing worktree. The main repo's HEAD is unchanged, but the worktree's HEAD moves — and any uncommitted work in that worktree must be preserved.

- **Starts on:** any (main repo) — `/worktree` runs from the main repo
- **Ends on:** same main-repo branch (assert at end)
- **Touches branches:** main-repo branch (read-only); creates feature/bugfix branches inside worktrees
- **Worktree-first:** yes — that IS this skill's purpose

**Self-check (every run):**

```bash
# Branch contract: record start branch+HEAD
_SKILL_START_BRANCH=$(git rev-parse --abbrev-ref HEAD)
_SKILL_START_HEAD=$(git rev-parse HEAD)                # captures main-repo starting branch
# ...skill body (worktree add / rebase / spawn agent)...
# Branch contract end: assert unchanged
if [[ "$(git rev-parse --abbrev-ref HEAD)" != "$_SKILL_START_BRANCH" || "$(git rev-parse HEAD)" != "$_SKILL_START_HEAD" ]]; then
  echo "ERROR: branch contract violated" >&2; exit 1
fi
```

The orchestrator NEVER runs `git checkout` in the main repo. All branch operations are scoped to the worktree via `git -C $WORKTREE ...`.

---

## Stash guard (cross-tree operations)

When the orchestrator (running in the main repo) issues `git -C <worktree> ...` operations, the main repo's working tree is untouched — but the **worktree's** working tree may be dirty (an autonomous sub-agent's WIP that the orchestrator can't see from the main-repo perspective). Before the orchestrator triggers a rebase/merge inside a worktree, it MUST:

1. Inspect the worktree: `git -C "$WORKTREE" status --porcelain`. If non-empty, stash with a session-tagged message:
   ```bash
   git -C "$WORKTREE" stash push -u -m "wt-reactivate-#$ISSUE-$$"
   ```
2. Run the rebase/merge (now via `sync-safe-merge.sh` — see below).
3. On success, pop: `git -C "$WORKTREE" stash pop` (preserve the stash on pop-conflict; never `stash drop` blindly).

---

## Sync merge policy (shipped reactivation rebase)

The shipped-reactivation path currently runs `git -C "$WORKTREE" rebase origin/dev` with no conflict policy. Replace with:

```bash
    --dir="$WORKTREE" \
    --strategy=auto-ours \
    --skill=worktree \
    --message="reactivate: sync dev into worktree"
```

The reactivation goal is to bring the worktree's branch up to date with `dev` while preserving the work the worktree was originally created for — so `auto-ours` (keep the feature branch's version on code-file conflict) is the correct policy. A bare `git rebase origin/dev` would silently apply `dev`'s side on conflict, recreating the PR #538 failure mode at reactivation time.

---

## Failure tier contract (autonomy phase)

After grill-me writes `decisions.json` (end of Phase 0), the autonomy phase runs to completion (Phase 1 → Phase 7.95) without prompting the user. Failures are sorted into two tiers:

**Catastrophic failures HALT the autonomy phase immediately.** These are the only conditions allowed to break the no-halts contract — they signal that *continuing is unsafe* (data loss risk, corrupted state, parallel-agent interference). The list is closed:

1. **Branch contract violation** — main repo HEAD changed during the run vs the snapshot taken at orchestrator start.
2. **Stash-pop conflict** in any worktree — preserved WIP cannot be auto-restored. Stash is preserved (never auto-dropped).
3. **Worktree path/branch mismatch** — directory `<type>-<issue>-*` exists but its checked-out branch ≠ `<type>/<issue>-*`.
4. **Main repo dirty when guard expects clean** — `branch-sanity.py` PreToolUse hook returned a block verdict.
5. **Disk full or permission denied on `.kris/scratch/` writes** — orchestrator can't even record state.

When a catastrophic failure fires, write `${SCRATCH_DIR}/catastrophic-halt.md` with a state snapshot (current branch, working-tree status, any preserved stashes, the operation that triggered the halt) and exit. The user re-runs `/wt` after triage.

**Non-catastrophic failures fall through with annotation.** Everything else: `gh` failure, empty diff from impl agent, review iteration cap reached, preview merge conflict beyond auto-ours, lint errors, `/fix` tab spawn nonzero, missing optional tooling. Each appends a row to `${SCRATCH_DIR}/failures.json` (schema in `.kris/context/grill-me-checklist-mode.md`) and the orchestrator continues to the next phase. The spawned `/fix` tab consumes `failures.json` as part of its brief and surfaces the punchlist to the user post-smoke-test.

Bash skeleton for any failure-prone step:

```bash
fail_nonfatal() {
  local phase="$1" code="$2" msg="$3" hint="$4"
  jq -n --arg phase "$phase" --arg code "$code" \
        --arg msg "$msg" --arg hint "$hint" \
        --arg ts "$(date -u +%FT%TZ)" \
    '{phase:$phase, tier:"non-catastrophic", code:$code, message:$msg, remediation_hint:$hint, timestamp:$ts}' \
    >> "${SCRATCH_DIR}/failures.ndjson"
}

fail_catastrophic() {
  local code="$1" msg="$2"
  {
    echo "# /wt catastrophic halt"
    echo "Code: $code"
    echo "Message: $msg"
    echo "Time: $(date -u +%FT%TZ)"
    echo
    echo "## State snapshot"
    git -C "$MAIN_BACKEND" branch --show-current
    git -C "$MAIN_BACKEND" status --porcelain
    git -C "$WORKTREE_PATH" status --porcelain 2>/dev/null
    echo
    echo "## Preserved artefacts"
    git -C "$WORKTREE_PATH" stash list 2>/dev/null
  } > "${SCRATCH_DIR}/catastrophic-halt.md"
  exit 2
}
```

`failures.ndjson` (newline-delimited JSON, append-only) is collated into `failures.json` at the end of the autonomy phase (Phase 7.95) before the spawn brief is built.

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
# /worktree --interactive  (REMOVED — superseded by Phase 0 grill-me bounded interview)

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
TASKS_DIR="${GIT_ROOT}/.kris/tasks"

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
WORKTREE_BACKEND="${WORKTREE_PATH}"

# Parse type / issue / slug from worktree name.
# Format: <type>-<issue>-<slug>  e.g.  refactor-579-spawn-tab-and-sweep-gate-fixes
# Without this, downstream phases (notably Phase 7.95 spawn) inherit a stale
# SLUG from the parent shell — producing tab titles like "#579 Spawn Tab And ..."
# instead of the expected "#579 spawn-tab-and-sweep-gate-fixes".
ISSUE_NUM=$(echo "$WORKTREE_NAME" | grep -oE '[0-9]+' | head -1)
TASK_TYPE="${WORKTREE_NAME%%-*}"
SLUG="${WORKTREE_NAME#${TASK_TYPE}-${ISSUE_NUM}-}"
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
if [ -f "${WORKTREE_PATH}/TASK.md" ]; then
  HAS_TASKMD=true
  CRITERIA_DONE=$(grep -c '\- \[x\]' "${WORKTREE_PATH}/TASK.md" || echo 0)
  CRITERIA_TOTAL=$(grep -c '\- \[.\]' "${WORKTREE_PATH}/TASK.md" || echo 0)
  # Extract the actual criteria lines for display
  CRITERIA_LIST=$(grep '\- \[.\]' "${WORKTREE_PATH}/TASK.md")
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

# 8. Recovery state (wt-state/ files — self-heal layer)
RECOVERY_STATE="none"
RECOVERY_LAST_ACTION=""
RECOVERY_LAST_JOURNAL=""
RECOVERY_LAST_VERIFICATION=""
RECOVERY_BLOCKER_COUNT=0
RECOVERY_AGE_MIN=""
WT_STATE_DIR="${WORKTREE_PATH}/.kris/wt-state"

if [ -f "${WT_STATE_DIR}/last-action.md" ]; then
  # Stale if last-action.md hasn't been touched in 30+ min
  STALE_AGE_MIN=30
  if [ -n "$(find "${WT_STATE_DIR}/last-action.md" -mmin +${STALE_AGE_MIN} 2>/dev/null)" ]; then
    RECOVERY_STATE="stale"
    RECOVERY_LAST_ACTION=$(cat "${WT_STATE_DIR}/last-action.md")

    # Compute age (rounded minutes) for display — falls back to "30+" if stat unavailable
    NOW_EPOCH=$(date +%s 2>/dev/null)
    MTIME_EPOCH=$(stat -c %Y "${WT_STATE_DIR}/last-action.md" 2>/dev/null || stat -f %m "${WT_STATE_DIR}/last-action.md" 2>/dev/null)
    if [ -n "$NOW_EPOCH" ] && [ -n "$MTIME_EPOCH" ]; then
      RECOVERY_AGE_MIN=$(( (NOW_EPOCH - MTIME_EPOCH) / 60 ))
    else
      RECOVERY_AGE_MIN="30+"
    fi

    # Last journal entry: split on "## " markers, take the last block (max 10 lines)
    if [ -f "${WT_STATE_DIR}/journal.md" ]; then
      RECOVERY_LAST_JOURNAL=$(awk '/^## /{block=""} {block=block $0 "\n"} END{print block}' "${WT_STATE_DIR}/journal.md" | head -10)
    fi

    # Last 3 verification entries (lines starting with "- [")
    if [ -f "${WT_STATE_DIR}/verification.md" ]; then
      RECOVERY_LAST_VERIFICATION=$(grep -E "^- \[" "${WT_STATE_DIR}/verification.md" | tail -3)
    fi

    # Open (not struck-through) blocker count
    if [ -f "${WT_STATE_DIR}/blockers.md" ]; then
      RECOVERY_BLOCKER_COUNT=$(grep -E "^- \[[0-9:]+\] \*\*Blocked:" "${WT_STATE_DIR}/blockers.md" | grep -vE "^- ~~" | wc -l)
    fi
  fi
fi
```

**Why mtime-based:** the executor's self-heal protocol (`.kris/templates/wt/executor.md` → "Self-heal protocol") rewrites `last-action.md` on every clean step transition. A 30+ minute gap means either: (a) the agent finished and exited cleanly, or (b) the session was wiped mid-work. Either way, surfacing the recovery block is helpful — fresh re-entries confirm "yep, last action 35min ago, picking up" while wiped sessions get full context restoration.

### A.3 Display Status

The display adapts based on task lifecycle stage. **When `RECOVERY_STATE == "stale"` (set in A.2.8), prepend the recovery block before the normal status display:**

```
{IF RECOVERY_STATE == "stale":}
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
🔁 DETECTED RECOVERY STATE — last action {RECOVERY_AGE_MIN} min ago
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Last journal entry:
{RECOVERY_LAST_JOURNAL — first 5 lines, indented 2 spaces}

Last action:
{RECOVERY_LAST_ACTION — full content, indented 2 spaces}

Last verification:
{RECOVERY_LAST_VERIFICATION — up to 3 lines, indented 2 spaces}

Open blockers: {RECOVERY_BLOCKER_COUNT}
  (full list in .kris/wt-state/blockers.md)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
{END IF}

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

**Recovery override:** when `RECOVERY_STATE == "stale"` (A.2.8 detected wt-state files older than 30min), prepend `[r] Resume` to PENDING, RUNNING_INCOMPLETE, and COMPLETE_NO_PR menus and make it the recommended default. The conflict with the existing `[r] Review` letter in RUNNING_INCOMPLETE is resolved by remapping Review to `[v] Review` for that menu only when recovery is offered (the `[r]` slot is more valuable as Resume since it loads the wt-state read-list into the spawned executor).

**PENDING** (task in `pending/`, no or few changes):
```
Task #287 is pending — work hasn't started yet.

  {IF RECOVERY_STATE == "stale":}
  [r] Resume    — read wt-state journal+last-action and continue from intent (recommended)
  [c] Continue  — fresh start, ignore wt-state
  {ELSE:}
  [c] Continue  — start implementation (recommended)
  {END IF}
  [e] Extend    — add/modify criteria before starting
  [i] Info      — just show status

Your choice [{r if stale else c}]: _
```

**RUNNING, criteria incomplete** (task in `running/`, work in progress):
```
Task #287 is in progress — {CRITERIA_DONE}/{CRITERIA_TOTAL} criteria met.

  Remaining:
    [ ] Criterion A
    [ ] Criterion B

  {IF RECOVERY_STATE == "stale":}
  [r] Resume    — read wt-state and continue from intent (recommended)
  [c] Continue  — fresh continue, ignore wt-state
  [v] Review    — review current changes
  {ELSE:}
  [c] Continue  — resume implementation (recommended)
  [r] Review    — review current changes
  {END IF}
  [e] Extend    — add new criteria
  [f] Fix       — address specific bugs or feedback
  [i] Info      — just show status

Your choice [{r if stale else c}]: _
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

  {IF RECOVERY_STATE == "stale":}
  [r] Resume    — read wt-state and verify completion (recommended if last action was mid-ship)
  [s] Ship      — run /fix to commit, push, create PR
  [v] Review    — review before shipping
  {ELSE:}
  [s] Ship      — run /fix to commit, push, create PR (recommended)
  [r] Review    — review before shipping
  {END IF}
  [e] Extend    — add last-minute changes
  [i] Info      — just show status

Your choice [{r if stale else s}]: _
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

RECOVERY_OVERLAY (orthogonal to lifecycle state):
  IF RECOVERY_STATE == "stale" AND lifecycle ∈ {PENDING, RUNNING_INCOMPLETE, COMPLETE_NO_PR}:
    → menu prepends [r] Resume as recommended default
    → RUNNING_INCOMPLETE remaps existing [r] Review → [v] Review to free the [r] slot
    → If user picks [r], A.5.1 spawn prompt extends read-list with the 4 wt-state files
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

#### A.5.0 Ensure session workspace exists

Access mode may target a worktree created before this refactor (no session dir) OR a new one (session dir present). Either way, before spawning agents, ensure `.kris/wt-sessions/<issue#>-<slug>/` exists with a current `brief.md` and `final-plan.md`:

```bash
SESSION_ID="${ISSUE_NUM}-${SLUG}"
SESSION_DIR="${GIT_ROOT}/.kris/wt-sessions/${SESSION_ID}"

if [ ! -f "${SESSION_DIR}/brief.md" ]; then
  mkdir -p "${SESSION_DIR}/plans"
  # Rebuild brief from TASK.md + GitHub issue + quick codebase scan
  # Follow .kris/templates/wt/brief-template.md structure.
  # For continue/targeted actions, include CRITERIA_LIST with checkmarks preserved.
  # Write to ${SESSION_DIR}/brief.md
fi

if [ ! -f "${SESSION_DIR}/final-plan.md" ]; then
  # For CONTINUE: write a minimal final-plan.md listing the unchecked criteria as steps.
  # For TARGETED: write the user's instructions as the Approach Summary + Implementation Steps.
  # For REVIEW-only: not needed (reviewer reads brief + actual diff).
fi

# Backfill wt-state/ for older worktrees created before the self-heal layer existed.
# Idempotent — only seeds files that don't yet exist.
WT_STATE_DIR="${WORKTREE_PATH}/.kris/wt-state"
WT_STATE_TEMPLATES="${GIT_ROOT}/.kris/templates/wt-state"
if [ ! -d "$WT_STATE_DIR" ]; then
  mkdir -p "$WT_STATE_DIR"
  for f in journal last-action verification blockers; do
    cp "${WT_STATE_TEMPLATES}/${f}.md.template" "${WT_STATE_DIR}/${f}.md"
  done
fi
```

#### A.5.1 Continue

Resume implementation on uncompleted criteria. After ensuring the session workspace (A.5.0):

**If user picked `[c] Continue`** (no recovery, or chose to ignore wt-state):

```
Agent(
  description: "continue: {slug} (#{ISSUE_NUM})",
  model: "opus",
  subagent_type: "general-purpose",
  prompt: "
You are the iteration executor for session {SESSION_ID} in CONTINUE mode.

Read these files, in order, before doing anything else:
  1. .kris/templates/wt/executor.md                 — your role and rules
  2. .kris/wt-sessions/{SESSION_ID}/brief.md        — task brief (criteria show which are done)
  3. .kris/wt-sessions/{SESSION_ID}/final-plan.md   — implementation spec for remaining work
  4. TASK.md in your cwd                            — progress tracker

Working directory: {WORKTREE_BACKEND}

CONTINUATION: Some criteria are already met (see TASK.md checkmarks). Do NOT redo
completed work. Implement only what final-plan.md names as remaining.
"
)
```

**If user picked `[r] Resume`** (RECOVERY_STATE was stale): the spawn prompt's read-list extends to include the 4 wt-state files so the executor inherits the prior agent's intent and verification trust.

```
Agent(
  description: "resume: {slug} (#{ISSUE_NUM})",
  model: "opus",
  subagent_type: "general-purpose",
  prompt: "
You are the iteration executor for session {SESSION_ID} in RESUME mode.

Read these files, in order, before doing anything else:
  1. .kris/templates/wt/executor.md                 — your role and rules (incl. self-heal protocol)
  2. .kris/wt-sessions/{SESSION_ID}/brief.md        — task brief
  3. .kris/wt-sessions/{SESSION_ID}/final-plan.md   — implementation spec
  4. .kris/wt-state/journal.md              — decision history (read all)
  5. .kris/wt-state/last-action.md          — what to resume from
  6. .kris/wt-state/verification.md         — what's already trusted (skip re-running these)
  7. .kris/wt-state/blockers.md             — open dependencies
  {IF .kris/wt-state/rehydrate-notes.md exists:}
  8. .kris/wt-state/rehydrate-notes.md      — rehydrate's pre-analysis (phase, confidence, artifact basis); informational only, NOT authoritative intent
  9. TASK.md in your cwd                            — progress tracker
  {ELSE:}
  8. TASK.md in your cwd                            — progress tracker

Working directory: {WORKTREE_BACKEND}

RESUME: the previous session left wt-state files describing intent and trusted verification.
Continue from `last-action.md`'s 'Next intended' without redoing what verification.md
has marked clean. Honor the journal's prior decisions unless explicitly overridden by
the brief or final-plan. Update wt-state files yourself as you proceed (see executor.md →
'Self-heal protocol').

If `rehydrate-notes.md` is included above, treat it as supplementary diagnostic context
from a prior `/rehydrate` run. `last-action.md` remains the authoritative intent file.
"
)
```

After implementation, launch review agent (A.5.2) and follow iteration loop (Phase 7).

#### A.5.2 Review

Launch a review agent against the current worktree state. Read-only.

```
Agent(
  description: "review: {slug} (#{ISSUE_NUM})",
  model: "opus",
  subagent_type: "general-purpose",
  prompt: "
You are the review agent for session {SESSION_ID}.

Read these files, in order:
  1. .kris/templates/wt/reviewer.md                 — role, axes, severity rubric, output format
  2. .kris/wt-sessions/{SESSION_ID}/brief.md        — original requirements (noting which criteria were checked)
  3. CLAUDE.md                                       — rules to verify against

Worktree backend dir: {WORKTREE_BACKEND}

Write your review to:
  .kris/wt-sessions/{SESSION_ID}/review.md
"
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

1. Ensure session workspace exists (A.5.0). Augment `brief.md` → append a new section:
   ```markdown
   ## Targeted Instructions (this invocation)
   {verbatim user instructions}
   ```
2. Write `final-plan.md` as a small focused plan: Approach Summary reflects the targeted objective, Implementation Steps are just the needed changes, Files Inventory scoped to the edge case.
3. Launch executor with the standard file-based spawn:

```
Agent(
  description: "targeted: {slug} (#{ISSUE_NUM})",
  model: "opus",
  subagent_type: "general-purpose",
  prompt: "
You are the implementation agent for session {SESSION_ID} in TARGETED mode.

Read these files, in order:
  1. .kris/templates/wt/executor.md                 — role and rules
  2. .kris/wt-sessions/{SESSION_ID}/brief.md        — task brief (note the 'Targeted Instructions' section)
  3. .kris/wt-sessions/{SESSION_ID}/final-plan.md   — scoped plan for this targeted work
  4. TASK.md in your cwd                            — progress tracker

Working directory: {WORKTREE_BACKEND}

TARGETED objective is the PRIMARY focus. If it relates to existing unchecked criteria,
check them off as you complete them. If it's new work, add it to TASK.md under
Implementation Notes (do not invent new acceptance criteria).
"
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

## Phase 0: Requirements Consolidation (grill-me bounded mode)

> **The autonomy contract.** Phase 0 is the ONLY place in `/wt` where the user is prompted. Once Phase 0 writes `decisions.json`, the autonomy phase (Phases 1–7.95) runs hands-off until the `/fix` tab is spawned. The next time a human is in the loop is the **smoke test** in the browser opened by inlined-preview. No question, prompt, or confirmation may be raised by the orchestrator or by any sub-skill it inlines during Phases 1–7.95.
>
> All `/wt` invocations enter Phase 0, including ACCESS mode (e.g., `/wt 287`). Grill-me's bounded checklist auto-decides high-confidence items silently — for an unambiguous `/wt 287 --info`, that's zero questions and a sub-second exit.
>
> Full protocol in `.kris/context/grill-me-checklist-mode.md`. Read it before modifying this phase.

> **NO-PAUSE DIRECTIVE (read this twice).** Between every two consecutive phases in `/wt` (0.5→1, 1→2, …, 7.9→7.95) there is **ZERO pause**. The orchestrator does NOT:
> - print a "Decisions locked, autonomy phase starting" line and stop;
> - emit an end-of-turn summary mid-flow;
> - wait for the user to say "continue" / "proceed" / "go";
> - spin a thinking indicator and wait for input.
>
> The instant grill-me writes `decisions.json`, the orchestrator's **immediate next tool call** is reading that file and proceeding to Phase 1. Same for every subsequent phase boundary. The only acceptable terminal states are:
> 1. `/fix` tab successfully spawned (Phase 7.95 → exit);
> 2. Catastrophic halt (5-item list — exit with `catastrophic-halt.md`);
> 3. `--setup-only` reached its endpoint (worktree created, no impl).
>
> Anything else where the orchestrator visibly stops mid-flow is a **bug** to be fixed forward. If you find yourself about to print "Phase X complete" and end your turn — do not. Make the next tool call instead.

### 0.0 Phase 0 Pipeline (overview)

```
0.1 Codebase scan ─┐
0.2 Complexity   ──┼─→  0.3 Build checklist  ─→  0.4 Invoke grill-me  ─→  decisions.json
0.2b Auto-suggest ─┘                                                          │
                                                                              ↓
                                                                     Phase 1 (autonomy starts)
```

The codebase scan, complexity assessment, and auto-suggest no longer present *user-facing* output. Their output flows into grill-me as confidence inputs and proposed checklist items. Grill-me decides what to ask vs auto-decide.

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

### 0.3 Build the grill-me checklist

Construct the checklist that grill-me will walk. Items, ordering, and confidence thresholds depend on mode (CREATE vs ACCESS) and on the flags the user passed.

**CREATE-mode checklist (10 items, in dependency order):**

| # | id | recommend-from | threshold | notes |
|---|----|----------------|-----------|-------|
| 1 | `type` | title-verb classifier | 0.85 | feature / bugfix / refactor / chore / docs |
| 2 | `title` | user-input (description arg) | 1.0 | always answered or pre-filled from arg |
| 3 | `issue_attach` | `--issue` flag presence | 1.0 if flag, else 0.7 | new vs attach to existing |
| 4 | `slug` | title kebab-converter | 0.95 | confirm only if collision in `.worktrees/` |
| 5 | `system` | path/keyword classifier (Phase 1.3) | 0.85 | DOTS / CDS / SA / UACS / Utils / MISIS / cross |
| 6 | `size` | complexity classifier (Phase 0.2) | 0.80 | XS / S / M / L / XL |
| 7 | `f_thread` | tier mapping + flags | 0.90 if flag, else 0.75 | on / off / N agents |
| 8 | `acceptance_criteria` | codebase-scan + auto-suggest | 0.40 | always asked unless `--ac=` flag pre-fills |
| 9 | `impl_hints` | codebase-scan edge cases | 0.50 | optional; `[skip-this]` allowed |
| 10 | `auto_preview_and_spawn` | flags | 0.95 | both default on; `--no-preview` / `--no-spawn` flip |

**ACCESS-mode checklist (3 items):**

| # | id | recommend-from | threshold | notes |
|---|----|----------------|-----------|-------|
| 1 | `which_worktree` | mode-detection match score | 0.90 | only asked if input ambiguously matched ≥2 worktrees |
| 2 | `action` | lifecycle-state derivation | 0.85 | continue / review / extend / targeted / ship — derived from task status, criteria progress, PR state |
| 3 | `targeted_instructions` | user-input | 0.0 | only present if action ∈ {extend, targeted}; always asked when present |

Flags pre-fill items at confidence 1.0:
- `--issue 123` → `issue_attach=existing-123`, confidence 1.0
- `--no-issue` → `issue_attach=skip`
- `--fusion` / `--f N` → `f_thread=on/N`
- `--nf` → `f_thread=off`
- `--setup-only` → `auto_preview_and_spawn=skip` (and Phase 5+ skipped)
- `--no-spawn` / `--no-spawn-tab` → adjust `auto_preview_and_spawn`
- `--review` (ACCESS) → `action=review`
- `--info` (ACCESS) → returns inventory only; no checklist invoked

### 0.4 Invoke grill-me

Build the args block per the schema in `.kris/context/grill-me-checklist-mode.md` and invoke `/grill-me`. Output path is the session scratch directory:

```bash
SESSION_ID="${ISSUE_NUM:-pending}-${SLUG:-unnamed}"
SCRATCH_DIR="${GIT_ROOT}/.kris/scratch/wt-session/${SESSION_ID}"
mkdir -p "$SCRATCH_DIR"
DECISIONS_FILE="${SCRATCH_DIR}/decisions.json"
```

Compose the args:

```text
<checklist mode="bounded" soft-cap="7" hard-cap="12">
  <item id="type" threshold="0.85" recommend-from="title-verb">…</item>
  …
</checklist>
<exit-tokens>go, ship-it, enough, done</exit-tokens>
<eli5>auto-subtitle</eli5>
<output>{DECISIONS_FILE}</output>
```

Invoke via the Skill tool: `Skill(skill="grill-me", args="<above block>")`.

Grill-me runs the bounded interview and writes `decisions.json`. Per the bounded-mode protocol, grill-me does NOT print a closing banner or wait for the orchestrator to acknowledge — it returns control silently the instant the file is written.

> **HANDOFF DIRECTIVE.** The moment the `Skill(grill-me, …)` call returns, the orchestrator's IMMEDIATE next tool call MUST be `Read("$DECISIONS_FILE")` (Phase 0.5). Do NOT print a summary. Do NOT end the turn. Do NOT wait. The Skill returning IS the trigger to continue.

### 0.5 Consume decisions.json

Read `decisions.json`. Hydrate orchestrator variables from the items array:

```bash
TASK_TYPE=$(jq -r '.items[] | select(.id=="type") | .value' "$DECISIONS_FILE")
TITLE=$(jq -r '.items[] | select(.id=="title") | .value' "$DECISIONS_FILE")
SLUG=$(jq -r '.items[] | select(.id=="slug") | .value' "$DECISIONS_FILE")
SYSTEM=$(jq -r '.items[] | select(.id=="system") | .value' "$DECISIONS_FILE")
SIZE=$(jq -r '.items[] | select(.id=="size") | .value' "$DECISIONS_FILE")
F_THREAD=$(jq -r '.items[] | select(.id=="f_thread") | .value' "$DECISIONS_FILE")
# … etc.
```

Every downstream phase reads from `decisions.json` only. **Do not re-prompt the user. Do not re-derive these values.**

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

> **You arrived here directly from Phase 0.5 with no pause.** If you find yourself reading this section as part of a fresh turn (i.e., the previous turn ended after Phase 0.5), that is the bug the NO-PAUSE DIRECTIVE was added to prevent. Re-read Phase 0's directive box and continue making tool calls without ending turns.
>
> Most decisions in this phase are already in `decisions.json` from grill-me. Phase 1 sub-sections below describe the underlying classifiers; in practice you READ from `decisions.json` rather than re-running them, unless an item was marked `source: "default"` (i.e., grill-me hit the soft-cap and applied a default — re-run the classifier on those items only).

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

> **Ordering is intentional.** `/issue` runs AFTER Phase 0 pre-flight + Phase 1 analysis (so the title inherits the analyzed type/slug/system and codebase-scan context) but BEFORE Phase 4.5 f-thread planning (so the issue # is available for the session dir, worktree, branch, and every downstream name). If planning later reveals a sharper title, `/rename` is the escape hatch — do not move `/issue` later in the flow.

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

1. **Worktree:** `${WORKTREE_PATH}/TASK.md` - Working copy for the sub-agent (inside `backend/`)
2. **Pending:** `${GIT_ROOT}/.kris/tasks/pending/{date}_{issue#}-{slug}.md` - Central tracking (in main backend)

**IMPORTANT:** Task tracking files go to `${GIT_ROOT}/.kris/tasks/` (the main repo's `.kris/tasks/`), NOT the repo root's `.kris/tasks/` and NOT inside the worktree's `.kris/tasks/`.

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
TASKS_DIR="${GIT_ROOT}/.kris/tasks"               # Task tracking lives in .kris/tasks/

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
WORKTREE_BACKEND="${WORKTREE_PATH}"           # Sub-agent working directory
```

**IMPORTANT safeguards:**
- **ALWAYS resolve `$GIT_ROOT` via `git rev-parse --show-toplevel`** — never hardcode a folder name, never use `pwd`, never use `../..`. The diagram above is structural; the actual folder name depends on the machine.
- **NEVER use `../` relative paths** — always compute from `$GIT_ROOT`
- **NEVER create `.kris/` at worktree root** — it belongs inside `backend/` (and is part of the repo, so it's already there in the worktree)
- **Task files go to `${GIT_ROOT}/.kris/tasks/`** (backend dir), NOT `${WORKTREE_PATH}/.kris/tasks/`
- **The sub-agent's cwd is `${WORKTREE_PATH}`** (the `backend/` subfolder), NOT the worktree root
- **TASK.md is created in `${WORKTREE_PATH}/TASK.md`** (where the sub-agent runs)

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
WORKTREE_BACKEND="${WORKTREE_PATH}"

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

Write the task file template (from Phase 3) into `${WORKTREE_PATH}/TASK.md` (where the sub-agent runs).

**Do NOT create TASK.md at the worktree root** — it must be inside `backend/`.

### 4.7 Initialize wt-state Directory (Self-Heal Layer)

Every worktree carries a `.kris/wt-state/` directory containing four markdown files the executor writes to as it works. These files are the **self-heal layer** — when a session is wiped (BSOD, power outage, hardware degradation), the next `/wt <issue#>` invocation reads them to recover intent, decisions, and verification trust without losing the previous agent's reasoning.

The directory is gitignored (see `backend/.gitignore` → `/.kris/wt-state/`) so per-session state never pollutes the repo.

```bash
WT_STATE_DIR="${WORKTREE_PATH}/.kris/wt-state"
WT_STATE_TEMPLATES="${GIT_ROOT}/.kris/templates/wt-state"

mkdir -p "$WT_STATE_DIR"
for f in journal last-action verification blockers; do
  cp "${WT_STATE_TEMPLATES}/${f}.md.template" "${WT_STATE_DIR}/${f}.md"
done
```

The four files (`journal.md`, `last-action.md`, `verification.md`, `blockers.md`) start as copies of their templates — the templates double as the runtime contract documentation, so the executor reads format + anti-patterns inline. The executor's write protocol is documented in `.kris/templates/wt/executor.md` → "Self-heal protocol" section (trigger → action table).

**Read path** (Access mode A.2.8 / A.3 / A.4 / A.5.1) detects stale state by checking `last-action.md` mtime; see Access mode below for the full recovery flow.

**Cross-worktree integration:** `/fix` Phase 9.5 scans peer worktrees' `blockers.md` files for references to the just-shipped issue and surfaces resolved dependencies.

---

## Phase 4.5.0: Session Workspace (Brief File)

> **Why this phase exists:** Subagent prompts used to inline ~1k tokens of template + context each (3 planners + 1 executor + 1 reviewer ≈ 5k tokens of orchestrator-side duplication per run). Writing the task context to a file once, and having each subagent `Read` it, eliminates that duplication. Spawn prompts shrink from ~1000 tokens to ~50. See `.kris/context/parallel-agents.md` for the session-dir layout and writer/reader contract.

### 4.5.0.1 Create session workspace

```bash
SESSION_ID="${ISSUE_NUM}-${SLUG}"                       # e.g. 287-add-payee-autocomplete
SESSION_DIR="${GIT_ROOT}/.kris/wt-sessions/${SESSION_ID}"
PLANS_DIR="${SESSION_DIR}/plans"

mkdir -p "$PLANS_DIR"
```

The session dir lives under `${GIT_ROOT}/.kris/wt-sessions/` (gitignored) — **not** inside the worktree, so the orchestrator and all subagents resolve the same absolute path.

### 4.5.0.2 Write `brief.md`

Follow `.kris/templates/wt/brief-template.md` for the required structure. Populate from pre-flight + analysis:

- **Identity / Worktree sections:** from Phase 1 analysis + Phase 4 worktree creation
- **Problem / Use Case & Proposed Solution:** from the issue body
- **Acceptance Criteria:** from the issue body (and any criteria added via auto-suggest adoption)
- **Files in Scope / Integration Points / Edge Cases:** from Phase 0.1 codebase scan
- **Patterns to Follow:** applicable `.kris/context/*.md` files (admin-panel, frontend-patterns, routes, compliance, modularity, etc. per CLAUDE.md trigger table)
- **Scope Boundaries:** explicit MODIFY list + the canonical "DO NOT MODIFY app/**, database/**" from CLAUDE.md
- **Adopted Suggestions / Clarifications:** from Phase 0.2 and 0.4
- **Compliance Notes:** only when the work touches PII-adjacent fields (see `.kris/context/compliance.md`)

**Hard rule — no placeholders.** If a section has no content, remove it. `{TBD}` blocks the downstream agents.

Write the final content to `${SESSION_DIR}/brief.md`.

### 4.5.0.3 Path constants for downstream phases

Every `/wt` phase after this point references session files by these paths:

```
BRIEF_PATH="${SESSION_DIR}/brief.md"
FINAL_PLAN_PATH="${SESSION_DIR}/final-plan.md"
REVIEW_PATH="${SESSION_DIR}/review.md"
PLAN_N_PATH="${PLANS_DIR}/plan-{N}.md"                  # template, N assigned per planner
```

Subagents will also reference these paths (the orchestrator passes them in the spawn prompt, or the subagent reads them from `SESSION_DIR` which is communicated once).

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

### 4.5.2 Planning Agent Template (file-based, not inlined)

The planning agent instructions live in `.kris/templates/wt/planner.md`. The orchestrator does **not** inline the template into each spawn prompt. Each planner reads the template plus the session brief — the spawn prompt is just pointers.

**Why:** 3 planners × ~500 tokens of inlined template = ~1.5k tokens of orchestrator-side prompt duplication per run. File-based reference brings each spawn prompt down to ~50 tokens.

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

Launch all N planning agents **in parallel in a single message**. Each spawn prompt is a tiny pointer — the planner reads the template and brief itself:

```
# Launch all N in ONE message with N parallel Agent tool calls.
# Each planner's spawn prompt follows this shape:

Agent(
  description: "plan-{N}: {slug} (#{ISSUE_NUM})",
  model: "sonnet",                          # planners run on Sonnet
  subagent_type: "general-purpose",
  prompt: "
You are f-thread planner {N} of {TOTAL_N} for session {SESSION_ID}.

Read these files, in order, before doing anything else:
  1. .kris/templates/wt/planner.md              — your role, output format, and rules
  2. .kris/wt-sessions/{SESSION_ID}/brief.md    — the task brief

Worktree backend dir (your cwd for codebase exploration):
  {WORKTREE_BACKEND}

Write your plan to:
  .kris/wt-sessions/{SESSION_ID}/plans/plan-{N}.md

Do not write anywhere else.
"
)
```

**CRITICAL:** All N must be launched in a **single message** (parallel tool calls). Do NOT launch sequentially.

**Model param:** always pass `model: "sonnet"` for planners (see `.kris/templates/wt/planner.md`).

### 4.5.4 Fusion (File-Based)

All planners write their outputs to `.kris/wt-sessions/{SESSION_ID}/plans/plan-{N}.md`. Fusion reads those files and writes a single `final-plan.md`. **Who does the fusion depends on N:**

- **N = 2** — orchestrator fuses inline. Reading two focused plan files is cheap, and spawning a separate agent adds indirection without saving much. Write `final-plan.md` directly.
- **N ≥ 3** — spawn a dedicated fusion agent. This keeps the orchestrator's context clean on heavier runs (3+ plan files total ~4–6k tokens that would otherwise persist in the orchestrator's context).

#### N = 2 (inline fusion)

The orchestrator reads `plans/plan-1.md` and `plans/plan-2.md`, applies the fusion process from `.kris/templates/wt/fusion.md`, and writes `.kris/wt-sessions/{SESSION_ID}/final-plan.md` following the structure in that template.

#### N ≥ 3 (dedicated fusion agent)

```
Agent(
  description: "fuse: {slug} (#{ISSUE_NUM})",
  model: "opus",                            # fusion is highest-leverage reasoning
  subagent_type: "general-purpose",
  prompt: "
You are the fusion agent for session {SESSION_ID}.

Read these files, in order, before doing anything else:
  1. .kris/templates/wt/fusion.md                  — your role, process, output format
  2. .kris/wt-sessions/{SESSION_ID}/brief.md       — the task brief
  3. .kris/wt-sessions/{SESSION_ID}/plans/*.md     — every planner's output ({N} files)

Write the consolidated plan to:
  .kris/wt-sessions/{SESSION_ID}/final-plan.md

Do not modify any other files.
"
)
```

### 4.5.5 Plan Handoff

The executor (Phase 5) reads `final-plan.md` as its authoritative implementation spec. It does **not** receive the raw plan-N files — those are kept only in the session dir for later inspection (review agent may reference them if a disagreement matters).

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

### 5.2 Agent Launch (file-based brief)

Launch the executor with a slim pointer prompt. The agent reads `.kris/templates/wt/executor.md` for role/rules, `brief.md` for task context, and `final-plan.md` for the authoritative implementation spec. **Foreground** — the orchestrator waits for completion before launching the reviewer.

```
Agent(
  description: "impl: {slug} (#{ISSUE_NUM})",
  model: "opus",                            # implementation correctness > cost
  subagent_type: "general-purpose",
  prompt: "
You are the implementation agent for session {SESSION_ID}.

Read these files, in order, before doing anything else:
  1. .kris/templates/wt/executor.md                 — your role, rules, quality gates
  2. .kris/wt-sessions/{SESSION_ID}/brief.md        — task brief, scope boundaries
  3. .kris/wt-sessions/{SESSION_ID}/final-plan.md   — authoritative implementation spec (if f-thread ran)
  4. CLAUDE.md                                       — project rules
  5. TASK.md in your cwd                            — progress tracker

Your working directory: {WORKTREE_BACKEND}
Branch: {BRANCH_NAME}

Execute final-plan.md verbatim. Check off acceptance criteria in TASK.md as you go.
Create the completion marker at the worktree root when done (not at cwd): ../.worktree-complete
Do not commit or push — /fix handles that.
"
)
```

**If f-thread did NOT run** (Tier 0 or `--nf`): write `final-plan.md` directly from the brief's Proposed Solution + Files in Scope + Edge Cases. The executor should never see a missing `final-plan.md`.

### 5.3 Brief Quality Gate (pre-launch check)

Before spawning the executor, the orchestrator verifies:

- [ ] `brief.md` exists and contains no `{TBD}` / `{To be determined}` placeholders
- [ ] `brief.md` → "Files in Scope" names at least one real file (path resolves)
- [ ] `brief.md` → "Scope Boundaries" has explicit MODIFY list
- [ ] `final-plan.md` exists (either from fusion, or written from the brief for non-f-thread runs)
- [ ] `final-plan.md` → "Implementation Steps" is ordered and each step names file paths

If any check fails, go back to the appropriate phase (0 → research gap, 4.5 → re-fuse, 5 → write final-plan from brief) and resolve BEFORE launching the executor.

---

## Phase 6: Review Agent (CRITICAL — Unbiased Verification)

After the implementation agent completes, launch a **separate, independent review agent**. This agent has NO knowledge of the implementation agent's thought process — it only sees the code changes and the original requirements. This eliminates bias.

### 6.1 Review Agent Launch (file-based)

Launch the reviewer with a slim pointer prompt. The agent reads `.kris/templates/wt/reviewer.md` for role/axes/rubric, plus the brief and final plan.

```
Agent(
  description: "review: {slug} (#{ISSUE_NUM})",
  model: "opus",                            # independent critique quality is load-bearing
  subagent_type: "general-purpose",
  prompt: "
You are the review agent for session {SESSION_ID}.

Read these files, in order, before doing anything else:
  1. .kris/templates/wt/reviewer.md                 — your role, review axes, severity rubric, output format
  2. .kris/wt-sessions/{SESSION_ID}/brief.md        — original requirements and scope boundaries
  3. .kris/wt-sessions/{SESSION_ID}/final-plan.md   — what the implementer was supposed to do
  4. CLAUDE.md                                       — rules to verify against

Worktree backend dir (for git diff and reading modified files):
  {WORKTREE_BACKEND}

Write your review to:
  .kris/wt-sessions/{SESSION_ID}/review.md

The first section MUST be the Severity Summary block (see reviewer.md).
"
)
```

**Run in FOREGROUND** — the orchestrator reads the severity summary to decide on iteration.

### 6.2 Orchestrator Processes Review

After the review agent returns, the orchestrator reads **only the Severity Summary block** from `review.md` (top of file, fixed format) to decide. It does **not** re-load the full findings into context unless an iteration executor is about to be spawned:

1. Read `.kris/wt-sessions/{SESSION_ID}/review.md` and parse the first section:
   - `VERDICT:` → PASS / PASS_WITH_NOTES / FAIL
   - `CRITICAL:` / `HIGH:` / `MEDIUM:` / `LOW:` counts
   - `Criteria met:` x/total
2. Decide next action per iteration rules (Phase 7).
3. If iterating, the iteration executor reads the full `review.md` itself — orchestrator does not re-quote findings into the spawn prompt.

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

### 7.3 Iteration Executor (file-based, reuses executor template)

For iterations 2+, spawn the same executor template in **iteration mode**. The agent reads the review findings from `review.md` itself — the orchestrator does not re-quote them into the spawn prompt.

```
Agent(
  description: "fix-iter-{N}: {slug} (#{ISSUE_NUM})",
  model: "opus",
  subagent_type: "general-purpose",
  prompt: "
You are the iteration-{ITERATION} executor for session {SESSION_ID}. ITERATION_MODE=true.

Read these files, in order, before doing anything else:
  1. .kris/templates/wt/executor.md                 — note the 'Iteration Mode' section
  2. .kris/wt-sessions/{SESSION_ID}/brief.md        — task brief
  3. .kris/wt-sessions/{SESSION_ID}/final-plan.md   — original implementation spec
  4. .kris/wt-sessions/{SESSION_ID}/review.md       — findings you must fix

Iteration severity filter:
  - Iteration 2: fix ALL findings (P0 + P1 + P2 + P3)
  - Iteration 3+: fix ONLY P0 findings

Working directory: {WORKTREE_BACKEND}

Fix the listed findings, re-run lint/format/tests, update TASK.md.
Do NOT recreate the completion marker (it already exists from iteration 1).
Do NOT refactor unrelated code.
"
)
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

## Phase 7.9: Auto-Preview (on green review)

Before writing the final report, surface the worktree's changes into the main repo so the user can immediately build and test — without committing/pushing. `/fix` remains the explicit step for commit + PR.

### 7.9.1 Should we preview?

Run `/preview` only when the review verdict permits it:

- **PASS** → run `/preview`
- **PASS_WITH_NOTES** → run `/preview` (notes are non-blocking)
- **FAIL** (iteration loop exited with remaining P0 findings) → skip preview; tell the user in the final report that preview was skipped and why

Also skip when:
- `--setup-only` is active (no implementation happened)
- `--no-review` is active AND user supplied `--no-preview` (belt-and-suspenders for intentionally terse runs)

### 7.9.2 Cross-session preview lock

Parallel `/wt` sessions can land here simultaneously — both would try to merge their feature branch into the main repo's `dev`. Use the lockfile at `${GIT_ROOT}/.kris/wt-sessions/.preview.lock` (see `.kris/context/parallel-agents.md` → "/preview Lock Protocol").

```bash
LOCK_FILE="${GIT_ROOT}/.kris/wt-sessions/.preview.lock"
NOW=$(date +%s)

if [ -f "$LOCK_FILE" ]; then
  LOCK_STARTED=$(jq -r '.started_at_epoch' "$LOCK_FILE" 2>/dev/null || echo 0)
  LOCK_OWNER=$(jq -r '.session_id' "$LOCK_FILE" 2>/dev/null || echo unknown)
  LOCK_AGE=$((NOW - LOCK_STARTED))

  if [ "$LOCK_OWNER" != "$SESSION_ID" ] && [ "$LOCK_AGE" -lt 600 ]; then
    # Fresh lock held by another session — abort preview, continue to report
    echo "Preview lock held by session $LOCK_OWNER (age ${LOCK_AGE}s). Skipping preview."
    PREVIEW_STATUS="skipped-lock"
  else
    # Stale or our own lock — overwrite
    [ "$LOCK_AGE" -ge 600 ] && echo "Stale preview lock (${LOCK_AGE}s old) — overwriting."
    printf '{"session_id":"%s","started_at_epoch":%d}\n' "$SESSION_ID" "$NOW" > "$LOCK_FILE"
    PREVIEW_STATUS="ok"
  fi
else
  printf '{"session_id":"%s","started_at_epoch":%d}\n' "$SESSION_ID" "$NOW" > "$LOCK_FILE"
  PREVIEW_STATUS="ok"
fi
```

Always release the lock after preview completes or aborts, even on failure. Use a trap or explicit cleanup before every exit path:

```bash
release_preview_lock() {
  [ -f "$LOCK_FILE" ] && CURRENT_OWNER=$(jq -r '.session_id' "$LOCK_FILE" 2>/dev/null || echo "")
  [ "$CURRENT_OWNER" = "$SESSION_ID" ] && rm -f "$LOCK_FILE"
}
trap release_preview_lock EXIT
```

### 7.9.3 Inline preview (do NOT invoke /preview as a slash-command)

The autonomy contract forbids invoking sub-skills that may prompt the user. `/preview`, when invoked as a slash-command, can prompt for branch-name conflicts, merge strategy, dev-server port, and other decisions. **Inline its decision logic here instead.** The standalone `/preview` skill remains available for direct user invocation; this orchestrator path does not call it.

```bash
if [ "$PREVIEW_STATUS" = "ok" ]; then
  # 1. Auto-commit any uncommitted changes on the worktree's feature branch.
  if [ -n "$(git -C "$WORKTREE_PATH" status --porcelain)" ]; then
    git -C "$WORKTREE_PATH" add -A
    git -C "$WORKTREE_PATH" commit -m "wip: pre-preview snapshot ($SESSION_ID)" \
      || fail_nonfatal "phase-7.9-preview" "preview-precommit-failed" \
                      "pre-preview commit failed" \
                      "/fix tab: review uncommitted state and decide whether to amend"
  fi

  # 2. Stash-aware merge of feature → main repo's dev.
  # See feedback_commit-task-files-before-stash.md and stash-aware-sync.md.
  MAIN_DIRTY=0
  if [ -n "$(git -C "$MAIN_BACKEND" status --porcelain)" ]; then
    MAIN_DIRTY=1
    # Commit untracked task files first (per memory feedback_commit-task-files-before-stash).
    git -C "$MAIN_BACKEND" add ".kris/tasks/" ".kris/prompts/" 2>/dev/null
    git -C "$MAIN_BACKEND" commit -m "chore(tasks): pre-preview snapshot for #${ISSUE_NUM}" \
      --allow-empty 2>/dev/null
    git -C "$MAIN_BACKEND" stash push -u -m "wt-preview-#${ISSUE_NUM}-$$" \
      || fail_catastrophic "preview-stash-failed" "could not stash main repo before merge"
  fi

  # 3. Merge feature branch with auto-ours strategy (preserves the worktree's intent).
  CURRENT_BRANCH=$(git -C "$MAIN_BACKEND" branch --show-current)
  if [ "$CURRENT_BRANCH" != "dev" ]; then
    git -C "$MAIN_BACKEND" checkout dev \
      || fail_catastrophic "preview-checkout-dev-failed" "cannot checkout dev for preview merge"
  fi

    --dir="$MAIN_BACKEND" \
    --strategy=auto-theirs \
    --skill=worktree-preview \
    --message="preview: ${TITLE} (#${ISSUE_NUM})" \
    || fail_nonfatal "phase-7.9-preview" "preview-merge-conflict" \
                    "feature merge into dev hit conflicts beyond auto-theirs" \
                    "/fix tab: inspect conflicted files and resolve manually before pushing"

  # 4. Restore stash if we pushed one (and we're still on the dev branch).
  if [ "$MAIN_DIRTY" -eq 1 ]; then
    git -C "$MAIN_BACKEND" stash pop \
      || fail_catastrophic "preview-stash-pop-conflict" \
                          "stash pop conflict — preserved stash; manual triage required"
  fi

  # 5. Return to origin branch (per memory feedback_return-to-origin-branch).
  if [ "$CURRENT_BRANCH" != "dev" ] && [ -n "$CURRENT_BRANCH" ]; then
    git -C "$MAIN_BACKEND" checkout "$CURRENT_BRANCH" 2>/dev/null
  fi

  # 6. Build frontend assets so Apache can serve the preview.
  ( cd "$MAIN_BACKEND" && npm run build 2>&1 | tee "${SCRATCH_DIR}/preview-build.log" ) \
    || fail_nonfatal "phase-7.9-preview" "preview-build-failed" \
                    "npm run build failed" \
                    "/fix tab: investigate build errors before shipping"

  # 7. Mark preview state for downstream /fix to reconcile.
  printf '{"session_id":"%s","previewed_at":"%s","branch":"%s"}\n' \
    "$SESSION_ID" "$(date -u +%FT%TZ)" "$BRANCH_NAME" \
    > "${WORKTREE_PATH}/.preview-state"

  release_preview_lock
  PREVIEW_STATUS="ok"
fi
```

Record `PREVIEW_STATUS` for the final report. Failures during preview are non-catastrophic (the only catastrophic preview failure is a stash-pop conflict, handled above) — they fall through to `/fix` tab via `failures.json`.

### 7.9.4 What the user gets from preview

- Their feature is merged into the main repo's local `dev` — Apache can serve it immediately.
- `/preview` writes `.preview-state` in the worktree so `/fix` can later reconcile any tweaks the user makes directly in main.
- **No push happened.** `/fix` is still required to commit-with-proper-message, push, and open the PR.

---

## Phase 7.95: Auto-spawn /fix tab (on green review)

After preview, when the verdict permits, **automatically invoke `wt-spawn-claude.py`** to open an isolated Windows Terminal tab pinned to the worktree, running `/fix`. Until this phase existed, `/fix` was a manual "next step" prose block in Phase 8 — adding an extra round-trip per shipped feature for the most common path (green review). The spawn happens here so the orchestrator's main thread stays out of the worktree's branch (preserving the branch contract).

### 7.95.1 Should we auto-spawn?

Run the spawn only when:

- **Verdict is PASS or PASS_WITH_NOTES** (FAIL means there are remaining P0/P1 — user should review manually before shipping)
- **`--setup-only` is NOT active** (no implementation happened — nothing to ship)
- **`--no-spawn` is NOT set** (opt-out for users who want to inspect before shipping)
- **`--no-spawn-tab` is NOT set** (when set, run `/fix` headlessly via `claude --print` instead of opening a tab — see 7.95.5)
- **`--no-issue` is NOT active** OR an issue was created — the spawner needs `--issue N` and `--slug X` for the tab title and report-file path

If any condition fails, set `SPAWN_STATUS="skipped-<reason>"` and let Phase 8 surface the manual command instead. `--no-spawn-tab` does NOT skip the spawn entirely — it routes to the headless block below and sets `SPAWN_STATUS="skipped-flag-headless"`.

**Mutual exclusion:** if both `--no-spawn` and `--no-spawn-tab` are set, `--no-spawn` wins (skip the spawn entirely) and a warning is logged. They mean different things:
- `--no-spawn` — skip auto-spawn entirely; user runs `/fix` manually later
- `--no-spawn-tab` — auto-run `/fix` headlessly (no new tab, no NTFS lock, blocks orchestrator until done)

### 7.95.2 Invoke the spawner

```bash
SPAWN_STATUS="skipped-fail"   # default; overridden on the success path

if [ "$VERDICT" = "PASS" ] || [ "$VERDICT" = "PASS_WITH_NOTES" ]; then
  # Mutual exclusion: --no-spawn wins over --no-spawn-tab.
  if [ "$NO_SPAWN" = "true" ] && [ "$NO_SPAWN_TAB" = "true" ]; then
    echo "⚠️  Both --no-spawn and --no-spawn-tab set; --no-spawn takes precedence (skipping entirely)"
  fi

  if [ "$SETUP_ONLY" = "true" ] || [ "$NO_SPAWN" = "true" ]; then
    SPAWN_STATUS="skipped-flag"
  elif [ -z "$ISSUE_NUM" ] || [ -z "$SLUG" ]; then
    SPAWN_STATUS="skipped-no-issue"
  elif [ "$NO_SPAWN_TAB" = "true" ]; then
    # Headless route — see 7.95.5
    SPAWN_STATUS="skipped-flag-headless"
  else
    REPORT_PATH="${GIT_ROOT}/.kris/wt-sessions/${ISSUE_NUM}-${SLUG}/spawn-report.json"
      --issue "$ISSUE_NUM" \
      --slug "$SLUG" \
      --worktree "$WORKTREE_BACKEND" \
      --prompt "Run /fix" \
      --auto-report
    SPAWN_EXIT=$?
    if [ "$SPAWN_EXIT" -eq 0 ]; then
      SPAWN_STATUS="ok"
    else
      SPAWN_STATUS="failed"
    fi
  fi
fi
```

The spawner runs in a separate OS process, so any branch swap or parallel-agent activity in the orchestrator cannot reach its HEAD (this is the same isolation rationale captured in `feedback_branch-sanity-on-anomaly.md`).

### 7.95.3 Skip when…

| Condition | `SPAWN_STATUS` | Phase 8 behavior |
|-----------|----------------|------------------|
| `VERDICT == PASS` and all gates pass | `ok` | "Tab spawned. Report at `<path>`" |
| `VERDICT == FAIL` | `skipped-fail` | Print manual command + remediation prose (current behavior) |
| `--setup-only` or `--no-spawn` | `skipped-flag` | Print manual command (user opted out) |
| `--no-spawn-tab` | `skipped-flag-headless` | Ran headless subprocess instead of new tab (see 7.95.5) |
| `--no-issue` flow with no issue # | `skipped-no-issue` | Print manual command (spawner needs `--issue`) |
| Spawner exited non-zero | `failed` | Print manual command + spawn error context |

Record `SPAWN_STATUS` and `REPORT_PATH` for Phase 8.

### 7.95.4 What the user gets from the spawn

- A new Windows Terminal tab pinned to the worktree, with a fresh `claude --dangerously-skip-permissions` running `/fix`.
- The spawned `/fix` will commit (including the session-scoped backend sweep from #574), push, and open the PR targeting `dev`.
- A JSON status report at `${REPORT_PATH}` when the tab finishes — `cat` it or ask "what's the status of #${ISSUE_NUM}?" to see the outcome.
- The orchestrator's main thread stays on its starting branch — branch contract preserved.

### 7.95.5 Headless mode (`--no-spawn-tab`)

When `--no-spawn-tab` is set (and `--no-spawn` is not), run `/fix` synchronously via `claude --print` instead of opening a new tab. This eliminates the NTFS lock that holds spawned tabs open after `/fix` completes — useful when the user prefers determinism over visual progress feedback.

```bash
if [ "$SPAWN_STATUS" = "skipped-flag-headless" ]; then
  REPORT_PATH="${GIT_ROOT}/.kris/wt-sessions/${ISSUE_NUM}-${SLUG}/spawn-report.json"
    --headless \
    --issue "$ISSUE_NUM" \
    --slug "$SLUG" \
    --worktree "$WORKTREE_BACKEND" \
    --prompt "Run /fix" \
    --auto-report
  SPAWN_EXIT=$?
  if [ "$SPAWN_EXIT" -eq 0 ]; then
    SPAWN_STATUS="ok-headless"
  else
    SPAWN_STATUS="failed-headless"
  fi
fi
```

Trade-offs vs the tab-spawn default:

- **No NTFS lock** — process exits cleanly when `/fix` completes; `/wc` can remove the worktree immediately afterward.
- **Blocks the orchestrator** — `/wt` does not return until `/fix` finishes. Acceptable when the orchestrator is already at Phase 7.95 and has no further work.
- **No visual feedback** — the user can't watch progress live. The JSON report at `${REPORT_PATH}` is the sole record of outcome.
- **Permission-prompt failure mode is loud** — if `claude --print` ever ignores `--dangerously-skip-permissions` (no tty), the process hangs; user can `Ctrl+C` and re-run without `--no-spawn-tab`.

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

{IF remaining findings — read from .kris/wt-sessions/{SESSION_ID}/review.md:}
Remaining Findings (not fixed — non-critical)
   [P2] {title} — {file}:{line} — {description}
   [P3] {title} — {file}:{line} — {description}

Preview
   Status: {ok | skipped-lock | skipped-fail | failed}
   {IF ok:}       Changes merged into main repo's local dev. Apache can serve the feature now.
                  `.preview-state` written; /fix will auto-reconcile any tweaks you make in main.
   {IF skipped-lock:} Another session had the preview lock. Run /preview {ISSUE_NUM} manually when ready.
   {IF skipped-fail:} Review failed (remaining P0/P1). Fix manually before previewing.
   {IF failed:}    /preview errored — check output above. You may need to run it manually.

Session Workspace (kept for inspection; cleaned by /worktree-cleanup)
   .kris/wt-sessions/{SESSION_ID}/
     brief.md         task context
     plans/           {N} f-thread planning outputs
     final-plan.md    fused implementation spec
     review.md        reviewer findings

Spawn
   Status: {ok | ok-headless | skipped-fail | skipped-flag | skipped-flag-headless | skipped-no-issue | failed | failed-headless}
   {IF ok:}        Tab spawned. Report will land at .kris/wt-sessions/{SESSION_ID}/spawn-report.json when /fix completes.
                   `cat` that file or ask "what's the status of #{ISSUE_NUM}?" to see the outcome.
   {IF ok-headless:} Ran /fix headlessly — no tab opened, no NTFS lock. Report at .kris/wt-sessions/{SESSION_ID}/spawn-report.json (already written).
   {IF skipped-fail:}     Verdict was FAIL — review manually before shipping.
   {IF skipped-flag:}     --setup-only or --no-spawn — see manual command in Next Steps below.
   {IF skipped-flag-headless:} --no-spawn-tab routed to headless invocation (see ok-headless / failed-headless above).
   {IF skipped-no-issue:} --no-issue flow without an issue # — spawner requires it; see manual command below.
   {IF failed:}    Spawner errored — see output above. Run the manual command below.
   {IF failed-headless:} Headless /fix errored — see output above. Re-run without --no-spawn-tab to fall back to tab mode.

Next Steps
   1. Test the feature via Apache (preview is live).
   2. /compact  — frees Phase 0 codebase scan + iteration-loop subagent reports (artifacts persist on disk in .kris/wt-sessions/{SESSION_ID}/)
   {IF SPAWN_STATUS != "ok" AND SPAWN_STATUS != "ok-headless":}
      — opens an isolated Windows Terminal tab pinned to the worktree. The spawned Claude runs /fix (commit, push, open PR targeting dev) without touching the main repo's HEAD. Result lands at .kris/wt-sessions/{SESSION_ID}/spawn-report.json — `cat` that file or ask "what's the status of #{ISSUE_NUM}?" to see the outcome.
   {ELSE IF SPAWN_STATUS == "ok-headless":}
   3. (Spawn ran headless — /fix already complete; see Spawn block above for outcome.)
   {ELSE:}
   3. (Spawn already in flight — see Spawn block above.)
   {END IF}
   4. After PR merged: /worktree-cleanup {ISSUE_NUM}
---
```

> **Why `/compact` before the spawned `/fix`:** /wt's main thread accumulates the Phase 0 codebase scan transcript and one summary block per Phase 7 iteration (impl + review subagent reports). Once Phase 8 prints, all of that is preserved on disk — `brief.md`, `final-plan.md`, `review.md`, the worktree diff itself. The spawned `/fix` only needs the diff, so compacting here drops the scan + iteration noise while keeping the task summary. Skip if ending the session.

> **Why the tab-spawn instead of `cd ../.worktrees/... && /fix`:** the manual-cd handoff puts the orchestrator's process on the worktree's branch, which is the root cause of the phantom-push and accidental-worktree-modification incident classes. `wt-spawn-claude.py` launches a fresh `claude --dangerously-skip-permissions` in a new Windows Terminal tab with `cwd` pinned to the worktree path. The spawned Claude runs in a separate OS process, so any branch swap or parallel-agent activity in the orchestrator cannot reach its HEAD. Falls back to `Start-Process pwsh` if Windows Terminal is missing. See memory: `feedback_branch-sanity-on-anomaly.md`.

### 8.2 Verdict-Specific Guidance

| Verdict | Preview ran? | User Guidance |
|---------|--------------|--------------|
| PASS | Yes | Preview is live in main repo — test, then `/fix` to commit + open PR. |
| PASS_WITH_NOTES | Yes | Preview is live; minor notes listed above. Review at your discretion, then `/fix`. |
| FAIL (max iterations) | No | Remaining P0/P1 in `review.md`. Fix manually, or re-run `/worktree {ISSUE_NUM} "specific instructions"` to target them. Preview was skipped to avoid staging broken code. |
| Preview locked by another session | — | Another session had the preview lock. Run `/preview {ISSUE_NUM}` manually when you're ready. |

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

### `--no-preview`

Skip Phase 7.9 auto-preview. Useful when another session already has the preview lock and you don't want `/wt` to even try, or when you explicitly want to inspect the worktree before staging changes into the main repo:

```bash
/worktree --no-preview "experimental refactor to inspect first"
```

Without this flag, preview runs automatically on PASS / PASS_WITH_NOTES and is skipped automatically on FAIL.

### `--no-spawn`

Skip Phase 7.95 auto-spawn of the `/fix` tab. Useful when you want to inspect the worktree's diff or run additional verification before shipping:

```bash
/worktree --no-spawn "implement risky migration — review before shipping"
```

Without this flag, the spawn runs automatically on PASS / PASS_WITH_NOTES (provided an issue # is available and `--setup-only` is not active) and is skipped automatically on FAIL. Phase 8 surfaces the manual `wt-spawn-claude.py` invocation as a Next Step when the auto-spawn is skipped, so you can run it yourself when ready.

### `--no-spawn-tab`

Run `/fix` headlessly (no new tab) — automated, blocks until complete, no NTFS lock. Distinct from `--no-spawn` which skips automation entirely:

```bash
/worktree --no-spawn-tab "ship and clean up immediately, no manual tab close"
```

Phase 7.95.5 invokes `wt-spawn-claude.py --headless`, which runs `claude --dangerously-skip-permissions --print "/fix"` synchronously in the orchestrator's process. The orchestrator blocks until `/fix` exits; the JSON status report still lands at `<MAIN_BACKEND>/.kris/wt-sessions/<id>/spawn-report.json`. Because the headless process exits cleanly when `/fix` finishes, `/wc` can remove the worktree immediately afterward — no spawned-tab handle holding the directory open.

**`--no-spawn` vs `--no-spawn-tab`:** `--no-spawn` skips the auto-spawn entirely (you run `/fix` manually later). `--no-spawn-tab` still runs `/fix` automatically, just without a new tab. Mutual exclusion: if both are set, `--no-spawn` wins (skip entirely) and a warning is logged.

**Failure mode:** if `claude --print` ever ignores `--dangerously-skip-permissions` (no tty), the headless process hangs. `Ctrl+C` and re-run without `--no-spawn-tab` to fall back to tab mode.

### `--interactive` (REMOVED)

Removed in favor of the Phase 0 grill-me bounded interview. The single-front-door model means every `/wt` invocation gets the relentless-one-question-at-a-time interview by default — but bounded by the soft cap (7 questions), with `[ship-it]` available on every prompt to accept defaults and exit. There is no longer a separate "interactive" mode.

If you genuinely want the old four-checkpoint flow, use the natural escape hatch: answer `?` on grill-me's question to get an ELI5 deep-dive, then answer it, then optionally type `change <key>` later if you spot a wrong auto-decision.

### Flags as pre-answers (mental model)

In the grill-me-front-door model, all remaining flags act as **pre-answers** to specific checklist items, not as bypasses of grill-me itself. Pre-answered items get confidence 1.0 and never become questions; they appear in the inline running tally with a `*` suffix to indicate flag-sourced.

| Flag | Pre-answers item | Effect on grilling |
|------|------------------|--------------------|
| `--issue 123` | `issue_attach=existing-123` | One fewer question; title/body sourced from GitHub issue |
| `--no-issue` | `issue_attach=skip` | Skip issue-creation phase; reduce one question |
| `--fusion` / `--f N` | `f_thread=on` (or N agents) | One fewer question; F-Thread runs regardless of size |
| `--nf` | `f_thread=off` | One fewer question; direct-impl path |
| `--setup-only` | `auto_preview_and_spawn=skip` | One fewer question; Phases 5+ skipped after worktree creation |
| `--no-spawn` / `--no-spawn-tab` | `auto_preview_and_spawn` variants | Adjust spawn behavior (see Phase 7.95) |
| `--review` (ACCESS) | `action=review` | ACCESS-mode action item resolved |
| `--info` (ACCESS) | (none) | Bypasses checklist entirely; prints inventory and exits |
| `--new` | mode=create | Forces CREATE branch in mode detection |

A `/wt --info 287` skips grill-me entirely (read-only). All other invocations enter grill-me; flags merely shrink the question budget.

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
| `/do` | Alternative manual flow — used inside worktree when `--setup-only`. Reads session brief from `.kris/wt-sessions/<id>/brief.md` when present, falls back to `TASK.md`. |
| `/preview` | Auto-invoked at Phase 7.9 on green review. Accepts `--session <id>` to read brief for the commit summary. Gated by `.kris/wt-sessions/.preview.lock` for cross-session safety. |
| `/fix` | **User-triggered** after preview testing — validates, commits, pushes, creates PR. Reads session brief for acceptance-criteria text when available. |
| `/worktree-cleanup` | Removes worktree after PR merged AND its matching `.kris/wt-sessions/<id>/` session dir. |
| `/issue` | Shares issue creation logic (DRY). Ordering (Phase 2, after pre-flight, before planning) is intentional — see Phase 2 header note. |

### Session-dir contract (summary)

The `/wt` orchestrator writes `brief.md` once. Subagents `Read` it (and the role template in `.kris/templates/wt/`) rather than receiving inlined context in their spawn prompts. This saves ~4–5k tokens per `/wt` run. Full contract in `.kris/context/parallel-agents.md` → "/wt Session Directory Layout".

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
