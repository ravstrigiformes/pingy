namespace Pingy.Core.Models;

public sealed record PortProbeResult(
    string TargetId,
    int Port,
    bool Success,
    double? RttMs,
    string Status,
    DateTimeOffset At);
