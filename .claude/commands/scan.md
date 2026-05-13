# /scan - Survey Active Work Areas

Analyze all areas currently being worked on — across worktrees, the dev repo, pending tasks, and open PRs — and present a unified dashboard of in-flight work.

> **Philosophy:**
> - Read-only command — never modifies files, commits, or pushes
> - Surfaces forgotten work, stale worktrees, and orphaned changes
> - Helps you decide what to pick up next or what needs attention
> - Works from the main repo (recommended) but also works inside a worktree

---

## Quick Start

```bash
/scan                    # Full scan: worktrees + dev changes + tasks + PRs
/scan --worktrees        # Only scan active worktrees
/scan --dev              # Only scan uncommitted changes in dev repo
/scan --tasks            # Only scan pending/running task files
/scan --prs              # Only scan open GitHub PRs
/scan --stale            # Highlight stale/forgotten work (no commits in 3+ days)
```

---

## How It Works

```
+-------------------------------------------------------+
|                       /scan                            |
+-------------------------------------------------------+
|                                                       |
|  Phase 1: Detect Environment                          |
|     - Am I in the main repo or a worktree?            |
|     - Resolve main repo path for all scans            |
|                                                       |
|  Phase 2: Scan Worktrees                              |
|     - List all git worktrees                          |
|     - For each: branch, issue#, last commit, status   |
|     - Detect completion markers (.worktree-complete)  |
|     - Flag stale worktrees (no recent commits)        |
|                                                       |
|  Phase 3: Scan Dev Repo Changes                       |
|     - Uncommitted changes (staged + unstaged)         |
|     - Untracked files                                 |
|     - Group by module/area                            |
|     - Identify coherent change groups                 |
|                                                       |
|  Phase 4: Scan Task Files                             |
|     - .kris/tasks/pending/ (backlog)                  |
|     - .kris/tasks/running/ (in progress)              |
|     - Cross-reference with worktrees and branches     |
|                                                       |
|  Phase 5: Scan GitHub PRs & Issues                    |
|     - Open PRs targeting dev                          |
|     - Open issues assigned or recently created        |
|     - Cross-reference with worktrees                  |
|                                                       |
|  Phase 6: Generate Dashboard                          |
|     - Unified view of all in-flight work              |
|     - Actionable recommendations                      |
|     - Stale work warnings                             |
|                                                       |
+-------------------------------------------------------+
```

---

## Phase 1: Detect Environment

### 1.1 Resolve Main Repo Path

```bash
# Detect if we're in a worktree
WORKTREE_INFO=$(git rev-parse --git-common-dir 2>/dev/null)

if [[ "$WORKTREE_INFO" != ".git" && -d "$WORKTREE_INFO" ]]; then
  IN_WORKTREE=true
  # Resolve to the main repo's backend directory
  MAIN_REPO=$(dirname "$WORKTREE_INFO")
  BACKEND_DIR="${MAIN_REPO}/backend"
else
  IN_WORKTREE=false
  BACKEND_DIR=$(pwd)
  MAIN_REPO=$(dirname "$BACKEND_DIR")
fi

GIT_ROOT=$(git rev-parse --show-toplevel)
```

### 1.2 Parse Flags

```bash
SCAN_WORKTREES=true
SCAN_DEV=true
SCAN_TASKS=true
SCAN_PRS=true
STALE_ONLY=false
STALE_THRESHOLD_DAYS=3

# If any specific flag is passed, only scan that section
if [[ "$ARGS" == *"--worktrees"* ]]; then SCAN_DEV=false; SCAN_TASKS=false; SCAN_PRS=false; fi
if [[ "$ARGS" == *"--dev"* ]]; then SCAN_WORKTREES=false; SCAN_TASKS=false; SCAN_PRS=false; fi
if [[ "$ARGS" == *"--tasks"* ]]; then SCAN_WORKTREES=false; SCAN_DEV=false; SCAN_PRS=false; fi
if [[ "$ARGS" == *"--prs"* ]]; then SCAN_WORKTREES=false; SCAN_DEV=false; SCAN_TASKS=false; fi
if [[ "$ARGS" == *"--stale"* ]]; then STALE_ONLY=true; fi
```

