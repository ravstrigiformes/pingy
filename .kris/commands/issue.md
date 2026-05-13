# /issue - Intelligent Issue Creation

Create well-structured GitHub issues with automatic project integration, intelligent labeling, and smart field detection.

> **Philosophy:**
> - Smart defaults with full customization available
> - Every selection has a **recommended option** clearly marked
> - Users can press **Enter repeatedly** to accept all defaults
> - Integrates with GitHub Projects for full traceability

---

## Quick Start

```bash
/issue                              # Interactive mode - guided issue creation
/issue "login timeout bug"          # Quick mode - AI analyzes and suggests
/issue --from-task                  # Convert current .kris/tasks/*.md to issue
/issue --auto "add user avatars"    # Full auto - create with smart defaults
```

---

## Issue Style Guide

> **All issues MUST follow this style guide for consistency.**

### Issue Updates & Amendments

When issues need updates after creation (e.g., during `/fix` process or after investigation):

**When to Edit Original Issue vs. Add Comment:**

| Scenario | Action |
|----------|--------|
| Correcting an incorrect assumption | **Edit** - strikethrough original, add correction |
| Adding newly discovered root cause | **Edit** - append to description |
| Minor implementation detail/note | **Comment** - keeps main issue clean |
| Progress update or interim finding | **Comment** |
| Significant scope change | **Edit** - with clear `[UPDATED]` marker |

**Edit Annotation Style:**

```markdown
## Root Cause
~~Initially suspected session driver issue~~
**[CONFIRMED]** Broadcasting (Reverb/Pusher) not configured - triggers 500 on broadcast events

## Details
- BROADCAST_CONNECTION=reverb but Reverb keys not set
- [ADDED] Pusher fallback also unconfigured
```

**Key markers:**
- `~~strikethrough~~` for superseded info (preserve original context)
- `[CONFIRMED]` / `[REJECTED]` for validated assumptions
- `[ADDED]` / `[UPDATED]` for new information
- `[WAS: x]` for changed values

**Rationale:** Preserving the investigation trail helps anyone reading the issue understand the journey, not just the conclusion. Never delete assumptions - annotate them to prevent "lost in translation" during review.

### Title Conventions

**From org templates:**

| Type | Prefix | Labels | Example |
|------|--------|--------|---------|
| Bug | `[Bug]:` | `bug`, `needs-triage` | `[Bug]: Payee geolocation filters not cascading through ancestor levels` |
| Feature | `[Feature]:` | `enhancement` | `[Feature]: Add Fund Adjustment Types lookup module with CRUD operations` |
| Tech | `[Tech]:` | `tech`, `maintenance` | `[Tech]: Enhance Bank and Branch module with extended metadata fields` |

**Extended conventions (used in practice):**

| Type | Prefix | Labels | Use For |
|------|--------|--------|---------|
| Refactor | `[Refactor]:` | `enhancement` | Code restructuring without behavior change |
| Chore | `[Chore]:` | `enhancement` | Maintenance, dependencies, config updates |

**Rules:**
- Use **sentence case** (capitalize first word only)
- Keep titles **under 80 characters**
- Describe the **outcome** for features/tech, **problem** for bugs
- Be specific about what module/component is affected

### Section Templates (from org ISSUE_TEMPLATE)

**Bug Issues (Bug.yml):**
```markdown
## Description

{What went wrong? Describe the bug clearly.}

## Steps to Reproduce

1. {Go to the page or feature...}
2. {Click on ...}
3. {Observe the bug ...}

## Expected Behavior

{What you expected to happen instead of the bug.}

## Actual Behavior

{What actually occurred, including any error messages.}

## Screenshot / Logs

{Paste screenshot URLs or drag images here.}

## Severity

{Low|Medium|High|Critical}
```

**Feature Issues (Feature.yml):**
```markdown
## Problem / Use Case

{What problem does this feature solve? Explain the need.}

## Proposed Solution

{Describe the solution. Include subsections for Backend/Frontend if applicable.}

## Impact

{Who benefits and why it's important.}

## Acceptance Criteria

- [ ] {Condition 1}
- [ ] {Condition 2}
- [ ] {Condition 3}

## Estimated Size

{Small|Medium|Large|Epic}
```

**Tech/Maintenance Issues (technical_maintenance.yml):**
```markdown
## Context / Motivation

{Why is this technical work needed? Explain the reason:}
- {operational issue / reliability concern / technical debt}
- {environment constraint / maintainability problem}

## Planned Changes

- {Technical change 1}
- {Technical change 2}
- {Technical change 3}

## Impact

- {Reliability / Performance / Debuggability}
- {Developer experience / Operational stability}

## Risks / Notes

- {Possible side effects}
- {Backward compatibility notes}
- {Or "No user-facing behavior changes expected"}

## Acceptance Criteria

- [ ] No user-facing behavior changes introduced
- [ ] Changes are limited to tech / maintenance scope
- [ ] {Additional criteria}

## Estimated Size

{Small (safe, isolated)|Medium (multiple files / modules)|Large (cross-cutting / needs extra review)}
```

