# /preview - Preview Worktree Changes in Main Repo

Merge worktree changes into the main repo's `dev` branch for testing via Apache.

> **Run from inside a worktree** (after making changes) OR **from the main repo** with an issue number.
>
> When run from the main repo (orchestrator mode), `/preview` finds the worktree by issue number and delegates — no need to cd into the worktree manually.

---

## Branch contract

> **Why this exists:** `/preview` performs Variant-A of stash-aware-sync (worktree → main-repo dev). It mutates the main repo's HEAD via `git checkout dev`. Without a contract, an orchestrator running `/preview` while a parallel agent is mid-task on a different main-repo branch will relocate that agent's WIP onto `dev`.

- **Starts on:** worktree feature branch (in-worktree mode) OR any branch (orchestrator mode — restored at end)
- **Ends on:** start branch (orchestrator mode); worktree branch (in-worktree mode)
- **Touches branches:** worktree feature branch + main repo's `dev`
- **Worktree-first:** auto-detects

**Self-check (every run):**

```bash
# Branch contract: record start branch+HEAD
_SKILL_START_BRANCH=$(git rev-parse --abbrev-ref HEAD)
_SKILL_START_HEAD=$(git rev-parse HEAD)                # captures the main repo's starting branch
# ...skill body (stash-aware merge to dev)...
# Restore main repo to starting branch BEFORE returning
git -C "$MAIN_BACKEND" checkout "$_BC_START_BRANCH"
# Branch contract end: assert unchanged
if [[ "$(git rev-parse --abbrev-ref HEAD)" != "$_SKILL_START_BRANCH" || "$(git rev-parse HEAD)" != "$_SKILL_START_HEAD" ]]; then
  echo "ERROR: branch contract violated" >&2; exit 1
fi
```

The existing Variant-A merge code already calls `git checkout dev` unconditionally; that step is acceptable BUT must be paired with a checkout back to `$_BC_START_BRANCH` before the skill returns.

---

## Sync merge policy

Variant-A merges the worktree's feature branch INTO main-repo's `dev`. The feature branch is the source of truth — its code changes must NOT be dropped. Use:

```bash
    --dir="$MAIN_BACKEND" \
    --strategy=auto-theirs \
    --skill=preview \
    --message="preview: merge $FEATURE_BRANCH into dev"
```

`auto-theirs` keeps the feature branch's version on conflict — the opposite of `/fix`'s policy because here the feature branch IS the incoming source.

Bare `git -C "$MAIN_BACKEND" merge "$FEATURE_BRANCH" --no-edit` is forbidden in this skill.

---

## Phantom-merge detection

`sync-safe-merge.sh` runs phantom-detect automatically. If the feature branch contains `fix(/feat(/perf(/refactor(` commits but the staged diff has zero code lines, the merge is aborted with an audit report. `/preview` halts and the user is told to inspect the conflict — the worktree is left intact for re-attempt.

---

## Quick Start

```bash
# From inside a worktree:
/preview                    # Commit, merge to dev, build
/preview --no-build         # Skip the build step
/preview --message "WIP"    # Custom commit message

# From main repo (orchestrator mode):
/preview 123                # Preview worktree for issue #123
/preview 123 --no-build     # Preview without build

# Invoked by /wt Phase 7.9 (auto-preview on green review):
/preview 123 --session 123-add-payee-autocomplete
# /wt passes --session so /preview can read brief.md for the commit summary.
```

---

## How It Works

```
┌─────────────────────────────────────────────────────────────┐
│  WORKTREE (feature branch)                                  │
│  You've made changes here                                   │
└──────────────────────┬──────────────────────────────────────┘
                       │
                       ▼
              ┌────────────────┐
              │ 1. Commit      │  ← Commits uncommitted changes
              │    changes     │     (if any) to feature branch
              └───────┬────────┘
                      │
                      ▼
              ┌────────────────┐
              │ 2. Merge into  │  ← Merges feature branch into
              │    main dev    │     main repo's dev branch
              └───────┬────────┘
                      │
                      ▼
              ┌────────────────┐
              │ 3. Build       │  ← npm run build in main repo
              │    assets      │     (Apache can now serve)
              └───────┬────────┘
                      │
                      ▼
              ┌────────────────┐
              │ 4. Ready to    │  ← Test via your Apache URL
              │    test!       │
              └────────────────┘
```

