using Dwalia.Configuration;
using Dwalia.Infrastructure;
using Dwalia.Models;
using static Dwalia.Win32.NativeMethods;
using static Dwalia.Win32.NativeConstants;
using static Dwalia.Win32.WindowStyles;

namespace Dwalia.Managers;

public class FocusManager
{
    private readonly WindowManager _windowManager;
    private ManagedWindow? _activeWindow;
    private int _activeBorderColor = DWM_ACTIVE_BORDER;
    private int _inactiveBorderColor = DWM_INACTIVE_BORDER;

    public ManagedWindow? ActiveWindow => _activeWindow;
    public event Action? FocusChanged;

    public FocusManager(WindowManager windowManager)
    {
        _windowManager = windowManager;
    }

    public void SetBorderColors(int active, int inactive)
    {
        _activeBorderColor = active;
        _inactiveBorderColor = inactive;
        if (_activeWindow != null)
            ApplyBorderColor(_activeWindow.Hwnd, _activeBorderColor);
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
        {
            _activeWindow.IsActive = false;
            ApplyBorderColor(_activeWindow.Hwnd, _inactiveBorderColor);
            if (ServiceLocator.TryResolve<ConfigRoot>(out var unfocusedCfg))
                WindowManager.SetWindowOpacity(_activeWindow.Hwnd, unfocusedCfg.Theme.WindowOpacityUnfocused);
        }
        _activeWindow = window;
        if (_activeWindow != null)
        {
            _activeWindow.IsActive = true;
            ApplyBorderColor(_activeWindow.Hwnd, _activeBorderColor);
            if (ServiceLocator.TryResolve<ConfigRoot>(out var focusedCfg))
                WindowManager.SetWindowOpacity(_activeWindow.Hwnd, focusedCfg.Theme.WindowOpacityFocused);
        }
        FocusChanged?.Invoke();
    }

    private static void ApplyBorderColor(IntPtr hwnd, int color)
    {
        var c = color;
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref c, sizeof(int));
    }

    public void FocusDown(List<ManagedWindow> tiled)
    {
        if (_activeWindow == null) return;
        var other = LayoutManager.FindWindowBelow(_activeWindow, tiled);
        if (other == null) { Logger.Info("FocusDown: no window below"); return; }
        SetActiveWindow(other);
    }

    public void FocusUp(List<ManagedWindow> tiled)
    {
        if (_activeWindow == null) return;
        var other = LayoutManager.FindWindowAbove(_activeWindow, tiled);
        if (other == null) { Logger.Info("FocusUp: no window above"); return; }
        SetActiveWindow(other);
    }

    public void FocusLeft(List<ManagedWindow> tiled)
    {
        if (_activeWindow == null) return;
        var other = LayoutManager.FindWindowLeft(_activeWindow, tiled);
        if (other == null) { Logger.Info("FocusLeft: no window left"); return; }
        SetActiveWindow(other);
    }

    public void FocusRight(List<ManagedWindow> tiled)
    {
        if (_activeWindow == null) return;
        var other = LayoutManager.FindWindowRight(_activeWindow, tiled);
        if (other == null) { Logger.Info("FocusRight: no window right"); return; }
        SetActiveWindow(other);
    }

    public void FocusWindow(IntPtr hwnd)
    {
        var mw = _windowManager.GetManagedWindow(hwnd);
        if (mw != null) SetActiveWindow(mw);
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
    }

    public void ActivateActiveWindow()
    {
        if (_activeWindow == null) return;
        Logger.Info($"Activating: '{_activeWindow.Title}'");
        _activeWindow.Focus();
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