**Refactor/Chore Issues (use Tech template with adjusted title):**
- Use `[Refactor]:` or `[Chore]:` prefix
- Follow the Tech/Maintenance body template
- Labels: `enhancement` (not `tech`, `maintenance`)

---

## Project Integration

All issues are automatically added to **"System Development and Enhancements"** project with these fields:

| Field | Detection Method | Values |
|-------|------------------|--------|
| **Status** | Always set to | `Backlog` |
| **Priority** | Detected from severity/impact | `P0`, `P1`, `P2` |
| **Size** | Estimated from scope | `XS`, `S`, `M`, `L`, `XL` |
| **System** | Detected from file paths/context | See below |
| **Start Date** | Estimated or user-provided | Date |
| **End Date** | Estimated based on size | Date |
| **Assignee** | Always set to | `ravstrigiformes` |

### System Detection

The system is intelligently detected from context:

| Pattern | System Label |
|---------|--------------|
| `Modules/Finance/`, `/finance/`, `cds`, `payee`, `fund`, `bank`, `check` | `bgh-katalyst:cds` |
| `Modules/Document/`, `/dots/`, `document`, `docstep`, `inbox`, `archive` | `bgh-katalyst:dots` |
| `Modules/User/`, `/admin/accounts`, `user`, `role`, `ability`, `access` | `bgh-katalyst:sa` |
| `Modules/UACS/`, `/uacs/`, `fund-source`, `object-code`, `location` | `bgh-katalyst:uacs` |
| `misis`, `management information` | `bgh-katalyst:misis` |
| `utils`, `calculator`, `generator`, `converter` | `bgh-katalyst:utils` |
| `components/k/`, `patterns/`, UI components, shared layouts | `bgh-katalyst` |
| Multiple systems, architectural, config | `bgh-katalyst` |

### Priority Detection

| Signal | Priority |
|--------|----------|
| Critical bug, security issue, data loss risk | `P0` |
| High-impact bug, blocking issue, important feature | `P1` |
| Normal bug, enhancement, tech debt | `P2` |

### Size Estimation

| Scope | Size | Typical Duration |
|-------|------|------------------|
| Single file, < 50 lines, trivial change | `XS` | < 1 day |
| 1-3 files, < 200 lines, isolated change | `S` | 1-2 days |
| 3-6 files, moderate complexity, one module | `M` | 3-5 days |
| 6-12 files, cross-module, significant feature | `L` | 1-2 weeks |
| 12+ files, architectural, multi-system | `XL` | 2+ weeks |

### Date Estimation

**Start Date:** Set to today (or user-provided if known)

**End Date:** Calculated from size:
| Size | Days Added |
|------|------------|
| XS | +1 day |
| S | +2 days |
| M | +5 days |
| L | +10 days |
| XL | +14 days |

*Weekends are skipped in calculation.*

---

## Flags

### `--auto` or `-a` (Automatic Mode)

Skip all prompts, use intelligent defaults:

```bash
/issue --auto "fix login timeout"
/issue -a "add avatar upload feature"
```

### `--from-task` (Convert Task File)

Convert the currently open `.kris/tasks/*.md` file to a GitHub issue:

```bash
/issue --from-task
```

This will:
1. Parse the task file structure
2. Extract title, description, checklist items
3. Map to appropriate issue type
4. Create issue with full metadata
5. Optionally move task file to `.kris/tasks-completed/`

### `--type` (Force Issue Type)

```bash
/issue --type=bug "session expires early"
/issue --type=feature "user avatars"
/issue --type=tech "cache optimization"
```

### `--system` (Force System Label)

```bash
/issue --system=bgh-katalyst:cds "payee validation"
/issue --system=bgh-katalyst "component refactor"
```

### `--priority` (Force Priority)

```bash
/issue --priority=P0 "security vulnerability"
/issue --priority=P1 "blocking bug"
```

### `--size` (Force Size)

```bash
/issue --size=XS "typo fix"
/issue --size=L "new module"
```

---

## Phase 1: Pre-flight Checks

### 1.1 Authentication Check

```bash
gh auth status
```

**Required scopes:** `repo`, `read:project`, `project`

If missing project scopes:
```
⚠️ Missing project scopes. Run:
   gh auth refresh -s read:project -s project
```

### 1.2 Get Project ID

