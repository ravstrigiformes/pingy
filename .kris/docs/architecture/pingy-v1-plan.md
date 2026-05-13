# pingy v1 — Locked Implementation Plan

**Project:** pingy — org-wide Windows network diagnostic overlay
**Status:** Locked (post-grill-me + F-thread × 4 fusion)
**Date locked:** 2026-05-12
**Air-gap profile:** On-prem only — zero external dependencies at build, deploy, or runtime

---

## 0. TL;DR

A WPF always-on-top widget on every Windows endpoint (~2000 employees, multi-building, AD-domain-joined) reports gateway/target health to a Laravel backend authed via Kerberos. IT sees a live status grid + ticket queue. Employees self-diagnose "is it me or the network?" and one-click ship diagnostics to IT. Fully on-prem, fully air-gapped — internal CA, internal HSM, internal CI, internal package mirrors, internal MDM/SCCM deployment.

v2 adds: traceroute, multi-engine DB deadlock probe (SQL Server / MySQL / Postgres), retention rollups, multi-driver helpdesk integrations.

---

## 1. Locked decisions (from grill-me)

| # | Decision | Choice |
|---|----------|--------|
| 1 | Audience | Every employee (~2000); lightweight client per machine |
| 2 | Visibility | Visible always-on-top widget (color-coded status) |
| 3 | Client tech | .NET 8 WPF, self-contained signed MSI |
| 4 | Backend stack | Laravel 11 + PostgreSQL |
| 5 | Probe targets | Server-pushed config per subnet/building |
| 6 | Telemetry cadence | Probe every **30s** (ICMP + **TCP-connect** to port 443/445 where target has `port`); aggregate (p50/p95/loss%) every 60s; **2-of-3 failure rule** before red state; traceroute on-demand only |
| 7 | Auth (client→backend) | Windows Integrated Auth (Kerberos / SPNEGO via nginx) |
| 8 | Deployment mechanism | SCCM/MECM (preferred) or AD GPO software install |
| 9 | DB deadlock check | Backend-side probe (v2); widget shows result |
| 10 | DB engines supported (v2) | SQL Server, MySQL/MariaDB, PostgreSQL — pluggable |
| 11 | Retention | 7d raw / 90d 5-min rollups (v2) / 1y hourly (v2) |
| 12 | Widget red-state action | Expand panel + one-click "Report to IT" with last 5 min of diagnostics |
| 13 | Helpdesk | Internal — tickets in pingy's own dashboard |
| 14 | Identity on dashboard | **Hashed AD username by default**; cleartext revealed only via audit-logged "reveal" action that writes `kind='reveal_identity'` to `ticket_events`. Hostname + building visible by default. |
| 15 | v1 scope | Widget + Laravel ingest with Kerberos + IT dashboard. Defer traceroute, DB probe, rollups, multi-driver helpdesk. |
| 16 | Operational profile | **Fully air-gapped — no public internet dependencies** |
| 17 | Probe protocols | **ICMP + TCP-connect** per target (TCP catches ICMP-rate-limited paths that ICMP-only diagnosis lies about) |
| 18 | Auth model | Kerberos primary in v1; **bearer-token schema designed-in from W1** for v2 off-LAN/VPN/BYOD clients — retrofitting auth across 2000 deployed clients is the worst kind of refactor |

---

## 2. Repo layout

