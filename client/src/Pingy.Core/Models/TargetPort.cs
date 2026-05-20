namespace Pingy.Core.Models;

public sealed record TargetPort(int Number, string? Label = null, ServiceCheck? Check = null);