---

## Phase 0: Smart Dispatch (CRITICAL — runs FIRST)

> *`/preview` always merges a feature branch into main repo's `dev` (this is intrinsic — no tab-spawn needed). The dispatch's only job is to figure out **which** feature branch when the user didn't pass one.*

### 0.1 Three modes, in order

```
MODE A — Inside a worktree (no issue arg needed):
  GIT_COMMON_DIR=$(git rev-parse --git-common-dir 2>/dev/null)
  IF "$GIT_COMMON_DIR" != ".git" → use current worktree's branch as FEATURE_BRANCH

MODE B — Main repo + explicit issue arg (e.g., /preview 123):
  ISSUE_NUM_ARG="123"
  Search .worktrees/*-${ISSUE_NUM_ARG}-* → resolve worktree path + FEATURE_BRANCH

MODE C — Main repo + NO issue arg → infer from active session:
  Candidates =
    .kris/wt-sessions/<N>-<slug>/  with mtime in last 2 hours
    AND has brief.md
    AND no matching shipped/<...>-<N>-<slug>.md task file
    AND .worktrees/*-<N>-* exists
  IF exactly 1 candidate → use it (and log "inferred #<N> from session activity")
  IF >1 candidates       → ASK USER which (one-line picker, like /fix Phase 0.2)
  IF 0 candidates        → error: "Pass /preview <issue#> or run from inside the worktree"
```

### 0.2 Always-dev guarantee

`/preview`'s target is **always main repo's local `dev`**, regardless of mode. The skill's stash-aware merge (Variant A) hardcodes this — there's no path that previews into another branch. The branch contract asserts main repo returns to `$_BC_START_BRANCH` after the merge so parallel agents aren't disturbed.

### 0.3 No tab-spawn

Unlike `/fix` and `/do`, `/preview` doesn't spawn a tab. The merge must run in main repo's process to mutate main repo's `dev`. A tab pinned to a worktree could not perform the merge into main without breaking its own isolation. `/preview` accepts the small exposure window because:

- It uses the existing `.kris/wt-sessions/.preview.lock` for cross-session safety.
- It runs `branch_contract_start` / `# Branch contract end: assert unchanged
if [[ "$(git rev-parse --abbrev-ref HEAD)" != "$_SKILL_START_BRANCH" || "$(git rev-parse HEAD)" != "$_SKILL_START_HEAD" ]]; then
  echo "ERROR: branch contract violated" >&2; exit 1
fi` to assert main repo's HEAD is unchanged.
- It is short-lived (commit + merge + optional build, ~30s typical).

---

## Phase 1: Detect Context

### 1.0 Orchestrator Mode Detection (NEW)

If an issue number argument is provided (e.g., `/preview 123`) and we're NOT in a worktree, we're in **orchestrator mode** — running from the main repo.

```bash
# Check if we're in a worktree
WORKTREE_INFO=$(git rev-parse --git-common-dir 2>/dev/null)
GIT_ROOT=$(git rev-parse --show-toplevel)

if [[ "$WORKTREE_INFO" == ".git" || "$WORKTREE_INFO" == ".git" ]] && [[ -n "$ISSUE_NUM_ARG" ]]; then
  # ORCHESTRATOR MODE: Find worktree by issue number
  WORKTREES_DIR="${GIT_ROOT}/.worktrees"
  WORKTREE_MATCH=$(find "$WORKTREES_DIR" -maxdepth 1 -type d -name "*-${ISSUE_NUM_ARG}-*" | head -1)

  if [[ -z "$WORKTREE_MATCH" ]]; then
    echo "ERROR: No worktree found for issue #${ISSUE_NUM_ARG}"
    exit 1
  fi

  # Set paths as if we're inside the worktree
  WORKTREE_BACKEND="${WORKTREE_MATCH}/backend"
  FEATURE_BRANCH=$(git -C "$WORKTREE_BACKEND" branch --show-current)
  ISSUE_NUM="$ISSUE_NUM_ARG"
  MAIN_REPO="$GIT_ROOT"
  MAIN_BACKEND="${GIT_ROOT}/backend"

  echo "Orchestrator mode: previewing worktree for issue #${ISSUE_NUM}"
  echo "  Worktree: $WORKTREE_MATCH"
  echo "  Branch: $FEATURE_BRANCH"

  # All subsequent operations use these paths
  # (commit changes in worktree, merge into main dev, build in main)
fi
```

