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
    private readonly Dictionary<(uint vkCode, bool shift), DwaliaCommand> _keyMap = new();
    private readonly List<string> _failedRegistrations = new();

    private IntPtr _hookHandle;
    private IntPtr _dwaliaHwnd;
    private LowLevelKeyboardProc _hookProcDelegate = null!;
    private bool _disposed;

    private bool _altHeld;
    private bool _altConsumed;
    private uint _altVkCode;
    private bool _shiftHeld;

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
            ["inc_master"] = DwaliaCommand.IncMaster,
            ["dec_master"] = DwaliaCommand.DecMaster,
            ["inc_gap"] = DwaliaCommand.IncGap,
            ["dec_gap"] = DwaliaCommand.DecGap,
            ["bar_next"] = DwaliaCommand.BarNext,
            ["bar_previous"] = DwaliaCommand.BarPrevious,
            ["toggle_bar"] = DwaliaCommand.ToggleBar,
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
    public IReadOnlyList<string> FailedRegistrations => _failedRegistrations;
    public int RegisteredCount => _keyMap.Count;

    public static (uint vkCode, bool shift) ParseBinding(string binding)
    {
        if (string.IsNullOrWhiteSpace(binding))
            throw new ArgumentException("Binding cannot be empty");

        var part = binding.Trim();

        if (part.StartsWith("Alt+", StringComparison.OrdinalIgnoreCase))
            part = part[4..].Trim();
        else if (part.StartsWith("Alt", StringComparison.OrdinalIgnoreCase))
            part = part[3..].Trim();

        bool shift = false;
        if (part.StartsWith("Shift+", StringComparison.OrdinalIgnoreCase))
        {
            shift = true;
            part = part[6..].Trim();
        }

        if (string.IsNullOrEmpty(part))
            throw new ArgumentException($"No key in binding: '{binding}'");

        if (KeyNameToVk.TryGetValue(part, out var vk))
            return (vk, shift);

        if (part.Length == 1)
        {
            char c = part[0];
            if (c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z')
                return ((uint)char.ToUpper(c), shift);
            if (c >= '0' && c <= '9')
                return ((uint)c, shift);
        }

        throw new ArgumentException($"Unrecognized key: '{part}' in '{binding}'");
    }

    public void Initialize(IntPtr dwaliaHwnd)
    {
        _dwaliaHwnd = dwaliaHwnd;

        if (!ServiceLocator.TryResolve<ConfigRoot>(out var config))
        {
            _failedRegistrations.Add("ConfigRoot not found in ServiceLocator");
            InstallHook();
            return;
        }

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
                var (vkCode, shift) = ParseBinding(entry.Binding);
                if (_keyMap.ContainsKey((vkCode, shift)))
                    _failedRegistrations.Add($"{entry.Binding} (duplicate)");
                else
                    _keyMap[(vkCode, shift)] = cmd;
            }
            catch (Exception ex)
            {
                _failedRegistrations.Add($"Invalid binding '{entry.Binding}': {ex.Message}");
            }
        }

        TryRegisterAlias(false, VK_J, DwaliaCommand.FocusDown);
        TryRegisterAlias(false, VK_K, DwaliaCommand.FocusUp);
        TryRegisterAlias(false, VK_H, DwaliaCommand.FocusLeft);
        TryRegisterAlias(false, VK_L, DwaliaCommand.FocusRight);
        TryRegisterAlias(false, VK_UP, DwaliaCommand.FocusUp);
        TryRegisterAlias(false, VK_DOWN, DwaliaCommand.FocusDown);
        TryRegisterAlias(true, VK_H, DwaliaCommand.SwapLeft);
        TryRegisterAlias(true, VK_L, DwaliaCommand.SwapRight);

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

    private void Register(bool shift, uint vkCode, DwaliaCommand command)
    {
        if (_keyMap.ContainsKey((vkCode, shift)))
            _failedRegistrations.Add($"{(shift ? "Shift+" : "")}{VkToString(vkCode)} (duplicate)");
        else
            _keyMap[(vkCode, shift)] = command;
    }

    private void TryRegisterAlias(bool shift, uint vkCode, DwaliaCommand command)
    {
        if (!_keyMap.ContainsKey((vkCode, shift)))
            _keyMap[(vkCode, shift)] = command;
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

    private IntPtr ProcessKeyDown(KBDLLHOOKSTRUCT kb)
    {
        if (kb.vkCode is VK_LSHIFT or VK_RSHIFT)
        {
            _shiftHeld = true;
            return IntPtr.Zero;
        }

        if (kb.vkCode is VK_LMENU or VK_RMENU)
        {
            _altHeld = true;
            _altVkCode = kb.vkCode;
            _altConsumed = false;
            return IntPtr.Zero;
        }

        if (_altHeld && _keyMap.TryGetValue((kb.vkCode, _shiftHeld), out var cmd))
        {
            _altConsumed = true;
            keybd_event((byte)_altVkCode, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            Logger.Info($"Dwalia: Alt+{(char)kb.vkCode} shift={_shiftHeld} → {cmd}");
            PostMessage(_dwaliaHwnd, WM_DWALIA_COMMAND, (IntPtr)(int)cmd, IntPtr.Zero);
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

        if (kb.vkCode is VK_LMENU or VK_RMENU)
        {
            if (_altConsumed)
            {
                _altConsumed = false;
                return (IntPtr)1;
            }
            _altHeld = false;
            return IntPtr.Zero;
        }

        return IntPtr.Zero;
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

        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
        _keyMap.Clear();
    }

    private void ReleaseStuckModifiers()
    {
        if (_altConsumed)
        {
            keybd_event((byte)VK_LMENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event((byte)VK_RMENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            _altConsumed = false;
            _altHeld = false;
        }
    }

    private static string VkToString(uint vk)
    {
        if (vk >= VK_0 && vk <= VK_9) return ((char)vk).ToString();
        if (vk >= VK_A && vk <= VK_Z) return ((char)vk).ToString();
        return vk switch
        {
            VK_SPACE => "Space",
            VK_RETURN => "Enter",
            VK_TAB => "Tab",
            VK_ESCAPE => "Escape",
            VK_LEFT => "Left",
            VK_RIGHT => "Right",
            VK_UP => "Up",
            VK_DOWN => "Down",
            _ => $"VK(0x{vk:X})"
        };
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
    IncMaster, DecMaster,
    IncGap, DecGap,
    BarNext, BarPrevious,
    ToggleBar,
}
