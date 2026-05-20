using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pingy.Widget.Settings;

// Loads/saves AppSettings to config/settings.json next to the .exe — the same
// portable layout as targets.json (AppContext.BaseDirectory). Failures degrade
// gracefully to defaults; the widget must never crash over a settings file.
internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public string FilePath { get; }

    public SettingsStore(string? overridePath = null)
    {
        FilePath = overridePath ?? Path.Combine(AppContext.BaseDirectory, "config", "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        }
        catch (System.Exception ex)
        {
            CrashLogger.Log("settings.load", ex);
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));

            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
            else File.Move(tmp, FilePath);
        }
        catch (System.Exception ex)
        {
            CrashLogger.Log("settings.save", ex);
        }
    }
}
