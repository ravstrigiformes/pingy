using Pingy.Core.Models;

namespace Pingy.Core.Probing;

public interface IServiceCheck
{
    Task<ServiceCheckResult> CheckAsync(Target target, TargetPort port, TimeSpan timeout, CancellationToken ct = default);
}

// L7 check outcome. Ok = response matched expectations. RttMs is the HTTP response
// time; null on failure. Status carries the HTTP code or a failure label.
public readonly record struct ServiceCheckResult(bool Ok, double? RttMs, string Status);
