using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using static Dwalia.Win32.NativeMethods;
using static Dwalia.Win32.WindowStyles;

namespace Dwalia.Views;

public class FocusBackground : IDisposable
{
    private readonly Window _window;
    private readonly Border _border;
    private IntPtr _hwnd;
    private bool _shown;
    private bool _disposed;

    public FocusBackground()
    {
        _border = new Border { IsHitTestVisible = false };

        _window = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = false,
            Focusable = false,
            Content = _border,
            Width = 1,
            Height = 1,
        };

        _window.SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(_window).Handle;
        };
    }

    public void Show(IntPtr behindHwnd, int x, int y, int w, int h, Color color, int radius)
    {
        if (_disposed) return;

        _border.Background = new SolidColorBrush(Color.FromArgb(0x30, color.R, color.G, color.B));
        _border.CornerRadius = new CornerRadius(radius);

        if (!_shown)
        {
            _window.Show();
            _shown = true;
        }

        SetWindowPos(_hwnd, behindHwnd, x, y, w, h, SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    public void Hide()
    {
        if (!_shown || _disposed) return;
        _window.Hide();
        _shown = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _window.Close();
    }
}
