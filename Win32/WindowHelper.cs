using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
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
        catch { return "unknown"; }
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
}
