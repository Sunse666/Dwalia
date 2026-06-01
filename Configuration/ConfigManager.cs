using System.IO;
using System.Text.Json;
using Dwalia.Infrastructure;

namespace Dwalia.Configuration;

public class ConfigManager
{
    private static readonly string ConfigPath = Path.Combine(
        AppContext.BaseDirectory, "config.json");

    public DwaliaConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var config = JsonSerializer.Deserialize<DwaliaConfig>(json);
                if (config != null)
                {
                    Logger.Info($"Config loaded from {ConfigPath}");
                    return config;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to load config: {ex.Message}. Using defaults.");
        }

        Logger.Info("Using default configuration");
        return GetDefaults();
    }

    public void Save(DwaliaConfig config)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
            Logger.Info($"Config saved to {ConfigPath}");
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to save config", ex);
        }
    }

    public static DwaliaConfig GetDefaults()
    {
        return new DwaliaConfig
        {
            Theme = new ThemeConfig(),
            Layout = new LayoutConfig
            {
                EnabledLayouts = new[] { "MasterStack", "Monocle", "Grid", "HorizontalStack", "Columns", "VerticalStack", "BSP" }
            },
            Workspaces = new WorkspaceConfig(),
            ExcludeProcesses = new[]
            {
                "shellexperiencehost", "SearchApp",
                "TextInputHost", "SystemSettings", "ApplicationFrameHost", "LockApp"
            },
            Rules = Array.Empty<WindowRuleConfig>(),
            LaunchTerminal = "wt.exe"
        };
    }

    public void ApplyRules(DwaliaConfig config, Managers.WindowManager windowManager,
        Managers.WorkspaceManager workspaceManager)
    {
        foreach (var mw in windowManager.ManagedWindows.Values)
        {
            foreach (var rule in config.Rules)
            {
                var normalizedRule = rule.Process.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? rule.Process[..^4] : rule.Process;
                if (mw.ProcessName.Equals(normalizedRule, StringComparison.OrdinalIgnoreCase))
                {
                    if (rule.Workspace.HasValue)
                    {
                        workspaceManager.MoveWindowToWorkspace(mw, rule.Workspace.Value);
                        Logger.Info($"Rule applied: {mw.ProcessName} -> workspace {rule.Workspace}");
                    }
                    if (rule.Floating.HasValue && rule.Floating.Value)
                    {
                        mw.State = Models.WindowLayoutState.Floating;
                    }
                    break;
                }
            }
        }
    }
}
