using System.Runtime.InteropServices;
using Dwalia.Configuration;
using Dwalia.Infrastructure;
using static Dwalia.Win32.NativeMethods;
using static Dwalia.Win32.NativeConstants;
using static Dwalia.Win32.WindowStyles;

namespace Dwalia.Managers;

public class HotKeyManager : IDisposable
{
    private readonly Dictionary<int, (uint Modifiers, uint Key, DwaliaCommand Command)> _hotkeys = new();
    private readonly Dictionary<(uint Modifiers, uint Key), DwaliaCommand> _keyMap = new();
    private readonly List<string> _failedRegistrations = new();
    private IntPtr _hwnd;
    private int _nextId = 1;
    private bool _disposed;

    public event EventHandler<DwaliaCommand>? CommandTriggered;
    public IReadOnlyList<string> FailedRegistrations => _failedRegistrations;
    public int RegisteredCount => _hotkeys.Count;

    public void Initialize(IntPtr mainWindowHwnd)
    {
        _hwnd = mainWindowHwnd;

        var modKey = ServiceLocator.TryResolve<DwaliaConfig>(out var config)
            ? config.ModKey
            : "Alt+Ctrl";

        uint mod = 0;
        foreach (var part in modKey.Split('+', StringSplitOptions.TrimEntries))
        {
            mod |= part.ToLowerInvariant() switch
            {
                "lwin" or "rwin" or "win" => MOD_WIN,
                "lalt" or "ralt" or "alt" => MOD_ALT,
                "lctrl" or "rctrl" or "ctrl" or "control" => MOD_CONTROL,
                "lshift" or "rshift" or "shift" => MOD_SHIFT,
                _ => 0
            };
        }
        if (mod == 0) mod = MOD_WIN | MOD_CONTROL;

        Register(mod, VK_J, DwaliaCommand.FocusNext);
        Register(mod, VK_K, DwaliaCommand.FocusPrevious);
        Register(mod | MOD_SHIFT, VK_J, DwaliaCommand.SwapNext);
        Register(mod | MOD_SHIFT, VK_K, DwaliaCommand.SwapPrevious);
        Register(mod, VK_SPACE, DwaliaCommand.ToggleFloat);
        Register(mod, VK_F, DwaliaCommand.ToggleFullscreen);
        Register(mod, VK_Q, DwaliaCommand.CloseWindow);
        Register(mod | MOD_SHIFT, VK_Q, DwaliaCommand.QuitDwalia);
        Register(mod, VK_RETURN, DwaliaCommand.LaunchTerminal);

        Register(mod, VK_1, DwaliaCommand.FocusWindow1);
        Register(mod, VK_2, DwaliaCommand.FocusWindow2);
        Register(mod, VK_3, DwaliaCommand.FocusWindow3);
        Register(mod, VK_4, DwaliaCommand.FocusWindow4);
        Register(mod, VK_5, DwaliaCommand.FocusWindow5);
        Register(mod, VK_6, DwaliaCommand.FocusWindow6);
        Register(mod, VK_7, DwaliaCommand.FocusWindow7);
        Register(mod, VK_8, DwaliaCommand.FocusWindow8);
        Register(mod, VK_9, DwaliaCommand.FocusWindow9);

        Register(mod | MOD_SHIFT, VK_1, DwaliaCommand.Workspace1);
        Register(mod | MOD_SHIFT, VK_2, DwaliaCommand.Workspace2);
        Register(mod | MOD_SHIFT, VK_3, DwaliaCommand.Workspace3);
        Register(mod | MOD_SHIFT, VK_4, DwaliaCommand.Workspace4);
        Register(mod | MOD_SHIFT, VK_5, DwaliaCommand.Workspace5);

        Register(mod, VK_LEFT, DwaliaCommand.WorkspacePrevious);
        Register(mod, VK_RIGHT, DwaliaCommand.WorkspaceNext);

        Register(mod | MOD_SHIFT, VK_LEFT, DwaliaCommand.MoveToWorkspacePrevious);
        Register(mod | MOD_SHIFT, VK_RIGHT, DwaliaCommand.MoveToWorkspaceNext);

        Register(mod, VK_S, DwaliaCommand.OpenSettings);

        Logger.Info($"HotKeyManager: registered {_hotkeys.Count} hotkeys (mod={modKey})");
    }

    public bool Register(uint modifiers, uint key, DwaliaCommand command)
    {
        int id = _nextId++;

        if (!RegisterHotKey(_hwnd, id, modifiers, key))
        {
            var error = Marshal.GetLastWin32Error();
            var desc = $"{ModifiersToString(modifiers)}+{VkToString(key)}";
            Logger.Warn($"RegisterHotKey failed: {desc} (mod=0x{modifiers:X}, vk=0x{key:X}, error={error})");
            _failedRegistrations.Add(desc);
            return false;
        }

        _hotkeys[id] = (modifiers, key, command);
        _keyMap[(modifiers, key)] = command;
        return true;
    }

    public bool HandleHotKeyMessage(int hotkeyId)
    {
        if (_hotkeys.TryGetValue(hotkeyId, out var hotkey))
        {
            Logger.Info($"Hotkey triggered: {hotkey.Command}");
            CommandTriggered?.Invoke(this, hotkey.Command);
            return true;
        }
        return false;
    }

    public bool TryMatchCommand(uint modifiers, uint vk, out DwaliaCommand command)
    {
        return _keyMap.TryGetValue((modifiers, vk), out command);
    }

    private static string ModifiersToString(uint mod)
    {
        var parts = new List<string>();
        if ((mod & MOD_ALT) != 0) parts.Add("Alt");
        if ((mod & MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((mod & MOD_SHIFT) != 0) parts.Add("Shift");
        if ((mod & MOD_WIN) != 0) parts.Add("Win");
        return parts.Count > 0 ? string.Join("+", parts) : "None";
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var id in _hotkeys.Keys)
        {
            UnregisterHotKey(_hwnd, id);
        }
        _hotkeys.Clear();
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
}
