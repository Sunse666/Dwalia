using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Dwalia.Infrastructure;

namespace Dwalia.Configuration;

public class ConfigManager
{
    private static readonly string ConfigPath = Path.Combine(
        AppContext.BaseDirectory, "config.yaml");

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    private FileSystemWatcher? _watcher;
    private System.Timers.Timer? _debounceTimer;
    private Action? _onConfigChanged;
    private readonly object _watcherLock = new();
    private DateTime _lastInternalSave = DateTime.MinValue;

    public void StartWatching(Action onConfigChanged)
    {
        lock (_watcherLock)
        {
            StopWatching();
            _onConfigChanged = onConfigChanged;

            var dir = Path.GetDirectoryName(ConfigPath);
            var file = Path.GetFileName(ConfigPath);
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(file)) return;

            _debounceTimer = new System.Timers.Timer(300) { AutoReset = false };
            _debounceTimer.Elapsed += (_, _) =>
            {
                Logger.Info("config.yaml changed, auto-reloading...");
                _onConfigChanged?.Invoke();
            };

            _watcher = new FileSystemWatcher(dir, file)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _watcher.Changed += (_, _) =>
            {
                if ((DateTime.UtcNow - _lastInternalSave).TotalMilliseconds < 500)
                    return;
                _debounceTimer?.Stop();
                _debounceTimer?.Start();
            };
            Logger.Info("Config file watcher started");
        }
    }

    public void StopWatching()
    {
        lock (_watcherLock)
        {
            _debounceTimer?.Stop();
            _debounceTimer?.Dispose();
            _debounceTimer = null;
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    public ConfigRoot Load()
    {
        if (!File.Exists(ConfigPath))
        {
            Logger.Info("config.yaml not found, generating defaults...");
            var defaults = GetDefaults();
            Save(defaults);
            return defaults;
        }

        try
        {
            var yaml = File.ReadAllText(ConfigPath);
            var config = Deserializer.Deserialize<ConfigRoot>(yaml);
            if (config != null)
            {
                Logger.Info($"Config loaded from {ConfigPath}");
                return config;
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to load config: {ex.Message}. Using defaults.");
        }

        return GetDefaults();
    }

    public void Save(ConfigRoot config)
    {
        try
        {
            _lastInternalSave = DateTime.UtcNow;
            var yaml = Serializer.Serialize(config);
            File.WriteAllText(ConfigPath, yaml);
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to save config", ex);
        }
    }

    public static ConfigRoot GetDefaults()
    {
        return new ConfigRoot
        {
            General = new GeneralConfig(),
            Theme = new ThemeConfig(),
            Layout = new LayoutConfig(),
            Workspaces = new List<WorkspaceEntry>
            {
                new() { Name = "1: Term" },
                new() { Name = "2: Code" },
                new() { Name = "3: Web" },
                new() { Name = "4: Comm" },
                new() { Name = "5: Misc" },
            },
            Launcher = new List<LauncherEntry>
            {
                new() { Name = "Terminal", Path = "wt.exe" },
                new() { Name = "Chrome", Path = "chrome.exe" },
                new() { Name = "VS Code", Path = "code" },
                new() { Name = "Explorer", Path = "explorer.exe" },
            },
            Keybindings = GetDefaultKeybindings(),
        };
    }

    public static List<KeybindingEntry> GetDefaultKeybindings()
    {
        return new List<KeybindingEntry>
        {
            new() { Command = "focus_down", Binding = "Alt+J" },
            new() { Command = "focus_up", Binding = "Alt+K" },
            new() { Command = "focus_left", Binding = "Alt+H" },
            new() { Command = "focus_right", Binding = "Alt+L" },
            new() { Command = "swap_down", Binding = "Alt+Shift+J" },
            new() { Command = "swap_up", Binding = "Alt+Shift+K" },
            new() { Command = "swap_left", Binding = "Alt+Shift+H" },
            new() { Command = "swap_right", Binding = "Alt+Shift+L" },
            new() { Command = "toggle_fullscreen", Binding = "Alt+F" },
            new() { Command = "cycle_layout", Binding = "Alt+T" },
            new() { Command = "toggle_float", Binding = "Alt+Shift+Space" },
            new() { Command = "close_window", Binding = "Alt+Q" },
            new() { Command = "quit", Binding = "Alt+Shift+Q" },
            new() { Command = "dec_gap", Binding = "Alt+OemComma" },
            new() { Command = "inc_gap", Binding = "Alt+OemPeriod" },
            new() { Command = "resize_left", Binding = "Alt+Ctrl+H" },
            new() { Command = "resize_down", Binding = "Alt+Ctrl+J" },
            new() { Command = "resize_up", Binding = "Alt+Ctrl+K" },
            new() { Command = "resize_right", Binding = "Alt+Ctrl+L" },
            new() { Command = "focus_1", Binding = "Alt+1" },
            new() { Command = "focus_2", Binding = "Alt+2" },
            new() { Command = "focus_3", Binding = "Alt+3" },
            new() { Command = "focus_4", Binding = "Alt+4" },
            new() { Command = "focus_5", Binding = "Alt+5" },
            new() { Command = "focus_6", Binding = "Alt+6" },
            new() { Command = "focus_7", Binding = "Alt+7" },
            new() { Command = "focus_8", Binding = "Alt+8" },
            new() { Command = "focus_9", Binding = "Alt+9" },
            new() { Command = "workspace_1", Binding = "Alt+Shift+1" },
            new() { Command = "workspace_2", Binding = "Alt+Shift+2" },
            new() { Command = "workspace_3", Binding = "Alt+Shift+3" },
            new() { Command = "workspace_4", Binding = "Alt+Shift+4" },
            new() { Command = "workspace_5", Binding = "Alt+Shift+5" },
            new() { Command = "workspace_next", Binding = "Alt+Shift+Right" },
            new() { Command = "workspace_previous", Binding = "Alt+Shift+Left" },
            new() { Command = "move_to_workspace_next", Binding = "Alt+Shift+N" },
            new() { Command = "move_to_workspace_previous", Binding = "Alt+Shift+M" },
            new() { Command = "launch_terminal", Binding = "Alt+Enter" },
            new() { Command = "toggle_bar", Binding = "Alt+U" },
            new() { Command = "bar_next", Binding = "Alt+Shift+Down" },
            new() { Command = "bar_previous", Binding = "Alt+Shift+Up" },
            new() { Command = "reload_config", Binding = "Alt+Shift+R" },
        };
    }

    public void ApplyRules(ConfigRoot config, Managers.WindowManager windowManager,
        Managers.WorkspaceManager workspaceManager)
    {
        foreach (var mw in windowManager.ManagedWindows.Values)
            ApplyRulesToWindow(config, workspaceManager, mw);
    }

    public void ApplyRulesToWindow(ConfigRoot config,
        Managers.WorkspaceManager workspaceManager, Models.ManagedWindow mw)
    {
        foreach (var rule in config.WindowRules)
        {
            bool processMatch = string.IsNullOrEmpty(rule.Process)
                || mw.ProcessName.Equals(
                    rule.Process.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                        ? rule.Process[..^4] : rule.Process,
                    StringComparison.OrdinalIgnoreCase);

            bool titleMatch = true;
            if (!string.IsNullOrEmpty(rule.Title))
            {
                titleMatch = rule.TitleMatchMode.ToLowerInvariant() switch
                {
                    "contains" => mw.Title.Contains(rule.Title, StringComparison.OrdinalIgnoreCase),
                    "startswith" => mw.Title.StartsWith(rule.Title, StringComparison.OrdinalIgnoreCase),
                    "regex" => MatchTitleRegex(mw.Title, rule.Title),
                    _ => mw.Title.Equals(rule.Title, StringComparison.OrdinalIgnoreCase),
                };
            }

            if (!processMatch || !titleMatch)
                continue;

            if (rule.Workspace is { Length: > 0 } wsName)
            {
                var ws = workspaceManager.Workspaces.FirstOrDefault(
                    w => w.Name.Equals(wsName, StringComparison.OrdinalIgnoreCase));
                if (ws != null)
                {
                    workspaceManager.MoveWindowToWorkspace(mw, ws.Id);
                    Logger.Info($"Rule: {mw.ProcessName} -> {ws.Name}");
                }
            }
            if (rule.Floating)
                mw.State = Models.WindowLayoutState.Floating;
            break;
        }
    }

    private static bool MatchTitleRegex(string title, string pattern)
    {
        try
        {
            return System.Text.RegularExpressions.Regex.IsMatch(
                title, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        catch (System.Text.RegularExpressions.RegexParseException ex)
        {
            Logger.Warn($"Invalid regex in window rule: {ex.Message}");
            return false;
        }
    }
}
