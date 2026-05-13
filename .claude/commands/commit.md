# Intelligent Batch Commit

> **Mirror:** `.kris/commands/commit.md` is a byte-for-byte copy of this file. `.claude/commands/` is the harness-loaded canonical; `.kris/commands/` is the checked-in mirror. After editing this file, run: `cp .claude/commands/commit.md .kris/commands/commit.md`

Analyze changes and create organized, semantic commits with automatic grouping, staging, and versioning.

---

## Branch contract

> **Why this exists:** `/commit` may create worktrees and switch branches; if it leaves the main repo on a different ref than it entered on, parallel agents working on the original branch lose their reference frame. The contract makes the safe behavior explicit and self-checking.

- **Starts on:** any (main repo or worktree)
- **Ends on:** same ref as start (assert at end)
- **Touches branches:** the current branch only; worktrees may be created for batched commits
- **Worktree-first:** yes — batched commits run inside an ephemeral worktree

**Self-check (every run):**

```bash
# Branch contract: record start branch+HEAD
_SKILL_START_BRANCH=$(git rev-parse --abbrev-ref HEAD)
_SKILL_START_HEAD=$(git rev-parse HEAD)                # captures HEAD ref + branch name
# ...skill body...
# Branch contract end: assert unchanged
if [[ "$(git rev-parse --abbrev-ref HEAD)" != "$_SKILL_START_BRANCH" || "$(git rev-parse HEAD)" != "$_SKILL_START_HEAD" ]]; then
  echo "ERROR: branch contract violated" >&2; exit 1
fi
```

**Stash guard:** if the working tree is dirty at start, stash with `git stash push -u -m "commit-pre-op-$$"` before any branch op and pop (or preserve the stash on conflict) before returning. See `.kris/context/stash-aware-sync.md`.

---

## Parallel Agent Safety (CRITICAL)

**This skill uses git worktrees to prevent file scattering when parallel agents are running.**

### The Problem

