using Pingy.Core.Models;

namespace Pingy.Core.Probing;

public interface IPortProbe
{
    Task<PortProbeResult> ProbeAsync(Target target, int port, TimeSpan timeout, CancellationToken ct = default);
}
