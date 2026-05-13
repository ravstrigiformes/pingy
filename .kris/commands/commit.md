# Intelligent Batch Commit

Analyze changes and create organized, semantic commits with automatic grouping, staging, and versioning.

---

## Parallel Agent Safety (CRITICAL)

**This skill uses git worktrees to prevent file scattering when parallel agents are running.**

### The Problem

When `/commit` creates multiple commits with branch operations:
1. Agent A stages files for group 1, commits
2. Parallel Agent B writes new files
3. Agent A stages group 2 (may accidentally pick up Agent B's files)
4. Files get committed to wrong group or lost

### Solution: Worktree Isolation

Each commit group is processed in an **isolated worktree** to prevent cross-contamination:

```
bgh-katalyst/
├── backend/                          # Main directory (ALWAYS on dev)
│   └── ... (parallel agents write here safely)
└── .worktrees/
    └── commit-{timestamp}/           # Temporary worktree for commits
```

**Workflow:**
1. Analyze changes in main directory
2. Create temporary worktree from current HEAD
3. Copy ONLY the specific files for each group
4. Commit in worktree
5. Push from worktree
6. Delete temporary worktree
7. Main directory unchanged

---

## Default Behavior (Fully Automatic)

1. **Analyze all unstaged and staged changes** in the repository
2. **Group related changes** by their logical category/topic (e.g., feature, bugfix, refactor, docs, style, config, etc.)
3. **Create temporary worktree** for isolated commits (parallel agent safety)
4. **For EACH group, create a SEPARATE commit** (in worktree):
   - **Stage only the files** belonging to the current group
   - **Determine version increment** for this specific commit (vW.X.Y.Z format):
     - Parse the previous commit message to get current version
     - W = Major version (breaking changes, architectural changes, major features)
     - X = Minor version (new features, significant enhancements, API changes)
     - Y = Patch version (bugfixes, small improvements, minor refactors)
     - Z = Iteration (formatting, documentation, config tweaks, minor style changes)
     - Auto-increment appropriate level based on this commit's scope
     - Auto-reset lower levels when higher ones increment (e.g., v0.0.13.0 → v0.0.14.0 resets Z to 0)
   - **Detect GitHub issue references** in:
     - Branch names (e.g., `feature/123-new-feature`)
     - File contents or comments containing `#123`, `issue #123`, etc.
     - Include appropriate closure keywords if applicable: "Resolves #123", "Fixes #123", "Closes #123", "Completes #123"
   - **Create commit message** in this format:
     ```
     <category>: <concise title describing the change>
     
     <GitHub issue reference if applicable>
     vW.X.Y.Z
     <additional context if needed>
     ```
   - **Update `package.json` and `.env` version** to match:
     ```bash
     npm pkg set version="${W}.${X}.${Y}.${Z}"
     sed -i "s/^APP_VERSION=.*/APP_VERSION=${W}.${X}.${Y}.${Z}/" .env
     git add package.json
     ```
   - **Execute the commit** with `git commit -m "title" -m "description"`
4. **Move to next group** and repeat step 3 until all groups are committed
5. **Result**: Multiple sequential commits, each focused on one logical category

## CRITICAL: One Commit Per Category Group

- DO NOT stage and commit all files at once
- Each category group gets its own separate commit
- Version numbers increment sequentially across commits
- Example sequence:
  ```
  Commit 1: config: Enforce 2-space indentation (v0.0.13.0)
  Commit 2: style: Reformat all frontend files (v0.0.13.1)
  Commit 3: refactor: Enhance batch operations service (v0.0.13.2)
  ```

## Available Flags for Manual Control

Use these flags to enable manual prompts at specific steps:

- `--manual-categories` or `-c`: Prompt for approval/modification of detected categories before grouping
- `--manual-files` or `-f`: Prompt for approval/modification of which files are included in each group
- `--manual-version` or `-v`: Prompt for manual version number input instead of auto-increment
- `--manual-issues` or `-i`: Prompt for manual GitHub issue reference input
- `--manual-proceed` or `-p`: Prompt for approval before proceeding to each next commit (commit-by-commit review)
- `--manual-all` or `-a`: Enable all manual prompts (full interactive mode)

## Examples

**Fully automatic:**
```
/commit
```

**Review categories and files before committing:**
```
/commit --manual-categories --manual-files
```

**Review everything step-by-step:**
```
/commit --manual-all
```

**Only control version numbers manually:**
```
/commit -v
```

## Commit Message Format Details

**First `-m` flag (title):**
- Format: `<category>: <concise description>`
- Category examples: `feat`, `fix`, `refactor`, `docs`, `perf`, `test`, `chore`, `style`
- Keep under 72 characters
- Use imperative mood ("add" not "added")

**Second `-m` flag (description):**
- Line 1: GitHub issue reference (if applicable)
- Line 2: Version number (vW.X.Y.Z)
- Line 3+: Additional context if needed (optional)

## Notes

- **Each category gets its own commit** - never combine multiple categories into one commit
- Commits are created sequentially, one after another
- Each commit should be self-contained and logically coherent
- Version increments with each commit based on the scope of that specific change
- Prioritize clarity - the title should make the change immediately understandable
- If no previous version found, start at v1.0.0.0
- For formatting/style changes (like reformatting files), increment Z (iteration)
- For config changes (like .editorconfig, .prettierrc), increment Z (iteration)
- For refactors or enhancements without breaking changes, increment Y (patch)
- For new features or API additions, increment X (minor)
- For breaking changes or major architectural shifts, increment W (major)
- If unsure about category boundaries, prefer smaller, more focused commits over large ones

---

## Worktree Workflow (Technical Detail)

### 1. Create Temporary Worktree

```bash
# Generate unique worktree name
TIMESTAMP=$(date +%Y%m%d-%H%M%S)
WORKTREE_DIR=".worktrees/commit-${TIMESTAMP}"

# Ensure .worktrees directory exists
mkdir -p .worktrees

# Create worktree from current HEAD (stays on same branch)
git worktree add "$WORKTREE_DIR" HEAD --detach
```

### 2. Process Each Commit Group

```bash
# For each group:
for group in "${COMMIT_GROUPS[@]}"; do
  # Copy ONLY the specific files for this group
  for file in "${group_files[@]}"; do
    cp "$file" "$WORKTREE_DIR/$file"
  done

  # Stage and commit in worktree
  git -C "$WORKTREE_DIR" add "${group_files[@]}"
  git -C "$WORKTREE_DIR" commit -m "$COMMIT_MESSAGE"
done
```

### 3. Push and Cleanup

```bash
# Push all commits from worktree
git -C "$WORKTREE_DIR" push origin HEAD:$(git -C "$WORKTREE_DIR" rev-parse --abbrev-ref @{-1})

# Remove worktree
git worktree remove "$WORKTREE_DIR"
```

### 4. Main Directory Sync

After worktree commits are pushed, the main directory can pull:

```bash
git pull --rebase origin dev
```

**Key Guarantee:** Main directory NEVER changes branch during the commit process. Parallel agents can safely continue writing files.

---

## Worktree Cleanup

If `/commit` is interrupted, worktrees may be left behind:

```bash
# List all worktrees
git worktree list

# Remove stale worktrees
git worktree prune

# Manually remove specific worktree
git worktree remove .worktrees/commit-20260306-143022
```

---

## Changelog Update (INCLUDED IN COMMIT)

> **CRITICAL:** The changelog entry is created BEFORE the commit and included in the same commit.
> This prevents changelog entries from being "left behind" as uncommitted local changes.

### Changelog File

```bash
# Calculate current week's changelog file (Monday to Sunday, ISO 8601)
TODAY=$(date +%Y-%m-%d)
DOW=$(date +%u)  # 1=Monday, 7=Sunday
MONDAY=$(date -d "$TODAY -$((DOW-1)) days" +%Y-%m-%d)
SUNDAY=$(date -d "$MONDAY +6 days" +%Y-%m-%d)
CHANGELOG_FILE=".kris/changelogs/${MONDAY}_to_${SUNDAY}.md"
```

### Entry Format (Per Commit)

For each commit group, BEFORE committing:

1. **Create the changelog entry** with known info and placeholders:

```markdown
#### {type}: {description} (v{VERSION})
- **Commit**: `(pending)`
- **Issue**: #{NUM} (if applicable)
- **PR**: (pending)
- **Area**: {Backend|Frontend|Full Stack|Docs/Config}
- **Version**: v{VERSION}
- **Files**: `{file1}`, `{file2}`, ...
- **Changes**:
  - {Change 1}
  - {Change 2}
```

2. **Update summary statistics** (total commits, features/bugs/other, by area)

3. **Stage the changelog file** alongside the group's other files

4. **Commit** (changelog is now part of the commit)

5. **Back-fill commit hash** by amending (pre-push, safe):
```bash
COMMIT_HASH=$(git rev-parse --short HEAD)
sed -i "s/\`(pending)\`/\`$COMMIT_HASH\`/" "$CHANGELOG_FILE"
git add "$CHANGELOG_FILE"
git commit --amend --no-edit
```

6. **After push**, the PR # field stays as `(pending)` since `/commit` does not create PRs.
   The `/worktree-cleanup` safety net or the next `/fix` run will back-fill it.

### Workflow Per Group

```bash
for group in "${COMMIT_GROUPS[@]}"; do
  # 1. Copy group files to worktree
  # 2. Create changelog entry with placeholders
  # 3. Copy changelog to worktree
  # 4. Stage all files + changelog
  # 5. Commit
  # 6. Amend with actual commit hash
done

# Push all commits
git push -u origin "$BRANCH_NAME"
```

### Example Multi-Commit Output

```
📓 Changelog Included in Commits:
   .kris/changelogs/2026-03-10_to_2026-03-16.md
   3 entries committed under Monday, March 10:
     - config: Enforce 2-space indentation (v0.0.13.0) ← `abc1234`
     - style: Reformat all frontend files (v0.0.13.1) ← `def5678`
     - refactor: Enhance batch operations service (v0.0.13.2) ← `ghi9012`
   Summary: 18 commits (+3), 14 resolved
   Note: PR # fields show (pending) — back-filled by /worktree-cleanup
```