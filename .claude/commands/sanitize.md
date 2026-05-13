# /sanitize - Drop CRLF + stat-cache "modified" false alarms

Restore working-tree files that `git status` flags as modified but `git diff` confirms have no real content delta — usually a mix of:

1. **CRLF noise** — files written with CRLF line endings on Windows when `.gitattributes` mandates LF; `git status` lights up but content matches after EOL normalization.
2. **Stat-cache ghosts** — file mtime/size got bumped (e.g. by a `git checkout` between branches during a parallel-agent swap) without content change. IDEs use the `git status`-style view and keep showing "modified" until the cache is refreshed.

Both buckets are safely fixed by `git checkout -- <file>` (re-extracts from index, makes working tree byte-identical to index), followed by `git update-index --refresh` to clear residual stat-cache entries.

**Alias:** `/sb`

> **Run from the main repo or a worktree** — branch-agnostic, file-scoped. Never moves HEAD.

---

## Branch contract

> **Why this exists:** This skill only restores files via `git checkout -- <pathspec>` and refreshes the index. It never moves HEAD, never creates branches, never stashes. Still, parallel-agent safety demands an explicit assertion at start and end.

- **Starts on:** any branch (main repo or worktree)
- **Ends on:** same branch (HEAD unchanged)
- **Touches branches:** none — index and working tree only
- **Worktree-first:** no — runs in place

**Self-check (inline — no helper script required):**

```bash
SANITIZE_START_BRANCH=$(git rev-parse --abbrev-ref HEAD)
SANITIZE_START_HEAD=$(git rev-parse HEAD)
# ...skill body (file restores + index refresh)...
SANITIZE_END_BRANCH=$(git rev-parse --abbrev-ref HEAD)
SANITIZE_END_HEAD=$(git rev-parse HEAD)
if [[ "$SANITIZE_START_BRANCH" != "$SANITIZE_END_BRANCH" || "$SANITIZE_START_HEAD" != "$SANITIZE_END_HEAD" ]]; then
  echo "ERROR: branch contract violated — start=$SANITIZE_START_BRANCH@$SANITIZE_START_HEAD end=$SANITIZE_END_BRANCH@$SANITIZE_END_HEAD" >&2
  exit 1
fi
```

This skill MUST NOT issue `git checkout <branch>`, `git switch`, `git reset`, `git stash`, or any branch-moving command. Only `git checkout -- <pathspec>` (file-restore form) and `git update-index --refresh`.

---

## Quick Start

```bash
/sanitize                   # Scan + restore noise files (default)
/sb                         # Same — alias
/sb --dry-run               # Show what would be restored, mutate nothing
/sb --status-only           # Just refresh the stat cache, do not restore files
```

---

## How It Works

### Two failure modes the skill covers

| Mode             | `git status` says | `git diff` says | Working tree | Index |
|------------------|-------------------|-----------------|--------------|-------|
| **CRLF noise**   | M                 | M (with CR diff) | CRLF         | LF    |
| **Stat ghost**   | M                 | clean            | LF (or CRLF) | matches after normalization |
| **Real diff**    | M                 | M (real content) | content X    | content Y |

`git status` does a fast stat-only check first (size + mtime). If those mismatch, it marks the file potentially-modified and *should* run a content check. In practice with `core.autocrlf=true` on Windows + `eol=lf` in `.gitattributes`, files often end up with stat-mismatch + content-match-after-normalization → the file shows as M in IDEs but has no diff. This is the "ghost."

### The fix

For both noise modes:

```bash
git checkout -- <file>
```

Re-extracts the file from the index. With `.gitattributes` `* text=auto eol=lf` the working tree gets LF endings; both sides become byte-identical and the false-modified indicator clears.

After all restores, run:

```bash
git update-index --refresh
```

This forcibly re-stats every tracked file and updates the cached stat for entries where content matches but stat differs. Without this, IDEs may continue showing the stale modified state until the user touches the file or restarts their editor.

### Why use `git status --porcelain` and not `git diff --name-only`?

`git diff --name-only` only lists files where the *content* diff is non-empty. It misses stat-cache ghosts entirely. The user's IDE shows ghosts as modified because IDEs read `git status`. To fix what the user *sees*, we have to start from the same source the IDE uses — `git status --porcelain`.

### Files with REAL diffs

Files where `git diff --quiet -- <file>` returns non-zero (real content change) are left alone. Even if they also have CRLF, the user's edits are preserved. They'll get the standard `warning: CRLF will be replaced by LF` notice on commit, which is harmless — git normalizes on commit per `.gitattributes`.

---

## Steps

### 1. Pre-flight assertion

Record start branch + HEAD (see "Branch contract" snippet above).

### 2. Build the candidate list (from `git status`, not `git diff`)

```bash
git status --porcelain | awk '/^.M / || /^M / || /^MM / { print substr($0, 4) }'
```

Only modified files (working-tree or staged-modified). The `awk` strips the 3-char status prefix to leave the path. Paths are relative to the **repo root**, not cwd. Strip the leading `<subdir>/` if running from a subdirectory of the repo.