### 1.1 Verify Worktree (direct mode)

If no issue number argument was provided, we must be inside a worktree:

```bash
if [[ "$WORKTREE_INFO" == ".git" ]]; then
  echo "ERROR: Not in a worktree. Run with issue number: /preview 123"
  exit 1
fi

# Get worktree info
FEATURE_BRANCH=$(git branch --show-current)
ISSUE_NUM=$(echo "$FEATURE_BRANCH" | grep -oE '[0-9]+' | head -1)
```

### 1.2 Find Main Repo

```bash
# Main repo is parent of .worktrees directory
MAIN_REPO=$(git rev-parse --git-common-dir | xargs dirname)
# e.g., /path/to/bgh-katalyst/.git -> /path/to/bgh-katalyst

MAIN_BACKEND="$MAIN_REPO/backend"
```

---

## Phase 2: Commit Changes (if any)

```bash
# If --session <id> was passed (from /wt Phase 7.9), read brief.md for a richer
# default commit summary. Falls back cleanly when no session or no brief exists.
SESSION_BRIEF_SUMMARY=""
if [ -n "$SESSION_ID_ARG" ]; then
  BRIEF_PATH="${GIT_ROOT}/.kris/wt-sessions/${SESSION_ID_ARG}/brief.md"
  if [ -f "$BRIEF_PATH" ]; then
    # Extract the "Approach Summary" or "Proposed Solution" one-liner from the brief.
    # Keep it to a single line for the WIP commit title.
    SESSION_BRIEF_SUMMARY=$(awk '/^## Proposed Solution/{flag=1;next} /^## /{flag=0} flag && NF' "$BRIEF_PATH" | head -1)
  fi
fi

# Check for uncommitted changes
if ! git diff --quiet || ! git diff --staged --quiet; then
  echo "Uncommitted changes detected. Committing..."

  # Stage all changes
  git add -A

  # Commit message precedence: explicit --message > session brief summary > generic default
  if [ -n "$CUSTOM_MESSAGE" ]; then
    COMMIT_MSG="$CUSTOM_MESSAGE"
  elif [ -n "$SESSION_BRIEF_SUMMARY" ]; then
    COMMIT_MSG="WIP: Preview for #${ISSUE_NUM} — ${SESSION_BRIEF_SUMMARY}"
  else
    COMMIT_MSG="WIP: Preview changes for #$ISSUE_NUM"
  fi
  git commit -m "$COMMIT_MSG"
fi
```

---

## Phase 3: Merge to Main Dev (Stash-Aware — Parallel Agent Safe)

See `.kris/context/stash-aware-sync.md` for the canonical stash-aware sync pattern. Apply **Variant A — Worktree → Main Dev**: source = current worktree's feature branch, target = main repo's local `dev`.

After a successful merge, write the `.preview-state` marker per Variant A's "Preview State Marker" subsection — this is the file `/fix` Phase 6.5 reads to know which dev commits to reconcile back.

**The `.preview-state` marker is NOT committed** — it's a local coordination file between `/preview` and `/fix`.

---

## Phase 4: Build Assets

```bash
# Build for Apache to serve (requires cd for npm)
cd "$MAIN_BACKEND"
npm run build

echo "Assets built. Ready to test via Apache."
```

---

## Output

### Success Output