```bash
# Get organization project
gh api graphql -f query='
query {
  organization(login: "mis-bghmc") {
    projectsV2(first: 20) {
      nodes {
        id
        title
        fields(first: 20) {
          nodes {
            ... on ProjectV2SingleSelectField {
              id
              name
              options { id name }
            }
            ... on ProjectV2Field {
              id
              name
            }
          }
        }
      }
    }
  }
}'
```

Cache the project ID and field IDs for the session.

---

## Phase 2: Context Analysis

### 2.1 Gather Context

Based on user input, analyze:

1. **User description** - Parse for keywords, intent
2. **Current branch** - May indicate related work
3. **Recent commits** - Context for what's being worked on
4. **Open files in IDE** - What the user is looking at
5. **Changed files** - What's been modified

### 2.2 Detect Issue Type

| Keywords/Patterns | Type |
|-------------------|------|
| `fix`, `bug`, `broken`, `error`, `crash`, `wrong`, `fail` | Bug |
| `add`, `new`, `feature`, `implement`, `enable`, `support` | Feature |
| `refactor`, `clean`, `optimize`, `improve`, `update` | Tech |
| `perf`, `performance`, `slow`, `speed`, `cache` | Perf |
| `docs`, `readme`, `documentation`, `comment` | Chore |

### 2.3 Detect System

Scan context for system indicators:

```javascript
const systemPatterns = {
  'bgh-katalyst:cds': [
    /Modules\/Finance/i, /finance/i, /payee/i, /fund/i, /bank/i,
    /check/i, /disbursement/i, /cds/i, /adjustment/i
  ],
  'bgh-katalyst:dots': [
    /Modules\/Document/i, /document/i, /dots/i, /inbox/i,
    /docstep/i, /archive/i, /tracking/i, /routing/i
  ],
  'bgh-katalyst:sa': [
    /Modules\/User/i, /user/i, /role/i, /ability/i, /access/i,
    /admin\/accounts/i, /permission/i, /authentication/i
  ],
  'bgh-katalyst:uacs': [
    /Modules\/UACS/i, /uacs/i, /fund-source/i, /object-code/i,
    /location/i, /region/i, /province/i, /barangay/i
  ],
  'bgh-katalyst:misis': [
    /misis/i, /management.*information/i, /report/i
  ],
  'bgh-katalyst:utils': [
    /utils/i, /calculator/i, /generator/i, /converter/i, /tool/i
  ]
};

// Default to 'bgh-katalyst' for shared/multi-system
```

### 2.4 Estimate Size & Dates

**Size estimation factors:**
- Number of files likely affected
- Complexity keywords ("simple" → XS, "architectural" → XL)
- Module scope (single vs. cross-cutting)
- User hints ("quick fix", "major feature")

**Date calculation:**
```javascript
function calculateEndDate(startDate, size) {
  const daysMap = { XS: 1, S: 2, M: 5, L: 10, XL: 14 };
  let days = daysMap[size];
  let date = new Date(startDate);

  while (days > 0) {
    date.setDate(date.getDate() + 1);
    // Skip weekends
    if (date.getDay() !== 0 && date.getDay() !== 6) {
      days--;
    }
  }
  return date;
}
```

---

## Phase 3: Interactive Issue Creation

### 3.1 Present Analysis

```
📋 ISSUE ANALYSIS
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Input: "fix login session timeout"

Detected:
  Type:     🐛 Bug (recommended)
  System:   bgh-katalyst:sa
  Priority: P2 (recommended)
  Size:     S (recommended)

Estimated dates:
  Start:    2026-02-24 (today)
  End:      2026-02-26 (S = +2 business days)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

### 3.2 Confirm or Modify

```
Accept these settings?

  [Y] Yes, proceed with these defaults (recommended)
  [t] Change type
  [s] Change system
  [p] Change priority
  [z] Change size
  [d] Change dates
  [n] Start over

Your choice [Y]: _
```

### 3.3 Generate Issue Content

**INTERACTIVE CHECKPOINT: Issue Preview**

```
📝 ISSUE PREVIEW
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Title: [Bug]: Login session timeout ignoring configuration

── Metadata ─────────────────────────────────────────
Type:     🐛 Bug
Labels:   bug
System:   bgh-katalyst:sa
Priority: P2
Size:     S
Assignee: ravstrigiformes
Project:  System Development and Enhancements
Status:   Backlog
Start:    2026-02-24
End:      2026-02-26

── Description ──────────────────────────────────────

## Description

The login session is timing out earlier than expected,
ignoring the SESSION_LIFETIME configuration value.

## Steps to Reproduce

1. Set SESSION_LIFETIME to a custom value in .env
2. Login to the application
3. Observe session expiring before configured time

## Expected Behavior

Session should last for the configured duration.

## Actual Behavior

Session expires prematurely regardless of configuration.

