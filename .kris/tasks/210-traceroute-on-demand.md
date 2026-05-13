# 210 — On-demand traceroute when red

**Status:** pending · **Owner:** unassigned · **Depends on:** v2 widget exists · **Source:** plan §1 row 6 (deferred from v1)

## Why

When a target goes red, the user wants to know "where in the path is it failing?" Per plan §6 cadence, traceroute is **on-demand only** — too heavy to run continuously. This task adds a "TRACE" button on red rows + a result panel showing each hop's RTT.

## Scope

Add a "TRACE" action that appears on red target rows (or always; UX TBD):

- Click → spawns a traceroute via `System.Net.NetworkInformation.Ping.Send` with incrementing TTL
- Each hop returns the responding router's IP + RTT
- Display results in a popup/expandable panel below the row showing each hop
- Cancel button stops the trace mid-flight (use CancellationToken)

Implementation outline:

```csharp
public async IAsyncEnumerable<TraceHop> TraceAsync(string host, int maxHops = 30, [EnumeratorCancellation] CancellationToken ct = default)
{
    using var ping = new Ping();
    for (int ttl = 1; ttl <= maxHops; ttl++) {
        var options = new PingOptions(ttl, true);
        var sw = Stopwatch.StartNew();
        var reply = await ping.SendPingAsync(host, TimeSpan.FromSeconds(2), Array.Empty<byte>(), options, ct);
        sw.Stop();
        yield return new TraceHop(ttl, reply.Address?.ToString(), sw.Elapsed.TotalMilliseconds, reply.Status);
        if (reply.Status == IPStatus.Success) yield break;
    }
}
```

Display: a panel that slides down below the target row showing:
```
Hop  RTT   Address
1    1ms   192.168.1.1
2    8ms   10.0.0.1
3    *     (timeout)
4    24ms  destination.host
```

The panel can also persist last trace result (collapsed) and refresh via a "RE-TRACE" button.

Trace data should be exportable (copy to clipboard) for support tickets.

## Acceptance criteria

- TRACE button visible on each row (or only on red rows — pick one)
- Clicking TRACE runs hop-by-hop probe and updates panel as results stream in
- Each hop shows: TTL, RTT, IP/hostname, status
- Cancel stops the trace cleanly (no orphan tasks)
- Re-trace works without leaks (previous results cleared)
- Maximum hops capped (default 30) to prevent runaway

## Files

- `client/src/Pingy.Core/Probing/ITraceRouter.cs` (new)
- `client/src/Pingy.Core/Probing/TraceRouter.cs` (new — using `System.Net.NetworkInformation.Ping` with TTL)
- `client/src/Pingy.Core/Models/TraceHop.cs` (new)
- `client/src/Pingy.Widget/ViewModels/TargetStatusViewModel.cs` — `TraceCommand`, `TraceHops` ObservableCollection
- `client/src/Pingy.Widget/MainWindowV2.xaml` — TRACE button, results panel template

## Design notes

- WPF doesn't have `IAsyncEnumerable` -> `ObservableCollection` adapter; iterate with `await foreach` and `Application.Current?.Dispatcher.Invoke`
- Trace can take 30s+ if every hop times out — UX should clearly show progress (per-hop loading state)
- `Ping.SendPingAsync` with TTL set on `PingOptions` is the simplest cross-platform-ish way; alternative is raw socket ICMP which needs admin

## Out of scope

- MTR-style continuous traceroute (defer)
- DNS reverse lookup for hop IPs (defer; user can copy IP and lookup elsewhere)
- Saving trace history (defer)
