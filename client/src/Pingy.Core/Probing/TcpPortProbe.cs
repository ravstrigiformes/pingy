using System.Diagnostics;
using System.Net.Sockets;
using Pingy.Core.Models;
using Pingy.Core.Util;

namespace Pingy.Core.Probing;

public sealed class TcpPortProbe : IPortProbe
{
    public async Task<TcpProbeResult> ProbeAsync(Target target, int port, TimeSpan timeout, CancellationToken ct = default)
    {
        if (port is <= 0 or > 65535)
            return new TcpProbeResult(target.Id, port, false, null, "InvalidPort", DateTimeOffset.UtcNow);

        var host = HostNormalizer.Normalize(target.Host);
        if (string.IsNullOrEmpty(host))
            return new TcpProbeResult(target.Id, port, false, null, "InvalidHost", DateTimeOffset.UtcNow);

        using var client = new TcpClient();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeout);

        var sw = Stopwatch.StartNew();
        try
        {
            await client.ConnectAsync(host, port, linked.Token);
            sw.Stop();
            return new TcpProbeResult(target.Id, port, true, sw.Elapsed.TotalMilliseconds, "Connected", DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new TcpProbeResult(target.Id, port, false, null, "Timeout", DateTimeOffset.UtcNow);
        }
        catch (SocketException ex)
        {
            var status = ex.SocketErrorCode switch
            {
                SocketError.ConnectionRefused => "Refused",
                SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain => "DnsFailure",
                SocketError.TimedOut => "Timeout",
                SocketError.HostUnreachable or SocketError.NetworkUnreachable => "Unreachable",
                _ => ex.SocketErrorCode.ToString(),
            };
            return new TcpProbeResult(target.Id, port, false, null, status, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            return new TcpProbeResult(target.Id, port, false, null, ex.GetType().Name, DateTimeOffset.UtcNow);
        }
    }
}