```
pingy/
├── client/                          # .NET 8 WPF solution
│   ├── Pingy.sln
│   ├── src/
│   │   ├── Pingy.Widget/            # WPF UI exe (always-on-top, per-user)
│   │   ├── Pingy.Agent/             # Windows Service (probing + upload)
│   │   ├── Pingy.Core/              # Pure domain: probe results, diagnosis FSM
│   │   ├── Pingy.Ipc/               # Named-pipe contracts (shared)
│   │   └── Pingy.Doctor/            # CLI for support: pingy-doctor.exe
│   ├── tests/
│   │   ├── Pingy.Core.Tests/
│   │   └── Pingy.Agent.Tests/
│   └── installer/Pingy.Wix/         # WiX v4 .wixproj
├── server/                          # Laravel 11 modular monolith
│   ├── app/Modules/{Auth,Ingest,Targets,Tickets,Machines,Dashboard}/
│   ├── app/Shared/Contracts/        # cross-module interfaces
│   ├── database/migrations/
│   ├── resources/views/dashboard/   # Livewire
│   └── tests/{Feature,Unit}/        # Pest
├── infra/
│   ├── nginx/                       # SPNEGO config + keytab placement
│   ├── docker/                      # local dev (compose w/ test KDC)
│   ├── terraform/ or ansible/       # prod (2 app + 1 PG + 1 Redis + MinIO)
│   └── k6/                          # load tests
├── docs/
│   └── adr/                         # architecture decision records
├── .kris/
│   └── docs/architecture/           # this file lives here
├── .ci/                             # pipeline definitions (provider-agnostic)
└── VERSION                          # client/server version pairing
```

Two CI pipelines (Windows runner for client, Linux runner for server). Single repo because two devs is too small to justify polyrepo overhead.

---

## 3. WPF client architecture

### Process model

**Two binaries, two processes** (split is non-negotiable):

- **`Pingy.Agent`** — Windows Service, runs as **LocalSystem**. Owns probing, aggregation, SQLite spool, HTTPS upload. Survives user logoff, lock, RDP-disconnect, fast-user-switch.
- **`Pingy.Widget`** — per-user WPF process (Run-key autostart). Thin MVVM view. Talks to agent via named pipe `\\.\pipe\pingy-agent-v1` (ACL: Authenticated Users). JSON-RPC contract.

**Why split:**
- Probing survives user-state changes (logoff, lock, RDP, kiosk sessions)
- Widget crash ≠ telemetry loss
- Kill-switch lives cleanly in the service
- "Report to IT" bundle is already on disk when user clicks
- Under LocalSystem, SPNEGO uses the **machine's computer-account ticket** — auth identity is machine-bound (privacy-friendlier), while the request body still carries `current_logged_in_user` as informational data

### Frameworks & UI

- MVVM via `CommunityToolkit.Mvvm` source generators only — no Prism, no ReactiveUI
- Three view models: Dot, ExpandPanel, ReportDialog
- Single-instance via mutex
- Widget runs from `HKCU\...\Run` registry entry written by MSI install

### ICMP

