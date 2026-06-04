using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Dwalia.Infrastructure;
using static Dwalia.Win32.NativeMethods;
using static Dwalia.Win32.NativeConstants;
using static Dwalia.Win32.WindowStyles;

namespace Dwalia.Views;

public class FocusBackground : IDisposable
{
    private Color _accentColor;
    private int _radius;
    private byte _activeAlpha;
    private byte _inactiveAlpha;
    private bool _fill;
    private readonly Dictionary<IntPtr, BgWindow> _windows = new();
    private readonly Dictionary<IntPtr, BgWindow> _byHwnd = new();
    private IntPtr _activeOwnerHwnd;
    private bool _disposed;
    private bool _dragMode;

    private Color _dragSourceColor;
    private Color _dragTargetColor;
    private BgWindow? _dragSource;
    private BgWindow? _dragTarget;
    private int _dragStartX;
    private int _dragStartY;
    public event EventHandler<(IntPtr SrcHwnd, IntPtr DstHwnd)>? SwapDrop;
    public bool IsDragging => _dragSource != null;
    public bool DragMode => _dragMode;

    private class BgWindow
    {
        public Window Window = null!;
        public Border Border = null!;
        public IntPtr Hwnd;
        public IntPtr OwnerHwnd;
        public int X, Y, W, H;
        public bool Pending;
    }

    public FocusBackground(Color color, int radius, double activeOpacity, double inactiveOpacity, bool fill = true)
    {
        _accentColor = color;
        _radius = radius;
        _activeAlpha = OpacityToAlpha(activeOpacity);
        _inactiveAlpha = OpacityToAlpha(inactiveOpacity);
        _fill = fill;

        if (ServiceLocator.TryResolve<Configuration.ConfigRoot>(out var cfg))
        {
            try
            {
                if (!string.IsNullOrEmpty(cfg.Theme.DragSourceColor))
                    _dragSourceColor = (Color)ColorConverter.ConvertFromString(cfg.Theme.DragSourceColor);
                else
                    _dragSourceColor = color;
            }
            catch { _dragSourceColor = color; }

            try
            {
                if (!string.IsNullOrEmpty(cfg.Theme.DragTargetColor))
                    _dragTargetColor = (Color)ColorConverter.ConvertFromString(cfg.Theme.DragTargetColor);
                else
                    _dragTargetColor = Color.FromRgb(0xFF, 0xFF, 0xFF);
            }
            catch { _dragTargetColor = Color.FromRgb(0xFF, 0xFF, 0xFF); }
        }
        else
        {
            _dragSourceColor = color;
            _dragTargetColor = Color.FromRgb(0xFF, 0xFF, 0xFF);
        }
    }

    public void UpdateStyle(Color color, int radius, double activeOpacity, double inactiveOpacity, bool fill)
    {
        _accentColor = color;
        _radius = radius;
        _activeAlpha = OpacityToAlpha(activeOpacity);
        _inactiveAlpha = OpacityToAlpha(inactiveOpacity);
        _fill = fill;

        foreach (var bg in _windows.Values)
        {
            bg.Border.CornerRadius = new CornerRadius(_radius);
            ApplyColor(bg);
        }
    }

    private static byte OpacityToAlpha(double opacity)
    {
        return (byte)(Math.Clamp(opacity, 0.0, 1.0) * 255);
    }

    public void Add(IntPtr ownerHwnd, int x, int y, int w, int h)
    {
        if (_disposed || _windows.ContainsKey(ownerHwnd)) return;

        var border = new Border
        {
            IsHitTestVisible = false,
            CornerRadius = new CornerRadius(_radius)
        };

        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = false,
            Focusable = false,
            Content = border,
            Width = 1,
            Height = 1,
        };

        var bg = new BgWindow
        {
            Window = window,
            Border = border,
            OwnerHwnd = ownerHwnd,
            X = x, Y = y, W = w, H = h,
        };
        _windows[ownerHwnd] = bg;

