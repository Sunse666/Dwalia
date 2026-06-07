using YamlDotNet.Serialization;

namespace Dwalia.Configuration;

public class ConfigRoot
{
    [YamlMember(Alias = "general")]
    public GeneralConfig General { get; set; } = new();

    [YamlMember(Alias = "theme")]
    public ThemeConfig Theme { get; set; } = new();

    [YamlMember(Alias = "layout")]
    public LayoutConfig Layout { get; set; } = new();

    [YamlMember(Alias = "workspaces")]
    public List<WorkspaceEntry> Workspaces { get; set; } = new();

    [YamlMember(Alias = "window_rules")]
    public List<WindowRuleConfig> WindowRules { get; set; } = new();

    [YamlMember(Alias = "widgets")]
    public List<WidgetConfig> Widgets { get; set; } = new();

    [YamlMember(Alias = "keybindings")]
    public List<KeybindingEntry> Keybindings { get; set; } = new();

    [YamlMember(Alias = "launcher")]
    public List<LauncherEntry> Launcher { get; set; } = new();

    [YamlMember(Alias = "autostart")]
    public List<AutostartEntry> Autostart { get; set; } = new();

    [YamlMember(Alias = "monitor")]
    public MonitorConfig Monitor { get; set; } = new();

    [YamlMember(Alias = "resize_mode")]
    public ResizeConfig ResizeMode { get; set; } = new();
}

public class GeneralConfig
{
    [YamlMember(Alias = "launch_terminal")]
    public string LaunchTerminal { get; set; } = "wt.exe";

    [YamlMember(Alias = "excluded_processes")]
    public List<string> ExcludedProcesses { get; set; } = new()
    {
        "SearchApp", "TextInputHost", "SystemSettings",
        "ApplicationFrameHost", "LockApp", "shellexperiencehost"
    };

    [YamlMember(Alias = "excluded_classes")]
    public List<string> ExcludedClasses { get; set; } = new();

    [YamlMember(Alias = "enable_logging")]
    public bool EnableLogging { get; set; } = false;

    [YamlMember(Alias = "enable_swallowing")]
    public bool EnableSwallowing { get; set; } = true;

    [YamlMember(Alias = "animation_enabled")]
    public bool AnimationEnabled { get; set; } = true;

    [YamlMember(Alias = "animation_duration")]
    public int AnimationDuration { get; set; } = 150;

    [YamlMember(Alias = "bar_height")]
    public int BarHeight { get; set; } = 40;

    [YamlMember(Alias = "bar_position")]
    public string BarPosition { get; set; } = "top";

    [YamlMember(Alias = "default_layout")]
    public string DefaultLayout { get; set; } = "Dynamic";

    [YamlMember(Alias = "startup_workspace")]
    public int StartupWorkspace { get; set; } = 0;

    [YamlMember(Alias = "auto_start")]
    public bool AutoStart { get; set; } = false;
}

public class ThemeConfig
{
    [YamlMember(Alias = "background")]
    public string Background { get; set; } = "#1a1b26";

    [YamlMember(Alias = "foreground")]
    public string Foreground { get; set; } = "#c0caf5";

    [YamlMember(Alias = "accent")]
    public string Accent { get; set; } = "#7aa2f7";

    [YamlMember(Alias = "muted")]
    public string Muted { get; set; } = "#565f89";

    [YamlMember(Alias = "taskbar_background")]
    public string TaskbarBackground { get; set; } = "#00ffffff";

    [YamlMember(Alias = "inactive_border")]
    public string InactiveBorder { get; set; } = "#3b4261";

    [YamlMember(Alias = "active_border")]
    public string ActiveBorder { get; set; } = "#7aa2f7";

    [YamlMember(Alias = "border_width")]
    public int BorderWidth { get; set; } = 2;

    [YamlMember(Alias = "enable_acrylic")]
    public bool EnableAcrylic { get; set; } = true;

    [YamlMember(Alias = "focus_active_opacity")]
    public double FocusActiveOpacity { get; set; } = 0.27;

    [YamlMember(Alias = "focus_inactive_opacity")]
    public double FocusInactiveOpacity { get; set; } = 0.09;

