using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using static Dwalia.Win32.NativeConstants;
using static Dwalia.Win32.NativeMethods;
using static Dwalia.Win32.WindowStyles;

namespace Dwalia.Views;

public class ColorFilterOverlay : IDisposable
{
    private Window? _window;
    private bool _disposed;

    public void Apply(Color color)
    {
        if (_disposed) return;

        if (_window == null)
        {
            _window = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                Focusable = false,
                Width = SystemParameters.VirtualScreenWidth,
                Height = SystemParameters.VirtualScreenHeight,
                Left = SystemParameters.VirtualScreenLeft,
                Top = SystemParameters.VirtualScreenTop,
            };

            _window.SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(_window).Handle;
                var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
                SetWindowLongPtr(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT);
                SetWindowPos(hwnd, HWND_TOPMOST,
                    0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            };

            _window.Show();
        }

        _window.Content = new System.Windows.Controls.Grid
        {
            Background = new SolidColorBrush(color),
            IsHitTestVisible = false,
        };
    }

    public void Hide()
    {
        if (_window != null)
            _window.Content = new System.Windows.Controls.Grid { Background = Brushes.Transparent };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _window?.Close();
        _window = null;
    }
}
