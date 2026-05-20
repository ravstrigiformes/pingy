namespace Pingy.Core.Models;

// Tri-state port health. Down = TCP refused/timeout; Degraded = TCP open but the
// L7 check failed; Ok = TCP open and (if configured) L7 passed. Ordered so a
// higher value is healthier.
public enum PortHealth
{
    Unknown = 0,
    Down,
    Degraded,
    Ok,
}

// Combined L4 + optional L7 result emitted by the probe orchestrator.
public sealed record PortProbeResult(
    string TargetId,
    int Port,
    PortHealth Health,
    double? RttMs,        // TCP connect RTT
    double? L7RttMs,      // HTTP response time; null when no L7 check ran
    string Status,        // L4 status ("Connected" | "Refused" | "Timeout" | ...)
    string? L7Status,     // L7 status ("200" | "500" | "CertInvalid" | "Timeout"); null when no L7 ran
    DateTimeOffset At);
