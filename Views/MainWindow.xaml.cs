using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Dwalia.Configuration;
using Dwalia.Infrastructure;
using Dwalia.Managers;
using FocusMgr = Dwalia.Managers.FocusManager;
using static Dwalia.Win32.NativeMethods;
using static Dwalia.Win32.NativeConstants;
using static Dwalia.Win32.WindowStyles;

namespace Dwalia.Views;

public enum BarMode { Docker, Info, Launcher }

public partial class MainWindow : Window
{
    private readonly WindowManager _windowManager;
    private readonly WindowEventHookManager _windowEventHookManager;
    private IntPtr _hwnd;
    private HwndSource? _hwndSource;
    private DispatcherTimer? _statusTimer;
    private DispatcherTimer? _infoTimer;
    private FocusBackground? _focusBackground;
    private ColorFilterOverlay? _colorFilter;
    private DispatcherTimer? _focusBgTimer;
    private BarMode _barMode = BarMode.Docker;
    private PerformanceCounter? _cpuCounter;
    private PerformanceCounter? _memCounter;
    private Color _focusBgColor;
    private Color _foregroundColor;
    private Color _mutedColor;

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
            wsm.WorkspaceChanged += (_, _) => { UpdateTaskBar(); UpdateWorkspacePills(); };
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

        if (ServiceLocator.TryResolve<ConfigRoot>(out var acCfg))
        {
            if (acCfg.Theme.EnableAcrylic)
            {
                ApplyAcrylic(_hwnd, acCfg.Theme.Background ?? "#1a1b26");
            }
            OverlayGrid.Background = Brushes.Transparent;
        }

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

        if (ServiceLocator.TryResolve<ConfigRoot>(out var cfg))
        {
            try { _focusBgColor = (Color)ColorConverter.ConvertFromString(cfg.Theme.Accent ?? "#7aa2f7"); }
            catch { _focusBgColor = Color.FromRgb(0x7a, 0xa2, 0xf7); }
            try { _foregroundColor = (Color)ColorConverter.ConvertFromString(cfg.Theme.Foreground ?? "#c0caf5"); }
            catch { _foregroundColor = Color.FromRgb(0xc0, 0xca, 0xf5); }
            try { _mutedColor = (Color)ColorConverter.ConvertFromString(cfg.Theme.Muted ?? "#565f89"); }
            catch { _mutedColor = Color.FromRgb(0x56, 0x5f, 0x89); }
        }
        else
        {
            _focusBgColor = Color.FromRgb(0x7a, 0xa2, 0xf7);
            _foregroundColor = Color.FromRgb(0xc0, 0xca, 0xf5);
            _mutedColor = Color.FromRgb(0x56, 0x5f, 0x89);
        }

        var focusRadius = cfg?.Theme.FocusRadius ?? 8;
        var focusActiveOp = cfg?.Theme.FocusActiveOpacity ?? 0.27;
        var focusInactiveOp = cfg?.Theme.FocusInactiveOpacity ?? 0.09;
        var focusFill = cfg?.Theme.FocusFill ?? true;

