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
**[CONFIRMED]** Sanctum stateful domains misconfigured - triggers 401 on cross-origin requests

## Details
- SANCTUM_STATEFUL_DOMAINS missing current dev origin
- [ADDED] CORS allowed_origins also outdated
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

Full per-type body shapes, title conventions, label sets, and field notes live in
dedicated template files. Read the one matching the detected issue type:

- **Bug** — see `.kris/templates/issue/bug.md` for the canonical body shape and field guidance.
- **Feature** — see `.kris/templates/issue/feature.md` for the canonical body shape and field guidance.
- **Tech / Maintenance** — see `.kris/templates/issue/tech.md` (also covers `[Refactor]:` and `[Chore]:` variants — same body, different title prefix, `enhancement` label).

The "Title Conventions" tables above (`[Bug]:` / `[Feature]:` / `[Tech]:` / `[Refactor]:` / `[Chore]:`) are the at-a-glance index of prefix + labels per type; the template files are the long-form source of truth for body shape and field guidance. `/fix` Phase 4.4 references the same files.

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
| `Modules/Booking/`, `/booking/`, `booking`, `reservation`, `room`, `guest` | `fnl-cat:booking` |
| `Modules/Admin/`, `/admin/`, `admin`, `panel`, `permission`, `role` | `fnl-cat:admin` |
| `Modules/User/`, `/auth/`, `user`, `auth`, `login`, `2fa`, `password` | `fnl-cat:user` |
| `Modules/Audit/`, `/audit/`, `audit`, `log`, `activity`, `tracking` | `fnl-cat:audit` |
| `utils`, `calculator`, `generator`, `converter` | `fnl-cat:utils` |
| `components/k/`, `patterns/`, UI components, shared layouts | `fnl-cat` |
| Multiple systems, architectural, config | `fnl-cat` |

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
/issue --system=fnl-cat:booking "reservation validation"
/issue --system=fnl-cat "component refactor"
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
# Get user projects (fnl-cat lives under a user account, not an org)
gh api graphql -f query='
query {
  user(login: "ravstrigiformes") {
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
  'fnl-cat:booking': [
    /Modules\/Booking/i, /booking/i, /reservation/i, /room/i, /guest/i,
    /check-in/i, /check-out/i, /inventory/i
  ],
  'fnl-cat:admin': [
    /Modules\/Admin/i, /admin/i, /panel/i, /permission/i, /role/i,
    /rbac/i, /dashboard/i
  ],
  'fnl-cat:user': [
    /Modules\/User/i, /user/i, /auth/i, /login/i, /2fa/i, /password/i,
    /account/i, /profile/i, /authentication/i
  ],
  'fnl-cat:audit': [
    /Modules\/Audit/i, /audit/i, /log/i, /activity/i, /tracking/i,
    /history/i, /trail/i
  ],
  'fnl-cat:utils': [
    /utils/i, /calculator/i, /generator/i, /converter/i, /tool/i
  ]
};

// Default to 'fnl-cat' for shared/multi-system
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
  System:   fnl-cat:user
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
System:   fnl-cat:user
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
   https://github.com/ravstrigiformes/fnl-cat/issues/97

📊 Project Fields Set
   Status:   Backlog
   Priority: P2
   Size:     S
   System:   fnl-cat:user
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
> /issue "reservation validation allowing invalid dates"

📋 Detected: Bug in fnl-cat:booking (P2, Size S)
   Creating issue...

✅ Created #98: [Bug]: Reservation validation allowing invalid dates
   Project: Backlog | P2 | S | fnl-cat:booking
   Due: 2026-02-26
```

### Feature Request

```
> /issue "add bulk export for admin users"

📋 Detected: Feature in fnl-cat:admin (P2, Size M)
   Creating issue...

✅ Created #99: [Feature]: Add bulk export for admin users
   Project: Backlog | P2 | M | fnl-cat:admin
   Due: 2026-03-03
```

### From Task File

```
> /issue --from-task

📄 Reading: .kris/tasks/update-booking-status-schema.md

📋 Detected: Tech task in fnl-cat:booking (P2, Size S)
   Title: Update Booking Status Schema
   Sections: Overview, Changes Required, Testing Checklist

✅ Created #100: [Tech]: Update Booking Status schema
   Project: Backlog | P2 | S | fnl-cat:booking
   Due: 2026-02-26

📁 Move task file to completed? [Y/n]: _
```

### Full Auto Mode

```
> /issue --auto "critical security fix for auth bypass"

📋 Auto-detected: Bug (P0, fnl-cat:user, Size S)
✅ Created #101: [Bug]: Critical security fix for auth bypass
   Project: Backlog | P0 | S | fnl-cat:user
   Due: 2026-02-26
```
