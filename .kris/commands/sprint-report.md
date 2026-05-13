# /sprint-report - Stakeholder-Ready Sprint Summary

Generate a polished, presentation-ready sprint report from git history, changelogs, GitHub PRs/issues, and pending tasks. Designed for two audiences: non-technical stakeholders first, then technical leads.

> **Philosophy:**
> - Reports should tell a **story**, not dump raw data
> - Non-technical audience comes first — "what got done and why it matters"
> - Technical details follow for engineering leads
> - Output is directly presentable AND feedable to a presentation-building agent
> - Auto-heals changelog gaps when detected

---

## Quick Start

```bash
/sprint-report                              # Current week (Monday to now)
/sprint-report 2026-03-17                   # From date to now
/sprint-report 2026-03-10 2026-03-23        # Specific date range
/sprint-report --dry-run                    # Preview data sources, don't generate
/sprint-report --no-update                  # Skip changelog gap-fill
```

---

## How It Works

```
┌─────────────────────────────────────────────────────────┐
│                    /sprint-report                        │
├─────────────────────────────────────────────────────────┤
│                                                         │
│  Phase 1: Parse Arguments & Resolve Dates               │
│     ↓                                                   │
│  Phase 2: Gather Raw Data                               │
│     ├── Git log (dev branch)                            │
│     ├── GitHub PRs (merged in range)                    │
│     ├── GitHub Issues (closed in range)                 │
│     ├── Changelogs (.kris/changelogs/)                  │
│     └── Pending tasks (.kris/tasks/pending/)            │
│     ↓                                                   │
│  Phase 3: Cross-Reference & Reconcile                   │
│     ├── Detect changelog gaps (commits without entries) │
│     ├── Flag anomalies (closed issues w/o commits)      │
│     └── Auto-update changelogs (unless --no-update)     │
│     ↓                                                   │
│  Phase 4: Compute Metrics                               │
│     ├── Commit stats (count, LOC, files)                │
│     ├── Issue velocity (opened vs closed)               │
│     ├── PR throughput (time-to-merge)                   │
│     ├── Area breakdown (Backend/Frontend/Full Stack)    │
│     └── Contributor breakdown                           │
│     ↓                                                   │
│  Phase 5: Generate Report                               │
│     ├── Executive Summary (non-technical)               │
│     ├── Technical Summary (engineering leads)           │
│     ├── Metrics Dashboard                               │
│     ├── What's Next                                     │
│     └── Anomalies & Notes                               │
│     ↓                                                   │
│  Phase 6: Write Output                                  │
│     └── .kris/reports/YYYY-MM-DD_sprint-report.md       │
│                                                         │
└─────────────────────────────────────────────────────────┘
```

---

## Phase 1: Parse Arguments & Resolve Dates

### 1.1 Argument Parsing

| Input | Interpretation |
|-------|---------------|
| No args | Current week: Monday of this week → today |
| Single date `YYYY-MM-DD` | From that date → today |
| Two dates `YYYY-MM-DD YYYY-MM-DD` | Explicit range (inclusive) |
| `--dry-run` | Show what data would be gathered, don't generate |
| `--no-update` | Skip changelog auto-update in Phase 3 |

### 1.2 Resolve Week Boundaries

```bash
# Determine the Monday of a given date's week
# If single date given, use it as start, today as end
# If no dates, find this week's Monday

# Store resolved values
START_DATE="YYYY-MM-DD"
END_DATE="YYYY-MM-DD"
```

### 1.3 Identify Changelog Files

Find all changelog files in `.kris/changelogs/` whose date ranges overlap with `START_DATE..END_DATE`.

```bash
# List changelogs and match overlapping ranges
ls .kris/changelogs/*.md
# Parse filenames: YYYY-MM-DD_to_YYYY-MM-DD.md
# Include file if: file_start <= END_DATE AND file_end >= START_DATE
```

---

## Phase 2: Gather Raw Data

### 2.1 Git Log (Dev Branch)

```bash
# All commits on dev within the date range
git log origin/dev --after="$START_DATE" --before="$END_DATE +1 day" \
  --pretty=format:"%H|%h|%s|%b|%an|%ae|%ai" \
  --reverse

# Diff stats for the range
git log origin/dev --after="$START_DATE" --before="$END_DATE +1 day" \
  --shortstat

# Per-commit file stats
git log origin/dev --after="$START_DATE" --before="$END_DATE +1 day" \
  --pretty=format:"%h" --numstat
```

