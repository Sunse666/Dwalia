using System.Runtime.InteropServices;
using Dwalia.Configuration;
using Dwalia.Infrastructure;
using Dwalia.Win32;
using static Dwalia.Win32.NativeMethods;
using static Dwalia.Win32.NativeConstants;
using static Dwalia.Win32.WindowStyles;

namespace Dwalia.Managers;

public class HotKeyManager : IDisposable
{
    private readonly Dictionary<(uint vkCode, bool shift, bool ctrl), DwaliaCommand> _keyMap = new();
    private readonly List<string> _failedRegistrations = new();

    private IntPtr _hookHandle;
    private IntPtr _mouseHookHandle;
    private IntPtr _dwaliaHwnd;
    private LowLevelKeyboardProc _hookProcDelegate = null!;
    private LowLevelMouseProc _mouseProcDelegate = null!;
    private bool _disposed;

    private bool _altHeld;
    private uint _altVkCode;
    private bool _shiftHeld;
    private bool _ctrlHeld;

    private System.Threading.Timer? _repeatTimer;
    private uint _repeatVk;
    private DwaliaCommand _repeatCmd;
    private bool _repeatFast;
    private const int RepeatDelay = 400;
    private const int RepeatRate = 30;

    private static readonly Dictionary<string, DwaliaCommand> CommandNameMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["focus_next"] = DwaliaCommand.FocusNext,
            ["focus_previous"] = DwaliaCommand.FocusPrevious,
            ["focus_down"] = DwaliaCommand.FocusDown,
            ["focus_up"] = DwaliaCommand.FocusUp,
            ["focus_left"] = DwaliaCommand.FocusLeft,
            ["focus_right"] = DwaliaCommand.FocusRight,
            ["swap_next"] = DwaliaCommand.SwapNext,
            ["swap_previous"] = DwaliaCommand.SwapPrevious,
            ["swap_down"] = DwaliaCommand.SwapDown,
            ["swap_up"] = DwaliaCommand.SwapUp,
            ["swap_left"] = DwaliaCommand.SwapLeft,
            ["swap_right"] = DwaliaCommand.SwapRight,
            ["toggle_float"] = DwaliaCommand.ToggleFloat,
            ["toggle_fullscreen"] = DwaliaCommand.ToggleFullscreen,
            ["toggle_scratchpad"] = DwaliaCommand.ToggleScratchpad,
            ["close_window"] = DwaliaCommand.CloseWindow,
            ["quit"] = DwaliaCommand.QuitDwalia,
            ["reload_config"] = DwaliaCommand.ReloadConfig,
            ["launch_terminal"] = DwaliaCommand.LaunchTerminal,
            ["focus_1"] = DwaliaCommand.FocusWindow1,
            ["focus_2"] = DwaliaCommand.FocusWindow2,
            ["focus_3"] = DwaliaCommand.FocusWindow3,
            ["focus_4"] = DwaliaCommand.FocusWindow4,
            ["focus_5"] = DwaliaCommand.FocusWindow5,
            ["focus_6"] = DwaliaCommand.FocusWindow6,
            ["focus_7"] = DwaliaCommand.FocusWindow7,
            ["focus_8"] = DwaliaCommand.FocusWindow8,
            ["focus_9"] = DwaliaCommand.FocusWindow9,
            ["workspace_1"] = DwaliaCommand.Workspace1,
            ["workspace_2"] = DwaliaCommand.Workspace2,
            ["workspace_3"] = DwaliaCommand.Workspace3,
            ["workspace_4"] = DwaliaCommand.Workspace4,
            ["workspace_5"] = DwaliaCommand.Workspace5,
            ["workspace_next"] = DwaliaCommand.WorkspaceNext,
            ["workspace_previous"] = DwaliaCommand.WorkspacePrevious,
            ["move_to_workspace_next"] = DwaliaCommand.MoveToWorkspaceNext,
            ["move_to_workspace_previous"] = DwaliaCommand.MoveToWorkspacePrevious,
            ["cycle_layout"] = DwaliaCommand.CycleLayout,
            ["cycle_layout_previous"] = DwaliaCommand.CycleLayoutPrevious,
            ["inc_gap"] = DwaliaCommand.IncGap,
            ["dec_gap"] = DwaliaCommand.DecGap,
            ["inc_inner_gap"] = DwaliaCommand.IncInnerGap,
            ["dec_inner_gap"] = DwaliaCommand.DecInnerGap,
            ["inc_outer_gap"] = DwaliaCommand.IncOuterGap,
            ["dec_outer_gap"] = DwaliaCommand.DecOuterGap,
            ["resize_left"] = DwaliaCommand.ResizeLeft,
            ["resize_down"] = DwaliaCommand.ResizeDown,
            ["resize_up"] = DwaliaCommand.ResizeUp,
            ["resize_right"] = DwaliaCommand.ResizeRight,
            ["bar_next"] = DwaliaCommand.BarNext,
            ["bar_previous"] = DwaliaCommand.BarPrevious,
            ["toggle_bar"] = DwaliaCommand.ToggleBar,
            ["activate_window"] = DwaliaCommand.ActivateWindow,
            ["toggle_sticky"] = DwaliaCommand.ToggleSticky,
            ["enter_resize_mode"] = DwaliaCommand.EnterResizeMode,
            ["exit_resize_mode"] = DwaliaCommand.ExitResizeMode,
        };

    private static readonly Dictionary<string, uint> KeyNameToVk =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Space"] = VK_SPACE, ["Enter"] = VK_RETURN, ["Return"] = VK_RETURN,
            ["Tab"] = VK_TAB, ["Escape"] = VK_ESCAPE,
            ["Left"] = VK_LEFT, ["Right"] = VK_RIGHT, ["Up"] = VK_UP, ["Down"] = VK_DOWN,
            ["Oem3"] = VK_OEM_3, ["OemOpenBrackets"] = VK_OEM_4, ["OemCloseBrackets"] = VK_OEM_6,
            ["OemComma"] = VK_OEM_COMMA, ["OemPeriod"] = VK_OEM_PERIOD,
        };

    public event EventHandler<DwaliaCommand>? CommandTriggered;
    public event EventHandler<bool>? ResizeModeChanged;
    public IReadOnlyList<string> FailedRegistrations => _failedRegistrations;
    public int RegisteredCount => _keyMap.Count;

    private bool _isResizeMode;
    public bool IsResizeMode => _isResizeMode;

    private uint _resizeLeftVk = VK_H;
    private uint _resizeDownVk = VK_J;
    private uint _resizeUpVk = VK_K;
    private uint _resizeRightVk = VK_L;

    public static (uint vkCode, bool shift, bool ctrl) ParseBinding(string binding)
    {
        if (string.IsNullOrWhiteSpace(binding))
            throw new ArgumentException("Binding cannot be empty");

        var part = binding.Trim();

        if (part.StartsWith("Alt+", StringComparison.OrdinalIgnoreCase))
            part = part[4..].Trim();
        else if (part.StartsWith("Alt", StringComparison.OrdinalIgnoreCase))
            part = part[3..].Trim();

        bool ctrl = false;
        if (part.StartsWith("Ctrl+", StringComparison.OrdinalIgnoreCase))
        {
            ctrl = true;
            part = part[5..].Trim();
        }

        bool shift = false;
        if (part.StartsWith("Shift+", StringComparison.OrdinalIgnoreCase))
        {
            shift = true;
            part = part[6..].Trim();
        }

        if (string.IsNullOrEmpty(part))
            throw new ArgumentException($"No key in binding: '{binding}'");

        if (KeyNameToVk.TryGetValue(part, out var vk))
            return (vk, shift, ctrl);

        if (part.Length == 1)
        {
            char c = part[0];
            if (c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z')
                return ((uint)char.ToUpper(c), shift, ctrl);
            if (c >= '0' && c <= '9')
                return ((uint)c, shift, ctrl);
        }

        throw new ArgumentException($"Unrecognized key: '{part}' in '{binding}'");
    }

    private static readonly (string binding, string command)[] DefaultBindings =
    {
        ("Alt+J", "focus_down"),
        ("Alt+K", "focus_up"),
        ("Alt+H", "focus_left"),
        ("Alt+L", "focus_right"),
        ("Alt+Shift+J", "swap_down"),
        ("Alt+Shift+K", "swap_up"),
        ("Alt+Shift+H", "swap_left"),
        ("Alt+Shift+L", "swap_right"),
        ("Alt+F", "toggle_fullscreen"),
        ("Alt+T", "cycle_layout"),
        ("Alt+Y", "cycle_layout_previous"),
        ("Alt+Shift+Space", "toggle_float"),
        ("Alt+Space", "activate_window"),
        ("Alt+Shift+S", "toggle_scratchpad"),
        ("Alt+Q", "close_window"),
        ("Alt+Shift+Q", "quit"),
        ("Alt+OemComma", "dec_gap"),
        ("Alt+OemPeriod", "inc_gap"),
        ("Alt+Ctrl+OemComma", "dec_inner_gap"),
        ("Alt+Ctrl+OemPeriod", "inc_inner_gap"),
        ("Alt+Shift+OemComma", "dec_outer_gap"),
        ("Alt+Shift+OemPeriod", "inc_outer_gap"),
        ("Alt+1", "focus_1"), ("Alt+2", "focus_2"), ("Alt+3", "focus_3"),
        ("Alt+4", "focus_4"), ("Alt+5", "focus_5"), ("Alt+6", "focus_6"),
        ("Alt+7", "focus_7"), ("Alt+8", "focus_8"), ("Alt+9", "focus_9"),
        ("Alt+Shift+1", "workspace_1"), ("Alt+Shift+2", "workspace_2"),
        ("Alt+Shift+3", "workspace_3"), ("Alt+Shift+4", "workspace_4"),
        ("Alt+Shift+5", "workspace_5"),
        ("Alt+Shift+Right", "workspace_next"),
        ("Alt+Shift+Left", "workspace_previous"),
        ("Alt+Shift+N", "move_to_workspace_next"),
        ("Alt+Shift+M", "move_to_workspace_previous"),
        ("Alt+Enter", "launch_terminal"),
        ("Alt+U", "toggle_bar"),
        ("Alt+Shift+Down", "bar_next"),
        ("Alt+Shift+Up", "bar_previous"),
        ("Alt+Shift+R", "reload_config"),
        ("Alt+S", "toggle_sticky"),
        ("Alt+R", "enter_resize_mode"),
        ("Alt+Ctrl+H", "resize_left"),
        ("Alt+Ctrl+J", "resize_down"),
        ("Alt+Ctrl+K", "resize_up"),
        ("Alt+Ctrl+L", "resize_right"),
    };

    public void Initialize(IntPtr dwaliaHwnd)
    {
        _dwaliaHwnd = dwaliaHwnd;
        _keyMap.Clear();
        _failedRegistrations.Clear();

        foreach (var (binding, cmdName) in DefaultBindings)
        {
            if (!CommandNameMap.TryGetValue(cmdName, out var cmd))
            {
                _failedRegistrations.Add($"Unknown default command: '{cmdName}'");
                continue;
            }
            try
            {
                var (vkCode, shift, ctrl) = ParseBinding(binding);
                _keyMap[(vkCode, shift, ctrl)] = cmd;
            }
            catch (Exception ex)
            {
                _failedRegistrations.Add($"Invalid default binding '{binding}': {ex.Message}");
            }
        }

        if (ServiceLocator.TryResolve<ConfigRoot>(out var config))
        {
            _resizeLeftVk = ResolveKeyName(config.ResizeMode.ResizeLeft);
            _resizeDownVk = ResolveKeyName(config.ResizeMode.ResizeDown);
            _resizeUpVk = ResolveKeyName(config.ResizeMode.ResizeUp);
            _resizeRightVk = ResolveKeyName(config.ResizeMode.ResizeRight);

            foreach (var entry in config.Keybindings)
            {
                if (string.IsNullOrWhiteSpace(entry.Binding)) continue;
                if (!CommandNameMap.TryGetValue(entry.Command, out var cmd))
                {
                    _failedRegistrations.Add($"Unknown command: '{entry.Command}'");
                    continue;
                }
                try
                {
                    var (vkCode, shift, ctrl) = ParseBinding(entry.Binding);
                    _keyMap[(vkCode, shift, ctrl)] = cmd;
                }
                catch (Exception ex)
                {
                    _failedRegistrations.Add($"Invalid binding '{entry.Binding}': {ex.Message}");
                }
            }
        }

        InstallHook();
    }

    private void InstallHook()
    {
        _hookProcDelegate = HookProc;
        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProcDelegate, IntPtr.Zero, 0);

        if (_hookHandle == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            var desc = $"SetWindowsHookEx(WH_KEYBOARD_LL) failed: error {error}";
            Logger.Error(desc);
            _failedRegistrations.Add(desc);
        }
        else
        {
            Logger.Info($"HotKeyManager: hook installed, {_keyMap.Count} bindings");
        }
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

        var msg = (uint)wParam;
        if (msg != WM_KEYDOWN && msg != WM_SYSKEYDOWN && msg != WM_KEYUP && msg != WM_SYSKEYUP)
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

        var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

        if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
        {
            var result = ProcessKeyDown(kb);
            if (result != IntPtr.Zero)
                return result;
        }
        else
        {
            var result = ProcessKeyUp(kb);
            if (result != IntPtr.Zero)
                return result;
        }

        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    public void EnterResizeMode()
    {
        if (_isResizeMode) return;
        _isResizeMode = true;
        StopRepeat();
        InstallMouseHook();
        Logger.Info("Entered resize mode (HJKL/Arrows to resize, drag to swap, Esc/Enter to exit)");
        ResizeModeChanged?.Invoke(this, true);
    }

    public void ExitResizeMode()
    {
        if (!_isResizeMode) return;
        _isResizeMode = false;
        StopRepeat();
        RemoveMouseHook();
        Logger.Info("Exited resize mode");
        ResizeModeChanged?.Invoke(this, false);
    }

    private IntPtr ProcessKeyDown(KBDLLHOOKSTRUCT kb)
    {
        if (_isResizeMode)
            return ProcessResizeModeKey(kb);

        if (kb.vkCode is VK_LSHIFT or VK_RSHIFT)
        {
            _shiftHeld = true;
            return IntPtr.Zero;
        }

        if (kb.vkCode is VK_LCONTROL or VK_RCONTROL)
        {
            _ctrlHeld = true;
            return IntPtr.Zero;
        }

        if (kb.vkCode is VK_LMENU or VK_RMENU)
        {
            _altHeld = true;
            _altVkCode = kb.vkCode;
            return IntPtr.Zero;
        }

        bool shift = _shiftHeld || (GetAsyncKeyState((int)VK_LSHIFT) & 0x8000) != 0 || (GetAsyncKeyState((int)VK_RSHIFT) & 0x8000) != 0;
        bool ctrl = _ctrlHeld || (GetAsyncKeyState((int)VK_LCONTROL) & 0x8000) != 0 || (GetAsyncKeyState((int)VK_RCONTROL) & 0x8000) != 0;
        bool alt = _altHeld || (GetAsyncKeyState((int)VK_LMENU) & 0x8000) != 0 || (GetAsyncKeyState((int)VK_RMENU) & 0x8000) != 0;

        if (alt && _keyMap.TryGetValue((kb.vkCode, shift, ctrl), out var cmd))
        {
            _shiftHeld = shift;
            _ctrlHeld = ctrl;
            _altHeld = alt;
            Logger.Info($"Dwalia: Alt+{(char)kb.vkCode} shift={shift} ctrl={ctrl} → {cmd}");
            PostMessage(_dwaliaHwnd, WM_DWALIA_COMMAND, (IntPtr)(int)cmd, IntPtr.Zero);

            if (kb.vkCode != _repeatVk)
            {
                StopRepeat();
                _repeatVk = kb.vkCode;
                _repeatCmd = cmd;
                _repeatFast = false;
                _repeatTimer = new System.Threading.Timer(_ =>
                {
                    if (!_repeatFast)
                    {
                        _repeatFast = true;
                        _repeatTimer?.Change(RepeatRate, RepeatRate);
                    }
                    PostMessage(_dwaliaHwnd, WM_DWALIA_COMMAND, (IntPtr)(int)_repeatCmd, IntPtr.Zero);
                }, null, RepeatDelay, Timeout.Infinite);
            }

            return (IntPtr)1;
        }

        return IntPtr.Zero;
    }

    private IntPtr ProcessKeyUp(KBDLLHOOKSTRUCT kb)
    {
        if (kb.vkCode is VK_LSHIFT or VK_RSHIFT)
        {
            _shiftHeld = false;
            return IntPtr.Zero;
        }

        if (kb.vkCode is VK_LCONTROL or VK_RCONTROL)
        {
            _ctrlHeld = false;
            return IntPtr.Zero;
        }

        if (kb.vkCode is VK_LMENU or VK_RMENU)
        {
            _altHeld = false;
            StopRepeat();
            return IntPtr.Zero;
        }

        if (kb.vkCode == _repeatVk)
        {
            StopRepeat();
            return (IntPtr)1;
        }

        return IntPtr.Zero;
    }

    private IntPtr ProcessResizeModeKey(KBDLLHOOKSTRUCT kb)
    {
        if (kb.vkCode is VK_LSHIFT or VK_RSHIFT)
        {
            _shiftHeld = true;
            return (IntPtr)1;
        }
        if (kb.vkCode is VK_LCONTROL or VK_RCONTROL)
        {
            _ctrlHeld = true;
            return (IntPtr)1;
        }
        if (kb.vkCode is VK_LMENU or VK_RMENU)
        {
            _altHeld = true;
            _altVkCode = kb.vkCode;
            return (IntPtr)1;
        }

        if (kb.vkCode == VK_ESCAPE || kb.vkCode == VK_RETURN)
        {
            ExitResizeMode();
            return (IntPtr)1;
        }

        bool shift = _shiftHeld || (GetAsyncKeyState((int)VK_LSHIFT) & 0x8000) != 0
            || (GetAsyncKeyState((int)VK_RSHIFT) & 0x8000) != 0;
        bool ctrl = _ctrlHeld || (GetAsyncKeyState((int)VK_LCONTROL) & 0x8000) != 0
            || (GetAsyncKeyState((int)VK_RCONTROL) & 0x8000) != 0;
        bool alt = _altHeld || (GetAsyncKeyState((int)VK_LMENU) & 0x8000) != 0
            || (GetAsyncKeyState((int)VK_RMENU) & 0x8000) != 0;

        if (alt && _keyMap.TryGetValue((kb.vkCode, shift, ctrl), out var bindingCmd))
        {
            if (bindingCmd == DwaliaCommand.EnterResizeMode)
            {
                ExitResizeMode();
                return (IntPtr)1;
            }
            PostMessage(_dwaliaHwnd, WM_DWALIA_COMMAND, (IntPtr)(int)bindingCmd, IntPtr.Zero);
            return (IntPtr)1;
        }

        DwaliaCommand? cmd = null;
        if (kb.vkCode == _resizeLeftVk  || kb.vkCode == VK_LEFT)  cmd = DwaliaCommand.ResizeLeft;
        else if (kb.vkCode == _resizeDownVk  || kb.vkCode == VK_DOWN)  cmd = DwaliaCommand.ResizeDown;
        else if (kb.vkCode == _resizeUpVk    || kb.vkCode == VK_UP)    cmd = DwaliaCommand.ResizeUp;
        else if (kb.vkCode == _resizeRightVk || kb.vkCode == VK_RIGHT) cmd = DwaliaCommand.ResizeRight;

        if (cmd.HasValue)
        {
            PostMessage(_dwaliaHwnd, WM_DWALIA_COMMAND, (IntPtr)(int)cmd.Value, IntPtr.Zero);
            return (IntPtr)1;
        }

        return (IntPtr)1;
    }

    private static uint ResolveKeyName(string name)
    {
        if (string.IsNullOrEmpty(name)) return 0;
        if (KeyNameToVk.TryGetValue(name, out var vk)) return vk;
        if (name.Length == 1)
        {
            char c = name[0];
            if (c is >= 'A' and <= 'Z') return c;
            if (c is >= 'a' and <= 'z') return (uint)char.ToUpper(c);
            if (c is >= '0' and <= '9') return c;
        }
        return 0;
    }

    private void InstallMouseHook()
    {
        _mouseProcDelegate = MouseHookProc;
        _mouseHookHandle = SetWindowsHookEx(WH_MOUSE_LL, _mouseProcDelegate, IntPtr.Zero, 0);
        if (_mouseHookHandle == IntPtr.Zero)
            Logger.Error($"Mouse hook install failed: {Marshal.GetLastWin32Error()}");
        else
            Logger.Info("Mouse hook installed for resize mode");
    }

    private void RemoveMouseHook()
    {
        if (_mouseHookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHookHandle);
            _mouseHookHandle = IntPtr.Zero;
            Logger.Info("Mouse hook removed");
        }
    }

    private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

        var msg = (uint)wParam;
        if (msg != WM_LBUTTONDOWN && msg != WM_LBUTTONUP && msg != WM_MOUSEMOVE)
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

        var ms = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

        var fb = ServiceLocator.TryResolve<Views.FocusBackground>(out var f) ? f : null;
        if (fb == null) return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

        if (msg == WM_LBUTTONDOWN)
        {
            if (ServiceLocator.TryResolve<MouseResizeManager>(out var mrm) && mrm.IsInZone(ms.ptX, ms.ptY))
                return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

            if (fb.TryHandleMouseDown(IntPtr.Zero, ms.ptX, ms.ptY))
                return (IntPtr)1;
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        if (msg == WM_MOUSEMOVE)
        {
            fb.TryHandleMouseMove(ms.ptX, ms.ptY);
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        if (msg == WM_LBUTTONUP)
        {
            if (fb.TryHandleMouseUp(ms.ptX, ms.ptY))
                return (IntPtr)1;
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private void StopRepeat()
    {
        _repeatTimer?.Dispose();
        _repeatTimer = null;
        _repeatVk = 0;
    }

    public void DispatchCommand(DwaliaCommand cmd)
    {
        CommandTriggered?.Invoke(this, cmd);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        ReleaseStuckModifiers();
        StopRepeat();

        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
        _keyMap.Clear();
    }

    private void ReleaseStuckModifiers()
    {
        if (_altHeld)
        {
            keybd_event((byte)VK_LMENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event((byte)VK_RMENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            _altHeld = false;
        }
    }

}

public enum DwaliaCommand
{
    FocusNext,
    FocusPrevious,
    FocusDown,
    FocusUp,
    FocusLeft,
    FocusRight,
    SwapNext,
    SwapPrevious,
    SwapDown,
    SwapUp,
    SwapLeft,
    SwapRight,
    ToggleFloat,
    ToggleFullscreen,
    ToggleScratchpad,
    CloseWindow,
    QuitDwalia,
    ReloadConfig,
    LaunchTerminal,
    FocusWindow1, FocusWindow2, FocusWindow3, FocusWindow4, FocusWindow5,
    FocusWindow6, FocusWindow7, FocusWindow8, FocusWindow9,
    Workspace1, Workspace2, Workspace3, Workspace4, Workspace5,
    WorkspaceNext, WorkspacePrevious,
    MoveToWorkspaceNext, MoveToWorkspacePrevious,
    CycleLayout,
    CycleLayoutPrevious,
    IncGap, DecGap,
    IncInnerGap, DecInnerGap, IncOuterGap, DecOuterGap,
    ResizeLeft, ResizeDown, ResizeUp, ResizeRight,
    BarNext, BarPrevious,
    ToggleBar,
    ActivateWindow,
    ToggleSticky,
    EnterResizeMode,
    ExitResizeMode,
}
