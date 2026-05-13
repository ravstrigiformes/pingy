# 150 — Export / Import targets.json

**Status:** pending · **Owner:** unassigned · **Depends on:** v2 widget exists · **Effort:** small

## Why

Users want to back up their target list, share with colleagues, or move between machines. Right now the only way is to manually copy `%LOCALAPPDATA%\Pingy\targets.json`.

## Scope

Add two menu items (or buttons in a settings menu) in the title bar overflow:

1. **Export** → opens `Microsoft.Win32.SaveFileDialog` with default name `pingy-targets-{yyyy-MM-dd}.json`. Writes the current `_currentConfig` to the chosen path.
2. **Import** → opens `Microsoft.Win32.OpenFileDialog` filtered to `*.json`. Reads the file, validates schema, and either:
   - **Merge** mode: appends imported targets that don't conflict by ID (skip duplicates, prompt on conflict)
   - **Replace** mode: clobbers all current targets

After import: rebuild filter chips, recompute health, restart probe loop.

UX for the menu: a small `▼` overflow button next to the close cluster. Opens a popup with Export / Import options.

## Acceptance criteria

- Export produces a valid JSON file matching the schema in `JsonTargetLoader`
- Import successfully reads a file produced by Export
- Import handles malformed JSON gracefully (error dialog, no app crash)
- Import handles ID conflicts cleanly (prompts user: skip / overwrite / cancel)
- Sample dots / polyline reset for newly added targets
- Existing probe loop doesn't break during import

## Files

- `client/src/Pingy.Widget/MainWindowV2.xaml` — add overflow button + popup menu
- `client/src/Pingy.Widget/MainWindowV2.xaml.cs` — handlers using `Microsoft.Win32` dialogs
- `client/src/Pingy.Widget/ViewModels/MainViewModel.cs` — `ExportToFileAsync(path)`, `ImportFromFileAsync(path, mergeMode)`
- `client/src/Pingy.Core/Config/JsonTargetLoader.cs` — possibly extract reusable serialization helper

## Design notes

- Don't store the user's config path in the exported file — keep it environment-agnostic
- Validate version field on import — reject if `version > 1` until migration logic exists
- Consider a "preview" of imported targets before committing (defer)

## Out of scope

- Cloud sync (out of scope per air-gap principle)
- Diff view between current and imported
