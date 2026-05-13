# /fix - Intelligent Issue Creation, Commit, Push & Tag

> **Mirror:** `.kris/commands/fix.md` is a byte-for-byte copy of this file. `.claude/commands/` is the harness-loaded canonical; `.kris/commands/` is the checked-in mirror. After editing this file, run: `cp .claude/commands/fix.md .kris/commands/fix.md`

Streamlined workflow that creates a GitHub issue, branches properly, validates code, commits, pushes, creates PR, merges to dev, and syncs local - all with smart recommendations you can approve or customize.

**Merges to dev by default.** Use `--no-dev` to skip the merge (e.g., when PR needs external review).

**Now integrates with `/issue` for planned work and includes parallel testing agents.**

---

## Branch contract

> **Why this exists:** `/fix` runs `git stash`, `git merge`, `git checkout --theirs .`, and triggers `/preview` and `/worktree-cleanup` — every one of those can move the main repo's HEAD. If `/fix` exits on a different branch than it entered, any parallel agent working on the original branch may have its WIP relocated.

- **Starts on:** any (feature/bugfix branch in worktree, or any branch in main repo)
- **Ends on:** same branch as start (assert at end)
- **Touches branches:** current branch + `dev` (when auto-merging the PR)
- **Worktree-first:** auto-detects — uses worktree path if running inside one

**Self-check (every run):**

```bash
# Branch contract: record start branch+HEAD
_SKILL_START_BRANCH=$(git rev-parse --abbrev-ref HEAD)
_SKILL_START_HEAD=$(git rev-parse HEAD)                # captures starting HEAD branch
# ...skill body...
# Branch contract end: assert unchanged
if [[ "$(git rev-parse --abbrev-ref HEAD)" != "$_SKILL_START_BRANCH" || "$(git rev-parse HEAD)" != "$_SKILL_START_HEAD" ]]; then
  echo "ERROR: branch contract violated" >&2; exit 1
fi
```

**Stash guard:** existing stash-aware-sync.md protocol still applies; combine with branch_contract_start so an interrupted skill leaves a recoverable stash AND the original branch checked out.

---

## Sync merge policy

> **Why this exists:** PR #538 / Issue #537 was a phantom merge — every code-file conflict between `dev` and the bugfix branch resolved against `dev`, dropping the fix. `/fix`'s default `git merge origin/dev --no-edit` provides no policy, so whatever the merge tool produces is what ships.

When `/fix` syncs `dev` into the feature branch (Phase 6.5 reconcile, pre-PR rebase, post-PR sync), it MUST use:

```bash
```

`auto-ours` keeps the feature branch's version of any conflicted code-extension file (`.ts .tsx .vue .php .js .jsx .mjs .cjs .css .scss .sass .html .blade.php`) when the feature branch has commits beyond the merge base touching that file. This protects the fix the user is shipping.

Bare `git merge origin/dev --no-edit` and `git checkout --theirs .` on code files are forbidden in this skill. The previous `--theirs` resolution on stash-pop conflicts is acceptable ONLY for non-code files; code-file stash-pop conflicts must surface to the user.

---

## Phantom-merge detection

Before `/fix` finalizes any sync merge or auto-merges the PR, it MUST verify the staged diff is non-empty across code extensions when the source branch has `fix(/feat(/perf(/refactor(` commits. `sync-safe-merge.sh` runs this check automatically; the skill must NOT bypass it with `--no-verify` or by re-implementing the merge inline.

If phantom-detect fires:
1. The merge is aborted (`git merge --abort`).
2. A report is written to `.kris/audits/sync-aborted-<date>-fix.md`.
3. `/fix` halts and prints the report path to the user. No PR is created or merged when this fires.

> **User Experience Philosophy:**
> - Every selection has a **recommended option** clearly marked
> - Users can press **Enter repeatedly** to accept all defaults and get a sensible result
> - Smart defaults mean less thinking, but full customization is always available
> - The command **always asks for confirmation** before taking action - never assumes

---

## Session Scope Rule (CRITICAL — READ FIRST)

See `.kris/context/session-scoping.md` for the canonical session-scoping rules (per-session cherry-pick, parallel-agent safety, baseline behavior, orchestrator-mode reconciliation, auto-mode checkpoint behavior). Every session-scoped skill (`/fix`, `/commit`, `/hotfix`, `/issue`, `/promote`) follows the same protocol — read the file once, then proceed.

---

## Issue Style Guide (CRITICAL)

> **All issues MUST follow the org templates at `mis-bghmc/.github/.github/ISSUE_TEMPLATE/`**

### Issue Updates During Fix Process

When discoveries or nuances arise during `/fix` that need to be documented:

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

### Title Conventions (from org templates)

| Type | Prefix | Labels | Example |
|------|--------|--------|---------|
| Bug | `[Bug]:` | `bug`, `needs-triage` | `[Bug]: Payee geolocation filters not cascading through ancestor levels` |
| Feature | `[Feature]:` | `enhancement` | `[Feature]: Add Fund Adjustment Types lookup module with CRUD operations` |
| Tech | `[Tech]:` | `tech`, `maintenance` | `[Tech]: Enhance Bank and Branch module with extended metadata fields` |
| Refactor | `[Refactor]:` | `enhancement` | `[Refactor]: Extract form logic into reusable Form components` |
| Chore | `[Chore]:` | `enhancement` | `[Chore]: Update database seeders and DBML schema` |

**Title Rules:**
- Use **sentence case** (capitalize first word only, except proper nouns)
- Keep titles **concise** (under 80 characters)
- Describe the **problem** (for bugs) or **outcome** (for features/tech)
- Be specific about what module/component is affected

### Section Headers (from org templates)

**Bug Issues (Bug.yml):**
```markdown
## Description
## Steps to Reproduce
## Expected Behavior
## Actual Behavior
## Screenshot / Logs
## Severity              <!-- Low|Medium|High|Critical -->
## Changes               <!-- Only for retroactive /fix issues -->
```

**Feature Issues (Feature.yml):**
```markdown
## Problem / Use Case
## Proposed Solution
## Impact
## Acceptance Criteria   <!-- REQUIRED, checkbox format -->
## Estimated Size        <!-- Small|Medium|Large|Epic -->
```

**Tech/Maintenance Issues (technical_maintenance.yml):**
```markdown
## Context / Motivation
## Planned Changes
## Impact
## Risks / Notes
## Acceptance Criteria   <!-- REQUIRED, checkbox format -->
## Estimated Size        <!-- Small (safe, isolated)|Medium (multiple files / modules)|Large (cross-cutting / needs extra review) -->
```

### Writing Style

| Element | Style | Example |
|---------|-------|---------|
| Descriptions | Past tense for completed, present for planned | "Session was timing out" vs "Users can upload avatars" |
| Acceptance Criteria | Checkbox format, past participle for done | `- [x] Identity column detection cached` |
| File references | Backtick code format | `` `AuthService.php` `` |
| Commit references | Backtick with hash | `` `abc1234` `` |
| Severity | Title case | `Low`, `Medium`, `High`, `Critical` |
| Size | As in dropdown | `Small`, `Medium`, `Large`, `Epic` or descriptive |

### Acceptance Criteria Format

**REQUIRED for Feature and Tech issues. Use checkbox format:**

```markdown
## Acceptance Criteria

- [x] Criterion that is already complete
- [x] Another completed criterion
- [ ] Criterion still pending (rare for retroactive issues)
```

**Good criteria are:**
- Testable and verifiable
- Written in past participle for completed items ("cached", "updated", "added")
- Specific, not vague ("Works on SQL Server" not "Works correctly")

### Labels (from org templates)

| Issue Type | Labels |
|------------|--------|
| Bug | `bug`, `needs-triage` |
| Feature | `enhancement` |
| Tech | `tech`, `maintenance` |
| Refactor | `enhancement` |
| Chore | `enhancement` |

### Changes Section (Retroactive Issues Only)

When creating issues for already-committed work via `/fix`, include:

```markdown
## Changes

- `{file1}` - {Brief change description}
- `{file2}` - {Brief change description}
```

## Context Resolution (CRITICAL)

Context for `/fix` comes from **three sources**, in priority order:

### 1. Explicit argument (highest priority)
```
/fix login timeout             # Focus on changes related to login timeout
/fix auth module               # Focus on changes in the auth module
/fix PayeeService              # Focus on changes to PayeeService
/fix "user can't save form"    # Focus on changes fixing this specific issue
```

### 2. Prior conversation context (auto-detected)
If the conversation already contains work context — e.g., you just implemented a feature, fixed a bug, or discussed specific changes — `/fix` uses that as implicit context. **No argument needed.**

**How it works:**
- Summarize the prior conversation work into a short description (1-2 sentences)
- Use that summary as the context filter for candidate scoring
- **Auto-select the best matching candidate** — skip Checkpoint #0 entirely
- Inform the user which target was auto-detected:

```
🎯 CONTEXT DETECTED (from conversation)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  Summary: "Added physical copy tracking for document steps"
  Best match: #1 Physical Copy Tracking Feature (96% confidence)

  Proceeding with this target...
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

**When conversation context is sufficient:**
- User worked on a specific task/feature/fix in this session
- The changed files align with what was discussed
- There's a clear "what was done and why" from the conversation

**When conversation context is NOT sufficient (fall back to Checkpoint #0):**
- Bare `/fix` with no prior conversation (fresh session)
- Conversation was about something unrelated to the uncommitted changes
- Multiple unrelated topics were discussed with no clear "main work"

### 3. No context (lowest priority)
```
/fix                           # Analyze ALL changes, recommend groupings
```
Analyzes all uncommitted changes and groups them intelligently. Shows Checkpoint #0 for user selection.

**Summary:**
- With explicit argument: Filters and prioritizes changes matching your description
- With conversation context: Auto-selects best candidate, skips Checkpoint #0
- Without either: Full candidate ranking with interactive selection
- Mixed changes: Will recommend splitting unrelated changes into separate branches

---

## Flags

### `--auto` or `-a` (Automatic Mode)

Skip all interactive checkpoints and use recommended defaults:

```
/fix --auto                    # Full auto: analyze → validate → issue → branch → commit → push
/fix -a                        # Same as above (short alias)
/fix login timeout --auto      # Auto mode with context filter
/fix -a "avatar feature"       # Auto mode with quoted context
```

**What `--auto` does:**
- Selects the #1 recommended fix candidate automatically
- Uses recommended severity/size without asking
- Creates issue with generated content (no preview)
- Creates branch and commits without confirmation
- Skips git tag (recommended default)
- Still runs validation - will STOP if validation fails (safety)

**When to use `--auto`:**
- You trust the analysis and just want it done
- Quick fixes where you've already reviewed the changes
- CI/CD pipelines or scripted workflows

**When NOT to use `--auto`:**
- First time using `/fix` (see how it works first)
- Large or complex changes (review is valuable)
- When you have mixed changes that need splitting

**Example auto flow:**
```
> /fix --auto

📊 Analyzing changes...
🎯 Auto-selected: "Session timeout fix" (94% confidence)
🔍 Validating code...
   ✅ PHP Syntax passed
   ✅ Pint passed
   ✅ Tests passed (3/3)
📋 Creating issue #42...
🌿 Creating branch bugfix/42-fix-session-timeout...
💾 Committing...
🚀 Pushing to origin...

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
✅ /fix --auto COMPLETE

   Issue: #42
   Branch: bugfix/42-fix-session-timeout
   Commit: abc1234

   Remaining: 4 other change groups (run /fix again)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

**Safety note:** Even in auto mode, validation failures will STOP the process. You'll need to fix issues and retry. This prevents pushing broken code.

### `--issue <#>` or `-i <#>` (Attach to Existing Issue)

Link your changes to an existing issue created via `/issue`:

```
/fix --issue 97              # Attach changes to existing issue #97
/fix -i 97                   # Short alias
/fix --issue 97 --auto       # Auto-commit attached to issue #97
```

**What `--issue` does:**
- Skips issue creation (uses the existing issue)
- Creates branch from issue: `bugfix/97-{slug-from-issue-title}`
- Commit message references: `Resolves #97`
- Updates project status from "Backlog" to "In Progress"
- Updates project start date to today if not set

**Use case:**
- Planned work: Create issue first with `/issue`, implement, then `/fix --issue #`
- Picked up tasks: Issue already exists in backlog, now you're implementing

### `--test` (Run Tests with Parallel Fix Agent)

Run Pest tests and automatically fix failures in parallel:

```
/fix --test                  # Run tests, spawn fix agent for failures
/fix --test --auto           # Auto-run, auto-fix, auto-commit
```

**What `--test` does:**
1. Runs relevant Pest tests based on changed files
2. If tests PASS → continues to commit
3. If tests FAIL → spawns **Test Fix Agent** in parallel:
   - Analyzes failure output
   - Identifies root cause
   - Proposes fix
   - Applies fix automatically (with user confirmation in non-auto mode)
   - Re-runs tests to verify
4. Continues when all tests pass

**Test Fix Agent behavior:**
- Reads test failure output
- Traces error to source file
- Analyzes expected vs actual behavior
- Generates minimal fix
- Reports what it changed and why

### `--review` (Deep Code Review via Agent)

Spawn a dedicated agent that semantically reviews every changed file before committing:

```
/fix --review                # Agent reviews code, then proceed interactively
/fix --review --auto         # Agent reviews, auto-proceed on PASS/WARN
```

**What `--review` does:** See "Review Validation Agent" in the Testing Agents section below.

### `--qa` (Generate QA Checklist)

Include QA testing artifacts with your commit:

```
/fix --qa                    # Generate QA checklist in PR/issue
/fix --qa --auto             # Auto-generate, include in issue body
```

**What `--qa` does:**
- Analyzes changes to identify testable scenarios
- Generates manual QA checklist
- Adds checklist to issue body under "## QA Testing"
- Spawns **QA Agent** to prepare:
  - Test scenarios based on code changes
  - Edge cases to verify
  - Regression areas to check
  - Screenshots/recordings needed

**QA Agent output:**
```markdown
## QA Testing

### Scenarios to Test
- [ ] Login with valid credentials → session lasts configured duration
- [ ] Login with "Remember Me" → extended session
- [ ] Session timeout → redirect to login page

### Edge Cases
- [ ] Network disconnect during session
- [ ] Multiple tabs with same session
- [ ] Config value = 0 (disabled timeout)

### Regression Check
- [ ] Existing logout functionality still works
- [ ] Session refresh on activity still works
```

