# /retrospect - Capture/Update Topic Retrospective

Scan `.kris/docs/retrospectives/`, fuzzy-match the current session's topic, and auto-fill an issues-table row + iteration-log entry on a match — or scaffold a new retrospective when no match exists. Autofill is sourced from git diff/log, the open PR (if any), and `retro-notes.md` written by the reviewer agent.

> **Run from anywhere inside the repo.** No tab spawn — `/retrospect` is an inline runtime (short edit, no branch mutation).
>
> **No issue # required.** Ad-hoc capture is a first-class mode: `/retrospect "lessons from Apache cert debug"` from any cwd is supported.

---

## Branch contract

> **Why this exists:** `/retrospect` only reads git state and writes markdown under `.kris/docs/retrospectives/`. It does NOT switch branches, commit, push, or run a merge. Parallel-agent WIP on sibling branches is safe.

- **Starts on:** any branch (worktree feature branch, main repo `dev`, ad-hoc — irrelevant)
- **Ends on:** start branch (unchanged)
- **Touches branches:** none
- **Worktree-first:** no preference — runs identically in main repo or any worktree

**Self-check (every run):**

```bash
# Branch contract: record start branch+HEAD
_SKILL_START_BRANCH=$(git rev-parse --abbrev-ref HEAD)
_SKILL_START_HEAD=$(git rev-parse HEAD)
# ...skill body...
# Branch contract end: assert unchanged
if [[ "$(git rev-parse --abbrev-ref HEAD)" != "$_SKILL_START_BRANCH" || "$(git rev-parse HEAD)" != "$_SKILL_START_HEAD" ]]; then
  echo "ERROR: branch contract violated" >&2; exit 1
fi
```

The skill never invokes `git checkout`, `git switch`, or `git worktree`. If branch state diverges at end, the diff was caused by something outside this skill — investigate.

---

## Quick Start

```bash
# Auto-detect topic from branch/cwd:
/retrospect                              # Match against retrospectives/, auto-target or picker

# Explicit topic arg (override auto-detect):
/retrospect dots-pagination              # Target by topic slug (fuzzy-matched)
/retrospect "DOTS documents pagination"  # Multi-word also accepted

# Force create-new mode (skip the match phase):
/retrospect --new dots-bulk-actions
/retrospect --new "DOTS bulk actions"

# Ad-hoc capture (no branch context required):
/retrospect --ad-hoc "lessons from Apache cert debug"

# Dry-run (no writes, just preview what /retrospect would do):
/retrospect --info
/retrospect dots-pagination --info

# Skip INDEX.md update (useful for batch operations):
/retrospect --no-update-index
```

---

## How It Works

```
┌─────────────────────────────────────────────────────────────┐
│  Phase 0 — Mode Detection                                   │
│  Inside worktree → derive topic from branch + diff          │
│  Main repo + arg → explicit topic                           │
│  Ad-hoc → user-provided topic, no branch context            │
└──────────────────────┬──────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────┐
│  Phase 1 — Topic Resolution                                 │
│  Normalize slug (UPPER_SNAKE ↔ lower-kebab)                 │
│  Fuzzy match against retrospectives/*.md                    │
│  Single strong match → auto-target                          │
│  Multiple matches → ranked picker                           │
│  No match → confirm create                                  │
└──────────────────────┬──────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────┐
│  Phase 2 — Gather Source Material                           │
│  git diff origin/dev..HEAD --stat                           │
│  git log origin/dev..HEAD --oneline                         │
│  gh pr view (if open PR on this branch)                     │
│  retro-notes.md from .kris/wt-sessions/<id>/ (if any)       │
│  User-provided body (ad-hoc mode)                           │
└──────────────────────┬──────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────┐
│  Phase 3 — Apply Updates                                    │
│  UPDATE mode: append row + iteration-log entry              │
│  CREATE mode: scaffold from DOTS_DOCUMENTS_PAGINATION shape │
│  Mark autofilled judgment lines with <!-- DRAFT -->         │
└──────────────────────┬──────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────┐
│  Phase 4 — Update INDEX.md                                  │
│  Append new entry OR update issue-count on existing row     │
└──────────────────────┬──────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────┐
│  Phase 5 — Output + Diff Summary                            │
│  Print TARGET_PATH + what changed                           │
│  Suggest commit message                                     │
└─────────────────────────────────────────────────────────────┘
```

