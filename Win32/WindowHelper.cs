using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Dwalia.Infrastructure;
using Dwalia.Models;
using static Dwalia.Win32.NativeMethods;
using static Dwalia.Win32.WindowStyles;

namespace Dwalia.Win32;

internal static class WindowHelper
{
    public static WindowInfo SnapshotWindowState(IntPtr hwnd)
    {
        var rect = GetWindowRectSafe(hwnd);
        return new WindowInfo
        {
            OriginalStyle = GetWindowLongPtr(hwnd, GWL_STYLE),
            OriginalExStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE),
            OriginalRect = rect,
            WasVisible = IsWindowVisible(hwnd)
        };
    }

    public static bool IsManageableWindow(IntPtr hwnd)
    {
        if (!IsWindow(hwnd)) return false;
        if (!IsWindowVisible(hwnd)) return false;

        var style = GetWindowLongPtr(hwnd, GWL_STYLE);
        var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        if ((style & WS_CHILD) != 0) return false;
        if ((style & WS_DISABLED) != 0) return false;
        if ((exStyle & WS_EX_TOOLWINDOW) != 0) return false;

        if (GetWindowTextLength(hwnd) == 0)
        {
            GetWindowRect(hwnd, out var r);
            if (r.Width <= 0 || r.Height <= 0) return false;
        }

        var className = GetClassNameSafe(hwnd);
        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd"
            or "Windows.UI.Core.CoreWindow" or "ApplicationFrameWindow")
            return false;

        if (IsWindowCloaked(hwnd)) return false;
        return true;
    }

    public static string GetWindowTextSafe(IntPtr hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length <= 0) return string.Empty;
        var sb = new StringBuilder(length + 1);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string GetProcessName(IntPtr hwnd)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out uint processId);
            if (processId == 0) return "unknown";
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName.ToLowerInvariant();
        }
        catch (Exception ex) { Logger.Warn($"Failed to get process name for hwnd 0x{hwnd:X}: {ex.Message}"); return "unknown"; }
    }

    public static uint GetProcessId(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint processId);
        return processId;
    }

    public static NativeMethods.RECT GetWindowRectSafe(IntPtr hwnd)
    {
        GetWindowRect(hwnd, out NativeMethods.RECT rect);
        return rect;
    }

    public static string GetClassNameSafe(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static bool IsWindowCloaked(IntPtr hwnd)
    {
        int result = DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int));
        return result == 0 && cloaked != 0;
    }

    public static uint GetParentProcessId(uint processId)
    {
        var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == IntPtr.Zero) return 0;

        try
        {
            var entry = new PROCESSENTRY32 { dwSize = Marshal.SizeOf<PROCESSENTRY32>() };
            if (Process32First(snapshot, ref entry))
            {
                do
                {
                    if (entry.th32ProcessID == processId)
                        return (uint)entry.th32ParentProcessID;
                }
                while (Process32Next(snapshot, ref entry));
            }
        }
        finally
        {
            CloseHandle(snapshot);
        }
        return 0;
    }

    [ComImport, Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow);
        [PreserveSig] int GetWindowDesktopId(IntPtr topLevelWindow, out Guid desktopId);
        [PreserveSig] int MoveWindowToDesktop(IntPtr topLevelWindow, ref Guid desktopId);
    }

    private static readonly Lazy<IVirtualDesktopManager?> _vdm = new(() =>
    {
        try
        {
            var t = Type.GetTypeFromCLSID(Guid.Parse("AA509086-5CA9-4C25-8F95-589D3C07B48A"));
            if (t == null) return null;
            return (IVirtualDesktopManager)Activator.CreateInstance(t)!;
        }
        catch (Exception ex) { Logger.Warn($"Failed to create VirtualDesktopManager: {ex.Message}"); return null; }
    });

    public static bool IsWindowOnCurrentDesktop(IntPtr hwnd)
    {
        if (_vdm.Value == null) return true;
        try { return _vdm.Value.IsWindowOnCurrentVirtualDesktop(hwnd) != 0; }
        catch (Exception ex) { Logger.Warn($"VirtualDesktopManager check failed: {ex.Message}"); return true; }
    }
}
