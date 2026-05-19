# Portable Release Build — Pingy.Widget

**Purpose:** How to build, ship, and run Pingy.Widget as a single portable `.exe` distributed via GitHub Releases (not committed to the repo).
**Audience:** Maintainers cutting builds; end-users who just want to run it.
**First built:** 2026-05-13 (`v0.1.0-dev.1`)

---

## 0. TL;DR

- **Build:** `dotnet publish` with single-file self-contained flags → `publish\Pingy.Widget.exe` (~68 MB, gitignored)
- **Ship:** Attach the .exe to a GitHub Release tagged from `main` (e.g. `v0.1.0-dev.1`). Binary lives on the Release, never in the repo
- **Run:** Drop the .exe in any folder, double-click. On first launch it creates `config\targets.json` next to itself. That folder is the *only* state the app produces
- **Move:** Copy the .exe + its `config\` folder together. No registry, no `%APPDATA%`, fully portable

---

## 1. Why a portable single-file build

| Constraint | Why it matters | Outcome |
|---|---|---|
| Air-gapped org | No app store, no installers from the internet | Single .exe, copy-deploy |
| ~2000 endpoints | Future SCCM/MECM rollout needs a clean payload | Self-contained = no .NET runtime prereq on targets |
| USB-stick / RDP scratch testing | IT walks the floor; sometimes "just run it on this box" | Drop and go, no admin |
| Reversible | No installer = no uninstall headache | Delete folder = gone |

This is the **xcopy deployment** model. .NET 8 + `PublishSingleFile=true` makes it trivial.

---

## 2. How the runtime layout works

The widget reads its config via `AppContext.BaseDirectory` — the directory containing the running `.exe` (works correctly for both standard *and* single-file deployments). See `client/src/Pingy.Core/Config/JsonTargetLoader.cs:25-32`.

### Folder layout after first run

```
C:\Pingy\                           ← put the .exe anywhere
├── Pingy.Widget.exe                ← single ~68 MB self-contained file
└── config\                         ← auto-created on first launch
    └── targets.json                ← auto-seeded with 2 sample hosts
```

### First-launch behaviour

1. Widget starts, computes `<exe-dir>\config\targets.json`
2. If that file exists → load it, done
3. If it doesn't exist:
   - Check legacy path `%LOCALAPPDATA%\Pingy\targets.json` (from older non-portable installs)
   - If legacy file exists → **copy** it into the new exe-relative location (one-time migration; legacy file is left in place)
   - Otherwise → write a seeded default with two sample targets (`host-a` 192.168.7.41, `host-b` 192.168.7.124, 5-second interval)
4. From then on, all reads/writes go to `<exe-dir>\config\targets.json` only

### What files the app creates

As of `v0.1.0-dev.1`: **only `config\targets.json`**.

No registry writes. No `%APPDATA%` use. No temp files outside the OS-managed single-file extraction cache (which .NET handles automatically and cleans).

If future versions add disk logging, expect a `logs\` folder next to the .exe (same exe-relative pattern). Update this doc when that lands.

### Implications

- **Multiple instances on one machine** (e.g. `C:\Pingy-prod\` and `C:\Pingy-test\`) → each has its own `config\`, fully isolated
- **Move the .exe alone** → it creates a *new* default config at the new location; the old `config\` doesn't follow
- **Move the .exe + `config\` together** → settings travel with you
- **Backup a user's setup** → just zip the folder

---

## 3. Building a release (maintainer flow)

### Prerequisites

- .NET 8 SDK (`dotnet --version` ≥ 8.0)
- `gh` CLI authenticated to GitHub (`gh auth status`)
- Working SSH push to `git@github.com:ravstrigiformes/pingy.git` (see [git-ssh-on-windows.md](git-ssh-on-windows.md) if not yet set up)

### Steps

```powershell
# 1. From the repo root, build single-file self-contained release
dotnet publish client\src\Pingy.Widget\Pingy.Widget.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o publish

# Output: publish\Pingy.Widget.exe (~68 MB) plus .pdb symbol files