When `/commit` creates multiple commits with branch operations:
1. Agent A stages files for group 1, commits
2. Parallel Agent B writes new files
3. Agent A stages group 2 (may accidentally pick up Agent B's files)
4. Files get committed to wrong group or lost

### Solution: Worktree Isolation

Each commit group is processed in an **isolated worktree** to prevent cross-contamination:

```
bgh-katalyst/
├── backend/                          # Main directory (ALWAYS on dev)
│   └── ... (parallel agents write here safely)
└── .worktrees/
    └── commit-{timestamp}/           # Temporary worktree for commits
```

**Workflow:**
1. Analyze changes in main directory
2. Create temporary worktree from current HEAD
3. Copy ONLY the specific files for each group
4. Commit in worktree
5. Push from worktree
6. Delete temporary worktree
7. Main directory unchanged

---

## Default Behavior (Fully Automatic)

1. **Analyze all unstaged and staged changes** in the repository
2. **Group related changes** by their logical category/topic (e.g., feature, bugfix, refactor, docs, style, config, etc.)
3. **Create temporary worktree** for isolated commits (parallel agent safety)
4. **For EACH group, create a SEPARATE commit** (in worktree):
   - **Stage only the files** belonging to the current group
   - **Determine version increment** for this specific commit (vW.X.Y.Z format):
     - Parse the previous commit message to get current version
     - W = Major version (breaking changes, architectural changes, major features)
     - X = Minor version (new features, significant enhancements, API changes)
     - Y = Patch version (bugfixes, small improvements, minor refactors)
     - Z = Iteration (formatting, documentation, config tweaks, minor style changes)
     - Auto-increment appropriate level based on this commit's scope
     - Auto-reset lower levels when higher ones increment (e.g., v0.0.13.0 → v0.0.14.0 resets Z to 0)
   - **Detect GitHub issue references** in:
     - Branch names (e.g., `feature/123-new-feature`)
     - File contents or comments containing `#123`, `issue #123`, etc.
     - Include appropriate closure keywords if applicable: "Resolves #123", "Fixes #123", "Closes #123", "Completes #123"
   - **Create commit message** in this format:
     ```
     <category>: <concise title describing the change>
     
     <GitHub issue reference if applicable>
     vW.X.Y.Z
     <additional context if needed>
     ```
   - **Update `package.json` and `.env` version** to match:
     ```bash
     npm pkg set version="${W}.${X}.${Y}.${Z}"
     sed -i "s/^APP_VERSION=.*/APP_VERSION=${W}.${X}.${Y}.${Z}/" .env
     git add package.json
     ```
   - **Execute the commit** with `git commit -m "title" -m "description"`
4. **Move to next group** and repeat step 3 until all groups are committed
5. **Result**: Multiple sequential commits, each focused on one logical category

## CRITICAL: One Commit Per Category Group

- DO NOT stage and commit all files at once
- Each category group gets its own separate commit
- Version numbers increment sequentially across commits
- Example sequence:
  ```
  Commit 1: config: Enforce 2-space indentation (v0.0.13.0)
  Commit 2: style: Reformat all frontend files (v0.0.13.1)
  Commit 3: refactor: Enhance batch operations service (v0.0.13.2)
  ```

## Available Flags for Manual Control

Use these flags to enable manual prompts at specific steps:

- `--manual-categories` or `-c`: Prompt for approval/modification of detected categories before grouping
- `--manual-files` or `-f`: Prompt for approval/modification of which files are included in each group
- `--manual-version` or `-v`: Prompt for manual version number input instead of auto-increment
- `--manual-issues` or `-i`: Prompt for manual GitHub issue reference input
- `--manual-proceed` or `-p`: Prompt for approval before proceeding to each next commit (commit-by-commit review)
- `--manual-all` or `-a`: Enable all manual prompts (full interactive mode)
- `--session <id>`: Optional `/wt` session id (`<issue#>-<slug>`); when present, reads `.kris/wt-sessions/<id>/brief.md` to seed a richer default commit subject/body. If omitted but `/commit` runs inside a `/wt` worktree, the session id is inferred from the worktree directory name. Mirrors `/preview --session`.

## Examples

**Fully automatic:**
```
/commit
```

**Review categories and files before committing:**
```
/commit --manual-categories --manual-files
```

**Review everything step-by-step:**
```
/commit --manual-all
```

**Only control version numbers manually:**
```
/commit -v
```

## Commit Message Format Details

**First `-m` flag (title):**
- Format: `<category>: <concise description>`
- Category examples: `feat`, `fix`, `refactor`, `docs`, `perf`, `test`, `chore`, `style`
- Keep under 72 characters
- Use imperative mood ("add" not "added")

**Second `-m` flag (description):**
- Line 1: GitHub issue reference (if applicable)
- Line 2: Version number (vW.X.Y.Z)
- Line 3+: Additional context if needed (optional)

## Notes

- **Each category gets its own commit** - never combine multiple categories into one commit
- Commits are created sequentially, one after another
- Each commit should be self-contained and logically coherent
- Version increments with each commit based on the scope of that specific change
- Prioritize clarity - the title should make the change immediately understandable
- If no previous version found, start at v1.0.0.0
- For formatting/style changes (like reformatting files), increment Z (iteration)
- For config changes (like .editorconfig, .prettierrc), increment Z (iteration)
- For refactors or enhancements without breaking changes, increment Y (patch)
- For new features or API additions, increment X (minor)
- For breaking changes or major architectural shifts, increment W (major)
- If unsure about category boundaries, prefer smaller, more focused commits over large ones

---

## Session-Brief Integration (`--session <id>`)

Mirrors `/preview --session`: when `/commit` knows the `/wt` session id, it can read
`.kris/wt-sessions/<id>/brief.md` to seed a richer default commit subject/body. The
brief data is treated as an *additional signal*, not an override — file-change analysis
still drives the primary message; the brief just informs summary phrasing.

### Detect session context

```bash
# Detect /wt worktree context
GIT_ROOT=$(git rev-parse --show-toplevel 2>/dev/null)

# If we're inside a /wt worktree (path contains /.worktrees/<type>-<num>-<slug>),
# derive session id. Silent no-op if detection fails.
if echo "$GIT_ROOT" | grep -q '/\.worktrees/'; then
  WT_DIR_NAME=$(basename "$GIT_ROOT")
  # Strip type prefix (feature-/bugfix-/refactor-/docs-/chore-/perf-/test-) to get <num>-<slug>
  INFERRED_SESSION_ID=$(echo "$WT_DIR_NAME" | sed -E 's/^(feature|bugfix|refactor|docs|chore|perf|test)-//')
  SESSION_ID_ARG="${SESSION_ID_ARG:-$INFERRED_SESSION_ID}"
fi
```

### Read the session brief (if any)

```bash
SESSION_BRIEF_SUMMARY=""
if [ -n "$SESSION_ID_ARG" ]; then
  # When inside a worktree, the session brief lives in the MAIN backend, not the worktree.
  # When outside a worktree, MAIN_BACKEND is just the current backend dir.
  if echo "$GIT_ROOT" | grep -q '/\.worktrees/'; then
    MAIN_BACKEND="${GIT_ROOT%/.worktrees/*}/backend"
  else
    MAIN_BACKEND="${GIT_ROOT}/backend"
  fi
  BRIEF_PATH="${GIT_ROOT}/.kris/wt-sessions/${SESSION_ID_ARG}/brief.md"
  if [ -f "$BRIEF_PATH" ]; then
    # Extract the brief's H1 title and the first non-empty line under "## Proposed Solution".
    BRIEF_TITLE=$(grep -m1 '^# Brief:' "$BRIEF_PATH" | sed 's/^# Brief: //')
    SESSION_BRIEF_SUMMARY=$(awk '/^## Proposed Solution/{flag=1;next} /^## /{flag=0} flag && NF' "$BRIEF_PATH" | head -1)
  fi
fi
```

### Wire brief into default subject generation

When building each group's default commit subject from the diff/files analysis, prefer
`$SESSION_BRIEF_SUMMARY` phrasing over a generic subject, but **only if** it's congruent
with what the group actually contains. If a group's files don't match the brief's scope
(e.g., unrelated maintenance changes landed in the same commit), fall back to the
file-driven default. Manual overrides via `--manual-categories` / `--manual-issues`
always win.

### Fallback behavior

- `git rev-parse` fails or `$GIT_ROOT` is not under `/.worktrees/` → skip detection silently.
- No `$SESSION_ID_ARG` after detection → no brief read; exact current behavior preserved.
- `BRIEF_PATH` doesn't exist → no brief read; no error surfaced to the user.

---

## Worktree Workflow (Technical Detail)

### 1. Create Temporary Worktree

```bash
# Generate unique worktree name
TIMESTAMP=$(date +%Y%m%d-%H%M%S)
WORKTREE_DIR=".worktrees/commit-${TIMESTAMP}"

# Ensure .worktrees directory exists
mkdir -p .worktrees

# Create worktree from current HEAD (stays on same branch)
git worktree add "$WORKTREE_DIR" HEAD --detach
```

### 2. Process Each Commit Group

```bash
# For each group:
for group in "${COMMIT_GROUPS[@]}"; do
  # Copy ONLY the specific files for this group
  for file in "${group_files[@]}"; do
    cp "$file" "$WORKTREE_DIR/$file"
  done

  # Stage and commit in worktree
  git -C "$WORKTREE_DIR" add "${group_files[@]}"
  git -C "$WORKTREE_DIR" commit -m "$COMMIT_MESSAGE"
done
```

### 3. Push and Cleanup

```bash
# Push all commits from worktree
git -C "$WORKTREE_DIR" push origin HEAD:$(git -C "$WORKTREE_DIR" rev-parse --abbrev-ref @{-1})

# Remove worktree
git worktree remove "$WORKTREE_DIR"
```

### 4. Main Directory Sync

After worktree commits are pushed, the main directory can pull:

```bash
git pull --rebase origin dev
```

**Key Guarantee:** Main directory NEVER changes branch during the commit process. Parallel agents can safely continue writing files.

---

## Worktree Cleanup

If `/commit` is interrupted, worktrees may be left behind:

```bash
# List all worktrees
git worktree list

# Remove stale worktrees
git worktree prune

# Manually remove specific worktree
git worktree remove .worktrees/commit-20260306-143022
```

---

## Kanban Move (INCLUDED IN COMMIT)

> **CRITICAL:** Task/prompt file moves are done BEFORE the commit and included in the same commit.
> This prevents kanban renames from being "left behind" as uncommitted local changes after push.

### Per-Group Flow

For each commit group, BEFORE staging the group's files:

1. **Resolve issue # for this group** — parse the group's commit message body for `Resolves #N` / `Fixes #N` / `Closes #N` / a bare `#N`. If none → skip kanban move for this group (e.g., chore commits without an issue).

2. **Move matching task file** (`.kris/tasks/{running,pending}/*_<N>-*.md` → `.kris/tasks/shipped/`):

   ```bash
   GIT_ROOT=$(git rev-parse --show-toplevel)
   TASKS_DIR="${GIT_ROOT}/.kris/tasks"
   TASK_PATTERN="*_${ISSUE_NUM}-*.md"

   mkdir -p "${TASKS_DIR}/shipped"

   for SRC in running pending; do
     TASK_FILE=$(find "${TASKS_DIR}/${SRC}" -maxdepth 1 -name "$TASK_PATTERN" 2>/dev/null | head -1)
     if [ -n "$TASK_FILE" ]; then
       TASK_BASENAME=$(basename "$TASK_FILE")
       # Move within the worktree so the rename is staged for THIS group's commit
       REL_FROM=".kris/tasks/${SRC}/${TASK_BASENAME}"
       REL_TO=".kris/tasks/shipped/${TASK_BASENAME}"
       cp "${TASK_FILE}" "${WORKTREE_DIR}/${REL_TO}"
       mkdir -p "$(dirname "${WORKTREE_DIR}/${REL_TO}")"
       git -C "$WORKTREE_DIR" mv "${REL_FROM}" "${REL_TO}" 2>/dev/null || \
         (mv "${WORKTREE_DIR}/${REL_FROM}" "${WORKTREE_DIR}/${REL_TO}" && \
          git -C "$WORKTREE_DIR" add -A "$REL_FROM" "$REL_TO")
       # Also move in main repo so subsequent groups / agents see the new state
       git mv "$TASK_FILE" "${TASKS_DIR}/shipped/${TASK_BASENAME}"
       echo "📋 Task moved: ${SRC} → shipped/${TASK_BASENAME}"
       break
     fi
   done
   ```

3. **Move matching prompt file(s)** (`.kris/prompts/pending/**/*_<N>-*.md` → `.kris/prompts/shipped/<same-subdir>/`):

   ```bash
   PROMPTS_DIR="${GIT_ROOT}/.kris/prompts"
   PROMPT_FILES=$(find "${PROMPTS_DIR}/pending" -name "*_${ISSUE_NUM}-*.md" 2>/dev/null)

   if [ -n "$PROMPT_FILES" ]; then
     while IFS= read -r PROMPT_FILE; do
       REL_PATH="${PROMPT_FILE#${PROMPTS_DIR}/pending/}"
       REL_FROM=".kris/prompts/pending/${REL_PATH}"
       REL_TO=".kris/prompts/shipped/${REL_PATH}"
       mkdir -p "$(dirname "${WORKTREE_DIR}/${REL_TO}")"
       mkdir -p "$(dirname "${PROMPTS_DIR}/shipped/${REL_PATH}")"
       git -C "$WORKTREE_DIR" mv "${REL_FROM}" "${REL_TO}" 2>/dev/null || true
       git mv "$PROMPT_FILE" "${PROMPTS_DIR}/shipped/${REL_PATH}"
       echo "📋 Prompt moved: pending/${REL_PATH} → shipped/${REL_PATH}"
     done <<< "$PROMPT_FILES"
   fi
   ```

4. **Stage** — `git mv` auto-stages both rename sides, so the moves flow into the upcoming group commit alongside the changelog and code changes.

### Idempotency

- File already in `shipped/` → skip silently
- No issue # in commit message → skip silently (chore commits, etc.)
- No task/prompt file matches the issue # → skip silently (task tracking not used for this group)

### 5. Session-Scoped Backend Sweep (per-group, BEFORE staging the group's code files)

**Per-group scoping is the whole point:** issue A's commit must NEVER bundle issue B's backend.

**Resolve MAIN_GIT_ROOT once outside the loop (CRITICAL: round-trip to absolutize):**

`git rev-parse --git-common-dir` may return `../.git` when run from `dev/backend`. The `cd && pwd` round-trip absolutizes the path so the per-group `cp` later (which runs from inside the temp worktree's cwd) dereferences `MAIN_GIT_ROOT/<path>` correctly.

```bash
SBS_GROUP_OUTER_SKIP=false
GIT_COMMON_DIR=$(git rev-parse --git-common-dir)
MAIN_GIT_ROOT="${GIT_COMMON_DIR%/.git}"
MAIN_GIT_ROOT="$(cd "$MAIN_GIT_ROOT" 2>/dev/null && pwd)" || MAIN_GIT_ROOT=""
if [ -z "$MAIN_GIT_ROOT" ] || [ ! -d "${MAIN_GIT_ROOT}/.kris" ]; then
  echo "🚫 Backend sweep: MAIN_GIT_ROOT resolved to '${MAIN_GIT_ROOT}' but .kris/ not found"
  echo "   Disabling per-group sweep for this /commit run."
  SBS_GROUP_OUTER_SKIP=true
fi

if [ "$SBS_GROUP_OUTER_SKIP" != "true" ]; then
fi
```

**Inside the per-group loop (each step gates on the skip flag — no fall-through):**

```bash
SBS_GROUP_SKIP="$SBS_GROUP_OUTER_SKIP"
if [ "$SBS_GROUP_SKIP" != "true" ] && [ -z "$GROUP_ISSUE_NUM" ]; then
  echo "ℹ️  Group has no issue # — skipping backend sweep (chore commit)"
  SBS_GROUP_SKIP=true
fi

if [ "$SBS_GROUP_SKIP" != "true" ]; then
  RESULT_FILE="/tmp/sbs-${GROUP_ISSUE_NUM}-$$"
  session_backend_sweep \
    --issue "$GROUP_ISSUE_NUM" \
    --mode per-group \
    --repo-root "$MAIN_GIT_ROOT" \
    --result-file "$RESULT_FILE"
  SWEEP_RC=$?

  source "$RESULT_FILE"
  rm -f "$RESULT_FILE"

  if [ "$SWEEP_RC" -eq 1 ] || [ "${#SBS_CONFLICTS[@]}" -gt 0 ]; then
    echo "🚫 Group #${GROUP_ISSUE_NUM}: backend sweep found conflicting session claims."
    echo "   Skipping auto-stage for THIS group; other groups will continue."
    # Do NOT halt the whole skill — other groups may be fine.
  else
    # Copy each staged file from main repo INTO the temp worktree, then add it
    # there so it lands in this group's commit. The helper already ran `git add`
    # in the main repo (per-group mode acts as `stage` semantically), so main
    # repo status will be clean after push.
    for path in "${SBS_TO_STAGE[@]}"; do
      SRC="${MAIN_GIT_ROOT}/${path}"
      DST="${WORKTREE_DIR}/${path}"
      mkdir -p "$(dirname "$DST")"
      cp -f "$SRC" "$DST"
      git -C "$WORKTREE_DIR" add -- "$path" \
        || echo "    ⚠️  worktree add failed for: $path"
    done
  fi
fi
```

**Per-group temp result file** uses `$GROUP_ISSUE_NUM` in the path so concurrent groups don't collide on `/tmp/sbs-*`.

### Why Pre-Commit (Not Post-Commit)

If the kanban move runs after `git commit`, the rename sits as an uncommitted local change. After push, the next agent / `git status` shows phantom renames that have to be cleaned up manually. Doing it pre-commit means:

- The PR contains the move → reviewers see the lifecycle change
- When the PR merges to `dev`, the main repo's kanban auto-updates via the merge — no follow-up sync
- `/worktree-cleanup`'s safety-net move becomes a no-op (file is already shipped)

---

## Changelog Update (INCLUDED IN COMMIT)

> **CRITICAL:** The changelog entry is created BEFORE the commit and included in the same commit.
> This prevents changelog entries from being "left behind" as uncommitted local changes.

### Changelog File

```bash
# Calculate current week's changelog file (Monday to Sunday, ISO 8601)
TODAY=$(date +%Y-%m-%d)
DOW=$(date +%u)  # 1=Monday, 7=Sunday
MONDAY=$(date -d "$TODAY -$((DOW-1)) days" +%Y-%m-%d)
SUNDAY=$(date -d "$MONDAY +6 days" +%Y-%m-%d)
CHANGELOG_FILE=".kris/changelogs/${MONDAY}_to_${SUNDAY}.md"
```

### Entry Format (Per Commit)

For each commit group, BEFORE committing:

1. **Create the changelog entry** with known info and placeholders:

```markdown
#### {type}: {description} (v{VERSION})
- **Commit**: `(pending)`
- **Issue**: #{NUM} (if applicable)
- **PR**: (pending)
- **Area**: {Backend|Frontend|Full Stack|Docs/Config}
- **Version**: v{VERSION}
- **Files**: `{file1}`, `{file2}`, ...
- **Changes**:
  - {Change 1}
  - {Change 2}
```

2. **Update summary statistics** (total commits, features/bugs/other, by area)

3. **Stage the changelog file** alongside the group's other files

4. **Commit** (changelog is now part of the commit)

5. **Back-fill commit hash** by amending (pre-push, safe):
```bash
COMMIT_HASH=$(git rev-parse --short HEAD)
sed -i "s/\`(pending)\`/\`$COMMIT_HASH\`/" "$CHANGELOG_FILE"
git add "$CHANGELOG_FILE"
git commit --amend --no-edit
```

6. **After push**, the PR # field stays as `(pending)` since `/commit` does not create PRs.
   The `/worktree-cleanup` safety net or the next `/fix` run will back-fill it.

### Workflow Per Group

```bash
for group in "${COMMIT_GROUPS[@]}"; do
  # 1. Copy group files to worktree
  # 2. Create changelog entry with placeholders
  # 3. Copy changelog to worktree
  # 4. Stage all files + changelog
  # 5. Commit
  # 6. Amend with actual commit hash
done

# Push all commits
git push -u origin "$BRANCH_NAME"
```

### Example Multi-Commit Output

```
📓 Changelog Included in Commits:
   .kris/changelogs/2026-03-10_to_2026-03-16.md
   3 entries committed under Monday, March 10:
     - config: Enforce 2-space indentation (v0.0.13.0) ← `abc1234`
     - style: Reformat all frontend files (v0.0.13.1) ← `def5678`
     - refactor: Enhance batch operations service (v0.0.13.2) ← `ghi9012`
   Summary: 18 commits (+3), 14 resolved
   Note: PR # fields show (pending) — back-filled by /worktree-cleanup
```