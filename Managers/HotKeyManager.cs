using System.Runtime.InteropServices;
using Dwalia.Infrastructure;
using Dwalia.Win32;
using static Dwalia.Win32.NativeMethods;
using static Dwalia.Win32.NativeConstants;
using static Dwalia.Win32.WindowStyles;

namespace Dwalia.Managers;

public enum HotKeyMode
{
    Normal,
    CommandMode
}

public class HotKeyManager : IDisposable
{
    private readonly Dictionary<(uint vkCode, bool shift), DwaliaCommand> _keyMap = new();
    private readonly List<string> _failedRegistrations = new();

    private IntPtr _hookHandle;
    private IntPtr _dwaliaHwnd;
    private LowLevelKeyboardProc _hookProcDelegate = null!;
    private bool _disposed;

    private HotKeyMode _mode = HotKeyMode.Normal;
    private DateTime _leaderTimestamp;
    private bool _shiftHeld;
    private bool _winHeld;
    private bool _winConsumed;

    private const int CommandModeTimeoutMs = 2000;

    public event EventHandler<DwaliaCommand>? CommandTriggered;
    public IReadOnlyList<string> FailedRegistrations => _failedRegistrations;
    public int RegisteredCount => _keyMap.Count;
    public HotKeyMode CurrentMode => _mode;

    public void Initialize(IntPtr dwaliaHwnd)
    {
        _dwaliaHwnd = dwaliaHwnd;

        Register(false, VK_J, DwaliaCommand.FocusNext);
        Register(false, VK_K, DwaliaCommand.FocusPrevious);
        Register(false, VK_SPACE, DwaliaCommand.ToggleFloat);
        Register(false, VK_F, DwaliaCommand.ToggleFullscreen);
        Register(false, VK_Q, DwaliaCommand.CloseWindow);
        Register(false, VK_RETURN, DwaliaCommand.LaunchTerminal);
        Register(false, VK_S, DwaliaCommand.OpenSettings);
        Register(false, VK_T, DwaliaCommand.CycleLayout);
        Register(false, VK_LEFT, DwaliaCommand.WorkspacePrevious);
        Register(false, VK_RIGHT, DwaliaCommand.WorkspaceNext);
        Register(false, VK_UP, DwaliaCommand.IncMaster);
        Register(false, VK_DOWN, DwaliaCommand.DecMaster);

        for (int i = 0; i < 9; i++)
            Register(false, VK_1 + (uint)i, DwaliaCommand.FocusWindow1 + i);

        Register(true, VK_J, DwaliaCommand.SwapNext);
        Register(true, VK_K, DwaliaCommand.SwapPrevious);
        Register(true, VK_Q, DwaliaCommand.QuitDwalia);
        Register(true, VK_LEFT, DwaliaCommand.MoveToWorkspacePrevious);
        Register(true, VK_RIGHT, DwaliaCommand.MoveToWorkspaceNext);
        Register(true, VK_UP, DwaliaCommand.IncGap);
        Register(true, VK_DOWN, DwaliaCommand.DecGap);

        for (int i = 0; i < 5; i++)
            Register(true, VK_1 + (uint)i, DwaliaCommand.Workspace1 + i);

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
            Logger.Info($"HotKeyManager: keyboard hook installed (Win+Space), bindings={_keyMap.Count}");
        }
    }

    private void Register(bool shift, uint vkCode, DwaliaCommand command)
    {
        if (_keyMap.ContainsKey((vkCode, shift)))
            _failedRegistrations.Add($"{(shift ? "Shift+" : "")}{VkToString(vkCode)} (duplicate)");
        else
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
        if (kb.vkCode is VK_LWIN or VK_RWIN)
        {
            _winHeld = true;
            return (IntPtr)1;
        }

        CheckTimeout();

        switch (_mode)
        {
            case HotKeyMode.Normal:
                if (_winHeld && kb.vkCode == VK_SPACE)
                {
                    _winConsumed = true;
                    _leaderTimestamp = DateTime.UtcNow;
                    _mode = HotKeyMode.CommandMode;
                    return (IntPtr)1;
                }
                return IntPtr.Zero;

            case HotKeyMode.CommandMode:
                if (kb.vkCode == VK_ESCAPE)
                {
                    _mode = HotKeyMode.Normal;
                    return (IntPtr)1;
                }
                if (_keyMap.TryGetValue((kb.vkCode, _shiftHeld), out var cmd))
                {
                    _mode = HotKeyMode.Normal;
                    PostMessage(_dwaliaHwnd, WM_DWALIA_COMMAND, (IntPtr)(int)cmd, IntPtr.Zero);
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

        if (kb.vkCode is VK_LWIN or VK_RWIN)
        {
            _winHeld = false;
            if (_winConsumed)
            {
                _winConsumed = false;
                return (IntPtr)1;
            }
            ReplayKey((byte)kb.vkCode);
            return (IntPtr)1;
        }

        if (_mode == HotKeyMode.CommandMode)
            return (IntPtr)1;

        return IntPtr.Zero;
    }

    private void CheckTimeout()
    {
        if (_mode == HotKeyMode.CommandMode &&
            (DateTime.UtcNow - _leaderTimestamp).TotalMilliseconds > CommandModeTimeoutMs)
        {
            _mode = HotKeyMode.Normal;
        }
    }

    private static void ReplayKey(byte vkCode)
    {
        keybd_event(vkCode, 0, 0, UIntPtr.Zero);
        keybd_event(vkCode, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    public void DispatchCommand(DwaliaCommand cmd)
    {
        CommandTriggered?.Invoke(this, cmd);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
        _keyMap.Clear();
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
    SwapNext,
    SwapPrevious,
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
    OpenSettings,
    CycleLayout,
    IncMaster, DecMaster,
    IncGap, DecGap,
}