    [YamlMember(Alias = "focus_radius")]
    public int FocusRadius { get; set; } = 8;

    [YamlMember(Alias = "focus_fill")]
    public bool FocusFill { get; set; } = true;

    [YamlMember(Alias = "focus_follows_mouse")]
    public bool FocusFollowsMouse { get; set; } = false;

    [YamlMember(Alias = "color_filter")]
    public string ColorFilter { get; set; } = "#7aa2f7";

    [YamlMember(Alias = "color_filter_opacity")]
    public double ColorFilterOpacity { get; set; } = 0.0;

    [YamlMember(Alias = "font_size")]
    public int FontSize { get; set; } = 16;

    [YamlMember(Alias = "bar_font")]
    public string BarFont { get; set; } = "Segoe UI";

    [YamlMember(Alias = "status_show_clock")]
    public bool StatusShowClock { get; set; } = true;

    [YamlMember(Alias = "status_show_cpu")]
    public bool StatusShowCpu { get; set; } = true;

    [YamlMember(Alias = "status_show_mem")]
    public bool StatusShowMem { get; set; } = true;

    [YamlMember(Alias = "status_show_battery")]
    public bool StatusShowBattery { get; set; } = true;

    [YamlMember(Alias = "drag_source_color")]
    public string DragSourceColor { get; set; } = "";

    [YamlMember(Alias = "drag_target_color")]
    public string DragTargetColor { get; set; } = "";

    [YamlMember(Alias = "context_menu_background")]
    public string ContextMenuBackground { get; set; } = "#2d2d2d";

    [YamlMember(Alias = "context_menu_foreground")]
    public string ContextMenuForeground { get; set; } = "#cccccc";

    [YamlMember(Alias = "context_menu_border")]
    public string ContextMenuBorder { get; set; } = "#444444";

    [YamlMember(Alias = "task_button_background")]
    public string TaskButtonBackground { get; set; } = "#1a1a3e";

    [YamlMember(Alias = "task_button_hover_background")]
    public string TaskButtonHoverBackground { get; set; } = "";

    [YamlMember(Alias = "monitor_bar_background")]
    public string MonitorBarBackground { get; set; } = "#00ffffff";

    [YamlMember(Alias = "monitor_bar_border")]
    public string MonitorBarBorder { get; set; } = "";

    [YamlMember(Alias = "workspace_pill_inactive_color")]
    public string WorkspacePillInactive { get; set; } = "";

    [YamlMember(Alias = "workspace_pill_empty_color")]
    public string WorkspacePillEmpty { get; set; } = "";

    [YamlMember(Alias = "workspace_pill_show_count")]
    public bool WorkspacePillShowCount { get; set; } = false;

    [YamlMember(Alias = "date_format")]
    public string DateFormat { get; set; } = "HH:mm:ss  yyyy-MM-dd";

    [YamlMember(Alias = "status_show_network")]
    public bool StatusShowNetwork { get; set; } = true;

    [YamlMember(Alias = "status_show_media")]
    public bool StatusShowMedia { get; set; } = true;

    [YamlMember(Alias = "widget_cpu_color")]
    public string WidgetCpuColor { get; set; } = "#00ff88";

    [YamlMember(Alias = "widget_mem_color")]
    public string WidgetMemColor { get; set; } = "#00ccff";

    [YamlMember(Alias = "widget_battery_color")]
    public string WidgetBatteryColor { get; set; } = "#88ff00";

    [YamlMember(Alias = "widget_network_down_color")]
    public string WidgetNetworkDownColor { get; set; } = "#00ff88";

    [YamlMember(Alias = "widget_network_up_color")]
    public string WidgetNetworkUpColor { get; set; } = "#ff4444";

    [YamlMember(Alias = "widget_media_color")]
    public string WidgetMediaColor { get; set; } = "#ff88ff";

    [YamlMember(Alias = "widget_separator_color")]
    public string WidgetSeparatorColor { get; set; } = "#334466";

    [YamlMember(Alias = "media_dot_playing_color")]
    public string MediaDotPlayingColor { get; set; } = "#00ff88";

