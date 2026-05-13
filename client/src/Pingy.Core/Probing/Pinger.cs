using System.Net.NetworkInformation;
using Pingy.Core.Models;
using Pingy.Core.Util;

namespace Pingy.Core.Probing;

public sealed class Pinger : IPinger
{
    private static readonly byte[] PayloadBytes = new byte[32];
    private static readonly PingOptions Options = new() { DontFragment = true };

    public async Task<PingResult> PingAsync(Target target, TimeSpan timeout, CancellationToken ct = default)
    {
        var host = HostNormalizer.Normalize(target.Host);
        if (string.IsNullOrEmpty(host))
            return new PingResult(target.Id, false, null, "InvalidHost", DateTimeOffset.UtcNow);

        using var ping = new Ping();
        try
        {
            var reply = await ping.SendPingAsync(host, timeout, PayloadBytes, Options, ct);
            return new PingResult(
                TargetId: target.Id,
                Success: reply.Status == IPStatus.Success,
                RttMs: reply.Status == IPStatus.Success ? reply.RoundtripTime : null,
                Status: reply.Status.ToString(),
                At: DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new PingResult(target.Id, false, null, ex.GetType().Name, DateTimeOffset.UtcNow);
        }
    }
}
