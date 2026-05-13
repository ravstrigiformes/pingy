# 120 — Per-target probe timeout + interval override

**Status:** pending · **Owner:** unassigned · **Depends on:** v2 widget exists

## Why

Two reasons to override the global probe settings per-target:

1. **Slow hosts** — some app servers respond in 800ms even when healthy. Global 1500ms timeout is fine, but overriding to 3s for those specific hosts cuts false-positive "DOWN" reports.
2. **High-priority hosts** — the office gateway should be probed every 1s, but a remote backup server only needs 60s. Per-target interval lets users tune without flooding the network.

## Scope

Extend `Target`:

```csharp
public sealed record Target(
    string Id, string Host, string Kind,
    string? Label = null,
    IReadOnlyList<string>? Tags = null,
    bool Enabled = true,
    int? IntervalSecondsOverride = null,   // null = use global
    int? TimeoutMsOverride = null);        // null = default 1500
```

In `MainViewModel`, the probe loop becomes per-target rather than synchronized:

```csharp
// Replace single PeriodicTimer with per-target timers
foreach (var t in Targets) StartTargetLoop(t);

private async Task ProbeOneAsync(TargetStatusViewModel tvm, CancellationToken ct)
{
    var period = TimeSpan.FromSeconds(tvm.Target.IntervalSecondsOverride ?? IntervalSeconds);
    var timeout = TimeSpan.FromMilliseconds(tvm.Target.TimeoutMsOverride ?? 1500);
    using var timer = new PeriodicTimer(period);
    while (!ct.IsCancellationRequested)
    {
        if (tvm.Target.Enabled)
        {
            var result = await _pinger.PingAsync(tvm.Target, timeout, ct);
            Application.Current?.Dispatcher.Invoke(() => tvm.Update(result));
        }
        try { await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { break; }
    }
}
```

In `AddTargetWindow`, add two optional fields (collapsible "ADVANCED" section to keep the dialog uncluttered):
- "INTERVAL OVERRIDE" — empty (use global) or 1–300 seconds
- "TIMEOUT OVERRIDE" — empty (use 1500ms) or 100–10000ms

Display the per-target interval in the row's tooltip when overridden.

## Acceptance criteria

- Per-target overrides persist to `targets.json` (only when set; null fields omitted via `JsonIgnoreCondition.WhenWritingNull`)
- A target with `interval_seconds_override: 1` probes every 1s while others probe at the global rate
- A target with `timeout_ms_override: 3000` waits up to 3s before declaring DOWN
- Editing a target's override restarts only that target's timer (not all of them)
- Adding a new target spawns a new probe loop without disrupting existing ones
- Tooltip on a row with override reads e.g. "Probing every 1s (override) · timeout 3s"

## Files

- `client/src/Pingy.Core/Models/Target.cs` — add overrides
- `client/src/Pingy.Widget/ViewModels/MainViewModel.cs` — restructure probe loop to per-target
- `client/src/Pingy.Widget/AddTargetWindow.xaml` + `.cs` — add advanced fields
- `client/src/Pingy.Widget/MainWindowV2.xaml` — tooltip showing override

## Design notes

- Per-target loops use more CTS sources but eliminate synchronization issues — each target is independent
- Validate ranges in the editor: interval [1, 300]s, timeout [100, 10000]ms
- Show a small "▲" or "★" indicator on rows with active overrides so users can see at a glance which targets diverge from defaults
- Consider showing the effective values (e.g., "every 1s" if overridden) in the row instead of just tooltip — defer

## Out of scope

- Per-target probe protocol (ICMP vs TCP) — separate work alongside task 200
- Time-based interval (faster during business hours) — out of scope
