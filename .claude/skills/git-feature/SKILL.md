---
name: git-feature
description: Create GitHub issues, feature branches, and commits with intelligent file grouping and conventional commit messages
allowed-tools: Read, Glob, Grep, Bash, AskUserQuestion
user-invocable: true
---

# Git Feature Workflow

This skill automates the git workflow for creating issues, branches, and commits with intelligent analysis.

## Workflow Steps

When the user invokes `/git-feature`, follow these steps:

### Step 1: Analyze Changes

Run these commands to understand the current state:

```bash
git status --short
git diff --name-only HEAD
git diff --stat HEAD
```

### Step 2: Group Related Changes

Analyze the changed files and group them by feature/purpose:
- Look at file paths to understand modules/features affected
- Read the actual diffs to understand what was changed
- Identify unrelated changes (settings files, formatting, etc.)

### Step 3: Determine Change Type

Based on the changes, determine the type:
- `fix/` - Bug fixes, error corrections
- `feature/` - New functionality
- `refactor/` - Code restructuring without behavior change
- `chore/` - Maintenance, dependencies, config
- `docs/` - Documentation only
- `test/` - Test additions or fixes
- `perf/` - Performance improvements

### Step 4: Ask User for Confirmation

Use `AskUserQuestion` to confirm:
1. The detected change type
2. A short feature name (for branch naming)
3. Which files to include (show grouped files)

Example:
```
Detected: fix (bug fix)
Suggested branch: fix/spa-auth-session-handling

Files to commit:
✓ app/Controllers/AuthController.php (auth fix)
✓ app/Routes/api.php (auth fix)
✗ .claude/settings.local.json (unrelated)

Proceed with this grouping?
```

### Step 5: Create GitHub Issue (if gh CLI available)

Check if `gh` is available:
```bash
where gh 2>nul || which gh 2>/dev/null
```

If available, create an issue with:
- **Title**: Concise description of the change
- **Body**:
  - ## Problem - What issue this addresses
  - ## Solution - What changes were made
  - ## Files Changed - List of affected files

### Step 6: Create Branch

```bash
git checkout -b {type}/{issue_number}-{feature-name}
# or without issue number if gh not available:
git checkout -b {type}/{feature-name}
```

Branch naming conventions:
- Use kebab-case: `fix/auth-session-handling`
- Keep it short but descriptive
- Include issue number when available: `fix/42-auth-session`

### Step 7: Stage Related Files Only

Only stage files that are part of this feature:
```bash
git add file1.php file2.ts file3.vue
```

Exclude:
- `.claude/settings*.json` - Local settings
- Unrelated formatting changes
- Files that belong to a different feature

### Step 8: Commit with Conventional Message

Create a commit with this format:

```
{type}: {short description}

{longer description if needed}

{bullet points of key changes}

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>
```

Types for commit message:
- `fix:` - Bug fix
- `feat:` - New feature
- `refactor:` - Code restructuring
- `chore:` - Maintenance
- `docs:` - Documentation
- `test:` - Tests
- `perf:` - Performance

### Step 9: Push and Provide Links

```bash
git push -u origin {branch-name}
```

Output:
- Branch name
- PR creation URL
- Issue URL (if created)

## Example Usage

User: `/git-feature`

Claude analyzes and responds:
```
Analyzing changes...

Found 6 modified files grouped as:

📦 Auth Session Fix (5 files)
  - app/Controllers/AuthController.php
  - app/Controllers/TwoFactorController.php
  - app/Routes/api.php
  - resources/js/composables/useAuth.ts
  - resources/js/types/index.d.ts

🚫 Excluded (1 file)
  - .claude/settings.local.json (local settings)

Type: fix
Branch: fix/spa-auth-session-handling

Create issue and commit?
```

## Handling Edge Cases

### Multiple Features in Changes
If changes span multiple unrelated features:
1. Ask user which feature to commit first
2. Stage only those files
3. Remind user to run `/git-feature` again for remaining changes

### No Changes
If working tree is clean:
```
No changes to commit. Working tree is clean.
```

### Uncommitted Changes on Wrong Branch
If on main/master with changes:
1. Create the feature branch first
2. Changes will move to the new branch
3. Then proceed with staging and commit

### gh CLI Not Available
Skip issue creation, but still:
1. Create branch with descriptive name
2. Commit with detailed message
3. Push and provide manual PR URL

## Commit Message Templates

### Bug Fix
```
fix: resolve {what was broken}

Problem: {description of the bug}
Solution: {what was done to fix it}

- {specific change 1}
- {specific change 2}

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>
```

### Feature
```
feat: add {feature name}

{Why this feature was needed}

- {key implementation detail 1}
- {key implementation detail 2}

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>
```

### Refactor
```
refactor: {what was restructured}

{Why this refactor was needed}

- {change 1}
- {change 2}

No functional changes.

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>
```
