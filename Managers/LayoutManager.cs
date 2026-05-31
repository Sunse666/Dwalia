using Dwalia.Infrastructure;
using Dwalia.Models;
using static Dwalia.Win32.NativeMethods;
using static Dwalia.Win32.WindowStyles;

namespace Dwalia.Managers;

public enum LayoutType { MasterStack, Monocle, Grid, HorizontalStack }

public class LayoutManager
{
    private readonly WindowManager _windowManager;
    private readonly WorkspaceManager _workspaceManager;
    private readonly FocusManager _focusManager;
    private System.Windows.Rect _area;
    private LayoutType _layout = LayoutType.MasterStack;
    private double _masterFactor = 0.6;
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

        switch (_layout)
        {
            case LayoutType.Monocle: ArrangeMonocle(windows, area); break;
            case LayoutType.Grid: ArrangeGrid(windows, area); break;
            case LayoutType.HorizontalStack: ArrangeHStack(windows, area); break;
            default: ArrangeMasterStack(windows, area); break;
        }
    }

    private void ArrangeMonocle(List<ManagedWindow> windows, System.Windows.Rect area)
    {
        var active = _focusManager.ActiveWindow;
        if (active == null || !windows.Contains(active))
            active = windows[0];

        foreach (var w in windows)
            ShowWindow(w.Hwnd, w == active ? SW_SHOW : SW_HIDE);

        Position(active, area);
    }

    private void ArrangeGrid(List<ManagedWindow> windows, System.Windows.Rect area)
    {
        int n = windows.Count;
        if (n == 0) return;
        int cols = (int)Math.Ceiling(Math.Sqrt(n));
        int rows = (int)Math.Ceiling((double)n / cols);
        double cw = (area.Width - (cols - 1) * _gap) / cols;
        double ch = (area.Height - (rows - 1) * _gap) / rows;

        for (int i = 0; i < n; i++)
        {
            int col = i % cols;
            int row = i / cols;
            var r = new System.Windows.Rect(
                area.X + col * (cw + _gap),
                area.Y + row * (ch + _gap),
                cw, ch);
            Position(windows[i], r);
        }
    }

    private void ArrangeHStack(List<ManagedWindow> windows, System.Windows.Rect area)
    {
        int n = windows.Count;
        if (n == 0) return;
        double h = (area.Height - (n - 1) * _gap) / n;
        for (int i = 0; i < n; i++)
        {
            var r = new System.Windows.Rect(area.X, area.Y + i * (h + _gap), area.Width, h);
            Position(windows[i], r);
        }
    }

    private void ArrangeMasterStack(List<ManagedWindow> windows, System.Windows.Rect area)
    {
        int n = windows.Count;
        if (n == 1)
        {
            Position(windows[0], area);
            return;
        }

        double mw = (area.Width - _gap) * _masterFactor;
        double sw = area.Width - mw - _gap;
        Position(windows[0], new System.Windows.Rect(area.X, area.Y, mw, area.Height));

        int s = n - 1;
        if (s > 0)
        {
            double sh = (area.Height - (s - 1) * _gap) / s;
            for (int i = 0; i < s; i++)
            {
                var r = new System.Windows.Rect(area.X + mw + _gap, area.Y + i * (sh + _gap), sw, sh);
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

    public void CycleLayout()
    {
        _layout = _layout switch
        {
            LayoutType.MasterStack => LayoutType.Monocle,
            LayoutType.Monocle => LayoutType.Grid,
            LayoutType.Grid => LayoutType.HorizontalStack,
            LayoutType.HorizontalStack => LayoutType.MasterStack,
            _ => LayoutType.MasterStack
        };
        Logger.Info($"Layout: {_layout}");
        Relayout();
    }

    public void ResizeMaster(double delta)
    {
        _masterFactor = Math.Clamp(_masterFactor + delta, 0.3, 0.8);
        Logger.Info($"Master factor: {_masterFactor:F2}");
        Relayout();
    }

    public void ResizeGap(int delta)
    {
        _gap = Math.Clamp(_gap + delta, 0, 24);
        _outer = Math.Clamp(_outer + delta, 0, 12);
        Logger.Info($"Gap: {_gap}, Outer: {_outer}");
        Relayout();
    }

    public void SwapNext()
    {
        SwapWindow(1);
    }

    public void SwapPrevious()
    {
        SwapWindow(-1);
    }

    private void SwapWindow(int direction)
    {
        var ws = _workspaceManager.GetActiveWorkspace();
        if (ws == null || _focusManager.ActiveWindow == null) return;
        var list = ws.Windows;
        var tiled = list.Where(w => w.State == WindowLayoutState.Tiled).ToList();
        if (tiled.Count < 2) return;
        int tiledIdx = tiled.IndexOf(_focusManager.ActiveWindow);
        if (tiledIdx < 0) return;
        int nextTiled = (tiledIdx + direction + tiled.Count) % tiled.Count;
        int realIdx = list.IndexOf(tiled[tiledIdx]);
        int realNext = list.IndexOf(tiled[nextTiled]);
        (list[realIdx], list[realNext]) = (list[realNext], list[realIdx]);
        _focusManager.SetActiveWindow(list[realNext]);
        Relayout();
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