**Extract from each commit:**
- Hash (short + full)
- Subject line → parse type prefix (`feat`, `fix`, `refactor`, `chore`, `tech`, `docs`, `perf`, `test`)
- Scope from parentheses: `feat(sa)` → scope = `sa`
- Issue references: `#123` patterns in subject and body
- Version tag: `vW.X.Y.Z` in body
- Author name and email
- Date

### 2.2 GitHub PRs (Merged in Range)

```bash
# List merged PRs in date range
gh pr list --state merged \
  --search "merged:${START_DATE}..${END_DATE}" \
  --json number,title,author,mergedAt,body,labels,comments,reviews,additions,deletions,changedFiles \
  --limit 100

# For each PR, get review comments if any
# gh api repos/{owner}/{repo}/pulls/{number}/reviews
```

**Extract from each PR:**
- PR number, title, author
- Merge date
- Labels
- Review comments (count, reviewers)
- Lines added/deleted, files changed
- Time from PR creation to merge (time-to-merge)
- Linked issue numbers from body

### 2.3 GitHub Issues (Closed in Range)

```bash
# Closed issues in date range
gh issue list --state closed \
  --search "closed:${START_DATE}..${END_DATE}" \
  --json number,title,labels,closedAt,author,body \
  --limit 100

# Also get currently open issues for "What's Next"
gh issue list --state open \
  --json number,title,labels,createdAt,author \
  --limit 50
```

**Flag anomalies:**
- Issues closed without a corresponding commit/PR → note as "Closed without code change (duplicate/won't-fix)"
- These should NOT happen per user's policy — flag prominently

### 2.4 Read Existing Changelogs

Read all overlapping changelog files identified in Phase 1.3. Parse each entry to extract:
- Commit hash
- Issue number
- PR number
- Area (Backend/Frontend/Full Stack/Docs)
- Version
- Change descriptions

### 2.5 Read Pending Tasks

```bash
# Read all files in .kris/tasks/pending/
ls .kris/tasks/pending/*.md
```

Parse each for: title, priority (from filename prefix like `CRITICAL-*`, priority keywords), creation date, description summary.

---

## Phase 3: Cross-Reference & Reconcile

### 3.1 Detect Changelog Gaps

Compare commits from Phase 2.1 against changelog entries from Phase 2.4.

**For each commit in git log:**
1. Search changelog entries for matching commit hash (short hash match)
2. If no match found → this is a **gap**
3. Collect all gaps

### 3.2 Auto-Update Changelogs (unless `--no-update`)

For each gap detected:
1. Determine which changelog file the commit belongs to (by date)
2. Create a new entry following the standard format:

```markdown
#### {type}({scope}): {description} ({version})
- **Commit**: `{hash}`
- **Issue**: #{number} (if detected)
- **PR**: #{number} (if detected)
- **Area**: {Backend|Frontend|Full Stack|Docs/Config}
- **Files**: {file list from git show}
- **Changes**:
  - {parsed from commit message body, or summarized from diff}
```

3. Insert entry under the correct day heading
4. Update the Summary table counts

**Report what was auto-filled:**
```
📝 Changelog updated: added 3 missing entries to 2026-03-17_to_2026-03-23.md
   - ce43eac8: feat(rbac): add system-scoped role assignments
   - 06330466: feat(cds): add address fields to CreateBranchModal
   - 17f06205: fix(frontend): add missing useGlobalErrorHandler composable
```

### 3.3 Flag Anomalies

Collect and categorize:
- **Orphaned closes**: Issues closed without commits (should not happen — flag as warning)
- **Unlinked commits**: Commits without issue references (note for traceability)
- **Stale PRs**: PRs opened in range but not yet merged

---

## Phase 4: Compute Metrics

### 4.1 Commit Metrics

| Metric | How to compute |
|--------|---------------|
| Total commits | Count from git log |
| Lines added | Sum `--shortstat` insertions |
| Lines deleted | Sum `--shortstat` deletions |
| Net lines | Added - Deleted |
| Files changed | Count unique files from `--numstat` |
| Commits/day | Total commits ÷ business days in range |

