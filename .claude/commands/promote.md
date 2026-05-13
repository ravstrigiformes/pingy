# /promote - Chained Branch Promotion

Promote `dev` through the environment chain in one command, stopping at the target.

> Thin orchestrator over `/stage`. Runs one `/stage SRC TGT` per hop.
> Run from the main repo (not a worktree).

> **Promotion flow reference:** `.kris/context/git-promote.md` — branch roles, regular/hotfix chains, skill relationships.

---

## Branch contract

> **Why this exists:** `/promote` chains `/stage` calls per hop. Each `/stage` checks out the target branch in the main repo. Without a contract, the orchestrator finishes on a different branch than it started on (e.g., `main` after a full chain), corrupting any parallel agent's reference frame.

- **Starts on:** any environment branch (typically `dev`)
- **Ends on:** start branch (restored at end)
- **Touches branches:** `dev`, `staging`, `beta`, `main` (whichever the chain spans)
- **Worktree-first:** no — `/stage` operates in-place on the main repo

**Self-check (every run):**

```bash
# Branch contract: record start branch+HEAD
_SKILL_START_BRANCH=$(git rev-parse --abbrev-ref HEAD)
_SKILL_START_HEAD=$(git rev-parse HEAD)                # captures starting branch
# ...skill body (chain of /stage calls)...
# Restore starting branch BEFORE return
git checkout "$_BC_START_BRANCH"
# Branch contract end: assert unchanged
if [[ "$(git rev-parse --abbrev-ref HEAD)" != "$_SKILL_START_BRANCH" || "$(git rev-parse HEAD)" != "$_SKILL_START_HEAD" ]]; then
  echo "ERROR: branch contract violated" >&2; exit 1
fi
```

---

## Sync merge policy

