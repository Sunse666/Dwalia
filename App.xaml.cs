using System.Windows;
using System.Windows.Threading;
using Dwalia.Configuration;
using Dwalia.Infrastructure;
using Dwalia.Managers;
using Dwalia.Views;
using Dwalia.Win32;

namespace Dwalia;

public partial class App : Application
{
    private WindowManager? _windowManager;
    private WindowEventHookManager? _hookManager;
    private WorkspaceManager? _workspaceManager;
    private LayoutManager? _layoutManager;
    private FocusManager? _focusManager;
    private HotKeyManager? _hotKeyManager;
    private ConfigManager? _configManager;
    private ConfigRoot? _config;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Logger.Info("Dwalia starting...");

        DispatcherUnhandledException += (_, ex) =>
        { Logger.Error("UI exception", ex.Exception); ex.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            if (ex.ExceptionObject is Exception ex2) Logger.Error("Domain exception", ex2);
            try { _windowManager?.RestoreAllWindows(); } catch { }
        };

        _configManager = new ConfigManager();
        _config = _configManager.Load();

        _windowManager = new WindowManager(_config.General.ExcludedProcesses);
        _hookManager = new WindowEventHookManager(_windowManager, Dispatcher);
        _workspaceManager = new WorkspaceManager();
        _windowManager.SetWorkspaceManager(_workspaceManager);
        _focusManager = new FocusManager(_windowManager);
        _focusManager.SetBorderColors(
            NativeConstants.ParseDwmColor(_config.Theme.ActiveBorder),
            NativeConstants.ParseDwmColor(_config.Theme.InactiveBorder));
        _hotKeyManager = new HotKeyManager();

        var names = _config.Workspaces.Select(w => w.Name).ToArray();
        if (names.Length > 0)
            _workspaceManager.Initialize(names);

        ServiceLocator.Register(_config);
        ServiceLocator.Register(_windowManager);
        ServiceLocator.Register(_hookManager);
        ServiceLocator.Register(_workspaceManager);
        ServiceLocator.Register(_hotKeyManager);
        ServiceLocator.Register(_configManager);
        ServiceLocator.Register(_focusManager);

        _mainWindow = new MainWindow(_windowManager, _hookManager);
        _hotKeyManager.CommandTriggered += OnCommandTriggered;

        _layoutManager = new LayoutManager(_windowManager, _workspaceManager, _focusManager);
        _layoutManager.SetEnabledLayouts(_config.Layout.EnabledLayouts);
        ServiceLocator.Register(_layoutManager);

        _hookManager.FocusChanged += (_, hwnd) => _focusManager.OnFocusEvent(hwnd);
        _hookManager.WindowDiscovered += (_, hwnd) =>
            _mainWindow.Dispatcher.Invoke(() =>
            {
                var mw = _windowManager.TryManageWindow(hwnd);
                if (mw != null)
                {
                    _configManager.ApplyRulesToWindow(_config, _workspaceManager, mw);
                    _mainWindow.RefreshBackgrounds();
                }
            });
        _windowManager.WindowUnmanaged += (_, mw) =>
        {
            if (_focusManager.ActiveWindow == mw)
                _focusManager.SetActiveWindow(null);
        };

        _mainWindow.Show();

        _configManager.ApplyRules(_config, _windowManager, _workspaceManager);

        Logger.Info("Dwalia started");
    }

    private void OnCommandTriggered(object? sender, DwaliaCommand cmd)
    {
        if (_workspaceManager == null || _focusManager == null || _layoutManager == null) return;
        CommandDispatcher.Execute(cmd, _workspaceManager, _focusManager, _layoutManager,
            _config?.General.LaunchTerminal ?? "wt.exe",
            reloadConfig: ReloadConfig,
            quit: () => _mainWindow?.Dispatcher.Invoke(() => Shutdown()),
            cycleBar: (dir) => _mainWindow?.Dispatcher.Invoke(() => _mainWindow.CycleBarMode(dir)),
            toggleBar: () => _mainWindow?.Dispatcher.Invoke(() => _mainWindow.ToggleBar()));
    }

    private void ReloadConfig()
    {
        var c = _configManager?.Load();
        if (c == null || _windowManager == null || _workspaceManager == null) return;

        _config = c;
        ServiceLocator.Register(c);
        _configManager?.ApplyRules(c, _windowManager, _workspaceManager);
        _focusManager?.SetBorderColors(
            NativeConstants.ParseDwmColor(c.Theme.ActiveBorder),
            NativeConstants.ParseDwmColor(c.Theme.InactiveBorder));

        _mainWindow?.Dispatcher.Invoke(() =>
        {
            _mainWindow.ApplyThemeFromConfig();
            _mainWindow.UpdateTaskBarColors();
            _hotKeyManager?.Dispose();
            _hotKeyManager = new HotKeyManager();
            _hotKeyManager.CommandTriggered += OnCommandTriggered;
            _hotKeyManager.Initialize(_mainWindow.GetHwnd());
            Logger.Info("Config reloaded");
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _windowManager?.RestoreAllWindows(); } catch { }
        _hotKeyManager?.Dispose();
        base.OnExit(e);
        Logger.Info("Dwalia exited");
    }
}
