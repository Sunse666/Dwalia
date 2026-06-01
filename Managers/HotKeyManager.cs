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
    Dwalia
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
    private bool _shiftHeld;
    private bool _ctrlHeld;
    private bool _ctrlConsumed;

    public event EventHandler<DwaliaCommand>? CommandTriggered;
    public event EventHandler<HotKeyMode>? ModeChanged;
    public IReadOnlyList<string> FailedRegistrations => _failedRegistrations;
    public int RegisteredCount => _keyMap.Count;
    public HotKeyMode CurrentMode => _mode;

    public void Initialize(IntPtr dwaliaHwnd)
    {
        _dwaliaHwnd = dwaliaHwnd;

        Register(false, VK_J, DwaliaCommand.FocusNext);
        Register(false, VK_K, DwaliaCommand.FocusPrevious);
        Register(true, VK_J, DwaliaCommand.SwapNext);
        Register(true, VK_K, DwaliaCommand.SwapPrevious);
        Register(false, VK_F, DwaliaCommand.ToggleFullscreen);
        Register(false, VK_T, DwaliaCommand.CycleLayout);
        Register(false, VK_S, DwaliaCommand.OpenSettings);
        Register(false, VK_LEFT, DwaliaCommand.FocusPrevious);
        Register(false, VK_RIGHT, DwaliaCommand.FocusNext);
        Register(false, VK_UP, DwaliaCommand.FocusPrevious);
        Register(false, VK_DOWN, DwaliaCommand.FocusNext);
        Register(false, VK_H, DwaliaCommand.DecMaster);
        Register(false, VK_L, DwaliaCommand.IncMaster);

        for (int i = 0; i < 9; i++)
            Register(false, VK_1 + (uint)i, DwaliaCommand.FocusWindow1 + i);

        Register(true, VK_SPACE, DwaliaCommand.ToggleFloat);
        Register(true, VK_RETURN, DwaliaCommand.LaunchTerminal);
        Register(true, VK_C, DwaliaCommand.CloseWindow);
        Register(true, VK_Q, DwaliaCommand.QuitDwalia);
        Register(true, VK_LEFT, DwaliaCommand.WorkspacePrevious);
        Register(true, VK_RIGHT, DwaliaCommand.WorkspaceNext);
        Register(true, VK_N, DwaliaCommand.MoveToWorkspaceNext);
        Register(true, VK_M, DwaliaCommand.MoveToWorkspacePrevious);
        Register(true, VK_OEM_COMMA, DwaliaCommand.DecGap);
        Register(true, VK_OEM_PERIOD, DwaliaCommand.IncGap);

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
            Logger.Info($"HotKeyManager: keyboard hook installed (Ctrl+`), bindings={_keyMap.Count}");
            foreach (var kv in _keyMap)
                Logger.Info($"  KeyMap: vk=0x{kv.Key.vkCode:X2} shift={kv.Key.shift} → {kv.Value}");
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
        if (kb.vkCode is VK_LCONTROL or VK_RCONTROL)
        {
            _ctrlHeld = true;
            return IntPtr.Zero;
        }

        if (_ctrlHeld && kb.vkCode == VK_OEM_3)
        {
            _ctrlConsumed = true;
            _mode = _mode == HotKeyMode.Normal ? HotKeyMode.Dwalia : HotKeyMode.Normal;
            ModeChanged?.Invoke(this, _mode);
            return (IntPtr)1;
        }

        switch (_mode)
        {
            case HotKeyMode.Normal:
                return IntPtr.Zero;

            case HotKeyMode.Dwalia:
                if (kb.vkCode is VK_LWIN or VK_RWIN or VK_LMENU or VK_RMENU)
                    return IntPtr.Zero;
                if (kb.vkCode == VK_ESCAPE)
                {
                    _mode = HotKeyMode.Normal;
                    ModeChanged?.Invoke(this, _mode);
                    return (IntPtr)1;
                }
                if (_keyMap.TryGetValue((kb.vkCode, _shiftHeld), out var cmd))
                {
                    Logger.Info($"Dwalia: {(char)kb.vkCode} shift={_shiftHeld} → {cmd}");
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

        if (kb.vkCode is VK_LCONTROL or VK_RCONTROL)
        {
            _ctrlHeld = false;
            if (_ctrlConsumed)
            {
                _ctrlConsumed = false;
                keybd_event((byte)kb.vkCode, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                return (IntPtr)1;
            }
            return IntPtr.Zero;
        }

        if (_mode == HotKeyMode.Dwalia && kb.vkCode is not VK_LWIN and not VK_RWIN and not VK_LMENU and not VK_RMENU)
            return (IntPtr)1;

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
        if (_ctrlConsumed)
        {
            keybd_event((byte)VK_LCONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event((byte)VK_RCONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            _ctrlConsumed = false;
            _ctrlHeld = false;
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