If `--status-only` was passed, skip to step 6 (just refresh, don't restore).

### 3. Classify each candidate

For each file `f`:

```bash
if git diff --quiet -- "$f" 2>/dev/null; then
  # status says M, diff says clean → ghost or CRLF-noise → restore
  echo "NOISE: $f"
else
  # real content diff → leave alone
  echo "REAL DIFF: $f"
fi
```

> **Critical pathspec footgun:** `git diff --quiet -- <pathspec>` returns 0 (no diff) **both** when the pathspec matches a clean file AND when it matches *nothing at all*. If the candidate-list paths don't resolve relative to the current cwd, every classification silently returns "no diff" and every file is mis-flagged as noise — which would lead to wholesale restoration of files with real changes.
>
> Always strip the repo-root prefix to get cwd-relative paths, OR run all `git diff --quiet` calls from the repo root.
>
> **Sanity check after classification:** sample 1–2 files from the noise bucket and run `git diff -- <file> | head` — if output is non-empty, the pathspec is misaligned and the run MUST abort.

### 4. Report the plan

```
Sanitization plan:
  Noise (CRLF + stat ghosts): N (will restore + refresh stat cache)
  Real diffs:                  M (left alone)

Sample noise files:
  app/Modules/Booking/Http/Controllers/SomeController.php
  database/seeders/SomeSeeder.php
  ... (rest)
```

If `--dry-run`, stop here.

### 5. Restore noise files

For batches >50, use a batched checkout to amortize process overhead:

```bash
printf '%s\n' "${noise_files[@]}" | xargs -d '\n' git checkout --
```

For smaller batches, a simple loop is fine:

```bash
for f in "${noise_files[@]}"; do
  git checkout -- "$f"
done
```

### 6. Refresh the stat cache

```bash
git update-index --refresh
```

This is **always run**, even with `--status-only` (which skips the file restore but still refreshes). It re-stats every tracked file and updates the stat cache for files whose content matches the index after normalization. Output may include `<file>: needs update` for files with real diffs — that's expected and harmless.

### 7. Verify

```bash
git status --porcelain | wc -l
```

Confirm the count dropped by ~`len(noise_files)`. Some residual untracked entries (`??`) are expected; only ` M` / `M ` / `MM` lines should have decreased.

### 8. Branch-contract assertion

Re-check start vs end branch/HEAD (see snippet above). Fail loud if drift detected.

### 9. Final report

```
Sanitized N noise files (CRLF + stat ghosts).
Refreshed stat cache.
Remaining modified files: M (real diffs).

If your IDE still shows stale modified indicators, restart its file watcher
(VS Code: "Developer: Reload Window" command).
```

---

## Edge cases

### IDE doesn't pick up the refresh

VS Code, JetBrains, etc. cache `git status` results in their own watcher. If the IDE keeps showing files as modified after the skill completes, the user needs to:

- VS Code: `Cmd/Ctrl+Shift+P` → "Developer: Reload Window"
- JetBrains: `File` → `Invalidate Caches and Restart`
- Or just close + reopen the file

The skill prints a hint about this in the final report.

### Submodules

Skip submodule entries:

```bash
git status --porcelain --ignore-submodules=all
```

### File deleted in HEAD

If a noise file shows up but `git checkout -- <file>` fails (e.g. file is in index but deletion was staged elsewhere), surface the error and leave the file untouched.

### Already-staged content

By default, the skill leaves staged content alone — it only restores against the index. Files in the staged area with real changes are not touched. If a file is `MM` (both staged and working-tree modified) and the working-tree diff vs index is empty after normalization, only the working-tree side is normalized — staged content is preserved.

### `core.autocrlf=true` interference

If `core.autocrlf=true` is set globally, `git checkout` writes CRLF to the working tree on Windows by default. `.gitattributes` `eol=lf` overrides this per-file. The skill checks the config and warns once if `core.autocrlf=true`, but proceeds — the per-file override should hold.

```bash
git config --get core.autocrlf
```

If it ever produces re-CRLF'd files (skill restores them, they reappear as modified next run), the user needs `.gitattributes` to win — verify with:

```bash
git check-attr eol -- <sample-file>
```

Should print `eol: set to lf`.

---

## When to run

- **After a parallel-agent branch swap** in the main repo (mtime gets bumped on hundreds of files; `git status` lies for hours afterwards)
- **Before `/commit`, `/fix`, `/stage`** if `git status` shows a sea of modified files you didn't touch
- **After switching machines** (if files were last touched on a CRLF-default machine and you're now on LF)
- **After bulk imports from generators** that write CRLF on Windows

## When NOT to run

- Mid-edit on a file you're actively working on — there's no risk of clobbering real changes (the classifier protects them), but extra noise during active work is distracting
- On binary files — `git diff --quiet` handles them correctly (binaries with stat-only changes still register as "no content diff" and get safely re-extracted)

---

## Integration

This skill is intentionally **manual-only**. It is NOT auto-invoked by `/commit`, `/fix`, `/stage`, or any other skill.

- File mutation without explicit user intent is risky.
- The noise scenario is recognizable on sight (`git status` shows hundreds of files you didn't edit).
- Users on always-LF machines (Linux, Mac) never hit this — auto-invoking would be wasted overhead.
