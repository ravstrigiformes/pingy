# 110 — Per-target enable/disable (pause monitoring)

**Status:** pending · **Owner:** unassigned · **Depends on:** v2 widget exists

## Why

Sometimes a target is known-down (server is being rebuilt, host on vacation, etc.) and the user wants to STOP probing it without losing the configuration. Today the only options are: live with the down indicator, or delete and re-add later. Per-target enable/disable solves this.

## Scope

Add an `Enabled` boolean to the `Target` record:

```csharp
public sealed record Target(
    string Id,
    string Host,
    string Kind,
    string? Label = null,
    IReadOnlyList<string>? Tags = null,
    bool Enabled = true);
```

`MainViewModel.RunLoopAsync` skips disabled targets:

```csharp
private async Task ProbeAllAsync(CancellationToken ct)
{
    var snapshot = Targets.Where(t => t.Target.Enabled).ToList();
    // ... existing probe logic ...
}
```

In `TargetStatusViewModel`, when the target is disabled:
- `StateBadge` becomes `"PAUSED"`
- `StateBrush` becomes a dim-gray brush
- `RttDisplay` becomes `"PAUSED"`
- The dot strip / polyline freezes (no new samples appended)

Add a toggle in `AddTargetWindow` (the editor): a `[ ENABLED ]` ToggleButton with cyber styling, default ON. When in edit mode, reflects the current value; on save, persists.

Also add a quick toggle on each row — small icon (e.g., `⏸` pause) that flips Enabled without opening the editor. Place left of the existing diamond status indicator.

## Acceptance criteria

- Disabled targets show "PAUSED" status, gray dot/badge
- Probe loop doesn't ping disabled targets (verify with `netstat -n -p tcp | grep <host>` or wireshark)
- Editor's ENABLED toggle works for both add and edit
- Quick-toggle on row updates immediately (next tick the target is paused/resumed)
- `targets.json` persists the `enabled` field (default `true` if missing for backward compat)
- Health indicator (top-left) does NOT count paused targets as down

## Files

- `client/src/Pingy.Core/Models/Target.cs` — add `Enabled` property
- `client/src/Pingy.Core/Probing/Pinger.cs` — no change needed (loop-level skip)
- `client/src/Pingy.Widget/ViewModels/MainViewModel.cs` — skip disabled in `ProbeAllAsync`, exclude from `RecomputeHealth`, add `ToggleTargetEnabledAsync(string id)`
- `client/src/Pingy.Widget/ViewModels/TargetStatusViewModel.cs` — `IsEnabled` derived; render `PAUSED` state when off
- `client/src/Pingy.Widget/AddTargetWindow.xaml` — add Enabled toggle
- `client/src/Pingy.Widget/AddTargetWindow.xaml.cs` — propagate Enabled
- `client/src/Pingy.Widget/MainWindowV2.xaml` — add quick-toggle button on row

## Design notes

- Backward compat: `Enabled = true` is the default — existing `targets.json` without the field still works
- A paused target should remain visible in the list (otherwise users forget it exists)
- Consider a "Show paused" filter chip later (defer)

## Out of scope

- Scheduling (e.g., "pause this target Mon-Fri 9-5") — separate feature if requested
- Bulk pause/unpause (defer)
