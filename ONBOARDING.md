# pingy — Onboarding Guide

> **Audience:** A fresh AI coding agent (or human dev) picking up this project. Read top-to-bottom on first encounter, then jump to the relevant section as needed.

---

## 0. What pingy is, in one paragraph

A Windows network diagnostic overlay for a ~2000-employee organisation. Each domain-joined Windows endpoint runs an always-on-top widget showing connection health (gateway/DHCP/key servers); the widget reports aggregated telemetry to a Laravel backend authed via Kerberos; IT sees a live status grid + a coalesced ticket queue. Employees self-diagnose "is it me or the network?" and one-click ship the last 5 minutes of diagnostics to IT. **Fully on-prem, fully air-gapped — zero public-internet dependencies at build, deploy, or runtime.**

The single source of truth for scope, architecture, and sequencing is **[`.kris/docs/architecture/pingy-v1-plan.md`](./.kris/docs/architecture/pingy-v1-plan.md)**. Read it before writing code.

---

## 1. Operating principles (non-negotiable)

1. **Air-gap first.** Every dependency must have an on-prem path: internal CA for code signing, internal HSM for key custody, internal RFC3161 TSA for timestamps, internal package mirrors (NuGet/Composer/npm), internal CI runners, SCCM/GPO for deployment. **No proposal that introduces a public-internet runtime/build/deploy dependency is acceptable.** If a dependency only ships via a public service, find a self-hostable equivalent or push back in writing.
2. **Two-process Windows client.** `Pingy.Agent` (Windows Service, LocalSystem) does all probing and HTTPS upload; `Pingy.Widget` (per-user WPF) is a thin view talking to the agent over a named pipe. Never combine them. Reasoning is in plan §3.
3. **Modular monolith on the backend.** Cross-module calls go through `app/Shared/Contracts` interfaces only — enforce with Deptrac in CI when added. No reaching into another module's models/services directly.
4. **Sync ingest writes; Horizon for async.** `/api/v1/ingest` writes to Postgres synchronously. Don't add Redis to the hot path. Resilience comes from the **client-side SQLite spool**, not from server-side queueing.
5. **Auth identity is the machine, not the user.** Under LocalSystem, SPNEGO uses the computer-account ticket. The AD username goes in the *body* as informational data and is stored hashed on the dashboard (audit-logged "reveal" action when IT needs the cleartext).
6. **Default to no comments and no docs files** unless the user explicitly asks. The plan doc and this onboarding doc are the asked-for exceptions; do not multiply them.

---

## 2. Prerequisites (verify before doing anything)

Run these. If any fail, install and re-run before proceeding.

```powershell
git --version            # >= 2.40
dotnet --version         # 8.0.x  (.NET 8 SDK)
dotnet --list-sdks       # confirm 8.0 SDK present
php --version            # 8.3.x
composer --version       # 2.x
node --version           # 20.x or 24.x
docker --version         # any recent (for local dev compose)
```

**Tooling installed and verified on this machine** (as of W1 setup):
- git 2.52, .NET 8.0.420, PHP 8.3.29, Composer 2.9.2, Node 24.12.

If you are working **off-prem during dev** (greenfield W1–W4), public package sources (nuget.org, packagist.org) are fine. From W5 onwards, dev should pin to internal mirrors to catch drift early.

---

## 3. Bootstrap from a fresh clone

```bash
# 1. Clone (use the org's internal mirror once one exists)
git clone <internal-git>/pingy.git
cd pingy

# 2. Read the locked plan — do not skip
$EDITOR .kris/docs/architecture/pingy-v1-plan.md

# 3. Backend deps
cd server
composer install
cp .env.example .env
php artisan key:generate
# When the dev compose is up (see §6), run:
php artisan migrate

# 4. Client build (Windows host required for WPF)
cd ../client
dotnet restore Pingy.sln
dotnet build Pingy.sln -c Debug

# 5. Sanity tests
cd server && ./vendor/bin/pest         # once Pest is installed (see §7)
cd ../client && dotnet test
```

