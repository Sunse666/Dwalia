using System.Diagnostics;
using System.IO;

namespace Dwalia.Infrastructure;

internal static class Logger
{
    private static readonly string LogPath = Path.Combine(
        AppContext.BaseDirectory, "dwalia.log");

    private static readonly object _lock = new();
    private static volatile bool _enabled;
    private const long MaxFileSize = 10 * 1024 * 1024;
    private const int MaxRotations = 3;

    public static bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public static void Info(string message) => Log("INFO", message);
    public static void Warn(string message) => Log("WARN", message);
    public static void Error(string message) => Log("ERROR", message);
    public static void Error(string message, Exception ex) => Log("ERROR", $"{message}: {ex}");

    private static void Log(string level, string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var entry = $"[{timestamp}] [{level}] {message}";

        Debug.WriteLine(entry);

        if (!_enabled) return;

        lock (_lock)
        {
            try
            {
                RotateIfNeeded();
                File.AppendAllText(LogPath, entry + Environment.NewLine);
            }
            catch
            {
            }
        }
    }

    private static void RotateIfNeeded()
    {
        try
        {
            if (!File.Exists(LogPath)) return;
            var info = new FileInfo(LogPath);
            if (info.Length < MaxFileSize) return;

            for (int i = MaxRotations - 1; i >= 0; i--)
            {
                var oldPath = i == 0 ? LogPath : Path.Combine(AppContext.BaseDirectory, $"dwalia.{i}.log");
                var newPath = Path.Combine(AppContext.BaseDirectory, $"dwalia.{i + 1}.log");
                if (File.Exists(oldPath))
                {
                    if (File.Exists(newPath)) File.Delete(newPath);
                    File.Move(oldPath, newPath);
                }
            }
        }
        catch
        {
        }
    }
}
