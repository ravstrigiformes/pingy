# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repo identity

This is **`pingy-windows`** — the Windows desktop client of the multi-repo `pingy` project. The bundled-prototype Laravel backend has been split out (planned sibling `pingy-server`); shared specs will eventually move to `pingy-protocol`. Only the Windows client lives here. `ONBOARDING.md` still references the older monorepo layout (with `server/`, `infra/`, etc.) — those folders do **not** exist in this repo, only `client/`. Treat ONBOARDING.md as authoritative for the *product* (locked decisions, principles, planning history) and this file as authoritative for the *current repo state*.

## Canonical context (read these before non-trivial work)

1. `ONBOARDING.md` — operating principles, 18 locked decisions (§5), top production risks, planning patterns. **Air-gap-first is non-negotiable** (no public-internet build/runtime deps).
2. `.kris/docs/architecture/pingy-v1-plan.md` — the locked v1 plan (full-stack, single source of truth for product scope/sequencing).
3. `.kris/tasks/README.md` + `.kris/tasks/NNN-*.md` — self-contained task briefs. Pick lowest-numbered first; `_DONE-YYYY-MM-DD` suffix marks completed ones.
4. `.kris/docs/architecture/thread-based-engineering.md` — F-thread / B-thread planning patterns the user prefers for non-trivial work.

## Build / test / run

Working dir for all commands: `client/`. Requires .NET 8 SDK on Windows (WPF).

```powershell
cd client
dotnet restore Pingy.sln
dotnet build Pingy.sln -c Debug
dotnet test                                          # all xUnit tests
dotnet test tests/Pingy.Core.Tests                   # single project
dotnet test --filter FullyQualifiedName~PingerTests  # single class
dotnet format                                        # lint/format
dotnet format --verify-no-changes                    # CI-style check

# Run the widget
./src/Pingy.Widget/bin/Debug/net8.0-windows/Pingy.Widget.exe
```

On first run, the widget creates `config/targets.json` **next to the .exe** (portable layout — `AppContext.BaseDirectory`, not `%LOCALAPPDATA%`). One-time migration from the legacy `%LOCALAPPDATA%\Pingy\targets.json` happens in `JsonTargetLoader.EnsureSeededAsync`.

## Solution architecture

`client/Pingy.sln` — five source projects + two test projects. The dependency arrows are the architecture:

```
Pingy.Widget (WPF, WinExe, net8.0-windows)
   ├─► Pingy.Core   (pure domain, net8.0 — no WPF, no service deps)
   └─► Pingy.Ipc    (record-based named-pipe contracts)

Pingy.Agent  (Worker Service skeleton, net8.0)
   ├─► Pingy.Core
   └─► Pingy.Ipc

Pingy.Doctor (support CLI, future)
```

**Why two processes (Widget + Agent):** locked decision (ONBOARDING §1.2, plan §3). `Pingy.Agent` is the Windows Service (LocalSystem) that owns probing + HTTPS upload; `Pingy.Widget` is the per-user view talking over a named pipe. **Never merge them.** Today the Widget probes directly via `Pingy.Core.Probing.Pinger` because the IPC split is W2 work — keep `Pingy.Core` free of WPF references so the Agent can pick it up unchanged.

**WPF entry point quirk:** `App.xaml` sets `StartupUri="MainWindowV2.xaml"`. `MainWindow.xaml` is legacy/v1 and unused at runtime. The active window is `MainWindowV2`. The `App` class hand-wires the VM in `OnStartup` (no DI container) — `JsonTargetLoader` + `Pinger` → `MainViewModel`. `MainWindowV2` pulls the VM off `App.ViewModel` in its constructor and kicks off `StartAsync()` in `Loaded`.

**MVVM convention:** `CommunityToolkit.Mvvm` source generators only (`[ObservableProperty]`, `partial void On*Changed`). Don't write hand-rolled `INotifyPropertyChanged`. View → ViewModel binding is the only direction; views call public methods on the VM for commands.

**Probe loop:** `MainViewModel.RunLoopAsync` uses a `PeriodicTimer` at `IntervalSeconds` cadence, fans out to all targets in parallel via `_pinger.PingAsync`, and marshals UI updates back through `Application.Current.Dispatcher.Invoke`. Changing the interval cancels the CTS and restarts the loop (`PersistAndRestartAsync`).

**Config layer:** `TargetsConfig` is an immutable `record`; mutations use `with { ... }` and go through `JsonTargetLoader.SaveAsync` (writes to `.tmp` + `File.Replace` for atomicity). JSON is snake_case (`JsonNamingPolicy.SnakeCaseLower`). When adding a target the slug comes from `HostNormalizer.Slugify(label)` with a 4-char GUID suffix on collision.

**Sort + filter:** `TargetsView` is a `CollectionView` over the `ObservableCollection<TargetStatusViewModel>`. Manual drag-reorder always wins — `MoveTargetAsync` resets `SelectedSort` to `MANUAL ORDER` (the only sort that has `IsDefault==true` and adds no `SortDescription`). Tag chips drive `TargetFilter`; when any chip is selected, `CanReorder` flips to false (you can't drop into a filtered subset).

**Diagnosis FSM (future, plan §9):** the `Pingy.Core/Diagnosis/` state machine is not yet implemented but is load-bearing for the product — its job is to **avoid confident "green"** when uncertain. The current widget only computes a coarse `HealthBrush` (cyan/yellow/magenta) in `RecomputeHealth`. When the FSM lands, default to amber/`Investigating`, not green.

## Coding conventions (project-specific)

- **Nullable + ImplicitUsings on** in every csproj. Honor it; don't `#nullable disable`.
- **`record` for IPC payloads** in `Pingy.Ipc`; `record` for config DTOs in `Pingy.Core.Models`.
- **No DI container in the Widget** — wire dependencies by hand in `App.OnStartup`. The Agent uses `Microsoft.Extensions.Hosting` and *will* have DI; don't conflate the two.
- **Probe code stays in `Pingy.Core`** — pure, testable, no WPF, no `Microsoft.Extensions.Hosting`.
- **xUnit only** for tests (no FluentAssertions, no Moq pulled in yet).
- **Conventional commits** (`feat(scope): …`, `fix(scope): …`). Scopes follow the project name: `widget`, `core`, `agent`, `ipc`, `doctor`.
- **Defer ADRs to `docs/adr/`** (folder will be created on first ADR) if proposing to deviate from a locked decision — don't quietly diverge.

## Skills shipped with this repo

`.claude/skills/` contains Laravel-flavored skills inherited from the original monorepo (`laravel-architecture`, `eloquent-patterns`, `php-coding-standards`, `pest-testing`, `laravel-security`, `schema-sync`, `data-sync`). They do **not** apply to this client repo — they are stale carryover until backend work moves to `pingy-server`. There is no WPF/.NET skill yet; write one if recurring patterns emerge.

## Things that look wrong but aren't

- `Pingy.Ipc/Class1.cs` is a placeholder — IPC contracts are W2 work.
- `MainWindow.xaml` exists alongside `MainWindowV2.xaml` — v1 is dead code kept for reference; v2 is the active UI.
- `server/`, `infra/`, `docs/adr/` referenced in `ONBOARDING.md` are *not* in this repo. They belong to the un-split bundled prototype.
- `publish/` contains a built `Pingy.Widget.exe` — convenience artifact, not a build target.
