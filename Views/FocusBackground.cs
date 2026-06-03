using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Dwalia.Win32;
using static Dwalia.Win32.NativeMethods;
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
    private IntPtr _activeOwnerHwnd;
    private bool _disposed;

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
            SetWindowPos(bg.Hwnd, new IntPtr(1), bg.X, bg.Y, bg.W, bg.H, SWP_NOACTIVATE);
            ApplyColor(bg);
            bg.Pending = false;
        };

        window.Show();
    }

    public void Remove(IntPtr ownerHwnd)
    {
        if (_windows.TryGetValue(ownerHwnd, out var bg))
        {
            bg.Window.Close();
            _windows.Remove(ownerHwnd);
        }
    }

    public void UpdatePosition(IntPtr ownerHwnd, int x, int y, int w, int h)
    {
        if (!_windows.TryGetValue(ownerHwnd, out var bg)) return;

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

    public void SetTarget(IntPtr ownerHwnd, int x, int y, int w, int h)
    {
        if (!_windows.TryGetValue(ownerHwnd, out var bg)) return;
        bg.X = x; bg.Y = y; bg.W = w; bg.H = h;
        bg.Pending = true;
    }

    public void AnimatePositions(int durationMs, Action onComplete)
    {
        var targets = _windows.Values
            .Where(bg => bg.Hwnd != IntPtr.Zero && bg.Pending)
            .ToList();

        foreach (var bg in targets)
        {
            NativeMethods.RECT cr;
            NativeMethods.GetWindowRect(bg.Hwnd, out cr);
            if (cr.Width <= 1 || cr.Height <= 1)
            {
                SetWindowPos(bg.Hwnd, new IntPtr(1), bg.X, bg.Y, bg.W, bg.H, SWP_NOACTIVATE);
                bg.Pending = false;
            }
        }

        targets = targets.Where(bg => bg.Pending).ToList();
        if (targets.Count == 0) { onComplete(); return; }

        var frames = new List<(BgWindow Bg, int FromX, int FromY, int FromW, int FromH,
                                        int ToX, int ToY, int ToW, int ToH)>();
        foreach (var bg in targets)
        {
            NativeMethods.RECT rect;
            if (NativeMethods.GetWindowRect(bg.Hwnd, out rect) && rect.Width > 1 && rect.Height > 1)
                frames.Add((bg, rect.Left, rect.Top, rect.Width, rect.Height, bg.X, bg.Y, bg.W, bg.H));
            else
            {
                SetWindowPos(bg.Hwnd, new IntPtr(1), bg.X, bg.Y, bg.W, bg.H, SWP_NOACTIVATE);
                bg.Pending = false;
            }
        }

        if (frames.Count == 0) { onComplete(); return; }

        var thread = new System.Threading.Thread(() =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            const uint flags = SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOCOPYBITS;
            const uint finalFlags = SWP_NOZORDER | SWP_NOACTIVATE;

            long lastFrameMs = -16;
            while (true)
            {
                long elapsedMs = sw.ElapsedMilliseconds;
                double rawT = Math.Min(1.0, (double)elapsedMs / durationMs);

                if (elapsedMs - lastFrameMs >= 16 || rawT >= 1.0)
                {
                    double t = EaseInOutCubic(rawT);
                    foreach (var (bg, fx, fy, fw, fh, tx, ty, tw, th) in frames)
                    {
                        int x = (int)(fx + (tx - fx) * t);
                        int y = (int)(fy + (ty - fy) * t);
                        int w = (int)(fw + (tw - fw) * t);
                        int h = (int)(fh + (th - fh) * t);
                        SetWindowPos(bg.Hwnd, new IntPtr(1), x, y, w, h, flags);
                    }
                    lastFrameMs = elapsedMs;
                }

                if (rawT >= 1.0) break;
                System.Threading.Thread.Sleep(1);
            }

            foreach (var (bg, _, _, _, _, tx, ty, tw, th) in frames)
            {
                SetWindowPos(bg.Hwnd, new IntPtr(1), tx, ty, tw, th, finalFlags);
                bg.Pending = false;
            }

            onComplete();
        })
        {
            IsBackground = true,
            Name = "DwaliaBgAnim"
        };
        thread.Start();
    }

    private static double EaseInOutCubic(double t)
    {
        return t < 0.5 ? 4.0 * t * t * t : 1.0 - Math.Pow(-2.0 * t + 2.0, 3.0) / 2.0;
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
        var prev = _activeOwnerHwnd;
        _activeOwnerHwnd = activeOwnerHwnd ?? IntPtr.Zero;

        if (prev != IntPtr.Zero && _windows.TryGetValue(prev, out var prevBg))
            ApplyColor(prevBg);
        if (_activeOwnerHwnd != IntPtr.Zero && _windows.TryGetValue(_activeOwnerHwnd, out var activeBg))
            ApplyColor(activeBg);
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var bg in _windows.Values)
            bg.Window.Close();
        _windows.Clear();
    }
}