        _focusBackground = new FocusBackground(_focusBgColor, focusRadius, focusActiveOp, focusInactiveOp, focusFill);

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
            lmg.RelayoutCompleted += RefreshAllBackgroundPositions;
            lmg.LayoutTargetsComputed += () =>
            {
                RefreshAllBackgroundPositions();
                lmg.StartWindowAnimation();
            };
        }

        _focusBgTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _focusBgTimer.Tick += (_, _) => RefreshFloatingBackgroundPositions();
        _focusBgTimer.Start();

        if (ServiceLocator.TryResolve<ConfigRoot>(out var filterCfg)
            && filterCfg.Theme.ColorFilterOpacity > 0)
        {
            ApplyColorFilter(filterCfg.Theme.ColorFilter, filterCfg.Theme.ColorFilterOpacity);
        }
    }

    private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return (IntPtr)MA_NOACTIVATE;
        }
        if (msg == WM_WINDOWPOSCHANGING)
        {
            var wp = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            if (!wp.hwndInsertAfter.Equals(new IntPtr(1)))
            {
                wp.hwndInsertAfter = new IntPtr(1);
                wp.flags &= ~4u;
                Marshal.StructureToPtr(wp, lParam, false);
            }
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
        Dispatcher.BeginInvoke(() => RefreshAllBackgroundPositions());
    }

    private string _layoutLabelText = "MasterStack";

    private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        Logger.Info("Shutting down — restoring all windows...");
        _focusBgTimer?.Stop();
        _infoTimer?.Stop();
        _cpuCounter?.Dispose();
        _memCounter?.Dispose();
        _colorFilter?.Dispose();
        _windowEventHookManager.Stop();
        _windowManager.RestoreAllWindows();
        _focusBackground?.Dispose();
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
                    ? new SolidColorBrush(_focusBgColor)
                    : new SolidColorBrush(_foregroundColor),
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

    private void ApplyAcrylic(IntPtr hwnd, string bgHex)
    {
        try
        {
            var bgColor = Color.FromRgb(0x1A, 0x1B, 0x26);
            try { bgColor = (Color)ColorConverter.ConvertFromString(bgHex); } catch { }

            var gradientColor = unchecked((int)(0x40000000
                | (uint)bgColor.R << 16 | (uint)bgColor.G << 8 | bgColor.B));
            var accent = new AccentPolicy
            {
                AccentState = 4,
                AccentFlags = 2,
                GradientColor = gradientColor,
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
        if (!ServiceLocator.TryResolve<ConfigRoot>(out var c))
            return;

        try { _foregroundColor = (Color)ColorConverter.ConvertFromString(c.Theme.Foreground ?? "#c0caf5"); }
        catch { }
        try { _mutedColor = (Color)ColorConverter.ConvertFromString(c.Theme.Muted ?? "#565f89"); }
        catch { }

        if (c.Theme.EnableAcrylic)
        {
            OverlayGrid.Background = Brushes.Transparent;
            ApplyAcrylic(_hwnd, c.Theme.Background ?? "#1a1b26");
        }
        else
        {
            OverlayGrid.Background = Brushes.Transparent;
        }

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

        LayoutLabel.Foreground = new SolidColorBrush(_mutedColor);
        ClockText.Foreground = new SolidColorBrush(_foregroundColor);
        var accentBrush = new SolidColorBrush(_focusBgColor);
        CpuText.Foreground = accentBrush;
        MemText.Foreground = accentBrush;
        BatteryText.Foreground = accentBrush;

        _focusBackground?.UpdateStyle(_focusBgColor, c.Theme.FocusRadius,
            c.Theme.FocusActiveOpacity, c.Theme.FocusInactiveOpacity, c.Theme.FocusFill);

        if (c.Theme.ColorFilterOpacity > 0)
            ApplyColorFilter(c.Theme.ColorFilter, c.Theme.ColorFilterOpacity);
        else
            _colorFilter?.Hide();
    }

    public void ApplyColorFilter(string hexColor, double opacity)
    {
        if (opacity <= 0)
        {
            _colorFilter?.Hide();
            return;
        }
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(hexColor);
            var alpha = (byte)(Math.Min(opacity * 255, 255));
            var filterColor = Color.FromArgb(alpha, c.R, c.G, c.B);
            _colorFilter ??= new ColorFilterOverlay();
            _colorFilter.Apply(filterColor);
        }
        catch { }
    }

    public void RefreshBackgrounds()
    {
        Dispatcher.BeginInvoke(RefreshAllBackgroundPositions);
    }

    public void CycleBarMode(int direction)
    {
        var modes = new[] { BarMode.Docker, BarMode.Info, BarMode.Launcher };
        var idx = Array.IndexOf(modes, _barMode);
        _barMode = modes[(idx + direction + modes.Length) % modes.Length];
        ShowBarMode(_barMode);
    }

    public void ToggleBar()
    {
        if (TaskBar.Visibility == Visibility.Visible)
        {
            TaskBar.Visibility = Visibility.Collapsed;
            InfoBar.Visibility = Visibility.Collapsed;
            LauncherBar.Visibility = Visibility.Collapsed;
            _infoTimer?.Stop();
        }
        else
        {
            ShowBarMode(_barMode);
        }
        UpdateBarArea();
    }

    private void ShowBarMode(BarMode mode)
    {
        _barMode = mode;
        TaskBar.Visibility = mode == BarMode.Docker ? Visibility.Visible : Visibility.Collapsed;
        InfoBar.Visibility = mode == BarMode.Info ? Visibility.Visible : Visibility.Collapsed;
        LauncherBar.Visibility = mode == BarMode.Launcher ? Visibility.Visible : Visibility.Collapsed;

        _infoTimer?.Stop();
        if (mode == BarMode.Info)
        {
            UpdateInfoBar();
            _infoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _infoTimer.Tick += (_, _) => UpdateInfoBar();
            _infoTimer.Start();
        }

        if (mode == BarMode.Launcher)
            BuildLauncherButtons();

        UpdateBarArea();
    }

    private void UpdateBarArea()
    {
        bool visible = TaskBar.Visibility == Visibility.Visible
                    || InfoBar.Visibility == Visibility.Visible
                    || LauncherBar.Visibility == Visibility.Visible;
        if (ServiceLocator.TryResolve<LayoutManager>(out var lm))
            lm.SetArea(_hwnd, visible ? TaskBar.Height : 0);
    }

    private void UpdateInfoBar()
    {
        ClockText.Text = DateTime.Now.ToString("HH:mm:ss  yyyy-MM-dd");
        try { CpuText.Text = $"CPU {(int)GetCpuUsage():D2}%"; } catch { CpuText.Text = "CPU --%"; }
        try { MemText.Text = $"MEM {(int)GetMemUsage():D2}%"; } catch { MemText.Text = "MEM --%"; }
        try { BatteryText.Text = GetBatteryText(); } catch { BatteryText.Text = "BAT --%"; }
    }

    private void BuildLauncherButtons()
    {
        LauncherButtons.Children.Clear();
        if (!ServiceLocator.TryResolve<ConfigRoot>(out var cfg)) return;
        if (cfg.Launcher.Count == 0)
        {
            LauncherButtons.Children.Add(new TextBlock
            {
                Text = "No apps configured. Add launcher entries to config.yaml",
                Foreground = new SolidColorBrush(_mutedColor),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            });
            return;
        }

        foreach (var entry in cfg.Launcher)
        {
            var name = string.IsNullOrEmpty(entry.Name) ? entry.Path : entry.Name;

            var btn = new Button
            {
                Content = name,
                Height = 28,
                Margin = new Thickness(3, 0, 3, 0),
                Padding = new Thickness(10, 0, 10, 0),
                FontSize = 11,
                Foreground = new SolidColorBrush(_foregroundColor),
                Background = new SolidColorBrush(Color.FromRgb(0x24, 0x28, 0x3e)),
                BorderThickness = new Thickness(0),
            };
            var cmd = entry.Path;
            btn.Click += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo { FileName = cmd, UseShellExecute = true }); }
                catch (Exception ex) { Logger.Warn($"Launch failed: {cmd}: {ex.Message}"); }
            };
            LauncherButtons.Children.Add(btn);
        }
    }

    private double GetCpuUsage()
    {
        _cpuCounter ??= new PerformanceCounter("Processor", "% Processor Time", "_Total");
        try { return _cpuCounter.NextValue() / Environment.ProcessorCount; }
        catch { return 0; }
    }

    private double GetMemUsage()
    {
        _memCounter ??= new PerformanceCounter("Memory", "% Committed Bytes In Use");
        try { return _memCounter.NextValue(); }
        catch { return 0; }
    }

    private static string GetBatteryText()
    {
        if (!GetSystemPowerStatus(out var ps)) return "BAT --%";
        if (ps.BatteryFlag == 128) return "BAT AC";
        var pct = ps.BatteryLifePercent;
        return pct <= 100 ? $"BAT {pct}%" : "BAT --%";
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus sps);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte Reserved1;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    public void UpdateTaskBarColors()
    {
        UpdateTaskBar();
        UpdateWorkspacePills();
        LayoutLabel.Foreground = new SolidColorBrush(_mutedColor);
        if (_barMode == BarMode.Launcher)
            BuildLauncherButtons();
    }

    private void UpdateWorkspacePills()
    {
        WorkspacePills.Children.Clear();
        if (!ServiceLocator.TryResolve<WorkspaceManager>(out var wsm)) return;

        foreach (var ws in wsm.Workspaces)
        {
            var isActive = ws.Id == wsm.ActiveWorkspaceId;
            var hasWindows = ws.Windows.Count > 0;
            Color pillColor;
            if (isActive)
                pillColor = _focusBgColor;
            else if (hasWindows)
                pillColor = _mutedColor;
            else
                pillColor = Color.FromRgb(
                    (byte)(_mutedColor.R / 2),
                    (byte)(_mutedColor.G / 2),
                    (byte)(_mutedColor.B / 2));
            var pill = new Border
            {
                Width = isActive ? 24 : 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(2, 0, 2, 0),
                Background = new SolidColorBrush(pillColor)
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
        AddBackgroundForWindow(mw);
    }

    private void AddBackgroundForWindow(Dwalia.Models.ManagedWindow mw)
    {
        if (_focusBackground == null) return;
        try
        {
            var r = mw.LayoutBounds;
            if (r.Width > 0)
            {
                _focusBackground.Add(mw.Hwnd, (int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
            }
            else
            {
                var wr = Dwalia.Win32.WindowHelper.GetWindowRectSafe(mw.Hwnd);
                if (wr.Width > 0 && wr.Height > 0)
                    _focusBackground.Add(mw.Hwnd, wr.Left, wr.Top, wr.Width, wr.Height);
            }
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
        if (!ServiceLocator.TryResolve<WindowManager>(out var wm)) return;

        var activeWs = wsm.GetActiveWorkspace();
        if (activeWs == null) return;

        var area = lm.Area;
        int gap = 4;
        if (ServiceLocator.TryResolve<ConfigRoot>(out var cfg))
            gap = cfg.Layout.InnerGap;

        int expand = gap / 2;

        foreach (var mw in wm.ManagedWindows.Values)
        {
            bool onActiveWs = mw.WorkspaceId == activeWs.Id;
            bool isFullscreen = mw.State == Dwalia.Models.WindowLayoutState.Fullscreen;

            if (!onActiveWs || isFullscreen)
            {
                _focusBackground.SetVisible(mw.Hwnd, false);
                continue;
            }

            try
            {
                if (mw.State == Dwalia.Models.WindowLayoutState.Tiled && mw.LayoutBounds.Width > 0)
                {
                    if (!IsWindowVisible(mw.Hwnd))
                    {
                        _focusBackground.SetVisible(mw.Hwnd, false);
                        continue;
                    }
                    var r = mw.LayoutBounds;
                    int x = Math.Max((int)area.X, (int)r.X - expand);
                    int y = Math.Max((int)area.Y, (int)r.Y - expand);
                    int right = Math.Min((int)(area.X + area.Width), (int)(r.X + r.Width + expand));
                    int bottom = Math.Min((int)(area.Y + area.Height), (int)(r.Y + r.Height + expand));
                    _focusBackground.UpdatePosition(mw.Hwnd, x, y, right - x, bottom - y);
                    _focusBackground.SetVisible(mw.Hwnd, true);
                }
                else if (mw.State == Dwalia.Models.WindowLayoutState.Floating)
                {
                    _focusBackground.SetVisible(mw.Hwnd, IsWindowVisible(mw.Hwnd));
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

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }
}
