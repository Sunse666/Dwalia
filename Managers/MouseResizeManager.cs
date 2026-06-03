using System.Runtime.InteropServices;
using Dwalia.Infrastructure;
using Dwalia.Win32;
using static Dwalia.Win32.NativeMethods;
using static Dwalia.Win32.NativeConstants;

namespace Dwalia.Managers;

public enum ResizeEdge { Left, Right, Top, Bottom }

public struct ResizeZone
{
    public System.Windows.Rect Bounds;
    public ResizeEdge Edge;
}

public class MouseResizeManager : IDisposable
{
    private IntPtr _hookHandle;
    private LowLevelMouseProc _hookProcDelegate = null!;
    private bool _disposed;
    private IntPtr _dwaliaHwnd;

    private readonly List<ResizeZone> _zones = new();
    private readonly object _lock = new();
    private static readonly IntPtr CursorWE = LoadCursor(IntPtr.Zero, 32644);
    private static readonly IntPtr CursorNS = LoadCursor(IntPtr.Zero, 32645);

    private bool _isDragging;
    private int _dragStartX;
    private int _dragStartY;
    private double _dragStartMasterFactor;
    private ResizeEdge _dragEdge;

    public Func<double>? GetCurrentMasterFactor;
    public event Action<double>? MasterFactorChanged;
    public event Action? ResizeEnded;

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCursor(IntPtr hCursor);

    public void Initialize(IntPtr dwaliaHwnd)
    {
        _dwaliaHwnd = dwaliaHwnd;
        _hookProcDelegate = HookProc;
        _hookHandle = SetWindowsHookEx(WH_MOUSE_LL, _hookProcDelegate, IntPtr.Zero, 0);

        if (_hookHandle == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            Logger.Error($"SetWindowsHookEx(WH_MOUSE_LL) failed: error {error}");
        }
        else
        {
            Logger.Info("MouseResizeManager: hook installed");
        }
    }

    public void UpdateZones(List<ResizeZone> zones)
    {
        lock (_lock)
        {
            _zones.Clear();
            _zones.AddRange(zones);
        }
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || _disposed)
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

        var msg = (uint)wParam;

        if (msg == WM_MOUSEMOVE)
        {
            var ms = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            return ProcessMouseMove(ms);
        }
        else if (msg == WM_LBUTTONDOWN)
        {
            var ms = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            return ProcessMouseDown(ms);
        }
        else if (msg == WM_LBUTTONUP)
        {
            ProcessMouseUp();
        }

        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private IntPtr ProcessMouseMove(MSLLHOOKSTRUCT ms)
    {
        if (_isDragging)
        {
            var deltaX = ms.ptX - _dragStartX;
            var deltaY = ms.ptY - _dragStartY;

            double delta;
            if (_dragEdge is ResizeEdge.Left or ResizeEdge.Right)
                delta = (double)deltaX / 800.0;
            else
                delta = (double)deltaY / 600.0;

            double newFactor = Math.Clamp(_dragStartMasterFactor + delta, 0.3, 0.8);
            MasterFactorChanged?.Invoke(newFactor);

            SetCursor(_dragEdge is ResizeEdge.Left or ResizeEdge.Right ? CursorWE : CursorNS);
            return (IntPtr)1;
        }

        lock (_lock)
        {
            foreach (var zone in _zones)
            {
                if (ms.ptX >= zone.Bounds.X && ms.ptX <= zone.Bounds.X + zone.Bounds.Width
                    && ms.ptY >= zone.Bounds.Y && ms.ptY <= zone.Bounds.Y + zone.Bounds.Height)
                {
                    SetCursor(zone.Edge is ResizeEdge.Left or ResizeEdge.Right ? CursorWE : CursorNS);
                    return (IntPtr)1;
                }
            }
        }

        return IntPtr.Zero;
    }

    private IntPtr ProcessMouseDown(MSLLHOOKSTRUCT ms)
    {
        lock (_lock)
        {
            foreach (var zone in _zones)
            {
                if (ms.ptX >= zone.Bounds.X && ms.ptX <= zone.Bounds.X + zone.Bounds.Width
                    && ms.ptY >= zone.Bounds.Y && ms.ptY <= zone.Bounds.Y + zone.Bounds.Height)
                {
                    _isDragging = true;
                    _dragStartX = ms.ptX;
                    _dragStartY = ms.ptY;
                    _dragStartMasterFactor = GetCurrentMasterFactor?.Invoke() ?? 0.6;
                    _dragEdge = zone.Edge;
                    return (IntPtr)1;
                }
            }
        }
        return IntPtr.Zero;
    }

    private void ProcessMouseUp()
    {
        if (_isDragging)
        {
            _isDragging = false;
            ResizeEnded?.Invoke();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }
}