---

## Phase 2: Scan Worktrees

### 2.1 List All Worktrees

```bash
# Get worktree list (exclude the main repo itself)
WORKTREES=$(git worktree list --porcelain | grep -A2 "^worktree " | grep -v "^$")
```

### 2.2 For Each Worktree, Gather Details

For every worktree (excluding the main repo entry):

```bash
WORKTREE_PATH=...    # from worktree list
BRANCH=...           # from worktree list

# Parse issue number from branch name
ISSUE_NUM=$(echo "$BRANCH" | grep -oE '[0-9]+' | head -1)

# Last commit info
LAST_COMMIT_DATE=$(git -C "$WORKTREE_PATH" log -1 --format="%ci" 2>/dev/null)
LAST_COMMIT_MSG=$(git -C "$WORKTREE_PATH" log -1 --format="%s" 2>/dev/null)
LAST_COMMIT_AGO=$(git -C "$WORKTREE_PATH" log -1 --format="%cr" 2>/dev/null)

# Count uncommitted changes in worktree
CHANGED_FILES=$(git -C "$WORKTREE_PATH" status --porcelain 2>/dev/null | wc -l)

# Check completion marker
COMPLETE_MARKER="${WORKTREE_PATH}/.worktree-complete"
if [ -f "$COMPLETE_MARKER" ]; then
  STATUS="complete"
else
  STATUS="active"
fi

# Check staleness
DAYS_SINCE=$(( ($(date +%s) - $(git -C "$WORKTREE_PATH" log -1 --format="%ct" 2>/dev/null || echo 0)) / 86400 ))
if [ "$DAYS_SINCE" -ge "$STALE_THRESHOLD_DAYS" ]; then
  STALE=true
fi
```

### 2.3 Get Issue Title (if issue number found)

```bash
if [ -n "$ISSUE_NUM" ]; then
  ISSUE_TITLE=$(gh issue view "$ISSUE_NUM" --json title -q '.title' 2>/dev/null || echo "")
  ISSUE_STATE=$(gh issue view "$ISSUE_NUM" --json state -q '.state' 2>/dev/null || echo "")
fi
```

### 2.4 Scan Worktree Changed Files by Area

For each worktree with uncommitted changes, group files by system area:

```bash
# Categorize changed files
git -C "$WORKTREE_PATH" status --porcelain 2>/dev/null | while read STATUS FILE; do
  case "$FILE" in
    resources/js/pages/app/sa/*|resources/js/components/app/sa/*)  AREA="SA" ;;
    resources/js/pages/app/dots/*|resources/js/components/app/dots/*) AREA="DOTS" ;;
    resources/js/pages/app/cds/*|resources/js/components/app/cds/*)  AREA="CDS" ;;
    resources/js/pages/app/uacs/*|resources/js/components/app/uacs/*) AREA="UACS" ;;
    resources/js/pages/app/utils/*|resources/js/components/app/utils/*) AREA="Utils" ;;
    resources/js/components/k/*) AREA="Katalyst UI" ;;
    resources/js/composables/*) AREA="Composables" ;;
    resources/js/stores/*) AREA="Stores" ;;
    resources/js/types/*) AREA="Types" ;;
    resources/js/layouts/*) AREA="Layouts" ;;
    app/Modules/*) AREA="Backend (library)" ;;
    routes/*) AREA="Routes" ;;
    database/*) AREA="Database" ;;
    resources/css/*) AREA="Styles" ;;
    .claude/*|.kris/*) AREA="Tooling" ;;
    *) AREA="Other" ;;
  esac
done
```

---

## Phase 3: Scan Dev Repo Changes

### 3.1 Gather Uncommitted Changes

