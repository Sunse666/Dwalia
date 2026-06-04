using System.Runtime.InteropServices;
using System.Text;

namespace Dwalia.Win32;

public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
public delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);
public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

public static partial class NativeMethods
{
    private const string User32 = "user32.dll";
    private const string DwmApi = "dwmapi.dll";
    private const string Kernel32 = "kernel32.dll";
    private const string PsApi = "psapi.dll";

    #region Window Management

    [DllImport(User32, SetLastError = true)]
    public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport(User32, SetLastError = true)]
    public static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport(User32, SetLastError = true)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport(User32, SetLastError = true)]
    public static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport(User32, SetLastError = true)]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport(User32, SetLastError = true)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport(User32, SetLastError = true)]
    public static extern bool IsWindowEnabled(IntPtr hWnd);

    [DllImport(User32, SetLastError = true)]
    public static extern nint GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport(User32, SetLastError = true)]
    public static extern nint SetWindowLongPtr(IntPtr hWnd, int nIndex, nint dwNewLong);

    [DllImport(User32, SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport(User32, SetLastError = true)]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport(User32, SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport(User32, SetLastError = true)]
    public static extern IntPtr BeginDeferWindowPos(int nNumWindows);

    [DllImport(User32, SetLastError = true)]
    public static extern IntPtr DeferWindowPos(IntPtr hWinPosInfo, IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport(User32, SetLastError = true)]
    public static extern bool EndDeferWindowPos(IntPtr hWinPosInfo);

    [DllImport(User32, SetLastError = true)]
    public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    [DllImport(User32, SetLastError = true)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport(User32, SetLastError = true)]
    public static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport(User32, SetLastError = true)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport(User32, SetLastError = true)]
    public static extern IntPtr WindowFromPoint(int x, int y);

    [DllImport(User32, SetLastError = true)]
    public static extern bool FlashWindow(IntPtr hWnd, bool bInvert);

    [DllImport("winmm.dll")]
    public static extern uint timeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll")]
    public static extern uint timeEndPeriod(uint uPeriod);

    [DllImport(User32, SetLastError = true)]
    public static extern IntPtr GetForegroundWindow();

    [DllImport(User32, SetLastError = true)]
    public static extern IntPtr GetDesktopWindow();

    [DllImport(User32, SetLastError = true)]
    public static extern IntPtr GetShellWindow();

    [DllImport(User32, SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport(User32, SetLastError = true)]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport(User32, SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport(User32, SetLastError = true)]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport(User32, SetLastError = true)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport(User32, SetLastError = true)]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport(User32, SetLastError = true)]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport(User32, SetLastError = true)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport(User32)]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip,
        MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport(User32)]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    #endregion

    #region Window Events

    [DllImport(User32, SetLastError = true)]
    public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport(User32, SetLastError = true)]
    public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    #endregion

    #region Hotkeys

    [DllImport(User32, SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport(User32, SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    #endregion

    #region Hook

    [DllImport(User32, SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport(User32, SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport(User32, SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(IntPtr hHook);

    [DllImport(User32, SetLastError = true)]
    public static extern IntPtr CallNextHookEx(IntPtr hHook, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport(User32)]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport(User32)]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public int ptX;
        public int ptY;
        public uint mouseData;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [DllImport(User32)]
    public static extern IntPtr SetCursor(IntPtr hCursor);

    [DllImport(User32)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    #endregion

    #region Process Info

    [DllImport(Kernel32)]
    public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport(Kernel32)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport(PsApi, SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern uint GetModuleBaseName(IntPtr hProcess, IntPtr hModule, StringBuilder lpBaseName, uint nSize);

    [DllImport(Kernel32)]
    public static extern uint GetCurrentThreadId();

    [DllImport(Kernel32, SetLastError = true)]
    public static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport(Kernel32, SetLastError = true)]
    public static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport(Kernel32, SetLastError = true)]
    public static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    public const uint TH32CS_SNAPPROCESS = 0x00000002;

    #endregion

    #region DWM

    [DllImport(DwmApi)]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, uint dwAttribute, out int pvAttribute, int cbAttribute);

    [DllImport(DwmApi)]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, uint dwAttribute, out RECT pvAttribute, int cbAttribute);

    [DllImport(DwmApi)]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, uint dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport(DwmApi)]
    public static extern int DwmFlush();

    #endregion

    #region Shell

    [DllImport("shell32.dll")]
    public static extern IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    #endregion

    #region DPI

    [DllImport(User32)]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    #endregion

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport(User32)]
    internal static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public uint uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uVersionOrTimeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PROCESSENTRY32
    {
        public int dwSize;
        public int cntUsage;
        public int th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public int th32ModuleID;
        public int cntThreads;
        public int th32ParentProcessID;
        public int pcPriClassBase;
        public int dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }
}