---

## Phase 0: Mode Detection (CRITICAL — runs FIRST)

> *Three modes. Resolve which one applies in order; first match wins.*

### 0.1 Three modes, in order

```
MODE A — Inside a worktree (derive topic from branch):
  GIT_COMMON_DIR=$(git rev-parse --git-common-dir 2>/dev/null)
  IF "$GIT_COMMON_DIR" != ".git" → derive topic from branch slug + diff
  Example: branch=feature/427c-pagination-bugfix → TOPIC_SEED="pagination-bugfix"

MODE B — Main repo + explicit topic arg:
  /retrospect dots-pagination
  /retrospect "DOTS documents pagination"
  → TOPIC_SEED = the arg (slug-normalized)

MODE C — Main repo + NO arg → infer from active session:
  Candidates =
    .kris/wt-sessions/<N>-<slug>/  with mtime in last 4 hours
    AND has brief.md
  IF exactly 1 candidate → use the session slug as TOPIC_SEED
                           (and log "inferred from session activity")
  IF >1 candidates       → ASK USER which (one-line picker)
  IF 0 candidates        → prompt for explicit topic or `--ad-hoc`

MODE D — Ad-hoc (no branch context, no session, explicit arg):
  /retrospect --ad-hoc "lessons from Apache cert debug"
  → TOPIC_SEED = the arg; no issue#, no branch info, no diff
  Body autofill skipped — user dictates content in conversation,
  or skill scaffolds a blank template.
```

### 0.2 Always-inline guarantee

`/retrospect` never spawns a tab. The work is short (a markdown edit, ~30s) and touches no branch state. Spawning a tab would add cost without benefit. Parallel-agent safety is provided by the branch contract (no branch mutation) and by file-path scoping (only writes under `.kris/docs/retrospectives/` and `.kris/docs/INDEX.md`).

### 0.3 Output of Phase 0

```bash
MODE=<A|B|C|D>
TOPIC_SEED=<derived-or-provided slug>
ISSUE_NUM=<num if available, empty otherwise>
SESSION_DIR=<path if a /wt session is involved, empty otherwise>
IS_AD_HOC=<true|false>
```

---

## Phase 1: Topic Resolution

> *Normalize the topic seed; fuzzy-match against existing retrospectives; resolve confidence; output TARGET_PATH and MODE (update|create).*

### 1.1 Slug normalization

Retrospective files use UPPER_SNAKE_CASE (e.g., `DOTS_DOCUMENTS_PAGINATION.md`). Branches and topic args use lower-kebab-case (e.g., `dots-documents-pagination`). Normalize both directions for matching:

```bash
# Normalize TOPIC_SEED to both forms
TOPIC_UPPER_SNAKE=$(echo "$TOPIC_SEED" | tr '[:lower:]' '[:upper:]' | tr '-' '_' | tr ' ' '_')
TOPIC_LOWER_KEBAB=$(echo "$TOPIC_SEED" | tr '[:upper:]' '[:lower:]' | tr '_' '-' | tr ' ' '-')

# Strip leading issue#-letter prefix if present (e.g., "427c-pagination-bugfix" → "pagination-bugfix")
TOPIC_LOWER_KEBAB=$(echo "$TOPIC_LOWER_KEBAB" | sed -E 's|^[0-9]+[a-z]?-||')
TOPIC_UPPER_SNAKE=$(echo "$TOPIC_UPPER_SNAKE" | sed -E 's|^[0-9]+[A-Z]?_||')
```

The stripping rule reflects `feedback_task-suffix-letter-derived.md`: a branch like `feature/427c-pagination-bugfix` is iteration C of topic 427's pagination work. The retrospective is on the *topic* (pagination), not the iteration (`427c`).

### 1.2 Fuzzy match

Scan `.kris/docs/retrospectives/*.md`. Match against:

1. **Exact filename match** (after slug normalization) — confidence 100%.
2. **Substring match** of the file's main keyword(s) against `TOPIC_LOWER_KEBAB` or vice versa — confidence 60–95% based on overlap length / total length.
3. **Keyword-set match** — tokenize the topic on `_` / `-` / space. Count overlapping tokens against the file's title heading (`# X — Retrospective`) and its "Topic scope" paragraph. Confidence = overlap_count / max(seed_tokens, file_tokens).

Threshold conventions (match `/worktree` Step 3):