```bash
# Run from main repo backend directory
cd "$BACKEND_DIR"

# Staged changes
STAGED=$(git diff --cached --name-only 2>/dev/null)

# Unstaged changes
UNSTAGED=$(git diff --name-only 2>/dev/null)

# Untracked files (exclude common noise)
UNTRACKED=$(git ls-files --others --exclude-standard 2>/dev/null)
```

### 3.2 Group by Module/Area

Apply the same area categorization as Phase 2.4 to all dev repo changes. Group files into coherent change sets:

```
Example groupings:
- "DOTS admin panel tweaks" (3 files in resources/js/pages/app/dots/)
- "Katalyst UI refinements" (2 files in resources/js/components/k/)
- "Tooling config updates" (4 files in .claude/, .kris/)
```

### 3.3 Identify Potential Fix Candidates

For each group, assess whether it looks like a complete, committable unit of work:

| Signal | Assessment |
|--------|------------|
| Files form logical unit | Ready for `/fix` |
| Mix of unrelated changes | Needs splitting |
| Single file, small diff | Quick fix candidate |
| Backend + frontend changes together | Full-stack change |
| Only tooling/config files | Config update |

---

## Phase 4: Scan Task Files

### 4.1 Read Task Directories

```bash
TASKS_DIR="${BACKEND_DIR}/.kris/tasks"

# Pending tasks (backlog)
PENDING=$(ls "$TASKS_DIR/pending/" 2>/dev/null | grep -v "^core$")

# Running tasks (in progress)
RUNNING=$(ls "$TASKS_DIR/running/" 2>/dev/null)
```

### 4.2 Parse Task File Metadata

For each task file, extract:

```bash
# From filename: YYYY-MM-DD_<issue#>-<slug>.md
TASK_DATE=$(echo "$FILENAME" | grep -oE '^[0-9]{4}-[0-9]{2}-[0-9]{2}' || echo "undated")
TASK_ISSUE=$(echo "$FILENAME" | grep -oE '_([0-9]+)-' | tr -d '_-')
TASK_SLUG=$(echo "$FILENAME" | sed 's/^[0-9_-]*//' | sed 's/\.md$//')

# From file content (first few lines)
TASK_TITLE=$(head -5 "$TASK_FILE" | grep -E "^#" | head -1 | sed 's/^#* //')
```

### 4.3 Cross-Reference Tasks with Worktrees

For each task with an issue number, check if a corresponding worktree exists:

| Task State | Worktree Exists | Status |
|------------|----------------|--------|
| pending | No | Backlog (not started) |
| pending | Yes, active | Misaligned (should be `running/`) |
| running | Yes, active | In progress (expected) |
| running | Yes, complete | Ready for `/fix` |
| running | No | Orphaned (worktree removed?) |

---

## Phase 5: Scan GitHub PRs & Issues

### 5.1 Open Pull Requests

```bash
# Fetch open PRs targeting dev
gh pr list --base dev --state open --json number,title,headRefName,createdAt,isDraft,author
```

### 5.2 Cross-Reference PRs with Worktrees

For each PR, check if a corresponding worktree still exists (indicates the work is still in review, not cleaned up yet).

### 5.3 Recent Open Issues

```bash
# Fetch open issues (last 20)
gh issue list --state open --limit 20 --json number,title,labels,assignees,createdAt
```

---

## Phase 6: Generate Dashboard

**Output shape** — follows the canonical report-section conventions at
`.kris/templates/reports/section.md` (separators, status markers, item-reference
format, uppercase area headings between `━` bookend lines). The layout below is
`/scan`-specific — unique sections are `ACTIVE WORKTREES`, `DEV REPO CHANGES`,
`TASK FILES`, `OPEN PULL REQUESTS`, `RECOMMENDATIONS`.

### Output Format

