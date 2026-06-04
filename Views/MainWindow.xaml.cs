using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
    private Color _ctxMenuBg = Color.FromRgb(0x2d, 0x2d, 0x2d);
    private Color _ctxMenuFg = Color.FromRgb(0xcc, 0xcc, 0xcc);
    private Color _ctxMenuBorder = Color.FromRgb(0x44, 0x44, 0x44);
    private Color _taskBtnBg = Color.FromRgb(0x24, 0x28, 0x3e);
    private Color _monitorBarBg = Color.FromArgb(0x55, 0x16, 0x16, 0x1e);
    private Color _monitorBarBorder = Color.FromRgb(0x3b, 0x42, 0x61);
    private Color _pillInactive = Color.FromRgb(0x56, 0x5f, 0x89);
    private Color _pillEmpty = Color.FromRgb(0x2b, 0x2f, 0x44);
    private bool _showPillCount;
    private Color _widgetPillBg = Color.FromArgb(0xFF, 0x1a, 0x1a, 0x2e);
    private PerformanceCounter? _netDownCounter;
    private PerformanceCounter? _netUpCounter;
    private string _netDownText = "";
    private string _netUpText = "";
    private string _cachedMediaText = "";
    private string _mediaScript = "";
    private int _mediaScriptInterval = 3;
    private int _mediaTick;
    private int _mediaScrollPos;
    private int _mediaScrollDir = 1;
    private bool _mediaPaused;
    private readonly List<System.Windows.Controls.Border> _monitorBars = new();
    private NOTIFYICONDATA _trayData;
    private bool _trayCreated;

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

        Left = System.Windows.SystemParameters.VirtualScreenLeft;
        Top = System.Windows.SystemParameters.VirtualScreenTop;
        Width = System.Windows.SystemParameters.VirtualScreenWidth;
        Height = System.Windows.SystemParameters.VirtualScreenHeight;

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
            SWP_NOZORDER | SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

        CreateTrayIcon();

        if (ServiceLocator.TryResolve<WindowEventHookManager>(out var ehm))
            ehm.SetMainWindowHwnd(_hwnd);

        if (ServiceLocator.TryResolve<HotKeyManager>(out var hkm))
        {
            hkm.Initialize(_hwnd);
            Logger.Info($"HotKeyManager initialized: {hkm.RegisteredCount} registered, {hkm.FailedRegistrations.Count} failed");

            hkm.ResizeModeChanged += (_, inResize) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (inResize)
                    {
                        LayoutLabel.Text = "RESIZE";
                        LayoutLabel.Foreground = new SolidColorBrush(Colors.Yellow);
                    }
                    else
                    {
                        LayoutLabel.Text = _layoutLabelText;
                        LayoutLabel.Foreground = new SolidColorBrush(_mutedColor);
                    }
                }));
            };
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

            ParseColor(cfg.Theme.ContextMenuBackground, ref _ctxMenuBg, 0x2d, 0x2d, 0x2d);
            ParseColor(cfg.Theme.ContextMenuForeground, ref _ctxMenuFg, 0xcc, 0xcc, 0xcc);
            ParseColor(cfg.Theme.ContextMenuBorder, ref _ctxMenuBorder, 0x44, 0x44, 0x44);
            ParseColor(cfg.Theme.TaskButtonBackground, ref _taskBtnBg, 0x24, 0x28, 0x3e);
            ParseColor(cfg.Theme.MonitorBarBackground, ref _monitorBarBg, null, null, null);
            if (!string.IsNullOrEmpty(cfg.Theme.MonitorBarBorder))
                ParseColor(cfg.Theme.MonitorBarBorder, ref _monitorBarBorder, null, null, null);
            else
                _monitorBarBorder = _mutedColor;
            if (!string.IsNullOrEmpty(cfg.Theme.WorkspacePillInactive))
                ParseColor(cfg.Theme.WorkspacePillInactive, ref _pillInactive, null, null, null);
            else
                _pillInactive = _mutedColor;
            if (!string.IsNullOrEmpty(cfg.Theme.WorkspacePillEmpty))
                ParseColor(cfg.Theme.WorkspacePillEmpty, ref _pillEmpty, null, null, null);
            else
                _pillEmpty = Color.FromRgb((byte)(_mutedColor.R / 2), (byte)(_mutedColor.G / 2), (byte)(_mutedColor.B / 2));

            _showPillCount = cfg.Theme.WorkspacePillShowCount;
            ParseColor(cfg.Theme.WidgetPillBackground, ref _widgetPillBg, 0x1a, 0x1a, 0x2e);

            var barH = Math.Clamp(cfg.General.BarHeight, 16, 80);
            TaskBar.Height = barH;
            InfoBar.Height = barH;
            LauncherBar.Height = barH;

            var fontSize = Math.Clamp(cfg.Theme.FontSize, 8, 24);
            var fontFamily = new FontFamily(cfg.Theme.BarFont);
            TaskBar.SetValue(TextElement.FontSizeProperty, (double)fontSize);
            TaskBar.SetValue(TextElement.FontFamilyProperty, fontFamily);
            InfoBar.SetValue(TextElement.FontSizeProperty, (double)fontSize);
            InfoBar.SetValue(TextElement.FontFamilyProperty, fontFamily);
            LauncherBar.SetValue(TextElement.FontSizeProperty, (double)fontSize);
            LauncherBar.SetValue(TextElement.FontFamilyProperty, fontFamily);
            LayoutLabel.FontSize = fontSize;
            LayoutLabel.FontWeight = FontWeights.SemiBold;

            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);

            ClockText.FontWeight = FontWeights.SemiBold;
            InfoWinTitle.FontWeight = FontWeights.Medium;
            LayoutLabel.Foreground = new SolidColorBrush(_mutedColor);
            LayoutLabel.Cursor = System.Windows.Input.Cursors.Hand;
            LayoutLabel.MouseLeftButtonDown += (_, _) =>
            {
                if (ServiceLocator.TryResolve<LayoutManager>(out var llm))
                    llm.CycleLayout();
            };
            LayoutLabel.MouseEnter += (_, _) =>
                LayoutLabel.Foreground = new SolidColorBrush(_foregroundColor);
            LayoutLabel.MouseLeave += (_, _) =>
                LayoutLabel.Foreground = new SolidColorBrush(_mutedColor);

            var barPos = cfg.General.BarPosition?.ToLowerInvariant() ?? "top";
            if (barPos == "bottom")
            {
                System.Windows.Controls.Grid.SetRow(TaskBar, 1);
                System.Windows.Controls.Grid.SetRow(InfoBar, 1);
                System.Windows.Controls.Grid.SetRow(LauncherBar, 1);
                System.Windows.Controls.Grid.SetRow(OverlayGrid, 0);
            }
        }
        else
        {
            _focusBgColor = Color.FromRgb(0x7a, 0xa2, 0xf7);
            _foregroundColor = Color.FromRgb(0xc0, 0xca, 0xf5);
            _mutedColor = Color.FromRgb(0x56, 0x5f, 0x89);
            _monitorBarBorder = _mutedColor;
            _pillInactive = _mutedColor;
            _pillEmpty = Color.FromRgb(0x2b, 0x2f, 0x44);
        }

        var focusRadius = cfg?.Theme.FocusRadius ?? 8;
        var focusActiveOp = cfg?.Theme.FocusActiveOpacity ?? 0.27;
        var focusInactiveOp = cfg?.Theme.FocusInactiveOpacity ?? 0.09;
        var focusFill = cfg?.Theme.FocusFill ?? true;

        _focusBackground = new FocusBackground(_focusBgColor, focusRadius, focusActiveOp, focusInactiveOp, focusFill);
        ServiceLocator.Register(_focusBackground);

        _focusBackground.SwapDrop += (_, swap) =>
            Dispatcher.BeginInvoke(new Action(() => HandleFocusSwapDrop(swap.SrcHwnd, swap.DstHwnd)));

        if (ServiceLocator.TryResolve<HotKeyManager>(out var hkmDrag))
        {
            hkmDrag.ResizeModeChanged += (_, inResize) =>
                Dispatcher.Invoke(new Action(() => _focusBackground?.SetDragMode(inResize)));
        }

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
            if (_focusBackground != null && _focusBackground.DragMode) return IntPtr.Zero;
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
        if (msg == WM_TRAYICON)
        {
            var mouseMsg = lParam.ToInt32();
            if (mouseMsg == WM_RBUTTONUP || mouseMsg == WM_CONTEXTMENU)
            {
                ShowTrayContextMenu();
                handled = true;
            }
            else if (mouseMsg == WM_LBUTTONDBLCLK)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    Show();
                    WindowState = WindowState.Normal;
                });
                handled = true;
            }
            return IntPtr.Zero;
        }
        if (msg == WM_DISPLAYCHANGE)
        {
            Logger.Info("Display change detected, re-enumerating monitors");
            Dispatcher.BeginInvoke(() => HandleDisplayChange());
            handled = true;
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
            var barH = TaskBar.Height;
            var barTop = true;
            if (ServiceLocator.TryResolve<ConfigRoot>(out var acfg))
                barTop = (acfg.General.BarPosition?.ToLowerInvariant() ?? "top") != "bottom";
            lm.SetArea(_hwnd, barTop ? barH : 0, barH);
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
        _netDownCounter?.Dispose();
        _netUpCounter?.Dispose();
        _colorFilter?.Dispose();
        DwmFlush();
        _windowEventHookManager.Stop();
        _windowManager.RestoreAllWindows();
        _focusBackground?.Dispose();
        RemoveTrayIcon();
        DwmFlush();
    }

    private void CreateTrayIcon()
    {
        try
        {
            var iconHandle = System.Drawing.Icon.ExtractAssociatedIcon(
                System.Reflection.Assembly.GetExecutingAssembly().Location)?.Handle
                ?? System.Drawing.SystemIcons.Application.Handle;

            _trayData = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1,
                uFlags = NIF_ICON | NIF_TIP | NIF_MESSAGE,
                uCallbackMessage = WM_TRAYICON,
                hIcon = iconHandle,
                szTip = "Dwalia Window Manager"
            };

            Shell_NotifyIcon(NIM_ADD, ref _trayData);
            _trayCreated = true;
            Logger.Info("System tray icon created");
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to create tray icon: {ex.Message}");
        }
    }

    private void ShowTrayContextMenu()
    {
        var menu = new ContextMenu
        {
            Background = new SolidColorBrush(_ctxMenuBg),
            Foreground = new SolidColorBrush(_ctxMenuFg),
            BorderBrush = new SolidColorBrush(_ctxMenuBorder)
        };

        if (Visibility == Visibility.Visible)
        {
            var hideItem = new MenuItem { Header = "Hide Dwalia" };
            hideItem.Click += (_, _) => Hide();
            menu.Items.Add(hideItem);
        }
        else
        {
            var showItem = new MenuItem { Header = "Show Dwalia" };
            showItem.Click += (_, _) => { Show(); WindowState = WindowState.Normal; };
            menu.Items.Add(showItem);
        }

        var quitItem = new MenuItem { Header = "Quit" };
        quitItem.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(quitItem);

        menu.IsOpen = true;
    }

    private void RemoveTrayIcon()
    {
        if (!_trayCreated) return;
        Shell_NotifyIcon(NIM_DELETE, ref _trayData);
        _trayCreated = false;
        Logger.Info("System tray icon removed");
    }

    private void HandleFocusSwapDrop(IntPtr srcHwnd, IntPtr dstHwnd)
    {
        if (!ServiceLocator.TryResolve<WindowManager>(out var wm)) return;
        if (!ServiceLocator.TryResolve<LayoutManager>(out var lm)) return;

        var srcMw = wm.GetManagedWindow(srcHwnd);
        if (srcMw == null || srcMw.State != Models.WindowLayoutState.Tiled) return;

        var dstMw = wm.GetManagedWindow(dstHwnd);
        if (dstMw == null || dstMw.State != Models.WindowLayoutState.Tiled) return;

        if (srcMw.WorkspaceId != dstMw.WorkspaceId || srcMw.MonitorId != dstMw.MonitorId) return;

        lm.SwapWindows(srcMw, dstMw);

        if (ServiceLocator.TryResolve<HotKeyManager>(out var hkm))
            hkm.ExitResizeMode();
    }

    private void UpdateTaskBar()
    {
        TaskBarItems.Items.Clear();

        if (!ServiceLocator.TryResolve<WorkspaceManager>(out var wsm)) return;
        var activeWs = wsm.GetActiveWorkspace();
        if (activeWs == null) return;

        var taskBarWindows = activeWs.Windows.ToList();
        foreach (var otherWs in wsm.Workspaces)
        {
            if (otherWs.Id == activeWs.Id) continue;
            foreach (var w in otherWs.Windows.Where(w => w.IsSticky))
            {
                if (!taskBarWindows.Contains(w))
                    taskBarWindows.Add(w);
            }
        }
        if (taskBarWindows.Count == 0) { UpdateWorkspacePills(); return; }

        ServiceLocator.TryResolve<FocusMgr>(out var fm);

        int startIdx = 0;
        if (fm?.ActiveWindow != null)
        {
            var idx = taskBarWindows.IndexOf(fm.ActiveWindow);
            if (idx >= 0) startIdx = idx;
        }

        var count = taskBarWindows.Count;
        for (int i = 0; i < count; i++)
        {
            var mw = taskBarWindows[(startIdx + i) % count];
            var shortTitle = mw.Title.Length > 25 ? mw.Title[..25] + "..." : mw.Title;
            var hwnd = mw.Hwnd;
            var captured = mw;

            var container = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(2, 0, 2, 0),
            };

            var pillBorder = new Border
            {
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(_taskBtnBg),
                Padding = new Thickness(0),
                Height = 28,
            };
            var pillStack = new StackPanel { Orientation = Orientation.Horizontal };

            var btn = new Button
            {
                Content = shortTitle,
                Height = 28,
                Padding = new Thickness(12, 0, 2, 0),
                FontSize = 11,
                Foreground = mw.IsActive
                    ? new SolidColorBrush(_focusBgColor)
                    : new SolidColorBrush(_foregroundColor),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                ToolTip = mw.Title,
                Cursor = System.Windows.Input.Cursors.Hand,
            };

            var btnBg = new SolidColorBrush(_taskBtnBg);
            var btnHoverBg = new SolidColorBrush(Color.FromArgb(
                (byte)(_taskBtnBg.A), (byte)Math.Min(_taskBtnBg.R + 20, 255),
                (byte)Math.Min(_taskBtnBg.G + 20, 255), (byte)Math.Min(_taskBtnBg.B + 20, 255)));

            btn.MouseEnter += (_, _) => pillBorder.Background = btnHoverBg;
            btn.MouseLeave += (_, _) => pillBorder.Background = btnBg;

            btn.Click += (_, _) =>
            {
                ShowWindow(hwnd, SW_RESTORE);
                SetForegroundWindow(hwnd);
                fm?.SetActiveWindow(captured);
            };

            btn.MouseDown += (_, e) =>
            {
                if (e.MiddleButton == MouseButtonState.Pressed)
                    PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            };

            btn.ContextMenu = BuildWindowContextMenu(captured);
            pillStack.Children.Add(btn);

            var closeBtn = new Button
            {
                Content = "✕",
                Width = 22, Height = 22,
                Padding = new Thickness(0),
                FontSize = 9,
                Foreground = new SolidColorBrush(_mutedColor),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                ToolTip = "Close (middle-click)",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            closeBtn.MouseEnter += (_, _) =>
                closeBtn.Foreground = new SolidColorBrush(Colors.IndianRed);
            closeBtn.MouseLeave += (_, _) =>
                closeBtn.Foreground = new SolidColorBrush(_mutedColor);
            closeBtn.Click += (_, _) =>
                PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            pillStack.Children.Add(closeBtn);

            pillBorder.Child = pillStack;
            container.Children.Add(pillBorder);

            TaskBarItems.Items.Add(container);
        }
        UpdateWorkspacePills();
    }

    private ContextMenu BuildWindowContextMenu(Dwalia.Models.ManagedWindow mw)
    {
        var menu = new ContextMenu
        {
            Background = new SolidColorBrush(_ctxMenuBg),
            Foreground = new SolidColorBrush(_ctxMenuFg),
            BorderBrush = new SolidColorBrush(_ctxMenuBorder)
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

        var stickyItem = new MenuItem
        {
            Header = mw.IsSticky ? "Unstick" : "Make Sticky",
            IsChecked = mw.IsSticky
        };
        stickyItem.Click += (_, _) =>
        {
            if (ServiceLocator.TryResolve<WorkspaceManager>(out var wsm2))
                wsm2.ToggleSticky(mw);
            if (ServiceLocator.TryResolve<LayoutManager>(out var lm2))
                lm2.Relayout();
        };
        menu.Items.Add(stickyItem);

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

        ParseColor(c.Theme.ContextMenuBackground, ref _ctxMenuBg, 0x2d, 0x2d, 0x2d);
        ParseColor(c.Theme.ContextMenuForeground, ref _ctxMenuFg, 0xcc, 0xcc, 0xcc);
        ParseColor(c.Theme.ContextMenuBorder, ref _ctxMenuBorder, 0x44, 0x44, 0x44);
        ParseColor(c.Theme.TaskButtonBackground, ref _taskBtnBg, 0x24, 0x28, 0x3e);
        ParseColor(c.Theme.MonitorBarBackground, ref _monitorBarBg, null, null, null);
        if (!string.IsNullOrEmpty(c.Theme.MonitorBarBorder))
            ParseColor(c.Theme.MonitorBarBorder, ref _monitorBarBorder, null, null, null);
        else
            _monitorBarBorder = _mutedColor;
        if (!string.IsNullOrEmpty(c.Theme.WorkspacePillInactive))
            ParseColor(c.Theme.WorkspacePillInactive, ref _pillInactive, null, null, null);
        else
            _pillInactive = _mutedColor;
        if (!string.IsNullOrEmpty(c.Theme.WorkspacePillEmpty))
            ParseColor(c.Theme.WorkspacePillEmpty, ref _pillEmpty, null, null, null);
        else
            _pillEmpty = Color.FromRgb((byte)(_mutedColor.R / 2), (byte)(_mutedColor.G / 2), (byte)(_mutedColor.B / 2));

        _showPillCount = c.Theme.WorkspacePillShowCount;
        ParseColor(c.Theme.WidgetPillBackground, ref _widgetPillBg, 0x1a, 0x1a, 0x2e);
        var wbg = new SolidColorBrush(_widgetPillBg);
        CpuPill.Background = wbg;
        MemPill.Background = wbg;
        BatteryPill.Background = wbg;
        NetworkPill.Background = wbg;
        MediaPill.Background = wbg;

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

        LayoutLabel.FontWeight = FontWeights.SemiBold;
        LayoutLabel.Foreground = new SolidColorBrush(_mutedColor);
        InfoWinTitle.FontWeight = FontWeights.Medium;
        InfoWinTitle.Foreground = new SolidColorBrush(_foregroundColor);
        ClockText.FontWeight = FontWeights.SemiBold;
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
        var next = modes[(idx + direction + modes.Length) % modes.Length];
        ShowBarMode(next);
    }

    public void ToggleBar()
    {
        var anyVisible = TaskBar.Visibility == Visibility.Visible
                      || InfoBar.Visibility == Visibility.Visible
                      || LauncherBar.Visibility == Visibility.Visible;
        if (anyVisible)
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
        var prevMode = _barMode;
        _barMode = mode;
        var duration = TimeSpan.FromMilliseconds(150);

        TaskBar.Visibility = mode == BarMode.Docker ? Visibility.Visible : Visibility.Collapsed;
        InfoBar.Visibility = mode == BarMode.Info ? Visibility.Visible : Visibility.Collapsed;
        LauncherBar.Visibility = mode == BarMode.Launcher ? Visibility.Visible : Visibility.Collapsed;

        if (prevMode != mode)
        {
            var nextBar = mode switch { BarMode.Info => InfoBar, BarMode.Launcher => LauncherBar, _ => TaskBar };
            FadeInBar(nextBar, duration);
        }

        _infoTimer?.Stop();
        if (mode == BarMode.Info)
        {
            if (ServiceLocator.TryResolve<ConfigRoot>(out var icfg))
            {
                ClockText.Visibility = icfg.Theme.StatusShowClock ? Visibility.Visible : Visibility.Collapsed;
                CpuPill.Visibility = icfg.Theme.StatusShowCpu ? Visibility.Visible : Visibility.Collapsed;
                MemPill.Visibility = icfg.Theme.StatusShowMem ? Visibility.Visible : Visibility.Collapsed;
                BatteryPill.Visibility = icfg.Theme.StatusShowBattery ? Visibility.Visible : Visibility.Collapsed;
                NetworkPill.Visibility = icfg.Theme.StatusShowNetwork ? Visibility.Visible : Visibility.Collapsed;
                MediaPill.Visibility = icfg.Theme.StatusShowMedia ? Visibility.Visible : Visibility.Collapsed;
                _mediaScript = icfg.Theme.MediaScript ?? "";
                _mediaScriptInterval = Math.Max(1, icfg.Theme.MediaScriptInterval);

                var wbg = new SolidColorBrush(_widgetPillBg);
                CpuPill.Background = wbg;
                MemPill.Background = wbg;
                BatteryPill.Background = wbg;
                NetworkPill.Background = wbg;
                MediaPill.Background = wbg;
            }
            InitNetworkCounters();
            InitMediaMonitor();
            UpdateInfoBar();
            _infoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _infoTimer.Tick += (_, _) => UpdateInfoBar();
            _infoTimer.Start();
        }

        if (mode == BarMode.Launcher)
            BuildLauncherButtons();

        UpdateBarArea();
    }

    private static void FadeInBar(UIElement el, TimeSpan duration)
    {
        el.Opacity = 0;
        var anim = new System.Windows.Media.Animation.DoubleAnimation(1.0, duration)
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
            }
        };
        el.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    private void UpdateBarArea()
    {
        bool visible = TaskBar.Visibility == Visibility.Visible
                    || InfoBar.Visibility == Visibility.Visible
                    || LauncherBar.Visibility == Visibility.Visible;
        if (ServiceLocator.TryResolve<LayoutManager>(out var lm))
        {
            var barH = visible ? TaskBar.Height : 0;
            var barTop = true;
            if (ServiceLocator.TryResolve<ConfigRoot>(out var cfg))
                barTop = (cfg.General.BarPosition?.ToLowerInvariant() ?? "top") != "bottom";
            lm.SetArea(_hwnd, barTop ? barH : 0, barH);
        }
    }

    private void UpdateInfoBar()
    {
        if (ServiceLocator.TryResolve<ConfigRoot>(out var icfg))
        {
            try { ClockText.Text = DateTime.Now.ToString(icfg.Theme.DateFormat); }
            catch { ClockText.Text = DateTime.Now.ToString("HH:mm:ss  yyyy-MM-dd"); }
        }

        if (ServiceLocator.TryResolve<FocusMgr>(out var fm) && fm.ActiveWindow != null)
            InfoWinTitle.Text = fm.ActiveWindow.Title;
        else
            InfoWinTitle.Text = "";

        try { CpuText.Text = CpuBar((int)GetCpuUsage()); }
        catch { CpuText.Text = " --%"; }

        try { MemText.Text = MemBar((int)GetMemUsage()); }
        catch { MemText.Text = " --%"; }

        try { BatteryText.Text = GetBatteryIcon(); }
        catch { BatteryText.Text = "🔋 --%"; }

        try
        {
            UpdateNetworkSpeeds();
            NetDownText.Text = _netDownText;
            NetUpText.Text = _netUpText;
        }
        catch { }

        _mediaTick++;
        if (_mediaTick % _mediaScriptInterval == 0)
            UpdateMediaInfo();
        MarqueeMedia();
    }

    private static string CpuBar(int pct) =>
        $"◈ {pct,3}%{ProgressBar(pct)}";

    private static string MemBar(int pct) =>
        $"◉ {pct,3}%{ProgressBar(pct)}";

    private static string ProgressBar(int pct)
    {
        int filled = Math.Clamp(pct * 8 / 100, 0, 8);
        return new string('▰', filled) + new string('▱', 8 - filled);
    }

    private static string GetBatteryIcon()
    {
        if (!GetSystemPowerStatus(out var ps)) return "🔋 --%";
        if (ps.BatteryFlag == 128) return "⚡ AC";
        var pct = ps.BatteryLifePercent;
        if (pct > 100) return "🔋 --%";
        string icon = pct switch
        {
            >= 90 => "🔋",
            >= 60 => "🔋",
            >= 30 => "🔋",
            >= 10 => "🪫",
            _ => "🪫"
        };
        return $"{icon} {pct,3}%";
    }

    private static string GetBatteryText() => GetBatteryIcon();

    private void InitNetworkCounters()
    {
        try
        {
            var cat = new PerformanceCounterCategory("Network Interface");
            var instances = cat.GetInstanceNames();
            var iface = instances.FirstOrDefault(i =>
                i.StartsWith("Ethernet") || i.StartsWith("Wi-Fi") || i.StartsWith("WLAN"))
                ?? instances.FirstOrDefault()
                ?? "";
            if (!string.IsNullOrEmpty(iface))
            {
                _netDownCounter = new PerformanceCounter("Network Interface", "Bytes Received/sec", iface);
                _netUpCounter = new PerformanceCounter("Network Interface", "Bytes Sent/sec", iface);
                _netDownCounter.NextValue();
                _netUpCounter.NextValue();
            }
        }
        catch { }
    }

    private void UpdateNetworkSpeeds()
    {
        try
        {
            if (_netDownCounter == null || _netUpCounter == null) return;
            float down = _netDownCounter.NextValue();
            float up = _netUpCounter.NextValue();
            _netDownText = FormatSpeed(down);
            _netUpText = FormatSpeed(up);
        }
        catch { }
    }

    private static string FormatSpeed(float bps)
    {
        if (bps < 1024) return $"{bps,4:F0} B";
        if (bps < 1024 * 1024) return $"{bps / 1024,4:F1}K";
        return $"{bps / (1024 * 1024),4:F1}M";
    }

    private void InitMediaMonitor()
    {
        if (!string.IsNullOrEmpty(_mediaScript)) { PollMediaScript(); return; }
        Task.Run(async () =>
        {
            try
            {
                var manager = await Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                void OnSessionChanged(Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager _, object _a)
                    => Dispatcher.Invoke(() => { UpdateMediaInfo(); AttachSessionEvents(manager.GetCurrentSession()); });
                manager.SessionsChanged += OnSessionChanged;
                AttachSessionEvents(manager.GetCurrentSession());
                Dispatcher.Invoke(() => UpdateMediaInfo());
            }
            catch { }
        });
    }

    private void AttachSessionEvents(Windows.Media.Control.GlobalSystemMediaTransportControlsSession? session)
    {
        if (session == null) return;
        session.MediaPropertiesChanged += (_, _) => Dispatcher.Invoke(() => UpdateMediaInfo());
        session.PlaybackInfoChanged += (_, _) => Dispatcher.Invoke(() =>
        {
            var info = session.GetPlaybackInfo();
            _mediaPaused = info.PlaybackStatus != Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            UpdateMediaInfo();
        });
    }

    private void PollMediaScript()
    {
        Task.Run(() =>
        {
            try
            {
                using var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c {_mediaScript}",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true,
                    }
                };
                proc.Start();
                var output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit(3000);
                if (!proc.HasExited) { proc.Kill(); return; }
                Dispatcher.Invoke(() =>
                {
                    var oldText = _cachedMediaText;
                    _cachedMediaText = output;
                    _mediaPaused = string.IsNullOrEmpty(output);
                    if (_cachedMediaText != oldText) _mediaScrollPos = 0;
                });
            }
            catch { }
        });
    }

    private void UpdateMediaInfo()
    {
        if (!string.IsNullOrEmpty(_mediaScript)) { PollMediaScript(); return; }
        try
        {
            var manager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager
                .RequestAsync().GetAwaiter().GetResult();
            var session = manager.GetCurrentSession();
            if (session == null) { _cachedMediaText = ""; _mediaPaused = true; return; }
            var pb = session.GetPlaybackInfo();
            _mediaPaused = pb.PlaybackStatus != Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var info = session.TryGetMediaPropertiesAsync().GetAwaiter().GetResult();
            if (info != null)
            {
                var title = info.Title ?? "";
                var artist = info.Artist ?? "";
                var old = _cachedMediaText;
                _cachedMediaText = string.IsNullOrEmpty(artist) ? title : $"♫ {artist} - {title}";
                if (_cachedMediaText != old) _mediaScrollPos = 0;
            }
        }
        catch { _cachedMediaText = ""; _mediaPaused = true; }
    }

    private void MarqueeMedia()
    {
        if (string.IsNullOrEmpty(_cachedMediaText))
        {
            MediaText.Text = "";
            return;
        }

        const int maxDisplay = 28;
        var text = _cachedMediaText;

        if (text.Length <= maxDisplay)
        {
            MediaText.Text = text;
            return;
        }

        if (_mediaPaused)
        {
            MediaText.Text = text.Length > maxDisplay ? text[..maxDisplay] : text;
            return;
        }

        _mediaScrollPos += _mediaScrollDir;
        if (_mediaScrollPos < 0)
        {
            _mediaScrollPos = 0;
            _mediaScrollDir = 1;
        }
        else if (_mediaScrollPos > text.Length - maxDisplay)
        {
            _mediaScrollPos = text.Length - maxDisplay;
            _mediaScrollDir = -1;
        }

        var display = text.Substring(_mediaScrollPos, Math.Min(maxDisplay, text.Length - _mediaScrollPos));
        MediaText.Text = display.PadRight(maxDisplay);
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

            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };

            try
            {
                var icon = System.Drawing.Icon.ExtractAssociatedIcon(entry.Path);
                if (icon != null)
                {
                    var img = new System.Windows.Controls.Image
                    {
                        Source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                            icon.Handle, Int32Rect.Empty,
                            System.Windows.Media.Imaging.BitmapSizeOptions.FromWidthAndHeight(16, 16)),
                        Width = 16, Height = 16,
                        Margin = new Thickness(0, 0, 6, 0),
                    };
                    content.Children.Add(img);
                    icon.Dispose();
                }
            }
            catch { }

            content.Children.Add(new TextBlock
            {
                Text = name,
                VerticalAlignment = VerticalAlignment.Center,
            });

            var pillBorder = new Border
            {
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(_taskBtnBg),
                Height = 28,
                Margin = new Thickness(3, 0, 3, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Child = new Button
                {
                    Content = content,
                    Height = 28,
                    Padding = new Thickness(12, 0, 12, 0),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(_foregroundColor),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                }
            };
            var btn = (Button)pillBorder.Child;
            var cmd = entry.Path;
            var normalBg = new SolidColorBrush(_taskBtnBg);
            var hoverBg = new SolidColorBrush(Color.FromArgb(
                (byte)_taskBtnBg.A, (byte)Math.Min(_taskBtnBg.R + 25, 255),
                (byte)Math.Min(_taskBtnBg.G + 25, 255), (byte)Math.Min(_taskBtnBg.B + 25, 255)));
            pillBorder.MouseEnter += (_, _) => pillBorder.Background = hoverBg;
            pillBorder.MouseLeave += (_, _) => pillBorder.Background = normalBg;
            btn.Click += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo { FileName = cmd, UseShellExecute = true }); }
                catch (Exception ex) { Logger.Warn($"Launch failed: {cmd}: {ex.Message}"); }
            };
            LauncherButtons.Children.Add(pillBorder);
        }
    }

    public static void WarmupInfoBar()
    {
        Task.Run(() =>
        {
            try
            {
                using var cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                using var mem = new PerformanceCounter("Memory", "% Committed Bytes In Use");
                cpu.NextValue();
                mem.NextValue();
                Logger.Info("InfoBar performance counters warmed up");
            }
            catch (Exception ex) { Logger.Warn($"InfoBar warmup failed: {ex.Message}"); }
        });
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

    private static void ParseColor(string hex, ref Color target, byte? rr, byte? gg, byte? bb)
    {
        if (!string.IsNullOrEmpty(hex))
        {
            try { target = (Color)ColorConverter.ConvertFromString(hex); return; }
            catch { }
        }
        if (rr.HasValue)
            target = Color.FromRgb(rr.Value, gg!.Value, bb!.Value);
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
        PopulatePills(WorkspacePills);
        PopulatePills(InfoWorkspacePills);
        PopulatePills(LauncherWorkspacePills);
    }

    private void PopulatePills(Panel panel)
    {
        panel.Children.Clear();
        if (!ServiceLocator.TryResolve<WorkspaceManager>(out var wsm)) return;

        foreach (var ws in wsm.Workspaces)
        {
            var isActive = ws.Id == wsm.ActiveWorkspaceId;
            var hasWindows = ws.Windows.Count > 0;
            Color pillBg;
            if (isActive)
                pillBg = _focusBgColor;
            else if (hasWindows)
                pillBg = _pillInactive;
            else
                pillBg = _pillEmpty;

            var wsId = ws.Id;
            var pill = new Border
            {
                Width = isActive ? 28 : 10,
                Height = 10,
                CornerRadius = new CornerRadius(5),
                Margin = new Thickness(3, 0, 3, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(pillBg),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = $"{wsId + 1}: {ws.Name}  ({ws.Windows.Count} windows)"
            };

            pill.MouseLeftButtonDown += (_, _) =>
            {
                if (ServiceLocator.TryResolve<WorkspaceManager>(out var wsm2))
                    wsm2.SwitchToWorkspace(wsId);
            };

            if (isActive)
            {
                pill.MouseEnter += (_, _) =>
                    pill.Background = new SolidColorBrush(Color.FromArgb(
                        255, (byte)Math.Min(_focusBgColor.R + 30, 255),
                        (byte)Math.Min(_focusBgColor.G + 30, 255),
                        (byte)Math.Min(_focusBgColor.B + 30, 255)));
                pill.MouseLeave += (_, _) =>
                    pill.Background = new SolidColorBrush(_focusBgColor);
            }

            panel.Children.Add(pill);

            if (_showPillCount)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = ws.Windows.Count.ToString(),
                    FontSize = 10,
                    Foreground = isActive
                        ? new SolidColorBrush(_focusBgColor)
                        : new SolidColorBrush(_mutedColor),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 4, 0),
                });
            }
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
        if (_focusBackground.IsDragging) return;
        if (!ServiceLocator.TryResolve<WorkspaceManager>(out var wsm)) return;
        if (!ServiceLocator.TryResolve<LayoutManager>(out var lm)) return;
        if (!ServiceLocator.TryResolve<WindowManager>(out var wm)) return;

        var activeWs = wsm.GetActiveWorkspace();
        if (activeWs == null) return;

        var activeWorkspaceIds = new HashSet<int>();
        if (ServiceLocator.TryResolve<MonitorManager>(out var mm) && mm.MonitorCount > 0)
        {
            foreach (var m in mm.Monitors)
                activeWorkspaceIds.Add(wsm.GetActiveWorkspaceIdForMonitor(m.Id));
        }
        else
        {
            activeWorkspaceIds.Add(activeWs.Id);
        }

        var area = lm.Area;
        int gap = 4;
        if (ServiceLocator.TryResolve<ConfigRoot>(out var cfg))
            gap = cfg.Layout.InnerGap;

        int expand = gap / 2;

        foreach (var mw in wm.ManagedWindows.Values)
        {
            bool onActiveWs = activeWorkspaceIds.Contains(mw.WorkspaceId);
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
        if (_focusBackground.IsDragging) return;
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

    private void HandleDisplayChange()
    {
        if (ServiceLocator.TryResolve<MonitorManager>(out var mm))
            mm.RefreshMonitors();

        Left = System.Windows.SystemParameters.VirtualScreenLeft;
        Top = System.Windows.SystemParameters.VirtualScreenTop;
        Width = System.Windows.SystemParameters.VirtualScreenWidth;
        Height = System.Windows.SystemParameters.VirtualScreenHeight;

        RebuildMonitorBars();
        UpdateBarArea();

        if (ServiceLocator.TryResolve<LayoutManager>(out var lm))
            lm.Relayout();
    }

    public void RebuildMonitorBars()
    {
        foreach (var bar in _monitorBars)
        {
            OverlayGrid.Children.Remove(bar);
        }
        _monitorBars.Clear();

        if (!ServiceLocator.TryResolve<MonitorManager>(out var mm)) return;
        if (mm.MonitorCount <= 1) return;
        if (ServiceLocator.TryResolve<ConfigRoot>(out var mcfg) && !mcfg.Monitor.MonitorBarEnabled) return;

        foreach (var monitor in mm.Monitors)
        {
            if (monitor.IsPrimary) continue;

            var bar = new System.Windows.Controls.Border
            {
                Height = 28,
                Background = new SolidColorBrush(_monitorBarBg),
                BorderBrush = new SolidColorBrush(_monitorBarBorder),
                BorderThickness = new Thickness(0, 0, 0, 1),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
            };

            var stack = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
            };

            if (ServiceLocator.TryResolve<WorkspaceManager>(out var wsm))
            {
                foreach (var ws in wsm.Workspaces)
                {
                    var activeWsId = wsm.GetActiveWorkspaceIdForMonitor(monitor.Id);
                    var isActive = ws.Id == activeWsId;
                    var hasWindows = ws.Windows.Count > 0;
                    Color pillColor;
                    if (isActive) pillColor = _focusBgColor;
                    else if (hasWindows) pillColor = _pillInactive;
                    else pillColor = _pillEmpty;
                    var pill = new System.Windows.Controls.Border
                    {
                        Width = isActive ? 16 : 6, Height = 6,
                        CornerRadius = new CornerRadius(3),
                        Margin = new Thickness(2, 0, 2, 0),
                        Background = new SolidColorBrush(pillColor)
                    };
                    stack.Children.Add(pill);
                }
            }

            bar.Child = stack;

            double left = monitor.WorkArea.Left - System.Windows.SystemParameters.VirtualScreenLeft;
            double top = monitor.WorkArea.Top - System.Windows.SystemParameters.VirtualScreenTop;
            bar.Width = monitor.WorkArea.Width;
            bar.Margin = new Thickness(left, top, 0, 0);

            if (OverlayGrid.Children.Count > 1)
                OverlayGrid.Children.Insert(OverlayGrid.Children.Count - 1, bar);
            else
                OverlayGrid.Children.Add(bar);

            _monitorBars.Add(bar);
        }
    }
}
