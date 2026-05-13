namespace Pingy.Core.Models;

public sealed record PingResult(string TargetId, bool Success, double? RttMs, string Status, DateTimeOffset At);
