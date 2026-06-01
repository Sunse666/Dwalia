using Dwalia.Infrastructure;
using Dwalia.Models;
using static Dwalia.Win32.NativeMethods;
using static Dwalia.Win32.WindowStyles;

namespace Dwalia.Managers;

public enum LayoutType { MasterStack, Monocle, Grid, HorizontalStack, Columns, VerticalStack, BSP }

public class LayoutManager
{
    private readonly WindowManager _windowManager;
    private readonly WorkspaceManager _workspaceManager;
    private readonly FocusManager _focusManager;
    private System.Windows.Rect _area;
    public event EventHandler<LayoutType>? LayoutChanged;

    private LayoutType _layout = LayoutType.MasterStack;
    private double _masterFactor = 0.6;
    private int _gap = 4;
    private int _outer = 2;
    private List<LayoutType> _enabledLayouts = new() { LayoutType.MasterStack, LayoutType.Monocle, LayoutType.Grid, LayoutType.HorizontalStack, LayoutType.Columns, LayoutType.VerticalStack, LayoutType.BSP };

    public LayoutManager(WindowManager wm, WorkspaceManager ws, FocusManager fm)
    {
        _windowManager = wm;
        _workspaceManager = ws;
        _focusManager = fm;
        _area = new System.Windows.Rect(0, 0, 1920, 1040);

        _windowManager.WindowManaged += (_, _) => Relayout();
        _windowManager.WindowUnmanaged += (_, _) => Relayout();
        _workspaceManager.WorkspaceChanged += (_, _) => Relayout();
        _focusManager.FocusChanged += Relayout;
    }

    public void SetArea(IntPtr mainHwnd, double taskbarHeight)
    {
        var monitor = MonitorFromWindow(mainHwnd, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO();
        mi.cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>();
        if (!GetMonitorInfo(monitor, ref mi))
        {
            Logger.Warn("GetMonitorInfo failed, using fallback area");
            return;
        }

        _area = new System.Windows.Rect(
            mi.rcWork.Left,
            mi.rcWork.Top,
            mi.rcWork.Width,
            Math.Max(200, mi.rcWork.Height - taskbarHeight));

        Logger.Info($"Work area: {_area.Width:F0}x{_area.Height:F0} @ ({_area.X:F0},{_area.Y:F0})");
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
            var sw = onActive ? SW_RESTORE : SW_HIDE;
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
            case LayoutType.Columns: ArrangeColumns(windows, area); break;
            case LayoutType.VerticalStack: ArrangeVStack(windows, area); break;
            case LayoutType.BSP: ArrangeBSP(windows, area); break;
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

    private void ArrangeColumns(List<ManagedWindow> windows, System.Windows.Rect area)
    {
        int n = windows.Count;
        if (n == 0) return;
        double cw = (area.Width - (n - 1) * _gap) / n;
        for (int i = 0; i < n; i++)
        {
            var r = new System.Windows.Rect(area.X + i * (cw + _gap), area.Y, cw, area.Height);
            Position(windows[i], r);
        }
    }

    private void ArrangeVStack(List<ManagedWindow> windows, System.Windows.Rect area)
    {
        int n = windows.Count;
        if (n == 1) { Position(windows[0], area); return; }

        var active = _focusManager.ActiveWindow;
        var master = (active != null && windows.Contains(active)) ? active : windows[0];
        var stack = windows.Where(w => w != master).ToList();

        double mh = (area.Height - _gap) * _masterFactor;
        double sh = area.Height - mh - _gap;
        Position(master, new System.Windows.Rect(area.X, area.Y, area.Width, mh));

        int s = stack.Count;
        if (s > 0)
        {
            double blockH = (sh - (s - 1) * _gap) / s;
            for (int i = 0; i < s; i++)
            {
                var r = new System.Windows.Rect(area.X, area.Y + mh + _gap + i * (blockH + _gap), area.Width, blockH);
                Position(stack[i], r);
            }
        }
    }

    private void ArrangeBSP(List<ManagedWindow> windows, System.Windows.Rect area)
    {
        ArrangeBSPRecursive(windows, 0, windows.Count, area, true);
    }

    private void ArrangeBSPRecursive(List<ManagedWindow> windows, int start, int count, System.Windows.Rect area, bool splitVertical)
    {
        if (count == 0) return;
        if (count == 1)
        {
            Position(windows[start], area);
            return;
        }

        int leftCount = count / 2;
        int rightCount = count - leftCount;

        if (splitVertical)
        {
            double leftW = area.Width * leftCount / count - _gap / 2.0;
            var left = new System.Windows.Rect(area.X, area.Y, leftW, area.Height);
            var right = new System.Windows.Rect(area.X + leftW + _gap, area.Y, area.Width - leftW - _gap, area.Height);
            ArrangeBSPRecursive(windows, start, leftCount, left, false);
            ArrangeBSPRecursive(windows, start + leftCount, rightCount, right, false);
        }
        else
        {
            double topH = area.Height * leftCount / count - _gap / 2.0;
            var top = new System.Windows.Rect(area.X, area.Y, area.Width, topH);
            var bottom = new System.Windows.Rect(area.X, area.Y + topH + _gap, area.Width, area.Height - topH - _gap);
            ArrangeBSPRecursive(windows, start, leftCount, top, true);
            ArrangeBSPRecursive(windows, start + leftCount, rightCount, bottom, true);
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

        var active = _focusManager.ActiveWindow;
        var master = (active != null && windows.Contains(active)) ? active : windows[0];
        var stack = windows.Where(w => w != master).ToList();

        double mw = (area.Width - _gap) * _masterFactor;
        double sw = area.Width - mw - _gap;
        Position(master, new System.Windows.Rect(area.X, area.Y, mw, area.Height));

        int s = stack.Count;
        if (s > 0)
        {
            double sh = (area.Height - (s - 1) * _gap) / s;
            for (int i = 0; i < s; i++)
            {
                var r = new System.Windows.Rect(area.X + mw + _gap, area.Y + i * (sh + _gap), sw, sh);
                Position(stack[i], r);
            }
        }
    }

    private static void Position(ManagedWindow mw, System.Windows.Rect r)
    {
        if (!IsWindow(mw.Hwnd)) return;
        SetWindowPos(mw.Hwnd, IntPtr.Zero,
            (int)r.X, (int)r.Y, (int)r.Width, (int)r.Height,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    public void SetEnabledLayouts(IEnumerable<string> names)
    {
        var enabled = new List<LayoutType>();
        foreach (var name in names)
        {
            if (Enum.TryParse<LayoutType>(name, true, out var lt))
                enabled.Add(lt);
        }
        if (enabled.Count == 0)
            enabled.Add(LayoutType.MasterStack);

        _enabledLayouts = enabled;
        if (!_enabledLayouts.Contains(_layout))
            _layout = _enabledLayouts[0];

        Logger.Info($"Enabled layouts: {string.Join(", ", _enabledLayouts)}");
    }

    public void CycleLayout()
    {
        var idx = _enabledLayouts.IndexOf(_layout);
        _layout = _enabledLayouts[(idx + 1) % _enabledLayouts.Count];
        Logger.Info($"Layout: {_layout}");
        LayoutChanged?.Invoke(this, _layout);
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

        mw.State = mw.State == WindowLayoutState.Floating
            ? WindowLayoutState.Tiled
            : WindowLayoutState.Floating;
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
