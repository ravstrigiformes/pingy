# pingy — Windows desktop widget

Cyberpunk-styled Windows network diagnostic widget. Always-on-top, drag-to-reorder targets, heart-monitor-style latency strip, mini mode, fully portable (config sits next to the .exe).

This repo is the **Windows client** of the pingy project. The bundled-prototype Laravel backend has been split into a sibling repo (planned: `pingy-server`); shared specs and the v1 architecture plan will move to `pingy-protocol/` once that repo exists.

> **Multi-platform plan:** future siblings — `pingy-linux`, `pingy-macos`, `pingy-android`, `pingy-ios`, `pingy-server`, `pingy-protocol`. Today, this Windows client is the only platform.

## Quick start

```powershell
# Prerequisites: .NET 8 SDK on Windows
git clone git@github.com:ravstrigiformes/pingy.git
cd pingy/client
dotnet build Pingy.sln

# Run the widget
.\src\Pingy.Widget\bin\Debug\net8.0-windows\Pingy.Widget.exe
```

On first run, the app creates `config/targets.json` next to the `.exe` (seeded with two placeholder hosts). Edit it via the in-app `+ ADD TARGET` dialog or directly in JSON.

## Repo layout

```
client/                  .NET 8 WPF solution
├── src/
│   ├── Pingy.Widget/    Always-on-top WPF UI (per-user)
│   ├── Pingy.Agent/     Windows Service skeleton (probing & upload, future)
│   ├── Pingy.Core/      Domain: probes, diagnosis FSM, JSON config
│   ├── Pingy.Ipc/       Named-pipe contracts (future Widget ↔ Agent split)
│   └── Pingy.Doctor/    Support CLI (future)
├── tests/               xUnit
└── installer/           WiX v4 (future)
infra/                   nginx (SPNEGO), docker compose for dev, k6 load tests
docs/adr/                Architecture Decision Records
.kris/                   Project context — plans, conventions, task briefs
.ci/                     Pipeline definitions (provider-agnostic)
```

## Stack (this repo)

- .NET 8 WPF
- CommunityToolkit.Mvvm (source-generated MVVM)
- Microsoft.Data.Sqlite (offline spool buffer, future)
- WiX v4 (MSI installer, future)

The original full-stack v1 plan (Windows + Laravel server + Kerberos auth + SCCM deploy) is preserved at `.kris/docs/architecture/pingy-v1-plan.md`. It describes the bundled prototype architecture and stays the canonical spec until split into per-repo specs.

## Status

**v2 widget shipped** — drag-reorder, mini mode, heart-monitor latency strip, animated cyber theme, click-to-edit, tag filter + sort, exe-relative portable config. See `.kris/tasks/` for backlog and `ONBOARDING.md` for fresh-agent onboarding.

## License

TBD.
