using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Dwalia.Infrastructure;
using Dwalia.Managers;
using FocusMgr = Dwalia.Managers.FocusManager;
using static Dwalia.Win32.NativeMethods;
using static Dwalia.Win32.NativeConstants;
using static Dwalia.Win32.WindowStyles;

namespace Dwalia.Views;

public partial class MainWindow : Window
{
    private readonly WindowManager _windowManager;
    private readonly WindowEventHookManager _windowEventHookManager;
    private IntPtr _hwnd;
    private HwndSource? _hwndSource;
    private DispatcherTimer? _statusTimer;
    private FocusBackground? _focusBackground;
    private DispatcherTimer? _focusBgTimer;
    private Color _focusBgColor;
    private int _focusBgRadius;

    public IntPtr GetHwnd() => _hwnd;

    public MainWindow(WindowManager wm, WindowEventHookManager ehm)
    {
        InitializeComponent();
        _windowManager = wm;
        _windowEventHookManager = ehm;

        _windowManager.WindowsChanged += (_, _) => UpdateTaskBar();
        _windowManager.WindowManaged += (_, mw) => OnWindowManaged(mw);
        _windowManager.WindowUnmanaged += (_, mw) => OnWindowUnmanaged(mw);

        if (ServiceLocator.TryResolve<FocusMgr>(out var fm))
            fm.FocusChanged += UpdateTaskBar;

        if (ServiceLocator.TryResolve<WorkspaceManager>(out var wsm))
        {
            wsm.WorkspaceChanged += (_, _) => { UpdateTaskBar(); UpdateWorkspacePills(); RefreshBackgroundVisibility(); };
        }

        var wa = System.Windows.SystemParameters.WorkArea;
        Left = wa.Left;
        Top = wa.Top;
        Width = wa.Width;
        Height = wa.Height;

        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(WndProcHook);

        Logger.Info($"MainWindow.OnSourceInitialized: HWND={_hwnd}, HwndSource={(_hwndSource != null ? "ok" : "NULL")}");

        ApplyAcrylic(_hwnd);

        SetWindowPos(_hwnd, new IntPtr(1), 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

        if (ServiceLocator.TryResolve<WindowEventHookManager>(out var ehm))
            ehm.SetMainWindowHwnd(_hwnd);

        if (ServiceLocator.TryResolve<HotKeyManager>(out var hkm))
        {
            hkm.Initialize(_hwnd);
            Logger.Info($"HotKeyManager initialized: {hkm.RegisteredCount} registered, {hkm.FailedRegistrations.Count} failed");
        }
        else
        {
            Logger.Warn("HotKeyManager NOT FOUND in ServiceLocator during OnSourceInitialized!");
        }

        if (ServiceLocator.TryResolve<Configuration.DwaliaConfig>(out var cfg))
        {
            try { _focusBgColor = (Color)ColorConverter.ConvertFromString(cfg.Theme.Accent ?? "#7aa2f7"); }
            catch { _focusBgColor = Color.FromRgb(0x7a, 0xa2, 0xf7); }
            _focusBgRadius = cfg.Theme.BorderWidth > 0 ? cfg.Theme.BorderWidth + 6 : 8;
        }
        else
        {
            _focusBgColor = Color.FromRgb(0x7a, 0xa2, 0xf7);
            _focusBgRadius = 8;
        }

        _focusBackground = new FocusBackground(_focusBgColor, _focusBgRadius);

        if (ServiceLocator.TryResolve<FocusMgr>(out var focusMgr))
        {
            focusMgr.FocusChanged += () =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    _focusBackground?.SetActive(focusMgr.ActiveWindow?.Hwnd);
                });
            };
        }

        if (ServiceLocator.TryResolve<LayoutManager>(out var lmg))
        {
            lmg.LayoutChanged += (_, _) => Dispatcher.BeginInvoke(RefreshAllBackgroundPositions);
        }

        _focusBgTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _focusBgTimer.Tick += (_, _) => RefreshFloatingBackgroundPositions();
        _focusBgTimer.Start();
    }

    private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return (IntPtr)MA_NOACTIVATE;
        }
        if (msg == WM_DWALIA_COMMAND)
        {
            if (ServiceLocator.TryResolve<HotKeyManager>(out var hkm))
            {
                hkm.DispatchCommand((DwaliaCommand)wParam.ToInt32());
                handled = true;
            }
            return IntPtr.Zero;
        }
        return IntPtr.Zero;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); LayoutLabel.Text = _layoutLabelText; };

        ApplyThemeFromConfig();

        if (ServiceLocator.TryResolve<LayoutManager>(out var lm))
        {
            lm.SetArea(_hwnd, TaskBar.Height);
            lm.LayoutChanged += (_, layout) => { _layoutLabelText = layout.ToString(); LayoutLabel.Text = _layoutLabelText; };
            lm.StatusMessage += msg =>
            {
                LayoutLabel.Text = msg;
                _statusTimer.Stop();
                _statusTimer.Start();
            };
        }

        _windowEventHookManager.Start();
        _windowManager.Initialize();
    }

    private string _layoutLabelText = "MasterStack";

    private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        Logger.Info("Shutting down — restoring all windows...");
        _focusBgTimer?.Stop();
        _focusBackground?.Dispose();
        _windowEventHookManager.Stop();
        _windowManager.RestoreAllWindows();
    }

    private void UpdateTaskBar()
    {
        TaskBarItems.Items.Clear();

        if (!ServiceLocator.TryResolve<WorkspaceManager>(out var wsm)) return;
        var activeWs = wsm.GetActiveWorkspace();
        if (activeWs == null || activeWs.Windows.Count == 0) return;

        ServiceLocator.TryResolve<FocusMgr>(out var fm);

        int startIdx = 0;
        if (fm?.ActiveWindow != null)
        {
            var idx = activeWs.Windows.IndexOf(fm.ActiveWindow);
            if (idx >= 0) startIdx = idx;
        }

        var count = activeWs.Windows.Count;
        for (int i = 0; i < count; i++)
        {
            var mw = activeWs.Windows[(startIdx + i) % count];
            var title = mw.Title.Length > 30 ? mw.Title[..30] + "..." : mw.Title;
            var btn = new Button
            {
                Content = title,
                Height = 28, Margin = new Thickness(4, 0, 4, 0),
                Padding = new Thickness(8, 0, 8, 0), FontSize = 11,
                Foreground = mw.IsActive
                    ? new SolidColorBrush(Color.FromRgb(0x7a, 0xa2, 0xf7))
                    : new SolidColorBrush(Color.FromRgb(0xc0, 0xca, 0xf5)),
                Background = new SolidColorBrush(Color.FromRgb(0x24, 0x28, 0x3e)),
                BorderThickness = new Thickness(0)
            };

            var hwnd = mw.Hwnd;
            var captured = mw;
            btn.Click += (_, _) =>
            {
                ShowWindow(hwnd, SW_RESTORE);
                SetForegroundWindow(hwnd);
                fm?.SetActiveWindow(captured);
            };

            btn.ContextMenu = BuildWindowContextMenu(captured);

            TaskBarItems.Items.Add(btn);
        }
        UpdateWorkspacePills();
    }

    private ContextMenu BuildWindowContextMenu(Dwalia.Models.ManagedWindow mw)
    {
        var menu = new ContextMenu
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2d, 0x2d, 0x2d)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xcc, 0xcc, 0xcc)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44))
        };

        var closeItem = new MenuItem { Header = "Close Window" };
        closeItem.Click += (_, _) => PostMessage(mw.Hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        menu.Items.Add(closeItem);

        var floatItem = new MenuItem
        {
            Header = mw.State == Dwalia.Models.WindowLayoutState.Floating ? "Tile" : "Float"
        };
        floatItem.Click += (_, _) =>
        {
            ServiceLocator.TryResolve<LayoutManager>(out var lm);
            lm?.ToggleFloating(mw.Hwnd);
        };
        menu.Items.Add(floatItem);

        menu.Items.Add(new Separator());

        var moveMenu = new MenuItem { Header = "Move to Workspace" };
        if (ServiceLocator.TryResolve<WorkspaceManager>(out var wsm))
        {
            foreach (var ws in wsm.Workspaces)
            {
                var wsItem = new MenuItem
                {
                    Header = $"{ws.Id + 1}: {ws.Name}",
                    IsChecked = mw.WorkspaceId == ws.Id
                };
                var targetId = ws.Id;
                wsItem.Click += (_, _) => wsm.MoveWindowToWorkspace(mw, targetId);
                moveMenu.Items.Add(wsItem);
            }
        }
        menu.Items.Add(moveMenu);

        return menu;
    }

    private void ApplyAcrylic(IntPtr hwnd)
    {
        try
        {
            var accent = new AccentPolicy
            {
                AccentState = 4,
                AccentFlags = 2,
                GradientColor = unchecked((int)0x401A1B26),
                AnimationId = 0
            };
            var data = new WindowCompositionAttributeData
            {
                Attribute = 19,
                Data = Marshal.AllocHGlobal(Marshal.SizeOf<AccentPolicy>()),
                SizeOfData = Marshal.SizeOf<AccentPolicy>()
            };
            Marshal.StructureToPtr(accent, data.Data, false);
            SetWindowCompositionAttribute(hwnd, ref data);
            Marshal.FreeHGlobal(data.Data);
        }
        catch (Exception ex)
        {
            Logger.Warn($"ApplyAcrylic failed: {ex.Message}");
        }
    }

    public void ApplyThemeFromConfig()
    {
        if (!ServiceLocator.TryResolve<Configuration.DwaliaConfig>(out var c))
            return;

        var bgHex = c.Theme.Background ?? "#221a1b26";
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(bgHex);
            OverlayGrid.Background = new SolidColorBrush(color);
        }
        catch { }

        if (c.Theme.Accent is { Length: > 0 } accent)
        {
            try
            {
                var accentColor = (Color)ColorConverter.ConvertFromString(accent);
                Application.Current.Resources["AccentBrush"] = new SolidColorBrush(accentColor);
            }
            catch { }
        }

        if (c.Theme.TaskbarBackground is { Length: > 0 } tbHex)
        {
            try
            {
                var tbColor = (Color)ColorConverter.ConvertFromString(tbHex);
                TaskBar.Background = new SolidColorBrush(tbColor);
            }
            catch { }
        }
    }

    private void UpdateWorkspacePills()
    {
        WorkspacePills.Children.Clear();
        if (!ServiceLocator.TryResolve<WorkspaceManager>(out var wsm)) return;

        foreach (var ws in wsm.Workspaces)
        {
            var isActive = ws.Id == wsm.ActiveWorkspaceId;
            var hasWindows = ws.Windows.Count > 0;
            var pill = new Border
            {
                Width = isActive ? 24 : 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(2, 0, 2, 0),
                Background = new SolidColorBrush(Color.FromRgb(
                    (byte)(isActive ? 0x7a : (hasWindows ? 0x56 : 0x3b)),
                    (byte)(isActive ? 0xa2 : (hasWindows ? 0x5f : 0x42)),
                    (byte)(isActive ? 0xf7 : (hasWindows ? 0x89 : 0x61))))
            };
            WorkspacePills.Children.Add(pill);
        }
    }


    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = false;
    }

    private void OnWindowManaged(Dwalia.Models.ManagedWindow mw)
    {
        if (_focusBackground == null) return;
        try
        {
            var r = Dwalia.Win32.WindowHelper.GetWindowRectSafe(mw.Hwnd);
            if (r.Width > 0 && r.Height > 0)
                _focusBackground.Add(mw.Hwnd, r.Left, r.Top, r.Width, r.Height);
        }
        catch { }
    }

    private void OnWindowUnmanaged(Dwalia.Models.ManagedWindow mw)
    {
        _focusBackground?.Remove(mw.Hwnd);
    }

    private void RefreshAllBackgroundPositions()
    {
        if (_focusBackground == null) return;
        if (!ServiceLocator.TryResolve<WorkspaceManager>(out var wsm)) return;
        if (!ServiceLocator.TryResolve<LayoutManager>(out var lm)) return;

        var activeWs = wsm.GetActiveWorkspace();
        if (activeWs == null) return;

        var area = lm.Area;
        int gap = 4;
        if (ServiceLocator.TryResolve<Configuration.DwaliaConfig>(out var cfg))
            gap = cfg.Layout.InnerGap;

        int expand = gap / 2;

        foreach (var mw in activeWs.Windows)
        {
            if (mw.State == Dwalia.Models.WindowLayoutState.Fullscreen) continue;
            try
            {
                if (mw.State == Dwalia.Models.WindowLayoutState.Tiled && mw.LayoutBounds.Width > 0)
                {
                    var r = mw.LayoutBounds;
                    int x = Math.Max((int)area.X, (int)r.X - expand);
                    int y = Math.Max((int)area.Y, (int)r.Y - expand);
                    int right = Math.Min((int)(area.X + area.Width), (int)(r.X + r.Width + expand));
                    int bottom = Math.Min((int)(area.Y + area.Height), (int)(r.Y + r.Height + expand));
                    _focusBackground.UpdatePosition(mw.Hwnd, x, y, right - x, bottom - y);
                    _focusBackground.SetVisible(mw.Hwnd, true);
                }
            }
            catch { }
        }
    }

    private void RefreshFloatingBackgroundPositions()
    {
        if (_focusBackground == null) return;
        if (!ServiceLocator.TryResolve<WorkspaceManager>(out var wsm)) return;

        var activeWs = wsm.GetActiveWorkspace();
        if (activeWs == null) return;

        foreach (var mw in activeWs.Windows)
        {
            if (mw.State != Dwalia.Models.WindowLayoutState.Floating) continue;
            try
            {
                var wr = Dwalia.Win32.WindowHelper.GetWindowRectSafe(mw.Hwnd);
                if (wr.Width > 0 && wr.Height > 0)
                {
                    _focusBackground.UpdatePosition(mw.Hwnd, wr.Left, wr.Top, wr.Width, wr.Height);
                    _focusBackground.SetVisible(mw.Hwnd, true);
                }
            }
            catch { }
        }
    }

    private void RefreshBackgroundVisibility()
    {
        if (_focusBackground == null) return;
        if (!ServiceLocator.TryResolve<WorkspaceManager>(out var wsm)) return;
        if (!ServiceLocator.TryResolve<WindowManager>(out var wm)) return;

        var activeWs = wsm.GetActiveWorkspace();
        if (activeWs == null) return;

        foreach (var mw in wm.ManagedWindows.Values)
        {
            bool visible = mw.WorkspaceId == activeWs.Id && mw.State != Dwalia.Models.WindowLayoutState.Fullscreen;
            _focusBackground.SetVisible(mw.Hwnd, visible);
        }
    }

}
