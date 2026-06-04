using Dwalia.Configuration;
using Dwalia.Infrastructure;
using Dwalia.Models;
using static Dwalia.Win32.NativeMethods;
using static Dwalia.Win32.WindowStyles;

namespace Dwalia.Managers;

public enum LayoutType { MasterStack, Monocle, Grid, HorizontalStack, Columns, VerticalStack, BSP, Dynamic }

public class LayoutManager
{
    private readonly WindowManager _windowManager;
    private readonly WorkspaceManager _workspaceManager;
    private readonly FocusManager _focusManager;
    private System.Windows.Rect _area;
    private readonly Dictionary<int, System.Windows.Rect> _monitorAreas = new();
    public System.Windows.Rect Area => _monitorAreas.TryGetValue(_workspaceManager.CurrentMonitorId, out var a) ? a : _area;
    public System.Windows.Rect GetAreaForMonitor(int monitorId) => _monitorAreas.TryGetValue(monitorId, out var a) ? a : _area;
    public double CurrentMasterFactor => _masterFactor;
    public bool SmartGaps
    {
        get => _smartGaps;
        set { _smartGaps = value; Relayout(); }
    }
    public event EventHandler<LayoutType>? LayoutChanged;
    public event Action<string>? StatusMessage;
    public event Action? RelayoutCompleted;

    private LayoutType _layout = LayoutType.Dynamic;
    private double _masterFactor = 0.6;
    private int _gap = 4;
    private int _outer = 2;
    private bool _smartGaps;
    private List<LayoutType> _enabledLayouts = new() { LayoutType.Dynamic };
    private List<IntPtr> _bspOrderedHwnds = new();

    private bool _isAnimating;
    private readonly List<(ManagedWindow Window, System.Windows.Rect Target)> _pendingPositions = new();
    private List<(ManagedWindow Window, System.Windows.Rect From, System.Windows.Rect To)> _animFrames = new();
    private int _animVersion;
    private int _animDuration;
    private bool _disableAnimation;
    private bool _preserveMaster;
    private bool _layoutJustSwitched;
    private ManagedWindow? _currentMaster;

