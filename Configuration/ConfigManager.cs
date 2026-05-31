using System.IO;
using System.Text.Json;
using Dwalia.Infrastructure;

namespace Dwalia.Configuration;

public class ConfigManager
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dwalia");
    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

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
            if (!Directory.Exists(ConfigDir))
                Directory.CreateDirectory(ConfigDir);

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
            ModKey = "Alt+Ctrl",
            Theme = new ThemeConfig(),
            Layout = new LayoutConfig(),
            Workspaces = new WorkspaceConfig(),
            ExcludeProcesses = new[]
            {
                "explorer", "shellexperiencehost", "SearchApp",
                "TextInputHost", "SystemSettings", "ApplicationFrameHost", "LockApp"
            },
            Rules = new[]
            {
                new WindowRuleConfig { Process = "firefox.exe", Workspace = 3 },
                new WindowRuleConfig { Process = "Code.exe", Workspace = 2 },
                new WindowRuleConfig { Process = "WindowsTerminal.exe", Workspace = 1 },
            },
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
                if (mw.ProcessName.Contains(rule.Process, StringComparison.OrdinalIgnoreCase))
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
