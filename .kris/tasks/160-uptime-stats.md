# 160 — Uptime % and last-failure timestamp per target

**Status:** pending · **Owner:** unassigned · **Depends on:** v2 widget exists · **Effort:** medium

## Why

"Is host X reliable?" needs more than a current dot. Tracking uptime % over a window (e.g., last 1 hour, last 24 hours) and the last-failure timestamp gives users incident-context at a glance.

## Scope

Extend `TargetStatusViewModel` with:

```csharp
[ObservableProperty] private double _uptimePercent1h;
[ObservableProperty] private double _uptimePercent24h;
[ObservableProperty] private DateTimeOffset? _lastFailureAt;
[ObservableProperty] private string _uptimeDisplay = "—";
```

Compute from the existing samples queue. For 1-hour window, also need a longer-running buffer than the current 60 samples (which only covers ~5 min at 5s interval). Add a separate timestamped sample log:

```csharp
private readonly LinkedList<(DateTimeOffset at, bool ok)> _statusLog = new();
private const int StatusLogMaxAgeHours = 25;

// In Update():
_statusLog.AddLast((result.At, result.Success));
while (_statusLog.First is not null && (DateTimeOffset.UtcNow - _statusLog.First.Value.at).TotalHours > StatusLogMaxAgeHours)
    _statusLog.RemoveFirst();
RecomputeUptime();
```

`RecomputeUptime`:
- Filter the log to last 1h and last 24h windows
- `UptimePercent = (successes / total) * 100`
- `LastFailureAt = last entry where !ok`

Display: extend the row tooltip OR add a small text below the badge:
```
UP 1h: 99.5% / 24h: 97.2% · last fail 12m ago
```

Or, more compact: a tiny secondary stat line in the row (only when graph is on, to keep dot-strip mode clean).

## Acceptance criteria

- Uptime % computed correctly over 1h and 24h windows
- Last-failure timestamp updates when target goes DOWN, persists after recovery
- Memory stays bounded (status log is auto-trimmed past 25h)
- Tooltip shows uptime when hovering a target row
- Doesn't double-count samples or skip when there's a gap (e.g., during sleep/wake)

## Files

- `client/src/Pingy.Widget/ViewModels/TargetStatusViewModel.cs` — add status log + uptime computation
- `client/src/Pingy.Widget/MainWindowV2.xaml` — extend tooltip / row display

## Design notes

- 25h cap (not 24h exactly) gives a small buffer so the 24h window always has full data even right at boundary
- `LinkedList<T>` is used for O(1) front-removal during trim; `Queue<T>` would also work
- Uptime calc could weight by interval if intervals vary per target (task 120) — defer that complexity until needed
- Consider writing the status log to disk so uptime survives app restart (defer — adds disk I/O complexity)

## Out of scope

- Persisting uptime stats across app restarts (defer)
- Notification when uptime drops below threshold (defer)
- Export uptime as CSV (defer)
