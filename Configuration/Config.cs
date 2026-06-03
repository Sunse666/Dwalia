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

    [YamlMember(Alias = "keybindings")]
    public List<KeybindingEntry> Keybindings { get; set; } = new();

    [YamlMember(Alias = "launcher")]
    public List<LauncherEntry> Launcher { get; set; } = new();
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

    [YamlMember(Alias = "enable_logging")]
    public bool EnableLogging { get; set; } = false;
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
    public string TaskbarBackground { get; set; } = "#5516161e";

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

    [YamlMember(Alias = "color_filter")]
    public string ColorFilter { get; set; } = "#7aa2f7";

    [YamlMember(Alias = "color_filter_opacity")]
    public double ColorFilterOpacity { get; set; } = 0.0;
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
        "MasterStack", "Monocle", "Grid", "HorizontalStack",
        "Columns", "VerticalStack", "BSP"
    };

    [YamlMember(Alias = "smart_gaps")]
    public bool SmartGaps { get; set; } = false;
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

    [YamlMember(Alias = "floating")]
    public bool Floating { get; set; }
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
