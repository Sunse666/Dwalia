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
    public double CurrentMasterFactor => _masterFactor;
    public event EventHandler<LayoutType>? LayoutChanged;
    public event Action<string>? StatusMessage;
    public event Action? RelayoutCompleted;

    private LayoutType _layout = LayoutType.MasterStack;
    private double _masterFactor = 0.6;
    private int _gap = 4;
    private int _outer = 2;
    private List<LayoutType> _enabledLayouts = new() { LayoutType.MasterStack, LayoutType.Monocle, LayoutType.Grid, LayoutType.HorizontalStack, LayoutType.Columns, LayoutType.VerticalStack, LayoutType.BSP };
    private List<IntPtr> _bspOrderedHwnds = new();

    private bool _isAnimating;
    private readonly List<(ManagedWindow Window, System.Windows.Rect Target)> _pendingPositions = new();
    private List<(ManagedWindow Window, System.Windows.Rect From, System.Windows.Rect To)> _animFrames = new();
    private int _animVersion;
    private const int AnimDuration = 150;
    private bool _disableAnimation;
    private bool _preserveMaster;

    public event Action<List<ResizeZone>>? ResizeZonesUpdated;

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
        _workspaceManager.WorkspaceChanged += (_, id) =>
        {
            var ws = _workspaceManager.GetWorkspace(id);
            if (ws != null)
            {
                _layout = ws.Layout;
                LayoutChanged?.Invoke(this, _layout);
            }
            Relayout();
        };
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

        if (resetFocus && tiled.Count > 0)
        {
            var first = tiled.OrderBy(w => GetWindowTopLeft(w).Y)
                             .ThenBy(w => GetWindowTopLeft(w).X)
                             .First();
            _focusManager.SetActiveWindow(first);
        }

        if (tiled.Count > 0)
        {
            _isAnimating = true;
            _pendingPositions.Clear();
            ArrangeTiled(tiled);
            _isAnimating = false;
            ApplyAnimated();
        }

        if (resetFocus)
        {
            var ordered = GetOrderedWindows();
            if (ordered.Count > 0)
                _focusManager.SetActiveWindow(ordered[0]);
        }

        if (tiled.Count == 0)
            NotifyLayoutComplete();
    }

    public void SetAnimationEnabled(bool enabled)
    {
        _disableAnimation = !enabled;
    }

    private void ApplyAnimated()
    {
        if (_pendingPositions.Count == 0)
        {
            NotifyLayoutComplete();
            return;
        }

        _animVersion++;

        if (_disableAnimation)
        {
            foreach (var (mw, target) in _pendingPositions)
                InstantPosition(mw, target);
            NotifyLayoutComplete();
            return;
        }

        _animFrames.Clear();

        foreach (var (mw, target) in _pendingPositions)
        {
            var raw = Win32.WindowHelper.GetWindowRectSafe(mw.Hwnd);
            if (raw.Width > 0 && raw.Height > 0)
            {
                var from = new System.Windows.Rect(raw.Left, raw.Top, raw.Width, raw.Height);
                if (Math.Abs(from.X - target.X) > 2 || Math.Abs(from.Y - target.Y) > 2
                    || Math.Abs(from.Width - target.Width) > 2 || Math.Abs(from.Height - target.Height) > 2)
                {
                    _animFrames.Add((mw, from, target));
                }
                else
                {
                    InstantPosition(mw, target);
                }
            }
            else
            {
                InstantPosition(mw, target);
            }
        }

        if (_animFrames.Count == 0)
        {
            NotifyLayoutComplete();
            return;
        }

        int myVersion = _animVersion;
        var frames = _animFrames.ToList();

        var thread = new Thread(() => AnimateThread(frames, myVersion))
        {
            IsBackground = true,
            Name = "DwaliaAnim"
        };
        thread.Start();
    }

    private void AnimateThread(List<(ManagedWindow Window, System.Windows.Rect From, System.Windows.Rect To)> frames, int version)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        const uint animFlags = SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOCOPYBITS;
        const uint finalFlags = SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW;
        const int targetFrameMs = 16;

        long lastFrameMs = -targetFrameMs;
        while (version == _animVersion)
        {
            long elapsedMs = sw.ElapsedMilliseconds;
            double rawT = Math.Min(1.0, (double)elapsedMs / AnimDuration);

            if (elapsedMs - lastFrameMs >= targetFrameMs || rawT >= 1.0)
            {
                double t = EaseOutQuart(rawT);
                ApplyDeferredFrame(frames, t, animFlags);
                lastFrameMs = elapsedMs;
            }

            if (rawT >= 1.0) break;

            Thread.Sleep(1);
        }

        if (version != _animVersion) return;

        ApplyDeferredFrame(frames, 1.0, finalFlags);

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (version != _animVersion) return;
            foreach (var (mw, _, to) in frames)
                mw.LayoutBounds = to;
            _animFrames.Clear();
            NotifyLayoutComplete();
        });
    }

    private static void ApplyDeferredFrame(
        List<(ManagedWindow Window, System.Windows.Rect From, System.Windows.Rect To)> frames,
        double t, uint flags)
    {
        var hdwp = BeginDeferWindowPos(frames.Count);
        if (hdwp == IntPtr.Zero) return;

        foreach (var (mw, from, to) in frames)
        {
            double x = from.X + (to.X - from.X) * t;
            double y = from.Y + (to.Y - from.Y) * t;
            double w = from.Width + (to.Width - from.Width) * t;
            double h = from.Height + (to.Height - from.Height) * t;
            hdwp = DeferWindowPos(hdwp, mw.Hwnd, IntPtr.Zero,
                (int)x, (int)y, (int)w, (int)h, flags);
        }
        EndDeferWindowPos(hdwp);
    }

    private static double EaseOutQuart(double t)
    {
        return 1.0 - Math.Pow(1.0 - t, 4);
    }

    private void NotifyLayoutComplete()
    {
        UpdateResizeZones();
        RelayoutCompleted?.Invoke();
    }

    private const int MinWindowWidth = 200;
    private const int MinWindowHeight = 150;

    private void ArrangeTiled(List<ManagedWindow> windows)
    {
        if (!_preserveMaster)
        {
            var active = _focusManager.ActiveWindow;
            if (active != null && windows.Count > 1 && windows.Contains(active))
            {
                windows.Remove(active);
                windows.Insert(0, active);
            }
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

        ManagedWindow masterV;
        if (_preserveMaster)
            masterV = windows[0];
        else
        {
            var active = _focusManager.ActiveWindow;
            masterV = (active != null && windows.Contains(active)) ? active : windows[0];
        }
        var stack = windows.Where(w => w != masterV).ToList();

        double mh = (area.Height - _gap) * _masterFactor;
        double sh = area.Height - mh - _gap;
        Position(masterV, new System.Windows.Rect(area.X, area.Y, area.Width, mh));

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

        ManagedWindow master;
        if (_preserveMaster)
            master = windows[0];
        else
        {
            var active = _focusManager.ActiveWindow;
            master = (active != null && windows.Contains(active)) ? active : windows[0];
        }
        var stack = windows.Where(w => w != master).ToList();

        double mw = (area.Width - _gap) * _masterFactor;
        double sw = area.Width - mw - _gap;
        Position(master, new System.Windows.Rect(area.X, area.Y, mw, area.Height));

        int s = stack.Count;
        if (s > 0)
        {
            double totalRatio = 0;
            foreach (var w in stack) totalRatio += Math.Max(0.1, w.StackRatio);
            double yOff = 0;
            for (int i = 0; i < s; i++)
            {
                double ratio = Math.Max(0.1, stack[i].StackRatio) / totalRatio;
                double h = Math.Max(MinWindowHeight, (area.Height - (s - 1) * _gap) * ratio);
                if (i == s - 1)
                    h = area.Height - yOff;

                var r = new System.Windows.Rect(area.X + mw + _gap, area.Y + yOff, sw, h);
                Position(stack[i], r);
                yOff += h + _gap;
            }
        }
    }

    private void Position(ManagedWindow mw, System.Windows.Rect r)
    {
        if (!IsWindow(mw.Hwnd)) return;
        mw.LayoutBounds = r;
        if (_isAnimating)
            _pendingPositions.Add((mw, r));
        else
            InstantPosition(mw, r);
    }

    private static void InstantPosition(ManagedWindow mw, System.Windows.Rect r)
    {
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
        var ws = _workspaceManager.GetActiveWorkspace();
        if (ws != null) ws.Layout = _layout;
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

    public void ResizeGap(int delta)
    {
        _gap = Math.Clamp(_gap + delta, 0, 24);
        _outer = Math.Clamp(_outer + delta, 0, 12);
        Logger.Info($"Gap: {_gap}, Outer: {_outer}");
        StatusMessage?.Invoke($"Gap: {_gap} Inner / {_outer} Outer");
        SaveLayoutConfig();
        Relayout();
    }

    private enum EdgeDir { Left, Right, Top, Bottom, None }

    public void ResizeLeft() => DoResize(false, true);
    public void ResizeRight() => DoResize(false, false);
    public void ResizeDown() => DoResize(true, false);
    public void ResizeUp() => DoResize(true, true);

    private void DoResize(bool horizontal, bool decrease)
    {
        _preserveMaster = true;
        try
        {
            var mw = _focusManager.ActiveWindow;
            if (mw == null || mw.State != WindowLayoutState.Tiled) return;

            var bounds = mw.LayoutBounds;
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            bool leftAlive = bounds.X > _area.X + _outer + 4;
            bool rightAlive = bounds.Right < _area.Right - _outer - 4;
            bool topAlive = bounds.Y > _area.Y + _outer + 4;
            bool bottomAlive = bounds.Bottom < _area.Bottom - _outer - 4;

            EdgeDir edge;
            if (horizontal)
            {
                if (topAlive) edge = EdgeDir.Top;
                else if (bottomAlive) edge = EdgeDir.Bottom;
                else return;
            }
            else
            {
                if (leftAlive) edge = EdgeDir.Left;
                else if (rightAlive) edge = EdgeDir.Right;
                else return;
            }

            if (edge is EdgeDir.Left or EdgeDir.Right)
            {
                double delta = decrease ? -0.03 : +0.03;
                _masterFactor = Math.Clamp(_masterFactor + delta, 0.2, 0.85);
                StatusMessage?.Invoke($"Master: {_masterFactor * 100:F0}%");
                SaveLayoutConfig();
                Relayout(resetFocus: false);
                return;
            }

            var ws = _workspaceManager.GetActiveWorkspace();
            if (ws == null) return;
            var tiled = ws.Windows.Where(w => w.State == WindowLayoutState.Tiled && w != mw).ToList();

            ManagedWindow? neighbor = edge == EdgeDir.Top
                ? FindWindowAbove(mw, tiled)
                : FindWindowBelow(mw, tiled);

            if (neighbor == null) return;

            double ratioStep = 0.15;
            double adjust = decrease
                ? (edge == EdgeDir.Top ? +ratioStep : -ratioStep)
                : (edge == EdgeDir.Top ? -ratioStep : +ratioStep);

            mw.StackRatio = Math.Max(0.1, mw.StackRatio + adjust);
            neighbor.StackRatio = Math.Max(0.1, neighbor.StackRatio - adjust);
            StatusMessage?.Invoke($"Ratio {mw.StackRatio:F1}/{neighbor.StackRatio:F1}");
            Relayout(resetFocus: false);
        }
        finally
        {
            _preserveMaster = false;
        }
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

    public void SwapDown()  => SwapDirectional(FindWindowBelow, "Down");
    public void SwapUp()    => SwapDirectional(FindWindowAbove, "Up");
    public void SwapLeft()  => SwapDirectional(FindWindowLeft, "Left");
    public void SwapRight() => SwapDirectional(FindWindowRight, "Right");

    private delegate ManagedWindow? WindowFinder(ManagedWindow active, List<ManagedWindow> tiled);

    private void SwapDirectional(WindowFinder finder, string direction)
    {
        var active = _focusManager.ActiveWindow;
        if (active == null || active.State != WindowLayoutState.Tiled) return;

        var ws = _workspaceManager.GetActiveWorkspace();
        if (ws == null) return;

        var tiled = ws.Windows
            .Where(w => w.State == WindowLayoutState.Tiled && w != active)
            .ToList();

        if (tiled.Count == 0) return;

        var other = finder(active, tiled);
        if (other == null)
        {
            Logger.Info($"Swap{direction}: no window in direction");
            return;
        }

        Logger.Info($"Swap {active.Title} ↔ {other.Title} ({direction})");

        var activeBounds = active.LayoutBounds;
        var otherBounds = other.LayoutBounds;

        active.LayoutBounds = otherBounds;
        other.LayoutBounds = activeBounds;

        ReSortWorkspaceWindows();
        _bspOrderedHwnds.Clear();

        _isAnimating = true;
        _pendingPositions.Clear();
        Position(active, active.LayoutBounds);
        Position(other, other.LayoutBounds);
        _isAnimating = false;
        ApplyAnimated();

        _focusManager.SetActiveWindow(null);
        _focusManager.SetActiveWindow(active);
        active.Focus();

        StatusMessage?.Invoke($"Swapped {direction}");
    }

    private void ReSortWorkspaceWindows()
    {
        var activeWs = _workspaceManager.GetActiveWorkspace();
        if (activeWs == null) return;

        var sorted = activeWs.Windows
            .OrderBy(w => w.LayoutBounds.Top)
            .ThenBy(w => w.LayoutBounds.Left)
            .ToList();

        activeWs.Windows.Clear();
        activeWs.Windows.AddRange(sorted);
    }

    internal static ManagedWindow? FindWindowBelow(ManagedWindow active, List<ManagedWindow> tiled)
    {
        var myBottom = active.LayoutBounds.Bottom;
        var myLeft = active.LayoutBounds.Left;
        var myRight = active.LayoutBounds.Right;

        return tiled
            .Where(w =>
            {
                var r = w.LayoutBounds;
                return r.Top >= myBottom - 0.5
                    && r.Right > myLeft + 0.5
                    && r.Left < myRight - 0.5;
            })
            .OrderBy(w => w.LayoutBounds.Top)
            .ThenBy(w => w.LayoutBounds.Left)
            .FirstOrDefault();
    }

    internal static ManagedWindow? FindWindowAbove(ManagedWindow active, List<ManagedWindow> tiled)
    {
        var myTop = active.LayoutBounds.Top;
        var myLeft = active.LayoutBounds.Left;
        var myRight = active.LayoutBounds.Right;

        return tiled
            .Where(w =>
            {
                var r = w.LayoutBounds;
                return r.Bottom <= myTop + 0.5
                    && r.Right > myLeft + 0.5
                    && r.Left < myRight - 0.5;
            })
            .OrderByDescending(w => w.LayoutBounds.Bottom)
            .ThenBy(w => w.LayoutBounds.Left)
            .FirstOrDefault();
    }

    internal static ManagedWindow? FindWindowLeft(ManagedWindow active, List<ManagedWindow> tiled)
    {
        var myLeft = active.LayoutBounds.Left;
        var myTop = active.LayoutBounds.Top;
        var myBottom = active.LayoutBounds.Bottom;

        return tiled
            .Where(w =>
            {
                var r = w.LayoutBounds;
                return r.Right <= myLeft + 0.5
                    && r.Bottom > myTop + 0.5
                    && r.Top < myBottom - 0.5;
            })
            .OrderByDescending(w => w.LayoutBounds.Right)
            .ThenBy(w => w.LayoutBounds.Top)
            .FirstOrDefault();
    }

    internal static ManagedWindow? FindWindowRight(ManagedWindow active, List<ManagedWindow> tiled)
    {
        var myRight = active.LayoutBounds.Right;
        var myTop = active.LayoutBounds.Top;
        var myBottom = active.LayoutBounds.Bottom;

        return tiled
            .Where(w =>
            {
                var r = w.LayoutBounds;
                return r.Left >= myRight - 0.5
                    && r.Bottom > myTop + 0.5
                    && r.Top < myBottom - 0.5;
            })
            .OrderBy(w => w.LayoutBounds.Left)
            .ThenBy(w => w.LayoutBounds.Top)
            .FirstOrDefault();
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
            NotifyLayoutComplete();
        }
    }

    private void UpdateResizeZones()
    {
        var ws = _workspaceManager.GetActiveWorkspace();
        if (ws == null) { ResizeZonesUpdated?.Invoke(new()); return; }
        var tiled = ws.Windows.Where(w => w.State == WindowLayoutState.Tiled).ToList();
        if (tiled.Count < 2) { ResizeZonesUpdated?.Invoke(new()); return; }

        var innerArea = new System.Windows.Rect(
            _area.X + _outer, _area.Y + _outer,
            Math.Max(MinWindowWidth, _area.Width - _outer * 2),
            Math.Max(MinWindowHeight, _area.Height - _outer * 2));

        var zones = new List<ResizeZone>();
        int zoneSize = Math.Max(8, _gap);

        switch (_layout)
        {
            case LayoutType.MasterStack:
                AddMasterStackZones(zones, tiled, innerArea, zoneSize);
                break;
            case LayoutType.Columns:
                AddColumnsZones(zones, tiled, innerArea, zoneSize);
                break;
            case LayoutType.VerticalStack:
                AddVStackZones(zones, tiled, innerArea, zoneSize);
                break;
            case LayoutType.Grid:
                AddGridZones(zones, tiled, innerArea, zoneSize);
                break;
            case LayoutType.HorizontalStack:
                AddHStackZones(zones, tiled, innerArea, zoneSize);
                break;
            case LayoutType.BSP:
                AddBSPZones(zones, tiled, innerArea, zoneSize);
                break;
        }

        ResizeZonesUpdated?.Invoke(zones);
    }

    private void AddMasterStackZones(List<ResizeZone> zones, List<ManagedWindow> tiled, System.Windows.Rect area, int zoneSize)
    {
        double mw = (area.Width - _gap) * _masterFactor;
        double zx = area.X + mw;
        zones.Add(new ResizeZone
        {
            Bounds = new System.Windows.Rect(zx, area.Y, Math.Max(zoneSize, _gap), area.Height),
            Edge = ResizeEdge.Left
        });
    }

    private void AddColumnsZones(List<ResizeZone> zones, List<ManagedWindow> tiled, System.Windows.Rect area, int zoneSize)
    {
        if (tiled.Count < 2) return;
        double firstW = (area.Width - (tiled.Count - 1) * _gap) * _masterFactor;
        double zx = area.X + firstW;
        zones.Add(new ResizeZone
        {
            Bounds = new System.Windows.Rect(zx, area.Y, Math.Max(zoneSize, _gap), area.Height),
            Edge = ResizeEdge.Left
        });
    }

    private void AddVStackZones(List<ResizeZone> zones, List<ManagedWindow> tiled, System.Windows.Rect area, int zoneSize)
    {
        double mh = (area.Height - _gap) * _masterFactor;
        double zy = area.Y + mh;
        zones.Add(new ResizeZone
        {
            Bounds = new System.Windows.Rect(area.X, zy, area.Width, Math.Max(zoneSize, _gap)),
            Edge = ResizeEdge.Top
        });
    }

    private void AddGridZones(List<ResizeZone> zones, List<ManagedWindow> tiled, System.Windows.Rect area, int zoneSize)
    {
        int cols = (int)Math.Ceiling(Math.Sqrt(tiled.Count));
        int rows = (int)Math.Ceiling((double)tiled.Count / cols);
        if (cols > 1)
        {
            double firstColW = (area.Width - (cols - 1) * _gap) * _masterFactor;
            double zx = area.X + firstColW;
            zones.Add(new ResizeZone
            {
                Bounds = new System.Windows.Rect(zx, area.Y, Math.Max(zoneSize, _gap), area.Height),
                Edge = ResizeEdge.Left
            });
        }
        if (rows > 1)
        {
            double firstRowH = (area.Height - (rows - 1) * _gap) * _masterFactor;
            double zy = area.Y + firstRowH;
            zones.Add(new ResizeZone
            {
                Bounds = new System.Windows.Rect(area.X, zy, area.Width, Math.Max(zoneSize, _gap)),
                Edge = ResizeEdge.Top
            });
        }
    }

    private void AddHStackZones(List<ResizeZone> zones, List<ManagedWindow> tiled, System.Windows.Rect area, int zoneSize)
    {
        double h = (area.Height - (tiled.Count - 1) * _gap) / tiled.Count;
        for (int i = 0; i < tiled.Count - 1; i++)
        {
            double zy = area.Y + (i + 1) * h + i * _gap;
            zones.Add(new ResizeZone
            {
                Bounds = new System.Windows.Rect(area.X, zy, area.Width, Math.Max(zoneSize, _gap)),
                Edge = ResizeEdge.Top
            });
        }
    }

    private void AddBSPZones(List<ResizeZone> zones, List<ManagedWindow> tiled, System.Windows.Rect area, int zoneSize)
    {
        double leftW = (area.Width - _gap) * _masterFactor;
        double zx = area.X + leftW;
        zones.Add(new ResizeZone
        {
            Bounds = new System.Windows.Rect(zx, area.Y, Math.Max(zoneSize, _gap), area.Height),
            Edge = ResizeEdge.Left
        });
    }
}
