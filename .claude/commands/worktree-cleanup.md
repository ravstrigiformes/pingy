# /worktree-cleanup - Clean Up Completed Worktrees

> **Mirror:** `.kris/commands/worktree-cleanup.md` is a byte-for-byte copy of this file. `.claude/commands/` is the harness-loaded canonical; `.kris/commands/` is the checked-in mirror. After editing this file, run: `cp .claude/commands/worktree-cleanup.md .kris/commands/worktree-cleanup.md`

Remove worktrees that have completed work (PR merged or ready for cleanup).

> **Run this from the main repo** (not from inside a worktree). The orchestrator refuses with a RED alert otherwise.

---

## How this skill runs

The entire playbook (queue drain, scan, categorize, backend gate, force-delete cascade with retries, workspace cleanup, branch-contract guards) is executed in a **single bash call** to `.kris/scripts/wc/orchestrator.sh`. The orchestrator emits one structured JSON summary on stdout. You read that summary and respond to the user — you do **not** chain bash commands yourself.

> **Why:** The previous skill described the playbook step-by-step in markdown, which forced you to construct chained commands like `source X && fn …; source Y; rm Z`. Each chain trips the harness's "Contains expansion" guard and prompts the user. The orchestrator is one auto-allowed `bash …` call — no prompts unless a RED alert fires.

---

## Branch contract

- **Starts on:** any (main repo)
- **Ends on:** same main-repo branch (orchestrator asserts at end via `branch_contract_start/end`)
- **Touches branches:** branches being deleted (read-only metadata + `git branch -D`); never moves HEAD
- **Worktree-first:** no — operates on existing worktrees

The orchestrator never issues `git checkout`. Per-target guards refuse RED if:
- /wc is being run from inside a worktree
- cwd is inside a worktree being removed
- `--branch` would delete a branch currently checked out in the main repo (or in another worktree)

If a guard fires, the orchestrator exits 1 with a RED alert; surface that to the user as-is.

---

## Quick Start

```bash
/worktree-cleanup                    # Scan, list status — no removals
/worktree-cleanup 224                # Clean specific issue (must be 'ready')
/worktree-cleanup 224 --force        # Allow stale/orphan PR cleanup
/worktree-cleanup 224 --branch       # Also delete the branch
/worktree-cleanup --all              # Auto-clean all 'ready' worktrees
/worktree-cleanup --all --force      # ...including stale/orphan
/worktree-cleanup --list             # Just show categorized status
/worktree-cleanup --dry-run          # Show what --all would do, don't mutate
```

---

## How to invoke (you, the agent)

### Step 1 — Resolve mode + flags from the user's args

| User invocation | `--mode` | `--issue` | other flags |
|-----------------|----------|-----------|-------------|
| `/worktree-cleanup` | `list` | (none) | (none) |
| `/worktree-cleanup --list` | `list` | (none) | (none) |
| `/worktree-cleanup 224` | `specific` | `224` | (none) |
| `/worktree-cleanup 224 --force` | `specific` | `224` | `--force` |
| `/worktree-cleanup 224 --branch` | `specific` | `224` | `--branch` |
| `/worktree-cleanup --all` | `all` | (none) | (none) |
| `/worktree-cleanup --all --force` | `all` | (none) | `--force` |
| `/worktree-cleanup --dry-run` | `all` | (none) | `--dry-run` |

`--prune-orphans` opts into removing husk dirs (folders on disk not tracked by `git worktree list`). Default off.

### Step 1.5 — Kill dev servers inside target worktrees (pre-flight)

Before invoking the orchestrator, terminate any background processes holding file handles inside worktrees being removed. On Windows, node/php processes started by `composer run dev`, `npm run dev`, `php artisan serve`, or `php artisan queue:work` keep the worktree directory locked, and `git worktree remove` fails with "directory not empty."