### 4.2 Issue Metrics

| Metric | How to compute |
|--------|---------------|
| Issues resolved | Count closed issues with matching commits |
| Issues opened | Count issues created in date range |
| Net velocity | Resolved - Opened (positive = burning down) |
| Resolution rate | Resolved ÷ (Open at start + Opened) × 100% |

### 4.3 PR Metrics

| Metric | How to compute |
|--------|---------------|
| PRs merged | Count from gh pr list |
| Avg time-to-merge | Mean of (mergedAt - createdAt) per PR |
| Fastest merge | Min time-to-merge |
| Slowest merge | Max time-to-merge |
| Avg review comments | Mean of review comment count per PR |

### 4.4 Area Breakdown

Categorize each commit by area:
- **Backend**: Only PHP files (`app/`, `database/`, `routes/`)
- **Frontend**: Only JS/TS/Vue files (`resources/js/`)
- **Full Stack**: Both backend and frontend files
- **Docs/Config**: Only `.md`, `.json`, config files

### 4.5 Contributor Breakdown

Group commits by author. For each:
- Commit count
- Lines changed (added + deleted)
- Primary area (most commits in which area)

---

## Phase 5: Generate Report

### Output File

```
.kris/reports/{END_DATE}_sprint-report.md
```

If a report for the same end date already exists, overwrite it (reports are regenerable).

### Report Template

The report MUST follow this exact structure. Use emojis, horizontal rules, and clear visual hierarchy for presentation readability.