`/promote` delegates each hop to `/stage`, which now uses `sync-safe-merge.sh` with `--strategy=fail-on-conflict` (see `/stage`'s contract). `/promote` MUST NOT add its own merge layer; if a hop conflicts, `/promote` halts the chain and surfaces the audit report.

---

## Phantom-merge detection

Inherited from `/stage`. `/promote` additionally writes a chain summary to `.kris/audits/promote-<date>.md` recording each hop's source/target/status — so a phantom that fires mid-chain leaves a clear trace of which hops landed and which didn't.

---

## Quick Start

```bash
/promote                    # dev → staging → beta → main  (full chain, default)
/promote --staging          # dev → staging  (stop at staging)
/promote --beta             # dev → staging → beta
/promote --prod             # dev → staging → beta → main  (explicit)

/promote --dry-run          # Preview all hops
/promote --force            # Skip per-hop confirmations
```

---

## Target Flags (pick one)

| Flag | Aliases | Stops at |
|------|---------|----------|
| `--staging` | `--stage` | `staging` |
| `--beta` | — | `beta` |
| `--main` | `--prod`, `--production` | `main` (default) |

If no target flag is given, default to `main` (full chain).
**No `--dev` flag** — `dev` is the source, not a target.

---

## How It Works

```
/promote --beta
    |
    v
+------------------+
| Resolve target   |  target = beta
+------------------+
    v
+------------------+
| Build hop list   |  [dev→staging, staging→beta]
+------------------+
    v
+------------------+
| For each hop:    |
|   Run /stage     |  Confirm per hop unless --force
|   Stop on fail   |  Conflict/failure aborts remaining hops
+------------------+
    v
+------------------+
| Summary report   |
+------------------+
```

---

## Phase 1: Parse Flags

```bash
# Target resolution (default: main)
TARGET="main"
case "$@" in
  *--staging*|*--stage*) TARGET="staging" ;;
  *--beta*) TARGET="beta" ;;
  *--main*|*--prod*|*--production*) TARGET="main" ;;
esac

FORCE=false
DRY_RUN=false
[[ "$@" == *--force* || "$@" == *-f* ]] && FORCE=true
[[ "$@" == *--dry-run* ]] && DRY_RUN=true
```

## Phase 2: Build Hop List

```bash
# Full chain in order
CHAIN=("dev:staging" "staging:beta" "beta:main")

# Truncate at target
HOPS=()
for HOP in "${CHAIN[@]}"; do
  HOPS+=("$HOP")
  TO="${HOP##*:}"
  [ "$TO" = "$TARGET" ] && break
done
```

## Phase 3: Pre-flight Check

- Ensure running from main repo (not a worktree) — same check as `/stage`
- `git fetch origin --prune` once upfront (avoid re-fetching per hop)
- For each planned hop, verify source is ahead of target. If a middle hop has nothing to promote, skip it (not a failure)

## Phase 4: Execute Hops

For each hop `SRC:TGT`:

1. Invoke `/stage SRC TGT` logic (merge + push + return to dev)
2. Pass through `--force` and `--dry-run` flags
3. If hop fails (conflict, error), **stop immediately** — do not proceed to next hop
4. Report hop result before moving on

**Reuse `/stage` internals, do not duplicate:**
- Stash-aware branch switching
- Merge commit format
- Version bump + tag (only for `beta → main`)
- Changelog update (only for `beta → main`)

## Phase 5: Summary Report

```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
📦 PROMOTE: dev → beta (2 hops)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

✅ dev → staging       (5 commits, #260 #261 #262)
✅ staging → beta      (5 commits promoted)

All hops complete. Target reached: beta
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

On failure:

```
❌ staging → beta      (merge conflict in 3 files)

Aborted after 1/2 hops. Resolve conflicts, then re-run:
   /stage staging beta
   /promote --beta --force   # if staging→beta done manually
```

---

## Relationship to `/stage`

`/promote` is an orchestrator. `/stage` is the primitive.

| Use | Skill |
|-----|-------|
| One specific hop (e.g., only `beta → main`) | `/stage beta main` |
| Full chain to production | `/promote` |
| Backport (reverse direction after hotfix) | `/stage main beta` → `/stage beta staging` → `/stage staging dev` (or use `/hotfix`) |

`/promote` never does backports — direction is always downstream (dev toward main).

---

## Examples

### Default full chain to production
```
> /promote

Plan: dev → staging → beta → main  (3 hops)

Hop 1/3: dev → staging
  [/stage dev staging output, confirm Y]
  ✅ Merged

Hop 2/3: staging → beta
  [/stage staging beta output, confirm Y]
  ✅ Merged

Hop 3/3: beta → main  (⚠️ PRODUCTION)
  [/stage beta main output, version bump, tag, confirm Y]
  ✅ Merged, tagged v0.0.3.0

✅ All hops complete.
```

### Stop at beta
```
> /promote --beta

Plan: dev → staging → beta  (2 hops)
...
```

### Dry run full chain
```
> /promote --dry-run

Plan: dev → staging → beta → main  (3 hops)

Would promote:
  dev → staging:   5 commits (#260, #261, #262)
  staging → beta:  5 commits (same)
  beta → main:     12 commits total (including prior beta work)

Version bump would be: v0.0.2.0 → v0.0.3.0 (minor)
```

### Force (skip confirmations)
```
> /promote --force

⚠️ Running full chain without confirmation prompts.
...
```

---

## Error Handling

| Error | Resolution |
|-------|------------|
| Conflicting target flags | Pick one. `/promote --beta --staging` is ambiguous |
| Running from worktree | Same as `/stage`: run from main repo |
| Nothing to promote on first hop | Exit with "dev is already in sync with staging" |
| Mid-chain empty hop | Skip it, continue (not an error) |
| Merge conflict mid-chain | Abort remaining hops, report which hop failed |

---

## Quick Reference

```bash
/promote                 # Full chain to main
/promote --staging       # Stop at staging
/promote --beta          # Stop at beta
/promote --dry-run       # Preview
/promote --force         # Skip confirmations (use with care for prod)
```
