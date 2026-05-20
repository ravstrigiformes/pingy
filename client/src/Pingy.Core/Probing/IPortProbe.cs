using Pingy.Core.Models;

namespace Pingy.Core.Probing;

public interface IPortProbe
{
    Task<TcpProbeResult> ProbeAsync(Target target, int port, TimeSpan timeout, CancellationToken ct = default);
}