```
>= 80%  →  strong match, auto-target
60–79%  →  candidate, include in picker
<  60%  →  not a match
```

### 1.3 Resolve confidence

```
1 strong match (>= 80%)  →  auto-target, MODE=update, proceed to Phase 2
0 matches                →  confirm with user: "Create new retrospective at <PATH>?"
                            On confirm: MODE=create, proceed to Phase 2
                            On deny: exit cleanly (no writes)
Multiple matches         →  show ranked picker:

  Multiple retrospectives match "pagination":

    1. [90%] DOTS_DOCUMENTS_PAGINATION.md
             "DOTS Documents Server-Side Pagination" (2 iterations, last: 2026-05-11)

    2. [65%] PAGINATION_HELPER_REFACTOR.md
             "Pagination helpers — shared util extraction" (1 iteration, last: 2026-04-30)

    Which? [1/2] or [n] to create new: _
```

If `--new` is passed, skip matching entirely and proceed to Phase 2 with MODE=create.

### 1.4 Output of Phase 1

```bash
TARGET_PATH=<absolute path to .kris/docs/retrospectives/X.md>
MODE=<update|create>
TOPIC_TITLE=<human-friendly title — autofilled from file heading if update; derived from arg if create>
```

---

## Phase 2: Gather Source Material (autofill inputs)

> *Collect everything we can fill into the retrospective automatically. Skipped fields stay blank — better empty than wrong.*

### 2.1 Git history (skip in MODE D ad-hoc)

```bash
# File inventory — what was touched on this branch
git diff origin/dev..HEAD --stat                 # diffstat for the "Scope" cell
DIFF_FILES=$(git diff origin/dev..HEAD --name-only)

# Commit references for the "References" cell
COMMITS=$(git log origin/dev..HEAD --oneline --format="%h")
COMMIT_SUBJECTS=$(git log origin/dev..HEAD --format="%s" | head -5)
```

If the branch has no upstream divergence (e.g., already merged), fall back to:

```bash
git log -10 --grep="#${ISSUE_NUM}" --format="%h %s" 2>/dev/null
```

### 2.2 PR body (when an open or recently-merged PR exists)

```bash
PR_JSON=$(gh pr view --json number,title,body,mergedAt 2>/dev/null)
if [ -n "$PR_JSON" ]; then
  PR_NUM=$(echo "$PR_JSON" | jq -r '.number')
  PR_BODY=$(echo "$PR_JSON" | jq -r '.body')
  # Extract the "## Summary" section if present — that's the scope summary
  PR_SUMMARY=$(echo "$PR_BODY" | awk '/^## Summary/{flag=1;next} /^## /{flag=0} flag && NF' | head -3)
fi
```

The PR's "## Summary" is usually the cleanest one-paragraph scope description. Fall back to commit subjects if no PR or no Summary section.

### 2.3 Reviewer's retro-notes (when a `/wt` session exists)

The reviewer agent writes strategic observations to `.kris/wt-sessions/<SESSION_ID>/retro-notes.md` during review (see `.kris/templates/wt/reviewer.md` → "Retrospective Hooks"). This is the autofill source for judgment sections.

```bash
RETRO_NOTES=""
if [ -n "$SESSION_DIR" ] && [ -f "${SESSION_DIR}/retro-notes.md" ]; then
  # Skip the "no notes" sentinel — empty judgment sections stay empty
  if ! grep -q "<!-- no retrospective notes for this review -->" "${SESSION_DIR}/retro-notes.md"; then
    RETRO_NOTES=$(cat "${SESSION_DIR}/retro-notes.md")
  fi
fi
```

The reviewer's notes are organized into four sections (Surprises / Deferred concerns / Follow-up suggestions / Design observations). Map them into the retrospective's body as:

| Reviewer notes section   | Retrospective body section            |
|--------------------------|---------------------------------------|
| Surprises                | "What WORKED" or "What DIDN'T WORK" (heuristic — positive surprise → worked; negative → didn't) |
| Deferred concerns        | "Known follow-ups not covered by this iteration" |
| Follow-up suggestions    | "Open questions for future iterations" |
| Design observations      | "Lessons — universal rules to apply next time" |

Every autofilled line gets a `<!-- DRAFT: review and edit -->` marker so the user knows it's reviewer-sourced, not editorial. Markers stay until the user explicitly accepts them in a subsequent commit.

### 2.4 User-provided body (MODE D ad-hoc)

In ad-hoc mode, the conversation IS the source. The skill prompts:

```
Ad-hoc capture for "lessons from Apache cert debug".

Paste body content (or leave empty for a blank template):
```

Whatever the user provides goes into the body. If empty, scaffold a blank template per Phase 3 CREATE shape.

### 2.5 Output of Phase 2

```bash
SOURCE_SUMMARY=<scope description for the issues-table row>
SOURCE_REFS=<PR # + commit short-hashes>
SOURCE_NOTES=<reviewer retro-notes content, or empty>
SHIPPED_DATE=$(date +%Y-%m-%d)    # today, per "Shipped" semantics
```

---

## Phase 3: Apply Updates

### 3.1 UPDATE mode

The retrospective already exists. Two surgical edits:

#### 3.1.a Append to "Issues involved" table

Locate the `| Issue | Shipped | Scope | References |` table. Append a new row at the bottom:

```markdown
| [#${ISSUE_NUM}](https://github.com/mis-bghmc/bgh-katalyst/issues/${ISSUE_NUM}) | ${SHIPPED_DATE} | ${SOURCE_SUMMARY} | PR [#${PR_NUM}](https://github.com/mis-bghmc/bgh-katalyst/pull/${PR_NUM}); commits ${COMMIT_HASHES_INLINE} |
```

If `ISSUE_NUM` is empty (ad-hoc), use a placeholder text in the Issue column (e.g., `manual capture` or the topic descriptor) and omit the GitHub link.

#### 3.1.b Append an "Iteration log" entry at the TOP of the log section

Append-only history — newest at the top per the canonical format:

```markdown
### ${SHIPPED_DATE} — #${ISSUE_NUM} ${SCOPE_BLURB} (commits ${COMMIT_HASHES_INLINE})

**On the topic:**
- ${COMMIT_SUBJECT_1}
- ${COMMIT_SUBJECT_2}
- ${COMMIT_SUBJECT_3}

**On this document:**
- Appended issue row + iteration log entry.
- ${IF body sections updated:} Updated <body-sections-that-changed> with drafted lines from reviewer's retro-notes.
```

The "On the topic" bullets autofill from commit subjects (capped at 5; user can prune). The "On this document" bullet always documents what THIS run changed in the retrospective file itself.

#### 3.1.c Body section updates (only if retro-notes content was found)

For each reviewer-notes section that maps to a body section (Phase 2.3 table), insert drafted lines into the CURRENT body — don't fork, don't append-only. The retrospective body reflects "current state" of the topic; the log captures change history.

Every inserted line is marked:

```markdown
- <!-- DRAFT: review and edit --> Reviewer flagged that the `selectPaginated` shim's flat-array branch could mask real totals when `data.length === _limit`. Worth adding a dev-mode console.warn.
```

If reviewer notes are absent, body sections stay untouched. No fake DRAFT lines. The user can dictate edits in conversation.

#### 3.1.d Diff sanity check (mandatory before writing)

After computing the diff in memory, present a summary:

```
About to update DOTS_DOCUMENTS_PAGINATION.md:
  + 1 row added to "Issues involved" table (#605)
  + 1 entry prepended to "Iteration log" (2026-05-11 — 4 lines)
  + 0 body sections modified (no retro-notes for this iteration)

Proceed? [Y/n]: _
```

`Y` writes; `n` aborts cleanly with no file changes.

### 3.2 CREATE mode

The retrospective doesn't exist. Scaffold from the DOTS_DOCUMENTS_PAGINATION structure as a template. Produce:

```markdown
# ${TOPIC_TITLE} — Retrospective

> **Living document.** Every iteration of work on this topic appends to the **Issues involved** table below and the **Iteration log** at the bottom. Body sections reflect the CURRENT state — for change history, see the log.
>
> **Topic scope:** <!-- DRAFT: review and edit --> ${AUTOFILLED_SCOPE_FROM_PR_OR_COMMITS}.
>
> **Pattern reference:** <!-- DRAFT: link to relevant guide if any -->

## Issues involved

Each row is a discrete iteration that touched this topic. Append to the bottom as new work lands.

| Issue | Shipped | Scope | References |
|-------|---------|-------|------------|
| ${SEED_ROW_BUILT_FROM_PHASE_2_SOURCES} |

**How to add a new row:** when shipping a follow-up, append the issue, ship date, scope summary, and PR/commit refs. Then update the relevant body sections inline (don't fork — keep current state coherent) and add a dated entry to the **Iteration log** at the end.

---

## What changed (one-paragraph summary)

<!-- DRAFT: review and edit --> ${AUTOFILLED_FROM_PR_SUMMARY_OR_COMMIT_SUBJECTS}

---

## Architecture

<!-- DRAFT: fill in once stable. Reference DOTS_DOCUMENTS_PAGINATION.md for the 3-layer ASCII diagram style. -->

---

## Files involved

<!-- DRAFT: autofilled from `git diff origin/dev..HEAD --stat` — review and curate. -->

${AUTOFILLED_FILES_TABLE}

---

## How the pieces fit together

<!-- DRAFT: cross-layer flow narration. Fill in once stable. -->

---

## ⚠️ Critical gotcha

<!-- DRAFT: any landmines this iteration hit? Fill in or remove this section. -->

---

## Smoke-test checklist

<!-- DRAFT: list of manual checks that exercise the failure modes you'd want to catch in review. -->

- [ ] ...

---

## Retrospective — What worked, what didn't, what to do differently

### What WORKED (keep these decisions)

${AUTOFILLED_FROM_REVIEWER_NOTES_POSITIVE_SURPRISES_OR_BLANK}

### What DIDN'T WORK (and why)

${AUTOFILLED_FROM_REVIEWER_NOTES_NEGATIVE_SURPRISES_OR_BLANK}

### Lessons — universal rules to apply next time

${AUTOFILLED_FROM_REVIEWER_DESIGN_OBSERVATIONS_OR_BLANK}

### Open questions for future iterations

${AUTOFILLED_FROM_REVIEWER_FOLLOW_UP_SUGGESTIONS_OR_BLANK}

---

## Lessons-learned memories worth checking before extending

<!-- DRAFT: link to relevant feedback_*.md memories. -->

---

## Iteration log

Append-only history of what each iteration changed in this document and on the topic. Newest at the top.

### ${SHIPPED_DATE} — #${ISSUE_NUM} ${SCOPE_BLURB} (initial creation)

**On the topic:**
${AUTOFILLED_COMMIT_BULLETS}

**On this document (initial creation):**
- Scaffolded from DOTS_DOCUMENTS_PAGINATION structure (issues table + iteration log + body section shell).
- Seeded first row in "Issues involved" with #${ISSUE_NUM}.
- Autofilled drafts in body sections from reviewer's retro-notes (where present).
```

### 3.3 Output of Phase 3

```bash
WRITES=<list of files modified, with line counts before/after>
```

---

## Phase 4: Update INDEX.md

`.kris/docs/INDEX.md` carries a "Retrospectives" section listing every retrospective with its issue count and last-updated date. `/retrospect` keeps it in sync.

### 4.1 UPDATE mode

Find the existing INDEX.md row for `TARGET_PATH`'s filename. Update:
- **Issues count:** increment by 1 (count rows in the "Issues involved" table).
- **Last updated:** today's date.

### 4.2 CREATE mode

Append a new row to the "Retrospectives" section table. Format mirrors the existing entries — read INDEX.md before writing to match the table shape exactly.

### 4.3 Skip when `--no-update-index`

For batch operations or dry-run scenarios, `--no-update-index` skips Phase 4 entirely. The retrospective file is written; INDEX.md drift can be reconciled later.

---

## Phase 5: Output

```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
✅ /retrospect COMPLETE
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

📍 Target: .kris/docs/retrospectives/DOTS_DOCUMENTS_PAGINATION.md
   Mode:   update

✍️  Wrote:
   + 1 row to "Issues involved" (#605)
   + 1 entry to "Iteration log" (2026-05-11)
   + 3 drafted lines in "What WORKED" / "Lessons" sections
     (each marked <!-- DRAFT: review and edit -->)

📚 INDEX.md updated:
   DOTS_DOCUMENTS_PAGINATION.md — 3 iterations (last: 2026-05-11)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

📌 NEXT STEPS:

   1. Review the DRAFT-marked lines. Accept or edit them.
   2. Commit:
      git add .kris/docs/retrospectives/DOTS_DOCUMENTS_PAGINATION.md .kris/docs/INDEX.md
      /commit
      (or batch into your next /fix run)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

For CREATE mode the message reflects the scaffold:

```
✍️  Created: .kris/docs/retrospectives/DOTS_BULK_ACTIONS.md
   + Seeded with 1 row in "Issues involved" (#605)
   + Body sections scaffolded (all marked <!-- DRAFT --> for editorial pass)
   + Iteration log seeded with initial-creation entry
```

---

## Flags

### `--new <topic>`

Force CREATE mode — skip Phase 1's match phase entirely. Useful when you know the topic is genuinely new and want to avoid an accidental auto-target on a near-match.

```bash
/retrospect --new dots-bulk-actions
/retrospect --new "DOTS bulk actions"
```

### `--ad-hoc <topic>`

Explicit MODE D — no branch context required. The topic is purely user-provided; no autofill from git/PR/session.

```bash
/retrospect --ad-hoc "lessons from Apache cert debug"
```

Body content can be provided in the same conversation turn; otherwise scaffold a blank template per Phase 3 CREATE.

### `--info`

Dry-run — show what `/retrospect` would do, no writes.

```bash
/retrospect --info
/retrospect dots-pagination --info
```

### `--no-update-index`

Skip Phase 4 (INDEX.md update). Useful for batch operations or when INDEX.md is being regenerated separately.

```bash
/retrospect --no-update-index
```

---

## Integration

| Skill | Relationship |
|-------|--------------|
| `/adjourn` | Audit-line scans `retrospectives/` for a topic match; surfaces a suggestion line when match found OR when work is incremental and no retrospective exists. See `adjourn.md` Phase 2 sub-check "Retrospective coverage". |
| `/fix` | End-of-fix line suggests `/retrospect` when work is incremental (branch-suffix `<N>a-` / `<N>b-` pattern OR multiple commits on overlapping files in last 30 days). See `fix.md` Phase 9 "NEXT STEPS". |
| `/wt` reviewer agent | Writes `.kris/wt-sessions/<id>/retro-notes.md` per `reviewer.md` "Retrospective Hooks". `/retrospect` autofill reads this file for judgment-section drafts. |
| `/preview` | No direct relationship — `/preview` is a pre-PR test loop; `/retrospect` is a post-PR knowledge-capture step. |

---

## Safety

1. **Inline runtime** — no branch mutation, no commit, no push, no tab spawn.
2. **Diff sanity check** before writing — user sees a summary of changes and confirms before any file is touched (UPDATE mode 3.1.d).
3. **DRAFT markers** on every autofilled judgment line — low-friction safety rail; user knows which lines need an editorial pass.
4. **No git operations beyond reading state** — `git diff`, `git log`, `gh pr view` are all read-only. The user commits the retrospective changes separately (via `/commit` or batched into the next `/fix`).
5. **Branch contract enforced** — start branch == end branch. Any divergence indicates a bug in this skill.

---

## Error Handling

| Error | Resolution |
|-------|------------|
| `retrospectives/` directory missing | Create it: `mkdir -p .kris/docs/retrospectives`. First-time use is fine. |
| `gh` not authenticated | Skip Phase 2.2 (PR body). Autofill still works from git history; PR_NUM stays empty in the row. |
| `git log origin/dev..HEAD` returns nothing (branch already merged, or no upstream) | Fall back to `git log -10 --grep="#${ISSUE_NUM}"`. |
| Multiple PRs on the branch (rare) | Pick the most recent; surface a one-line note in Phase 5 output. |
| User aborts at Phase 3.1.d "Proceed? [Y/n]" | Exit cleanly, no file changes, no INDEX.md update. |
| Picker shows no good candidates AND user picks `[n]` | MODE flips to create; proceed to Phase 2 CREATE-mode scaffolding. |

---

## Examples

### Example 1: Inline UPDATE inside a worktree

```
> /retrospect

[Phase 0] Mode A — inside worktree feature/427c-pagination-bugfix
[Phase 1] Strong match (90%): DOTS_DOCUMENTS_PAGINATION.md
[Phase 2] Sources: 2 commits, PR #594, retro-notes.md (3 sections present)
[Phase 3] About to update DOTS_DOCUMENTS_PAGINATION.md:
            + 1 row to "Issues involved" (#427c)
            + 1 entry to "Iteration log"
            + 4 drafted lines in body sections

          Proceed? [Y/n]: Y

[Phase 4] INDEX.md updated.
[Phase 5] ✅ Done. Review DRAFT lines, then /commit.
```

### Example 2: Ad-hoc capture from main repo

```
> /retrospect --ad-hoc "lessons from Apache cert debug"

[Phase 0] Mode D — ad-hoc capture
[Phase 1] No match. Create new retrospective at:
          .kris/docs/retrospectives/APACHE_CERT_DEBUG.md?
          [Y/n]: Y

[Phase 2] No git diff (ad-hoc). Awaiting body content (or empty for blank template).
[Phase 3] Scaffolded APACHE_CERT_DEBUG.md from canonical structure.
[Phase 4] INDEX.md updated (new entry).
[Phase 5] ✅ Done. Body sections marked <!-- DRAFT --> for editorial pass.
```

### Example 3: Dry-run with `--info`

```
> /retrospect dots-pagination --info

[Phase 0] Mode B — explicit topic "dots-pagination"
[Phase 1] Strong match (100%): DOTS_DOCUMENTS_PAGINATION.md
[Phase 2] Would source: git log -10 (no branch context), no PR (main repo dev)
[Phase 3] Would write:
            + 0 rows (no issue # in main repo context)
            + 0 iteration entries
            + 0 body section updates

          ℹ️  Nothing to do. Run inside a worktree or pass --ad-hoc with content.
```

### Example 4: Multi-match picker

```
> /retrospect filter

[Phase 0] Mode B — explicit topic "filter"
[Phase 1] Multiple retrospectives match "filter":

            1. [75%] UNIVERSAL_FILTER_PANEL.md
                     "Universal filter panel — shape-engine wire-up" (1 iteration)

            2. [65%] SPECIFIC_FIELD_SEARCH_PANEL.md
                     "Specific-field search panel — JSON shape contract" (2 iterations)

          Which? [1/2] or [n] to create new: 2

[Phase 2] ...
```

---

## Why no tab spawn?

Compared to `/fix` and `/do` (which spawn tabs in worktrees to isolate long-running, branch-mutating work):

- **No branch mutation.** `/retrospect` writes only to `.kris/docs/retrospectives/` and `.kris/docs/INDEX.md`. Neither is on the critical path of any feature branch.
- **No git commits.** The user commits via `/commit` or batches into the next `/fix`.
- **Short-lived.** Median runtime ~30s (fuzzy match + diff compute + two file writes).
- **No parallel-agent risk.** The files this skill touches don't overlap with any worktree's source tree.

`/preview` is the closest sister skill (also inline, also no tab spawn). The difference: `/preview` mutates main repo's `dev` branch, so it has a strict branch contract that returns main repo to its starting branch. `/retrospect` doesn't touch branches at all — its branch contract is "nothing changes."

---

## Why DRAFT markers, not "review before write"?

Per `feedback_skills-session-scoping.md` and the user's stated autonomy preference: minimize ceremony, maximize agent autonomy. The DRAFT marker is the cheapest possible safety rail:

- **Doesn't block flow.** The file is written; work continues.
- **Self-documents the source.** A user reading the retrospective later sees which lines came from the reviewer agent vs. human editorial.
- **Cleaned up incrementally.** The user removes `<!-- DRAFT: review and edit -->` markers as they endorse each line, on whatever cadence suits them. No big "review pass" event required.
- **Survives partial review.** If only half the DRAFT lines are reviewed before commit, the other half stays marked — the next reader knows where the boundary is.

Compare to the alternative: "show all autofills, ask user to approve each before writing." That's a 5-question prompt on every run. The DRAFT pattern collapses it to one diff-summary confirmation (Phase 3.1.d).

---

## Scope Boundaries

`/retrospect` writes to:
- `.kris/docs/retrospectives/*.md` (the file being created or updated)
- `.kris/docs/INDEX.md` (the index entry — unless `--no-update-index`)

`/retrospect` does NOT:
- Commit, push, or otherwise mutate git state beyond `git read-tree` style reads.
- Modify task files in `.kris/tasks/`.
- Modify session files in `.kris/wt-sessions/` (it READS `retro-notes.md` but never writes there).
- Update the changelog (that's `/commit` / `/fix`).
- Modify CLAUDE.md or context files.

If the user's intent requires any of the above, route them to the appropriate skill (`/commit`, `/fix`, `/do`).