```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  WORK AREA SCAN
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  Date: 2026-03-30
  Repo: bgh-katalyst/backend (dev branch)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

ACTIVE WORKTREES (3)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

  [1] #357 feature/357-routing-template-user-integration
      Title: [Feature]: Routing template user integration
      Status: Active
      Last commit: 2 days ago - "feat: add template selection UI"
      Uncommitted: 4 files (DOTS, Composables)
      Task: running/2026-03-26_357-routing-template-user-integration.md

  [2] #358 chore/358-sync-kris-commands
      Title: [Chore]: Sync .kris/commands with .claude/commands
      Status: COMPLETE (marker found)
      Last commit: 1 day ago - "chore: sync commands"
      PR: #1 (MERGED)
      Action: Ready for /worktree-cleanup

  [3] #351 chore/351-adjourn-skill-and-fix-dev-flag
      Title: [Chore]: Adjourn skill and fix dev flag
      Status: Active
      Last commit: 5 days ago - "wip: adjourn skill draft"
      Uncommitted: 0 files
      STALE - No commits in 5 days

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

DEV REPO CHANGES (uncommitted)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

  Total: 14 files (5 modified, 9 untracked)

  By Area:
    Tooling (7 files)
      M  .claude/CLAUDE.md
      M  .claude/commands/title.md
      M  .claude/settings.local.json
      M  .kris/commands/title.md
      ?? .claude/commands/compliance.md
      ?? .kris/context/compliance-dpa.md
      ... (+3 more)

    Tasks (2 files)
      ?? .kris/tasks/pending/2026-03-26_357-routing-template-user-integration.md
      ?? .kris/tasks/shipped/2026-03-26_358-sync-kris-commands.md

  Change Groups:
    [A] Compliance context files (5 files) - NEW content
        .kris/context/compliance*.md
        Likely: Config/docs update, committable as one unit

    [B] Command updates (3 files) - MODIFIED + NEW
        .claude/commands/*, .kris/commands/*
        Likely: Tooling update, committable as one unit

    [C] Task file updates (2 files) - NEW
        .kris/tasks/pending/*, .kris/tasks/shipped/*
        Likely: Task tracking, committable with related changes

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

TASK FILES
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

  Running (0):
    (none)

  Pending (30+):
    Recent (with issue #):
      #357 - routing-template-user-integration (2026-03-26)
              Worktree: YES (active)
      #258 - session-timeout-warning (2026-03-12)
              Worktree: NO
      #224 - doc-register-dialog-dismiss (2026-03-09)
              Worktree: NO

    Undated / Legacy (27):
      ROUTING_TEMPLATES_PRD.md
      ROUTING_TEMPLATES_BACKEND.md
      ROUTING_TEMPLATES_FRONTEND.md
      ... (use /scan --tasks for full list)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

OPEN PULL REQUESTS
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

  PR #1 - chore: sync .kris/commands (MERGED)
          Branch: chore/358-sync-kris-commands
          Worktree: YES (ready for cleanup)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

RECOMMENDATIONS
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

  Cleanup needed:
    - /worktree-cleanup 358    Worktree complete, PR merged
    - Review #351 worktree     Stale for 5 days, consider resuming or closing

  Ready to commit (dev repo):
    - Group [A]: Compliance files    /fix "compliance context files"
    - Group [B]: Command updates     /fix "command updates"

  Ready to pick up:
    - #258 session-timeout-warning   No worktree, pending since 2026-03-12
    - #224 doc-register-dialog       No worktree, pending since 2026-03-09

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

---

## Flags

### `--worktrees`

Only scan and display active worktrees:

```bash
/scan --worktrees
```

Shows the ACTIVE WORKTREES section only.

### `--dev`

Only scan uncommitted changes in the dev repo:

```bash
/scan --dev
```

Shows the DEV REPO CHANGES section only. Useful for quickly seeing what's sitting uncommitted.

### `--tasks`

Only scan task files in `.kris/tasks/`:

```bash
/scan --tasks
```

Shows full list of pending and running tasks with cross-references.

### `--prs`

Only scan open GitHub PRs:

```bash
/scan --prs
```

Shows open PRs with worktree cross-references.

### `--stale`

Filter all sections to only show stale/forgotten work:

```bash
/scan --stale
```

Highlights:
- Worktrees with no commits in 3+ days
- Pending tasks older than 7 days with no worktree
- Dev repo changes that don't belong to any known task
- Open PRs older than 3 days

### `--json`

Output raw scan data as JSON (for piping to other tools):

```bash
/scan --json
```

---

## Staleness Detection

### Thresholds

| Item | Stale After | Warning |
|------|------------|---------|
| Worktree (no new commits) | 3 days | "Stale - no commits in N days" |
| Worktree (complete, not cleaned) | 1 day | "Ready for cleanup" |
| Pending task (no worktree) | 7 days | "Consider picking up or closing" |
| Running task (no worktree) | 1 day | "Orphaned - worktree removed?" |
| Open PR (not merged) | 3 days | "Review needed" |
| Dev repo uncommitted changes | 1 day | "Consider committing or stashing" |

### Staleness Scoring

Items are sorted by urgency:

| Priority | Condition |
|----------|-----------|
| HIGH | Complete worktree not cleaned up (blocking branch) |
| HIGH | Running task with no worktree (lost work?) |
| MEDIUM | Stale worktree (forgotten work) |
| MEDIUM | Uncommitted dev changes (risk of loss) |
| LOW | Pending tasks without worktrees (backlog) |
| LOW | Open PRs awaiting review |

---

## Cross-Reference Matrix

The scan builds a cross-reference between all data sources:

```
Issue # → Worktree? → Task File? → PR? → Dev Changes?
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
#357      YES          pending/     -      -
#358      YES(done)    shipped/     #1     -
#351      YES          -            -      -
#258      -            pending/     -      -
#224      -            pending/     -      -
(none)    -            -            -      YES (14 files)
```

This matrix reveals:
- **Aligned work**: Issue + worktree + task + PR all present
- **Gaps**: Task exists but no worktree (not started)
- **Orphans**: Worktree exists but no task file
- **Loose changes**: Dev repo changes not tied to any issue

---

## Running Inside a Worktree

When `/scan` is run from inside a worktree, it still scans the **main repo** context but highlights the current worktree:

```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  WORK AREA SCAN (from worktree: feature-357-...)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

