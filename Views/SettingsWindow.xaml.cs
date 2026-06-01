using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dwalia.Configuration;
using Dwalia.Infrastructure;

namespace Dwalia.Views;

public partial class SettingsWindow : Window
{
    private readonly DwaliaConfig _config;

    public SettingsWindow()
    {
        InitializeComponent();
        _config = ServiceLocator.TryResolve<DwaliaConfig>(out var c)
            ? c
            : ConfigManager.GetDefaults();
        LoadValues();
        BgOpacitySlider.ValueChanged += (_, _) => BgOpacityLabel.Text = ((int)BgOpacitySlider.Value).ToString();
        TbOpacitySlider.ValueChanged += (_, _) => TbOpacityLabel.Text = ((int)TbOpacitySlider.Value).ToString();
        AccentColorBox.TextChanged += OnAccentChanged;
    }

    private void LoadValues()
    {
        ModKeyCombo.SelectedIndex = _config.ModKey switch
        {
            "Win" => 1, "Alt+Win" => 2, "Ctrl+Win" => 3, _ => 0
        };

        var bgHex = _config.Theme.Background ?? "#221a1b26";
        var bgAlpha = bgHex.Length >= 7
            ? int.Parse(bgHex[1..3], System.Globalization.NumberStyles.HexNumber)
            : 0x22;
        BgOpacitySlider.Value = (int)(bgAlpha / 255.0 * 100);
        BgOpacityLabel.Text = ((int)BgOpacitySlider.Value).ToString();

        AccentColorBox.Text = _config.Theme.Accent;
        TerminalBox.Text = _config.LaunchTerminal;
        OnAccentChanged(null!, null!);

        var enabled = new HashSet<string>(_config.Layout.EnabledLayouts, StringComparer.OrdinalIgnoreCase);
        ChkMasterStack.IsChecked = enabled.Contains("MasterStack");
        ChkMonocle.IsChecked = enabled.Contains("Monocle");
        ChkGrid.IsChecked = enabled.Contains("Grid");
        ChkHorizontalStack.IsChecked = enabled.Contains("HorizontalStack");
        ChkColumns.IsChecked = enabled.Contains("Columns");
        ChkVerticalStack.IsChecked = enabled.Contains("VerticalStack");
        ChkBSP.IsChecked = enabled.Contains("BSP");
    }

    private void OnAccentChanged(object sender, TextChangedEventArgs e)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(AccentColorBox.Text);
            AccentPreview.Background = new SolidColorBrush(color);
        }
        catch { }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        try
        {
            _config.ModKey = ModKeyCombo.SelectedIndex switch
            {
                1 => "Win", 2 => "Alt+Win", 3 => "Ctrl+Win", _ => "Alt+Ctrl"
            };

            var bgAlpha = (byte)((int)BgOpacitySlider.Value * 255 / 100);
            var tbAlpha = (byte)((int)TbOpacitySlider.Value * 255 / 100);
            _config.Theme.Background = $"#{bgAlpha:x2}1a1b26";
            _config.Theme.Accent = AccentColorBox.Text;
            _config.LaunchTerminal = TerminalBox.Text;

            var enabledLayouts = new List<string>();
            if (ChkMasterStack.IsChecked == true) enabledLayouts.Add("MasterStack");
            if (ChkMonocle.IsChecked == true) enabledLayouts.Add("Monocle");
            if (ChkGrid.IsChecked == true) enabledLayouts.Add("Grid");
            if (ChkHorizontalStack.IsChecked == true) enabledLayouts.Add("HorizontalStack");
            if (ChkColumns.IsChecked == true) enabledLayouts.Add("Columns");
            if (ChkVerticalStack.IsChecked == true) enabledLayouts.Add("VerticalStack");
            if (ChkBSP.IsChecked == true) enabledLayouts.Add("BSP");
            _config.Layout.EnabledLayouts = enabledLayouts.ToArray();

            if (ServiceLocator.TryResolve<ConfigManager>(out var cm))
            {
                cm.Save(_config);
                Logger.Info("Settings saved");
            }
            else
            {
                Logger.Warn("ConfigManager not found in ServiceLocator");
            }

            if (ServiceLocator.TryResolve<Managers.LayoutManager>(out var lm))
                lm.SetEnabledLayouts(_config.Layout.EnabledLayouts);

            ApplyLive(bgAlpha, tbAlpha);
            Close();
        }
        catch (Exception ex)
        {
            Logger.Error("Save settings failed", ex);
            MessageBox.Show($"Save failed: {ex.Message}", "Dwalia", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyLive(byte bgAlpha, byte tbAlpha)
    {
        var accent = TryParseColor(_config.Theme.Accent);
        Application.Current.Resources["AccentBrush"] = new SolidColorBrush(accent);

        if (Owner is not MainWindow mw) return;

        var bgGrid = mw.FindName("StatusText") as FrameworkElement;
        if (bgGrid != null)
        {
            var parent = VisualTreeHelper.GetParent(bgGrid) as Panel;
            if (parent != null)
                parent.Background = new SolidColorBrush(Color.FromArgb(bgAlpha, 0x1a, 0x1b, 0x26));
        }

        if (mw.FindName("TaskBar") is Border taskbar)
            taskbar.Background = new SolidColorBrush(Color.FromArgb(tbAlpha, 0x16, 0x16, 0x1e));
    }

    private static Color TryParseColor(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return Colors.Transparent; }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnTitleBarDrag(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }

    private void OnNavGeneral(object sender, RoutedEventArgs e)
    {
        PageGeneral.Visibility = Visibility.Visible;
        PageTheme.Visibility = Visibility.Collapsed;
        PageLayouts.Visibility = Visibility.Collapsed;
        NavGeneral.Background = new SolidColorBrush(Color.FromRgb(0x2d, 0x2d, 0x2d));
        NavGeneral.Foreground = new SolidColorBrush(Color.FromRgb(0xff, 0xff, 0xff));
        NavTheme.Background = Brushes.Transparent;
        NavTheme.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
        NavLayouts.Background = Brushes.Transparent;
        NavLayouts.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
    }

    private void OnNavTheme(object sender, RoutedEventArgs e)
    {
        PageGeneral.Visibility = Visibility.Collapsed;
        PageTheme.Visibility = Visibility.Visible;
        PageLayouts.Visibility = Visibility.Collapsed;
        NavTheme.Background = new SolidColorBrush(Color.FromRgb(0x2d, 0x2d, 0x2d));
        NavTheme.Foreground = new SolidColorBrush(Color.FromRgb(0xff, 0xff, 0xff));
        NavGeneral.Background = Brushes.Transparent;
        NavGeneral.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
        NavLayouts.Background = Brushes.Transparent;
        NavLayouts.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
    }

    private void OnNavLayouts(object sender, RoutedEventArgs e)
    {
        PageGeneral.Visibility = Visibility.Collapsed;
        PageTheme.Visibility = Visibility.Collapsed;
        PageLayouts.Visibility = Visibility.Visible;
        NavLayouts.Background = new SolidColorBrush(Color.FromRgb(0x2d, 0x2d, 0x2d));
        NavLayouts.Foreground = new SolidColorBrush(Color.FromRgb(0xff, 0xff, 0xff));
        NavGeneral.Background = Brushes.Transparent;
        NavGeneral.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
        NavTheme.Background = Brushes.Transparent;
        NavTheme.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
    }
}