### `--thorough` or `-t` (Full Validation: Review + Tests)

The "do everything" flag — runs **both** `--review` and `--test` in sequence:

```
/fix -t                        # Full validation: agent review + Pest tests
/fix --thorough                # Same as -t
/fix -t --auto                 # Full validation, auto-proceed
/fix -t -a                     # Shorthand for above
```

**What `-t` does:**

Equivalent to `--review --test`. Runs in this order:
1. **Review Agent** — reads changed files, checks imports/types/contracts/payloads
2. If review PASS/WARN → proceeds to tests
3. If review FAIL → stops, shows errors, asks user to fix
4. **Pest Tests** — runs relevant tests based on changed files
5. If tests FAIL → spawns Test Fix Agent to auto-fix
6. Both must pass before proceeding to commit

**This is the recommended flag for non-trivial changes.** It catches both structural issues (missing imports, type mismatches) and behavioral issues (broken tests).

**When NOT to use `-t`:**
- Docs-only or config-only changes (no code to validate)
- Time-sensitive hotfixes (adds review time)
- When you only need one: use `--review` or `--test` individually

### `--no-dev` (Skip Merge to Dev)

By default, `/fix` merges the PR to dev and syncs local after creation. Use `--no-dev` to skip this when external review is needed:

```
/fix                         # Create PR, merge to dev, sync local (DEFAULT)
/fix --no-dev                # Create PR only — don't merge
/fix --no-dev -n             # Short: skip merge, useful when PR needs review
```

**Default behavior (merge to dev):**

After PR creation, `/fix` automatically runs an additional phase:

1. Normal `/fix` completes through PR creation (Phase 7.8)
2. **New Phase 7.10: Merge to dev**
   ```bash
   # Merge the just-created PR
   gh pr merge ${PR_NUM} --merge

   # Sync local dev using stash-aware pattern (parallel agent safe)
   # See "Stash-Aware Sync Pattern" below
   if [ "$IN_WORKTREE" = true ]; then
     TARGET_DIR="${MAIN_REPO}"
   else
     TARGET_DIR="."
   fi

   # Stash any uncommitted work (ours + parallel agents')
   STASH_MSG="fix-dev-sync-#${ISSUE_NUM}"
   git -C "$TARGET_DIR" stash push --include-untracked -m "$STASH_MSG" 2>/dev/null
   STASHED=$?

   # Fetch and merge
   git -C "$TARGET_DIR" fetch upstream dev 2>/dev/null || git -C "$TARGET_DIR" fetch origin dev
   git -C "$TARGET_DIR" merge upstream/dev --no-edit 2>/dev/null || \
     git -C "$TARGET_DIR" merge origin/dev --no-edit

   # Restore stashed work
   if [ "$STASHED" -eq 0 ]; then
     git -C "$TARGET_DIR" stash pop 2>/dev/null || {
       # Pop conflicts = our files are already in the merge
       # Drop stash, parallel agents' unrelated files survive in working tree
       git -C "$TARGET_DIR" checkout --theirs . 2>/dev/null
       git -C "$TARGET_DIR" stash drop
     }
   fi
   ```
3. Continue to Phase 8 (completion marker) and Phase 9 (final summary)

### Stash-Aware Sync Pattern (CRITICAL — Parallel Agent Safety)

Any git operation that touches the main repo working tree (checkout, merge, pull) can fail if parallel agents have uncommitted changes. The stash-aware pattern prevents this:

```bash
# 1. Stash everything (our work + parallel agents' work)
git stash push --include-untracked -m "$CONTEXT_MSG" 2>/dev/null
STASHED=$?

# 2. Perform the git operation (merge, pull, checkout, etc.)
git merge origin/dev --no-edit   # or checkout, pull, etc.

# 3. Restore stashed work
if [ "$STASHED" -eq 0 ]; then
  git stash pop 2>/dev/null || {
    # Conflict means our stashed files overlap with what we just merged
    # The merge already has the correct version — drop the stash
    # Parallel agents' unrelated changes survive because they don't conflict
    git stash drop
  }
fi
```

**Why this is safe:**
- Stash captures ALL uncommitted state (tracked + untracked)
- After merge, `stash pop` restores parallel agents' unrelated changes
- If pop conflicts, it's because the stashed files are the same ones we just merged — drop is safe
- No `git checkout -- file` or `rm` that could destroy parallel agents' work

**This pattern is used by:** `/fix`, `/stage`, `/hotfix`, `/preview`

**When to use `--no-dev`:**
- Changes that need code review from another person
- Large or risky features that should sit in PR for review
- When branch protection rules require approvals

**Combines with other flags:**
```
/fix --auto                  # Zero-interaction: validate → commit → PR → merge → sync
/fix -t                      # Thorough validation, then merge
/fix --issue 97              # Attach to existing issue, then merge
/fix --no-dev                # Stop after PR creation, don't merge
```

**Auto-mode in worktrees:** When `AUTO_MODE=true` (inside a worktree), the merge happens without confirmation. The `/worktree` pre-flight phase already validated the work.

**Summary output with `--dev`:**
```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
✅ /fix COMPLETE
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

📋 Issue: #347
💾 Committed: abc1234
🚀 Pushed: origin/feature/347-escape-close-dialogs
📝 PR #352 created → dev
🔀 PR #352 merged to dev
📥 Local dev synced

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

📌 NEXT: /stage dev staging  (when ready for QA)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

### `--batch` (Process Multiple Issues)

Iteratively process all fixable uncommitted changes:

```
/fix --batch                 # Interactive batch mode
/fix --batch --auto          # Full auto batch processing
/fix --batch --confidence=90 # Only process 90%+ confidence matches
```

**What `--batch` does:**
1. Analyzes ALL uncommitted changes
2. Groups into logical fix candidates
3. Scores each by confidence
4. Processes each group sequentially:
   - Creates/attaches issue
   - Validates code
   - Commits and pushes
5. Reports summary at end

**Example batch output:**
```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
📊 BATCH PROCESSING COMPLETE
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Processed: 4 fix groups
Committed: 3 successful
Skipped:   1 (low confidence)

✅ #97 bugfix/97-session-timeout (abc1234)
✅ #98 feature/98-user-avatars (def5678)
✅ #99 bugfix/99-payee-validation (ghi9012)
⏭️  Config updates (skipped - 65% confidence)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

---

## Testing Agents

The `/fix` skill can spawn specialized agents to handle testing tasks in parallel:

### Test Fix Agent

> **Model assignment:** this agent runs on **Sonnet**. The orchestrator must pass `model: "sonnet"` when spawning.
> Rationale: one failing test → one minimal fix is a convergent, bounded workload; Opus is wasted here.
> See also: `.kris/context/model-assignments.md` — canonical model-selection reference.

**Trigger:** Test failures during validation phase
**Purpose:** Automatically identify and fix failing tests

**Process:**
1. Parse test failure output
2. Identify failing test file and assertion
3. Trace to source code causing failure
4. Analyze expected vs actual behavior
5. Generate minimal fix
6. Apply fix and re-run test
7. Report changes to user

**Example output:**
```
🔧 TEST FIX AGENT
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Failed: AuthServiceTest::test_session_timeout_config
  Expected: 7200
  Actual:   1800

Root cause identified:
  `AuthService.php:42` - Using hardcoded value instead of config

Fix applied:
  - return 1800;
  + return config('session.lifetime', 1800) * 60;

Re-running test...
✅ AuthServiceTest::test_session_timeout_config PASSED

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

### QA Agent

> **Model assignment:** this agent runs on **Sonnet**. The orchestrator must pass `model: "sonnet"` when spawning.
> Rationale: structured checklist generation from a known diff — templated output, low reasoning depth.
> See also: `.kris/context/model-assignments.md` — canonical model-selection reference.

**Trigger:** `--qa` flag or when creating PR
**Purpose:** Generate comprehensive QA testing checklist

**Process:**
1. Analyze changed files and their purpose
2. Identify user-facing functionality affected
3. Generate test scenarios (happy path)
4. Generate edge cases
5. Identify regression areas
6. Format as markdown checklist

**Example output:**
```
📋 QA AGENT - Test Plan Generated
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

## QA Testing

### Affected Areas
- Authentication: Session timeout behavior
- User experience: Login persistence

### Test Scenarios

**Happy Path:**
- [ ] Login → session active for configured duration
- [ ] Activity refreshes session timer
- [ ] Timeout → graceful redirect to login

**Edge Cases:**
- [ ] SESSION_LIFETIME=0 (should disable timeout)
- [ ] SESSION_LIFETIME=1440 (24 hours)
- [ ] Multiple browser tabs
- [ ] Network interruption during session

**Regression:**
- [ ] Logout still works immediately
- [ ] "Remember Me" still extends session
- [ ] Session data persists correctly

### Test Data Needed
- User account with standard permissions
- Config values: 30, 60, 120, 1440 minutes

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

### Review Validation Agent

> **Model assignment:** this agent runs on **Opus**. The orchestrator must pass `model: "opus"` when spawning.
> Rationale: deep cross-file semantic review — same correctness bar as the `/wt` reviewer; downgrading lets silent bugs through.
> See also: `.kris/context/model-assignments.md` — canonical model-selection reference.

**Trigger:** `--review` flag (or `-t` / `--thorough` which includes review)
**Purpose:** Deep semantic review of changed files to catch issues that syntax checks and builds miss

**Process:**
1. Receive the list of files in the fix candidate
2. Read each file in full (not just the diff)
3. For each file, perform these checks:

**Import Validation:**
- Every `import` resolves to an existing file/module
- No unused imports left behind
- No missing imports (references to unimported symbols)

**Emit/Prop Contract Validation (Vue):**
- Child `defineEmits` signature matches parent `@event` handler parameter types
- Child `defineProps` matches parent's bound props
- No `emit('event')` without corresponding `defineEmits` declaration

**Payload Validation:**
- API mutation payloads include all required fields from the corresponding `*Request.php`
- Field names match (no camelCase/snake_case mismatches)
- Optional fields use `|| undefined` pattern (not `|| null` which sends null)

**Cross-File Consistency:**
- If a type/interface changed, all consumers of that type are updated
- If an emit signature changed, all parents using that component are updated
- If a composable return shape changed, all callers destructure correctly

**Type Safety (lightweight):**
- Ref types match their assignments
- Function parameter types match call sites
- No obvious `any` leaks in critical paths (emit handlers, API payloads)

4. Produce a report with PASS / WARN / FAIL per file
5. If any FAIL: block the fix and show file:line references
6. If only WARN: show concerns, let user decide
7. If all PASS: proceed silently

**Agent prompt template:**
```
You are a code review agent. Your job is to deeply validate the following
files that are about to be committed. Focus ONLY on correctness issues
that would cause runtime errors, compile errors, or silent bugs.

DO NOT flag:
- Style issues (formatting, naming conventions)
- Missing documentation or comments
- Performance concerns (unless catastrophic)
- Existing issues in unchanged code

DO flag:
- Missing or wrong imports
- Emit/prop contract mismatches between parent and child
- API payload fields that don't match backend validation rules
- Type mismatches at function boundaries
- References to deleted/renamed symbols

Files to review:
{file_list_with_full_content}

For each file, report:
- ✅ PASS: {reason}
- ⚠️ WARN: {issue} at {file}:{line} — {suggestion}
- ❌ FAIL: {issue} at {file}:{line} — {must fix because}

End with overall verdict: PASS / WARN / FAIL
```

**Key constraint:** The agent only reads files in the fix candidate + their direct imports/parents (to check contracts). It does NOT review the entire codebase.

### Build Validation Agent

**Trigger:** Always runs during validation phase
**Purpose:** Ensure code compiles and passes static checks

**Process:**
1. PHP syntax check (`php -l`)
2. Code style (`vendor/bin/pint --test`)
3. TypeScript compilation (`npx vue-tsc --noEmit`)
4. Build verification (`npm run build`)

**On failure:** Reports specific errors and suggests fixes.

---

## Project Integration

When using `--issue` or creating new issues, `/fix` integrates with GitHub Projects:

### Project Fields Updated

| Field | When | Value |
|-------|------|-------|
| Status | On branch creation | `In Progress` |
| Start Date | On first commit | Today (if not set) |
| End Date | On issue close | Today |
| System | On issue creation | Auto-detected |
| Priority | On issue creation | Based on severity |
| Size | On issue creation | Based on scope |

### Status Transitions

```
/issue creates     → Status: "Backlog"
/fix --issue #     → Status: "In Progress", Start Date: today
PR merged          → Status: "Done", End Date: today (via GitHub automation)
```

---

## Phase 0: Orchestrator-Mode Worktree Delegation (CRITICAL — runs FIRST)

> *If the user typed `/fix` from the main repo while a worktree session is active, route the work to the worktree instead of the main repo. The user shouldn't have to `cd` and start a fresh `claude` session every time.*

### 0.1 Decide whether to delegate

`/fix` operates on the *feature branch* and the *uncommitted changes that produced it*. When invoked from the **main repo on a shared branch** (`dev`, `staging`, `beta`, `main`), running locally is almost always wrong — the main repo's WIP belongs to other parallel agents, not this session.

**Detect orchestrator mode** by combining these signals (evaluated in order):

```
SIGNAL A — Inside a worktree:
  Resolve worktree status:
    git -C "$PWD" rev-parse --git-common-dir   →  ".git"  means main repo
                                                  ≠ ".git" (resolves to <main>/.git/worktrees/<name>) means worktree
  IF inside a worktree → RUN_LOCAL (do not delegate; jump to Phase 1)

SIGNAL B — Issue # provided as arg AND worktree match exists:
  /fix 412                                       →  TARGET_ISSUE = 412
  /fix 412 "context note"                        →  TARGET_ISSUE = 412, context arg unchanged
  Search .worktrees/ for *-${TARGET_ISSUE}-*    →  if exactly one match: DELEGATE
                                                    if multiple: ASK USER which one
                                                    if zero: NORMAL local /fix (e.g. fixing dev directly)

SIGNAL C — Conversation context points at a worktree:
  When did this session last touch a worktree?
    - Was `/wt <N>` or `/worktree <N>` run in this session?
    - Was `/preview <N>` run from the main repo in this session?
    - Does any `.kris/wt-sessions/<N>-<slug>/` exist whose mtime is within the
      last hour AND has no matching shipped task file (PR not opened yet)?
    - Does `.worktrees/*-<N>-*/backend/.preview-state` exist whose `dev_head`
      matches the current main repo HEAD?
  IF any are true with a single inferred TARGET_ISSUE → DELEGATE

SIGNAL D — Bare `/fix` from main repo with no worktree context:
  → NORMAL local /fix (Phase 1 onward)
```

