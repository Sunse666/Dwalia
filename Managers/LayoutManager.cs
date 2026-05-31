using Dwalia.Infrastructure;
using Dwalia.Models;
using static Dwalia.Win32.NativeMethods;
using static Dwalia.Win32.WindowStyles;

namespace Dwalia.Managers;

public class LayoutManager
{
    private readonly WindowManager _windowManager;
    private readonly WorkspaceManager _workspaceManager;
    private readonly FocusManager _focusManager;
    private System.Windows.Rect _area;
    private int _gap = 4;
    private int _outer = 2;

    public LayoutManager(WindowManager wm, WorkspaceManager ws, FocusManager fm)
    {
        _windowManager = wm;
        _workspaceManager = ws;
        _focusManager = fm;
        _area = new System.Windows.Rect(0, 0, 1920, 1040);

        _windowManager.WindowManaged += (_, _) => Relayout();
        _windowManager.WindowUnmanaged += (_, _) => Relayout();
        _workspaceManager.WorkspaceChanged += (_, _) => Relayout();
    }

    public void SetArea(System.Windows.Rect area)
    {
        if (area.Width <= 0 || area.Height <= 0 ||
            double.IsNaN(area.Width) || double.IsNaN(area.Height))
        {
            Logger.Warn($"SetArea rejected invalid area: {area.Width}x{area.Height}");
            return;
        }
        _area = area;
        Relayout();
    }

    public void Relayout()
    {
        var ws = _workspaceManager.GetActiveWorkspace();
        if (ws == null) return;

        var windows = ws.Windows.ToList();
        var tiled = windows.Where(w => w.State == WindowLayoutState.Tiled).ToList();

        Logger.Info($"Relayout: {tiled.Count} tiled / {_windowManager.ManagedWindows.Count} total, area={_area.Width:F0}x{_area.Height:F0}");

        foreach (var mw in _windowManager.ManagedWindows.Values)
        {
            if (!IsWindow(mw.Hwnd)) continue;
            if (mw.State == WindowLayoutState.Floating) continue;
            var onActive = mw.WorkspaceId == _workspaceManager.ActiveWorkspaceId;
            var sw = onActive ? SW_SHOW : SW_HIDE;
            ShowWindow(mw.Hwnd, sw);
        }

        if (tiled.Count == 0) return;
        ArrangeTiled(tiled);
    }

    private const int MinWindowWidth = 200;
    private const int MinWindowHeight = 150;

    private void ArrangeTiled(List<ManagedWindow> windows)
    {
        var area = new System.Windows.Rect(
            _area.X + _outer, _area.Y + _outer,
            Math.Max(MinWindowWidth, _area.Width - _outer * 2),
            Math.Max(MinWindowHeight, _area.Height - _outer * 2));

        int n = windows.Count;

        if (n == 1)
        {
            Logger.Info($"  Tiling solo: '{windows[0].Title}' -> ({area.X:F0},{area.Y:F0} {area.Width:F0}x{area.Height:F0})");
            Position(windows[0], area);
            return;
        }

        double mw = (area.Width - _gap) * 0.6;
        double sw = area.Width - mw - _gap;

        double mh = (area.Height - 0 * _gap) / 1;
        Logger.Info($"  Tiling master: '{windows[0].Title}' -> ({area.X:F0},{area.Y:F0} {mw:F0}x{mh:F0})");
        Position(windows[0], new System.Windows.Rect(area.X, area.Y, mw, mh));

        int s = n - 1;
        if (s > 0)
        {
            double sh = (area.Height - (s - 1) * _gap) / s;
            for (int i = 0; i < s; i++)
            {
                var r = new System.Windows.Rect(area.X + mw + _gap, area.Y + i * (sh + _gap), sw, sh);
                Logger.Info($"  Tiling stack[{i}]: '{windows[1 + i].Title}' -> ({r.X:F0},{r.Y:F0} {r.Width:F0}x{r.Height:F0})");
                Position(windows[1 + i], r);
            }
        }
    }

    private static void Position(ManagedWindow mw, System.Windows.Rect r)
    {
        if (!IsWindow(mw.Hwnd)) return;
        uint dpi = GetDpiForWindow(mw.Hwnd);
        if (dpi == 0) dpi = 96;
        double scale = dpi / 96.0;
        SetWindowPos(mw.Hwnd, IntPtr.Zero,
            (int)(r.X * scale), (int)(r.Y * scale),
            (int)(r.Width * scale), (int)(r.Height * scale),
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    public void ToggleFloating(IntPtr hwnd)
    {
        var mw = _windowManager.GetManagedWindow(hwnd);
        if (mw == null) return;

        if (mw.State == WindowLayoutState.Floating)
        {
            mw.State = WindowLayoutState.Tiled;
            Win32.WindowHelper.RemoveWindowChrome(hwnd);
        }
        else
        {
            mw.State = WindowLayoutState.Floating;
            Win32.WindowHelper.RestoreWindowChrome(hwnd, mw.OriginalWindowInfo);
        }
        Relayout();
    }

    public void ToggleFullscreen(IntPtr hwnd)
    {
        var mw = _windowManager.GetManagedWindow(hwnd);
        if (mw == null) return;

        if (mw.State == WindowLayoutState.Fullscreen)
        {
            mw.State = mw.PreFullscreenState ?? WindowLayoutState.Floating;
            if (mw.PreFullscreenRect.HasValue)
            {
                var r = mw.PreFullscreenRect.Value;
                SetWindowPos(mw.Hwnd, IntPtr.Zero,
                    r.Left, r.Top, r.Width, r.Height,
                    SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }

            if (mw.State == WindowLayoutState.Tiled)
                Win32.WindowHelper.RemoveWindowChrome(hwnd);
            else
                Win32.WindowHelper.RestoreWindowChrome(hwnd, mw.OriginalWindowInfo);

            Relayout();
        }
        else
        {
            mw.PreFullscreenState = mw.State;
            mw.PreFullscreenRect = Win32.WindowHelper.GetWindowRectSafe(hwnd);
            mw.State = WindowLayoutState.Fullscreen;
            Position(mw, _area);
        }
    }
}
