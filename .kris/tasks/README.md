# pingy — Task Backlog

This folder holds **self-contained task briefs** that any AI agent (or human dev) can pick up cold. Each `NNN-*.md` file is one unit of work with: context, scope, acceptance criteria, files involved, design notes.

## How to use

When you (the next agent) start a session:

1. **Read** `ONBOARDING.md` at the repo root (mandatory).
2. **Read** `.kris/docs/architecture/pingy-v1-plan.md` (the locked v1 plan).
3. **Pick one** task file from this folder, lowest-numbered first.
4. **Read it in full** before touching code — each brief lists its own files and acceptance criteria.
5. **Mark progress** via the harness's task tools (e.g., `TaskCreate` / `TaskUpdate`) so the user can see status.
6. **Don't deviate from the locked plan** — if a task seems wrong, write an ADR in `docs/adr/` and surface it to the user.

## Conventions

- `NNN-` prefix = sequencing hint (lower first), not a hard dependency. Each task notes its own dependencies.
- Tasks marked **`[BLOCKED]`** in their frontmatter need an upstream task done first.
- Tasks marked **`[OPTIONAL]`** are nice-to-have polish, not blocking.
- After completing a task, **rename the file** with a `_DONE-YYYY-MM-DD` suffix (e.g., `100-drag-reorder-targets_DONE-2026-05-13.md`) — keeps history visible without git diff. Add a "Status: ✅ DONE" header + brief implementation summary at the top of the file before the original brief.

## Current backlog

### W1 backend (foundational — required before any backend work)
- `010-dev-compose.md` — Postgres + Redis + MinIO + test-KDC + nginx-SPNEGO local dev stack
- `020-ci-skeletons.md` — Provider-agnostic CI pipeline definitions in `.ci/`
- `030-laravel-ecosystem-packages.md` — Install Pest, Horizon, Livewire, Larastan, Deptrac and configure
- `040-module-service-providers.md` — Stub ServiceProviders for the 6 modules and register them

### v2 widget polish — pending (client-side, can ship in any order)
- `110-per-target-enable-disable.md` — Pause/resume monitoring per target without deleting
- `120-per-target-timeout-and-interval.md` — Override global probe timeout/interval per target
- `130-target-notes-field.md` — Free-form notes per target (visible in editor + tooltip)
- `140-right-click-context-menu.md` — Quick actions on each row (copy IP, edit, delete, disable)
- `150-export-import-targets.md` — JSON file export/import for backup and sharing
- `160-uptime-stats.md` — Track uptime % and last-failure timestamp per target
- `170-settings-persistence.md` — Persist UI toggles (animations, opacity, mini, sort, window pos) across restart
- `180-keyboard-reorder-shortcuts.md` — Alt+↑/↓ reorder, F2 edit, Delete, Ctrl+N add

### v2 widget polish — done (this sprint)
- `100-drag-reorder-targets_DONE-2026-05-13.md` — Drag handles + ghost popup + insertion indicator + auto-sort-reset

### Stretch (multi-session)
- `200-tcp-connect-probe.md` — Add TCP-connect probe alongside ICMP (per plan §11 standing revision #3)
- `210-traceroute-on-demand.md` — On-demand traceroute when red, attached to ticket payload
- `220-export-as-image.md` — Screenshot/PNG export of current widget state for incident reports
- `230-custom-grab-cursor.md` — Ship .cur files for grab/grabbing hands (replace `Cursors.Hand`)
