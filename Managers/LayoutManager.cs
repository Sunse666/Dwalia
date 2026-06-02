using Dwalia.Configuration;
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
    public System.Windows.Rect Area => _area;
    public event EventHandler<LayoutType>? LayoutChanged;
    public event Action<string>? StatusMessage;
    public event Action? RelayoutCompleted;

    private LayoutType _layout = LayoutType.MasterStack;
    private double _masterFactor = 0.6;
    private int _gap = 4;
    private int _outer = 2;
    private List<LayoutType> _enabledLayouts = new() { LayoutType.MasterStack, LayoutType.Monocle, LayoutType.Grid, LayoutType.HorizontalStack, LayoutType.Columns, LayoutType.VerticalStack, LayoutType.BSP };
    private List<IntPtr> _bspOrderedHwnds = new();

    public LayoutManager(WindowManager wm, WorkspaceManager ws, FocusManager fm)
    {
        _windowManager = wm;
        _workspaceManager = ws;
        _focusManager = fm;
        _area = new System.Windows.Rect(0, 0, 1920, 1040);

        if (ServiceLocator.TryResolve<ConfigRoot>(out var config))
        {
            _masterFactor = config.Layout.MasterFactor;
            _gap = config.Layout.InnerGap;
            _outer = config.Layout.OuterGap;
        }

        _windowManager.WindowManaged += (_, _) => Relayout();
        _windowManager.WindowUnmanaged += (_, _) => Relayout();
        _workspaceManager.WorkspaceChanged += (_, _) => Relayout();
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
            mi.rcWork.Top + taskbarHeight,
            mi.rcWork.Width,
            Math.Max(200, mi.rcWork.Height - taskbarHeight));

        Logger.Info($"Work area: {_area.Width:F0}x{_area.Height:F0} @ ({_area.X:F0},{_area.Y:F0})");
        Relayout();
    }

    public void Relayout(bool resetFocus = true)
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

        if (tiled.Count > 0)
            ArrangeTiled(tiled);

        if (resetFocus)
        {
            var ordered = GetOrderedWindows();
            if (ordered.Count > 0)
                _focusManager.SetActiveWindow(ordered[0]);
        }

        RelayoutCompleted?.Invoke();
    }

    private const int MinWindowWidth = 200;
    private const int MinWindowHeight = 150;

    private void ArrangeTiled(List<ManagedWindow> windows)
    {
        var active = _focusManager.ActiveWindow;
        if (active != null && windows.Count > 1 && windows.Contains(active))
        {
            windows.Remove(active);
            windows.Insert(0, active);
        }

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
        if (n == 1) { Position(windows[0], area); return; }

        int cols = (int)Math.Ceiling(Math.Sqrt(n));
        int rows = (int)Math.Ceiling((double)n / cols);

        double firstColW, otherColW, firstRowH, otherRowH;

        if (cols > 1)
        {
            firstColW = (area.Width - (cols - 1) * _gap) * _masterFactor;
            otherColW = Math.Max(MinWindowWidth, (area.Width - firstColW - (cols - 1) * _gap) / (cols - 1));
        }
        else
        {
            firstColW = area.Width;
            otherColW = 0;
        }

        if (rows > 1)
        {
            firstRowH = (area.Height - (rows - 1) * _gap) * _masterFactor;
            otherRowH = Math.Max(MinWindowHeight, (area.Height - firstRowH - (rows - 1) * _gap) / (rows - 1));
        }
        else
        {
            firstRowH = area.Height;
            otherRowH = 0;
        }

        for (int i = 0; i < n; i++)
        {
            int col = i % cols;
            int row = i / cols;
            double x = col == 0 ? area.X : area.X + firstColW + _gap + (col - 1) * (otherColW + _gap);
            double y = row == 0 ? area.Y : area.Y + firstRowH + _gap + (row - 1) * (otherRowH + _gap);
            double w = col == 0 ? firstColW : otherColW;
            double h = row == 0 ? firstRowH : otherRowH;
            Position(windows[i], new System.Windows.Rect(x, y, w, h));
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
        if (n == 1) { Position(windows[0], area); return; }

        double firstW = (area.Width - (n - 1) * _gap) * _masterFactor;
        double otherW = Math.Max(MinWindowWidth, (area.Width - firstW - (n - 1) * _gap) / (n - 1));

        Position(windows[0], new System.Windows.Rect(area.X, area.Y, firstW, area.Height));
        for (int i = 1; i < n; i++)
        {
            var r = new System.Windows.Rect(
                area.X + firstW + _gap + (i - 1) * (otherW + _gap),
                area.Y, otherW, area.Height);
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
        _bspOrderedHwnds.Clear();
        int n = windows.Count;
        if (n == 0) return;
        if (n == 1) { Position(windows[0], area); _bspOrderedHwnds.Add(windows[0].Hwnd); return; }

        int leftCount = n / 2;
        int rightCount = n - leftCount;

        double leftW = (area.Width - _gap) * _masterFactor;
        var left = new System.Windows.Rect(area.X, area.Y, leftW, area.Height);
        var right = new System.Windows.Rect(area.X + leftW + _gap, area.Y, area.Width - leftW - _gap, area.Height);

        ArrangeBSPRecursive(windows, 0, leftCount, left, false);
        ArrangeBSPRecursive(windows, leftCount, rightCount, right, false);
    }

    private void ArrangeBSPRecursive(List<ManagedWindow> windows, int start, int count, System.Windows.Rect area, bool splitVertical)
    {
        if (count == 0) return;
        if (count == 1)
        {
            Position(windows[start], area);
            _bspOrderedHwnds.Add(windows[start].Hwnd);
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
        mw.LayoutBounds = r;
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
        StatusMessage?.Invoke($"{_layout}");
        Relayout();
    }

    public List<ManagedWindow> GetOrderedWindows()
    {
        var ws = _workspaceManager.GetActiveWorkspace();
        if (ws == null) return new();
        var all = ws.Windows.ToList();

        if (_layout == LayoutType.BSP && _bspOrderedHwnds.Count > 0)
        {
            var ordered = new List<ManagedWindow>();
            foreach (var hwnd in _bspOrderedHwnds)
            {
                var mw = all.FirstOrDefault(w => w.Hwnd == hwnd);
                if (mw != null) ordered.Add(mw);
            }
            var remaining = all.Except(ordered)
                .OrderBy(w => GetWindowTopLeft(w).Y)
                .ThenBy(w => GetWindowTopLeft(w).X)
                .ToList();
            ordered.AddRange(remaining);
            return ordered;
        }

        return all
            .OrderBy(w => GetWindowTopLeft(w).Y)
            .ThenBy(w => GetWindowTopLeft(w).X)
            .ToList();
    }

    private System.Windows.Point GetWindowTopLeft(ManagedWindow w)
    {
        if (w.State == WindowLayoutState.Fullscreen)
            return new System.Windows.Point(_area.X, _area.Y);

        if (w.State == WindowLayoutState.Floating || w.LayoutBounds.Width <= 0)
        {
            var rect = Win32.WindowHelper.GetWindowRectSafe(w.Hwnd);
            if (rect.Width > 0 && rect.Height > 0)
                return new System.Windows.Point(rect.Left, rect.Top);
        }

        return new System.Windows.Point(w.LayoutBounds.X, w.LayoutBounds.Y);
    }

    public void SetMasterFactor(double value)
    {
        _masterFactor = Math.Clamp(value, 0.3, 0.8);
        SaveLayoutConfig();
        Relayout();
    }

    public void SetGaps(int inner, int outer)
    {
        _gap = Math.Clamp(inner, 0, 24);
        _outer = Math.Clamp(outer, 0, 12);
        SaveLayoutConfig();
        Relayout();
    }

    public void ResizeMaster(double delta)
    {
        _masterFactor = Math.Clamp(_masterFactor + delta, 0.3, 0.8);
        Logger.Info($"Master factor: {_masterFactor:F2}");
        StatusMessage?.Invoke($"Master: {_masterFactor * 100:F0}%");
        SaveLayoutConfig();
        Relayout();
    }

    public void ResizeGap(int delta)
    {
        _gap = Math.Clamp(_gap + delta, 0, 24);
        _outer = Math.Clamp(_outer + delta, 0, 12);
        Logger.Info($"Gap: {_gap}, Outer: {_outer}");
        StatusMessage?.Invoke($"Gap: {_gap} Inner / {_outer} Outer");
        SaveLayoutConfig();
        Relayout();
    }

    private void SaveLayoutConfig()
    {
        if (ServiceLocator.TryResolve<ConfigRoot>(out var config) &&
            ServiceLocator.TryResolve<ConfigManager>(out var cm))
        {
            config.Layout.MasterFactor = _masterFactor;
            config.Layout.InnerGap = _gap;
            config.Layout.OuterGap = _outer;
            cm.Save(config);
        }
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
        var ordered = GetOrderedWindows();
        if (ordered.Count < 2 || _focusManager.ActiveWindow == null) return;
        var active = _focusManager.ActiveWindow;

        int activeIdx = ordered.IndexOf(active);
        if (activeIdx < 0) return;

        int otherIdx = (activeIdx + direction + ordered.Count) % ordered.Count;
        var other = ordered[otherIdx];

        var list = _workspaceManager.GetActiveWorkspace()!.Windows;
        list.Remove(active);
        if (other != active)
            list.Remove(other);

        list.Insert(0, other);
        list.Add(active);

        _focusManager.SetActiveWindow(other);
        Relayout(resetFocus: false);
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
            RelayoutCompleted?.Invoke();
        }
    }
}
