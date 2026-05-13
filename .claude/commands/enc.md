# /enc — Capture Mid-Session Scope Drift

Quick fire-and-forget capture for ideas, issues, or end-user feedback that surface mid-conversation but aren't in the current task's scope. Writes a single markdown file under `.kris/tasks/gaps/pending/` and announces it in one line. **Never derails the active task. Never asks more than one question.**

---

## Invocation forms

| Form | Meaning |
|------|---------|
| `/enc <title>` | Dev-drift mode. Auto-classify P-level from title text. Write immediately. |
| `/enc P2 <title>` | Dev-drift mode with explicit P-level. Write immediately. |
| `/enc --fb "<quote>"` | User-feedback mode, inline quote. URL optional via `--url`. |
| `/enc --fb "<quote>" --url <url>` | Feedback + URL in one line. |
| `/enc --fb` | Feedback mode, paste-and-go. Next line(s) are the raw feedback; a line starting with `http` is treated as the URL. |
| `/enc` (no args) | Scan the last few conversation turns for the drift candidate. Propose a one-line title + P-level. Write on confirm. |

Aliases: `/encounter`, `/enc-fb` (the latter implies `--fb`).

---

## ARGUMENTS

$ARGUMENTS

---

## Procedure

### 1. Parse the invocation

From `$ARGUMENTS`, determine:
- **Mode**: `dev-drift` (default) or `feedback` (`--fb` flag present, or invoked via `/enc-fb`)
- **Explicit P-level**: token matching `^P[0-3]$` (or `P?`) at the start of args, after stripping flags
- **Title / raw text**: everything else
- **URL** (feedback mode only): `--url <value>` flag, OR a line starting with `http://` / `https://` if multi-line paste

If `$ARGUMENTS` is empty or only flags: enter "no-args" branch (step 7).

### 2. Locate the gaps folder

Find the git root:
```bash
git rev-parse --show-toplevel
```

Target directory: `<git-root>/.kris/tasks/gaps/pending/`.

**Gap kanban:** newly captured gaps always go into `pending/`. The `shipped/` sibling exists for gaps that have been promoted to a task. Dismiss-as-noise = plain delete.

If `.kris/tasks/gaps/pending/` does not exist, create it (and any parents). If there is no git root (the cwd is not a git repo), fall back to `./.kris/tasks/gaps/pending/` under the current working directory and warn in the announcement line.

### 3. Classify P-level (only if not explicit)

Run a case-insensitive substring scan over the title + raw text. **Highest level wins.** Combine both vocab sets in feedback mode.

| Level | Dev-style triggers | User-style triggers (feedback adds) |
|-------|--------------------|--------------------------------------|
| **P0** | vulnerability, exploit, security, data loss, crash, production down, critical, leak | "saw someone else's data", "wrong amount", "money missing", "logged in as wrong person" |
| **P1** | bug, doesn't work, missing, fails, error, incorrect, wrong, needs fix, must have, compliance, blocked, broken | "doesn't work", "broke", "broken", "stuck", "spins forever", "won't let me", "I can't", "lost my data", "disappeared", "froze", "hung", "lacking" |
| **P2** | improve, enhance, add, feature, refactor, review, investigate, consider, should, implement, update, replicate | "confusing", "weird", "awkward", "should be easier", "wish it would", "annoying", "would be better if" |
| **P3** | rename, tweak, cosmetic, minor, low priority, someday, would be nice, polish | "looks ugly", "tiny issue", "barely noticed", "small thing", "label", "icon vs text" |

**Defaults if nothing matches:**
- Dev-drift mode → `P2`
- Feedback mode with no URL **and** no severity signal → `P?` (explicit "unclassifiable, needs triage")
- Feedback mode with URL but no severity signal → `P2`

### 4. Generate slug + filename

- Slug: 4–8 words from title (or first sentence of raw text in feedback mode), lowercase, hyphens only, no punctuation, no `.md`.
- Date: today, ISO format (`yyyy-mm-dd`).
- Filename:
  - Dev-drift: `<P>_<date>_<slug>.md`
  - Feedback: `<P>_<date>_fb-<slug>.md` (the `fb-` prefix makes feedback gaps greppable as a class)
- If a file with that name already exists, append `-2`, `-3`, … until unique.

### 5. Decode URL (feedback mode, if URL provided)

Match the URL path against fnl-cat route patterns. If routes aren't recognized, write `UNKNOWN — verify routing` and let the user fill in `affected_area` later. (This project's routing is still evolving — add patterns to this table as conventions stabilize.)

| URL pattern | Decoded affected_area |
|-------------|----------------------|
| `/admin/{module}/{tab}` | `Admin > {module} > {tab}` |
| `/booking/{rest}` | `Booking > {rest}` |
| anything else | `UNKNOWN — verify routing` |

Title-case `{module}` and `{tab}` segments (split on `-`, capitalize words).

### 6. PII pre-scan (feedback mode only)

Run these regexes over the raw text:
- Email: `[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}`
- 10+ digit runs: `\d{10,}`
- PH mobile: `(\+?63|0)9\d{9}`

