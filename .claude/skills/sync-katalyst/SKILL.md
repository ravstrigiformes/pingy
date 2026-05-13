---
name: sync-katalyst
description: Sync slash commands, skills, hooks, and agents from the sibling bgh-katalyst repo into fnl-cat. Auto-adapts katalyst-specific bits (larapass, clinic compliance, encounter workflow, etc.) into fnl-cat equivalents and produces an action report. Use when user says "sync from katalyst", "pull katalyst updates", or after major katalyst changes.
---

# /sync-katalyst — One-way port of `.claude/` + tooling from bgh-katalyst into fnl-cat

Katalyst (`C:\krism\projects\web\bgh\bgh-katalyst\backend\.claude\` + `backend/.kris/`) is the upstream source for slash commands, skills, hooks, and agents. This skill **scans the diff, auto-adapts katalyst-specific bits, and writes adapted copies into fnl-cat** — then prints a structured report of what was copied verbatim, what was adapted, what was skipped, and why.

> **One-way mirror.** Changes flow katalyst → fnl-cat only. Never reverse-merge fnl-cat into katalyst from this skill.
>
> **No auto-overwrite of customized files.** If fnl-cat has diverged from katalyst on a common file, the skill flags it for review rather than clobbering local customizations.

---

## Quick Start

```bash
/sync-katalyst                  # Full scan + auto-adapt + report
/sync-katalyst --dry-run        # Show the plan, write nothing
/sync-katalyst --only skills    # Limit to one category (commands|skills|hooks|agents)
/sync-katalyst --force-overwrite <name>   # Overwrite a flagged file even if fnl-cat diverged
```

---

## Repository layout assumptions

| Side | Root | Notes |
|------|------|-------|
| katalyst (source) | `C:\krism\projects\web\bgh\bgh-katalyst\backend\` | Laravel app lives under `backend/`. Hidden dirs at `backend/.claude/` and `backend/.kris/`. |
| fnl-cat (target) | `C:\krism\projects\web\fnl\fnl-cat\` | Laravel app at repo root. Hidden dirs at `.claude/` and `.kris/`. |

If katalyst root path differs (user renamed/moved the sibling), prompt once for the path and remember it for this session only.

---

## Categories synced

| Path (katalyst) | Path (fnl-cat) | Strategy |
|-----------------|----------------|----------|
| `backend/.claude/commands/*.md` | `.claude/commands/*.md` + mirror to `.kris/commands/` | Adapt-and-port new files; flag divergent common files |
| `backend/.claude/skills/<name>/SKILL.md` | `.claude/skills/<name>/SKILL.md` + mirror to `.kris/skills/` | Same |
| `backend/.claude/hooks/*.py` | `.claude/hooks/*.py` + mirror to `.kris/hooks/` | Copy verbatim if not present; flag if present and differs |
| `backend/.claude/agents/*.md` | `.claude/agents/*.md` | Adapt-and-port if name maps; otherwise flag |
| `backend/.kris/context/*.md` | `.kris/context/*.md` | Adapt: drop katalyst-specific references; flag larapass/clinic-specific files |

---

## Auto-adaptation rules

When porting any text content from katalyst → fnl-cat, apply these substitutions/strips:

### Strip / replace

| katalyst term | fnl-cat replacement | Notes |
|---------------|--------------------|-------|
| `larapass` | (strip the sentence/bullet entirely) | fnl-cat doesn't use larapass |
| `backend/` path prefix | `` (drop, fnl-cat is single-tree) | E.g., `backend/.kris/tasks/` → `.kris/tasks/` |
| `.kris/scripts/test-branch-contract.sh` | inline branch-contract snippet (see `/sanitize` for example) | fnl-cat doesn't have `.kris/scripts/` |
| `.kris/scripts/sync-safe-merge.sh` | flag for skip (no equivalent) | |
| `.kris/scripts/wt-spawn-claude.py` | flag for skip (no equivalent) | |
| HIPAA / FHIR references | (strip clause) | fnl-cat is not a clinic app |
| DPA 2012 / RA 10173 | keep — also applies to fnl-cat | passenger PII is regulated |
| Clinic-specific routes (`/sa/uacs/...`, `/dots/...`, `/cds/...`) | replace with fnl-cat patterns or `UNKNOWN — verify routing` | |
| Module names: `dots`, `cds`, `uacs`, `payee` | (drop or generalize) | These are bgh modules |
| `katalyst.<key>` in localStorage | `fnlcat.<key>` | Brand-namespaced keys |

### Skip entirely (do not port)

| Item | Reason |
|------|--------|
| `rehydrate` skill | Tightly coupled to katalyst's multi-planner `/wt` orchestrator with `wt-state/`, `scratch/wt-session/`, fusion phase tables. fnl-cat's `/worktree` doesn't have these. |
| `ab-components` skill | Depends on `KAbCompare.vue` Vue component that doesn't exist in fnl-cat; references katalyst-only k-component dir structure. |
| `admin-panel-integration` command | References AccountsLayout/RolesLayout patterns specific to katalyst's admin shell. |
| `compliance` command | HIPAA/FHIR-focused; fnl-cat needs a DPA-only variant if at all. |
| `iron` / `iron-revert` commands | Tied to `.kris/scripts/sync-safe-merge.sh` and recovery-state ledgers fnl-cat doesn't have. Worth porting eventually but requires the infra first — flag, don't copy. |
| `enc.md` URL-pattern table for DOTS/CDS/UACS | Already adapted on prior port — leave fnl-cat's version alone if present. |

If a skipped item's name appears in the report, it goes under "Skipped — incompatible" with the rule that fired.

---

## Common-file divergence rule

For files that exist in both repos with differing content (`adjourn.md`, `do.md`, `fix.md`, `worktree.md`, etc.):

1. **Compute hash + size both sides.** If katalyst is newer (mtime) AND fnl-cat hasn't been edited since last sync → propose overwrite.
2. **Otherwise, flag for review.** Print a unified diff snippet (first 40 lines of change) and ask the user to pick:
   - `[c]` Copy katalyst version verbatim
   - `[a]` Auto-adapt katalyst version (apply substitution rules above) and write it
   - `[m]` Manual merge — open both in editor (skill exits, prints paths)
   - `[s]` Skip this file

Use `AskUserQuestion` for the choice. Default action when running with `--force-overwrite <name>`: `[a]` auto-adapt.

**Do not blanket-overwrite common files.** fnl-cat has local customizations (e.g. `worktree-cleanup.md` is larger than katalyst's) that may be intentional improvements.

---

## Procedure

### 1. Resolve katalyst path

Default: `C:\krism\projects\web\bgh\bgh-katalyst\backend`. If missing, ask user once via `AskUserQuestion` and continue. Sanity-check by ensuring `<path>/.claude/commands/` and `<path>/.kris/` both exist.

### 2. Inventory both sides

For each category:
```bash
KATALYST_ITEMS=$(find "$KATALYST_ROOT/.claude/<category>" -maxdepth 2 -name '*.md' -o -name '*.py')
FNL_CAT_ITEMS=$(find "$FNL_CAT_ROOT/.claude/<category>" -maxdepth 2 -name '*.md' -o -name '*.py')
```

Bucket items into:
- **New in katalyst** (not present in fnl-cat)
- **Common — identical** (skip, no action)
- **Common — diverged** (apply common-file divergence rule)
- **Only in fnl-cat** (no action; report as "local-only, preserved")

### 3. Auto-adapt new items

For each "new in katalyst" item that isn't in the skip-list:
1. Read full file content from katalyst.
2. Apply substitution rules (`larapass` strip, `backend/` path drop, `.kris/scripts/test-branch-contract.sh` inline replacement, etc.).
3. If substitutions removed an entire section (e.g., a whole "When to run — after larapass import" bullet became empty), trim the surrounding whitespace cleanly.
4. Write to `.claude/<category>/<name>` AND mirror to `.kris/<category>/<name>`.

### 4. Aliases come along for the ride

If a ported command has known aliases in katalyst (e.g. `enc` ↔ `enc-fb` + `encounter`, `sanitize-backend` ↔ `sb`), port all of them. If the primary command is renamed during adaptation (e.g. `sanitize-backend` → `sanitize`), update the alias bodies to point to the new name.

### 5. Hooks

Hooks (`.py` files) are usually self-contained. Port verbatim unless they reference paths that don't exist in fnl-cat (e.g. `.kris/scripts/...`). Flag if so.

### 6. Settings & statusline

DO NOT touch `settings.json`, `settings.local.json`, or `statusline.sh` automatically. Print a one-line note in the report if katalyst's versions differ: "Settings/statusline differ — review manually." These often contain machine-specific paths.

### 7. Write the report

Final output goes both to stdout and to `.kris/docs/reports/sync-katalyst/<yyyy-mm-dd_HHMM>.md`. Structure:

```markdown
# /sync-katalyst report — <yyyy-mm-dd HH:MM>

## Source
- katalyst: <path>
- katalyst HEAD: <short-sha> (<author-date>)

## Copied verbatim
- `<category>/<name>` — <one-line reason>

## Copied with adaptation
- `<category>/<name>`
  - Adaptations: <rules applied, comma-separated>
  - Notes: <anything noteworthy, e.g. renamed from X to Y>

## Skipped — incompatible
- `<category>/<name>` — <rule from skip-list>

## Skipped — diverged (needs human decision)
- `<category>/<name>`
  - Diff snippet (first 40 lines):
  ```diff
  <diff>
  ```
  - Suggested action: <c/a/m/s>

## Manual follow-ups
- <bullets — e.g. "Review CLAUDE.md changes for porting", "Settings differ; review manually">
```

### 8. Versioning

Bump the patch + iteration in fnl-cat per `CLAUDE.md`:
- Update `APP_VERSION` in `.env`
- Update `version` in `package.json`
- Prepend a new `.kris/CHANGELOG.md` entry

Only do this if the user confirms — print "Apply version bump? (y/n)" at the end of the report. Default = no bump if user doesn't reply.

---

## When NOT to run

- During an active feature branch — sync produces a wide diff that pollutes feature PRs. Switch to `dev` first.
- Right after a katalyst-side breaking change you haven't read about — pull katalyst's `CHANGELOG.md` first and skim it before letting this skill auto-adapt.
- If you're mid-conflict-resolution on shared files — finish the existing edit first.

---

## Edge cases

### Katalyst sibling not found

If `C:\krism\projects\web\bgh\bgh-katalyst\backend` doesn't exist:
1. Ask user for the path via `AskUserQuestion`.
2. Validate it's a git repo and has `.claude/` + `.kris/` subdirs.
3. Remember for the session only — never persist katalyst's location into committed files.

### Adaptation produces empty body

If stripping katalyst-isms leaves a file under 200 chars (mostly frontmatter, no body), flag it as "needs manual port — adaptation gutted it." Don't write the gutted version.

### Skill rename collision

If katalyst names a skill that conflicts with an existing fnl-cat skill of the same name (e.g. both have `data-sync`), apply the divergence rule — don't silently overwrite.

### Alias-only files

For files whose entire content is `Alias for /X. Run the /X command with: $ARGUMENTS`, just rewrite the target name if X was renamed during adaptation. No further processing.

---

## Integration

- This skill **does not** invoke `/commit` or `/fix`. After it finishes, run `/commit` manually to stage the synced files with a sensible message (e.g. `chore(tooling): sync commands/skills from katalyst — <date>`).
- The report file under `.kris/docs/reports/sync-katalyst/` is committed alongside the sync; it's the audit trail.
- If you want to roll back a sync, `git restore` the changed files — the report tells you which ones.

---

## Reference

- Source-of-truth path: `C:\krism\projects\web\bgh\bgh-katalyst\backend\.claude\`
- fnl-cat conventions: `CLAUDE.md` (root), `.kris/context/*`
- Mirror convention: `.claude/` is live runtime, `.kris/` is committed backup