    [YamlMember(Alias = "media_dot_paused_color")]
    public string MediaDotPausedColor { get; set; } = "#334466";

    [YamlMember(Alias = "bar_border_color")]
    public string BarBorderColor { get; set; } = "#3b4261";

    [YamlMember(Alias = "pill_corner_radius")]
    public int PillCornerRadius { get; set; } = 14;

    [YamlMember(Alias = "pill_height")]
    public int PillHeight { get; set; } = 30;

    [YamlMember(Alias = "task_pill_corner_radius")]
    public int TaskPillCornerRadius { get; set; } = 14;

    [YamlMember(Alias = "task_pill_height")]
    public int TaskPillHeight { get; set; } = 32;

    [YamlMember(Alias = "task_hover_brighten")]
    public int TaskHoverBrighten { get; set; } = 20;

    [YamlMember(Alias = "progress_filled_char")]
    public string ProgressFilledChar { get; set; } = "▰";

    [YamlMember(Alias = "progress_empty_char")]
    public string ProgressEmptyChar { get; set; } = "▱";

    [YamlMember(Alias = "marquee_speed")]
    public int MarqueeSpeed { get; set; } = 25;

    [YamlMember(Alias = "media_script")]
    public string MediaScript { get; set; } = "";

    [YamlMember(Alias = "media_script_interval")]
    public int MediaScriptInterval { get; set; } = 3;

    [YamlMember(Alias = "widget_pill_background")]
    public string WidgetPillBackground { get; set; } = "#1a1a3e";
}

public class LayoutConfig
{
    [YamlMember(Alias = "inner_gap")]
    public int InnerGap { get; set; } = 4;

    [YamlMember(Alias = "outer_gap")]
    public int OuterGap { get; set; } = 4;

    [YamlMember(Alias = "master_factor")]
    public double MasterFactor { get; set; } = 0.6;

    [YamlMember(Alias = "enabled_layouts")]
    public List<string> EnabledLayouts { get; set; } = new()
    {
        "Dynamic"
    };

    [YamlMember(Alias = "smart_gaps")]
    public bool SmartGaps { get; set; } = false;
}

public class MonitorConfig
{
    [YamlMember(Alias = "monitor_mode")]
    public string MonitorMode { get; set; } = "independent";

    [YamlMember(Alias = "monitor_bar_enabled")]
    public bool MonitorBarEnabled { get; set; } = true;
}

public class ResizeConfig
{
    [YamlMember(Alias = "resize_left")]
    public string ResizeLeft { get; set; } = "H";

    [YamlMember(Alias = "resize_down")]
    public string ResizeDown { get; set; } = "J";

    [YamlMember(Alias = "resize_up")]
    public string ResizeUp { get; set; } = "K";

    [YamlMember(Alias = "resize_right")]
    public string ResizeRight { get; set; } = "L";
}

public class WorkspaceEntry
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";
}

public class WindowRuleConfig
{
    [YamlMember(Alias = "process")]
    public string Process { get; set; } = "";

    [YamlMember(Alias = "title")]
    public string? Title { get; set; }

    [YamlMember(Alias = "title_match_mode")]
    public string TitleMatchMode { get; set; } = "Exact";

    [YamlMember(Alias = "workspace")]
    public string? Workspace { get; set; }

    [YamlMember(Alias = "monitor")]
    public int? Monitor { get; set; }

    [YamlMember(Alias = "floating")]
    public bool Floating { get; set; }

    [YamlMember(Alias = "fullscreen")]
    public bool Fullscreen { get; set; }

    [YamlMember(Alias = "sticky")]
    public bool Sticky { get; set; }

    [YamlMember(Alias = "ignore")]
    public bool Ignore { get; set; }

    [YamlMember(Alias = "layout")]
    public string? Layout { get; set; }
}

public class KeybindingEntry
{
    [YamlMember(Alias = "command")]
    public string Command { get; set; } = "";

    [YamlMember(Alias = "binding")]
    public string Binding { get; set; } = "";
}

public class LauncherEntry
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "path")]
    public string Path { get; set; } = "";
}

public class AutostartEntry
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "command")]
    public string Command { get; set; } = "";
}
