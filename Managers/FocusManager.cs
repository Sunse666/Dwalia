using Dwalia.Infrastructure;
using Dwalia.Models;
using static Dwalia.Win32.NativeMethods;

namespace Dwalia.Managers;

public class FocusManager
{
    private readonly WindowManager _windowManager;
    private ManagedWindow? _activeWindow;

    public ManagedWindow? ActiveWindow => _activeWindow;
    public event Action? FocusChanged;

    public FocusManager(WindowManager windowManager)
    {
        _windowManager = windowManager;
    }

    public void OnFocusEvent(IntPtr focusedHwnd)
    {
        var mw = ResolveManagedWindow(focusedHwnd);
        SetActiveWindow(mw);
    }

    public void SetActiveWindow(ManagedWindow? window)
    {
        if (_activeWindow == window) return;
        Logger.Info($"Focus -> {window?.Title ?? "none"}");

        if (_activeWindow != null)
            _activeWindow.IsActive = false;
        _activeWindow = window;
        if (_activeWindow != null)
            _activeWindow.IsActive = true;
        FocusChanged?.Invoke();
    }

    public void FocusWindow(IntPtr hwnd)
    {
        var mw = _windowManager.GetManagedWindow(hwnd);
        if (mw != null) { SetActiveWindow(mw); mw.Focus(); }
    }

    public void FocusNext(IList<ManagedWindow> windows)
    {
        if (windows.Count == 0)
        {
            Logger.Warn("FocusNext: workspace is empty");
            return;
        }
        int idx = _activeWindow != null ? windows.IndexOf(_activeWindow) : -1;
        var next = windows[(idx + 1) % windows.Count];
        SetActiveWindow(next);
        next.Focus();
    }

    public void FocusPrevious(IList<ManagedWindow> windows)
    {
        if (windows.Count == 0)
        {
            Logger.Warn("FocusPrevious: workspace is empty");
            return;
        }
        int idx = _activeWindow != null ? windows.IndexOf(_activeWindow) : -1;
        var prev = windows[(idx - 1 + windows.Count) % windows.Count];
        SetActiveWindow(prev);
        prev.Focus();
    }

    private ManagedWindow? ResolveManagedWindow(IntPtr hwnd)
    {
        var mw = _windowManager.GetManagedWindow(hwnd);
        if (mw != null) return mw;
        for (int i = 0; i < 10; i++)
        {
            var p = GetParent(hwnd);
            if (p == IntPtr.Zero) break;
            mw = _windowManager.GetManagedWindow(p);
            if (mw != null) return mw;
            hwnd = p;
        }
        return null;
    }
}
