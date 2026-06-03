using Dwalia.Infrastructure;
using Dwalia.Models;
using Dwalia.Win32;
using static Dwalia.Win32.NativeMethods;
using static Dwalia.Win32.WindowStyles;

namespace Dwalia.Managers;

public class MonitorManager
{
    private readonly List<MonitorData> _monitors = new();

    public IReadOnlyList<MonitorData> Monitors => _monitors;
    public int MonitorCount => _monitors.Count;
    public int PrimaryMonitorId { get; private set; }

    public event EventHandler? MonitorsChanged;

    public void RefreshMonitors()
    {
        var oldCount = _monitors.Count;
        _monitors.Clear();

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, MonitorEnumCallback, IntPtr.Zero);

        if (_monitors.Count == 0)
        {
            _monitors.Add(new MonitorData
            {
                Id = 0, DeviceName = "Primary", IsPrimary = true,
                Bounds = new System.Windows.Rect(
                    System.Windows.SystemParameters.VirtualScreenLeft,
                    System.Windows.SystemParameters.VirtualScreenTop,
                    System.Windows.SystemParameters.VirtualScreenWidth,
                    System.Windows.SystemParameters.VirtualScreenHeight),
                WorkArea = System.Windows.SystemParameters.WorkArea
            });
            PrimaryMonitorId = 0;
        }

        Logger.Info($"MonitorManager: {_monitors.Count} monitor(s)");
        foreach (var m in _monitors)
            Logger.Info($"  [{m.Id}] {m.WorkArea.Width:F0}x{m.WorkArea.Height:F0}");

        if (oldCount != _monitors.Count)
            MonitorsChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool MonitorEnumCallback(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData)
    {
        var mi = new MONITORINFO();
        mi.cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>();
        if (!GetMonitorInfo(hMonitor, ref mi)) return true;

        bool isPrimary = (mi.dwFlags & 1) != 0;

        var bounds = new System.Windows.Rect(
            mi.rcMonitor.Left, mi.rcMonitor.Top,
            mi.rcMonitor.Width, mi.rcMonitor.Height);
        var workArea = new System.Windows.Rect(
            mi.rcWork.Left, mi.rcWork.Top,
            mi.rcWork.Width, mi.rcWork.Height);

        var monitor = new MonitorData
        {
            Id = _monitors.Count,
            DeviceName = "",
            IsPrimary = isPrimary,
            Bounds = bounds,
            WorkArea = workArea
        };
        _monitors.Add(monitor);
        if (isPrimary) PrimaryMonitorId = monitor.Id;
        return true;
    }

    public int GetMonitorIdAtCursor()
    {
        if (GetCursorPos(out var pt))
        {
            for (int i = 0; i < _monitors.Count; i++)
            {
                var m = _monitors[i];
                if (pt.X >= m.Bounds.Left && pt.X < m.Bounds.Right &&
                    pt.Y >= m.Bounds.Top && pt.Y < m.Bounds.Bottom)
                    return i;
            }
        }
        return 0;
    }

    public int GetMonitorIdForWindow(IntPtr hwnd)
    {
        if (!IsWindow(hwnd)) return 0;
        var rect = WindowHelper.GetWindowRectSafe(hwnd);
        if (rect.Width <= 0) return 0;
        int cx = rect.Left + rect.Width / 2;
        int cy = rect.Top + rect.Height / 2;
        for (int i = 0; i < _monitors.Count; i++)
        {
            var m = _monitors[i];
            if (cx >= m.Bounds.Left && cx < m.Bounds.Right &&
                cy >= m.Bounds.Top && cy < m.Bounds.Bottom)
                return i;
        }
        return 0;
    }

    public MonitorData? GetMonitor(int id)
    {
        if (id < 0 || id >= _monitors.Count) return null;
        return _monitors[id];
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);
}
