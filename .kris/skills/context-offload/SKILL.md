---
name: context-offload
description: Offload detailed sections from CLAUDE.md to modular context files while preserving references
allowed-tools: Read, Edit, Write, Glob, Grep, AskUserQuestion
user-invocable: true
---

# Context Offload

A skill for managing CLAUDE.md file size by offloading detailed documentation to modular context files. This keeps the main CLAUDE.md lean and focused on critical rules while preserving detailed guidance in on-demand context files.

## Philosophy

Based on context engineering best practices from Anthropic and Boris Cherny:
- **Minimal high-signal tokens** - Main file should only contain what's always relevant
- **Just-in-time retrieval** - Detailed docs loaded when needed, not upfront
- **Safety rails stay in main** - Critical restrictions never get offloaded

## Invocation

### `/context-offload` (default)

Directly proceeds to offload a section. Ask the user which section to offload.

### `/context-offload --optimize`

Runs a comprehensive review of ALL CLAUDE.md files and ALL context files before any action. Use this to:
- Detect redundancy and duplication
- Find optimization opportunities
- Ensure the context structure is healthy

### `/context-offload --audit`

Quick audit of main CLAUDE.md only - shows sections and line counts.

### `/context-offload --add`

Create a new context file and add signpost to CLAUDE.md.

### When to Suggest `--optimize`

Proactively suggest running `--optimize` when:
1. **First time running this skill** in a project
2. **Multiple CLAUDE.md files detected** (root + .claude/ + subdirectories)
3. **Context files seem fragmented** (many small files under 50 lines)
4. **Orphaned context files** (files not referenced in any CLAUDE.md)
5. **User is about to offload** but hasn't reviewed the full landscape recently

Example suggestion:
> "I notice there are multiple CLAUDE.md files and 4 context files. Consider running `/context-offload --optimize` first to review the full context landscape before making changes."

---

## Mode: Optimize (`--optimize`)

Comprehensive review of the entire context ecosystem before making changes.

### Step 1 - Discovery

Scan for ALL documentation files:

1. **Find all CLAUDE.md files:**
   - `./CLAUDE.md` (project root)
   - `./.claude/CLAUDE.md`
   - `./*/CLAUDE.md` (subdirectory CLAUDE.md files)
   - Any other `CLAUDE.md` in the project

2. **Find all context files:**
   - `.kris/context/*.md`
   - Any other designated context directories

3. **Count lines and parse structure** for each file

### Step 2 - Analysis