**Hard rule:** delegation only happens in **orchestrator mode** (main repo, shared branch, worktree match). When inside a worktree (Signal A), `/fix` runs locally regardless — a sub-agent inside an already-correct cwd would be pure overhead.

### 0.2 Confirm before delegating (one-shot)

When Signal B/C resolves to a single worktree, show a one-line confirmation and proceed unless the user redirects:

```
🔀 Delegating /fix to worktree for #${TARGET_ISSUE}
   Worktree: ../.worktrees/${WORKTREE_NAME}
   Branch:   ${FEATURE_BRANCH}

   [Enter] proceed  |  [l] run locally instead  |  [c] cancel
```

If the user picks `l`, set `RUN_LOCAL=true` and jump to Phase 1. If `c`, abort.

### 0.3 Spawn the delegated /fix in an isolated Windows Terminal tab

> **Why a tab spawn instead of an in-process `Agent()`:** an in-process sub-agent shares the orchestrator's OS process and cwd. Branch swaps in the orchestrator's main repo (or a parallel agent's activity) can race against the sub-agent's worktree-targeted operations. A separate `claude` CLI in its own Windows Terminal tab, with cwd pinned to the worktree, is fully insulated — git worktrees give HEAD-level isolation, and a separate process gives cwd isolation. See `feedback_branch-sanity-on-anomaly.md`.

Construct and run:

```bash
# Ensure session brief exists (the spawned tab will read it for context)
SESSION_ID="${TARGET_ISSUE}-${SLUG}"

# Compose the prompt. Read-list points at brief/review/TASK so the tab
# inherits orchestrator's analysis without paying for a re-scan.
PROMPT=$(cat <<EOF
You are the /fix delegate for session ${SESSION_ID}.

Read these files first (absolute paths, in order):
  1. ${GIT_ROOT}/.claude/commands/fix.md
  2. ${GIT_ROOT}/.kris/context/session-scoping.md
  3. ${GIT_ROOT}/.kris/wt-sessions/${SESSION_ID}/brief.md
  4. ${GIT_ROOT}/.kris/wt-sessions/${SESSION_ID}/review.md  (if present)
  5. ./TASK.md
  6. ./.preview-state  (if present, root of worktree)

Then execute /fix from Phase 1 onward (Phase 0 will short-circuit via Signal A).

The session brief gives you the PR title/body, the review file gives you
remaining-notes context for the PR description, and .preview-state triggers
Phase 6.5 reconciliation.
EOF
)

# Resolve report path under MAIN_BACKEND so the orchestrator can read it.
REPORT_FILE="${GIT_ROOT}/.kris/wt-sessions/${SESSION_ID}/spawn-report.json"

# Spawn isolated tab. Falls back to Start-Process pwsh if Windows Terminal is missing.
  --issue "${TARGET_ISSUE}" \
  --slug "${SLUG}" \
  --worktree "${WORKTREE_PATH}" \
  --prompt "${PROMPT}" \
  --report-file "${REPORT_FILE}"
```

The spawned tab inherits no orchestrator context — that's the point. The brief/review/TASK files provide everything it needs.

### 0.4 Surface the spawn + handle the report

After spawning, the orchestrator's job in main repo is small:

1. Print to user: tab title (`#${TARGET_ISSUE} ${SLUG}`), worktree path, and report path.
2. **Optional polling:** if the user wants to be notified when the tab finishes, the orchestrator can use `ScheduleWakeup` (e.g., 600s = 10 min) to check `${REPORT_FILE}` periodically. Stop polling once the file exists OR after a watchdog timeout (~2 hours). Never sleep aggressively — the user can always switch to the tab if they want live progress.
3. **On report appearance:** read the JSON, surface the `summary` and `pr_url` to the user, then perform main-repo-side cleanup:
   - Move task file `running/<date>_<issue#>-<slug>.md` → `shipped/` in the **main backend** if `status == "completed"` and `pr_url` is present
   - Remove `.preview-state` if reconciliation completed (the spawned `/fix` Phase 6.5 should have done this; double-check)
   - Note: main repo's `dev` may still have the local merge from `/preview`. After the PR merges, the user can reset via `git -C ${GIT_ROOT} reset --hard origin/dev` — do NOT do this automatically (other parallel agents may have local commits).

If the report contains `status: "failed"` or `status: "needs-input"`, surface the `summary` verbatim and ask the user how to proceed.

**No back-channel beyond the report file.** The spawned tab cannot inject messages into the orchestrator's session — by design. The report file is the contract.

### 0.5 Skip delegation when

- `RUN_LOCAL=true` was forced via flag or Step 0.2 redirect
- Inside a worktree already (Signal A short-circuit)
- No worktree match for the inferred issue # (Signal B fall-through)
- Multiple worktree matches AND user can't disambiguate within one prompt — abort with: "Multiple worktrees match. Re-run with `/fix <issue#>` to disambiguate."

---

## Phase 1: Pre-flight Checks

> *Making sure everything is ready before we start*

### 1.1 Authentication Check

```bash
gh auth status
```

**If not authenticated:**
- STOP and inform user: "GitHub CLI not authenticated. Run `gh auth login` first."
- Provide quick setup instructions

### 1.2 Repository Status

```bash
# Get repo info
gh repo view --json nameWithOwner,url,defaultBranchRef -q '{repo: .nameWithOwner, url: .url, default_branch: .defaultBranchRef.name}'

# Current branch
git branch --show-current

# Check for uncommitted changes
git status --porcelain

# Check if local branch has unpushed commits
git fetch origin "$CURRENT_BRANCH" 2>/dev/null
AHEAD_COUNT=$(git rev-list --count "origin/${CURRENT_BRANCH}..HEAD" 2>/dev/null || echo 0)
```

**Capture:**
- `REPO_NAME` - e.g., `mis-bghmc/bgh-katalyst`
- `REPO_URL` - e.g., `https://github.com/mis-bghmc/bgh-katalyst`
- `DEFAULT_BRANCH` - usually `main` or `master`
- `CURRENT_BRANCH` - where user is now
- `HAS_CHANGES` - boolean (uncommitted tracked/untracked files)
- `AHEAD_COUNT` - number of local commits ahead of origin

**Decision tree:**

| State | `/fix` behavior |
|-------|-----------------|
| `HAS_CHANGES=true` | Normal flow → Phase 2 (analyze, branch, PR, merge) |
| `HAS_CHANGES=false` AND `AHEAD_COUNT>0` AND on shared branch (`dev`/`staging`/`beta`/`main`) | **Direct Push Mode** (Phase 1.2.1) |
| `HAS_CHANGES=false` AND `AHEAD_COUNT>0` AND on feature branch | Push feature branch + create PR (skip to Phase 7.7) |
| `HAS_CHANGES=false` AND `AHEAD_COUNT=0` | STOP: "Nothing to commit or push." |

### 1.2.1 Direct Push Mode (shared branch, no uncommitted changes)

> *When you've already committed directly to `dev`/`staging`/`beta`/`main` (common for sprint reports, docs, quick chores) and just need to push.*

**Trigger:** On a shared branch with `AHEAD_COUNT > 0` and no uncommitted changes.

**Rationale:** The `/fix` flow (issue → feature branch → PR → merge) is theater for already-committed-to-dev work. This mode skips to what's actually needed: sync from upstream and push.

**Flow:**

1. Show summary of what will be pushed:
   ```
   ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
   📤 DIRECT PUSH MODE
   ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

   Branch: dev
   Local is 1 commit ahead of origin/dev:

     4c67e2fc docs(reports): add sprint report for 2026-04-06 → 2026-04-12

   No uncommitted changes. Ready to push.
   ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
   ```

2. **Optional upstream sync** (stash-aware, parallel-agent safe):
   ```bash
   # Only if upstream remote exists and tracks this branch
   git fetch upstream "$CURRENT_BRANCH" 2>/dev/null
   UPSTREAM_AHEAD=$(git rev-list --count "HEAD..upstream/${CURRENT_BRANCH}" 2>/dev/null || echo 0)

   if [ "$UPSTREAM_AHEAD" -gt 0 ]; then
     echo "Upstream has $UPSTREAM_AHEAD new commit(s). Syncing first..."
     git merge "upstream/${CURRENT_BRANCH}" --no-edit
   fi
   ```

3. **Push:**
   ```bash
   git push origin "$CURRENT_BRANCH"
   ```

4. **Final summary:**
   ```
   ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
   ✅ /fix COMPLETE (direct push)
   ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

   Branch:  dev
   Pushed:  1 commit to origin/dev
   Commit:  4c67e2fc docs(reports): add sprint report for 2026-04-06 → 2026-04-12

   📌 NEXT: /stage dev staging  (when ready for QA)
   ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
   ```

**Confirmation:** Normal mode prompts once before push; `--auto` / `AUTO_MODE` pushes without prompting.

**Skipped in direct push mode:**
- Issue creation (commit already exists; issue # was handled when the commit was authored, or docs commits don't need one)
- Branch creation (already on target branch)
- Worktree creation (no isolation needed — nothing else is in flight)
- PR creation (no intermediate branch to PR from)
- Merge-to-dev (already on dev)

**Flag interactions:**
- `--no-dev` is ignored (nothing to stop — there's no PR here)
- `--auto` auto-confirms the push
- `--thorough` / `--test` / `--review` still run if code files are in the unpushed commits (skip for docs-only)

### 1.3 Issue-First Check (RECOMMENDED)

> **Verify the issue-first workflow was followed** before proceeding to analysis.
> This catches missing issues early, before work is committed without traceability.

**Detection logic (in priority order):**

1. **`--issue N` flag** → issue exists, skip check
2. **In worktree** → parse issue # from branch name (e.g., `feature/368-slug` → #368)
3. **Branch name has issue #** → parse from current branch
4. **Conversation context** → check if an issue was mentioned/created this session

**If no issue detected:**

```
⚠️  ISSUE-FIRST CHECK
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

No GitHub issue detected for this work.

The issue-first workflow ensures every commit is traceable:
  Issue # → task file → branch → session title → PR

  [I] Create issue now (recommended)
      └─ Runs /issue logic, then continues /fix with --issue #
  [s] Skip — proceed without an issue
      └─ Commit won't reference an issue, reduced traceability

Your choice [I]: _
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

**If [I] selected:** Create the issue following `/issue` style guide (see Phase 6.1), then continue with the new issue number.

**Auto-mode:** When `AUTO_MODE=true`, auto-creates the issue without prompting.

### 1.4 Session Title Check

> **Verify the terminal/session title matches the issue** for multi-pane identification.

After the issue number is resolved (from 1.3, `--issue`, or branch detection):

```bash
# Check if terminal title contains the issue number
# (heuristic: session was renamed if /title or /rename was used)
```

**If session title doesn't match:**

```
📌 SESSION TITLE
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

  Issue: #368 task-cleanup-issue-first-workflow
  Session title appears unset or mismatched.

  [R] Rename session now (recommended)
      └─ Sets title to "#368 task-cleanup-issue-first-workflow"
  [s] Skip — keep current title

Your choice [R]: _
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

**If [R] selected:**
```bash
/rename #${ISSUE_NUM} ${SLUG}
echo -ne "\033]0;#${ISSUE_NUM} ${SLUG}\007"
```

**Auto-mode / In worktree:** Auto-rename without prompting (worktree agents can't interact anyway, and auto-mode users want zero friction).

**Skip conditions (don't show this check):**
- Session was already renamed this conversation (detected from prior `/rename` or `/title` call)
- Running inside a worktree (sub-agents don't have terminal panes to rename)

---

## Phase 2: Analyze Changes & Identify Best Fix Candidate

> *Understanding what changed, why it matters, and whether it's actually a complete fix*

### 2.1 Gather All Changes

```bash
# Staged changes (priority)
git diff --cached --stat
git diff --cached --name-only
git diff --cached

# Unstaged changes
git diff --stat
git diff --name-only
git diff

# Untracked files
git ls-files --others --exclude-standard

# Recent commits for version context
git log --oneline -5
```

### 2.1.1 Backend Changes Detection (Library Sync)

```bash
# Check if any backend files are modified
BACKEND_FILES=$(git diff --cached --name-only | grep -E "^(app/|database/)" || true)

if [ -n "$BACKEND_FILES" ]; then
  HAS_BACKEND_CHANGES=true
  echo "⚠️  BACKEND CHANGES DETECTED"
  echo "   The following files require library sync:"
  echo "$BACKEND_FILES" | sed 's/^/   - /'
  echo ""
  echo "   📋 Remember to update: .kris/docs/BACKEND_CHANGES.md"
fi
```

**When backend changes are detected:**
1. Flag the commit with `[LIBRARY SYNC]` in the output
2. After successful commit, remind user to update `BACKEND_CHANGES.md`
3. Optionally offer to open the file for editing

**Backend file patterns:**
- `app/Modules/*` - Module code (models, services, controllers)
- `app/Http/*` - HTTP layer
- `database/migrations/*` - Schema changes
- `database/seeders/*` - Seed data

### 2.2 Identify Best Fix Candidate (Default Behavior)

> *When no context is provided, intelligently determine what should be committed*

**The command should analyze changes to find the "best candidate" - a coherent set of changes that:**
1. Actually fixes or implements something complete
2. Won't break production if deployed
3. Makes logical sense as a single unit of work

**Analysis Process:**

1. **Read the diffs deeply** - Understand what each change does, not just what files changed
2. **Identify the problem being solved:**
   - What was broken/missing before these changes?
   - What behavior is being corrected or added?
   - Is there a clear "before vs after" improvement?
3. **Check for completeness:**
   - Are all necessary files included? (e.g., if a service changed, is the controller updated too?)
   - Are there orphaned changes that don't connect to anything?
   - Would this fix work standalone, or does it depend on uncommitted changes?
4. **Assess risk:**
   - Does this change critical paths (auth, payments, data integrity)?
   - Are there potential side effects?
   - Is the scope minimal and focused?

**Best Candidate Scoring:**

| Signal | Score | Meaning |
|--------|-------|---------|
| Changes form logical unit | +3 | Files are related, changes make sense together |
| Clear problem → solution | +3 | Can articulate what was wrong and how it's fixed |
| All dependencies included | +2 | No missing pieces that would break the fix |
| Minimal scope | +2 | Focused changes, not sprawling |
| Tests pass/updated | +2 | Confidence the fix works |
| Low risk area | +1 | Not touching critical systems |
| Orphaned/unrelated changes | -2 | Files that don't connect to the main fix |
| Incomplete fix | -3 | Missing necessary changes |
| Breaking changes detected | -3 | Could cause production issues |

**Example Analysis Output:**

```
📊 ANALYZING CHANGES FOR FIX CANDIDATES...
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Found 12 changed files. Analyzing relationships...
Grouped into 5 potential fix candidates.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
🏆 RANKED FIX CANDIDATES (best to least confident)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

[1] 🥇 SESSION TIMEOUT FIX (recommended)
    ─────────────────────────────────────────────────
    Type: 🐛 Bug Fix | Confidence: 94% | Risk: 🟡 Medium

    Problem: Session timeout was hardcoded to 30 minutes,
             ignoring SESSION_LIFETIME environment variable.

    Solution: AuthService now reads from config('session.lifetime')

    Files (3):
      ✓ app/Modules/Auth/Services/AuthService.php
      ✓ config/session.php
      ✓ tests/Feature/Auth/SessionTimeoutTest.php

    Why recommended: Complete fix, has tests, isolated scope
    ─────────────────────────────────────────────────

[2] 🥈 USER AVATAR FEATURE
    ─────────────────────────────────────────────────
    Type: 🚀 Feature | Confidence: 87% | Risk: 🟡 Medium

    Problem: Users had no way to upload profile pictures.

    Solution: Added avatar upload with image processing.

    Files (4):
      ✓ app/Modules/User/Models/User.php
      ✓ app/Modules/User/Services/AvatarService.php
      ✓ database/migrations/2026_02_03_add_avatar.php
      ✓ resources/js/components/Avatar.vue

    Why not #1: Includes migration (higher deployment risk)
    ─────────────────────────────────────────────────

[3] 🥉 PAYEE VALIDATION FIX
    ─────────────────────────────────────────────────
    Type: 🐛 Bug Fix | Confidence: 82% | Risk: 🟢 Low

    Problem: Payee bank account validation was too permissive.

    Solution: Added stricter validation rules.

    Files (2):
      ✓ app/Modules/Finance/Payee/Services/PayeeService.php
      ✓ app/Modules/Finance/Payee/Http/Requests/StorePayeeRequest.php

    Why not higher: Missing test coverage for new validation
    ─────────────────────────────────────────────────

[4] FRONTEND STYLING UPDATES
    ─────────────────────────────────────────────────
    Type: 🎨 Style | Confidence: 78% | Risk: 🟢 Low

    Summary: Button and form styling consistency fixes.

    Files (2):
      ✓ resources/js/components/k/vuetify/form/v1/index.ts
      ✓ resources/css/app.css

    Why not higher: Style-only, low impact
    ─────────────────────────────────────────────────

[5] CONFIG UPDATES
    ─────────────────────────────────────────────────
    Type: 📝 Chore | Confidence: 65% | Risk: 🟢 Low

    Summary: Environment example and config tweaks.

    Files (1):
      ✓ .env.example

    Why not higher: Incomplete - usually accompanies other changes
    ─────────────────────────────────────────────────

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
💡 TIP: Candidates are ranked by completeness, test coverage,
   risk level, and logical coherence. Higher confidence = safer to ship.
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

### 2.3 Confirm or Redirect (CRITICAL CHECKPOINT)

**INTERACTIVE CHECKPOINT #0: Fix Target Confirmation**

**Skip conditions (any of these → skip this checkpoint):**
- `AUTO_MODE=true` (worktree detection) — auto-selects recommended option
- `--auto` flag — auto-selects recommended option
- **Prior conversation context exists** — auto-selects the best matching candidate (see "Context Resolution" section above)
- Explicit context argument provided (e.g., `/fix login timeout`) — auto-selects best match for that context

**When this checkpoint IS shown:**
- Fresh session with no prior context AND no explicit argument AND not in auto mode

> *Show up to 5 candidates so user has full visibility of options*

```
🎯 FIX TARGET CONFIRMATION
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

I found 5 potential fix candidates from your changes.
Here's my recommendation:

  🥇 #1: "Session timeout fix" (94% confidence)

  Type: 🐛 Bug Fix
  Files: AuthService.php, config/session.php, SessionTimeoutTest.php
  Why: Complete fix with tests, isolated scope, low risk

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Which fix would you like to process?

  [1] Session timeout fix (recommended)
      └─ 94% confidence, has tests, isolated
  [2] User avatar feature
      └─ 87% confidence, includes migration
  [3] Payee validation fix
      └─ 82% confidence, missing tests
  [4] Frontend styling updates
      └─ 78% confidence, style-only
  [5] Config updates
      └─ 65% confidence, may be incomplete

  [d] Describe a different target manually
  [c] Custom file selection
  [?] Explain the ranking in detail

Your choice [1]: _
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

**If user selects [d] - Describe different target:**

```
📝 DESCRIBE YOUR TARGET
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

What would you like to fix/commit instead?

Examples:
  • "the avatar upload feature"
  • "user profile validation bug"
  • "all frontend changes"
  • "just the migration files"

Your target: _
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

Then re-analyze with user's description as the context filter.

**If user selects [c] - Custom file selection:**

```
📁 CUSTOM FILE SELECTION
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

All changed files:

  [ ] app/Modules/Auth/Services/AuthService.php
  [ ] config/session.php
  [ ] tests/Feature/Auth/SessionTimeoutTest.php
  [ ] app/Modules/User/Models/User.php
  [ ] app/Modules/User/Services/AvatarService.php
  [ ] database/migrations/2026_02_03_add_avatar.php
  [ ] resources/js/components/Avatar.vue
  [ ] app/Modules/Finance/Payee/Services/PayeeService.php
  [ ] .env.example
  ... (showing 9 of 12 files)

Enter file numbers to include (e.g., "1,2,3" or "1-3,7"):
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

**If user selects [?] - Explain the ranking:**

```
💡 WHY CANDIDATES ARE RANKED THIS WAY
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

#1 SESSION TIMEOUT FIX (94% confidence)
   ✅ Coherence: 3 files work together as a unit
   ✅ Completeness: All necessary changes present
   ✅ Has tests: SessionTimeoutTest.php updated
   ✅ Clear problem→solution: Hardcoded → config-based
   ✅ Isolated: Doesn't affect other modules
   🟡 Medium risk: Touches auth (but scoped change)

#2 USER AVATAR FEATURE (87% confidence)
   ✅ Coherence: 4 files form complete feature
   ✅ Completeness: Model, service, migration, UI
   ⚠️ No tests: Missing test coverage
   ⚠️ Has migration: Higher deployment risk
   ✅ Clear feature: Adds new capability

#3 PAYEE VALIDATION FIX (82% confidence)
   ✅ Coherence: Service + request work together
   ⚠️ Missing tests: No validation tests added
   ✅ Low risk: Validation-only change
   ⚠️ Partial: May need controller updates too

#4 FRONTEND STYLING (78% confidence)
   ✅ Low risk: Style-only changes
   ⚠️ Low impact: Not a functional fix
   ✅ Complete: Standalone style updates

#5 CONFIG UPDATES (65% confidence)
   ⚠️ Likely incomplete: .env.example usually
      accompanies code changes
   ⚠️ Orphaned: No related code changes detected

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Ranking factors: completeness > test coverage > risk > coherence
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

### 2.4 Group Related Changes

Analyze the changes and group them by logical relationship:

**Grouping criteria:**
- Same module/directory (e.g., all `app/Modules/Finance/` changes)
- Same feature area (e.g., all authentication-related files)
- Same type of change (e.g., all migration files)
- Shared context from the diff content

**For each group, identify:**
- Files involved
- Type of change (bug fix, feature, refactor, etc.)
- Brief description of what changed
- Impact level (how significant is this change?)

### 2.3 Apply Context Filter (if provided)

If user provided context after `/fix`:
1. Score each change group by relevance to the context
2. Prioritize highly-relevant groups
3. Flag low-relevance groups as "unrelated - consider separate branch"

### 2.4 Classification Logic

**Bug Fix indicators** (will use `bugfix/` branch, Bug.yml template):
- Correcting wrong behavior → "It was doing X, now it does Y correctly"
- Error/exception handling → "Prevented crash when..."
- Fixing broken functionality → "X wasn't working, now it does"
- Keywords in diff: `fix`, `bug`, `error`, `broken`, `incorrect`, `crash`, `issue`, `wrong`, `fail`, `null`, `undefined`, `missing`
- Modifying existing code to correct logic
- Test failure fixes

**Feature indicators** (will use `feature/` branch, Feature.yml template):
- New functionality → "Users can now do X"
- New files/components → "Added new X component"
- Capability additions → "X now supports Y"
- Keywords: `add`, `new`, `implement`, `create`, `introduce`, `enhance`, `support`, `enable`
- New API endpoints, UI components, configuration options

**Other types** (will prompt user to choose bug or feature):
- `refactor` - Restructuring without behavior change
- `perf` - Performance improvements
- `docs` - Documentation changes
- `style` - Formatting only
- `test` - Test additions/fixes
- `chore` - Build, dependencies, tooling

### 2.5 Present Recommendations

**INTERACTIVE CHECKPOINT #1: Change Analysis**

**Auto-mode:** When `AUTO_MODE=true`, auto-selects the recommended option [Y] and skips this prompt.

Display to user:

```
📊 CHANGE ANALYSIS
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Context: {user's context or "All changes"}

Found {N} change group(s):

┌─────────────────────────────────────────────────────
│ GROUP 1: {recommended name}
│ Type: 🐛 Bug Fix (recommended)
│ Files:
│   • app/Modules/Auth/Services/AuthService.php
│   • app/Modules/Auth/Http/Controllers/LoginController.php
│ Summary: Fixed login timeout not respecting session config
│ Severity: Medium (recommended)
└─────────────────────────────────────────────────────

┌─────────────────────────────────────────────────────
│ GROUP 2: {recommended name}
│ Type: 🚀 Feature (recommended)
│ Files:
│   • app/Modules/User/Models/User.php
│   • database/migrations/2026_02_03_000001_add_avatar.php
│ Summary: Added user avatar upload capability
│ Size: Small (recommended)
└─────────────────────────────────────────────────────

⚠️  RECOMMENDATION: Split into 2 separate branches/issues
    (Bug fix and feature should be reviewed independently)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

**Ask user (always show recommendation):**

```
How would you like to proceed?

  [Y] Accept these groupings (recommended)
  [n] No, modify the groupings
  [c] Combine all into one commit

Your choice [Y]: _
```

If multiple groups exist:

```
Process groups separately or together?

  [S] Separately - one branch/issue per group (recommended)
      └─ Best practice: keeps changes atomic and reviewable
  [t] Together - combine into single branch/issue
      └─ Use when changes are tightly coupled

Your choice [S]: _
```

```
Which group to process first?

  [1] 🐛 Session timeout fix (recommended)
      └─ Highest confidence, smallest scope, lowest risk
  [2] 🚀 User avatar feature
  [3] 📝 Config updates

Your choice [1]: _
```

---

## Phase 3: Code Validation

See `.kris/templates/fix/validation.md`. Read it before executing this phase. The agent runs the parallel validation suite (syntax + lint + format + typecheck + tests + build) and surfaces Interactive Checkpoint #2 per the steps in that file.

---

## Phase 4: Fetch Issue Templates (MANDATORY)

> *Getting the right template from your organization - MUST adhere to template structure*

### 4.1 Fetch Organization Templates

**CRITICAL:** Always fetch and parse organization templates. Issue content MUST match the template structure.

```bash
# Get org name
ORG=$(gh repo view --json owner -q '.owner.login')

# Fetch all available templates
gh api "/repos/${ORG}/.github/contents/.github/ISSUE_TEMPLATE" 2>/dev/null

# Fetch Bug template
gh api "/repos/${ORG}/.github/contents/.github/ISSUE_TEMPLATE/Bug.yml" \
  --jq '.content' 2>/dev/null | base64 -d

# Fetch Feature template
gh api "/repos/${ORG}/.github/contents/.github/ISSUE_TEMPLATE/Feature.yml" \
  --jq '.content' 2>/dev/null | base64 -d

# Fetch Tech/Maintenance template
gh api "/repos/${ORG}/.github/contents/.github/ISSUE_TEMPLATE/technical_maintenance.yml" \
  --jq '.content' 2>/dev/null | base64 -d
```

### 4.2 Parse Template Structure

**IMPORTANT:** Parse the YAML template to identify:
1. All `textarea` fields and their `id` values
2. Which fields have `validations.required: true`
3. `dropdown` fields and their `options`
4. Field labels and descriptions for context

**Bug.yml required fields:**
- `description` (required) - What went wrong?
- `steps` (required) - How to reproduce
- `expected` - What should happen
- `actual` - What actually happened
- `screenshot` - Visual evidence
- `severity` - Low/Medium/High/Critical

**Feature.yml required fields:**
- `problem` (required) - What problem does this solve?
- `proposal` (required) - How should it work?
- `impact` - Who benefits?
- `criteria` (required) - How do we know it's done? **← ACCEPTANCE CRITERIA**
- `size` - Small/Medium/Large/Epic

**technical_maintenance.yml required fields:**
- `context` (required) - Why is this technical work needed?
- `changes` (required) - List the technical changes
- `impact` - What improves as a result?
- `risks` - Any risks, limitations, or follow-ups?
- `criteria` (required) - Conditions for completion **← ACCEPTANCE CRITERIA**
- `size` - Small/Medium/Large

### 4.3 Template Adherence Rules

1. **All required fields MUST be populated** - Never skip required fields
2. **Use exact section headers** - Match the template's field labels
3. **Dropdown values must match exactly** - Use "Medium" not "medium" if template specifies
4. **Acceptance Criteria is REQUIRED** for Feature and Tech issues
5. **Follow the Style Guide** - See "Issue Style Guide" section above

### 4.4 Fallback Templates

If org templates unavailable, use sensible defaults that still follow the style guide:

**Bug Default:**
```markdown
### Description
{auto-generated from diff analysis}

### Steps to Reproduce
1. {inferred or "See related code changes"}

### Expected Behavior
{inferred from fix}

### Actual Behavior
{inferred from what was broken}

### Severity
{recommended based on impact}
```

**Feature Default:**
```markdown
### Problem / Use Case
{auto-generated from context}

### Proposed Solution
{auto-generated from implementation}

### Impact
{inferred from scope}

### Acceptance Criteria
- [ ] {generated from changes}
```

---

## Phase 5: Interactive Issue Creation

> *Creating a well-documented issue with your approval*
> *If `--issue <#>` is provided, this phase is skipped and the existing issue is used.*

### 5.0 Check for Existing Issue (--issue flag)

If `--issue <#>` was provided:

```bash
# Fetch existing issue
gh issue view $ISSUE_NUM --json number,title,body,labels,state

# Validate issue exists and is open
if [ "$STATE" != "OPEN" ]; then
  echo "⚠️ Issue #$ISSUE_NUM is not open. Continue anyway? [y/N]"
fi

# Extract info for branch naming
ISSUE_TITLE=$(gh issue view $ISSUE_NUM --json title -q '.title')
ISSUE_TYPE=$(detect_type_from_title "$ISSUE_TITLE")  # [Bug], [Feature], etc.

# Update project status to "In Progress"
gh api graphql -f query='...' # Update status field

# Update start date if not set
gh api graphql -f query='...' # Set start date to today
```

**Output when using existing issue:**
```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
📋 USING EXISTING ISSUE
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Issue: #97 [Bug]: Login session timeout ignoring configuration
Status: Open
Labels: bug, priority:P2

Project updates:
  Status:     Backlog → In Progress
  Start Date: (not set) → 2026-02-24

Skipping issue creation, proceeding to branch...
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

Then skip to Phase 6 (Branch Creation).

### 5.1 Generate Issue Content

Based on the change analysis, auto-populate all fields. **MUST follow template structure and style guide.**

> **Before generating from diff:** if this branch came from `/wt`, a session brief already exists with hand-curated acceptance criteria. Prefer it over regenerating.
>
> ```bash
> # Derive session id from branch (format: {type}/{issue#}-{slug})
> BRANCH_SLUG=$(echo "$BRANCH_NAME" | sed -E "s|^[^/]+/${ISSUE_NUM}-||")
> SESSION_BRIEF="${GIT_ROOT}/.kris/wt-sessions/${ISSUE_NUM}-${BRANCH_SLUG}/brief.md"
>
> if [ -f "$SESSION_BRIEF" ]; then
>   # Pull Acceptance Criteria and Proposed Solution straight from the brief.
>   # Saves tokens (no regeneration from diff) and keeps commit/PR/issue text
>   # consistent with what the /wt executor was aiming for.
>   BRIEF_CRITERIA=$(awk '/^## Acceptance Criteria/{flag=1;next} /^## /{flag=0} flag' "$SESSION_BRIEF")
>   BRIEF_SOLUTION=$(awk '/^## Proposed Solution/{flag=1;next} /^## /{flag=0} flag' "$SESSION_BRIEF")
>   # Fall back to diff-derived generation only for fields the brief doesn't cover
>   # (Files listing, Commit hash, Severity/Size chosen by /fix's analysis).
> fi
> ```

The fallback issue body shapes used when the org templates are unavailable live in dedicated files — agent reads the matching one before generating issue content:

- **Bug** — see `.kris/templates/fix/issue-bug.md` for the fallback template body.
- **Feature** — see `.kris/templates/fix/issue-feature.md` for the fallback template body.
- **Tech / Maintenance** — see `.kris/templates/fix/issue-tech.md` for the fallback template body.

The "Section Headers" table in the Issue Style Guide above is the at-a-glance index of required fields per type — the linked template files contain the full body shape, field notes, title convention, and label set.

### 5.2 Present Issue Preview

**INTERACTIVE CHECKPOINT #3: Issue Preview**

**Auto-mode:** When `AUTO_MODE=true`, auto-selects the recommended option [Y] and skips this prompt.

```
📝 ISSUE PREVIEW
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Title: [Bug]: Login session timeout ignoring configuration value

── Metadata ─────────────────────────────────────────
Type:    🐛 Bug (recommended)
Labels:  bug, needs-triage
Project: System Development and Enhancements (default)

── Description ──────────────────────────────────────
The login session was timing out after 30 minutes regardless of
the SESSION_LIFETIME value in .env. The AuthService was using a
hardcoded value instead of reading from config.

**Affected files:**
- `app/Modules/Auth/Services/AuthService.php` - Fixed timeout config read
- `config/session.php` - Added missing lifetime accessor

── Steps to Reproduce ───────────────────────────────
1. Set SESSION_LIFETIME=120 in .env
2. Login to the application
3. Wait 30+ minutes without activity
4. Observe premature session expiration

── Expected Behavior ────────────────────────────────
Session should last 120 minutes as configured.

── Actual Behavior ──────────────────────────────────
Session expired after 30 minutes (hardcoded default).

── Severity ─────────────────────────────────────────
Medium (recommended)
  └─ Affects user experience but has workaround (re-login)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

**Ask user (always show recommendation):**

```
Does this issue look correct?

  [Y] Yes, create this issue (recommended)
  [e] Edit - let me modify some fields
  [n] No, start over with different content

Your choice [Y]: _
```

If editing or if type/severity/size needs confirmation:

```
Select issue type:

  [B] Bug - Something is broken or not working correctly (recommended)
      └─ Based on: Fixing incorrect timeout behavior
  [F] Feature - New functionality or capability
  [T] Task - Refactor, maintenance, documentation

Your choice [B]: _
```

```
Select severity level (for bugs):

  [L] Low - Minor inconvenience, easy workaround
  [M] Medium - Affects functionality, workaround exists (recommended)
      └─ Based on: Auth-related but isolated change
  [H] High - Significantly impacts users, no easy workaround
  [C] Critical - System unusable, security risk, data loss

Your choice [M]: _
```

For features, select size:

```
Select feature size:

  [S] Small - Few files, minimal risk (recommended)
      └─ Based on: 3 files, self-contained changes
  [M] Medium - Multiple components, moderate complexity
  [L] Large - Major feature, many files affected
  [E] Epic - Significant architectural change

Your choice [S]: _
```

---

## Phase 6: Worktree Creation (IMMEDIATELY AFTER CONFIRMATION)

> *Creating isolated worktree FIRST - before any file operations*
>
> **CRITICAL: This phase happens IMMEDIATELY after Phase 2 confirmation, BEFORE validation.**
> **The main directory NEVER changes branch. All work happens in the worktree.**

### 6.0 Why Worktrees Are Non-Negotiable

**Without worktrees, parallel agents cause file scattering:**
```
❌ BAD: Branch switching in main directory
   Agent A: git checkout feature/42    ← Main directory now on feature branch
   Agent B: writes file.ts             ← Goes to feature branch unexpectedly!
   Agent A: git checkout dev            ← Agent B's file orphaned or lost
```

**With worktrees, isolation is guaranteed:**
```
✅ GOOD: Isolated worktree
   Agent A: creates .worktrees/fix-42/  ← Separate directory
   Agent A: works in worktree only      ← Main directory untouched
   Agent B: writes file.ts              ← Goes to main directory (dev)
   Agent A: commits in worktree         ← No collision possible
```

**Worktree structure:**
```
bgh-katalyst/
├── backend/                          # Main worktree (ALWAYS on dev)
│   └── ... (parallel agents safe)
└── .worktrees/
    └── fix-{issue#}-{slug}/          # Isolated fix worktree
        └── backend/                  # Full repo checkout on feature branch
```

### 6.1 Create Issue First (Delegate to `/issue` Logic)

> **Follow the `/issue` skill's style guide, templates, and project integration.**
> This ensures consistent issues regardless of whether they're created via `/issue`, `/fix`, `/worktree`, or `/hotfix`.
> See `/issue` command for: title conventions, section templates, project field detection, size/date estimation.

```bash
# Create the issue following /issue style guide:
# 1. Title: [Bug]: / [Feature]: / [Tech]: prefix, sentence case, <80 chars
# 2. Body: Use org templates (Bug.yml, Feature.yml, technical_maintenance.yml)
# 3. Labels: bug/enhancement/tech,maintenance
# 4. Project: "System Development and Enhancements" with all fields
# 5. Assignee: ravstrigiformes

ISSUE_URL=$(gh issue create \
  --title "$ISSUE_TITLE" \
  --body "$ISSUE_BODY" \
  --label "$LABELS" \
  --project "System Development and Enhancements" \
  --type "$ISSUE_TYPE")

# Extract issue number — cascades into: branch, worktree dir, task file, session title
ISSUE_NUM=$(echo "$ISSUE_URL" | grep -oE '[0-9]+$')
```

**Issue Metadata Fields (set automatically):**

| Field | How it's set | Value |
|-------|--------------|-------|
| **Title** | `--title` | `[Bug]: ...` or `[Feature]: ...` (per `/issue` style guide) |
| **Labels** | `--label` | `bug`, `enhancement`, `needs-triage`, etc. |
| **Type** | `--type` | Intelligent (see below) |
| **Project** | `--project` | "System Development and Enhancements" (default) |
| **Assignees** | Not auto-set | User can add manually |
| **Milestone** | Not auto-set | User can add manually |

**Intelligent Type Detection:**

| Detected Change Pattern | Issue Type |
|------------------------|------------|
| Fixing broken/incorrect behavior | `Bug` |
| Correcting errors, exceptions | `Bug` |
| New functionality, new files | `Feature` |
| Adding capabilities | `Feature` |
| Code restructuring (no behavior change) | `Task` |
| Performance improvements | `Task` |
| Documentation updates | `Task` |
| Security fixes | `Bug` (+ `security` label) |

**Type Selection Logic:**
```
IF keywords match [fix, bug, error, broken, crash, wrong, fail, null, undefined]
   → Type = "Bug"
IF keywords match [add, new, implement, create, introduce, enhance, feature]
   → Type = "Feature"
IF keywords match [refactor, restructure, reorganize, clean, optimize]
   → Type = "Task"
ELSE
   → Type = "Task" (safe default)
```

### 6.2 Create Worktree & Branch

**Branch naming convention (industry standard):**
```
{type}/{issue-number}-{kebab-case-description}
```

Examples:
- `bugfix/42-fix-login-session-timeout` → PR to `dev`
- `feature/43-add-user-avatar-upload` → PR to `dev`
- `hotfix/44-critical-auth-bypass` → PR to `main` (then backport)

**Hotfix Flow (Critical Severity):**
Hotfixes bypass `dev` and `staging` for urgent production fixes:
1. `hotfix/*` branch created from `main`
2. PR targets `main` directly
3. After merge, backport to `staging` and `dev`:
   ```bash
   git checkout staging && git merge origin/main && git push
   git checkout dev && git merge origin/staging && git push
   ```

```bash
# Determine branch type
if [ "$SEVERITY" = "Critical" ]; then
  BRANCH_TYPE="hotfix"
elif [ "$ISSUE_TYPE" = "bug" ]; then
  BRANCH_TYPE="bugfix"
else
  BRANCH_TYPE="feature"
fi

# Generate slug from title (max 50 chars)
SLUG=$(echo "$TITLE" | tr '[:upper:]' '[:lower:]' | tr ' ' '-' | tr -cd 'a-z0-9-' | cut -c1-50)

# Create branch name
BRANCH_NAME="${BRANCH_TYPE}/${ISSUE_NUM}-${SLUG}"

# Create worktree directory name
WORKTREE_DIR=".worktrees/fix-${ISSUE_NUM}-${SLUG}"

# Ensure .worktrees directory exists
mkdir -p .worktrees

# Determine base branch (hotfixes branch from main, others from dev)
if [ "$BRANCH_TYPE" = "hotfix" ]; then
  BASE_BRANCH="origin/main"
else
  BASE_BRANCH="origin/dev"
fi

# Create worktree with new branch from appropriate base
git worktree add "$WORKTREE_DIR" -b "$BRANCH_NAME" "$BASE_BRANCH"

```

### 6.3 Copy Changed Files to Worktree

```bash
# The worktree starts clean (matching dev branch)
# Copy ONLY the specific files for this fix from main working directory

# For each file in the fix group:
for file in {file1} {file2} {file3}; do
  # Copy from main working directory to worktree
  cp "backend/$file" "$WORKTREE_DIR/$file"
done

# Stage the copied files
git -C "$WORKTREE_DIR" add {file1} {file2} {file3}
```

**Why copy instead of checkout?**
- Main directory keeps ALL your uncommitted work untouched
- Worktree only gets the specific files for this fix
- No risk of losing changes or mixing fixes

### 6.4 Handle Dependencies (if needed)

```bash
# Install isolated dependencies — do NOT symlink to the main repo
composer install --no-dev --no-interaction --quiet
npm ci --silent
```

**IMPORTANT:** Never symlink or junction `vendor/` or `node_modules/` to the main repo. This causes the main repo's dependencies to be corrupted or deleted during worktree cleanup. Each worktree must have its own isolated copy.

**INTERACTIVE CHECKPOINT #4: Worktree Created**

**Auto-mode:** When `AUTO_MODE=true`, auto-selects the recommended option [Y] and skips this prompt.

```
🌿 WORKTREE CREATED
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Issue: #42 (created)
Branch: bugfix/42-fix-login-session-timeout
Worktree: .worktrees/fix-42-fix-login-session-timeout/

Staged files (in worktree):
  ✓ app/Modules/Auth/Services/AuthService.php
  ✓ config/session.php

Main directory unchanged:
  • All uncommitted changes preserved
  • No stashing required

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

**Ask user (always show recommendation):**

```
Worktree created and files staged. Ready to commit?

  [Y] Yes, proceed to commit (recommended)
      └─ Files look correct, ready to go
  [r] Review - show me the staged diff first
  [m] Modify - change which files are staged
  [n] No, abort this operation

Your choice [Y]: _
```

---

## Phase 6.5: Auto-Reconcile Post-Preview Dev Commits

See `.kris/context/stash-aware-sync.md` for the canonical stash-aware sync pattern. Apply **Variant B — Post-Preview Reconcile**: source = main repo's `dev` (post-preview commits), target = current feature branch in this worktree.

This phase runs automatically when applicable (`.preview-state` exists in the worktree root) — no flag needed. When `/preview` wasn't used, this phase is a no-op.

---

## Phase 6.7: Pre-commit Kanban Move (CRITICAL)

> *Move task/prompt files to `shipped/` BEFORE the commit so the rename is part of the commit's diff (not stranded as an uncommitted change after push).*

**Why pre-commit, not post-commit:** If the move runs after `git commit`/`git push`, the rename sits as an uncommitted local change in the worktree. The PR doesn't reflect the lifecycle change, and when the PR merges to `dev`, the main repo's kanban stays out of sync until manually moved. Doing it pre-commit means the move is part of the merge — kanban state stays in lock-step with branch state automatically.

Phase 9.1.1's same logic remains as a safety net for cases where this phase didn't fire (issue # resolved late, task/prompt created mid-flow, etc.).

### 6.7.1 Skip Conditions

```bash
# ISSUE_NUM is resolved upstream (Phase 1/5/6). Skip silently if missing.
if [ -z "$ISSUE_NUM" ]; then
  echo "ℹ️  No issue number resolved — skipping kanban move"
  # continue to Phase 7
fi
```

### 6.7.2 Move Task File (`running/` or `pending/` → `shipped/`)

```bash
GIT_ROOT=$(git rev-parse --show-toplevel)
TASKS_DIR="${GIT_ROOT}/.kris/tasks"
TASK_PATTERN="*_${ISSUE_NUM}-*.md"

mkdir -p "${TASKS_DIR}/shipped"

# Check running first (expected location after /do), then pending (if /do was skipped)
TASK_FILE=$(find "${TASKS_DIR}/running" -maxdepth 1 -name "$TASK_PATTERN" 2>/dev/null | head -1)
if [ -z "$TASK_FILE" ]; then
  TASK_FILE=$(find "${TASKS_DIR}/pending" -maxdepth 1 -name "$TASK_PATTERN" 2>/dev/null | head -1)
fi

if [ -n "$TASK_FILE" ] && [ -f "$TASK_FILE" ]; then
  TASK_BASENAME=$(basename "$TASK_FILE")
  git mv "$TASK_FILE" "${TASKS_DIR}/shipped/${TASK_BASENAME}"
  echo "📋 Task moved (pre-commit): → shipped/${TASK_BASENAME}"
elif ls "${TASKS_DIR}/shipped/"$TASK_PATTERN 1>/dev/null 2>&1; then
  echo "📋 Task already in shipped/ — skipping move"
else
  echo "ℹ️  No task file found for #${ISSUE_NUM} (task tracking not used)"
fi
```

```bash
PROMPTS_DIR="${GIT_ROOT}/.kris/prompts"
PROMPT_PATTERN="*_${ISSUE_NUM}-*.md"

mkdir -p "${PROMPTS_DIR}/shipped"

# Search recursively (prompts may live in subdirs like pending/cds/, pending/dots/)
PROMPT_FILES=$(find "${PROMPTS_DIR}/pending" -name "$PROMPT_PATTERN" 2>/dev/null)

if [ -n "$PROMPT_FILES" ]; then
  while IFS= read -r PROMPT_FILE; do
    # Preserve subdirectory: pending/cds/foo.md → shipped/cds/foo.md
    REL_PATH="${PROMPT_FILE#${PROMPTS_DIR}/pending/}"
    DEST="${PROMPTS_DIR}/shipped/${REL_PATH}"
    mkdir -p "$(dirname "$DEST")"
    git mv "$PROMPT_FILE" "$DEST"
    echo "📋 Prompt moved (pre-commit): → shipped/${REL_PATH}"
  done <<< "$PROMPT_FILES"
else
  echo "ℹ️  No pending prompts found for #${ISSUE_NUM}"
fi
```

### 6.7.4 Result

After this phase, any kanban moves are auto-staged by `git mv`. The Phase 7 commit will include them in its diff. The Phase 7.3 commit preview will show the renames so the user can confirm before committing.

---

## Phase 6.8: Session-Scoped Backend Sweep (CRITICAL)

**Why this runs AFTER 6.7 and BEFORE 7:** Phase 6.7 moves the prompt(s) into `prompts/shipped/`; the sweep checks BOTH `pending/` and `shipped/`, so the ordering is no longer load-bearing — but staging must complete before Phase 7's commit captures the index.

### 6.8.1 Skip condition + MAIN_GIT_ROOT resolution + sweep (single guarded block)

> *This phase is a single skip-aware block. If `ISSUE_NUM` is empty OR `MAIN_GIT_ROOT` doesn't resolve to a valid main-repo backend layout, the entire sweep is skipped via a `SBS_SKIP=true` flag. Subsequent steps gate on that flag — never fall through.*

```bash
SBS_SKIP=false
if [ -z "$ISSUE_NUM" ]; then
  echo "ℹ️  No issue # in scope — skipping backend sweep"
  SBS_SKIP=true
fi

if [ "$SBS_SKIP" != "true" ]; then
  GIT_COMMON_DIR=$(git rev-parse --git-common-dir)
  MAIN_GIT_ROOT="${GIT_COMMON_DIR%/.git}"
  # Round-trip through `cd && pwd` to absolutize relative results like `../.git`.
  MAIN_GIT_ROOT="$(cd "$MAIN_GIT_ROOT" 2>/dev/null && pwd)" || MAIN_GIT_ROOT=""
  if [ -z "$MAIN_GIT_ROOT" ] || [ ! -d "${MAIN_GIT_ROOT}/.kris" ]; then
    echo "🚫 Backend sweep: MAIN_GIT_ROOT resolved to '${MAIN_GIT_ROOT}' but .kris/ not found"
    echo "   Skipping sweep to avoid silent miss. Inspect manually."
    SBS_SKIP=true
  fi
fi
```

### 6.8.2 Run the sweep (stage mode) — gated on SBS_SKIP

```bash
if [ "$SBS_SKIP" != "true" ]; then
  RESULT_FILE="/tmp/sbs-${ISSUE_NUM}-$$"
  session_backend_sweep \
    --issue "$ISSUE_NUM" \
    --mode stage \
    --repo-root "$MAIN_GIT_ROOT" \
    --result-file "$RESULT_FILE"
  SWEEP_RC=$?

  source "$RESULT_FILE"
  rm -f "$RESULT_FILE"
fi
```

### 6.8.3 Handle sweep result — also gated on SBS_SKIP

```bash
if [ "$SBS_SKIP" != "true" ]; then
  if [ "$SWEEP_RC" -eq 1 ] || [ "${#SBS_CONFLICTS[@]}" -gt 0 ]; then
    # Conflicts — two sessions claim the same file. Halt /fix; user must resolve.
    echo "🚫 /fix halted: backend sweep found conflicting session claims."
    echo "   Resolve by editing one session's brief.md / prompt to drop the overlap,"
    echo "   then re-run /fix."
    exit 1
  fi

  echo "Backend sweep: staged ${#SBS_TO_STAGE[@]} file(s); ${#SBS_ORPHANS[@]} orphan(s) left for parallel agents"
fi
```

> The sweep already prints the `📦 Bundling N backend file(s) ...` line and the orphan summary when it runs — Phase 7's commit preview will then show the staged backend files in the diff.

---

## Phase 7: Commit & Push

> *Committing with proper linking and pushing to remote*

### 7.1 Determine Version

See `.kris/templates/fix/commit-message.md` "Version Determination" — auto-increments build/patch/minor based on `{type}`, syncs to `package.json` + `.env`. **Major bumps (W) are never auto-applied** — they require explicit user approval per the CLAUDE.md major-version rule.

### 7.2 Craft Commit Message

See `.kris/templates/fix/commit-message.md` for the canonical commit message shape (subject + body + footer, including the version footer that pairs with Phase 7.1's auto-increment).

### 7.3 Present Commit Preview

**INTERACTIVE CHECKPOINT #5: Commit Preview** — see `.kris/templates/fix/commit-message.md` "Commit Preview Checkpoint" for the preview UI shape and the user-facing "Accept / Edit / Cancel" prompt. Auto-mode (`AUTO_MODE=true`) accepts the recommended message verbatim.

### 7.4 Prepare Changelog Entry (BEFORE Commit)

> **CRITICAL:** The changelog entry MUST be created BEFORE the commit so it's included in the same commit.
> Commit hash and PR # are not yet known — use placeholders that get back-filled after commit/push.

#### 7.4.1 Determine Changelog File

See `.kris/templates/fix/changelog-entry.md` "Determine the Weekly Changelog File" — Monday-to-Sunday ISO 8601 window, file naming, and template creation.

#### 7.4.2 Entry Format

See `.kris/templates/fix/changelog-entry.md` for the entry shape, variable list (`{commit_hash}`, `{issue_ref}`, `{area}`, `{files}`, `{summary}`), placeholder back-fill rules, type-mapping table, and area-detection table.

#### 7.4.3 Update Summary Statistics

After adding the entry, update the summary table at the top:

1. **Total Commits**: Increment by 1
2. **Issues Resolved**: Increment by 1 (if issue linked)
3. **Features/Bug Fixes/Other**: Increment appropriate counter
4. **By Area**: Increment appropriate area counter

#### 7.4.4 Stage Changelog

See `.kris/templates/fix/changelog-entry.md` "Stage the Changelog Alongside the Code Changes".

### 7.5 Execute Commit

```bash
git commit -m "$COMMIT_TITLE" -m "$COMMIT_BODY"
COMMIT_HASH=$(git rev-parse --short HEAD)
```

### 7.6 Back-fill Commit Hash in Changelog

See `.kris/templates/fix/changelog-entry.md` "Back-fill the Commit Hash After `git commit`" — amends the commit BEFORE push, so no shared-history rewrite.

### 7.7 Push to Remote

```bash
# Push with upstream tracking
git push -u origin "$BRANCH_NAME"
```

### 7.8 Create PR

```bash
# Create PR targeting dev (or main for hotfixes)
if [ "$BRANCH_TYPE" = "hotfix" ]; then
  BASE_BRANCH="main"
else
  BASE_BRANCH="dev"
fi

PR_URL=$(gh pr create --base "$BASE_BRANCH" \
  --title "$COMMIT_TITLE" \
  --body "$PR_BODY")

PR_NUM=$(echo "$PR_URL" | grep -oE '[0-9]+$')
```

### 7.9 Back-fill PR # in Changelog

See `.kris/templates/fix/changelog-entry.md` "Back-fill the PR Number After `gh pr create`" — uses `--force-with-lease` on the fresh feature branch with no collaborators (safety rationale documented in the template).

#### 7.9.1 Example Entry (After Back-fill)

See `.kris/templates/fix/changelog-entry.md` "Example Entry (After Back-fill)" for the populated entry shape.

---

## Phase 8: Labels & Tags

> *Adding metadata for tracking and releases*

### 8.1 Update Issue Labels

Add status labels to track progress:

```bash
# Add work-in-progress label
gh issue edit "$ISSUE_NUM" --add-label "in-progress"

# Add priority label based on severity (for bugs)
if [ "$SEVERITY" = "Critical" ]; then
  gh issue edit "$ISSUE_NUM" --add-label "priority:critical"
elif [ "$SEVERITY" = "High" ]; then
  gh issue edit "$ISSUE_NUM" --add-label "priority:high"
fi

# Add size label (for features)
if [ -n "$SIZE" ]; then
  gh issue edit "$ISSUE_NUM" --add-label "size:${SIZE,,}"
fi
```

### 8.2 Create Git Tag (Optional)

**INTERACTIVE CHECKPOINT #6: Tagging**

**Auto-mode:** When `AUTO_MODE=true`, auto-selects the recommended option [Y] and skips this prompt.

```
🏷️  GIT TAG (Optional)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Current version: v1.2.3 (from last tag)
Recommended next: v1.2.4 (patch increment for bug fix)

Tag this commit?
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

**Ask user (always show recommendation):**

```
Would you like to create a git tag for this commit?

  [N] No, skip tagging (recommended)
      └─ Best practice: tag after PR is merged to main
  [y] Yes, tag as v1.2.4 (auto-incremented)
  [c] Custom - enter a specific version

Your choice [N]: _
```

If user selects [y] or [c]:

```bash
# Get recommended version
LAST_TAG=$(git describe --tags --abbrev=0 2>/dev/null || echo "v0.0.0")
# Parse and increment appropriately
NEXT_TAG=$(increment_version "$LAST_TAG" "$CHANGE_TYPE")

# Create annotated tag
git tag -a "$NEXT_TAG" -m "Release $NEXT_TAG

$COMMIT_TITLE

Issue: #$ISSUE_NUM
Branch: $BRANCH_NAME"

# Push tag
git push origin "$NEXT_TAG"
```

---

## Phase 9: Cleanup & Summary

> *Creating completion marker and showing what was accomplished*

### 9.1 Create Completion Marker (When in Worktree)

**If `/fix` is running inside a worktree** (created by `/worktree`), create a marker file for cleanup tracking:

```bash
# Detect if in worktree
WORKTREE_INFO=$(git rev-parse --git-common-dir 2>/dev/null)
if [[ "$WORKTREE_INFO" != ".git" && -d "$WORKTREE_INFO" ]]; then
  IN_WORKTREE=true
  WORKTREE_PATH=$(pwd)
fi

# Create completion marker
if [ "$IN_WORKTREE" = true ]; then
  cat > .worktree-complete << EOF
{
  "issue": $ISSUE_NUM,
  "pr": $PR_NUM,
  "branch": "$BRANCH_NAME",
  "commit": "$COMMIT_HASH",
  "version": "$NEXT_VERSION",
  "completed": "$(date -Iseconds)",
  "title": "$ISSUE_TITLE"
}
EOF
fi
```

**Marker file:** `.worktree-complete`
- Created in worktree root
- Contains issue #, PR #, branch, commit, timestamp
- Used by `/worktree-cleanup` to identify completed work

### 9.1.1 Move Task to Shipped (Safety Net)

> **Note:** The canonical task/prompt move now happens in **Phase 6.7** (pre-commit) so the rename is part of the commit's diff. This phase is a **safety net** for cases where Phase 6.7 was skipped — e.g., issue # was resolved late, task file was created mid-flow, or the file was somehow missed. It's idempotent: a no-op if the task is already in `shipped/`.

```bash
# Find task file (format: YYYY-MM-DD_<issue#>-<slug>.md)
# Task files live in .kris/tasks/, NOT at the git root
GIT_ROOT=$(git rev-parse --show-toplevel)
TASKS_DIR="${GIT_ROOT}/.kris/tasks"
TASK_PATTERN="*_${ISSUE_NUM}-*.md"

# Ensure shipped directory exists
mkdir -p "${TASKS_DIR}/shipped"

# Check running first (expected location after /do)
TASK_FILE=$(find "${TASKS_DIR}/running" -maxdepth 1 -name "$TASK_PATTERN" 2>/dev/null | head -1)

if [ -n "$TASK_FILE" ] && [ -f "$TASK_FILE" ]; then
  TASK_BASENAME=$(basename "$TASK_FILE")
  mv "$TASK_FILE" "${TASKS_DIR}/shipped/"
  echo "📋 Task moved (safety net): running → shipped"
  echo "   File: $TASK_BASENAME"
  echo "   ⚠️  Phase 6.7 should have caught this — investigate why it didn't fire"
else
  # Fallback: check pending (if /do was skipped)
  TASK_FILE=$(find "${TASKS_DIR}/pending" -maxdepth 1 -name "$TASK_PATTERN" 2>/dev/null | head -1)

  if [ -n "$TASK_FILE" ] && [ -f "$TASK_FILE" ]; then
    TASK_BASENAME=$(basename "$TASK_FILE")
    mv "$TASK_FILE" "${TASKS_DIR}/shipped/"
    echo "📋 Task moved (safety net): pending → shipped (skipped running)"
    echo "   File: $TASK_BASENAME"
    echo "   ⚠️  Phase 6.7 should have caught this — investigate why it didn't fire"
  elif ls "${TASKS_DIR}/shipped/"$TASK_PATTERN 1>/dev/null 2>&1; then
    echo "📋 Task already in shipped/ (moved by Phase 6.7)"
  else
    echo "ℹ️  No task file found for issue #${ISSUE_NUM}"
    echo "   Task tracking was not used for this fix"
  fi
fi

# Same safety-net pass for prompts (Phase 6.7 is canonical move site)
PROMPTS_DIR="${GIT_ROOT}/.kris/prompts"
LEFTOVER_PROMPTS=$(find "${PROMPTS_DIR}/pending" -name "*_${ISSUE_NUM}-*.md" 2>/dev/null)
if [ -n "$LEFTOVER_PROMPTS" ]; then
  while IFS= read -r PROMPT_FILE; do
    REL_PATH="${PROMPT_FILE#${PROMPTS_DIR}/pending/}"
    DEST="${PROMPTS_DIR}/shipped/${REL_PATH}"
    mkdir -p "$(dirname "$DEST")"
    mv "$PROMPT_FILE" "$DEST"
    echo "📋 Prompt moved (safety net): → shipped/${REL_PATH}"
    echo "   ⚠️  Phase 6.7 should have caught this — investigate why it didn't fire"
  done <<< "$LEFTOVER_PROMPTS"
fi
```

This completes the task lifecycle:
```
pending → running → shipped
         (/do)      (/fix Phase 6.7, with Phase 9.1.1 as safety net)
```

### 9.1.5 Cross-Worktree Blocker Scan (Phase 9.5)

> *After successful PR creation, scan peer worktrees' `blockers.md` files for references to the just-shipped issue. If matches are found, surface them to the user with an option to write resolution lines into the matching files. This is the propagation half of the self-heal layer (the read half lives in `/wt` Access mode A.2.8).*

**Skip conditions:**
- `--no-pr` mode (PR not created → nothing to propagate)
- `IN_WORKTREE = false` and no `ISSUE_NUM` available
- `${GIT_ROOT}/.worktrees/` does not exist (no peers)

```bash
# Phase 9.5 — runs only on successful PR creation. Skip on dry-run / failure.
SHIPPED_ISSUE="${ISSUE_NUM}"
SHIPPED_BRANCH_SLUG=$(echo "$BRANCH_NAME" | sed -E 's|^[a-z]+/[0-9]+-||')

GIT_ROOT=$(git rev-parse --show-toplevel)
WORKTREES_DIR="${GIT_ROOT}/.worktrees"
# Resolve the just-shipped worktree's root (cwd may be ${WT_ROOT}/backend, not WT_ROOT itself)
WORKTREE_ROOT=$(git -C "$(pwd)" rev-parse --show-toplevel 2>/dev/null)

# Build the match regex. Always include `#<num>\b` and `/<num>-` (branch path token).
# Only include the branch slug if it's non-empty, distinct from the full branch name
# (i.e. sed actually stripped a `<type>/<num>-` prefix), and at least 4 chars long
# — empty/short slugs would expand the alternation to match every line.
MATCH_REGEX="#${SHIPPED_ISSUE}\b|/${SHIPPED_ISSUE}-"
if [ -n "$SHIPPED_BRANCH_SLUG" ] \
   && [ "$SHIPPED_BRANCH_SLUG" != "$BRANCH_NAME" ] \
   && [ ${#SHIPPED_BRANCH_SLUG} -ge 4 ]; then
  MATCH_REGEX="${MATCH_REGEX}|${SHIPPED_BRANCH_SLUG}"
fi

MATCHES_FILE=$(mktemp)

if [ -d "$WORKTREES_DIR" ] && [ -n "$SHIPPED_ISSUE" ]; then
  # git worktree list --porcelain returns the FULL list regardless of cwd
  # (worktree metadata is centralized in .git/worktrees/). Filter to those
  # whose path actually exists (handles force-deleted dirs that didn't get pruned).
  while IFS= read -r WT_PATH; do
    [ -z "$WT_PATH" ] && continue
    [ ! -d "$WT_PATH" ] && continue
    [ "$WT_PATH" = "$GIT_ROOT" ] && continue       # skip main repo
    [ "$WT_PATH" = "$WORKTREE_ROOT" ] && continue  # skip self (the just-shipped worktree)

    BLOCKERS_FILE="${WT_PATH}/.kris/wt-state/blockers.md"
    [ ! -f "$BLOCKERS_FILE" ] && continue

    # Match using the guarded regex built above. Exclude already-resolved
    # (struck-through) lines that start with "- ~~".
    HITS=$(grep -nE "$MATCH_REGEX" "$BLOCKERS_FILE" 2>/dev/null | grep -vE "^[0-9]+:- ~~" || true)

    if [ -n "$HITS" ]; then
      echo "WT: ${WT_PATH}" >> "$MATCHES_FILE"
      echo "$HITS" >> "$MATCHES_FILE"
      echo "---" >> "$MATCHES_FILE"
    fi
  done < <(git worktree list --porcelain | awk '/^worktree /{print $2}')
fi

if [ -s "$MATCHES_FILE" ]; then
  echo "🔓 Resolved blockers detected in other worktrees:"
  cat "$MATCHES_FILE"
  echo
  read -r -p "Notify? [Y] writes resolution lines into matching blockers.md files | [n] skip: " NOTIFY_REPLY
  NOTIFY_REPLY="${NOTIFY_REPLY:-Y}"

  if [[ "$NOTIFY_REPLY" =~ ^[Yy] ]]; then
    NOW_HM=$(date +%H:%M)
    # Re-walk matches and append resolution line per worktree
    while IFS= read -r LINE; do
      case "$LINE" in
        "WT: "*)
          WT_TARGET="${LINE#WT: }"
          BLOCKERS_TARGET="${WT_TARGET}/.kris/wt-state/blockers.md"
          if [ -f "$BLOCKERS_TARGET" ]; then
            printf -- "- [%s] **Resolved (by feature-%s ship):** PR #%s merged; this worktree's reference to #%s is now satisfied.\n" \
              "$NOW_HM" "$SHIPPED_ISSUE" "$PR_NUM" "$SHIPPED_ISSUE" >> "$BLOCKERS_TARGET"
            echo "  → wrote resolution line to ${BLOCKERS_TARGET}"
          fi
          ;;
      esac
    done < "$MATCHES_FILE"
  fi
fi

rm -f "$MATCHES_FILE"
```

**Edge cases handled:**
- `git worktree list --porcelain` may include pruned entries → filtered by `[ -d "$WT_PATH" ]`
- Issue # collisions (`#547` vs `#5470`) → word-boundary regex `#${SHIPPED_ISSUE}\b`
- Already-resolved blockers (struck-through with `- ~~`) → excluded from hits
- The just-shipped worktree itself → excluded by `[ "$WT_PATH" = "$WORKTREE_ROOT" ]` (where `WORKTREE_ROOT` is `git -C "$(pwd)" rev-parse --show-toplevel`, since cwd is typically `${WORKTREE_ROOT}/backend`)
- Empty / short / unchanged `SHIPPED_BRANCH_SLUG` → guarded out of the match regex (otherwise an empty alternation would match every line)
- Main repo (which has no `wt-state/`) → excluded by `[ "$WT_PATH" = "$GIT_ROOT" ]`

### 9.2 Final Summary

#### 9.2.0 Incremental-work detection (gate for retrospective suggestion)

Before rendering the final summary, compute whether this fix qualifies as **incremental work** on an existing topic. If so, the summary appends a one-line `/retrospect` suggestion to "NEXT STEPS". Two signals; either alone is sufficient (combined raises confidence):

```bash
# Signal 1 — branch-suffix pattern (deliberate signal per
# feedback_task-suffix-letter-derived.md — e.g., feature/427c-pagination-bugfix
# is iteration C of topic 427's pagination work).
BRANCH_SUFFIX_MATCH=$(echo "$BRANCH_NAME" | grep -oE "/${ISSUE_NUM}[a-z]-" | head -1)

# Signal 2 — git-history overlap heuristic. Multiple commits touching the same
# files in the last 30 days suggests this is iterating on a recently-worked
# surface, even without an explicit suffix letter.
RECENT_FILE_OVERLAP=0
if [ -n "$CHANGED_FILES" ]; then
  RECENT_FILE_OVERLAP=$(git log --since="30 days ago" --name-only --pretty=format: -- $CHANGED_FILES 2>/dev/null | sort -u | grep -c .)
fi

IS_INCREMENTAL=false
if [ -n "$BRANCH_SUFFIX_MATCH" ]; then
  IS_INCREMENTAL=true   # Strong signal
elif [ "$RECENT_FILE_OVERLAP" -gt 10 ]; then
  IS_INCREMENTAL=true   # Heuristic — file churn on the same area
fi

# Also surface the topic-name guess for the suggestion text. Strip the
# <type>/<issue#><letter?>- prefix; what remains is the topic slug.
TOPIC_GUESS=$(echo "$BRANCH_NAME" | sed -E 's|^[a-z]+/[0-9]+[a-z]?-||')
```

**False-positive note:** Signal 2 (git-history overlap) is a heuristic and CAN fire spuriously (e.g., unrelated changes to a hot file). The retrospective suggestion is **opt-in** — the user reads it and either runs `/retrospect` or ignores it. No harm if occasionally over-suggested; the cost of the false positive is one ignored line in the report.

**When running inside a worktree (from /worktree):**

```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
✅ /fix COMPLETE
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

📍 Worktree: feature-224-doc-register-dialog-dismiss
   (Completion marker created)

📋 Issue: #224
   [Feature]: Enhance document registration dialog

🏷️  Version: v0.3.1.0

💾 Committed
   abc1234: feat: enhance document registration dialog (#224)

🚀 Pushed
   origin/feature/224-doc-register-dialog-dismiss

📝 PR Created
   #225: feat: enhance document registration dialog
   https://github.com/mis-bghmc/bgh-katalyst/pull/225

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

📌 CLEANUP (after PR is merged):

   Option 1 - One command (copy-paste after exiting Claude):

      exit && cd /c/krism/projects/web/bgh02_staging/bgh-katalyst/backend && git worktree remove ../.worktrees/feature-224-doc-register-dialog-dismiss

   Option 2 - Use cleanup skill (from main repo):

      /worktree-cleanup 224

   Option 3 - Clean all merged worktrees:

      /worktree-cleanup

{IF IS_INCREMENTAL=true:}
📝 RETROSPECTIVE:

   This looks like incremental work on topic `${TOPIC_GUESS}` —
   ${BRANCH_SUFFIX_MATCH:+suffix-derived branch ($BRANCH_NAME); }${RECENT_FILE_OVERLAP:+${RECENT_FILE_OVERLAP} overlapping files in the last 30 days.}

   Consider running `/retrospect` to update the topic retrospective:

      /retrospect

   (auto-targets the existing retrospective if one exists, otherwise scaffolds a new one)
{END IF}

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

**When running from main repo (normal /fix flow):**

```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
✅ /fix COMPLETE
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

📋 Issue Created
   #42: [Bug]: Login session timeout ignoring configuration value
   https://github.com/mis-bghmc/bgh-katalyst/issues/42

🌿 Worktree Created
   Path: .worktrees/fix-42-fix-login-session-timeout/
   Branch: bugfix/42-fix-login-session-timeout

🏷️  Version: v0.3.1.0

💾 Committed
   abc1234: fix: correct session timeout to use config value (#42)

🚀 Pushed
   origin/bugfix/42-fix-login-session-timeout

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

📌 NEXT STEPS:
   1. Create PR: gh pr create --base dev
   2. After merge, clean up: /worktree-cleanup 42
{IF IS_INCREMENTAL=true:}
   3. 📝 Update retrospective for `${TOPIC_GUESS}` — run `/retrospect`
      (auto-targets the existing retrospective; appends issue row + iteration log)
{END IF}

📂 WORKTREE STATUS:
   Path: .worktrees/fix-42-fix-login-session-timeout/
   Main directory: unchanged (all other work preserved)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

### 9.3 List Active Worktrees

```bash
# See all active worktrees
git worktree list

# Example output:
# /c/projects/bgh-katalyst              d2d357e5 [dev]
# /c/projects/bgh-katalyst/.worktrees/fix-42-session  abc1234 [bugfix/42-fix-session]
```

### 9.4 Multiple Groups Handling

Each fix group gets its own worktree - no interference between fixes:

```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
📊 PROGRESS: 1 of 2 groups processed

Completed:
  ✅ Group 1: bugfix/42-fix-login-session-timeout
     Worktree: .worktrees/fix-42-fix-login-session-timeout/

Remaining:
  ⏳ Group 2: User avatar upload feature (2 files)
     Run `/fix` again → creates new worktree

Active Worktrees:
  git worktree list  # See all active worktrees

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

**Parallel fixes are now possible:**
```bash
# Each /fix creates its own worktree
.worktrees/
├── fix-42-session-timeout/     # First fix
├── fix-43-avatar-feature/      # Second fix (parallel)
└── fix-44-validation-bug/      # Third fix (parallel)

# Work on any fix without affecting others
git -C .worktrees/fix-43-avatar-feature status
```

---

## Execution Checklist (CRITICAL)

> **Changelog AND kanban moves are now part of the commit** — no more "left behind" local changes.

**Before reporting success, `/fix` MUST have completed ALL of these:**

| Step | Where | What |
|------|-------|------|
| Reconciliation (if applicable) | Phase 6.5 | Cherry-pick post-preview dev commits to feature branch |
| Kanban move (task + prompt) | Phase 6.7 | `git mv` task `running/→shipped/` and prompt `pending/→shipped/` BEFORE commit |
| Version determination | Phase 7.1 | Parse latest version from `origin/dev` commit messages, auto-increment |
| Version in commit | Phase 7.2 | Include `NEXT_VERSION` on last line of commit message body |
| Changelog entry | Phase 7.4 | Create entry BEFORE commit, include in same commit |
| Changelog back-fill | Phase 7.6 + 7.9 | Amend commit hash (pre-push) and PR # (post-push) |
| Version in marker | Phase 9.1 | Include `version` field in `.worktree-complete` JSON |
| Changelog summary | Phase 7.4.3 | Update summary table counters (commits, issues, features/bugs, area) |
| Kanban safety net | Phase 9.1.1 | Idempotent re-check; warns if Phase 6.7 was skipped |
| Kill dev servers | Phase 9.2 | Terminate any `composer run dev` / `npm run dev` / `php artisan serve` / queue workers started during the session before reporting success |

**If ANY of these are missing, the fix is INCOMPLETE.** `/worktree-cleanup` will catch and remediate.

### Phase 9.2 — Kill background dev servers (Windows file-lock guard)

Before reporting `/fix` complete, `TaskStop` (or shell-kill) any background tasks this session started for LAN preview / local testing inside the worktree:

- `composer run dev` (concurrent npm + php-server + queue)
- `npm run dev` / `vite`
- `php artisan serve`
- `php artisan queue:work` / `queue:listen`

**Why:** on Windows, node/php processes hold file handles inside the worktree directory. If they're still running when the user (or automation) later invokes `/worktree-cleanup`, `git worktree remove` fails with "directory not empty" or "file is locked." Killing them here is much cheaper than recovering from a stuck cleanup.

If running inside `/wt`'s spawned `/fix` tab, ensure this kills processes by **PID inside the worktree**, not by name globally — the orchestrator's main repo may have its own dev server running.

**How to apply:** scan `TaskList` for active background tasks owned by this session; call `TaskStop` on each. Then on Windows, defensively check `netstat -ano | findstr :<dev-port>` and kill orphan PIDs whose `Get-Process` path is under the worktree.

This phase NEVER fails the fix — best-effort cleanup. If a process refuses to die, log a warning in the spawn-report and let `/wc` retry.

### Changelog Lifecycle in a Fix

```
Phase 7.4  → Create entry with placeholders: Commit=(pending), PR=(pending)
Phase 7.5  → Commit (changelog included in the commit)
Phase 7.6  → Amend: replace Commit=(pending) with actual hash (pre-push, safe)
Phase 7.7  → Push
Phase 7.8  → Create PR
Phase 7.9  → Amend: replace PR=(pending) with actual PR # (force-push, safe on fresh branch)
```

**Why this ordering matters:**
- Changelog is ALWAYS committed (never left as local change)
- `git pull` after merge will include the changelog entry
- No manual back-patching needed
- `/worktree-cleanup` safety net catches any remaining `(pending)` placeholders

---

## Quick Reference: Interactive Checkpoints

> **Design Principle:** Every selection ALWAYS has a recommended option. Users can spam Enter and get sensible results.
>
> **With `--auto` flag:** All checkpoints are skipped, recommended options are used automatically. Only validation failures will stop the process.

| # | Checkpoint | Recommended | Alternatives | `--auto` behavior |
|---|------------|-------------|--------------|-------------------|
| 0 | Fix Target | **[1] Top candidate** | [2-5], [d] Describe, [c] Custom | Auto-selects #1. Also skipped when conversation context or explicit argument exists |
| 1 | Change Groups | **[Y] Accept** | [n] Modify, [c] Combine | Auto-accepts |
| 1b | Process Mode | **[S] Separately** | [t] Together | Separately |
| 2 | Validation | **[Y] Proceed** / **[F] Fix** | [s] Skip, [a] Abort | Spawns Test Fix Agent |
| 2b | Test Failures | **[F] Fix with agent** | [s] Skip, [a] Abort | Auto-fix in parallel |
| 3 | Issue Preview | **[Y] Create** | [e] Edit, [n] Start over | Auto-creates |
| 3b | Type | **Context-based** (Bug/Feature/Task) | Manual selection | Auto-selects |
| 3c | Severity | **Context-based** (for bugs) | [L] [M] [H] [C] | Auto-selects |
| 3d | Size | **Context-based** (for features) | [S] [M] [L] [E] | Auto-selects |
| 4 | Branch Ready | **[Y] Proceed** | [r] Review, [m] Modify | Auto-proceeds |
| 5 | Commit Message | **[Y] Commit** | [e] Edit, [n] Cancel | Auto-commits |
| 6 | Git Tag | **[N] Skip** | [y] Auto, [c] Custom | Skips |
| 7 | Merge to Dev | **[Y] Merge** (default) | [n] Cancel, `--no-dev` to skip | Auto-merges |

**Default Project:** All issues are automatically added to "System Development and Enhancements" project.

**With `--issue <#>`:** Checkpoints 3, 3b, 3c, 3d are skipped (existing issue used).

**With `--review`:** Review Validation Agent checks all files before Phase 3. Blocks on FAIL, warns on WARN.

**With `--test`:** Test Fix Agent runs Pest tests, auto-fixes failures in parallel.

**With `-t` / `--thorough`:** Runs both `--review` then `--test` in sequence. Both must pass.

**With `--qa`:** QA Agent generates test checklist, added to issue body.

**Merge to dev is DEFAULT.** After PR creation, `/fix` auto-merges to dev and syncs local. Use `--no-dev` to skip (e.g., when PR needs external review).

---

## Error Handling

| Error | Resolution |
|-------|------------|
| `gh` not authenticated | Run `gh auth login` - will guide through OAuth |
| No changes detected | Inform and exit - nothing to do |
| Template fetch fails | Use built-in defaults |
| Validation fails | Show specific errors, offer fix/skip/abort |
| Issue creation fails | Show error, don't proceed with commit |
| Push fails | Suggest `git push --set-upstream`, check permissions |
| Branch exists | Offer to switch to existing or create with suffix |

---

## Configuration Notes

**GitHub CLI Authentication:**
```bash
gh auth login  # One-time setup
gh auth status # Verify
```

**Organization Templates:**
Templates should be in `{org}/.github` repository at `.github/ISSUE_TEMPLATE/`

**Supported Template Formats:**
- YAML (`.yml`) - Modern GitHub Issue Forms (preferred)
- Markdown (`.md`) - Legacy format with YAML frontmatter

**Conventional Commits Types:**
- `fix:` - Bug fixes (correlates to PATCH in semver)
- `feat:` - New features (correlates to MINOR in semver)
- `refactor:` - Code restructuring
- `perf:` - Performance improvements
- `docs:` - Documentation
- `style:` - Formatting
- `test:` - Tests
- `chore:` - Maintenance

---

## Examples

### Automatic Mode (Zero Interaction)

```
> /fix --auto

📊 Analyzing 12 changed files...
🎯 Auto-selected: "Session timeout fix" (94% confidence)
🔍 Validating...
   ✅ PHP Syntax ✅ Pint ✅ Tests (3/3)
📋 Created issue #42
🌿 Created branch bugfix/42-fix-session-timeout
💾 Committed abc1234
🚀 Pushed to origin

✅ COMPLETE | Remaining: 4 change groups
```

*Result: Zero interaction, done in seconds.*

---

### Quick Mode (Spam Enter)

```
> /fix

📊 Analyzing changes...
🎯 Found 5 candidates. #1: "Session timeout fix" (94%)

Which fix? [1/2/3/4/5/d/c/?] [1]: ↵
All checks passed! Proceed? [Y/n]: ↵
Issue preview ready. Accept? [Y/e/n]: ↵
Severity: [L/M/H/C] [M]: ↵
Branch created. Ready? [Y/r/m/n]: ↵
Commit message ready? [Y/e/n]: ↵
Create git tag? [y/N/c]: ↵

✅ /fix COMPLETE
   Issue: #42
   Branch: bugfix/42-fix-session-timeout
   Commit: abc1234
```

*Result: 7 Enter presses, full control with sensible defaults.*

---

### Targeted Auto Fix

```
/fix avatar feature --auto
```
→ Filters to avatar-related changes, processes automatically

### Targeted Manual Fix

```
/fix login timeout
```
→ Filters to login/timeout changes, interactive mode

### Feature Addition

```
/fix user avatar feature
```
→ Recognizes as feature, uses Feature.yml template, creates `feature/` branch

### After Partial Processing

```
/fix
```
→ Detects remaining uncommitted changes in main directory, creates new worktree for next fix

### Full Interactive Mode

```
/fix
```
Then select `[?]` to see detailed explanations, `[c]` for custom file selection, or pick any of the 5 candidates.

---

### Attach to Existing Issue (from /issue)

```
> /issue "session timeout bug"
✅ Created #97: [Bug]: Session timeout ignoring configuration

> # ... implement the fix ...

> /fix --issue 97 --auto

📋 Using existing issue #97
📊 Project: Backlog → In Progress
🔍 Validating...
   ✅ PHP Syntax ✅ Pint ✅ Tests (3/3)
🌿 Created branch bugfix/97-session-timeout
💾 Committed abc1234
🚀 Pushed to origin

✅ COMPLETE | Issue #97 linked
```

---

### With Test Auto-Fix

```
> /fix --test --auto

📊 Analyzing changes...
🔍 Running tests...
   ❌ AuthServiceTest::test_session_timeout FAILED

🔧 TEST FIX AGENT spawned
   Analyzing: Expected 7200, got 1800
   Root cause: AuthService.php:42 hardcoded value
   Fix applied: config('session.lifetime') * 60
   Re-running test...
   ✅ PASSED

📋 Created issue #98
🌿 Created branch bugfix/98-session-timeout
💾 Committed abc1234 (includes agent fix)
🚀 Pushed to origin

✅ COMPLETE | 1 test auto-fixed
```

---

### With QA Checklist

```
> /fix --qa

📊 Analyzing changes...
📋 QA AGENT generating test plan...

✅ Issue #99 created with QA checklist:

## QA Testing

### Test Scenarios
- [ ] Login → session active for configured duration
- [ ] Activity refreshes session timer
- [ ] Timeout → graceful redirect to login

### Edge Cases
- [ ] SESSION_LIFETIME=0
- [ ] Multiple browser tabs

🌿 Created branch bugfix/99-session-timeout
💾 Committed abc1234
🚀 Pushed to origin

✅ COMPLETE | QA checklist included
```

---

### With Thorough Mode (-t)

```
> /fix -t

📊 Analyzing changes...
🎯 Auto-selected: "Quick-add auto-select for branch modal" (92%)

🔬 REVIEW VALIDATION AGENT
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Reviewing 3 files in fix candidate...

✅ CreateBranchModal.vue
   ✓ All imports resolve
   ✓ emit('success', newBranchId) signature valid
   ✓ response?.data?.id access pattern safe

⚠️  PayeeBankAccountForm.vue
   ✓ All imports resolve
   ⚠ handleBranchCreated(newBranchId: number) — modal emits Id type
     → Non-blocking: Id is number | string, param is number
   ✓ branchId.value assignment type-compatible

✅ frontend-patterns.md
   ✓ Documentation only

Result: WARN (1 non-blocking)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Proceed? [Y/n]: ↵

🧪 Running Pest tests...
   ✅ 3 passed, 0 failed

🔍 Validating code...
   ✅ PHP Syntax ✅ Build
📋 Created issue #42
🌿 Created branch feature/42-quick-add-auto-select
💾 Committed abc1234
🚀 Pushed to origin

✅ COMPLETE (review: WARN, tests: PASS)
```

---

### Batch Processing

```
> /fix --batch --confidence=85

📊 Found 5 change groups
   Filtering by confidence >= 85%...
   Processing 3 qualifying groups...

[1/3] Session timeout fix (94%)
   ✅ #100 bugfix/100-session-timeout (abc1234)

[2/3] Payee validation (89%)
   ✅ #101 bugfix/101-payee-validation (def5678)

[3/3] User avatar feature (87%)
   ✅ #102 feature/102-user-avatar (ghi9012)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
📊 BATCH COMPLETE: 3 committed, 2 skipped (low confidence)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

---

### Default Flow (Merge to Dev)

```
> /fix --auto

📊 Analyzing changes...
🔍 Validating...
   ✅ PHP Syntax ✅ Pint ✅ TypeScript ✅ Build
📋 Created issue #347
🌿 Branch: feature/347-escape-close-dialogs
💾 Committed abc1234
🚀 Pushed to origin
📝 PR #352 created → dev
🔀 PR #352 merged to dev
📥 Local dev synced

✅ COMPLETE | Merged to dev
   Next: /stage dev staging
```

---

### Complete Workflow: /issue → /fix

```bash
# Day 1: Planning
/issue "implement fund adjustment effect tracking"
# → Created #103: [Feature]: Implement fund adjustment effect tracking
# → Project: Backlog | P2 | M | bgh-katalyst:cds | Due: 2026-03-03

# Day 2: Implementation
# ... write the code ...

# Day 2: Commit with tests and QA
/fix --issue 103 --test --qa --auto
# → Using issue #103
# → Project: In Progress | Start: 2026-02-25
# → Tests passed (or auto-fixed)
# → QA checklist generated
# → Committed and pushed

# Day 3: Create PR to dev
gh pr create --base dev
# → PR references #103, includes QA checklist
# → After merge to dev, promote: dev → staging (QA) → main (production)
```
