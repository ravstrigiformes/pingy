# /preview - Preview Worktree Changes in Main Repo

Merge worktree changes into the main repo's `dev` branch for testing via Apache.

> **Run from inside a worktree** (after making changes) OR **from the main repo** with an issue number.
>
> When run from the main repo (orchestrator mode), `/preview` finds the worktree by issue number and delegates — no need to cd into the worktree manually.

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
# Check for uncommitted changes
if ! git diff --quiet || ! git diff --staged --quiet; then
  echo "Uncommitted changes detected. Committing..."

  # Stage all changes
  git add -A

  # Commit with default or custom message
  COMMIT_MSG="${CUSTOM_MESSAGE:-"WIP: Preview changes for #$ISSUE_NUM"}"
  git commit -m "$COMMIT_MSG"
fi
```

---

## Phase 3: Merge to Main Dev

```bash
# Ensure main repo is on dev
CURRENT_BRANCH=$(git -C "$MAIN_BACKEND" branch --show-current)
if [[ "$CURRENT_BRANCH" != "dev" ]]; then
  echo "WARNING: Main repo not on dev branch. Switching..."
  git -C "$MAIN_BACKEND" checkout dev
fi

# Merge feature branch
git -C "$MAIN_BACKEND" merge "$FEATURE_BRANCH" --no-edit

echo "Feature branch merged into dev"
```

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

📌 WHEN DONE TESTING:

   If changes work:    /fix      (push + create PR)
   If changes fail:    Keep editing, then /preview again

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

---

## Multiple Previews

You can run `/preview` multiple times as you iterate:

```
[Make changes] → /preview → [Test] → [More changes] → /preview → [Test] → /fix
```

Each `/preview`:
1. Commits new changes to the feature branch
2. Merges into dev (fast-forward if possible)
3. Rebuilds assets

---

## After Testing

### Changes Work - Create PR

```bash
/fix
```

This will:
- Push the feature branch to origin
- Create PR from feature → dev
- The PR will show all your changes (even though local dev already has them)
- When PR merges on GitHub, origin/dev gets the changes

### Changes Need More Work

Just keep editing and run `/preview` again.

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
| 3. Test | Worktree | `/preview` → Apache |
| 4. Iterate | Worktree | Edit → `/preview` → Test |
| 5. Finalize | Worktree | `/fix` |
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
