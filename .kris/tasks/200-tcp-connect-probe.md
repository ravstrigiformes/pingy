# 200 — TCP-connect probe alongside ICMP

**Status:** pending · **Owner:** unassigned · **Depends on:** v2 widget exists · **Source:** plan §11 standing revision #3 (locked)

## Why

Plan §11 locked this revision in W1 because **ICMP-only diagnosis lies on networks that rate-limit or de-prioritize ICMP** under load. A TCP-connect probe (e.g., to port 443 or 445) catches the case "ICMP shows red, but TCP traffic is actually fine." Without this, IT loses faith in the dot within a month.

## Scope

Extend `Target` (already has `port` capability via the `port` field on `targets` table per plan §5 schema):

```csharp
public sealed record Target(
    // ... existing ...,
    int? Port = null);  // when set, run TCP-connect probe alongside ICMP
```

Create a new probe in `Pingy.Core.Probing`:

```csharp
public interface ITcpProbe {
    Task<PingResult> ProbeAsync(string host, int port, TimeSpan timeout, CancellationToken ct = default);
}

public sealed class TcpProbe : ITcpProbe {
    public async Task<PingResult> ProbeAsync(string host, int port, TimeSpan timeout, CancellationToken ct = default) {
        var sw = Stopwatch.StartNew();
        try {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, ct).WaitAsync(timeout, ct);
            sw.Stop();
            return new PingResult($"{host}:{port}", true, sw.Elapsed.TotalMilliseconds, "TcpConnect", DateTimeOffset.UtcNow);
        } catch (OperationCanceledException) { throw; }
        catch (Exception ex) {
            return new PingResult($"{host}:{port}", false, null, ex.GetType().Name, DateTimeOffset.UtcNow);
        }
    }
}
```

In `MainViewModel.ProbeAllAsync`, run both probes when `Port` is set, and merge results:

- If ICMP succeeds AND TCP succeeds → state UP, RTT = ICMP RTT
- If ICMP fails BUT TCP succeeds → state UP-TCP-ONLY (yellow indicator), RTT = TCP connect time
- If ICMP succeeds BUT TCP fails → state UP-ICMP-ONLY (yellow), RTT = ICMP RTT
- If both fail → state DOWN

Add a new state badge `TCP-OK` or `ICMP-OK` for partial states.

In `AddTargetWindow`, add a "PORT (optional, enables TCP probe)" field.

## Acceptance criteria

- Targets with Port set probe both ICMP and TCP every interval
- Mixed states (ICMP up, TCP down) display correctly with yellow indicator
- TCP probe respects the same timeout as ICMP (or a separate `TcpTimeoutMs` if user sets it)
- Health rollup (top-left indicator) correctly handles partial-up states
- `targets.json` persists `port` field

## Files

- `client/src/Pingy.Core/Models/Target.cs` — add `Port`
- `client/src/Pingy.Core/Probing/ITcpProbe.cs` (new)
- `client/src/Pingy.Core/Probing/TcpProbe.cs` (new)
- `client/src/Pingy.Widget/ViewModels/MainViewModel.cs` — inject TcpProbe + run both probes
- `client/src/Pingy.Widget/ViewModels/TargetStatusViewModel.cs` — add tri-state status (UP / DOWN / PARTIAL)
- `client/src/Pingy.Widget/AddTargetWindow.xaml` + `.cs` — Port field

## Design notes

- TCP-connect probe doesn't send any application data — just opens the TCP handshake and closes
- For HTTP/HTTPS hosts, port 443 is the safest default (most servers accept TCP on 443 even if HTTP fails)
- For Windows file servers, port 445 (SMB) is typical
- Be cautious about probing ports IDS/IPS systems might flag (e.g., 22 SSH brute-force detection) — let users pick

## Out of scope

- HTTP probe (separate task — actually sends a request and checks for 2xx response)
- TLS handshake validation (just establishes TCP, doesn't verify cert)
- DNS-only probe (separate task)