# 2. Bump VERSION file if needed, then tag (semver; -dev.N for pre-release builds)
git tag -a v0.1.0-dev.1 -m "First single-file portable build (pre-release)"
git push origin v0.1.0-dev.1

# 3. Create GitHub Release, attach the .exe (mark --prerelease for -dev tags)
gh release create v0.1.0-dev.1 publish\Pingy.Widget.exe `
  --repo ravstrigiformes/pingy `
  --prerelease `
  --title "v0.1.0-dev.1 — First portable build" `
  --notes-file release-notes.md
```

### What each `dotnet publish` flag does

| Flag | Purpose |
|---|---|
| `-c Release` | Optimized build, no debug symbols in IL |
| `-r win-x64` | Target Windows x64 (the deployment platform) |
| `--self-contained true` | Bundle the .NET 8 runtime inside the .exe; target machine needs no .NET install |
| `-p:PublishSingleFile=true` | Pack everything into one `.exe` instead of `.exe + dozens of .dll` |
| `-p:IncludeNativeLibrariesForSelfExtract=true` | Embed native libs (e.g. WPF interop) inside the single file too |
| `-p:EnableCompressionInSingleFile=true` | Gzip the bundle — significant size reduction at small startup cost |

### Versioning convention

- `VERSION` file in repo root is the source of truth
- `0.1.0-dev` in `VERSION` → tag as `v0.1.0-dev.N` (incrementing N per build) and mark Release as **pre-release**
- When ready to ship stable, bump `VERSION` to `0.1.0`, tag as `v0.1.0`, drop `--prerelease`

---

## 4. Distributing to end-users

### "Download and run" flow (one user, one machine)

1. Send the user: `https://github.com/ravstrigiformes/pingy/releases/latest`
   *(or a specific tag: `…/releases/tag/v0.1.0-dev.1`)*
2. They download `Pingy.Widget.exe`
3. They put it in a folder of their choice (e.g. `C:\Pingy\`, `%USERPROFILE%\Apps\Pingy\`, or a network share)
4. Double-click. Done.

### Direct binary URL (skip the Releases page)

```
https://github.com/ravstrigiformes/pingy/releases/download/<tag>/Pingy.Widget.exe
```

Example: `https://github.com/ravstrigiformes/pingy/releases/download/v0.1.0-dev.1/Pingy.Widget.exe`

### Org-wide rollout (future)

For ~2000 endpoints, this hand-distribution model doesn't scale. The path forward is:
- **SCCM/MECM** publishes the .exe + a desired-state policy that places it under `C:\ProgramData\Pingy\` and pins a Start Menu shortcut
- **AD GPO** as fallback for non-SCCM-managed machines
- Eventually wrap in a signed MSI for cleaner Add/Remove Programs presence

The single-file portable .exe is the *payload* in all of those scenarios — the deployment tech wraps it; the .exe itself stays the same.

---

## 5. Why we don't commit the .exe to the repo

| Concern | Detail |
|---|---|
| **Git history bloat** | A 68 MB binary committed once stays in `.git/objects` forever. Every clone, on every machine, pays that cost — even after the file is deleted from `HEAD`. By release ~10 the repo would be >700 MB |
| **.NET convention** | `bin\`, `obj\`, and `publish\` are always gitignored in .NET projects (already line 90 of `.gitignore`). There is no committed `dist\` equivalent |
| **Wrong primitive** | "A downloadable build" is what **GitHub Releases** exists for — versioned, tagged, deletable, has its own URL space, doesn't touch the source tree |
| **Reviewability** | A diff with a binary in it is unreviewable. PRs should be readable source changes only |

If a binary ever needs to be in the repo (it shouldn't), the right tool is Git LFS, not raw blobs. We don't have an LFS use case here.

---

## 6. Related references

- [git-ssh-on-windows.md](git-ssh-on-windows.md) — SSH key + `core.sshCommand` setup that gets `git push` working on Windows *(write this doc if it doesn't exist yet)*
- `client/src/Pingy.Core/Config/JsonTargetLoader.cs` — config-loading source of truth
- `.gitignore` line 90 — confirms `publish/` is excluded
- `VERSION` — single source of truth for the version number
