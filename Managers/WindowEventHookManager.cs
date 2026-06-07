using System.Runtime.InteropServices;
using System.Windows.Threading;
using Dwalia.Configuration;
using Dwalia.Infrastructure;
using Dwalia.Win32;
using static Dwalia.Win32.NativeMethods;
using static Dwalia.Win32.NativeConstants;
using static Dwalia.Win32.WindowStyles;

namespace Dwalia.Managers;

public class WindowEventHookManager
{
    private IntPtr _hook;
    private IntPtr _foregroundHook;
    private readonly WinEventDelegate _hookDelegate;
    private readonly WinEventDelegate _foregroundDelegate;
    private readonly WindowManager _windowManager;
    private readonly Dispatcher _dispatcher;
    private IntPtr _mainWindowHwnd;

    public event EventHandler<IntPtr>? FocusChanged;
    public event EventHandler<IntPtr>? WindowDiscovered;

    public WindowEventHookManager(WindowManager windowManager, Dispatcher dispatcher)
    {
        _windowManager = windowManager;
        _dispatcher = dispatcher;
        _hookDelegate = WinEventProc;
        _foregroundDelegate = ForegroundHookProc;
    }

    public void SetMainWindowHwnd(IntPtr hwnd) => _mainWindowHwnd = hwnd;

    public void Start()
    {
        Logger.Info("WindowEventHookManager starting...");
        _hook = SetWinEventHook(EVENT_OBJECT_CREATE, EVENT_OBJECT_NAMECHANGE,
            IntPtr.Zero, _hookDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);

        if (_hook == IntPtr.Zero)
            Logger.Error($"SetWinEventHook failed: {Marshal.GetLastWin32Error()}");

        _foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _foregroundDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    public void Stop()
    {
        if (_hook != IntPtr.Zero) { UnhookWinEvent(_hook); _hook = IntPtr.Zero; }
        if (_foregroundHook != IntPtr.Zero) { UnhookWinEvent(_foregroundHook); _foregroundHook = IntPtr.Zero; }
    }

    private void ForegroundHookProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (_mainWindowHwnd != IntPtr.Zero && hwnd != _mainWindowHwnd)
        {
            if (ServiceLocator.TryResolve<Views.FocusBackground>(out var fb) && fb.DragMode)
                return;
            SetWindowPos(_mainWindowHwnd, new IntPtr(1), 0, 0, 0, 0,
                SWP_NOZORDER | SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }

    private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (idObject != OBJID_WINDOW || hwnd == IntPtr.Zero) return;

        try
        {
            switch (eventType)
            {
                case EVENT_OBJECT_CREATE:
                case EVENT_OBJECT_SHOW:
                    TryManage(hwnd);
                    break;
                case EVENT_OBJECT_DESTROY:
                    _dispatcher.InvokeAsync(() => _windowManager.OnWindowDestroyed(hwnd));
                    break;
                case EVENT_OBJECT_FOCUS:
                    _dispatcher.InvokeAsync(() => FocusChanged?.Invoke(this, hwnd));
                    break;
                case EVENT_OBJECT_NAMECHANGE:
                    _windowManager.UpdateWindowTitle(hwnd);
                    TryManage(hwnd);
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"WinEventProc error: event={eventType}", ex);
        }
    }

    private void TryManage(IntPtr hwnd)
    {
        if (_windowManager.IsManaged(hwnd)) return;
        if (!WindowHelper.IsManageableWindow(hwnd)) return;
        var pn = WindowHelper.GetProcessName(hwnd);
        if (_windowManager.ExcludedProcesses.Contains(pn)) return;
        var cls = WindowHelper.GetClassNameSafe(hwnd);
        if (_windowManager.ExcludedClasses.Contains(cls))
        {
            Logger.Info($"Filtered: '{WindowHelper.GetWindowTextSafe(hwnd)}' ({pn}) class={cls}");
            return;
        }

        if (ServiceLocator.TryResolve<ConfigRoot>(out var cfg) &&
            ConfigManager.ShouldIgnore(cfg, pn, WindowHelper.GetWindowTextSafe(hwnd)))
        {
            Logger.Info($"Ignored by rule: '{WindowHelper.GetWindowTextSafe(hwnd)}' ({pn}) class={cls}");
            return;
        }

        WindowDiscovered?.Invoke(this, hwnd);
    }
}
