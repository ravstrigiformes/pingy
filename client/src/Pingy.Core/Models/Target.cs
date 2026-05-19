namespace Pingy.Core.Models;

public sealed record Target(
    string Id,
    string Host,
    string Kind,
    string? Label = null,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyList<TargetPort>? Ports = null);
