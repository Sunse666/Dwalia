using System.Diagnostics;
using System.IO;

namespace Dwalia.Infrastructure;

internal static class Logger
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Dwalia", "dwalia.log");

    private static readonly object _lock = new();

    public static void Info(string message) => Log("INFO", message);
    public static void Warn(string message) => Log("WARN", message);
    public static void Error(string message) => Log("ERROR", message);
    public static void Error(string message, Exception ex) => Log("ERROR", $"{message}: {ex}");

    private static void Log(string level, string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var entry = $"[{timestamp}] [{level}] {message}";

        Debug.WriteLine(entry);

        lock (_lock)
        {
            try
            {
                var dir = Path.GetDirectoryName(LogPath)!;
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.AppendAllText(LogPath, entry + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}