---

## 4. Repo map

```
pingy/
├── client/                          # .NET 8 WPF solution
│   ├── Pingy.sln
│   ├── src/
│   │   ├── Pingy.Widget/            # WPF UI (per-user, always-on-top)
│   │   ├── Pingy.Agent/             # Windows Service (probing + upload)
│   │   ├── Pingy.Core/              # Pure domain (probe results, diagnosis FSM)
│   │   ├── Pingy.Ipc/               # Named-pipe contracts (Widget <-> Agent)
│   │   └── Pingy.Doctor/            # Support CLI: pingy-doctor.exe
│   ├── tests/
│   │   ├── Pingy.Core.Tests/        # xUnit
│   │   └── Pingy.Agent.Tests/       # xUnit
│   └── installer/                   # WiX v4 .wixproj (TBD)
├── server/                          # Laravel 11
│   ├── app/
│   │   ├── Modules/
│   │   │   ├── Auth/                # Kerberos REMOTE_USER -> User; bearer-token (v2)
│   │   │   ├── Ingest/              # /api/v1/ingest hot path
│   │   │   ├── Targets/             # subnet -> target list; admin CRUD
│   │   │   ├── Tickets/             # Report-to-IT, coalescing, audit log
│   │   │   ├── Machines/            # registration, last-seen, status
│   │   │   └── Dashboard/           # Livewire grid + triage views
│   │   └── Shared/
│   │       ├── Contracts/           # Cross-module interfaces (only allowed coupling)
│   │       └── DTOs/                # Shared transfer objects
│   ├── database/migrations/
│   ├── resources/views/dashboard/
│   ├── tests/{Feature,Unit}/        # Pest
│   └── ...                          # Standard Laravel layout
├── infra/
│   ├── nginx/                       # SPNEGO reverse-proxy config
│   ├── docker/                      # docker-compose.yml for dev (PG, Redis, MinIO, KDC)
│   ├── k6/                          # ingest_burst.js & friends
│   └── keytabs/                     # .gitignored — local Kerberos keytabs only
├── docs/adr/                        # Architecture Decision Records
├── .kris/
│   └── docs/architecture/
│       ├── thread-based-engineering.md   # F-thread/B-thread/etc. patterns
│       └── pingy-v1-plan.md              # ⭐ Single source of truth
├── .ci/                             # Pipeline definitions (provider-agnostic)
├── .claude/
│   ├── skills/                      # Reusable conventions (see §10)
│   └── agents/                      # Project-bound agent personas
├── README.md
├── ONBOARDING.md                    # ⭐ You are here
├── VERSION                          # 0.1.0-dev
└── .gitignore, .gitattributes
```

---

## 5. Locked architecture decisions (the 18 things you don't get to re-debate)

These came out of a `/grill-me` session + `/F-thread × 4` planning fusion. Don't re-litigate. If you think one is wrong, write an ADR in `docs/adr/` and raise it in a PR — don't quietly diverge.