        window.SourceInitialized += (_, _) =>
        {
            bg.Hwnd = new WindowInteropHelper(window).Handle;
            _byHwnd[bg.Hwnd] = bg;
            var exStyle = GetWindowLongPtr(bg.Hwnd, GWL_EXSTYLE);
            SetWindowLongPtr(bg.Hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT);
            SetWindowPos(bg.Hwnd, new IntPtr(1), bg.X, bg.Y, bg.W, bg.H,
                SWP_NOACTIVATE | SWP_FRAMECHANGED);
            ApplyColor(bg);
            bg.Pending = false;

            if (_dragMode)
            {
                exStyle = GetWindowLongPtr(bg.Hwnd, GWL_EXSTYLE);
                SetWindowLongPtr(bg.Hwnd, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT);
                ApplyDragColor(bg);
                SetWindowPos(bg.Hwnd, HWND_TOPMOST, bg.X, bg.Y, bg.W, bg.H,
                    SWP_NOACTIVATE | SWP_FRAMECHANGED);
            }
        };

        window.Show();
    }

    public void Remove(IntPtr ownerHwnd)
    {
        if (_windows.TryGetValue(ownerHwnd, out var bg))
        {
            bg.Window.Close();
            _windows.Remove(ownerHwnd);
            if (bg.Hwnd != IntPtr.Zero) _byHwnd.Remove(bg.Hwnd);
        }
    }

    public void UpdatePosition(IntPtr ownerHwnd, int x, int y, int w, int h)
    {
        if (!_windows.TryGetValue(ownerHwnd, out var bg)) return;

        if (_dragMode && ReferenceEquals(bg, _dragSource)) return;

        bg.X = x; bg.Y = y; bg.W = w; bg.H = h;

        if (bg.Hwnd != IntPtr.Zero)
        {
            SetWindowPos(bg.Hwnd, new IntPtr(1), x, y, w, h, SWP_NOACTIVATE);
            bg.Pending = false;
        }
        else
        {
            bg.Pending = true;
        }
    }

    public void SetVisible(IntPtr ownerHwnd, bool visible)
    {
        if (!_windows.TryGetValue(ownerHwnd, out var bg) || bg.Hwnd == IntPtr.Zero) return;
        SetWindowPos(bg.Hwnd, new IntPtr(1), 0, 0, 0, 0,
            SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE |
            (visible ? SWP_SHOWWINDOW : SWP_HIDEWINDOW));
    }

    public void SetActive(IntPtr? activeOwnerHwnd)
    {
        if (_dragMode) return;
        var prev = _activeOwnerHwnd;
        _activeOwnerHwnd = activeOwnerHwnd ?? IntPtr.Zero;

        if (prev != IntPtr.Zero && _windows.TryGetValue(prev, out var prevBg))
            ApplyColor(prevBg);
        if (_activeOwnerHwnd != IntPtr.Zero && _windows.TryGetValue(_activeOwnerHwnd, out var activeBg))
            ApplyColor(activeBg);
    }

    public void SetDragMode(bool enabled)
    {
        _dragMode = enabled;

        foreach (var bg in _windows.Values)
        {
            if (bg.Hwnd == IntPtr.Zero) continue;

            var exStyle = GetWindowLongPtr(bg.Hwnd, GWL_EXSTYLE);
            if (enabled)
            {
                SetWindowLongPtr(bg.Hwnd, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT);
                ApplyDragColor(bg);
                SetWindowPos(bg.Hwnd, HWND_TOPMOST, bg.X, bg.Y, bg.W, bg.H,
                    SWP_NOACTIVATE | SWP_FRAMECHANGED);
            }
            else
            {
                SetWindowLongPtr(bg.Hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT);
                ApplyColor(bg);
                SetWindowPos(bg.Hwnd, new IntPtr(1), bg.X, bg.Y, bg.W, bg.H,
                    SWP_NOACTIVATE | SWP_FRAMECHANGED);
            }
        }
    }

    private void ApplyColor(BgWindow bg)
    {
        var isActive = bg.OwnerHwnd == _activeOwnerHwnd;
        var alpha = isActive ? _activeAlpha : _inactiveAlpha;
        var color = Color.FromArgb(alpha, _accentColor.R, _accentColor.G, _accentColor.B);

        if (_fill)
        {
            bg.Border.Background = new SolidColorBrush(color);
            bg.Border.BorderBrush = null;
            bg.Border.BorderThickness = new Thickness(0);
        }
        else
        {
            bg.Border.Background = Brushes.Transparent;
            bg.Border.BorderBrush = new SolidColorBrush(color);
            bg.Border.BorderThickness = new Thickness(2);
        }
    }

    private void ApplyDragColor(BgWindow bg)
    {
        var isActive = bg.OwnerHwnd == _activeOwnerHwnd;
        byte alpha = isActive ? (byte)200 : (byte)120;
        var color = Color.FromArgb(alpha, _accentColor.R, _accentColor.G, _accentColor.B);
        var borderColor = isActive
            ? Color.FromArgb(255, _accentColor.R, _accentColor.G, _accentColor.B)
            : Color.FromArgb(180, _accentColor.R, _accentColor.G, _accentColor.B);

        bg.Border.Background = new SolidColorBrush(color);
        bg.Border.BorderBrush = new SolidColorBrush(borderColor);
        bg.Border.BorderThickness = new Thickness(isActive ? 3 : 2);
    }

    private void ApplySourceHighlight(BgWindow bg)
    {
        bg.Border.Background = new SolidColorBrush(
            Color.FromArgb(120, _dragSourceColor.R, _dragSourceColor.G, _dragSourceColor.B));
        bg.Border.BorderBrush = new SolidColorBrush(
            Color.FromArgb(255, _dragSourceColor.R, _dragSourceColor.G, _dragSourceColor.B));
        bg.Border.BorderThickness = new Thickness(4);
    }

    private void ApplyTargetHighlight(BgWindow bg)
    {
        bg.Border.Background = new SolidColorBrush(
            Color.FromArgb(80, _dragTargetColor.R, _dragTargetColor.G, _dragTargetColor.B));
        bg.Border.BorderBrush = new SolidColorBrush(
            Color.FromArgb(255, _dragTargetColor.R, _dragTargetColor.G, _dragTargetColor.B));
        bg.Border.BorderThickness = new Thickness(4);
    }

    public bool TryHandleMouseDown(IntPtr hwnd, int screenX, int screenY)
    {
        if (!_dragMode) return false;

        BgWindow? hit = null;
        foreach (var bg in _windows.Values)
        {
            if (bg.Hwnd == IntPtr.Zero) continue;
            if (screenX >= bg.X && screenX < bg.X + bg.W &&
                screenY >= bg.Y && screenY < bg.Y + bg.H)
            {
                hit = bg;
                break;
            }
        }

        if (hit == null) return false;

        _dragSource = hit;
        _dragStartX = screenX;
        _dragStartY = screenY;
        ApplySourceHighlight(hit);
        return true;
    }

    public bool TryHandleMouseMove(int screenX, int screenY)
    {
        if (!_dragMode || _dragSource == null) return false;

        BgWindow? hovered = null;
        foreach (var bg in _windows.Values)
        {
            if (bg.Hwnd == IntPtr.Zero || ReferenceEquals(bg, _dragSource)) continue;
            if (screenX >= bg.X && screenX < bg.X + bg.W &&
                screenY >= bg.Y && screenY < bg.Y + bg.H)
            {
                hovered = bg;
                break;
            }
        }

        if (!ReferenceEquals(hovered, _dragTarget))
        {
            if (_dragTarget != null) ApplyDragColor(_dragTarget);
            _dragTarget = hovered;
            if (_dragTarget != null) ApplyTargetHighlight(_dragTarget);
        }

        return true;
    }

    public bool TryHandleMouseUp(int screenX, int screenY)
    {
        if (!_dragMode || _dragSource == null) return false;

        var srcHwnd = _dragSource.OwnerHwnd;
        IntPtr dstHwnd = _dragTarget?.OwnerHwnd ?? IntPtr.Zero;

        if (_dragTarget != null)
        {
            ApplyDragColor(_dragTarget);
            _dragTarget = null;
        }
        ApplyDragColor(_dragSource);
        _dragSource = null;

        if (dstHwnd != IntPtr.Zero && dstHwnd != srcHwnd)
            SwapDrop?.Invoke(this, (srcHwnd, dstHwnd));

        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var bg in _windows.Values)
            bg.Window.Close();
        _windows.Clear();
    }
}
