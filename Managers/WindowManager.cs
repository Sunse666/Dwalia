using Dwalia.Infrastructure;
using Dwalia.Models;
using Dwalia.Win32;
using static Dwalia.Win32.NativeMethods;
using static Dwalia.Win32.WindowStyles;

namespace Dwalia.Managers;

public class WindowManager
{
    private readonly Dictionary<IntPtr, ManagedWindow> _managedWindows = new();
    private readonly HashSet<string> _excludedProcesses;
    private WorkspaceManager? _workspaceManager;

    public IReadOnlyDictionary<IntPtr, ManagedWindow> ManagedWindows => _managedWindows;
    public IReadOnlyCollection<string> ExcludedProcesses => _excludedProcesses;
    public int ManagedCount => _managedWindows.Count;

    public event EventHandler<ManagedWindow>? WindowManaged;
    public event EventHandler<ManagedWindow>? WindowUnmanaged;
    public event EventHandler<ManagedWindow>? WindowTitleChanged;
    public event EventHandler? WindowsChanged;

    public WindowManager(IEnumerable<string> excludedProcesses)
    {
        _excludedProcesses = new HashSet<string>(excludedProcesses, StringComparer.OrdinalIgnoreCase);
        _excludedProcesses.Add(System.Diagnostics.Process.GetCurrentProcess().ProcessName);
    }

    public void SetWorkspaceManager(WorkspaceManager ws) => _workspaceManager = ws;

    public void Initialize()
    {
        Logger.Info("WindowManager: enumerating windows...");
        EnumWindows((hwnd, _) =>
        {
            if (WindowHelper.IsManageableWindow(hwnd))
            {
                var pn = WindowHelper.GetProcessName(hwnd);
                if (!_excludedProcesses.Contains(pn))
                    TryManageWindow(hwnd);
            }
            return true;
        }, IntPtr.Zero);
        Logger.Info($"WindowManager: {_managedWindows.Count} windows managed");
    }

    public ManagedWindow? TryManageWindow(IntPtr hwnd)
    {
        if (_managedWindows.ContainsKey(hwnd)) return null;
        if (!WindowHelper.IsManageableWindow(hwnd)) return null;
        var pn = WindowHelper.GetProcessName(hwnd);
        if (_excludedProcesses.Contains(pn)) return null;

        try
        {
            var mw = new ManagedWindow(hwnd);
            _managedWindows[hwnd] = mw;

            _workspaceManager?.AddWindow(mw, _workspaceManager.ActiveWorkspaceId);

            WindowManaged?.Invoke(this, mw);
            WindowsChanged?.Invoke(this, EventArgs.Empty);
            Logger.Info($"Managed: '{mw.Title}' ({mw.ProcessName})");
            return mw;
        }
        catch (Exception ex)
        {
            Logger.Error($"TryManageWindow failed hwnd={hwnd}", ex);
            return null;
        }
    }

    public void UnmanageWindow(IntPtr hwnd)
    {
        if (!_managedWindows.TryGetValue(hwnd, out var mw)) return;
        Logger.Info($"Unmanaging: '{mw.Title}'");

        if (IsWindow(hwnd))
        {
            var r = mw.OriginalWindowInfo.OriginalRect;
            SetWindowPos(hwnd, IntPtr.Zero, r.Left, r.Top, r.Width, r.Height,
                SWP_NOZORDER | (mw.OriginalWindowInfo.WasVisible ? SWP_SHOWWINDOW : 0U));
        }

        _managedWindows.Remove(hwnd);
        _workspaceManager?.RemoveWindow(mw);
        WindowUnmanaged?.Invoke(this, mw);
        WindowsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RestoreAllWindows()
    {
        Logger.Info($"Restoring {_managedWindows.Count} windows...");
        foreach (var mw in _managedWindows.Values.ToList())
            UnmanageWindow(mw.Hwnd);
        Logger.Info("All windows restored");
    }

    public void OnWindowDestroyed(IntPtr hwnd)
    {
        if (_managedWindows.TryGetValue(hwnd, out var mw))
        {
            Logger.Info($"Window destroyed: '{mw.Title}'");
            _managedWindows.Remove(hwnd);
            _workspaceManager?.RemoveWindow(mw);
            WindowUnmanaged?.Invoke(this, mw);
            WindowsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void UpdateWindowTitle(IntPtr hwnd)
    {
        if (_managedWindows.TryGetValue(hwnd, out var mw))
        {
            mw.UpdateTitle();
            WindowTitleChanged?.Invoke(this, mw);
        }
    }

    public bool IsManaged(IntPtr hwnd) => _managedWindows.ContainsKey(hwnd);
    public ManagedWindow? GetManagedWindow(IntPtr hwnd) =>
        _managedWindows.TryGetValue(hwnd, out var mw) ? mw : null;
}