```markdown
# 🚀 Sprint Report: {START_DATE} → {END_DATE}

> Generated on {TODAY} | BGH Katalyst Platform
> Version range: {FIRST_VERSION} → {LATEST_VERSION}

---

## 📋 Executive Summary

> *For stakeholders and project managers — what got done this sprint in plain language.*

### 🎯 Sprint Highlights

{Write 3-5 bullet points summarizing the MOST IMPACTFUL changes in non-technical language.
Focus on user-facing outcomes, not implementation details.
Use action verbs: "Added", "Fixed", "Improved", "Secured", "Streamlined".}

- ✅ **{Highlight 1}** — {One sentence explaining the user/business impact}
- ✅ **{Highlight 2}** — {One sentence explaining the user/business impact}
- ✅ **{Highlight 3}** — {One sentence explaining the user/business impact}
- 🐛 **{Bug fix highlight}** — {What was broken and how users are affected}
- 🔧 **{Maintenance highlight}** — {Why this matters for reliability/performance}

### 📊 Sprint at a Glance

| Metric | Value |
|--------|-------|
| 🔢 **Issues Resolved** | {count} |
| 📝 **Pull Requests Merged** | {count} |
| 📦 **Commits** | {count} |
| 📂 **Files Changed** | {count} |
| ➕ **Lines Added** | {count} |
| ➖ **Lines Removed** | {count} |
| 📈 **Net Change** | {+/- count} lines |
| ⏱️ **Avg PR Merge Time** | {duration} |
| 🏷️ **Releases** | {version list or "None"} |

### 🏗️ What Was Delivered

Group deliverables by system/module. Each item should be understandable by a non-developer.

#### {System Name} (e.g., Document Tracking)

| Status | Item | Impact |
|--------|------|--------|
| ✅ | {Feature/fix name} | {Who benefits and how} |
| ✅ | {Feature/fix name} | {Who benefits and how} |

#### {System Name} (e.g., Super Admin)

| Status | Item | Impact |
|--------|------|--------|
| ✅ | {Feature/fix name} | {Who benefits and how} |

{Repeat for each system that had changes}

### ⚠️ Known Issues & Risks

{List any anomalies detected in Phase 3.3, translated to stakeholder language.
If none, write "No anomalies detected this sprint."}

- ⚠️ {Issue description in plain language}

---

## 🔧 Technical Summary

> *For engineering leads — detailed breakdown of changes, architecture decisions, and code metrics.*

### 📈 Metrics Dashboard

#### Velocity

| Metric | This Sprint | Trend |
|--------|-------------|-------|
| Commits | {count} | {📈📉➡️ vs previous period if available} |
| Issues Closed | {count} | {📈📉➡️} |
| Issues Opened | {count} | {📈📉➡️} |
| Net Velocity | {+/- count} | {📈📉➡️} |

#### Code Volume

| Area | Commits | Lines Changed | Files |
|------|---------|---------------|-------|
| 🖥️ Backend | {n} | +{add}/-{del} | {n} |
| 🎨 Frontend | {n} | +{add}/-{del} | {n} |
| 🔄 Full Stack | {n} | +{add}/-{del} | {n} |
| 📝 Docs/Config | {n} | +{add}/-{del} | {n} |
| **Total** | **{n}** | **+{add}/-{del}** | **{n}** |

#### PR Health

| Metric | Value |
|--------|-------|
| PRs Merged | {count} |
| Avg Time-to-Merge | {duration} |
| Fastest Merge | {duration} ({PR title}) |
| Slowest Merge | {duration} ({PR title}) |
| Avg Review Comments | {count} |

#### Contributors

| Contributor | Commits | Lines Changed | Primary Area |
|-------------|---------|---------------|--------------|
| {name} | {n} | +{add}/-{del} | {area} |

### 📋 Detailed Change Log

{For each commit, grouped by day descending (newest first), include the full changelog entry.
This section mirrors the weekly changelog format but filtered to the sprint range.}

#### 📅 {Day, Month Date}

**{type}({scope}): {description}** `{version}`
- Commit: `{hash}` | Issue: #{n} | PR: #{n}
- Area: {area} | Files: {count} changed
- {Change bullet 1}
- {Change bullet 2}
- {Change bullet 3}

{Repeat for each commit}

### 🏷️ Releases in This Sprint

{If any beta→main promotions occurred, list them with their included issues.
If none, write "No production releases this sprint."}

#### {version} ({date})
- **Issues**: #{n}, #{n}, #{n}
- **Summary**: {n} features, {n} bug fixes, {n} maintenance

---

## 🔮 What's Next

> *Upcoming work based on pending tasks and open issues.*

### 🎯 Planned Work

{Read from .kris/tasks/pending/ and open GitHub issues. Group by priority/system.}

| Priority | Item | System | Source |
|----------|------|--------|--------|
| 🔴 Critical | {task name} | {system} | {task file or issue #} |
| 🟡 Medium | {task name} | {system} | {task file or issue #} |
| 🟢 Low | {task name} | {system} | {task file or issue #} |

### 🚧 Open Items

| Issue | Title | Labels | Age |
|-------|-------|--------|-----|
| #{n} | {title} | {labels} | {days since created} |

---

## 📎 Appendix

### 🔗 Quick Links

- [All PRs in range](https://github.com/{owner}/{repo}/pulls?q=is:pr+merged:{START_DATE}..{END_DATE})
- [All Issues closed](https://github.com/{owner}/{repo}/issues?q=is:issue+closed:{START_DATE}..{END_DATE})
- [Commit history](https://github.com/{owner}/{repo}/commits/dev?since={START_DATE}&until={END_DATE})

### 🏥 Anomalies Log

{Detailed list of any anomalies from Phase 3.3. Include raw data for investigation.}

| Type | Detail | Action Needed |
|------|--------|---------------|
| {type} | {description} | {suggestion} |

{If no anomalies: "✅ No anomalies detected. All commits have issue references, all closed issues have corresponding code changes."}

---

*📊 Generated by `/sprint-report` | BGH Katalyst Platform*
*🤖 Powered by [Claude Code](https://claude.com/claude-code)*
```

---

## Phase 6: Write Output & Confirm

### 6.1 Create Report Directory (if needed)

```bash
mkdir -p .kris/reports
```

### 6.2 Write Report File

Write the generated report to:
```
.kris/reports/{END_DATE}_sprint-report.md
```

### 6.3 Display Summary to User

After writing the report, show a brief confirmation:

