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
                Validate(config);
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

    public void Validate(ConfigRoot config)
    {
        if (config == null) return;
        var g = config.General; var t = config.Theme; var l = config.Layout;

        if (g.BarHeight < 16 || g.BarHeight > 80)
        { Logger.Warn($"bar_height {g.BarHeight} out of range [16-80], clamped"); g.BarHeight = Math.Clamp(g.BarHeight, 16, 80); }
        if (g.BarPosition is not "top" and not "bottom")
        { Logger.Warn($"bar_position '{g.BarPosition}' invalid, using 'top'"); g.BarPosition = "top"; }
        if (g.AnimationDuration < 0 || g.AnimationDuration > 2000)
        { Logger.Warn($"animation_duration {g.AnimationDuration} out of range, clamped"); g.AnimationDuration = Math.Clamp(g.AnimationDuration, 0, 2000); }
        if (!Enum.TryParse<Managers.LayoutType>(g.DefaultLayout, true, out _))
        { Logger.Warn($"default_layout '{g.DefaultLayout}' invalid, using 'Dynamic'"); g.DefaultLayout = "Dynamic"; }

        if (t.FontSize < 8 || t.FontSize > 24)
        { Logger.Warn($"font_size {t.FontSize} out of range [8-24], clamped"); t.FontSize = Math.Clamp(t.FontSize, 8, 24); }
        if (t.BorderWidth < 1 || t.BorderWidth > 8)
        { Logger.Warn($"border_width {t.BorderWidth} out of range [1-8], clamped"); t.BorderWidth = Math.Clamp(t.BorderWidth, 1, 8); }
        if (t.FocusActiveOpacity < 0 || t.FocusActiveOpacity > 1)
        { Logger.Warn($"focus_active_opacity {t.FocusActiveOpacity} out of range, clamped"); t.FocusActiveOpacity = Math.Clamp(t.FocusActiveOpacity, 0, 1); }
        if (t.FocusInactiveOpacity < 0 || t.FocusInactiveOpacity > 1)
        { Logger.Warn($"focus_inactive_opacity {t.FocusInactiveOpacity} out of range, clamped"); t.FocusInactiveOpacity = Math.Clamp(t.FocusInactiveOpacity, 0, 1); }
        if (t.ColorFilterOpacity < 0 || t.ColorFilterOpacity > 1)
        { Logger.Warn($"color_filter_opacity {t.ColorFilterOpacity} out of range, clamped"); t.ColorFilterOpacity = Math.Clamp(t.ColorFilterOpacity, 0, 1); }
        if (t.PillCornerRadius < 0 || t.PillCornerRadius > 40)
        { Logger.Warn($"pill_corner_radius {t.PillCornerRadius} out of range, clamped"); t.PillCornerRadius = Math.Clamp(t.PillCornerRadius, 0, 40); }
        if (t.PillHeight < 10 || t.PillHeight > 80)
        { Logger.Warn($"pill_height {t.PillHeight} out of range, clamped"); t.PillHeight = Math.Clamp(t.PillHeight, 10, 80); }
        if (t.MarqueeSpeed < 5 || t.MarqueeSpeed > 200)
        { Logger.Warn($"marquee_speed {t.MarqueeSpeed} out of range, clamped"); t.MarqueeSpeed = Math.Clamp(t.MarqueeSpeed, 5, 200); }
        if (t.MediaScriptInterval < 1 || t.MediaScriptInterval > 60)
        { Logger.Warn($"media_script_interval {t.MediaScriptInterval} out of range, clamped"); t.MediaScriptInterval = Math.Clamp(t.MediaScriptInterval, 1, 60); }

        if (l.MasterFactor < 0.2 || l.MasterFactor > 0.85)
        { Logger.Warn($"master_factor {l.MasterFactor} out of range [0.2-0.85], clamped"); l.MasterFactor = Math.Clamp(l.MasterFactor, 0.2, 0.85); }
        if (l.InnerGap < 0 || l.InnerGap > 24)
        { Logger.Warn($"inner_gap {l.InnerGap} out of range [0-24], clamped"); l.InnerGap = Math.Clamp(l.InnerGap, 0, 24); }
        if (l.OuterGap < 0 || l.OuterGap > 12)
        { Logger.Warn($"outer_gap {l.OuterGap} out of range [0-12], clamped"); l.OuterGap = Math.Clamp(l.OuterGap, 0, 12); }

        var validLayouts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "Dynamic","MasterStack","Monocle","Grid","HorizontalStack","Columns","VerticalStack","BSP" };
        l.EnabledLayouts.RemoveAll(name => { if (!validLayouts.Contains(name)) { Logger.Warn($"Unknown layout '{name}' removed from enabled_layouts"); return true; } return false; });
        if (l.EnabledLayouts.Count == 0) { l.EnabledLayouts.Add("Dynamic"); Logger.Warn("enabled_layouts was empty, added Dynamic"); }

        var validBarPages = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "All","Docker","Basic","Advanced" };
        foreach (var w in config.Widgets)
        {
            if (!validBarPages.Contains(w.BarPage))
            { Logger.Warn($"Widget '{w.Type}': unknown bar_page '{w.BarPage}', using 'All'"); w.BarPage = "All"; }
            if (w.Align is not "left" and not "center" and not "right")
            { Logger.Warn($"Widget '{w.Type}': unknown align '{w.Align}', using 'right'"); w.Align = "right"; }
        }

        foreach (var rule in config.WindowRules)
        {
            if (rule.TitleMatchMode is { Length: > 0 } mode
                && mode is not "Exact" and not "Contains" and not "StartsWith" and not "Regex")
            { Logger.Warn($"Window rule '{rule.Process}': unknown title_match_mode '{mode}', using 'Exact'"); rule.TitleMatchMode = "Exact"; }
        }
    }

    public static ConfigRoot GetDefaults()
    {
        return new ConfigRoot
        {
            Widgets = GetDefaultWidgets(),
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
            Autostart = new List<AutostartEntry>
            {
                new() { Name = "Terminal", Command = "wt.exe" },
            },
        };
    }

    public static List<WidgetConfig> GetDefaultWidgets()
    {
        return new List<WidgetConfig>
        {
            new() { Type = "workspace",  BarPage = "All",   Align = "left",   Order = 1 },
            new() { Type = "cpu",       BarPage = "Info",  Align = "right",  Order = 4 },
            new() { Type = "memory",    BarPage = "Info",  Align = "right",  Order = 5 },
            new() { Type = "battery",   BarPage = "Info",  Align = "right",  Order = 6 },
            new() { Type = "network",   BarPage = "Info",  Align = "right",  Order = 3 },
            new() { Type = "media",     BarPage = "Info",  Align = "right",  Order = 7 },
            new() { Type = "clock",     BarPage = "Info",  Align = "center", Order = 1 },
            new() { Type = "active_window", BarPage = "Info", Align = "left", Order = 2 },
            new() { Type = "layout",    BarPage = "Docker", Align = "right",  Order = 2 },
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
            if (rule.Fullscreen)
                mw.State = Models.WindowLayoutState.Fullscreen;
            if (rule.Sticky)
                mw.IsSticky = true;
            if (rule.Layout is { Length: > 0 } layoutName
                && Enum.TryParse<Managers.LayoutType>(layoutName, true, out var ltype)
                && ServiceLocator.TryResolve<Managers.LayoutManager>(out var lm))
            {
                var mwWs = workspaceManager.Workspaces.FirstOrDefault(w => w.Id == mw.WorkspaceId);
                if (mwWs != null) mwWs.Layout = ltype;
            }
            break;
        }
    }

    public static bool ShouldIgnore(ConfigRoot config, string processName, string title)
    {
        foreach (var rule in config.WindowRules)
        {
            if (!rule.Ignore) continue;

            bool processMatch = string.IsNullOrEmpty(rule.Process)
                || processName.Equals(
                    rule.Process.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                        ? rule.Process[..^4] : rule.Process,
                    StringComparison.OrdinalIgnoreCase);

            if (!processMatch) continue;

            if (rule.Title == null)
                return true;

            bool titleMatch = rule.TitleMatchMode.ToLowerInvariant() switch
            {
                "contains" => title.Contains(rule.Title, StringComparison.OrdinalIgnoreCase),
                "startswith" => title.StartsWith(rule.Title, StringComparison.OrdinalIgnoreCase),
                "regex" => MatchTitleRegex(title, rule.Title),
                _ => title.Equals(rule.Title, StringComparison.OrdinalIgnoreCase),
            };

            if (titleMatch) return true;
        }
        return false;
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