If **any** match, set `pii_warning: true` in frontmatter. **Do not redact.** The raw text stays intact — reproduction value > sanitization at capture time. The warning surfaces in the announcement; user decides whether to redact before sharing externally.

Note: 10+ digit runs will false-positive on tracking IDs and other legitimate identifiers. False positives are fine; the warning is advisory, not a block.

This project handles passenger PII (RA 10173 — see `.kris/context/data-privacy.md`). Always respect the warning when copying gap contents into external tickets, screenshots, or PRs.

### 7. No-args branch

If invoked as bare `/enc`:
1. Scan the last ~10 conversation turns for a candidate drift topic (something the user mentioned in passing, or that you noticed, but isn't part of the active task).
2. Propose a one-line title + classified P-level.
3. Ask: `📝 capture this as <P> gap: "<title>"? (y/n)` — this is the *only* question the skill ever asks. One line, one answer.
4. On `y`: proceed to write. On `n`: exit silently.

### 8. Write the file

**Dev-drift body template:**
```markdown
---
captured: <yyyy-mm-dd>
captured_by: agent | user
p_level: P<n>
origin: <issue#-or-task-slug-from-current-branch-if-any>
related_files: <optional; only fill if obvious from context>
---

## What
<one paragraph: the issue or idea, verbatim if user-supplied, agent-restated if scanned>

## Why it matters
<1–2 sentences: impact / who's affected / what breaks if ignored>

## Suggested approach (optional, omit if not obvious)
<one line>

## Next-session bootstrap
- <2–4 bullets: enough for a fresh session to act cold>
- <include relevant file paths, branch context, related issue numbers if known>
```

**Feedback body template:**
```markdown
---
captured: <yyyy-mm-dd>
captured_by: user-feedback
p_level: P<n> | P?
origin: end-user-feedback
affected_area: <decoded from URL> | UNKNOWN
source_url: <raw URL, omit if not provided>
pii_warning: true | <omit if false>
---

## What
> "<verbatim user quote — blockquote, no paraphrasing>"

<one-line agent restatement of what the user is describing>

## Why it matters
<1–2 sentences inferred from severity + area>

## Missing context (resolve before acting)
- [ ] Browser / device (mobile? desktop? Chrome / Safari?)
- [ ] Reproduction steps (what did they click to reach this?)
- [ ] Account / role (which system access? regular user vs admin?)
- [ ] Frequency (always? intermittent? one-off?)
- [ ] Timestamp (when did it happen? logs needed?)
<- add 1–2 area-specific bullets if affected_area is decoded>

## Next-session bootstrap
- Source: end-user feedback
- URL: <raw URL or "not provided">
- Decoded location: <affected_area>
- <1–3 bullets on files/pages/components to look at first, based on affected_area>
```

The verbatim blockquote in feedback mode is **mandatory**. User phrasing is evidence; never paraphrase it away.

If `captured_by` cannot be determined from context (e.g., agent invocation vs user invocation), default to `user` in dev-drift and `user-feedback` in `--fb` mode.

### 9. Announce in one line

Print exactly one line and return to the prior task:
```
📝 captured <P> gap: <title> → .kris/tasks/gaps/pending/<filename>
```

If `pii_warning` is true, add `(⚠️ may contain PII)`:
```
📝 captured P1 gap (⚠️ may contain PII): receive button hangs → .kris/tasks/gaps/pending/P1_2026-05-11_fb-receive-button-hangs.md
```

If git root was not found and the fallback path was used:
```
📝 captured P2 gap: <title> → ./.kris/tasks/gaps/pending/<filename> (⚠️ not in a git repo; check location)
```

### 10. Return to the prior task

Do **not** offer next steps. Do **not** ask follow-up questions. Do **not** start triaging or `/worktree`-ing the gap. The whole point of `/enc` is that it's a side-channel — the active task continues as if nothing happened.

If the user later wants to act on the gap, they can run `/worktree "<paste gap title>"` to graduate it into a full task. The gap file stays in `.kris/tasks/gaps/pending/` until promoted or deleted.

---

## Rules of engagement

1. **No grilling.** This is the opposite of `/worktree`. Capture > completeness.
2. **One question max.** Only the no-args confirm prompt. Never ask follow-ups in normal flow.
3. **Verbatim wins.** In feedback mode, preserve the user's exact words. Paraphrasing destroys evidence.
4. **Honest unknowns.** If you can't classify, write `P?` and `UNKNOWN` — never guess to look helpful.
5. **No side effects.** Don't touch any other files. Don't create issues. Don't commit. Don't open PRs. Don't start `/worktree`. Just write the one gap file.
6. **Cross-platform.** Use the Write tool for the file write (works on Windows, macOS, Linux uniformly). Use Bash for `git rev-parse`. No PowerShell-specific syntax inside the gap body.

---

## Deployment & offload pattern

This command follows the project's `.claude/` ↔ `.kris/` mirror convention:

- **Live / active:** `.claude/commands/enc.md` — what Claude Code reads at runtime
- **Backup mirror:** `.kris/commands/enc.md` — persistent store, committed to git
- **Aliases live in both:** `encounter.md`, `enc-fb.md`

When slimming a session's context, you can delete `.claude/commands/enc.md` (and the two aliases) — restore them from `.kris/commands/` when needed.
