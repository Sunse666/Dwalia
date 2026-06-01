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

    public IntPtr GetHwnd() => _hwnd;

    public MainWindow(WindowManager wm, WindowEventHookManager ehm)
    {
        InitializeComponent();
        _windowManager = wm;
        _windowEventHookManager = ehm;

        _windowManager.WindowsChanged += (_, _) => UpdateTaskBar();

        if (ServiceLocator.TryResolve<FocusMgr>(out var fm))
            fm.FocusChanged += UpdateTaskBar;

        if (ServiceLocator.TryResolve<WorkspaceManager>(out var wsm))
            wsm.WorkspaceChanged += (_, _) => { UpdateTaskBar(); UpdateStatus(); UpdateWorkspacePills(); };

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
    }

    private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return (IntPtr)MA_NOACTIVATE;
        }
        if (msg == WM_HOTKEY)
        {
            var hotkeyId = wParam.ToInt32();
            Logger.Info($"WM_HOTKEY received: id={hotkeyId}");
            if (ServiceLocator.TryResolve<HotKeyManager>(out var hkm))
            {
                if (hkm.HandleHotKeyMessage(hotkeyId))
                    handled = true;
            }
            else
            {
                Logger.Warn("WM_HOTKEY: HotKeyManager not found in ServiceLocator");
            }
        }
        return IntPtr.Zero;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); LayoutLabel.Text = _layoutLabelText; };

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
        UpdateStatus();
    }

    private string _layoutLabelText = "MasterStack";

    private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        Logger.Info("Shutting down — restoring all windows...");
        _windowEventHookManager.Stop();
        _windowManager.RestoreAllWindows();
    }

    private void UpdateStatus()
    {
        if (ServiceLocator.TryResolve<HotKeyManager>(out var hkm) && hkm.FailedRegistrations.Count > 0)
        {
            StatusText.Text = $"WARNING: {hkm.FailedRegistrations.Count} hotkey(s) failed: {string.Join(", ", hkm.FailedRegistrations)}";
        }
        else
        {
            StatusText.Text = "";
        }
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
        UpdateStatus();
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
        if (!ServiceLocator.TryResolve<HotKeyManager>(out var hkm)) return;

        uint mod = 0;
        var m = Keyboard.Modifiers;
        if ((m & ModifierKeys.Alt) != 0) mod |= MOD_ALT;
        if ((m & ModifierKeys.Control) != 0) mod |= MOD_CONTROL;
        if ((m & ModifierKeys.Shift) != 0) mod |= MOD_SHIFT;
        if ((m & ModifierKeys.Windows) != 0) mod |= MOD_WIN;

        uint vk = KeyToVk(e.Key);
        if (vk == 0) return;

        if (hkm.TryMatchCommand(mod, vk, out var cmd))
        {
            e.Handled = true;
            Execute(cmd);
        }
    }

    private void Execute(DwaliaCommand cmd)
    {
        if (!ServiceLocator.TryResolve<WorkspaceManager>(out var ws)) return;
        if (!ServiceLocator.TryResolve<FocusMgr>(out var fm)) return;
        if (!ServiceLocator.TryResolve<LayoutManager>(out var lm)) return;
        var terminal = ServiceLocator.TryResolve<Dwalia.Configuration.DwaliaConfig>(out var c) ? c.LaunchTerminal : "wt.exe";

        CommandDispatcher.Execute(cmd, ws, fm, lm, terminal,
            openSettings: () => new SettingsWindow { Owner = this }.ShowDialog(),
            quit: () => System.Windows.Application.Current.Shutdown());
    }

    private static uint KeyToVk(Key key) => key switch
    {
        Key.A => VK_A, Key.B => VK_B, Key.C => VK_C, Key.D => VK_D, Key.E => VK_E,
        Key.F => VK_F, Key.G => VK_G, Key.H => VK_H, Key.I => VK_I, Key.J => VK_J,
        Key.K => VK_K, Key.L => VK_L, Key.M => VK_M, Key.N => VK_N, Key.O => VK_O,
        Key.P => VK_P, Key.Q => VK_Q, Key.R => VK_R, Key.S => VK_S, Key.T => VK_T,
        Key.U => VK_U, Key.V => VK_V, Key.W => VK_W, Key.X => VK_X, Key.Y => VK_Y,
        Key.Z => VK_Z,
        Key.D0 => VK_0, Key.D1 => VK_1, Key.D2 => VK_2, Key.D3 => VK_3, Key.D4 => VK_4,
        Key.D5 => VK_5, Key.D6 => VK_6, Key.D7 => VK_7, Key.D8 => VK_8, Key.D9 => VK_9,
        Key.Enter => VK_RETURN, Key.Space => VK_SPACE,
        _ => 0
    };
}