| # | Decision |
|---|----------|
| 1 | ~2000 employees / multi-building / Windows fleet / AD-domain-joined |
| 2 | Visible always-on-top widget |
| 3 | .NET 8 WPF + WiX v4 MSI (not MSIX) |
| 4 | Laravel 11 + PostgreSQL 16 |
| 5 | Server-pushed target config per subnet (ETag + 304) |
| 6 | **Probe every 30s** (not 10s — revised in W1 lock-in); ICMP + TCP-connect; aggregate every 60s; **2-of-3 failure rule before red state**; traceroute on-demand only |
| 7 | Kerberos / SPNEGO via nginx + Bit4Id `spnego-http-auth-nginx-module` (Apache + `mod_auth_gssapi` is the fallback) |
| 8 | SCCM/MECM (preferred) or AD GPO software install |
| 9 | DB deadlock check is a **backend-side** probe (v2) |
| 10 | DB engines (v2): SQL Server, MySQL/MariaDB, PostgreSQL — pluggable |
| 11 | Retention: 7d raw / 90d 5-min rollups (v2) / 1y hourly (v2) |
| 12 | Widget red-state: expand panel + one-click "Report to IT" with last 5-min blob |
| 13 | Internal helpdesk only (tickets in pingy's own dashboard) |
| 14 | **Hashed AD username on dashboard by default** + audit-logged "reveal" action |
| 15 | v1 = widget + ingest + dashboard. Defer traceroute, DB probe, rollups, multi-driver helpdesk. |
| 16 | **Air-gapped operational profile** (zero public-internet deps) |
| 17 | **TCP-connect probe in v1** alongside ICMP (catches ICMP-rate-limited paths) |
| 18 | **Dual-auth data model designed in W1** (Kerberos primary v1; bearer-token path schema-ready for v2 off-LAN clients) |

**Standing constraints** (every plan must respect these):
- No external HSM-as-a-service. On-prem HSM (YubiHSM 2 / Thales / SafeNet) or hardware token attached to the Windows CI runner.
- Code-signing cert from internal AD CS only. CDP/AIA must point to internal HTTP URLs — verify with `certutil -url <cert>` before first signing.
- Build runner has no public-internet route. NuGet/Composer/npm fetched from internal Artifactory/Nexus mirror.
- Privacy: full AD username surfacing requires HR/legal sign-off (still pending). Until then, dashboard shows hostname + building + hash; reveal is consent-by-action.

---

## 6. Local dev compose (TBD — W1 task #6)

When `infra/docker/compose.yml` exists, it should bring up:

| Service | Image | Purpose |
|---------|-------|---------|
| postgres | `postgres:16` | Primary datastore |
| redis | `redis:7` | Horizon queue backing |
| minio | `minio/minio` | S3-compatible diagnostic blob storage |
| kdc | `gcavalcante8808/krb5-server` (or build) | Local KDC for Kerberos integration tests |
| nginx-spnego | `bitnami/nginx` + custom build | SPNEGO reverse proxy in front of php-fpm |

Bootstrap script (TBD at `infra/docker/bootstrap.sh`) should:
1. Create the test KDC realm `PINGY.LOCAL`
2. Add an HTTP service principal `HTTP/pingy.pingy.local`
3. Export the keytab to `infra/keytabs/pingy.keytab` (gitignored)
4. Configure nginx to use that keytab

Until the compose exists, develop against your own Postgres + Redis (e.g., Docker Desktop, WSL).

---

## 7. Laravel ecosystem packages to install (W1, not yet done)

```bash
cd server
composer require laravel/horizon
composer require livewire/livewire
composer require --dev pestphp/pest pestphp/pest-plugin-laravel
composer require --dev larastan/larastan
composer require --dev qossmic/deptrac-shim   # to enforce module boundaries (§1.3)

php artisan horizon:install
php artisan vendor:publish --provider="Laravel\Horizon\HorizonServiceProvider"
./vendor/bin/pest --init
```

Pint comes pre-installed with Laravel 11.

After installing, configure `.env`:
```env
DB_CONNECTION=pgsql
DB_HOST=127.0.0.1
DB_PORT=5432
DB_DATABASE=pingy
DB_USERNAME=pingy
DB_PASSWORD=...
QUEUE_CONNECTION=redis
REDIS_HOST=127.0.0.1
```

---

## 8. v1 status — what's done, what's next

**Done (W1 in progress):**
- ✅ Locked plan written and persisted at `.kris/docs/architecture/pingy-v1-plan.md`
- ✅ Git initialized (branch `main`, LF normalization configured)
- ✅ Top-level folder structure scaffolded per plan §1
- ✅ `.gitignore`, `.gitattributes`, `README.md`, `VERSION` (0.1.0-dev), `ONBOARDING.md`
- ✅ .NET 8 solution scaffolded: `Pingy.sln` + 5 projects + 2 test projects, references wired
- ✅ NuGet packages installed: CommunityToolkit.Mvvm, Microsoft.Extensions.Hosting + WindowsServices, Microsoft.Data.Sqlite, Microsoft.Extensions.Logging.Abstractions
- ✅ Laravel 11.31 installed at `server/`
- ✅ Module folder structure created under `server/app/Modules/`
- ✅ All 4 standing revisions locked in (cadence 30s + 2-of-3, TCP probe, hashed username, dual-auth model)

**Windows widget — shipped releases** (pre-release single-file `.exe` builds on GitHub
Releases; client feature work runs ahead of the W1 backend tasks below):
- `v0.1.0-dev.1`–`dev.6` — portable single-file build, resizable/responsive grid, TCP
  port probing + crash diagnostics, L7 HTTP/HTTPS checks, timeout-aware charting +
  owner field, system tray, ICMP-aware status
- `v0.1.0-dev.7` — "This PC" readout (device name · LAN IP · gateway) with click-to-copy;
  app-wide themed tooltips + missing button tooltips
- `v0.1.0-dev.8` — interaction performance: removed per-tick visual-tree churn,
  batched probe updates, native OS window drag
- `v0.1.0-dev.9` — Settings panel (⚙ gear) + toolbar declutter + persisted preferences
  (`config/settings.json`); fixed a latent `Run.Text` tooltip crash
- `v0.1.0-dev.10` — live per-probe updates (a slow/timing-out target no longer stalls
  the rest) + themed Settings steppers

Full list with downloadable `.exe` per tag: GitHub Releases. Build/ship procedure is
`.kris/docs/references/portable-release-build.md`.

**Next (W1 remaining):**
- ⬜ Install Laravel ecosystem packages (§7)
- ⬜ Configure `server/.env` for Postgres + Redis
- ⬜ Stand up local dev compose (§6) — task #6
- ⬜ Draft CI workflow skeletons in `.ci/` — task #7
- ⬜ First commit (suggested message at §11)
- ⬜ Write ServiceProvider stubs for each module + register in `bootstrap/providers.php`

**W2 (per plan §10):**
- nginx + SPNEGO end-to-end with KDC test container; Auth module resolves REMOTE_USER
- `Pingy.Agent` Worker Service skeleton, named-pipe IPC, SQLite spool
- HSM/token configured on Windows CI runner; signtool PoC against unsigned dev build

---

## 9. The diagnosis FSM — get this right or kill the project

Plan §9 calls out: "the diagnosis logic is a trust lie-detector. A green dot when the user can't work destroys confidence in 24 hours." The `Pingy.Core` diagnosis state machine must default to **amber + "no network issues detected, contact IT if app is broken"** rather than confident green.

States to model in `Pingy.Core/Diagnosis/`:
- `Healthy` — gateway up + ≥ N targets up + no recent flips
- `Degraded` — partial loss but reachable
- `Gateway unreachable` — local LAN issue
- `Server down (specific)` — gateway up + named target down
- `Investigating` — first failure window, awaiting 2-of-3 confirmation
- `Unknown` — agent restart, suspend resume, network change in last 60s

Conservative wins. When in doubt, never say "green" — say "investigating" or "no issues detected, but if your app is broken, contact IT."

---

## 10. Coding standards & conventions (use these skills)

This repo ships project-bound skills under `.claude/skills/`. Apply them automatically when working in the relevant area:

| Area | Skill | Notes |
|------|-------|-------|
| Laravel module structure | `.claude/skills/laravel-architecture/` | Modular monolith, service layer, DTOs, Action classes |
| Eloquent / queries | `.claude/skills/eloquent-patterns/` | Optimization, relationships, query patterns |
| PHP language | `.claude/skills/php-coding-standards/` | PSR-12, PHP 8.2+ features, type safety |
| Pest testing | `.claude/skills/pest-testing/` | TDD with Pest; **no mocked DB** for integration tests |
| Laravel security | `.claude/skills/laravel-security/` | OWASP, auth, authz patterns |
| Schema sync | `.claude/skills/schema-sync/` | Detect drift between migrations and prod DB |
| Data sync | `.claude/skills/data-sync/` | Reference/lookup data sync between seeders and prod |

WPF/.NET conventions (no skill yet — write one if you start hitting recurring patterns):
- MVVM via `CommunityToolkit.Mvvm` source generators only
- `record` types for IPC payloads in `Pingy.Ipc`
- `IAsyncDisposable` everywhere a Service owns a resource
- xUnit for tests; one `*Tests` class per production class

---

## 11. How to ship a change

1. **Branch** from `main`: `git switch -c feat/ingest-coalescing`
2. **Read the affected plan section** before changing code. If you're about to deviate from a locked decision, stop — write an ADR in `docs/adr/NNNN-decision.md` first.
3. **Write the test before the fix** (Pest for backend, xUnit for client).
4. **Run local checks:**
   ```bash
   cd server && ./vendor/bin/pest && ./vendor/bin/pint --test && ./vendor/bin/phpstan analyse
   cd client && dotnet test && dotnet format --verify-no-changes
   ```
5. **Commit with conventional-commit style:**
   ```
   feat(ingest): coalesce same-diagnosis tickets within 60s by subnet

   Plan §6 mitigation #2. Cuts ticket-table churn during incident bursts.
   k6 ingest_burst.js shows 250 raw → 1 parent + 249 child ticket_events.
   ```
6. **Open PR.** Reference the plan section + any ADRs.

---

## 12. Common commands

```powershell
# Read the plan
code .kris\docs\architecture\pingy-v1-plan.md

# Build everything
cd client; dotnet build Pingy.sln; cd ..\server; composer install

# Run backend tests
cd server; .\vendor\bin\pest

# Run client tests
cd client; dotnet test

# Run a single Laravel module's tests
cd server; .\vendor\bin\pest --filter=Ingest

# Lint
cd server; .\vendor\bin\pint
cd client; dotnet format

# Local dev (when compose exists)
docker compose -f infra\docker\compose.yml up -d
cd server; php artisan migrate; php artisan horizon
```

---

## 12.5 Task backlog

Standalone task briefs live in **`.kris/tasks/`** — each `NNN-*.md` file is self-contained (context, scope, acceptance criteria, files, design notes). Read `.kris/tasks/README.md` first for picking convention. Use this when handing work to a fresh AI session.

## 13. Pointers to deeper context

| Question | Where to look |
|----------|---------------|
| What's the v1 scope? | Plan §1 + §15 (deferred items) |
| Why 2 processes on the client? | Plan §3 + this guide §1.2 |
| Why sync ingest writes? | Plan §4 + this guide §1.4 |
| What's the database schema? | Plan §5 |
| How do we handle incident bursts? | Plan §6 |
| How does code signing work without a public CA? | Plan §7 |
| Top 3 production risks? | Plan §9 |
| 8-week sequencing? | Plan §10 |
| F-thread / B-thread planning patterns? | `.kris/docs/architecture/thread-based-engineering.md` |

---

## 14. If you're an AI agent picking this up

You are stepping into a partially scaffolded project at the start of W1. Before you write any code:

1. **Read the plan in full** — `.kris/docs/architecture/pingy-v1-plan.md`. It's ~3200 words. Do not skim.
2. **Read this onboarding guide in full** — you've already done that if you're reading this.
3. **Check task list** — use `TaskList` (or equivalent in your harness) to see what's pending.
4. **Verify prerequisites** (§2) and the "what's done" snapshot (§8) is still accurate. If files don't match the snapshot, trust the files and update §8 in this doc.
5. **Pick one task at a time.** Mark it `in_progress`, complete it, mark it `completed`. Don't batch multiple tasks into a megacommit.
6. **Ask the user before deviating** from any locked decision (§5). Use a clarifying question, not silent divergence.
7. **Default to no comments and no new docs.** This file and the plan are the asked-for exceptions.
8. **For any "should I…?" question:** the conservative answer is usually right. Pingy is infrastructure that 2000 people will see — boring, predictable, debuggable beats clever.

When in doubt: re-read plan §9 (Top 3 production risks). Most "should I…?" questions resolve to "yes if it reduces one of those three risks; no otherwise."
