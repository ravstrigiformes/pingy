using Pingy.Core.Models;

namespace Pingy.Core.Config;

public interface ITargetLoader
{
    string ConfigPath { get; }
    Task<TargetsConfig> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(TargetsConfig config, CancellationToken ct = default);
}