**For `--mode specific <N>`:** kill processes under the single target worktree path.
**For `--mode all`:** iterate `git worktree list --porcelain`, kill processes under each (excluding the main repo's path).
**For `--mode list`:** skip — read-only.

**How to apply:**
1. `TaskList` → identify background tasks owned by this session whose cwd is inside a target worktree path. `TaskStop` each.
2. On Windows: `Get-Process | Where-Object { $_.Path -like "$WORKTREE_PATH\*" }` then `Stop-Process -Id <pid> -Force` per match.
3. On macOS/Linux: `lsof +D "$WORKTREE_PATH" | awk '{print $2}' | sort -u` then `kill -9 <pid>` per PID.

This phase NEVER fails `/wc` — best-effort cleanup. If a process refuses to die, the orchestrator's removal step will surface the file-lock failure as a YELLOW alert and queue the worktree for retry on next run.

### Step 2 — One Bash call

```bash
bash .kris/scripts/wc/orchestrator.sh \
  --mode <list|specific|all> \
  [--issue N] \
  [--force] [--branch] [--prune-orphans] [--dry-run] \
  --json-out /tmp/wc-summary-$$.json
```

The orchestrator writes the JSON summary to `/tmp/wc-summary-$$.json` AND prints it to stdout. Capture stdout (or read the file) — both are equivalent.

> **Path note:** if you're running from the main repo's `backend/` cwd, drop the `backend/` prefix: `bash .kris/scripts/wc/orchestrator.sh …`. The orchestrator detects whichever cwd it's called from.

### Step 3 — Parse and respond

The summary is one JSON object. Key fields:

```jsonc
{
  "schema_version": 1,
  "exit": 0,                      // 0=clean, 1=RED HALT, 2=YELLOW, 3=arg error
  "mode": "all",
  "main_repo_root": "/d/.../dev",
  "started_branch": "dev",
  "ended_branch": "dev",          // must equal started_branch — orchestrator enforces
  "categories": [                 // every worktree found, with its category
    {"issue": 224, "category": "ready", "name": "...", "path": "...",
     "branch": "...", "pr": 225, "pr_state": "MERGED",
     "completed": "2026-03-09T14:30:00Z"}
  ],
  "removals": [                   // present in --mode all|specific
    {"issue": 224, "result": "removed", "strategy": "rm_rf",
     "attempts": 1, "branch_deleted": false, "workspace_removed": true,
     "gate_ms": 412, "remove_ms": 612}
  ],
  "queue_replayed": [...],         // Phase 0 deferred-delete queue retries
  "queue_remaining": [...],        // paths still locked (queued for next run)
  "husk_dirs": [...],              // dirs on disk not tracked by git
  "alerts": [
    {"severity": "red", "code": "backend_gate_refused",
     "issue": 585, "files": ["app/Modules/Foo.php"],
     "remediation": "run /fix to commit, or stage manually"}
  ]
}
```

**Exit-code mapping:**

| Exit | Severity | What you do |
|------|----------|-------------|
| `0` | clean | Print a one-paragraph summary of what happened. Done. |
| `1` | RED HALT | Read `alerts[]` for `severity=red` entries. Print each as a banner with the `code` and `remediation` (if present). DO NOT retry, DO NOT mutate. Wait for the user's decision. |
| `2` | YELLOW | Print summary including the yellow alerts. Continue normally — yellow is informational (queued retries, auto-remediated changelog, stale PRs that need `--force`, husk dirs). |
| `3` | arg error | The orchestrator rejected the args. Re-check your invocation against the table above. |

---

## RED alert codes (HALT — surface to user, await decision)

| Code | Meaning | Typical remediation |
|------|---------|---------------------|
| `running_from_worktree` | /wc was run from inside a worktree, not the main repo | `cd` to the main repo and re-run |
| `main_repo_root_unresolved` | Layout doesn't match BGH-katalyst (no `<root>/.kris/`) | Verify cwd is inside the project; check git config |
| `branch_contract_start_failed` | Could not capture HEAD ref at start | git metadata is in a bad state — investigate manually |
| `branch_contract_violated` | HEAD moved during /wc execution | Bug — file an issue; do not retry on the same scope |
| `cwd_inside_target` | The cwd is inside a worktree being removed | `cd` to the main repo and re-run |
| `target_branch_is_main_head` | `--branch` would delete the main repo's checked-out branch | Switch the main repo to a different branch first, or drop `--branch` |
| `target_branch_in_other_worktree` | `--branch` would orphan another worktree | Resolve the other worktree first |
| `backend_gate_refused` | Uncommitted session-scoped backend files for the issue | Run `/fix` to commit them, or `git add` + commit manually, then re-run /wc |
| `backend_gate_conflict` | Two sessions claim the same backend file | Resolve the conflict manually before /wc |
| `specific_target_not_found` | `--issue N` matched no worktree dir | Check the issue number with `--list` |
| `specific_target_active` | `--issue N` has no completion marker | Work appears in progress — do not clean up |
| `specific_target_not_ready` | `--issue N` is stale/orphan | Re-run with `--force` if you really want to remove it |

## YELLOW alert codes (informational — keep going)

| Code | Meaning |
|------|---------|
| `lock_contention` | Another /wc is running. Try again in a few seconds. |
| `still_locked` | A queued path is still locked. Will retry on next /wc. |
| `queued_for_retry` | This run's force-delete failed; queued for next run. |
| `husk_dir` | Disk dir not tracked by `git worktree list`. Use `--prune-orphans` to remove. |
| `pr_open` / `pr_closed_unmerged` / `pr_unknown` | PR isn't merged. Use `--force` to override. |

---

## Retry semantics (configured inside the orchestrator)

- **Force-delete cascade** runs 5 strategies (rm -rf → cmd rmdir → PowerShell Remove-Item → attrib + retry → Shell.Application COM). This whole cascade is wrapped in **3 attempts** with **750ms / 1500ms backoff** between them. Total max wait per worktree ≈ 2.25s. If all 3 attempts exhaust, the path is appended to `.kris/.wc-pending-deletes` for the next /wc run.
- **`gh pr view`** retries once with 1s delay on transient failure; otherwise falls through to `pr_state: UNKNOWN`.
- **Queue drain (Phase 0)** is the cross-run retry mechanism — within a run, each queued path gets one cascade attempt; remaining entries persist in the queue.
- **Backend gate** is deterministic (no retry).

---

## Output rules (you, the agent)

- **One Bash call per /wc invocation** — never chain `source X && session_backend_sweep …` etc. yourself. The orchestrator already does this internally.
- After the call, summarize the outcome in conversation — do **not** dump the raw JSON unless the user asks.
- Print RED banners verbatim with the alert `code` and `remediation`. The user needs to see exactly what the orchestrator decided.
- If the user wants to override a YELLOW (e.g., `pr_open`), suggest re-running with `--force`. Do not loop into the orchestrator to retry on your own.

---

## Examples

### `/worktree-cleanup --list` → exit 0

```
3 worktrees:
  ready (1):   #224 feature-224-doc-register (PR #225 MERGED)
  active (1):  #230 feature-230-add-user-avatars
  orphan (1):  #215 refactor-215-extract-auth (PR #216 unknown)
```

### `/worktree-cleanup 224` → exit 0

```
Removed worktree for #224:
  Path:     .../.worktrees/feature-224-doc-register
  Strategy: rm_rf (1 attempt)
  Workspace cleaned: yes
  Branch retained (use --branch to delete)
```

### `/worktree-cleanup 585` → exit 1 (RED)

```
BACKEND GATE REFUSED — issue #585

Uncommitted session-scoped backend files:
  - app/Modules/Foo/FooService.php
  - app/Modules/Foo/Resources/FooResource.php

Remediation: run /fix to commit them, or stage manually:
  git add app/Modules/Foo/FooService.php app/Modules/Foo/Resources/FooResource.php
  git commit -m "feat(585): backend support for Foo"
  git push

Then re-run: /worktree-cleanup 585
```

### `/worktree-cleanup --all` → exit 2 (YELLOW)

```
Cleaned 2 worktrees, queued 1 for next run:
  #224 feature-224-doc-register (rm_rf, 1 attempt)
  #220 bugfix-220-session-timeout (cmd_rmdir, 2 attempts)
  #215 refactor-215-extract-auth: queued — file lock persists
       Path: .../.worktrees/refactor-215-extract-auth
       Run /worktree-cleanup again later to retry.
```

---

## Marker file (created by /fix — read by /wc)

Location: `<worktree>/.worktree-complete`

```json
{
  "issue": 224,
  "pr": 225,
  "branch": "feature/224-...",
  "commit": "abc1234f",
  "completed": "2026-03-09T14:30:00Z",
  "title": "[Feature]: ..."
}
```

The orchestrator reads `pr`, `commit`, `completed` from the marker. PR state comes from `gh pr view`.

---

## Quick Reference

```bash
# Single call (always one Bash, never chained):
bash .kris/scripts/wc/orchestrator.sh --mode list \
  --json-out /tmp/wc-summary-$$.json

bash .kris/scripts/wc/orchestrator.sh --mode specific --issue 224 \
  --json-out /tmp/wc-summary-$$.json

bash .kris/scripts/wc/orchestrator.sh --mode all --force \
  --json-out /tmp/wc-summary-$$.json

# Manual git fallback (only if orchestrator is unavailable):
git worktree list                  # show all worktrees
git worktree remove <path>         # remove specific
git worktree prune                 # clean stale entries
```
