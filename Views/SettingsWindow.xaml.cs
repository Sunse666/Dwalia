using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dwalia.Configuration;
using Dwalia.Infrastructure;
using Dwalia.Managers;

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
        InnerGapSlider.ValueChanged += (_, _) => InnerGapLabel.Text = ((int)InnerGapSlider.Value).ToString();
        OuterGapSlider.ValueChanged += (_, _) => OuterGapLabel.Text = ((int)OuterGapSlider.Value).ToString();
        MasterFactorSlider.ValueChanged += (_, _) => MasterFactorLabel.Text = ((int)MasterFactorSlider.Value).ToString();
    }

    private void LoadValues()
    {
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

        InnerGapSlider.Value = _config.Layout.InnerGap;
        OuterGapSlider.Value = _config.Layout.OuterGap;
        MasterFactorSlider.Value = _config.Layout.MasterFactor * 100.0;
        InnerGapLabel.Text = ((int)InnerGapSlider.Value).ToString();
        OuterGapLabel.Text = ((int)OuterGapSlider.Value).ToString();
        MasterFactorLabel.Text = ((int)MasterFactorSlider.Value).ToString();

        LoadKeybindings();
    }

    private void LoadKeybindings()
    {
        KeybindingsPanel.Children.Clear();

        if (!ServiceLocator.TryResolve<HotKeyManager>(out var hkm))
            return;

        var bindings = hkm.CommandBindings;
        var defaults = HotKeyManager.GetDefaultBindings();

        foreach (var kv in bindings.OrderBy(k => GetCommandDisplayName(k.Key)))
        {
            var cmdName = GetCommandDisplayName(kv.Key);
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var nameLabel = new TextBlock
            {
                Text = cmdName,
                Foreground = new SolidColorBrush(Color.FromRgb(0xcc, 0xcc, 0xcc)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(nameLabel, 0);
            row.Children.Add(nameLabel);

            var keyBox = new TextBox
            {
                Text = kv.Value,
                Tag = kv.Key,
                Height = 28
            };
            Grid.SetColumn(keyBox, 1);
            row.Children.Add(keyBox);

            var defaultText = defaults.TryGetValue(kv.Key.ToString(), out var d) ? $"Default: {d}" : "";
            var defaultLabel = new TextBlock
            {
                Text = defaultText,
                Foreground = new SolidColorBrush(Color.FromRgb(0x56, 0x5f, 0x89)),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            Grid.SetColumn(defaultLabel, 2);
            row.Children.Add(defaultLabel);

            KeybindingsPanel.Children.Add(row);
        }
    }

    private static string GetCommandDisplayName(DwaliaCommand cmd)
    {
        var name = cmd.ToString();
        var sb = new StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && char.IsLower(name[i - 1]))
                sb.Append(' ');
            sb.Append(name[i]);
        }
        return sb.ToString();
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
            var bgAlpha = (byte)((int)BgOpacitySlider.Value * 255 / 100);
            var tbAlpha = (byte)((int)TbOpacitySlider.Value * 255 / 100);
            _config.Theme.Background = $"#{bgAlpha:x2}1a1b26";
            _config.Theme.TaskbarBackground = $"#{tbAlpha:x2}16161e";
            _config.Theme.Accent = AccentColorBox.Text;
            _config.LaunchTerminal = TerminalBox.Text;
            _config.Layout.InnerGap = (int)InnerGapSlider.Value;
            _config.Layout.OuterGap = (int)OuterGapSlider.Value;
            _config.Layout.MasterFactor = MasterFactorSlider.Value / 100.0;

            var enabledLayouts = new List<string>();
            if (ChkMasterStack.IsChecked == true) enabledLayouts.Add("MasterStack");
            if (ChkMonocle.IsChecked == true) enabledLayouts.Add("Monocle");
            if (ChkGrid.IsChecked == true) enabledLayouts.Add("Grid");
            if (ChkHorizontalStack.IsChecked == true) enabledLayouts.Add("HorizontalStack");
            if (ChkColumns.IsChecked == true) enabledLayouts.Add("Columns");
            if (ChkVerticalStack.IsChecked == true) enabledLayouts.Add("VerticalStack");
            if (ChkBSP.IsChecked == true) enabledLayouts.Add("BSP");
            _config.Layout.EnabledLayouts = enabledLayouts.ToArray();

            if (!SaveKeybindings())
                return;

            if (ServiceLocator.TryResolve<ConfigManager>(out var cm))
            {
                cm.Save(_config);
                Logger.Info("Settings saved (keybindings apply after restart)");
            }
            else
            {
                Logger.Warn("ConfigManager not found in ServiceLocator");
            }

            if (ServiceLocator.TryResolve<LayoutManager>(out var lm))
            {
                lm.SetEnabledLayouts(_config.Layout.EnabledLayouts);
                lm.SetGaps(_config.Layout.InnerGap, _config.Layout.OuterGap);
                lm.SetMasterFactor(_config.Layout.MasterFactor);
            }

            ApplyLive(bgAlpha, tbAlpha);
            Close();
        }
        catch (Exception ex)
        {
            Logger.Error("Save settings failed", ex);
            MessageBox.Show($"Save failed: {ex.Message}", "Dwalia", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool SaveKeybindings()
    {
        var bindings = new Dictionary<string, string>();
        var errors = new List<string>();

        foreach (var child in KeybindingsPanel.Children)
        {
            if (child is not Grid row) continue;
            var textBox = row.Children.OfType<TextBox>().FirstOrDefault();
            if (textBox?.Tag is not DwaliaCommand cmd) continue;

            var keyStr = textBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(keyStr))
            {
                errors.Add($"{GetCommandDisplayName(cmd)}: key cannot be empty");
                continue;
            }

            try
            {
                HotKeyManager.ParseKeyString(keyStr);
                bindings[cmd.ToString()] = keyStr;
            }
            catch (Exception ex)
            {
                errors.Add($"{GetCommandDisplayName(cmd)}: {ex.Message}");
            }
        }

        if (errors.Count > 0)
        {
            MessageBox.Show(
                $"Invalid keybinding(s):\n{string.Join("\n", errors)}",
                "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        _config.Keybindings.Bindings = bindings;
        return true;
    }

    private void ApplyLive(byte bgAlpha, byte tbAlpha)
    {
        var accent = TryParseColor(_config.Theme.Accent);
        Application.Current.Resources["AccentBrush"] = new SolidColorBrush(accent);

        if (Owner is not MainWindow mw) return;
        mw.ApplyThemeFromConfig();
    }

    private static Color TryParseColor(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return Colors.Transparent; }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void OnTitleBarDrag(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }

    private void SetActivePage(Grid page)
    {
        PageGeneral.Visibility = page == PageGeneral ? Visibility.Visible : Visibility.Collapsed;
        PageTheme.Visibility = page == PageTheme ? Visibility.Visible : Visibility.Collapsed;
        PageLayouts.Visibility = page == PageLayouts ? Visibility.Visible : Visibility.Collapsed;
        PageKeybindings.Visibility = page == PageKeybindings ? Visibility.Visible : Visibility.Collapsed;

        var bg = new SolidColorBrush(Color.FromRgb(0x2d, 0x2d, 0x2d));
        var fg = new SolidColorBrush(Color.FromRgb(0xff, 0xff, 0xff));
        var muted = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));

        NavGeneral.Background = page == PageGeneral ? bg : Brushes.Transparent;
        NavGeneral.Foreground = page == PageGeneral ? fg : muted;
        NavTheme.Background = page == PageTheme ? bg : Brushes.Transparent;
        NavTheme.Foreground = page == PageTheme ? fg : muted;
        NavLayouts.Background = page == PageLayouts ? bg : Brushes.Transparent;
        NavLayouts.Foreground = page == PageLayouts ? fg : muted;
        NavKeybindings.Background = page == PageKeybindings ? bg : Brushes.Transparent;
        NavKeybindings.Foreground = page == PageKeybindings ? fg : muted;
    }

    private void OnNavGeneral(object sender, RoutedEventArgs e) => SetActivePage(PageGeneral);
    private void OnNavTheme(object sender, RoutedEventArgs e) => SetActivePage(PageTheme);
    private void OnNavLayouts(object sender, RoutedEventArgs e) => SetActivePage(PageLayouts);
    private void OnNavKeybindings(object sender, RoutedEventArgs e) => SetActivePage(PageKeybindings);
}
