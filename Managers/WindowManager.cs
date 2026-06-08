using Dwalia.Configuration;
using Dwalia.Infrastructure;
using Dwalia.Models;
using Dwalia.Win32;
using static Dwalia.Win32.NativeMethods;
using static Dwalia.Win32.WindowStyles;

namespace Dwalia.Managers;

public class WindowManager
{
    private readonly Dictionary<IntPtr, ManagedWindow> _managedWindows = new();
    private readonly Dictionary<IntPtr, IntPtr> _swallowedParents = new();
    private readonly HashSet<string> _excludedProcesses;
    private readonly HashSet<string> _excludedClasses;
    private WorkspaceManager? _workspaceManager;

    public IReadOnlyDictionary<IntPtr, ManagedWindow> ManagedWindows => _managedWindows;
    public IReadOnlyCollection<string> ExcludedProcesses => _excludedProcesses;
    public IReadOnlyCollection<string> ExcludedClasses => _excludedClasses;
    public int ManagedCount => _managedWindows.Count;

    public event EventHandler<ManagedWindow>? WindowManaged;
    public event EventHandler<ManagedWindow>? WindowUnmanaged;
    public event EventHandler<ManagedWindow>? WindowTitleChanged;
    public event EventHandler? WindowsChanged;

    public WindowManager(IEnumerable<string> excludedProcesses, IEnumerable<string> excludedClasses)
    {
        _excludedProcesses = new HashSet<string>(excludedProcesses, StringComparer.OrdinalIgnoreCase);
        _excludedProcesses.Add(System.Diagnostics.Process.GetCurrentProcess().ProcessName);
        _excludedClasses = new HashSet<string>(excludedClasses, StringComparer.OrdinalIgnoreCase);
    }

    public void SetWorkspaceManager(WorkspaceManager ws) => _workspaceManager = ws;

    public static void SetWindowOpacity(IntPtr hwnd, double opacity)
    {
        byte alpha = (byte)(Math.Clamp(opacity, 0.0, 1.0) * 255);
        IntPtr exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        if ((exStyle & WS_EX_LAYERED) == 0)
        {
            exStyle |= WS_EX_LAYERED;
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, exStyle);
        }
        SetLayeredWindowAttributes(hwnd, 0, alpha, LWA_ALPHA);
    }

    private bool ShouldIgnoreByRules(IntPtr hwnd)
    {
        if (!ServiceLocator.TryResolve<ConfigRoot>(out var config)) return false;
        return ConfigManager.ShouldIgnore(config,
            WindowHelper.GetProcessName(hwnd),
            WindowHelper.GetWindowTextSafe(hwnd));
    }

    public void Initialize()
    {
        Logger.Info("WindowManager: enumerating windows...");
        EnumWindows((hwnd, _) =>
        {
            if (WindowHelper.IsManageableWindow(hwnd))
            {
                var pn = WindowHelper.GetProcessName(hwnd);
                if (!_excludedProcesses.Contains(pn) && !_excludedClasses.Contains(WindowHelper.GetClassNameSafe(hwnd)) && !ShouldIgnoreByRules(hwnd))
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
        if (_excludedClasses.Contains(WindowHelper.GetClassNameSafe(hwnd))) return null;
        if (ShouldIgnoreByRules(hwnd)) return null;

        try
        {
            var mw = new ManagedWindow(hwnd);
            _managedWindows[hwnd] = mw;

            _workspaceManager?.AddWindow(mw, _workspaceManager.ActiveWorkspaceId);

            if (ServiceLocator.TryResolve<ConfigRoot>(out var opCfg)
                && (opCfg.Theme.WindowOpacityFocused < 1.0 || opCfg.Theme.WindowOpacityUnfocused < 1.0))
                SetWindowOpacity(hwnd, opCfg.Theme.WindowOpacityUnfocused);

            WindowManaged?.Invoke(this, mw);
            WindowsChanged?.Invoke(this, EventArgs.Empty);
            var cls = WindowHelper.GetClassNameSafe(hwnd);
            Logger.Info($"Managed: '{mw.Title}' ({mw.ProcessName}) class={cls}");
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
            ShowWindow(hwnd, SW_SHOW);
            if (mw.OriginalWindowInfo.OriginalStyle != 0)
                SetWindowLongPtr(hwnd, GWL_STYLE, mw.OriginalWindowInfo.OriginalStyle);
            if (mw.OriginalWindowInfo.OriginalExStyle != 0)
                SetWindowLongPtr(hwnd, GWL_EXSTYLE, mw.OriginalWindowInfo.OriginalExStyle);
            var r = mw.OriginalWindowInfo.OriginalRect;
            SetWindowPos(hwnd, IntPtr.Zero, r.Left, r.Top, r.Width, r.Height,
                SWP_NOZORDER | SWP_FRAMECHANGED | (mw.OriginalWindowInfo.WasVisible ? SWP_SHOWWINDOW : 0U));
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
        if (_swallowedParents.ContainsKey(hwnd))
        {
            RestoreSwallowedParent(hwnd);
        }

        if (_managedWindows.TryGetValue(hwnd, out var mw))
        {
            Logger.Info($"Window destroyed: '{mw.Title}'");
            _managedWindows.Remove(hwnd);
            _workspaceManager?.RemoveWindow(mw);
            WindowUnmanaged?.Invoke(this, mw);
            WindowsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SwallowWindow(IntPtr parentHwnd, IntPtr childHwnd)
    {
        if (!_managedWindows.TryGetValue(parentHwnd, out var parentMw)) return;
        _swallowedParents[childHwnd] = parentHwnd;
        parentMw.SwallowedByHwnd = childHwnd;
        if (_managedWindows.TryGetValue(childHwnd, out var childMw))
            childMw.SwallowingHwnd = parentHwnd;

        ShowWindow(parentHwnd, SW_HIDE);
        _workspaceManager?.RemoveWindow(parentMw);

        ServiceLocator.TryResolve<LayoutManager>(out var lm);
        lm?.Relayout();

        Logger.Info($"Swallowing: parent '{parentMw.Title}' replaced by child HWND {childHwnd}");
        WindowsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RestoreSwallowedParent(IntPtr childHwnd)
    {
        if (!_swallowedParents.TryGetValue(childHwnd, out var parentHwnd)) return;
        if (!_managedWindows.TryGetValue(parentHwnd, out var parentMw)) return;

        if (_managedWindows.TryGetValue(childHwnd, out var childMw))
            childMw.SwallowingHwnd = IntPtr.Zero;

        parentMw.SwallowedByHwnd = IntPtr.Zero;
        _workspaceManager?.AddWindow(parentMw, parentMw.WorkspaceId);
        ShowWindow(parentHwnd, SW_SHOWNOACTIVATE);
        _swallowedParents.Remove(childHwnd);

        ServiceLocator.TryResolve<LayoutManager>(out var lm2);
        lm2?.Relayout();

        Logger.Info($"Restored swallowed parent: '{parentMw.Title}'");
        WindowsChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool IsSwallowingActive(IntPtr hwnd) => _swallowedParents.ContainsKey(hwnd);
    public IntPtr GetSwallowedParent(IntPtr childHwnd) =>
        _swallowedParents.TryGetValue(childHwnd, out var parent) ? parent : IntPtr.Zero;

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