YOU ARE HERE:
  [*] #357 feature/357-routing-template-user-integration
      ...

OTHER WORKTREES:
  [2] #358 chore/358-sync-kris-commands
      ...
```

---

## Error Handling

| Error | Resolution |
|-------|------------|
| Not a git repository | "Run from inside the project directory" |
| gh CLI not authenticated | Skip GitHub sections, show local data only |
| No worktrees found | "No active worktrees. Use /worktree to start one." |
| No uncommitted changes | "Dev repo is clean." |
| No task files | "No tasks in .kris/tasks/" |

---

## Integration with Other Skills

| After `/scan` shows... | Use |
|------------------------|-----|
| Complete worktree | `/worktree-cleanup` to remove it |
| Stale worktree | `/worktree <issue#>` to resume work |
| Committable dev changes | `/fix` to commit and push |
| Pending task, no worktree | `/worktree --issue <#>` to start it |
| Orphaned running task | Investigate, then move to shipped/ or restart |

---

## Examples

### Full Scan

```
> /scan

  WORK AREA SCAN
  ...
  (all sections displayed)
```

### Quick Worktree Check

```
> /scan --worktrees

ACTIVE WORKTREES (2)
  [1] #357 feature/357-routing-template... Active, 2 days ago
  [2] #351 chore/351-adjourn-skill...      STALE (5 days)
```

### What Needs Attention?

```
> /scan --stale

STALE ITEMS (3)
  WORKTREE #351 - No commits in 5 days
  TASK #258 - Pending since 2026-03-12 (18 days), no worktree
  DEV CHANGES - 14 uncommitted files in main repo
```

### Dev Repo Only

```
> /scan --dev

DEV REPO CHANGES
  14 files (5 modified, 9 untracked)

  Change Groups:
    [A] Compliance files (5 files) - ready to commit
    [B] Command updates (3 files) - ready to commit
    [C] Task tracking (2 files) - commit with related work
```
