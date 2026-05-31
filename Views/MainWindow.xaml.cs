using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
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
        if (ServiceLocator.TryResolve<LayoutManager>(out var lm))
        {
            lm.SetArea(_hwnd, TaskBar.Height);
            lm.LayoutChanged += (_, layout) => LayoutLabel.Text = layout.ToString();
        }

        _windowEventHookManager.Start();
        _windowManager.Initialize();
        UpdateStatus();
    }

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

            TaskBarItems.Items.Add(btn);
        }
        UpdateStatus();
        UpdateWorkspacePills();
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
        ServiceLocator.TryResolve<FocusMgr>(out var fm);
        ServiceLocator.TryResolve<WorkspaceManager>(out var ws);
        ServiceLocator.TryResolve<LayoutManager>(out var lm);
        var aws = ws?.GetActiveWorkspace();

        switch (cmd)
        {
            case DwaliaCommand.FocusNext:
                fm?.FocusNext(aws?.Windows ?? new List<Dwalia.Models.ManagedWindow>());
                break;
            case DwaliaCommand.FocusPrevious:
                fm?.FocusPrevious(aws?.Windows ?? new List<Dwalia.Models.ManagedWindow>());
                break;
            case DwaliaCommand.ToggleFloat:
                if (fm?.ActiveWindow != null) lm?.ToggleFloating(fm.ActiveWindow.Hwnd);
                break;
            case DwaliaCommand.ToggleFullscreen:
                if (fm?.ActiveWindow != null) lm?.ToggleFullscreen(fm.ActiveWindow.Hwnd);
                break;
            case DwaliaCommand.CloseWindow:
                if (fm?.ActiveWindow != null) PostMessage(fm.ActiveWindow.Hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                break;
            case DwaliaCommand.QuitDwalia:
                System.Windows.Application.Current.Shutdown();
                break;
            case DwaliaCommand.OpenSettings:
                new SettingsWindow { Owner = this }.ShowDialog();
                break;
            case DwaliaCommand.FocusWindow1: FocusWindowByIndex(0, fm, aws); break;
            case DwaliaCommand.FocusWindow2: FocusWindowByIndex(1, fm, aws); break;
            case DwaliaCommand.FocusWindow3: FocusWindowByIndex(2, fm, aws); break;
            case DwaliaCommand.FocusWindow4: FocusWindowByIndex(3, fm, aws); break;
            case DwaliaCommand.FocusWindow5: FocusWindowByIndex(4, fm, aws); break;
            case DwaliaCommand.FocusWindow6: FocusWindowByIndex(5, fm, aws); break;
            case DwaliaCommand.FocusWindow7: FocusWindowByIndex(6, fm, aws); break;
            case DwaliaCommand.FocusWindow8: FocusWindowByIndex(7, fm, aws); break;
            case DwaliaCommand.FocusWindow9: FocusWindowByIndex(8, fm, aws); break;
            case DwaliaCommand.Workspace1: ws?.SwitchToWorkspace(0); break;
            case DwaliaCommand.Workspace2: ws?.SwitchToWorkspace(1); break;
            case DwaliaCommand.Workspace3: ws?.SwitchToWorkspace(2); break;
            case DwaliaCommand.Workspace4: ws?.SwitchToWorkspace(3); break;
            case DwaliaCommand.Workspace5: ws?.SwitchToWorkspace(4); break;
            case DwaliaCommand.WorkspaceNext: ws?.NextWorkspace(); break;
            case DwaliaCommand.WorkspacePrevious: ws?.PreviousWorkspace(); break;
            case DwaliaCommand.MoveToWorkspaceNext: MoveActiveRelativeWpf(1, fm, ws); break;
            case DwaliaCommand.MoveToWorkspacePrevious: MoveActiveRelativeWpf(-1, fm, ws); break;
            case DwaliaCommand.CycleLayout: lm?.CycleLayout(); break;
            case DwaliaCommand.IncMaster: lm?.ResizeMaster(0.05); break;
            case DwaliaCommand.DecMaster: lm?.ResizeMaster(-0.05); break;
            case DwaliaCommand.IncGap: lm?.ResizeGap(1); break;
            case DwaliaCommand.DecGap: lm?.ResizeGap(-1); break;
            case DwaliaCommand.SwapNext: lm?.SwapNext(); break;
            case DwaliaCommand.SwapPrevious: lm?.SwapPrevious(); break;
        }
    }

    private static void FocusWindowByIndex(int index, FocusMgr? fm, Dwalia.Models.Workspace? aws)
    {
        if (fm == null || aws == null) return;
        if (index < 0 || index >= aws.Windows.Count) return;
        fm.SetActiveWindow(aws.Windows[index]);
        aws.Windows[index].Focus();
    }

    private static void MoveActiveRelativeWpf(int direction, FocusMgr? fm, WorkspaceManager? ws)
    {
        if (fm?.ActiveWindow == null || ws == null) return;
        var count = ws.Workspaces.Count;
        var newWs = (fm.ActiveWindow.WorkspaceId + direction + count) % count;
        ws.MoveWindowToWorkspace(fm.ActiveWindow, newWs);
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
