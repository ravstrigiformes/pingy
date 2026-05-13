using Pingy.Core.Models;

namespace Pingy.Core.Probing;

public interface IPinger
{
    Task<PingResult> PingAsync(Target target, TimeSpan timeout, CancellationToken ct = default);
}
