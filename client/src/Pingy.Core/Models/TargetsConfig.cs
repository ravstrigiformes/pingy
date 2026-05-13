namespace Pingy.Core.Models;

public sealed record TargetsConfig(int Version, int IntervalSeconds, IReadOnlyList<Target> Targets);
