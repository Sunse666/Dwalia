namespace Dwalia.Configuration;

public class DwaliaConfig
{
    public string ModKey { get; set; } = "Alt+Ctrl";
    public ThemeConfig Theme { get; set; } = new();
    public LayoutConfig Layout { get; set; } = new();
    public WorkspaceConfig Workspaces { get; set; } = new();
    public string[] ExcludeProcesses { get; set; } = Array.Empty<string>();
    public WindowRuleConfig[] Rules { get; set; } = Array.Empty<WindowRuleConfig>();
    public string LaunchTerminal { get; set; } = "wt.exe";
}

public class ThemeConfig
{
    public string Background { get; set; } = "#1a1b26";
    public string Foreground { get; set; } = "#c0caf5";
    public string Accent { get; set; } = "#7aa2f7";
    public string InactiveBorder { get; set; } = "#3b4261";
    public string ActiveBorder { get; set; } = "#7aa2f7";
    public int BorderWidth { get; set; } = 2;
    public int TitleBarHeight { get; set; } = 28;
    public string FontFamily { get; set; } = "Segoe UI";
    public int FontSize { get; set; } = 12;
}

public class LayoutConfig
{
    public int MasterCount { get; set; } = 1;
    public double MasterFactor { get; set; } = 0.6;
    public int InnerGap { get; set; } = 4;
    public int OuterGap { get; set; } = 4;
}

public class WorkspaceConfig
{
    public int Count { get; set; } = 5;
    public string[] Names { get; set; } = new[] { "1:Term", "2:Code", "3:Web", "4:Comm", "5:Misc" };
}

public class WindowRuleConfig
{
    public string Process { get; set; } = string.Empty;
    public int? Workspace { get; set; }
    public bool? Floating { get; set; }
}