```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
✅ /preview COMPLETE
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Branch:  feature/224-doc-register-dialog-dismiss
Issue:   #224
Commit:  abc1234 "feat(dots): add click-outside dismiss"

✓ Changes merged into main repo's dev branch
✓ Assets built

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

📌 TEST NOW:

   Open your Apache URL and test the changes.

📌 GRADUATED TO MAIN REPO:

   Your feature is now merged into main's dev.
   For minor tweaks, edit directly in the main repo —
   no need to /preview again for small adjustments.

   Quick iterate:  Edit in main repo → npm run build → test
   When done:      /fix   (auto-reconciles your tweaks back
                           to the feature branch before PR)

📌 STILL IN WORKTREE?

   For larger changes, you can still edit here and /preview again.
   /fix will reconcile regardless of where the edits were made.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

---

## Flags

### `--no-build`

Skip the build step (useful if you just want to test PHP changes):

```bash
/preview --no-build
```

### `--message` or `-m`

Custom commit message:

```bash
/preview --message "feat: add dismiss animation"
/preview -m "WIP: testing layout changes"
```

### `--session <session-id>`

Pass the `/wt` session id so `/preview` can read `brief.md` for a richer default commit summary. Used by `/wt` Phase 7.9; humans rarely need it — a manual `--message` is usually clearer.

```bash
/preview 123 --session 123-add-payee-autocomplete
```

---

## Post-Preview Workflow (Graduated)

After the first `/preview`, your feature is on main's dev. You have two paths for refinement:

### Path A: Edit in Main Repo (Recommended for minor tweaks)

```
/preview → [Test] → Edit in main repo → npm run build → [Test] → /fix
```

- No need to return to the worktree for small CSS fixes, copy changes, etc.
- Just edit, build, test — fast iteration
- `/fix` auto-reconciles those dev commits back to the feature branch

### Path B: Edit in Worktree + Re-preview (For larger changes)

```
/preview → [Test] → [More changes in worktree] → /preview → [Test] → /fix
```

Each subsequent `/preview`:
1. Commits new changes to the feature branch
2. Merges into dev (fast-forward if possible)
3. Rebuilds assets
4. Updates `.preview-state` marker

### Mixed Path (Both are fine)

You can mix both paths. `/fix` reconciles all post-preview dev commits regardless.

---

## After Testing

### Changes Work - Create PR

```bash
/fix
```

This will:
- **Auto-reconcile** any post-preview dev commits back to the feature branch
- Push the feature branch to origin
- Create PR from feature → dev
- The PR will show all your changes (including post-preview refinements)
- When PR merges on GitHub, origin/dev gets the changes

### Minor Tweaks Needed

Edit directly in the main repo (on dev), then `npm run build` and test. No need to `/preview` again. When satisfied, run `/fix` from the worktree — it auto-reconciles.

### Larger Changes Needed

Edit in the worktree and run `/preview` again.

### Abandon Changes

```bash
# From main repo, reset dev to remote
cd /path/to/main/repo/backend
git reset --hard origin/dev

# Then clean up worktree
/worktree-cleanup 224 --force
```

---

## Integration with Workflow

| Step | Location | Command |
|------|----------|---------|
| 1. Create task | Main repo | `/worktree "description"` |
| 2. Implement | Worktree | `/do` |
| 3. Preview | Worktree | `/preview` → Apache |
| 4. Refine | **Main repo** | Edit → `npm run build` → Test |
| 5. Finalize | Worktree | `/fix` (auto-reconciles) |
| 6. Cleanup | Main repo | `/worktree-cleanup 224` |

---

## Error Handling

| Error | Resolution |
|-------|------------|
| Not in worktree | "Run this from inside a worktree" |
| Main repo not on dev | Auto-switches to dev, warns user |
| Merge conflict | "Merge conflict detected. Resolve in main repo, then re-run" |
| Build fails | Shows build error, suggests checking for issues |

---

## Safety Notes

1. **Main repo stays on dev** - This command never switches the main repo away from dev
2. **Feature branch preserved** - All commits go to the feature branch, not dev
3. **Dev is local only** - Changes to dev are not pushed (until PR merge)
4. **Reversible** - Can reset dev to origin/dev if needed
5. **`.preview-state` is local** - Not committed, just coordinates between `/preview` and `/fix`
6. **Auto-reconciliation** - `/fix` cherry-picks post-preview dev commits back to the feature branch, so the PR is complete