## Severity

Medium

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

### 3.4 Set Session & Terminal Title

After issue creation, set the session name and pane title:

```bash
TITLE="#${ISSUE_NUM} ${SLUG}"
/rename $TITLE
echo -ne "\033]0;${TITLE}\007"
```

### 3.5 Create Issue

```bash
# Create the issue
ISSUE_URL=$(gh issue create \
  --title "[Bug]: Login session timeout ignoring configuration" \
  --body "$ISSUE_BODY" \
  --label "bug" \
  --assignee "ravstrigiformes")

ISSUE_NUM=$(echo "$ISSUE_URL" | grep -oE '[0-9]+$')

# Add to project with fields
gh api graphql -f query='
mutation($projectId: ID!, $itemId: ID!) {
  addProjectV2ItemById(input: {projectId: $projectId, contentId: $itemId}) {
    item { id }
  }
}' -f projectId="$PROJECT_ID" -f itemId="$ISSUE_NODE_ID"

# Set project fields
gh api graphql -f query='
mutation($projectId: ID!, $itemId: ID!, $fieldId: ID!, $value: String!) {
  updateProjectV2ItemFieldValue(input: {
    projectId: $projectId
    itemId: $itemId
    fieldId: $fieldId
    value: {singleSelectOptionId: $value}
  }) { projectV2Item { id } }
}'
# Repeat for Status, Priority, Size, System, Start Date, End Date
```

---

## Phase 4: Summary

```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
✅ /issue COMPLETE
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

📋 Issue Created
   #97: [Bug]: Login session timeout ignoring configuration
   https://github.com/mis-bghmc/bgh-katalyst/issues/97

📊 Project Fields Set
   Status:   Backlog
   Priority: P2
   Size:     S
   System:   bgh-katalyst:sa
   Start:    2026-02-24
   End:      2026-02-26
   Assignee: ravstrigiformes

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

📌 NEXT STEPS:
   1. Start implementation
   2. Run /fix --issue 97 when ready to commit
   3. Issue auto-closes on PR merge

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

---

## Integration with /fix

The `/issue` skill creates issues for **planned work**. When you're ready to implement:

```bash
/fix --issue 97              # Attach changes to existing issue #97
/fix --issue 97 --auto       # Auto-commit attached to issue #97
```

This updates the `/fix` workflow to:
1. Skip issue creation (use existing)
2. Create isolated worktree (parallel agent safety)
3. Create branch: `bugfix/97-login-session-timeout` from `dev`
4. Commit with `Resolves #97`
5. Update project status to "In Progress"
6. PR targets `dev` (or `main` for Critical/hotfix)

**Branch Flow:**
- Bug/Feature issues → `feature/*` or `bugfix/*` from `dev` → PR to `dev` → promote to `staging` → `main`
- Critical issues → `hotfix/*` from `main` → PR to `main` → backport to `staging` → `dev`

---

## Error Handling

| Error | Resolution |
|-------|------------|
| Missing project scopes | Guide to run `gh auth refresh -s read:project -s project` |
| Project not found | List available projects, ask user to select |
| Field not found | Fall back to not setting that field, warn user |
| Issue creation fails | Show error, offer retry |

---

## Examples

### Quick Bug Report

```
> /issue "payee validation allowing invalid accounts"

📋 Detected: Bug in bgh-katalyst:cds (P2, Size S)
   Creating issue...

✅ Created #98: [Bug]: Payee validation allowing invalid accounts
   Project: Backlog | P2 | S | bgh-katalyst:cds
   Due: 2026-02-26
```

### Feature Request

```
> /issue "add bulk export for documents"

📋 Detected: Feature in bgh-katalyst:dots (P2, Size M)
   Creating issue...

✅ Created #99: [Feature]: Add bulk export for documents
   Project: Backlog | P2 | M | bgh-katalyst:dots
   Due: 2026-03-03
```

### From Task File

```
> /issue --from-task

📄 Reading: .kris/tasks/update-fund-adjustment-types-schema.md

📋 Detected: Tech task in bgh-katalyst:cds (P2, Size S)
   Title: Update Fund Adjustment Types Schema
   Sections: Overview, Changes Required, Testing Checklist

✅ Created #100: [Tech]: Update Fund Adjustment Types schema
   Project: Backlog | P2 | S | bgh-katalyst:cds
   Due: 2026-02-26

📁 Move task file to completed? [Y/n]: _
```

### Full Auto Mode

```
> /issue --auto "critical security fix for auth bypass"

📋 Auto-detected: Bug (P0, bgh-katalyst:sa, Size S)
✅ Created #101: [Bug]: Critical security fix for auth bypass
   Project: Backlog | P0 | S | bgh-katalyst:sa
   Due: 2026-02-26
```
