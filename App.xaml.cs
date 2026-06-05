using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Dwalia.Configuration;
using Dwalia.Infrastructure;
using Dwalia.Managers;
using Dwalia.Models;
using Dwalia.Views;
using Dwalia.Win32;
using static Dwalia.Win32.NativeMethods;
using static Dwalia.Win32.WindowStyles;

namespace Dwalia;

public partial class App : Application
{
    private WindowManager? _windowManager;
    private WindowEventHookManager? _hookManager;
    private WorkspaceManager? _workspaceManager;
    private LayoutManager? _layoutManager;
    private MonitorManager? _monitorManager;
    private FocusManager? _focusManager;
    private HotKeyManager? _hotKeyManager;
    private MouseResizeManager? _mouseResizeManager;
    private IpcServer? _ipcServer;
    private ConfigManager? _configManager;
    private ConfigRoot? _config;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Logger.Info("Dwalia starting...");
        timeBeginPeriod(1);

        DispatcherUnhandledException += (_, ex) =>
        { Logger.Error("UI exception", ex.Exception); ex.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            if (ex.ExceptionObject is Exception ex2) Logger.Error("Domain exception", ex2);
            try { _windowManager?.RestoreAllWindows(); } catch { }
        };

        _configManager = new ConfigManager();
        _config = _configManager.Load();
        Logger.Enabled = _config.General.EnableLogging;

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

        _layoutManager = new LayoutManager(_windowManager, _workspaceManager, _focusManager);
        _layoutManager.SetEnabledLayouts(_config.Layout.EnabledLayouts);
        if (_config.General.DefaultLayout is { Length: > 0 } defLayout
            && Enum.TryParse<Managers.LayoutType>(defLayout, true, out var lt))
        {
            foreach (var ws in _workspaceManager.Workspaces)
                ws.Layout = lt;
        }
        ServiceLocator.Register(_layoutManager);

        _mainWindow = new MainWindow(_windowManager, _hookManager);

        _monitorManager = new MonitorManager();
        _monitorManager.RefreshMonitors();
        ServiceLocator.Register(_monitorManager);

        foreach (var m in _monitorManager.Monitors)
            _workspaceManager.InitializeForMonitor(m.Id);

        _hotKeyManager.CommandTriggered += OnCommandTriggered;

        _mouseResizeManager = new MouseResizeManager();
        ServiceLocator.Register(_mouseResizeManager);

        var widgetManager = new WidgetManager();
        ServiceLocator.Register(widgetManager);
        _mouseResizeManager.GetCurrentMasterFactor = () => _layoutManager?.CurrentMasterFactor ?? 0.6;
        _mouseResizeManager.GetSplitRatio = (splitId) => _layoutManager?.GetSplitRatio(splitId) ?? 0.5;
        _mouseResizeManager.GetAreaSize = () =>
        {
            if (_layoutManager == null) return (1920.0, 1080.0);
            var a = _layoutManager.Area;
            return (a.Width, a.Height);
        };

        var scratchpadManager = new ScratchpadManager();
        ServiceLocator.Register(scratchpadManager);

        _layoutManager.ResizeZonesUpdated += zones =>
            _mainWindow?.Dispatcher.Invoke(() => _mouseResizeManager.UpdateZones(zones));

        _mouseResizeManager.MasterFactorChanged += factor =>
            _mainWindow?.Dispatcher.Invoke(() =>
            {
                _layoutManager.SetAnimationEnabled(false);
                _layoutManager.SetMasterFactor(factor, save: false);
            });

        _mouseResizeManager.SplitFactorChanged += (splitId, factor) =>
            _mainWindow?.Dispatcher.Invoke(() =>
            {
                _layoutManager.SetAnimationEnabled(false);
                _layoutManager.AdjustSplitRatio(splitId, factor);
            });

        _mouseResizeManager.ResizeEnded += () =>
            _mainWindow?.Dispatcher.Invoke(() =>
            {
                _layoutManager.SetAnimationEnabled(true);
                _layoutManager.SaveLayoutConfig();
                _layoutManager.SyncAfterResize();
            });

        if (_config.Theme.FocusFollowsMouse)
        {
            _mouseResizeManager.FindWindowAtPoint = (x, y) =>
            {
                var hwnd = WindowFromPoint(x, y);
                if (hwnd != IntPtr.Zero && _windowManager.IsManaged(hwnd))
                    return hwnd;
                return IntPtr.Zero;
            };
            _mouseResizeManager.FocusWindowAtPoint = (hwnd) =>
            {
                var mw = _windowManager.GetManagedWindow(hwnd);
                if (mw != null)
                    _focusManager.SetActiveWindow(mw);
            };
        }

