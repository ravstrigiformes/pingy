using System.IO;
using System.Text;

namespace Pingy.Widget;

// Portable crash log: writes alongside the .exe (matches the targets.json layout convention)
// so a copy-paste of the publish folder takes its logs with it. Single global lock; failures
// to log are themselves swallowed — never let the logger be the thing that crashes the app.
internal static class CrashLogger
{
    private static readonly object Gate = new();

    public static string LogPath { get; } =
        Path.Combine(AppContext.BaseDirectory, "logs", "crash.log");

    public static void Log(string source, Exception? ex, string? extra = null)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            sb.Append('[').Append(DateTimeOffset.Now.ToString("u")).Append("] ");
            sb.Append(source);
            if (!string.IsNullOrEmpty(extra)) sb.Append(" — ").Append(extra);
            sb.AppendLine();
            if (ex is not null)
            {
                sb.AppendLine(ex.ToString());
            }
            sb.AppendLine(new string('-', 60));

            lock (Gate)
            {
                File.AppendAllText(LogPath, sb.ToString());
            }
        }
        catch
        {
            // Logging must never crash the app.
        }
    }
}