    public event Action<List<ResizeZone>>? ResizeZonesUpdated;
    public event Action? LayoutTargetsComputed;

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
            _smartGaps = config.Layout.SmartGaps;
            _animDuration = config.General.AnimationDuration;
            _disableAnimation = !config.General.AnimationEnabled;
        }

        _windowManager.WindowManaged += (_, mw) =>
        {
            Relayout(resetFocus: false);
            _focusManager.SetActiveWindow(mw);
        };
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
        _workspaceManager.StickyChanged += (_, _) => Relayout();
    }

    public void SetArea(IntPtr mainHwnd, double topOffset, double totalBarHeight)
    {
        _monitorAreas.Clear();

        if (ServiceLocator.TryResolve<MonitorManager>(out var mm) && mm.MonitorCount > 0)
        {
            foreach (var m in mm.Monitors)
            {
                var area = new System.Windows.Rect(
                    m.WorkArea.Left,
                    m.WorkArea.Top + topOffset,
                    m.WorkArea.Width,
                    Math.Max(200, m.WorkArea.Height - totalBarHeight));
                _monitorAreas[m.Id] = area;
                Logger.Info($"Monitor {m.Id} work area: {area.Width:F0}x{area.Height:F0} @ ({area.X:F0},{area.Y:F0})");
            }
            _area = _monitorAreas.GetValueOrDefault(0, _area);
        }
        else
        {
            var monitor = MonitorFromWindow(mainHwnd, MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFO();
            mi.cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>();
            if (GetMonitorInfo(monitor, ref mi))
            {
                _area = new System.Windows.Rect(
                    mi.rcWork.Left,
                    mi.rcWork.Top + topOffset,
                    mi.rcWork.Width,
                    Math.Max(200, mi.rcWork.Height - totalBarHeight));
            }
            _monitorAreas[0] = _area;
            Logger.Info($"Work area: {_area.Width:F0}x{_area.Height:F0} @ ({_area.X:F0},{_area.Y:F0})");
        }
        Relayout();
    }

    public void Relayout(bool resetFocus = true)
    {
        _isAnimating = true;
        _pendingPositions.Clear();

        var activeWorkspaceIds = new HashSet<int>();
        if (ServiceLocator.TryResolve<MonitorManager>(out var mm) && mm.MonitorCount > 0)
        {
            foreach (var m in mm.Monitors)
                activeWorkspaceIds.Add(_workspaceManager.GetActiveWorkspaceIdForMonitor(m.Id));
        }
        else
        {
            activeWorkspaceIds.Add(_workspaceManager.ActiveWorkspaceId);
        }

        Logger.Info($"Relayout: {_windowManager.ManagedWindows.Count} managed, active workspaces: [{string.Join(",", activeWorkspaceIds)}]");

        foreach (var mw in _windowManager.ManagedWindows.Values)
        {
            if (!IsWindow(mw.Hwnd)) continue;
            if (mw.IsScratchpad) continue;
            if (mw.SwallowedByHwnd != IntPtr.Zero) continue;
            if (mw.State == WindowLayoutState.Floating) continue;
            if (mw.IsSticky)
            {
                ShowWindow(mw.Hwnd, SW_SHOWNOACTIVATE);
                continue;
            }
            var onActive = activeWorkspaceIds.Contains(mw.WorkspaceId);
            var sw = onActive ? SW_SHOWNOACTIVATE : SW_HIDE;
            ShowWindow(mw.Hwnd, sw);
        }

        if (ServiceLocator.TryResolve<MonitorManager>(out var monitorMgr) && monitorMgr.MonitorCount > 0)
        {
            foreach (var monitor in monitorMgr.Monitors)
            {
                var activeWsId = _workspaceManager.GetActiveWorkspaceIdForMonitor(monitor.Id);
                var ws = _workspaceManager.GetWorkspace(activeWsId);
                if (ws == null) continue;
                _layout = ws.Layout;

                var area = GetAreaForMonitor(monitor.Id);
                var windows = ws.Windows.ToList();
                foreach (var otherWs in _workspaceManager.Workspaces)
                {
                    if (otherWs.Id == ws.Id) continue;
                    foreach (var w in otherWs.Windows.Where(w => w.IsSticky && w.MonitorId == monitor.Id))
                    {
                        if (!windows.Contains(w))
                            windows.Add(w);
                    }
                }
                var tiled = windows.Where(w => w.State == WindowLayoutState.Tiled).ToList();

                if (tiled.Count > 0)
                    ArrangeTiledInArea(tiled, area, activeWsId);
            }

            _isAnimating = false;
            _layoutJustSwitched = false;
            ApplyAnimated();

            if (resetFocus)
            {
                var currentMonitorActiveWsId = _workspaceManager.GetActiveWorkspaceIdForMonitor(_workspaceManager.CurrentMonitorId);
                var currentWs = _workspaceManager.GetWorkspace(currentMonitorActiveWsId);
                if (currentWs != null && currentWs.Windows.Count > 0)
                {
                    _layout = currentWs.Layout;
                    var ordered = GetOrderedWindows();
                    if (ordered.Count > 0)
                        _focusManager.SetActiveWindow(ordered[0]);
                }
            }
        }
        else
        {
            var ws = _workspaceManager.GetActiveWorkspace();
            if (ws == null) { _layoutJustSwitched = false; return; }
            _layout = ws.Layout;

            var windows = ws.Windows.ToList();
            foreach (var otherWs in _workspaceManager.Workspaces)
            {
                if (otherWs.Id == ws.Id) continue;
                foreach (var w in otherWs.Windows.Where(w => w.IsSticky))
                {
                    if (!windows.Contains(w))
                        windows.Add(w);
                }
            }
            var tiled = windows.Where(w => w.State == WindowLayoutState.Tiled).ToList();

            if (tiled.Count > 0)
                ArrangeTiled(tiled, ws.Id);

            _isAnimating = false;
            _layoutJustSwitched = false;
            ApplyAnimated();

            if (resetFocus)
            {
                var ordered = GetOrderedWindows();
                if (ordered.Count > 0)
                    _focusManager.SetActiveWindow(ordered[0]);
            }
        }

        _layoutJustSwitched = false;
    }

    private void ArrangeTiledInArea(List<ManagedWindow> windows, System.Windows.Rect area, int workspaceId)
    {
        if (!_preserveMaster && _layout == LayoutType.Monocle)
        {
            var active = _focusManager.ActiveWindow;
            if (active != null && windows.Count > 1 && windows.Contains(active))
            {
                windows.Remove(active);
                windows.Insert(0, active);
            }
        }

        int effectiveOuter = (_smartGaps && windows.Count <= 1) ? 0 : _outer;

        var layoutArea = new System.Windows.Rect(
            area.X + effectiveOuter, area.Y + effectiveOuter,
            Math.Max(MinWindowWidth, area.Width - effectiveOuter * 2),
            Math.Max(MinWindowHeight, area.Height - effectiveOuter * 2));

        switch (_layout)
        {
            case LayoutType.Monocle: ArrangeMonocle(windows, layoutArea); break;
            case LayoutType.Grid: ArrangeGrid(windows, layoutArea); break;
            case LayoutType.HorizontalStack: ArrangeHStack(windows, layoutArea); break;
            case LayoutType.Columns: ArrangeColumns(windows, layoutArea); break;
            case LayoutType.VerticalStack: ArrangeVStack(windows, layoutArea); break;
            case LayoutType.BSP: ArrangeBSP(windows, layoutArea); break;
            case LayoutType.Dynamic: ArrangeDynamic(windows, layoutArea, workspaceId); break;
            default: ArrangeMasterStack(windows, layoutArea); break;
        }
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
                DirectPosition(mw, target);
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

        LayoutTargetsComputed?.Invoke();
    }

    public void StartWindowAnimation()
    {
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
        const long frameInterval = 16;

        long nextFrameAt = 0;
        while (version == _animVersion)
        {
            long elapsedMs = sw.ElapsedMilliseconds;
            double rawT = Math.Min(1.0, (double)elapsedMs / _animDuration);

            if (elapsedMs >= nextFrameAt || rawT >= 1.0)
            {
                double t = EaseInOutCubic(rawT);
                ApplyDeferredFrame(frames, t, animFlags);
                nextFrameAt += frameInterval;
            }

            if (rawT >= 1.0) break;

            long remaining = nextFrameAt - sw.ElapsedMilliseconds;
            if (remaining > 2)
                Thread.Sleep((int)(remaining - 1));
            while (sw.ElapsedMilliseconds < nextFrameAt && version == _animVersion)
                Thread.SpinWait(50);
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

    private static double EaseInOutCubic(double t)
    {
        return t < 0.5 ? 4.0 * t * t * t : 1.0 - Math.Pow(-2.0 * t + 2.0, 3.0) / 2.0;
    }

    private void NotifyLayoutComplete()
    {
        UpdateResizeZones();
        RelayoutCompleted?.Invoke();
    }

    private const int MinWindowWidth = 80;
    private const int ZoneEdgeMargin = 24;
    private const int MinWindowHeight = 80;

    private void ArrangeTiled(List<ManagedWindow> windows, int workspaceId)
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

        int effectiveOuter = (_smartGaps && windows.Count <= 1) ? 0 : _outer;

        var area = new System.Windows.Rect(
            _area.X + effectiveOuter, _area.Y + effectiveOuter,
            Math.Max(MinWindowWidth, _area.Width - effectiveOuter * 2),
            Math.Max(MinWindowHeight, _area.Height - effectiveOuter * 2));

        switch (_layout)
        {
            case LayoutType.Monocle: ArrangeMonocle(windows, area); break;
            case LayoutType.Grid: ArrangeGrid(windows, area); break;
            case LayoutType.HorizontalStack: ArrangeHStack(windows, area); break;
            case LayoutType.Columns: ArrangeColumns(windows, area); break;
            case LayoutType.VerticalStack: ArrangeVStack(windows, area); break;
            case LayoutType.BSP: ArrangeBSP(windows, area); break;
            case LayoutType.Dynamic: ArrangeDynamic(windows, area, workspaceId); break;
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

        double totalH = area.Height - (n - 1) * _gap;
        double totalRatio = 0;
        foreach (var w in windows) totalRatio += Math.Max(0.1, w.StackRatio);

        double yOff = 0;
        for (int i = 0; i < n; i++)
        {
            double ratio = Math.Max(0.1, windows[i].StackRatio) / totalRatio;
            double h = Math.Max(MinWindowHeight, totalH * ratio);
            if (i == n - 1) h = area.Height - yOff;

            var r = new System.Windows.Rect(area.X, area.Y + yOff, area.Width, h);
            Position(windows[i], r);
            yOff += h + _gap;
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
            masterV = _currentMaster ?? windows[0];
        else if (_layoutJustSwitched)
            masterV = windows[0];
        else
        {
            var active = _focusManager.ActiveWindow;
            masterV = (active != null && windows.Contains(active)) ? active : windows[0];
            _currentMaster = masterV;
        }
        var stack = windows.Where(w => w != masterV).ToList();

        double mh = (area.Height - _gap) * _masterFactor;
        double sh = area.Height - mh - _gap;
        Position(masterV, new System.Windows.Rect(area.X, area.Y, area.Width, mh));

        int s = stack.Count;
        if (s > 0)
        {
            double totalRatio = 0;
            foreach (var w in stack) totalRatio += Math.Max(0.1, w.StackRatio);
            double yOff = 0;
            for (int i = 0; i < s; i++)
            {
                double ratio = Math.Max(0.1, stack[i].StackRatio) / totalRatio;
                double h = Math.Max(MinWindowHeight, (sh - (s - 1) * _gap) * ratio);
                if (i == s - 1) h = sh - yOff;

                var r = new System.Windows.Rect(area.X, area.Y + mh + _gap + yOff, area.Width, h);
                Position(stack[i], r);
                yOff += h + _gap;
            }
        }
    }

    private void ArrangeBSP(List<ManagedWindow> windows, System.Windows.Rect area)
    {
        _bspOrderedHwnds.Clear();
        int n = windows.Count;
        if (n == 0) return;
        if (n == 1) { Position(windows[0], area); _bspOrderedHwnds.Add(windows[0].Hwnd); return; }

        int wsId = _workspaceManager.ActiveWorkspaceId;
        var root = _bspRoots.GetValueOrDefault(wsId);

        if (root == null || CountBSPLeaves(root) != n)
        {
            root = BuildBSPTree(windows, 0, n, false, wsId);
            _bspRoots[wsId] = root;
        }
        else
        {
            SyncBSPLeaves(root, windows, 0);
        }

        _splitNodes[root!.Id] = root;
        LayoutSplitNode(root, area, _gap);
        CollectBSPLeaves(root, _bspOrderedHwnds);
    }

    private SplitNode? BuildBSPTree(List<ManagedWindow> windows, int start, int count, bool splitVertical, int wsId)
    {
        if (count == 0) return null;
        if (count == 1) return new SplitNode { Window = windows[start] };

        int leftCount = count / 2;
        int rightCount = count - leftCount;

        var node = new SplitNode
        {
            Vertical = !splitVertical,
            Ratio = leftCount / (double)count
        };
        _splitNodes[node.Id] = node;

        node.First = BuildBSPTree(windows, start, leftCount, !splitVertical, wsId);
        node.Second = BuildBSPTree(windows, start + leftCount, rightCount, !splitVertical, wsId);
        return node;
    }

    private static int CountBSPLeaves(SplitNode? node)
    {
        if (node == null) return 0;
        if (node.Window != null) return 1;
        return CountBSPLeaves(node.First) + CountBSPLeaves(node.Second);
    }

    private static int SyncBSPLeaves(SplitNode node, List<ManagedWindow> windows, int idx)
    {
        if (node.Window != null)
        {
            if (idx < windows.Count) node.Window = windows[idx];
            return idx + 1;
        }
        if (node.First != null) idx = SyncBSPLeaves(node.First, windows, idx);
        if (node.Second != null) idx = SyncBSPLeaves(node.Second, windows, idx);
        return idx;
    }

    private static void CollectBSPLeaves(SplitNode? node, List<IntPtr> result)
    {
        if (node == null) return;
        if (node.Window != null) { result.Add(node.Window.Hwnd); return; }
        CollectBSPLeaves(node.First, result);
        CollectBSPLeaves(node.Second, result);
    }

    private void ArrangeDynamic(List<ManagedWindow> windows, System.Windows.Rect area, int workspaceId)
    {
        int n = windows.Count;
        if (n == 0) return;

        var alive = new HashSet<IntPtr>(windows.Select(w => w.Hwnd));
        var treeWindows = new HashSet<IntPtr>();

        var root = GetDynamicRoot(workspaceId);
        if (root != null)
        {
            root = PruneDeadLeaves(root, alive);
            if (root != null)
                CollectWindows(root, treeWindows);
        }

        bool needsAdd = false;
        foreach (var mw in windows)
        {
            if (!treeWindows.Contains(mw.Hwnd)) { needsAdd = true; break; }
        }

        if (!needsAdd && root != null)
        {
            LayoutSplitNode(root, area, _gap);
            return;
        }

        if (root == null)
        {
            var first = _focusManager.ActiveWindow ?? windows[0];
            root = new SplitNode { Window = first };
            treeWindows.Add(first.Hwnd);
        }

        foreach (var mw in windows)
        {
            if (treeWindows.Contains(mw.Hwnd)) continue;

            var focused = _focusManager.ActiveWindow;
            var anchor = (focused != null && treeWindows.Contains(focused.Hwnd))
                ? focused
                : windows.FirstOrDefault(w => treeWindows.Contains(w.Hwnd));
            if (anchor == null)
            {
                root = BuildTreeFromWindows(windows, area);
                break;
            }

            if (root.Window != null)
            {
                bool vert = area.Width > area.Height;
                root = new SplitNode
                {
                    Vertical = vert,
                    Ratio = 0.5,
                    First = new SplitNode { Window = anchor },
                    Second = new SplitNode { Window = mw }
                };
                treeWindows.Add(mw.Hwnd);
                continue;
            }

            var leaf = FindLeaf(root, anchor.Hwnd);
            if (leaf == null)
            {
                anchor = windows.FirstOrDefault(w =>
                    treeWindows.Contains(w.Hwnd) && FindLeaf(root, w.Hwnd) != null);
                if (anchor == null)
                {
                    root = BuildTreeFromWindows(windows, area);
                    break;
                }
            }

            var leafArea = ComputeLeafArea(root, area, anchor.Hwnd);
            bool vertical = leafArea.Width > leafArea.Height;

            var split = new SplitNode
            {
                Vertical = vertical,
                Ratio = 0.5,
                First = new SplitNode { Window = anchor },
                Second = new SplitNode { Window = mw }
            };

            ReplaceLeaf(root, anchor.Hwnd, split);
            treeWindows.Add(mw.Hwnd);
        }

        SetDynamicRoot(workspaceId, root);
        LayoutSplitNode(root, area, _gap);
    }

    private SplitNode BuildTreeFromWindows(List<ManagedWindow> windows, System.Windows.Rect area)
    {
        if (windows.Count == 0) return new SplitNode();
        if (windows.Count == 1) return new SplitNode { Window = windows[0] };

        var focused = _focusManager.ActiveWindow;
        var ordered = focused != null && windows.Contains(focused)
            ? new List<ManagedWindow> { focused }.Concat(windows.Where(w => w != focused)).ToList()
            : windows;

        bool vert = area.Width > area.Height;
        var root = new SplitNode
        {
            Vertical = vert,
            Ratio = 0.5,
            First = new SplitNode { Window = ordered[0] },
            Second = new SplitNode { Window = ordered[1] }
        };

        for (int i = 2; i < ordered.Count; i++)
        {
            var prev = ordered[i - 1];
            var leaf = FindLeaf(root, prev.Hwnd);
            if (leaf == null) continue;

            var leafArea = ComputeLeafArea(root, area, prev.Hwnd);
            bool vertical = leafArea.Width > leafArea.Height;

            var split = new SplitNode
            {
                Vertical = vertical,
                Ratio = 0.5,
                First = new SplitNode { Window = prev },
                Second = new SplitNode { Window = ordered[i] }
            };
            ReplaceLeaf(root, prev.Hwnd, split);
        }

        return root;
    }

    private static SplitNode? PruneDeadLeaves(SplitNode node, HashSet<IntPtr> alive)
    {
        if (node.Window != null)
            return alive.Contains(node.Window.Hwnd) ? node : null;

        node.First = node.First != null ? PruneDeadLeaves(node.First, alive) : null;
        node.Second = node.Second != null ? PruneDeadLeaves(node.Second, alive) : null;

        if (node.First == null && node.Second == null) return null;
        if (node.First == null) return node.Second;
        if (node.Second == null) return node.First;
        if (node.Ratio < 0.25 || node.Ratio > 0.75)
            node.Ratio = 0.5;

        return node;
    }

    private static void CollectWindows(SplitNode node, HashSet<IntPtr> result)
    {
        if (node.Window != null) { result.Add(node.Window.Hwnd); return; }
        if (node.First != null) CollectWindows(node.First, result);
        if (node.Second != null) CollectWindows(node.Second, result);
    }

    private static SplitNode? FindLeaf(SplitNode? node, IntPtr hwnd)
    {
        if (node == null) return null;
        if (node.Window != null) return node.Window.Hwnd == hwnd ? node : null;
        return FindLeaf(node.First, hwnd) ?? FindLeaf(node.Second, hwnd);
    }

    private System.Windows.Rect ComputeLeafArea(SplitNode root, System.Windows.Rect area, IntPtr hwnd)
    {
        return ComputeLeafAreaRec(root, area, hwnd, _gap);
    }

    private static System.Windows.Rect ComputeLeafAreaRec(SplitNode node, System.Windows.Rect area, IntPtr hwnd, int gap)
    {
        if (node.Window != null)
            return node.Window.Hwnd == hwnd ? area : new System.Windows.Rect();

        var firstArea = new System.Windows.Rect();
        var secondArea = new System.Windows.Rect();

        if (node.First == null || node.Second == null) return new System.Windows.Rect();

        if (node.Vertical)
        {
            double firstW = (area.Width - gap) * node.Ratio;
            double secondW = area.Width - firstW - gap;
            firstArea = new System.Windows.Rect(area.X, area.Y, firstW, area.Height);
            secondArea = new System.Windows.Rect(area.X + firstW + gap, area.Y, secondW, area.Height);
        }
        else
        {
            double firstH = (area.Height - gap) * node.Ratio;
            double secondH = area.Height - firstH - gap;
            firstArea = new System.Windows.Rect(area.X, area.Y, area.Width, firstH);
            secondArea = new System.Windows.Rect(area.X, area.Y + firstH + gap, area.Width, secondH);
        }

        var result = ComputeLeafAreaRec(node.First, firstArea, hwnd, gap);
        if (result.Width > 0) return result;
        return ComputeLeafAreaRec(node.Second, secondArea, hwnd, gap);
    }

    private static SplitNode? FindParentSplit(SplitNode? node, IntPtr hwnd)
    {
        if (node == null || node.Window != null) return null;
        if (node.First?.Window?.Hwnd == hwnd) return node;
        if (node.Second?.Window?.Hwnd == hwnd) return node;
        return FindParentSplit(node.First, hwnd) ?? FindParentSplit(node.Second, hwnd);
    }

    private static SplitNode? FindDirectionalParentSplit(SplitNode? node, IntPtr hwnd, bool vertical)
    {
        if (node == null || node.Window != null) return null;

        bool inFirst = ContainsWindow(node.First, hwnd);
        var child = inFirst ? node.First : node.Second;

        var deeper = FindDirectionalParentSplit(child, hwnd, vertical);
        if (deeper != null) return deeper;

        if (node.Vertical == vertical) return node;
        return null;
    }

    private static bool ContainsWindow(SplitNode? node, IntPtr hwnd)
    {
        if (node == null) return false;
        if (node.Window != null) return node.Window.Hwnd == hwnd;
        return ContainsWindow(node.First, hwnd) || ContainsWindow(node.Second, hwnd);
    }

    private static bool ReplaceLeaf(SplitNode node, IntPtr targetHwnd, SplitNode replacement)
    {
        if (node.First != null && node.First.Window?.Hwnd == targetHwnd)
        {
            node.First = replacement;
            return true;
        }
        if (node.Second != null && node.Second.Window?.Hwnd == targetHwnd)
        {
            node.Second = replacement;
            return true;
        }
        if (node.First != null && ReplaceLeaf(node.First, targetHwnd, replacement))
            return true;
        if (node.Second != null && ReplaceLeaf(node.Second, targetHwnd, replacement))
            return true;
        return false;
    }

    private void LayoutSplitNode(SplitNode node, System.Windows.Rect area, int gap)
    {
        if (node.Window != null)
        {
            Position(node.Window, area);
            return;
        }

        if (node.First == null || node.Second == null) return;

        if (node.Vertical)
        {
            double firstW = Math.Max(MinWindowWidth, (area.Width - gap) * node.Ratio);
            double secondW = Math.Max(MinWindowWidth, area.Width - firstW - gap);
            var firstArea = new System.Windows.Rect(area.X, area.Y, firstW, area.Height);
            var secondArea = new System.Windows.Rect(area.X + firstW + gap, area.Y, secondW, area.Height);
            LayoutSplitNode(node.First, firstArea, gap);
            LayoutSplitNode(node.Second, secondArea, gap);
        }
        else
        {
            double firstH = Math.Max(MinWindowHeight, (area.Height - gap) * node.Ratio);
            double secondH = Math.Max(MinWindowHeight, area.Height - firstH - gap);
            var firstArea = new System.Windows.Rect(area.X, area.Y, area.Width, firstH);
            var secondArea = new System.Windows.Rect(area.X, area.Y + firstH + gap, area.Width, secondH);
            LayoutSplitNode(node.First, firstArea, gap);
            LayoutSplitNode(node.Second, secondArea, gap);
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
            master = _currentMaster ?? windows[0];
        else if (_layoutJustSwitched)
            master = windows[0];
        else
        {
            var active = _focusManager.ActiveWindow;
            master = (active != null && windows.Contains(active)) ? active : windows[0];
            _currentMaster = master;
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
        else if (_disableAnimation)
            DirectPosition(mw, r);
        else
            InstantPosition(mw, r);
    }

    private static void InstantPosition(ManagedWindow mw, System.Windows.Rect r)
    {
        SetWindowPos(mw.Hwnd, IntPtr.Zero,
            (int)r.X, (int)r.Y, (int)r.Width, (int)r.Height,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOCOPYBITS);
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
        ApplyLayoutChange();
    }

    public void CycleLayoutPrevious()
    {
        var idx = _enabledLayouts.IndexOf(_layout);
        _layout = _enabledLayouts[(idx - 1 + _enabledLayouts.Count) % _enabledLayouts.Count];
        ApplyLayoutChange();
    }

    private void ApplyLayoutChange()
    {
        _layoutJustSwitched = true;
        var ws = _workspaceManager.GetActiveWorkspace();
        if (_layout != LayoutType.Dynamic)
            _dynamicRoots.Remove(ws?.Id ?? _workspaceManager.ActiveWorkspaceId);
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

    public void SetMasterFactor(double value, bool save = true)
    {
        _masterFactor = Math.Clamp(value, 0.3, 0.8);
        if (save) SaveLayoutConfig();
        if (_disableAnimation)
            DirectMasterFactorLayout();
        else
            Relayout();
    }

    private void DirectMasterFactorLayout()
    {
        var ws = _workspaceManager.GetActiveWorkspace();
        if (ws == null) return;
        var tiled = ws.Windows.Where(w => w.State == WindowLayoutState.Tiled).ToList();
        if (tiled.Count == 0) return;

        int eo = (_smartGaps && tiled.Count <= 1) ? 0 : _outer;
        var area = new System.Windows.Rect(
            Area.X + eo, Area.Y + eo,
            Math.Max(MinWindowWidth, Area.Width - eo * 2),
            Math.Max(MinWindowHeight, Area.Height - eo * 2));

        switch (_layout)
        {
            case LayoutType.MasterStack: ArrangeMasterStack(tiled, area); break;
            case LayoutType.Columns: ArrangeColumns(tiled, area); break;
            case LayoutType.VerticalStack: ArrangeVStack(tiled, area); break;
            default: return;
        }

        UpdateResizeZones();
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

    public void ResizeInnerGap(int delta)
    {
        _gap = Math.Clamp(_gap + delta, 0, 24);
        Logger.Info($"Inner gap: {_gap}");
        StatusMessage?.Invoke($"Inner gap: {_gap}");
        SaveLayoutConfig();
        Relayout();
    }

    public void ResizeOuterGap(int delta)
    {
        _outer = Math.Clamp(_outer + delta, 0, 12);
        Logger.Info($"Outer gap: {_outer}");
        StatusMessage?.Invoke($"Outer gap: {_outer}");
        SaveLayoutConfig();
        Relayout();
    }

    private class SplitNode
    {
        public ManagedWindow? Window;
        public SplitNode? First;
        public SplitNode? Second;
        public bool Vertical;
        public double Ratio = 0.5;
        public int Id;

        private static int _nextId;
        public SplitNode() { Id = System.Threading.Interlocked.Increment(ref _nextId); }
    }

    private readonly Dictionary<int, SplitNode?> _dynamicRoots = new();
    private readonly Dictionary<int, SplitNode> _splitNodes = new();
    private readonly Dictionary<int, SplitNode?> _bspRoots = new();

    private SplitNode? GetDynamicRoot(int workspaceId) =>
        _dynamicRoots.TryGetValue(workspaceId, out var r) ? r : null;

    private void SetDynamicRoot(int workspaceId, SplitNode? value)
    {
        _dynamicRoots[workspaceId] = value;
    }

    private enum EdgeDir { Left, Right, Top, Bottom, None }

    public void ResizeLeft() => DoResize(false, true);
    public void ResizeRight() => DoResize(false, false);
    public void ResizeDown() => DoResize(true, false);
    public void ResizeUp() => DoResize(true, true);

    private void DoResize(bool horizontal, bool decrease)
    {
        if (_layout != LayoutType.Dynamic && _layout != LayoutType.BSP
            && _layout != LayoutType.MasterStack && _layout != LayoutType.Columns
            && _layout != LayoutType.VerticalStack)
            return;

        _preserveMaster = true;
        _disableAnimation = true;
        try
        {
            var mw = _focusManager.ActiveWindow;
            if (mw == null || mw.State != WindowLayoutState.Tiled) return;

            if (_layout == LayoutType.Dynamic || _layout == LayoutType.BSP)
            {
                SplitNode? root = _layout == LayoutType.Dynamic
                    ? GetDynamicRoot(mw.WorkspaceId)
                    : _bspRoots.GetValueOrDefault(mw.WorkspaceId);
                if (root != null)
                {
                    var parent = FindDirectionalParentSplit(root, mw.Hwnd, !horizontal);
                    if (parent != null)
                    {
                        double d = decrease ? -0.05 : +0.05;
                        parent.Ratio = Math.Clamp(parent.Ratio + d, 0.15, 0.85);
                        StatusMessage?.Invoke($"Split: {parent.Ratio * 100:F0}%");
                        LayoutSplitNodeDirect(root, GetLayoutArea(), _gap);
                        UpdateResizeZones();
                        RelayoutCompleted?.Invoke();
                        return;
                    }
                }
            }

            double delta = decrease ? -0.03 : +0.03;
            _masterFactor = Math.Clamp(_masterFactor + delta, 0.2, 0.85);
            StatusMessage?.Invoke($"Master: {_masterFactor * 100:F0}%");
            SaveLayoutConfig();
            DirectMasterFactorLayout();
            RelayoutCompleted?.Invoke();
        }
        finally
        {
            _disableAnimation = false;
            _preserveMaster = false;
        }
    }

    private System.Windows.Rect GetLayoutArea()
    {
        var ws = _workspaceManager.GetActiveWorkspace();
        int eo = (_smartGaps && ws != null && ws.Windows.Count <= 1) ? 0 : _outer;
        return new System.Windows.Rect(
            Area.X + eo, Area.Y + eo,
            Math.Max(MinWindowWidth, Area.Width - eo * 2),
            Math.Max(MinWindowHeight, Area.Height - eo * 2));
    }

    public void SaveLayoutConfig()
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

    public void SyncAfterResize()
    {
        UpdateResizeZones();
        RelayoutCompleted?.Invoke();
    }

    public void SwapDown()  => SwapDirectional(FindWindowBelow, "Down");
    public void SwapUp()    => SwapDirectional(FindWindowAbove, "Up");
    public void SwapLeft()  => SwapDirectional(FindWindowLeft, "Left");
    public void SwapRight() => SwapDirectional(FindWindowRight, "Right");

    public void SwapWindows(ManagedWindow a, ManagedWindow b)
    {
        var aBounds = a.LayoutBounds;
        var bBounds = b.LayoutBounds;
        a.LayoutBounds = bBounds;
        b.LayoutBounds = aBounds;

        ReSortWorkspaceWindows();
        _bspOrderedHwnds.Clear();

        var dynRoot = GetDynamicRoot(a.WorkspaceId);
        if (dynRoot != null)
            SwapLeafWindows(dynRoot, a, b);
        var bspRoot = _bspRoots.GetValueOrDefault(a.WorkspaceId);
        if (bspRoot != null)
            SwapLeafWindows(bspRoot, a, b);

        if (ReferenceEquals(_currentMaster, a))
            _currentMaster = b;
        else if (ReferenceEquals(_currentMaster, b))
            _currentMaster = a;

        _isAnimating = true;
        _pendingPositions.Clear();
        Position(a, a.LayoutBounds);
        Position(b, b.LayoutBounds);
        _isAnimating = false;
        ApplyAnimated();
    }

    private static bool SwapLeafWindows(SplitNode node, ManagedWindow a, ManagedWindow b)
    {
        if (node.Window != null)
        {
            if (ReferenceEquals(node.Window, a)) { node.Window = b; return true; }
            if (ReferenceEquals(node.Window, b)) { node.Window = a; return true; }
            return false;
        }
        bool found = false;
        if (node.First != null && SwapLeafWindows(node.First, a, b)) found = true;
        if (node.Second != null && SwapLeafWindows(node.Second, a, b)) found = true;
        return found;
    }

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
            case LayoutType.BSP:
                AddBSPZones(zones, tiled, innerArea, zoneSize);
                break;
            case LayoutType.Dynamic:
                AddDynamicZones(zones, innerArea, zoneSize);
                break;
            case LayoutType.Columns:
                AddColumnsZones(zones, tiled, innerArea, zoneSize);
                break;
            case LayoutType.VerticalStack:
                AddVStackZones(zones, tiled, innerArea, zoneSize);
                break;
        }

        ResizeZonesUpdated?.Invoke(zones);
    }

    private void AddMasterStackZones(List<ResizeZone> zones, List<ManagedWindow> tiled, System.Windows.Rect area, int zoneSize)
    {
        double mw = (area.Width - _gap) * _masterFactor;
        double zx = area.X + mw - ZoneEdgeMargin;
        double zWidth = _gap + ZoneEdgeMargin * 2;
        zones.Add(new ResizeZone
        {
            Bounds = new System.Windows.Rect(zx, area.Y, Math.Max(zWidth, zoneSize), area.Height),
            Edge = ResizeEdge.Left
        });
    }

    private void AddColumnsZones(List<ResizeZone> zones, List<ManagedWindow> tiled, System.Windows.Rect area, int zoneSize)
    {
        if (tiled.Count < 2) return;
        double firstW = (area.Width - (tiled.Count - 1) * _gap) * _masterFactor;
        double zx = area.X + firstW - ZoneEdgeMargin;
        double zWidth = _gap + ZoneEdgeMargin * 2;
        zones.Add(new ResizeZone
        {
            Bounds = new System.Windows.Rect(zx, area.Y, Math.Max(zWidth, zoneSize), area.Height),
            Edge = ResizeEdge.Left
        });
    }

    private void AddVStackZones(List<ResizeZone> zones, List<ManagedWindow> tiled, System.Windows.Rect area, int zoneSize)
    {
        if (tiled.Count < 2) return;
        double mh = (area.Height - _gap) * _masterFactor;
        double zy = area.Y + mh - ZoneEdgeMargin;
        double zHeight = _gap + ZoneEdgeMargin * 2;
        zones.Add(new ResizeZone
        {
            Bounds = new System.Windows.Rect(area.X, zy, area.Width, Math.Max(zHeight, zoneSize)),
            Edge = ResizeEdge.Top
        });
    }

    private void AddBSPZones(List<ResizeZone> zones, List<ManagedWindow> tiled, System.Windows.Rect area, int zoneSize)
    {
        if (tiled.Count < 2) return;
        int wsId = _workspaceManager.ActiveWorkspaceId;
        var root = _bspRoots.GetValueOrDefault(wsId);
        if (root == null) return;
        AddBSPZonesRec(zones, root, area, zoneSize);
    }

    private void AddBSPZonesRec(List<ResizeZone> zones, SplitNode node, System.Windows.Rect area, int zoneSize)
    {
        if (node.Window != null) return;
        if (node.First == null || node.Second == null) return;

        if (node.Vertical)
        {
            double firstW = Math.Max(MinWindowWidth, (area.Width - _gap) * node.Ratio);
            double secondW = Math.Max(MinWindowWidth, area.Width - firstW - _gap);
            double zx = area.X + firstW - ZoneEdgeMargin;
            double zWidth = _gap + ZoneEdgeMargin * 2;
            zones.Add(new ResizeZone
            {
                Bounds = new System.Windows.Rect(zx, area.Y, Math.Max(zWidth, zoneSize), area.Height),
                Edge = ResizeEdge.Left,
                SplitId = node.Id
            });
            AddBSPZonesRec(zones, node.First, new System.Windows.Rect(area.X, area.Y, firstW, area.Height), zoneSize);
            AddBSPZonesRec(zones, node.Second, new System.Windows.Rect(area.X + firstW + _gap, area.Y, secondW, area.Height), zoneSize);
        }
        else
        {
            double firstH = Math.Max(MinWindowHeight, (area.Height - _gap) * node.Ratio);
            double secondH = Math.Max(MinWindowHeight, area.Height - firstH - _gap);
            double zy = area.Y + firstH - ZoneEdgeMargin;
            double zHeight = _gap + ZoneEdgeMargin * 2;
            zones.Add(new ResizeZone
            {
                Bounds = new System.Windows.Rect(area.X, zy, area.Width, Math.Max(zHeight, zoneSize)),
                Edge = ResizeEdge.Top,
                SplitId = node.Id
            });
            AddBSPZonesRec(zones, node.First, new System.Windows.Rect(area.X, area.Y, area.Width, firstH), zoneSize);
            AddBSPZonesRec(zones, node.Second, new System.Windows.Rect(area.X, area.Y + firstH + _gap, area.Width, secondH), zoneSize);
        }
    }

    private void AddDynamicZones(List<ResizeZone> zones, System.Windows.Rect area, int zoneSize)
    {
        var ws = _workspaceManager.GetActiveWorkspace();
        if (ws == null) return;
        var root = GetDynamicRoot(ws.Id);
        if (root == null) return;
        AddDynamicZonesRec(zones, root, area, zoneSize);
    }

    private void AddDynamicZonesRec(List<ResizeZone> zones, SplitNode node, System.Windows.Rect area, int zoneSize)
    {
        if (node.Window != null) return;
        if (node.First == null || node.Second == null) return;

        _splitNodes[node.Id] = node;

        if (node.Vertical)
        {
            double firstW = Math.Max(MinWindowWidth, (area.Width - _gap) * node.Ratio);
            double secondW = Math.Max(MinWindowWidth, area.Width - firstW - _gap);
            double zx = area.X + firstW - ZoneEdgeMargin;
            double zWidth = _gap + ZoneEdgeMargin * 2;
            zones.Add(new ResizeZone
            {
                Bounds = new System.Windows.Rect(zx, area.Y, Math.Max(zWidth, zoneSize), area.Height),
                Edge = ResizeEdge.Left,
                SplitId = node.Id
            });
            AddDynamicZonesRec(zones, node.First, new System.Windows.Rect(area.X, area.Y, firstW, area.Height), zoneSize);
            AddDynamicZonesRec(zones, node.Second, new System.Windows.Rect(area.X + firstW + _gap, area.Y, secondW, area.Height), zoneSize);
        }
        else
        {
            double firstH = Math.Max(MinWindowHeight, (area.Height - _gap) * node.Ratio);
            double secondH = Math.Max(MinWindowHeight, area.Height - firstH - _gap);
            double zy = area.Y + firstH - ZoneEdgeMargin;
            double zHeight = _gap + ZoneEdgeMargin * 2;
            zones.Add(new ResizeZone
            {
                Bounds = new System.Windows.Rect(area.X, zy, area.Width, Math.Max(zHeight, zoneSize)),
                Edge = ResizeEdge.Top,
                SplitId = node.Id
            });
            AddDynamicZonesRec(zones, node.First, new System.Windows.Rect(area.X, area.Y, area.Width, firstH), zoneSize);
            AddDynamicZonesRec(zones, node.Second, new System.Windows.Rect(area.X, area.Y + firstH + _gap, area.Width, secondH), zoneSize);
        }
    }

    public double GetSplitRatio(int splitId) =>
        _splitNodes.TryGetValue(splitId, out var node) ? node.Ratio : 0.5;

    public void AdjustSplitRatio(int splitId, double newRatio)
    {
        newRatio = Math.Clamp(newRatio, 0.15, 0.85);
        if (!_splitNodes.TryGetValue(splitId, out var node)) return;
        node.Ratio = newRatio;

        var ws = _workspaceManager.GetActiveWorkspace();
        if (ws == null) return;
        var root = GetDynamicRoot(ws.Id) ?? _bspRoots.GetValueOrDefault(ws.Id);
        if (root == null) return;

        int effectiveOuter = (_smartGaps && ws.Windows.Count <= 1) ? 0 : _outer;
        var layoutArea = new System.Windows.Rect(
            Area.X + effectiveOuter, Area.Y + effectiveOuter,
            Math.Max(MinWindowWidth, Area.Width - effectiveOuter * 2),
            Math.Max(MinWindowHeight, Area.Height - effectiveOuter * 2));

        LayoutSplitNodeDirect(root, layoutArea, _gap);
        UpdateResizeZones();
    }

    private void LayoutSplitNodeDirect(SplitNode node, System.Windows.Rect area, int gap)
    {
        if (node.Window != null) { DirectPosition(node.Window, area); return; }
        if (node.First == null || node.Second == null) return;

        if (node.Vertical)
        {
            double firstW = Math.Max(MinWindowWidth, (area.Width - gap) * node.Ratio);
            double secondW = Math.Max(MinWindowWidth, area.Width - firstW - gap);
            LayoutSplitNodeDirect(node.First, new System.Windows.Rect(area.X, area.Y, firstW, area.Height), gap);
            LayoutSplitNodeDirect(node.Second, new System.Windows.Rect(area.X + firstW + gap, area.Y, secondW, area.Height), gap);
        }
        else
        {
            double firstH = Math.Max(MinWindowHeight, (area.Height - gap) * node.Ratio);
            double secondH = Math.Max(MinWindowHeight, area.Height - firstH - gap);
            LayoutSplitNodeDirect(node.First, new System.Windows.Rect(area.X, area.Y, area.Width, firstH), gap);
            LayoutSplitNodeDirect(node.Second, new System.Windows.Rect(area.X, area.Y + firstH + gap, area.Width, secondH), gap);
        }
    }

    private static void DirectPosition(ManagedWindow mw, System.Windows.Rect r)
    {
        if (!IsWindow(mw.Hwnd)) return;
        mw.LayoutBounds = r;
        SetWindowPos(mw.Hwnd, IntPtr.Zero,
            (int)r.X, (int)r.Y, (int)r.Width, (int)r.Height,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS);
    }
}
