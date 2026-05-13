# /title - Name Session & Set Terminal Title

Generate a kebab-cased session name from context and set it via `/rename`.

## Usage

```bash
/title                       # Auto-generate from context
/title my-custom-name        # Use explicit name
```

## Title Format

**Always kebab-case:** `#<issue_number> kebab-cased-title`

Without issue number: `kebab-cased-title`

Examples:
- `#357 routing-template-user-integration`
- `#224 dialog-dismiss`
- `cds-payee-validation`
- `config-rework-title-skill`

---

## Title Resolution (Priority Order)

### 1. Explicit argument
If `$ARGUMENTS` is provided, use it directly as the title.

### 2. Branch context
If on a `feature/`, `bugfix/`, or `hotfix/` branch:
```
feature/224-dialog-dismiss → #224 dialog-dismiss
hotfix/180-build-fix       → #180 build-fix
```
Parse issue number and slug from branch name.

### 3. Worktree context
If cwd contains `.worktrees/`, parse the worktree directory name:
```
feature-316-draggable-dialogs → #316 draggable-dialogs
```
Strip the type prefix (`feature-`, `bugfix-`, `hotfix-`).

### 3.5. Recent-issue context (post-merge dev sessions)
If on `dev` (or any non-feature branch) with no explicit arg, scan the conversation for a recent issue number + branch/worktree slug — e.g. `/fix` or `/worktree` activity in the last ~N turns, or the most recently shipped task file in `.kris/tasks/shipped/`. If found, use `#<issue> <kebab-slug>` (same slug as the branch/worktree, minus the type prefix).

Rationale: just-finished work is a stronger session identifier than an inferred conversation topic. A merged feature shouldn't be relabeled to a generic activity tag the moment the branch closes.

### 4. Conversation context (no issue)
Only when no issue/worktree context exists at all (pure exploration, review sessions without a ticket). Generate a short kebab-cased title from the conversation topic.

**System prefixes:** add entries here as fnl-cat modules stabilize. Examples to model on:

| Keywords | Prefix |
|----------|--------|
| booking, schedule, trip, van | `booking-` |
| user, role, admin, auth | `admin-` |
| skills, commands, config, hooks | `config-` |
| Multiple/general | (none) |

**Rules:** 2-5 words, all kebab-case, lead with the most distinguishing word.

---

## Steps

1. **Resolve title** using priority order above
2. **Output `/rename`** — `/rename` is a built-in Claude Code command. Output it as plain text so the user can execute it:
   ```
   /rename <title>
   ```
3. **Set terminal pane title:**
   ```bash
   echo -ne "\033]0;<title>\007"
   ```
4. **Confirm:** `Session: <title>`

---

## Integration

These commands call `/title` automatically — no need to run separately:

| Command | When |
|---------|------|
| `/worktree` | After worktree creation |
| `/do` | After task context loaded |
| `/fix` | After issue resolved |
| `/hotfix` | After branch creation |
| `/issue` | After issue created |
