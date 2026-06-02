using System.Runtime.InteropServices;
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

    private readonly Dictionary<DwaliaCommand, string> _commandDisplayMap = new();

    private static readonly Dictionary<string, uint> KeyNameToVk =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Space"] = VK_SPACE, ["Enter"] = VK_RETURN, ["Return"] = VK_RETURN,
            ["Tab"] = VK_TAB, ["Escape"] = VK_ESCAPE,
            ["Left"] = VK_LEFT, ["Right"] = VK_RIGHT, ["Up"] = VK_UP, ["Down"] = VK_DOWN,
            ["Oem3"] = VK_OEM_3, ["OemComma"] = VK_OEM_COMMA, ["OemPeriod"] = VK_OEM_PERIOD,
        };

    public event EventHandler<DwaliaCommand>? CommandTriggered;
    public IReadOnlyList<string> FailedRegistrations => _failedRegistrations;
    public int RegisteredCount => _keyMap.Count;
    public IReadOnlyDictionary<DwaliaCommand, string> CommandBindings => _commandDisplayMap;

    public static Dictionary<string, string> GetDefaultBindings()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(DwaliaCommand.FocusNext)] = "J",
            [nameof(DwaliaCommand.FocusPrevious)] = "K",
            [nameof(DwaliaCommand.SwapNext)] = "Shift+J",
            [nameof(DwaliaCommand.SwapPrevious)] = "Shift+K",
            [nameof(DwaliaCommand.ToggleFullscreen)] = "F",
            [nameof(DwaliaCommand.CycleLayout)] = "T",
            [nameof(DwaliaCommand.ToggleFloat)] = "Shift+Space",
            [nameof(DwaliaCommand.CloseWindow)] = "Q",
            [nameof(DwaliaCommand.QuitDwalia)] = "Shift+Q",
            [nameof(DwaliaCommand.DecMaster)] = "H",
            [nameof(DwaliaCommand.IncMaster)] = "L",
            [nameof(DwaliaCommand.DecGap)] = "OemComma",
            [nameof(DwaliaCommand.IncGap)] = "OemPeriod",
            [nameof(DwaliaCommand.WorkspacePrevious)] = "Left",
            [nameof(DwaliaCommand.WorkspaceNext)] = "Right",
            [nameof(DwaliaCommand.MoveToWorkspacePrevious)] = "Shift+Left",
            [nameof(DwaliaCommand.MoveToWorkspaceNext)] = "Shift+Right",
            [nameof(DwaliaCommand.LaunchTerminal)] = "Enter",
            [nameof(DwaliaCommand.OpenSettings)] = "Shift+S",
            [nameof(DwaliaCommand.FocusWindow1)] = "1",
            [nameof(DwaliaCommand.FocusWindow2)] = "2",
            [nameof(DwaliaCommand.FocusWindow3)] = "3",
            [nameof(DwaliaCommand.FocusWindow4)] = "4",
            [nameof(DwaliaCommand.FocusWindow5)] = "5",
            [nameof(DwaliaCommand.FocusWindow6)] = "6",
            [nameof(DwaliaCommand.FocusWindow7)] = "7",
            [nameof(DwaliaCommand.FocusWindow8)] = "8",
            [nameof(DwaliaCommand.FocusWindow9)] = "9",
            [nameof(DwaliaCommand.Workspace1)] = "Shift+1",
            [nameof(DwaliaCommand.Workspace2)] = "Shift+2",
            [nameof(DwaliaCommand.Workspace3)] = "Shift+3",
            [nameof(DwaliaCommand.Workspace4)] = "Shift+4",
            [nameof(DwaliaCommand.Workspace5)] = "Shift+5",
            [nameof(DwaliaCommand.BarNext)] = "Shift+Down",
            [nameof(DwaliaCommand.BarPrevious)] = "Shift+Up",
            [nameof(DwaliaCommand.ToggleBar)] = "U",
        };
    }

    public static (uint vkCode, bool shift) ParseKeyString(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key string cannot be empty");
        bool shift = false;
        string keyPart = key.Trim();
        if (keyPart.StartsWith("Shift+", StringComparison.OrdinalIgnoreCase))
        {
            shift = true;
            keyPart = keyPart[6..].Trim();
        }
        if (KeyNameToVk.TryGetValue(keyPart, out var vk))
            return (vk, shift);
        if (keyPart.Length == 1)
        {
            char c = keyPart[0];
            if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                return ((uint)c, shift);
        }
        throw new ArgumentException($"Unrecognized key: '{key}'");
    }

    public static string FormatKey(uint vkCode, bool shift)
    {
        string key = vkCode switch
        {
            VK_SPACE => "Space",
            VK_RETURN => "Enter",
            VK_TAB => "Tab",
            VK_ESCAPE => "Escape",
            VK_LEFT => "Left",
            VK_RIGHT => "Right",
            VK_UP => "Up",
            VK_DOWN => "Down",
            VK_OEM_3 => "Oem3",
            VK_OEM_COMMA => "OemComma",
            VK_OEM_PERIOD => "OemPeriod",
            >= VK_0 and <= VK_9 => ((char)vkCode).ToString(),
            >= VK_A and <= VK_Z => ((char)vkCode).ToString(),
            _ => $"VK(0x{vkCode:X})"
        };
        return shift ? $"Shift+{key}" : key;
    }

    public void Initialize(IntPtr dwaliaHwnd)
    {
        _dwaliaHwnd = dwaliaHwnd;

        var bindings = new Dictionary<string, string>(
            GetDefaultBindings(), StringComparer.OrdinalIgnoreCase);

        if (ServiceLocator.TryResolve<Configuration.DwaliaConfig>(out var config)
            && config.Keybindings?.Bindings != null)
        {
            foreach (var kv in config.Keybindings.Bindings)
            {
                if (!string.IsNullOrWhiteSpace(kv.Value))
                    bindings[kv.Key] = kv.Value;
            }
        }

        foreach (var kv in bindings)
        {
            try
            {
                var (vkCode, shift) = ParseKeyString(kv.Value);
                if (Enum.TryParse<DwaliaCommand>(kv.Key, out var cmd))
                    Register(shift, vkCode, cmd);
            }
            catch (Exception ex)
            {
                _failedRegistrations.Add($"Invalid binding '{kv.Key}={kv.Value}': {ex.Message}");
            }
        }

        TryRegisterAlias(false, VK_UP, DwaliaCommand.FocusPrevious);
        TryRegisterAlias(false, VK_DOWN, DwaliaCommand.FocusNext);
        TryRegisterAlias(true, VK_UP, DwaliaCommand.BarPrevious);
        TryRegisterAlias(true, VK_DOWN, DwaliaCommand.BarNext);

        _commandDisplayMap.Clear();
        foreach (var kv in bindings)
        {
            if (Enum.TryParse<DwaliaCommand>(kv.Key, out var cmd))
                _commandDisplayMap[cmd] = kv.Value;
        }

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
            Logger.Info($"HotKeyManager: keyboard hook installed (Alt+key), bindings={_keyMap.Count}");
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
    BarNext, BarPrevious,
    ToggleBar,
}