`System.Net.NetworkInformation.Ping` (managed) — async, 1500ms timeout, 32-byte payload, DontFragment=true. Raw sockets rejected: no fidelity gain at 10s cadence, requires admin (we have it but it's a needless escalation), trips EDR/Defender.

### Scheduling & buffering

- `PeriodicTimer` **30s** in agent, parallel `Ping.SendPingAsync` per target + parallel TCP-connect to target's `port` (if set)
- **2-of-3 failure rule:** state flips to degraded/red only after 2 of the last 3 windows show failure — single dropped packet does not flip the dot
- Aggregator computes p50/p95/loss in 60s tumbling windows **with ±15% jitter** (avoid wall-clock thundering herd)
- In-memory ring buffer (last 30 min, ~50 KB) + SQLite spool at `C:\ProgramData\Pingy\spool.db` (cap 50 MB, FIFO drop)
- Spool survives reboots; on-startup drains to backend before fresh probes
- `WaitableTimer` with `TIMER_RESUME_FROM_SUSPEND` so 60s upload tick survives modern-standby S0ix

### Config storage

| Path | Purpose | Writer |
|------|---------|--------|
| `C:\ProgramData\Pingy\config.json` | Machine config (backend URL, log level) | Agent service |
| `%LOCALAPPDATA%\Pingy\ui.json` | User UI prefs (panel position) | Widget |
| `C:\ProgramData\Pingy\spool.db` | Offline telemetry spool | Agent service |
| `HKLM\Software\Policies\Pingy\BackendUrl` | GPO override | IT (GPO) |

### Backend URL discovery (priority order)

1. MSI install property `BACKEND_URL` (set at deploy time by SCCM/GPO)
2. GPO registry override `HKLM\Software\Policies\Pingy\BackendUrl`
3. AD DNS SRV `_pingy._tcp.<ad-domain>` — IT can move backend without redeploy
4. Hardcoded build-time fallback

### Sleep/wake/network change

- `SystemEvents.PowerModeChanged` Resume → flush spool, refresh targets, reset RTT baseline
- `NetworkChange.NetworkAddressChanged` (debounced 5s) → re-resolve gateway via `GetIPProperties().GatewayAddresses`, refetch `/targets`, drop stale in-flight probes
- Suspend → drop in-flight HTTP, write a "gap" marker so dashboard doesn't read suspend as packet loss
- Backoff on upload failure: exponential 5s → 5min cap, full jitter

### Kerberos (client side)

`HttpClient` with `WinHttpHandler { ServerCertificateValidationCallback = …, ServerCredentials = CredentialCache.DefaultNetworkCredentials, PreAuthenticate = true }`. SPN: `HTTP/pingy.corp.<tenant>`. No fallback to NTLM — it would mask a real Kerberos break.

### Companion CLI: `pingy-doctor.exe`

Day-1 ship. Runs:
- `klist` — current Kerberos tickets
- `nltest /dsgetdc:<domain>` — DC connectivity
- DNS SRV resolution check
- `/api/v1/whoami` round-trip with raw SPNEGO header dump
- Spool size + last successful upload timestamp
- Output a paste-ready support bundle

IT will use this daily.

---

## 4. Laravel backend architecture

### Module boundaries

**Modular monolith** under `app/Modules/<Name>/`:

| Module | Responsibility |
|--------|----------------|
| `Auth` | Kerberos REMOTE_USER → User resolution; AD group sync (nightly Artisan); short-lived signed cookie for dashboard XHR |
| `Machines` | Registration, last-seen, AD-username binding, current status materialized view |
| `Targets` | Subnet → target list resolution; admin CRUD; versioned config blob with ETag |
| `Ingest` | Hot-path write endpoint; **only module that writes telemetry** |
| `Tickets` | "Report to IT" submissions, triage state machine, ticket_events audit log, server-side coalescing |
| `Dashboard` | Read-only Livewire views — machine grid + ticket triage |

Each module: `Models/`, `Services/`, `Actions/`, `Http/{Controllers,Requests,Resources}/`, `DTOs/`, `Routes/api.php`, `Providers/`. Cross-module calls go **only** through `app/Shared/Contracts` interfaces. Enforce with **Deptrac in CI**.

### Web stack

**nginx + PHP-FPM + SPNEGO** (Bit4Id-maintained `spnego-http-auth-nginx-module` fork; fall back to Apache + `mod_auth_gssapi` sidecar if module proves rough). **Never IIS, never bare Apache.**

- nginx terminates SPNEGO, sets `REMOTE_USER` from Kerberos principal
- nginx **strips `REMOTE_USER` from inbound requests** before setting it (classic header-injection pitfall)
- Forwards to PHP-FPM via `fastcgi_param REMOTE_USER`
- Keytab `/etc/krb5.keytab`, mode 0640, owned by www-data, distributed via Ansible from a sealed vault

### Queue strategy

**Sync writes for `/ingest`.** 33 req/s baseline is trivial for a single INSERT; queueing to Redis just to immediately drain adds latency and a hot-path SPOF.

Horizon + Redis kept for: ticket notifications, target-config rebuilds, daily rollup jobs (v2), incident-burst overflow, async blob uploads to MinIO. Workers split into `default` and `tickets` supervisors.

Resilience during DB outage comes from the **client-side SQLite spool**, not server-side queueing.

### API contracts (v1)

All endpoints SPNEGO-authed. `machine_id` in body is **cross-checked against the SPN's host principal** — clients cannot impersonate other machines.

```
POST /api/v1/machines/register
  Body: {hostname, os_version, agent_version, mac_addresses[], ipv4_addresses[]}
  → 200 {machine_id, target_refresh_seconds}

GET  /api/v1/targets?machine_id=...&etag=...
  → 200 {version, ttl_s, targets:[{id, host, kind, port?, weight}]}
  → 304 if unchanged

POST /api/v1/ingest
  Body: {machine_id, agent_version, sent_at,
         samples:[{target_id, window_start, window_end,
                   sent, recv, p50_ms, p95_ms, loss_pct}]}
  → 202 {accepted: N, server_time, next_poll_s}

POST /api/v1/tickets
  Header: Idempotency-Key: <uuid>
  Body: {summary, diagnosis_code,
         attached_window_start, attached_window_end,
         last_5min_blob}
  → 201 {ticket_id, ticket_number}

GET  /api/v1/agent-policy
  → {disabled: bool, until?: timestamp, ping_cadence_s_override?}

GET  /api/v1/whoami           # debug, kept forever
  → {ad_user, ad_sid, machine_principal, request_remote_addr}
```

---

## 5. Database schema (PostgreSQL)

**Postgres over MySQL:** native partitioning, JSONB + GIN, BRIN time-series indexes, `cidr`/`inet` types for subnet matching.

```sql
machines (
  id              BIGSERIAL PRIMARY KEY,
  hostname        CITEXT UNIQUE NOT NULL,
  ad_sid          TEXT UNIQUE,                  -- stable across rename
  ad_username     TEXT,                          -- last logged-in (informational)
  os_version      TEXT,
  agent_version   TEXT,
  subnet_cidr     CIDR,
  status          SMALLINT,                      -- 0 ok / 1 degraded / 2 down / 3 stale
  first_seen_at   TIMESTAMPTZ,
  last_seen_at    TIMESTAMPTZ
);
CREATE INDEX ON machines(last_seen_at DESC);
CREATE INDEX ON machines USING gist (subnet_cidr inet_ops);

targets (
  id              BIGSERIAL PRIMARY KEY,
  host            TEXT NOT NULL,
  kind            TEXT NOT NULL,                 -- gateway|core_router|dhcp|dns|app_server
  port            INT,                           -- nullable; if set, TCP-connect probe
  display_name    TEXT,
  active          BOOLEAN DEFAULT true,
  created_at      TIMESTAMPTZ,
  updated_at      TIMESTAMPTZ
);

target_assignments (
  id              BIGSERIAL PRIMARY KEY,
  subnet_cidr     CIDR NOT NULL,
  target_id       BIGINT REFERENCES targets,
  priority        SMALLINT,
  UNIQUE(subnet_cidr, target_id)
);
CREATE INDEX ON target_assignments USING gist (subnet_cidr inet_ops);

telemetry_aggregates (
  machine_id      BIGINT NOT NULL,
  target_id       BIGINT NOT NULL,
  window_start    TIMESTAMPTZ NOT NULL,
  window_end      TIMESTAMPTZ NOT NULL,
  sent            SMALLINT, recv SMALLINT,
  p50_ms          REAL, p95_ms REAL, loss_pct REAL,
  PRIMARY KEY (window_start, machine_id, target_id)
) PARTITION BY RANGE (window_start);
-- Daily partitions managed by pg_partman; 7-day retention via DROP PARTITION (O(1))
-- BRIN on window_start; btree on (machine_id, window_start DESC)

tickets (
  id                      BIGSERIAL PRIMARY KEY,
  ticket_number           VARCHAR(16) UNIQUE,
  machine_id              BIGINT REFERENCES machines,
  reporter_ad_username    TEXT,
  diagnosis_code          TEXT,
  summary                 TEXT,
  status                  SMALLINT,             -- new|triage|in_progress|resolved|closed
  assigned_to             BIGINT,
  attached_window_start   TIMESTAMPTZ,
  attached_window_end     TIMESTAMPTZ,
  diagnostic_blob_url     TEXT,                 -- MinIO object URL
  parent_ticket_id        BIGINT REFERENCES tickets,  -- coalescing
  created_at, updated_at, resolved_at TIMESTAMPTZ
);
CREATE INDEX ON tickets(status, created_at DESC);
CREATE INDEX ON tickets(machine_id);

ticket_events (
  id                BIGSERIAL PRIMARY KEY,
  ticket_id         BIGINT REFERENCES tickets ON DELETE CASCADE,
  actor_ad_username TEXT,
  kind              TEXT,                       -- comment|status_change|assignment|coalesce|reveal_identity
  payload           JSONB,
  created_at        TIMESTAMPTZ
);
CREATE INDEX ON ticket_events(ticket_id, created_at);
```

**PgBouncer** (transaction mode) in front of Postgres. PHP-FPM without pooling is the classic 2am page.

---

## 6. Ingest hot-path & incident-burst plan

**Steady state:** 2000 clients × 1 POST/60s = ~33 req/s. Trivial.

**Real risk — incident burst:** core router blip flips ~800 widgets red simultaneously; ~30% click "Report to IT" within 60s → ~250 ticket POSTs + retry-stacked ingest queue.

### Mitigations baked into v1

1. **Client-side jitter** on every periodic action (±15% on aggregation flush, 0–30s random delay before "Report to IT" submit) — cuts peak ~10×
2. **Server-side coalescing** — ≥50 tickets with same `diagnosis_code` + `subnet_cidr` in 60s → auto-coalesce into a parent ticket (children link via `parent_ticket_id`). IT sees one row "Gateway unreachable on 10.20.30.0/24 (147 reports)" not 147 rows. Single Redis counter per `(code, subnet)` bucket
3. **Per-machine rate limit** — 1 ticket / 60s, 1 ingest / 30s. nginx `limit_req` keyed on `X-Remote-User`
4. **Backpressure** — `/ingest` returns `429 Retry-After` if Postgres write latency p95 > 500ms; client respects via SQLite spool
5. **Idempotency-Key on `/tickets`** — never silent-retry; idempotency is for safety, not retry
6. **Two app VMs from day 1** — single-app-server is a Day-2 outage waiting to happen
7. **Separate PHP-FPM pools** — `ingest`, `tickets`, `dashboard`. A ticket flood cannot starve telemetry; a dashboard query cannot starve either
8. **Async blob upload** — `/tickets` writes the `last_5min_blob` to MinIO via a Horizon job; the synchronous part is just the row insert

### Capacity target

Sustain **500 req/s for 5 minutes** (worst-case incident burst). k6 test in `infra/k6/` gates merges to main.

---

## 7. MSI / signing pipeline (air-gapped)

### Build

**WiX v4** over MSIX. WiX gives clean `ServiceInstall`/`ServiceControl`, MSI properties SCCM/GPO can pass at deploy time, well-trodden enterprise path.

**Pipeline** (on-prem CI, self-hosted Windows runner):

1. `dotnet publish -c Release -r win-x64 --self-contained` (single-file, ReadyToRun) for `Pingy.Agent`, `Pingy.Widget`, `Pingy.Doctor`
2. NuGet restore from **internal mirror only** (build VM has no public egress)
3. Sign each `.exe`/`.dll` with `signtool` — key referenced via KSP from on-prem HSM (no key on disk)
4. WiX `candle`/`light` → `pingy.msi` with bundled service install action
5. Sign the `.msi` (same KSP path)
6. **RFC3161 timestamp** every signature against **internal TSA** — without timestamps, binaries break on cert expiry
7. Publish artifact to internal release feed
8. **Manual promotion gate** to SCCM (or GPO software-install share) — installer rollouts deserve a human gate
9. SCCM Distribution Points push to collection in waves (pilot → ring 1 → ring 2 → fleet); GPO equivalent uses security-group filtering

### Cert handling — non-negotiables

- Code-signing cert issued from **internal AD CS**, template = "Code Signing"
- Private key generated **on the HSM**, non-exportable, never on build-VM disk, never in CI secrets
- CDP and AIA extensions point to **internal HTTP** URLs (`http://pki.corp.local/crl/...`, `http://pki.corp.local/aia/...`)
- Verify with `certutil -url <cert>` before first signing — public URLs hang every install for 15s then fail
- Build VM is in a dedicated VLAN: egress only to internal HSM, internal package mirror, internal git, internal TSA, internal artifact store. **No public internet route.**
- AD CS root must be in `Trusted Root Certification Authorities` on every fleet machine. AD GPO does this automatically for domain-joined PCs — verify on a clean install with `certlm.msc`

### Why no SmartScreen warnings

SmartScreen only triggers when a user double-clicks a downloaded file. MSIs installed by SCCM client agent or GPO run as SYSTEM in a managed context and **never trigger SmartScreen**. Our 2000-seat fleet is 100% managed deployment → SmartScreen is irrelevant.

---

## 8. On-prem infra footprint

| Component | Sizing | Notes |
|-----------|--------|-------|
| App VMs | 2 × 4 vCPU / 8 GB Linux | nginx + PHP-FPM + Laravel |
| Postgres | 4 vCPU / 16 GB, gp3-equiv SSD | Managed if internal DBaaS; PgBouncer in front |
| Redis | 2 vCPU | Horizon backing |
| MinIO | Existing object storage or 1 × 4 vCPU / S3-compat | Diagnostic blob storage |
| CI controller | 1 × Linux (Jenkins/Drone/Woodpecker) | Reuses existing if present |
| Windows CI runner | 1 × Windows Server 2022 | Only host that touches HSM |
| HSM | 1 × YubiHSM 2 / Thales nShield / SafeNet | Or hardware token (Yubikey/SafeNet eToken) on the runner |
| Reuses (existing) | AD/KDC, AD CS, internal DNS, NTP, Prom+Grafana, NuGet/Composer mirrors | — |

Headroom: 5× expected baseline, 4× incident-burst budget.

---

## 9. Top 3 production risks

### 1. Kerberos misconfig will break the whole fleet silently

SPN duplication, keytab kvno drift after AD password rotation, clock skew >5min, CNAME records (Kerberos hates CNAMEs by default), nginx-SPNEGO module quirks.

**Mitigations:**
- `pingy-doctor.exe` companion CLI ships day 1 — IT will use it daily
- **Synthetic Kerberos canary** from a domain-joined VM hits `/api/v1/ingest` every 60s; pages on failure
- `/api/v1/whoami` debug endpoint kept forever
- Document keytab rotation runbook **before** first prod deploy, not after the first outage
- AD DCs as NTP source for clients and servers (Kerberos breaks at >5min skew)

### 2. The diagnosis logic is a trust lie-detector

A green dot when the user can't work destroys confidence in 24 hours. Default to **amber + "no network issues detected, contact IT if app is broken"** rather than confident green. The diagnosis FSM in `Pingy.Core` needs an explicit "we don't know" state. Conservative wins.

### 3. Probe traffic is itself a network event

2000 hosts × 10s ICMP to core router = 200 pps sustained. Trivial *unless* the router's control-plane policer treats ICMP-to-self as low priority and your "outage" is actually you DoS'ing the management plane. Mitigations:
- Coordinate with NetOps **before** rollout
- Ping the gateway interface IP (data plane), not loopback
- **Get the binary hash allowlisted with security/EDR team before pilot** — 2000 endpoints emitting rapid signed-but-unfamiliar ICMP looks like a beacon to CrowdStrike/Defender for Endpoint

### Air-gap-specific risks

- **Cert lifecycle ownership** — AD CS code-signing certs typically last 2–3 years. Add calendar reminder 60 days before expiry. With timestamping enforced, already-installed MSIs survive expiry; but no new MSIs ship until renewed. Document renewal in W8.
- **CRL availability is a hard dependency** — if `pki.corp.local` is down, fresh installs stall on revocation lookup. Replicate CDP across at least two internal web servers; monitor with Prometheus.
- **Internal mirror drift** — internal NuGet/Composer mirror needs a refresh policy (weekly sync). Otherwise security patches stop arriving silently. Pick a cadence and own it.

---

## 10. Sequencing (8 weeks: 1 BE + 1 WinDev + 0.5 DevOps)

| Wk | Backend (1.0) | Windows (1.0) | DevOps (0.5) |
|----|---------------|---------------|--------------|
| **1** | Laravel skeleton, modules scaffolded, Auth + `/whoami` | Solution + Service skeleton, `Ping` spike against internal target | Self-hosted runner; Postgres/Redis/MinIO dev compose; KDC test container; **issue code-signing cert from AD CS w/ internal CDP/AIA**; **stand up internal RFC3161 TSA** (EJBCA or freeTSA) |
| **2** | nginx+SPNEGO end-to-end; Auth resolves REMOTE_USER; `pingy-doctor` design | `Pingy.Agent` Worker Service; named-pipe IPC; SQLite spool | HSM/token configured on Windows runner; signtool PoC end-to-end against unsigned dev build |
| **3** | Machines + Targets modules + migrations + ETag on `/targets` | Probe loop; ring buffer; jittered timer; target fetch + cache | WiX skeleton; unsigned MSI builds in CI |
| **4** | Ingest endpoint + partitioning + per-machine rate limit | HTTPS upload via SPNEGO (LocalSystem machine ticket); retry/backoff | Signing pipeline end-to-end green; signed MSI installs on test VM |
| **5** | Tickets module + coalescing + Idempotency-Key + diagnostic blob to MinIO async | Diagnosis FSM in `Pingy.Core`; expand panel UI; "Report to IT" flow | Staging env stood up; k6 baseline run |
| **6** | Dashboard (Livewire): machine grid (5s poll) + ticket triage view | Sleep/wake/network handlers + SRV discovery + `pingy-doctor.exe` | SCCM application package + collection structure |
| **7** | Hardening: separate PHP-FPM pools (ingest/tickets/dashboard), kill-switch endpoint, k6 at 500 req/s | Kill-switch consumer; agent self-telemetry; crash dumps; MSI upgrade scenarios | EDR allowlisting w/ security; prod terraform/ansible applied; **50-machine pilot via SCCM ring 0** |
| **8** | Bug bash; runbooks (keytab rotation, cert renewal, incident response); HR/legal sign-off on identity | Bug bash; signed RC build; MSI uninstall scenarios | SCCM ring 0 → ring 1 (200 machines); monitoring (Prom+Grafana) |

**Anything not done by W5 gets cut, not slipped.** GA gate end of W8 → ramp 500 machines/wk after pilot stabilizes.

---

## 11. Standing recommended revisions

**All 4 F-thread-flagged revisions are now locked into the spec (as of 2026-05-12).** See decisions #6, #14, #17, #18 in §1. No outstanding revisions.

If new revisions arise during implementation, write them as ADRs in `docs/adr/NNNN-decision.md` and surface to the user before quietly diverging from the locked plan.

---

## Appendix: Commands worth keeping

```powershell
# Verify code-signing cert has internal CDP/AIA
certutil -url <path-to-pingy-codesign.cer>

# Check fleet machine trusts our internal CA
certlm.msc  # Trusted Root Certification Authorities → look for org root CA

# Kerberos sanity (run from a domain-joined Windows machine)
klist
nltest /dsgetdc:<domain>
nltest /sc_query:<domain>

# Install MSI via SCCM client manually (debugging)
ccmexec.exe /forcepolicy
```

```bash
# Postgres partition rotation status
SELECT * FROM partman.show_partitions('public.telemetry_aggregates');

# Horizon queue depth (during incident drill)
php artisan horizon:status
redis-cli LLEN queues:default
redis-cli LLEN queues:tickets

# k6 burst test
k6 run infra/k6/ingest_burst.js --vus 500 --duration 5m
```
