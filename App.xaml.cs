using System.Windows;
using System.Windows.Threading;
using Dwalia.Configuration;
using Dwalia.Infrastructure;
using Dwalia.Managers;
using Dwalia.Views;

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
    private DwaliaConfig? _config;
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

        _windowManager = new WindowManager(_config.ExcludeProcesses);
        _hookManager = new WindowEventHookManager(_windowManager, Dispatcher);
        _workspaceManager = new WorkspaceManager();
        _windowManager.SetWorkspaceManager(_workspaceManager);
        _focusManager = new FocusManager(_windowManager);
        _hotKeyManager = new HotKeyManager();

        if (_config.Workspaces.Names.Length > 0)
            _workspaceManager.Initialize(_config.Workspaces.Names);

        ServiceLocator.Register(_config);
        ServiceLocator.Register(_windowManager);
        ServiceLocator.Register(_hookManager);
        ServiceLocator.Register(_workspaceManager);
        ServiceLocator.Register(_hotKeyManager);
        ServiceLocator.Register(_configManager);

        _mainWindow = new MainWindow(_windowManager, _hookManager);
        _hotKeyManager.CommandTriggered += OnCommandTriggered;

        _mainWindow.Show();

        _layoutManager = new LayoutManager(_windowManager, _workspaceManager, _focusManager);
        ServiceLocator.Register(_layoutManager);
        ServiceLocator.Register(_focusManager);

        _hookManager.FocusChanged += (_, hwnd) => _focusManager.OnFocusEvent(hwnd);
        _hookManager.WindowDiscovered += (_, hwnd) =>
            _mainWindow.Dispatcher.Invoke(() => _windowManager.TryManageWindow(hwnd));
        _windowManager.WindowUnmanaged += (_, mw) =>
        {
            if (_focusManager.ActiveWindow == mw)
                _focusManager.SetActiveWindow(null);
        };

        _configManager.ApplyRules(_config, _windowManager, _workspaceManager);

        Logger.Info("Dwalia started");
    }

    private void OnCommandTriggered(object? sender, DwaliaCommand cmd)
    {
        if (_workspaceManager == null || _focusManager == null || _layoutManager == null) return;
        var aws = _workspaceManager.GetActiveWorkspace();

        switch (cmd)
        {
            case DwaliaCommand.FocusNext: _focusManager.FocusNext(aws.Windows); break;
            case DwaliaCommand.FocusPrevious: _focusManager.FocusPrevious(aws.Windows); break;
            case DwaliaCommand.ToggleFloat: if (_focusManager.ActiveWindow != null) _layoutManager.ToggleFloating(_focusManager.ActiveWindow.Hwnd); break;
            case DwaliaCommand.ToggleFullscreen: if (_focusManager.ActiveWindow != null) _layoutManager.ToggleFullscreen(_focusManager.ActiveWindow.Hwnd); break;
            case DwaliaCommand.CloseWindow: if (_focusManager.ActiveWindow != null) Dwalia.Win32.NativeMethods.PostMessage(_focusManager.ActiveWindow.Hwnd, Dwalia.Win32.WindowStyles.WM_CLOSE, IntPtr.Zero, IntPtr.Zero); break;
            case DwaliaCommand.QuitDwalia: _mainWindow?.Dispatcher.Invoke(() => Shutdown()); break;
            case DwaliaCommand.FocusWindow1: FocusWindow(0); break;
            case DwaliaCommand.FocusWindow2: FocusWindow(1); break;
            case DwaliaCommand.FocusWindow3: FocusWindow(2); break;
            case DwaliaCommand.FocusWindow4: FocusWindow(3); break;
            case DwaliaCommand.FocusWindow5: FocusWindow(4); break;
            case DwaliaCommand.FocusWindow6: FocusWindow(5); break;
            case DwaliaCommand.FocusWindow7: FocusWindow(6); break;
            case DwaliaCommand.FocusWindow8: FocusWindow(7); break;
            case DwaliaCommand.FocusWindow9: FocusWindow(8); break;
            case DwaliaCommand.Workspace1: _workspaceManager.SwitchToWorkspace(0); break;
            case DwaliaCommand.Workspace2: _workspaceManager.SwitchToWorkspace(1); break;
            case DwaliaCommand.Workspace3: _workspaceManager.SwitchToWorkspace(2); break;
            case DwaliaCommand.Workspace4: _workspaceManager.SwitchToWorkspace(3); break;
            case DwaliaCommand.Workspace5: _workspaceManager.SwitchToWorkspace(4); break;
            case DwaliaCommand.WorkspaceNext: _workspaceManager.NextWorkspace(); break;
            case DwaliaCommand.WorkspacePrevious: _workspaceManager.PreviousWorkspace(); break;
            case DwaliaCommand.MoveToWorkspaceNext: MoveActiveRelative(1); break;
            case DwaliaCommand.MoveToWorkspacePrevious: MoveActiveRelative(-1); break;
            case DwaliaCommand.LaunchTerminal: LaunchTerminal(_config?.LaunchTerminal ?? "wt.exe"); break;
            case DwaliaCommand.ReloadConfig: ReloadConfig(); break;
            case DwaliaCommand.OpenSettings: OpenSettingsWindow(); break;
            case DwaliaCommand.CycleLayout: _layoutManager?.CycleLayout(); break;
            case DwaliaCommand.IncMaster: _layoutManager?.ResizeMaster(0.05); break;
            case DwaliaCommand.DecMaster: _layoutManager?.ResizeMaster(-0.05); break;
            case DwaliaCommand.IncGap: _layoutManager?.ResizeGap(1); break;
            case DwaliaCommand.DecGap: _layoutManager?.ResizeGap(-1); break;
            case DwaliaCommand.SwapNext: _layoutManager?.SwapNext(); break;
            case DwaliaCommand.SwapPrevious: _layoutManager?.SwapPrevious(); break;
        }
    }

    private void FocusWindow(int index)
    {
        if (_workspaceManager == null || _focusManager == null) return;
        var windows = _workspaceManager.GetActiveWorkspace().Windows;
        if (index < 0 || index >= windows.Count)
        {
            Logger.Warn($"FocusWindow{index + 1}: only {windows.Count} windows in workspace");
            return;
        }
        _focusManager.SetActiveWindow(windows[index]);
        windows[index].Focus();
    }

    private void MoveActiveRelative(int direction)
    {
        if (_workspaceManager == null || _focusManager?.ActiveWindow == null) return;
        var count = _workspaceManager.Workspaces.Count;
        var newWs = (_focusManager.ActiveWindow.WorkspaceId + direction + count) % count;
        _workspaceManager.MoveWindowToWorkspace(_focusManager.ActiveWindow, newWs);
    }

    private static void LaunchTerminal(string cmd)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = cmd, UseShellExecute = true }); }
        catch (Exception ex) { Logger.Error($"Launch terminal failed: {ex.Message}"); }
    }

    private void OpenSettingsWindow()
    {
        _mainWindow?.Dispatcher.Invoke(() =>
        {
            var sw = new Views.SettingsWindow();
            sw.Owner = _mainWindow;
            sw.ShowDialog();
        });
    }

    private void ReloadConfig()
    {
        var c = _configManager?.Load();
        if (c != null && _windowManager != null && _workspaceManager != null)
            _configManager?.ApplyRules(c, _windowManager, _workspaceManager);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _windowManager?.RestoreAllWindows(); } catch { }
        _hotKeyManager?.Dispose();
        base.OnExit(e);
        Logger.Info("Dwalia exited");
    }
}
