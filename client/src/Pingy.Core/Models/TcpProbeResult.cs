namespace Pingy.Core.Models;

// Layer-4 only result: did the TCP handshake complete. The orchestrator
// (MainViewModel.ProbePortAsync) combines this with an optional L7 ServiceCheck
// into the richer PortProbeResult.
public sealed record TcpProbeResult(
    string TargetId,
    int Port,
    bool Connected,
    double? RttMs,
    string Status,
    DateTimeOffset At);