```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
📊 SPRINT REPORT GENERATED
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

📅 Range:     {START_DATE} → {END_DATE}
🏷️ Versions:  {FIRST_VERSION} → {LATEST_VERSION}
📦 Commits:   {count}
🔢 Issues:    {resolved_count} resolved
📝 PRs:       {merged_count} merged

📄 Report:    .kris/reports/{END_DATE}_sprint-report.md

{If changelog was updated:}
📝 Changelog: Updated {count} missing entries in {changelog_file}

{If anomalies detected:}
⚠️  Anomalies: {count} items flagged (see report appendix)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

---

## Flags

### `--dry-run` or `-d`

Preview what data would be gathered without generating the report:

```bash
/sprint-report --dry-run
```

Shows:
- Resolved date range
- Changelog files that would be read
- Commit count in range
- PR and issue counts
- Detected changelog gaps

### `--no-update`

Skip the changelog auto-update step (Phase 3.2):

```bash
/sprint-report --no-update 2026-03-17
```

Useful when you want a read-only report without modifying changelogs.

### `--compare`

Include comparison with previous sprint period (same duration, immediately prior):

```bash
/sprint-report --compare
```

Adds trend arrows (📈📉➡️) and delta percentages to all metrics by comparing against the previous period of equal length.

---

## Presentation Agent Handoff

The generated report is structured for easy consumption by a presentation-building agent. Key design decisions:

1. **Clear section hierarchy** — H1 → H2 → H3 with consistent naming
2. **Tables everywhere** — easy to convert to slides
3. **Emoji prefixes** — visual anchors for section identification
4. **Executive Summary first** — can be extracted as a standalone slide deck
5. **Appendix at end** — reference material, not presentation content

**Suggested prompt for presentation agent:**
```
Read the sprint report at .kris/reports/{date}_sprint-report.md.
Create a slide deck with:
- Slide 1: Title + date range + version range
- Slide 2: Sprint Highlights (from Executive Summary)
- Slide 3: Sprint at a Glance metrics table
- Slide 4-N: What Was Delivered (one slide per system)
- Slide N+1: Metrics Dashboard (charts from Technical Summary)
- Slide N+2: What's Next (planned work table)
Keep it visual. Use the emoji indicators from the report.
```

---

## Error Handling

| Error | Resolution |
|-------|------------|
| No commits in range | Report still generates with "No activity in this period" |
| GitHub CLI not authenticated | Show `gh auth login` instruction |
| Changelog files missing | Generate report from git/GitHub data only, note gap |
| Invalid date format | Show usage examples, ask for correction |
| Future end date | Cap at today's date with warning |

---

## Integration with Other Skills

| Skill | Relationship |
|-------|-------------|
| `/commit` | Creates changelog entries that feed this report |
| `/fix` | Creates commits + PRs + changelog entries |
| `/stage` | Creates release entries referenced in report |
| `/hotfix` | Creates hotfix entries (flagged with 🚨 in report) |
| `/worktree` | Creates tasks in `pending/` shown in "What's Next" |

---

## Examples

### Generate This Week's Report

```
> /sprint-report

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
📊 SPRINT REPORT GENERATED
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

📅 Range:     2026-03-23 → 2026-03-23
🏷️ Versions:  v0.3.1.0 → v0.3.2.0
📦 Commits:   3
🔢 Issues:    2 resolved
📝 PRs:       2 merged

📄 Report:    .kris/reports/2026-03-23_sprint-report.md

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

### Generate Report for Past Two Weeks

```
> /sprint-report 2026-03-10 2026-03-23

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
📊 SPRINT REPORT GENERATED
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

📅 Range:     2026-03-10 → 2026-03-23
🏷️ Versions:  v0.2.8.0 → v0.3.2.0
📦 Commits:   28
🔢 Issues:    15 resolved
📝 PRs:       12 merged

📄 Report:    .kris/reports/2026-03-23_sprint-report.md

📝 Changelog: Updated 2 missing entries in 2026-03-09_to_2026-03-15.md

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

### Dry Run

```
> /sprint-report --dry-run 2026-03-17

📊 DRY RUN: Sprint Report Preview
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

📅 Range:      2026-03-17 → 2026-03-23
📁 Changelogs: 2026-03-17_to_2026-03-23.md
📦 Commits:    13 on dev
📝 PRs:        8 merged
🔢 Issues:     10 closed
⚠️  Gaps:       0 commits missing from changelog

Would generate: .kris/reports/2026-03-23_sprint-report.md

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Run without --dry-run to generate.
```