        _hookManager.FocusChanged += (_, hwnd) =>
        {
            _focusManager.OnFocusEvent(hwnd);
            if (_monitorManager != null && _focusManager.ActiveWindow != null)
            {
                var monitorId = _monitorManager.GetMonitorIdForWindow(hwnd);
                _workspaceManager!.CurrentMonitorId = monitorId;
            }
        };
        _hookManager.WindowDiscovered += (_, hwnd) =>
            _mainWindow.Dispatcher.Invoke(() =>
            {
                if (_config?.General.EnableSwallowing == true && _windowManager != null)
                {
                    var ownerHwnd = GetWindow(hwnd, GW_OWNER);
                    IntPtr parentHwnd = IntPtr.Zero;
                    if (ownerHwnd != IntPtr.Zero && _windowManager.IsManaged(ownerHwnd))
                    {
                        parentHwnd = ownerHwnd;
                    }
                    else
                    {
                        var childPid = WindowHelper.GetProcessId(hwnd);
                        var parentPid = WindowHelper.GetParentProcessId(childPid);
                        if (parentPid > 0 && parentPid != childPid)
                        {
                            var candidate = _windowManager.ManagedWindows.Values
                                .FirstOrDefault(mw => mw.ProcessId == parentPid && mw.SwallowedByHwnd == IntPtr.Zero);
                            if (candidate != null)
                                parentHwnd = candidate.Hwnd;
                        }
                    }

                    if (parentHwnd != IntPtr.Zero)
                    {
                        var childMw = _windowManager.TryManageWindow(hwnd);
                        if (childMw != null)
                        {
                            _windowManager.SwallowWindow(parentHwnd, hwnd);
                            _mainWindow.RefreshBackgrounds();
                            return;
                        }
                    }
                }

                var mw = _windowManager!.TryManageWindow(hwnd);
                if (mw != null)
                {
                    _configManager!.ApplyRulesToWindow(_config!, _workspaceManager!, mw);
                    _mainWindow.RefreshBackgrounds();
                    _layoutManager?.Relayout();
                }
            });
        _windowManager.WindowUnmanaged += (_, mw) =>
        {
            if (_focusManager.ActiveWindow == mw)
                _focusManager.SetActiveWindow(null);
        };

        _mainWindow.Show();

        if (ServiceLocator.TryResolve<WidgetManager>(out var wm))
        {
            wm.Initialize();
            _mainWindow?.Dispatcher.Invoke(() => _mainWindow.SetupWidgetBars());
        }

        if (_config.General.StartupWorkspace > 0
            && _config.General.StartupWorkspace < _workspaceManager.Workspaces.Count)
        {
            _workspaceManager.SwitchToWorkspace(_config.General.StartupWorkspace);
        }

        Views.MainWindow.WarmupInfoBar();

        _mouseResizeManager.Initialize(_mainWindow!.GetHwnd());

        _configManager.ApplyRules(_config, _windowManager, _workspaceManager);

        foreach (var entry in _config.Autostart)
        {
            if (!string.IsNullOrWhiteSpace(entry.Command))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = entry.Command,
                        UseShellExecute = true
                    });
                    Logger.Info($"Autostart launched: {entry.Name} ({entry.Command})");
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Autostart failed for '{entry.Name}': {ex.Message}");
                }
            }
        }

        _configManager.StartWatching(() =>
            _mainWindow?.Dispatcher.Invoke(() => ReloadConfig()));

        _ipcServer = new IpcServer(Dispatcher);
        _ipcServer.Start();

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
        Logger.Enabled = c.General.EnableLogging;
        ServiceLocator.Register(c);
        _configManager?.ApplyRules(c, _windowManager, _workspaceManager);
        if (_layoutManager != null) _layoutManager.SmartGaps = c.Layout.SmartGaps;
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
        _configManager?.StopWatching();
        _ipcServer?.Dispose();
        try { _windowManager?.RestoreAllWindows(); } catch { }
        _hotKeyManager?.Dispose();
        _mouseResizeManager?.Dispose();
        timeEndPeriod(1);
        DwmFlush();
        base.OnExit(e);
        Logger.Info("Dwalia exited");
    }
}
