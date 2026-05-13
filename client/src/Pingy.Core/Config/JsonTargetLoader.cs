using System.Text.Json;
using Pingy.Core.Models;

namespace Pingy.Core.Config;

public sealed class JsonTargetLoader : ITargetLoader
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions WriteOpts = new(JsonOpts) { WriteIndented = true };

    public string ConfigPath { get; }

    public JsonTargetLoader(string? overridePath = null)
    {
        ConfigPath = overridePath ?? DefaultPath();
    }

    public static string DefaultPath()
    {
        // Resolve to a path relative to the running executable so the whole folder
        // can be copy-pasted/moved and still find its own data. AppContext.BaseDirectory
        // works for both standard and single-file deployments.
        var exeDir = AppContext.BaseDirectory;
        return Path.Combine(exeDir, "config", "targets.json");
    }

    private static string LegacyAppDataPath()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, "Pingy", "targets.json");
    }

    public async Task<TargetsConfig> LoadAsync(CancellationToken ct = default)
    {
        await EnsureSeededAsync(ct);
        await using var stream = File.OpenRead(ConfigPath);
        var config = await JsonSerializer.DeserializeAsync<TargetsConfig>(stream, JsonOpts, ct);
        return config ?? throw new InvalidOperationException($"Empty or invalid config at {ConfigPath}");
    }

    public async Task SaveAsync(TargetsConfig config, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(ConfigPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = ConfigPath + ".tmp";
        await using (var stream = File.Create(tmp))
            await JsonSerializer.SerializeAsync(stream, config, WriteOpts, ct);

        if (File.Exists(ConfigPath)) File.Replace(tmp, ConfigPath, null);
        else File.Move(tmp, ConfigPath);
    }

    private async Task EnsureSeededAsync(CancellationToken ct)
    {
        if (File.Exists(ConfigPath)) return;

        var dir = Path.GetDirectoryName(ConfigPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        // Migration: copy from the legacy %LOCALAPPDATA%\Pingy\targets.json (one-time) so
        // existing users don't lose their target list when switching to exe-relative storage.
        var legacy = LegacyAppDataPath();
        if (File.Exists(legacy))
        {
            try { File.Copy(legacy, ConfigPath); return; }
            catch { /* fall through to seed defaults */ }
        }

        var sample = new TargetsConfig(
            Version: 1,
            IntervalSeconds: 5,
            Targets: new[]
            {
                new Target(
                    Id: "host-a",
                    Host: "192.168.7.41",
                    Kind: "server",
                    Label: "Host A",
                    Tags: new[] { "server" }),
                new Target(
                    Id: "host-b",
                    Host: "192.168.7.124",
                    Kind: "server",
                    Label: "Host B",
                    Tags: new[] { "server" }),
            });

        await using var stream = File.Create(ConfigPath);
        await JsonSerializer.SerializeAsync(stream, sample, WriteOpts, ct);
    }
}
