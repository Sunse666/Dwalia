using Dwalia.Win32;
using static Dwalia.Win32.WindowStyles;

namespace Dwalia.Models;

public enum WindowLayoutState
{
    Tiled,
    Floating,
    Fullscreen
}

public class ManagedWindow
{
    public IntPtr Hwnd { get; init; }
    public uint ProcessId { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public WindowLayoutState State { get; set; } = WindowLayoutState.Tiled;

    public WindowInfo OriginalWindowInfo { get; init; } = null!;

    public int WorkspaceId { get; set; }

    public bool IsActive { get; set; }

    public System.Windows.Rect LayoutBounds { get; set; }

    public NativeMethods.RECT? PreFullscreenRect { get; set; }
    public WindowLayoutState? PreFullscreenState { get; set; }

    public ManagedWindow(IntPtr hwnd)
    {
        Hwnd = hwnd;
        ProcessId = WindowHelper.GetProcessId(hwnd);
        ProcessName = WindowHelper.GetProcessName(hwnd);
        Title = WindowHelper.GetWindowTextSafe(hwnd);
        OriginalWindowInfo = WindowHelper.SnapshotWindowState(hwnd);
    }

    public void UpdateTitle()
    {
        Title = WindowHelper.GetWindowTextSafe(Hwnd);
    }

    public void Focus()
    {
        NativeMethods.ShowWindow(Hwnd, SW_RESTORE);
        NativeMethods.SetForegroundWindow(Hwnd);
        NativeMethods.SetFocus(Hwnd);
    }
}
