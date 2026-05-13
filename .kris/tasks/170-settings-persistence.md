# 170 — Persist UI settings across restart

**Status:** pending · **Owner:** unassigned · **Depends on:** v2 widget exists · **Effort:** medium

## Why

Today, every user-visible toggle resets on app start: `AnimationsEnabled` → true, `ShowMiniDots` → true, `IsTopmost` → true, `IsMiniMode` → false, `SelectedSort` → MANUAL ORDER, `OpacitySlider.Value` → 0.95, window Width/Height → 640×500. Position too. The user re-tweaks every launch.

Target list order, label, tags, host, and `interval_seconds` already persist (in `targets.json`) — but the UI state doesn't.

## Scope

Add a sibling JSON file next to `targets.json`, e.g. `settings.json`, containing a `WidgetSettings` record:

```jsonc
{
  "version": 1,
  "animations_enabled": true,
  "show_mini_dots": true,
  "is_topmost": true,
  "is_mini_mode": false,
  "selected_sort_label": "MANUAL ORDER",   // matches SortChoice.Label
  "opacity": 0.95,
  "full_width": 640,
  "full_height": 500,
  "mini_width": 160,
  "mini_height": 200,
  "window_left": null,                      // null = let WPF pick
  "window_top": null
}
```

New `SettingsLoader` (mirrors `JsonTargetLoader`):
- `LoadAsync()` returns `WidgetSettings`, applying defaults for missing fields (so v1 → v2 schema bumps don't crash)
- `SaveAsync(settings)` writes atomically (`.tmp` + `File.Replace`)
- Same exe-relative path strategy as `JsonTargetLoader.DefaultPath()`

In `MainViewModel`:
- `StartAsync` reads settings AFTER targets are loaded; applies them to the corresponding properties
- Each settable property gets a `partial void OnXChanged` that calls a debounced `_ = SaveSettingsAsync()` — debounce ~300 ms so dragging the opacity slider doesn't write hundreds of times
- One central `_settings` field holds the current snapshot; per-property handlers update one field then save

In `MainWindowV2.xaml.cs`:
- `Loaded` event: after VM has loaded settings, apply window-level state (`Width`/`Height`/`Left`/`Top`/`WindowState`)
- `LocationChanged`/`SizeChanged` handlers: forward new values to VM (which debounce-saves)

## Acceptance criteria

- Toggle PIN off, change INTERVAL to 30, drag the OPACITY slider to 0.6, switch to mini mode, position the window in a corner → close and relaunch → exactly that state restored
- New `settings.json` file created next to `config/targets.json` after first state change
- Missing settings file (fresh install): app uses current defaults without error
- Old settings file with a future `version` value: app logs a warning, ignores it, falls back to defaults
- Window `Left/Top` outside any visible monitor (e.g., disconnected second display) is clamped back into the primary monitor's working area (`SystemParameters.WorkArea`)
- Resizing the window during a debounce window (300 ms) doesn't cause file lock contention (atomic write handles it; verify with rapid-resize stress)

## Files

- `client/src/Pingy.Core/Models/WidgetSettings.cs` (new)
- `client/src/Pingy.Core/Config/ISettingsLoader.cs` (new)
- `client/src/Pingy.Core/Config/JsonSettingsLoader.cs` (new) — same atomic-write pattern as `JsonTargetLoader`
- `client/src/Pingy.Widget/ViewModels/MainViewModel.cs` — load/save plumbing + per-property handlers
- `client/src/Pingy.Widget/MainWindowV2.xaml.cs` — window-level state save/restore
- `client/src/Pingy.Widget/App.xaml.cs` — instantiate `JsonSettingsLoader` and pass to MainViewModel constructor

## Design notes

- Debouncing: simplest is a single `Timer` field on `MainViewModel` that resets on each property change; on tick, calls `_loader.SaveAsync(_settings)`. `System.Threading.Timer` works; remember thread marshalling.
- Don't persist transient state like `HealthBrush`, `StatusLine`, `Targets[].DropIndicator` — those derive from data
- Reuse the same `AppContext.BaseDirectory` + `config/` location pattern from `JsonTargetLoader` so the settings file is part of the portable folder
- Settings file should be human-editable (pretty-printed JSON, snake_case via the same `JsonSerializerOptions`)
- Consider "import/export settings" as a follow-up to share preferred configs across machines

## Out of scope

- Cloud sync of settings (air-gap principle)
- Profile system (multiple named setting bundles)
- First-run wizard