For each file, extract:
- Total lines
- Section headings (## level)
- Lines per section
- Keywords: CRITICAL, NEVER, DO NOT, safety-related terms
- Signposts (references to other files)
- Code block count and size

### Step 3 - Cross-File Analysis

Detect issues across the entire ecosystem:

| Issue Type | Detection Method | Severity |
|------------|------------------|----------|
| **Duplicate content** | Fuzzy match section headings and content between files | High |
| **Redundant CLAUDE.md files** | Multiple files with overlapping purpose | High |
| **Orphaned context files** | Context files not referenced in any CLAUDE.md signpost | Medium |
| **Missing signposts** | Large sections in CLAUDE.md that should reference context files | Medium |
| **Thin context files** | Files under 50 lines - consider merging | Low |
| **Bloated context files** | Files over 500 lines - consider splitting | Low |
| **Inconsistent headers** | Context files missing "When to read" guidance | Low |
| **Stale references** | Signposts pointing to non-existent files | High |

### Step 4 - Generate Report

```
## Context Optimization Report

### File Inventory

| File | Lines | Sections | Status |
|------|-------|----------|--------|
| `./CLAUDE.md` | 90 | 8 | Generic Laravel guidelines |
| `./.claude/CLAUDE.md` | 256 | 12 | Project-specific |
| `.kris/context/admin-panel.md` | 380 | 15 | Detailed component APIs |
| `.kris/context/frontend-patterns.md` | 150 | 8 | Vue/TanStack patterns |
| `.kris/context/git-workflow.md` | 100 | 6 | Git conventions |
| `.kris/context/code-patterns.md` | 60 | 4 | Backend patterns |
| **Total context loaded at start** | **346** | — | Under 400 ✓ |

### Issues Detected

#### High Severity
1. **Redundant CLAUDE.md files** - `./CLAUDE.md` and `./.claude/CLAUDE.md` both loaded
   - Overlap: Indentation standards, PHP conventions
   - Recommendation: Merge into single `.claude/CLAUDE.md` or make root file framework-only

#### Medium Severity
2. **Potential content overlap** - `admin-panel.md` and `frontend-patterns.md`
   - Both cover TanStack Query patterns
   - Recommendation: Deduplicate or cross-reference

#### Low Severity
3. **Thin context file** - `code-patterns.md` (60 lines)
   - Could be merged into main CLAUDE.md or another context file

### Optimization Recommendations

| Priority | Action | Impact |
|----------|--------|--------|
| 1 | Merge root CLAUDE.md into .claude/CLAUDE.md | Reduces redundancy, single source of truth |
| 2 | Add cross-references between admin-panel.md and frontend-patterns.md | Clearer navigation |
| 3 | Consider merging code-patterns.md into main file | Reduces file count |

### Health Score: 7/10

- ✓ Main file under 400 lines
- ✓ Context files have headers
- ✓ Signpost table exists
- ✗ Multiple CLAUDE.md files
- ✗ Some content overlap detected
```

### Step 5 - Offer Actions

```
AskUserQuestion:
  question: "Which optimization would you like to perform?"
  header: "Action"
  options:
    - label: "Fix high severity issues"
      description: "Address redundancy and overlaps first"
    - label: "Run full optimization"
      description: "Fix all detected issues"
    - label: "Just the report"
      description: "I'll handle it manually"
    - label: "Optimize specific file"
      description: "Let me choose which file to optimize"
```

---

## Mode: Offload Section (default)

Move a detailed section from CLAUDE.md to a context file.

### Pre-flight Check

Before offloading, do a quick scan:
1. Count CLAUDE.md files in project
2. Count context files in `.kris/context/`
3. If this looks like first-time use OR multiple CLAUDE.md files exist, suggest:
   > "Consider running `/context-offload --optimize` first to review the full context landscape."

### Step 1 - Read Current State

1. Read `.claude/CLAUDE.md` (or the project's main CLAUDE.md)
2. Read `.kris/context/` directory listing to see existing context files
3. Count total lines in main CLAUDE.md
4. Parse and list all ## sections with line counts

### Step 2 - Identify Section

Ask which section to offload:

```
AskUserQuestion:
  question: "Which section should be offloaded?"
  header: "Section"
  options:
    - label: "{Section 1} ({N} lines)"
      description: "First large section detected"
    - label: "{Section 2} ({N} lines)"
      description: "Second large section detected"
    - label: "I'll specify manually"
      description: "Let me type the section heading"
```

Prioritize showing sections over 50 lines as options.

### Step 3 - Validate

Before offloading, check:
1. **Is it a safety rail?** - Sections marked CRITICAL or containing restrictions should NOT be offloaded
2. **Is it detailed enough?** - Sections under 20 lines probably don't need offloading
3. **Does a context file exist?** - Check if content belongs in existing file or needs new one

If the section contains CRITICAL markers, warn the user:

```
AskUserQuestion:
  question: "This section contains CRITICAL restrictions. These should typically stay in the main file. Are you sure you want to offload it?"
  header: "Warning"
  options:
    - label: "Keep in main file (Recommended)"
      description: "Safety rails should stay in main CLAUDE.md"
    - label: "Offload anyway"
      description: "I understand the risk and want to proceed"
```

### Step 4 - Determine Target

Ask where the content should go:

```
AskUserQuestion:
  question: "Where should this content go?"
  header: "Target"
  options:
    - label: "Existing context file"
      description: "Append to an existing .kris/context/*.md file"
    - label: "New context file"
      description: "Create a new context file for this content"
```

If existing file, show the list of `.kris/context/*.md` files and let user choose.

If new file:
1. Ask for the filename (kebab-case, no extension)
2. Generate a descriptive header with "When to read this file" guidance

### Step 5 - Execute Offload

1. **Create/update context file**:
   - If new: Create `.kris/context/{name}.md` with header and content
   - If existing: Append content with clear section divider

2. **Update main CLAUDE.md**:
   - Remove the detailed content
   - Replace with a signpost pointing to the context file
   - Update the Context Files table if it exists

3. **Report results**:
   - Lines removed from main file
   - New total line count
   - Path to context file

### Signpost Format

When replacing content, use this format:

```markdown
## {Section Title}

For detailed {topic} documentation, read: `.kris/context/{filename}.md`
```

Or for inline references:

```markdown
**For detailed {topic}, read:** `.kris/context/{filename}.md`
```

---

## Mode: Audit (`--audit`)

Quick audit of main CLAUDE.md only - shows sections and line counts.

### Step 1 - Read and Parse

1. Read the main CLAUDE.md
2. Parse all top-level sections (## headings)
3. Count lines per section

### Step 2 - Categorize

For each section, determine:

| Category | Criteria | Recommendation |
|----------|----------|----------------|
| Safety Rail | Contains CRITICAL, restrictions, NEVER, DO NOT | Keep in main |
| Quick Reference | Under 30 lines, frequently needed | Keep in main |
| Detailed Guide | Over 50 lines, component APIs, extensive examples | Offload |
| Code Patterns | Templates, boilerplate examples | Offload |
| Workflow Steps | Step-by-step procedures | Keep summary, offload details |

### Step 3 - Present Report

Output a prioritized table:

```
## CLAUDE.md Audit Report

**Current size:** {lines} lines
**Target size:** <400 lines
**Reduction needed:** {lines - 400} lines

### Recommended for Offload

| Section | Lines | Target File | Reason |
|---------|-------|-------------|--------|
| Admin Control Panel | 380 | admin-panel.md | Detailed component APIs |
| Frontend Patterns | 150 | frontend-patterns.md | Code examples |
| Git Workflow | 100 | git-workflow.md | PR templates, commands |

### Keep in Main File

| Section | Lines | Reason |
|---------|-------|--------|
| Critical Restrictions | 80 | Safety rails |
| Overview | 45 | Essential orientation |
| Key Workflows | 30 | Quick reference |

### Recommended Action Order
1. Offload "Admin Control Panel" -> saves 380 lines
2. Offload "Frontend Patterns" -> saves 150 lines
3. ...
```

### Step 4 - Offer to Execute

```
AskUserQuestion:
  question: "Would you like me to offload the recommended sections?"
  header: "Action"
  options:
    - label: "Offload all recommended"
      description: "Process all sections marked for offload"
    - label: "Offload one at a time"
      description: "I'll confirm each section individually"
    - label: "Just the report"
      description: "Don't make changes, I'll do it manually"
```

---

## Mode: Add New Context (`--add`)

Create a new context file and add a signpost to CLAUDE.md.

### Step 1 - Gather Info

```
AskUserQuestion:
  question: "What topic will this context file cover?"
  header: "Topic"
  (free text)
```

Then ask for the filename:

```
AskUserQuestion:
  question: "What should the file be named? (kebab-case, no extension)"
  header: "Filename"
  (free text - suggest based on topic)
```

### Step 2 - Generate Header

Create the context file with a standard header:

```markdown
# {Topic Title} - {Subtitle}

> **When to read this file:** {Guidance on when this context is relevant}

This document contains {description of what's in this file}.

---

{Content goes here}
```

### Step 3 - Update Main CLAUDE.md

1. Add entry to the Context Files table
2. If the table doesn't exist, create it

### Step 4 - Report

- Path to new context file
- Updated Context Files table

---

## Rules

1. **Never offload CRITICAL sections** without explicit user override
2. **Always create signposts** when removing content from main file
3. **Maintain the Context Files table** in main CLAUDE.md
4. **Use kebab-case** for context filenames
5. **Include "When to read" guidance** at the top of every context file
6. **Preserve section hierarchy** - if offloading a subsection, keep parent heading in main
7. **Target <400 lines** for main CLAUDE.md (guideline, not hard rule)
8. **Group related content** - don't create too many small context files

---

## Context File Structure

All context files should follow this structure:

```markdown
# {Title}

> **When to read this file:** {Clear guidance}

{Brief intro paragraph}

---

## {Section 1}

{Content}

---

## {Section 2}

{Content}
```

---

## Example Signpost Table

The main CLAUDE.md should have a Context Files section:

```markdown
## Context Files

Before working on specific areas, read the relevant context file:

| Task | Context File |
|------|--------------|
| Admin panel development | `.kris/context/admin-panel.md` |
| Frontend patterns (Vue, TanStack) | `.kris/context/frontend-patterns.md` |
| Git operations, PRs, issues | `.kris/context/git-workflow.md` |
| Backend/service patterns | `.kris/context/code-patterns.md` |
```

---

## Portability

This skill is designed to be portable across projects. The key conventions:

1. **Context folder:** `.kris/context/` (can be customized)
2. **Main CLAUDE.md location:** `.claude/CLAUDE.md` or project root
3. **Signpost format:** Consistent across all projects
4. **Context file header:** Always includes "When to read" guidance

To use in another project:
1. Copy this skill to `.claude/skills/context-offload/`
2. Create the `.kris/context/` directory
3. Add the Context Files table to your CLAUDE.md
4. Run `/context-offload audit` to get recommendations
