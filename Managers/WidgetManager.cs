using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Dwalia.Configuration;
using Dwalia.Infrastructure;
using Dwalia.Models;
using Dwalia.Styling;
using Dwalia.Win32;
using static Dwalia.Win32.NativeMethods;
using static Dwalia.Win32.NativeConstants;
using static Dwalia.Win32.WindowStyles;

namespace Dwalia.Managers;

public class WidgetManager
{
    private readonly Dictionary<string, List<WidgetEntry>> _widgetsByBar = new();
    private DispatcherTimer? _updateTimer;
    private DispatcherTimer? _marqueeTimer;
    private PerformanceCounter? _cpuCounter;
    private PerformanceCounter? _memCounter;
    private PerformanceCounter? _netDownCounter;
    private PerformanceCounter? _netUpCounter;
    private PerformanceCounter? _gpuCounter;
    private PerformanceCounter? _diskReadCounter;
    private PerformanceCounter? _diskWriteCounter;
    private readonly List<PerformanceCounter> _allCounters = new();
    private string _cachedMedia = "";
    private bool _mediaPaused;
    private bool _mediaInit;
    private double _marqueeSpeed = 25;
    private long _lastNetSample;
    private float _lastNetDown;
    private float _lastNetUp;

    private object? _volumeEndpoint;
    private bool _volInit;
    private string _weatherCache = "";
    private DateTime _weatherLastFetch;
    private string _stockCache = "";
    private DateTime _stockLastFetch;
    private string _clipboardCache = "";
    private string _todoCache = "";
    private DateTime _todoLastFetch;
    private Dictionary<string, string> _cmdCaches = new();
    private DateTime _cmdLastFetch;

    public event Action? WidgetsChanged;

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };

    static WidgetManager()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Dwalia/1.0");
    }

    private class WidgetEntry
    {
        public WidgetConfig Config = null!;
        public Border Pill = null!;
        public TextBlock? Text;
        public StackPanel? Panel;
        public Border? Dot;
        public Canvas? Canvas;
        public TextBlock? SecondaryText;
        public Slider? VolumeSlider;
        public string CachedText = "";
        public Dictionary<IntPtr, Border> TabPills = new();

        public bool CavaExpanded;
        public bool HomeExpanded;
        public List<Button> HomeButtons = new();
        public string PowerPendingAction = "";
        public DateTime PowerPendingTime;
        public string ScriptOutput = "";
        public string ScriptPath = "";
        public DateTime ScriptLastRun;
        public bool DockInit;
    }

    public void Initialize()
    {
        if (!ServiceLocator.TryResolve<ConfigRoot>(out var cfg)) return;
        _marqueeSpeed = Math.Max(5, cfg.Theme.MarqueeSpeed);
        _widgetsByBar.Clear();

        var allPages = new List<string>();
        foreach (var w in cfg.Widgets.Where(w => w.Enabled))
        {
            if (w.BarPage != "All" && !allPages.Contains(w.BarPage))
                allPages.Add(w.BarPage);
        }
        if (!allPages.Contains("Docker"))
            allPages.Insert(0, "Docker");

        foreach (var w in cfg.Widgets.Where(w => w.Enabled))
        {
            var pages = w.BarPage == "All"
                ? allPages.ToArray()
                : new[] { w.BarPage };
            foreach (var page in pages)
            {
                if (!_widgetsByBar.ContainsKey(page))
                    _widgetsByBar[page] = new();
                _widgetsByBar[page].Add(new WidgetEntry { Config = w });
            }
        }

        Task.Run(() => { InitPerformanceCounters(); });

        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _updateTimer.Tick += (_, _) => UpdateAll();
        _updateTimer.Start();

        _marqueeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _marqueeTimer.Tick += (_, _) => StepMarqueeAll();
        _marqueeTimer.Start();

        if (ServiceLocator.TryResolve<WorkspaceManager>(out var wsm))
            wsm.WorkspaceChanged += (_, _) => RefreshWorkspaceWidgets();

        WidgetsChanged?.Invoke();
    }

    private void RefreshWorkspaceWidgets()
    {
        foreach (var list in _widgetsByBar.Values)
            foreach (var we in list)
                if (we.Config.Type == "workspace")
                    UpdateWorkspace(we);
    }

    public Panel BuildBarContent(string page)
    {
        var grid = new Grid { Margin = new Thickness(8, 0, 8, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var center = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };

        Grid.SetColumn(left, 0);
        Grid.SetColumn(center, 1);
        Grid.SetColumn(right, 2);
        grid.Children.Add(left);
        grid.Children.Add(center);
        grid.Children.Add(right);

        if (!_widgetsByBar.TryGetValue(page, out var widgets)) return grid;

        var ordered = widgets
            .OrderBy(w => w.Config.Align switch
            {
                "left" => 0, "center" => 1, _ => 2
            })
            .ThenBy(w => w.Config.Order);

        foreach (var we in ordered)
        {
            if (we.Config.Type is "window_tabs" or "launcher" or "taskbar" or "systray")
            {
                we.Panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                switch (we.Config.Align)
                {
                    case "left": left.Children.Add(we.Panel); break;
                    case "center": center.Children.Add(we.Panel); break;
                    default: right.Children.Add(we.Panel); break;
                }
            }
            else
            {
                we.Pill = BuildPill(we);
                switch (we.Config.Align)
                {
                    case "left": left.Children.Add(we.Pill); break;
                    case "center": center.Children.Add(we.Pill); break;
                    default: right.Children.Add(we.Pill); break;
                }
            }
        }
        return grid;
    }

    private Border BuildPill(WidgetEntry we)
    {
        var c = we.Config;
        var h = c.Height > 0 ? c.Height : 22;
        var cr = h / 3;

        var pillBg = ParseColor(c.PillColor) ?? GetDefaultPillBg();

        var pill = new Border
        {
            CornerRadius = new CornerRadius(cr),
            Height = h,
            Background = new SolidColorBrush(pillBg),
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(3, 0, 3, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = $"widget type:{c.Type}"
        };
        if (c.Width > 0 && c.Type != "active_window") pill.Width = c.Width;

        switch (c.Type)
        {
            case "workspace":
                pill.Child = new StackPanel { Orientation = Orientation.Horizontal };
                break;
            case "layout":
                we.Text = new TextBlock { FontSize = c.FontSize > 0 ? (double)c.FontSize : 16, FontWeight = FontWeights.SemiBold, Text = we.CachedText };
                pill.Child = we.Text;
                break;
            case "active_window":
                we.Text = new TextBlock { FontSize = c.FontSize > 0 ? (double)c.FontSize : 16, Text = "..." };
                pill.Child = we.Text;
                if (c.Width > 0) pill.MaxWidth = c.Width;
                break;
            case "clock":
            case "time_only":
            case "date_only":
                we.Text = new TextBlock { FontSize = c.FontSize > 0 ? (double)c.FontSize : 18, FontWeight = FontWeights.SemiBold, Text = we.CachedText };
                pill.Child = we.Text;
                break;
            case "network":
                we.Panel = new StackPanel { Orientation = Orientation.Horizontal };
                we.Text = new TextBlock { FontSize = c.FontSize > 0 ? (double)c.FontSize : 15, FontFamily = new FontFamily("Consolas") };
                we.SecondaryText = new TextBlock { FontSize = c.FontSize > 0 ? (double)c.FontSize : 15, FontFamily = new FontFamily("Consolas") };
                we.Panel.Children.Add(we.Text);
                we.Panel.Children.Add(new TextBlock { Text = " · ", FontSize = c.FontSize > 0 ? (double)c.FontSize : 15 });
                we.Panel.Children.Add(we.SecondaryText);
                pill.Child = we.Panel;
                break;
            case "media":
                var dotSize = Math.Max(6, (h - 6) / 3);
                we.Dot = new Border
                {
                    Width = dotSize, Height = dotSize,
                    CornerRadius = new CornerRadius(dotSize / 2),
                    Margin = new Thickness(0, 0, 5, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var canvasH = h - 4;
                var fontSize = c.FontSize > 0 ? (double)c.FontSize : 15;
                we.Canvas = new Canvas { Width = c.Width > 0 ? c.Width - dotSize - 20 : 170, Height = canvasH };
                we.Text = new TextBlock { FontSize = fontSize, FontFamily = new FontFamily("Consolas") };
                Canvas.SetTop(we.Text, (canvasH - fontSize) / 2);
                we.Canvas.Children.Add(we.Text);
                we.Text.RenderTransform = new TranslateTransform();
                var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                sp.Children.Add(we.Dot);
                sp.Children.Add(we.Canvas);
                pill.Child = sp;
                if (c.Width <= 0) pill.Width = c.Width > 0 ? c.Width : 200;
                break;
            case "window_controls":
                we.Panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                pill.Child = we.Panel;
                pill.Padding = new Thickness(2);
                pill.Background = Brushes.Transparent;
                break;
            case "cava":
                we.Panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                we.Text = new TextBlock { FontSize = c.FontSize > 0 ? (double)c.FontSize : 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, MinWidth = 45, TextAlignment = TextAlignment.Center };
                we.Panel.Children.Add(we.Text);

                var accent = ParseColor(c.TextColor) ?? GetAccentColorOrDefault();
                var trackBg = Color.FromArgb(0x44, accent.R, accent.G, accent.B);
                we.VolumeSlider = new Slider
                {
                    Minimum = 0, Maximum = 100,
                    Width = 0,
                    Height = h,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 0, 0),
                    IsMoveToPointEnabled = true,
                    Focusable = false,
                };
                try { we.VolumeSlider.Template = BuildSliderTemplate(accent, trackBg); } catch { }
                we.VolumeSlider.MouseLeftButtonDown += (_, e2) => { e2.Handled = true; };
                we.VolumeSlider.ValueChanged += (_, _) =>
                {
                    if (!_settingCavaVolume && _volumeEndpoint != null)
                    {
                        try
                        {
                            var ep = (IAudioEndpointVolume)_volumeEndpoint;
                            var g = Guid.Empty;
                            ep.SetMasterVolumeLevelScalar((float)(we.VolumeSlider!.Value / 100.0), ref g);
                            var pct = (int)we.VolumeSlider!.Value;
                            var icon = pct >= 66 ? "🔊" : pct >= 33 ? "🔉" : pct > 0 ? "🔈" : "🔇";
                            we.Text!.Text = $"{icon} {pct}%";
                        }
                        catch { }
                    }
                };
                we.Panel.Children.Add(we.VolumeSlider);
                pill.Child = we.Panel;
                pill.Padding = new Thickness(6, 2, 6, 2);
                pill.Cursor = System.Windows.Input.Cursors.Hand;
                if (c.Width <= 0) pill.MinWidth = 55;
                break;
            case "home":
                we.Panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                we.Text = new TextBlock { FontSize = c.FontSize > 0 ? (double)c.FontSize : 15, Text = "☰" };
                we.Panel.Children.Add(we.Text);
                pill.Child = we.Panel;
                pill.Cursor = System.Windows.Input.Cursors.Hand;
                break;
            case "script":
            case "label":
            case "button":
            default:
                we.Text = new TextBlock { FontSize = c.FontSize > 0 ? (double)c.FontSize : 15, Text = we.CachedText };
                pill.Child = we.Text;
                break;
        }

        if (string.IsNullOrEmpty(c.TextColor))
        {
            var lightFg = new SolidColorBrush(Colors.White);
            if (we.Panel != null) we.Panel.Children.OfType<TextBlock>().ToList().ForEach(t => t.Foreground = lightFg);
            if (we.Text != null) we.Text.Foreground = lightFg;
            if (we.SecondaryText != null) we.SecondaryText.Foreground = lightFg;
        }
        else
        {
            ApplyWidgetColor(we);
        }
        if (Styling.StyleEngine.HasStyles)
            Styling.StyleEngine.ApplyToElement(pill);

        ApplyHover(pill);
        return pill;
    }

    private void ApplyWidgetColor(WidgetEntry we)
    {
        var textColor = ParseColor(we.Config.TextColor);
        if (we.Text != null && textColor.HasValue)
            we.Text.Foreground = new SolidColorBrush(textColor.Value);
        if (we.SecondaryText != null && textColor.HasValue)
            we.SecondaryText.Foreground = new SolidColorBrush(textColor.Value);
    }

    private Color GetDefaultPillBg()
    {
        if (ServiceLocator.TryResolve<ConfigRoot>(out var cfg))
        {
            try { return (Color)ColorConverter.ConvertFromString(cfg.Theme.WidgetPillBackground); }
            catch (Exception ex) { Logger.Warn($"Widget update failed: {ex.Message}"); }
        }
        return Color.FromArgb(0x44, 0xff, 0xff, 0xff);
    }

    private static Color GetAccentColorOrDefault()
    {
        if (ServiceLocator.TryResolve<ConfigRoot>(out var cfg))
        {
            try { return (Color)ColorConverter.ConvertFromString(cfg.Theme.Accent); }
            catch { }
        }
        return Color.FromRgb(0x7a, 0xa2, 0xf7);
    }

    private static ControlTemplate BuildSliderTemplate(Color accent, Color trackBg)
    {
        var accHex = $"#{accent.R:X2}{accent.G:X2}{accent.B:X2}";
        var trkHex = $"#{trackBg.R:X2}{trackBg.G:X2}{trackBg.B:X2}";
        var dimHex = $"#{accent.R/3:X2}{accent.G/3:X2}{accent.B/3:X2}";

        var xaml =
            $""""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             TargetType="Slider">
              <Grid>
                <Border Height="6" Background="{dimHex}" CornerRadius="3" VerticalAlignment="Center"/>
                <Track x:Name="PART_Track" VerticalAlignment="Stretch">
                  <Track.DecreaseRepeatButton>
                    <RepeatButton>
                      <RepeatButton.Template>
                        <ControlTemplate TargetType="RepeatButton">
                          <Grid>
                            <Border Background="Transparent"/>
                            <Border Height="6" Background="{accHex}" CornerRadius="3,0,0,3" VerticalAlignment="Center"/>
                          </Grid>
                        </ControlTemplate>
                      </RepeatButton.Template>
                    </RepeatButton>
                  </Track.DecreaseRepeatButton>
                  <Track.Thumb>
                    <Thumb>
                      <Thumb.Template>
                        <ControlTemplate TargetType="Thumb">
                          <Grid>
                            <Border Background="Transparent" Width="26" Height="26"/>
                            <Border Width="16" Height="16" Background="{accHex}" CornerRadius="8" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                          </Grid>
                        </ControlTemplate>
                      </Thumb.Template>
                    </Thumb>
                  </Track.Thumb>
                  <Track.IncreaseRepeatButton>
                    <RepeatButton>
                      <RepeatButton.Template>
                        <ControlTemplate TargetType="RepeatButton">
                          <Grid>
                            <Border Background="Transparent"/>
                            <Border Height="6" Background="{trkHex}" CornerRadius="0,3,3,0" VerticalAlignment="Center"/>
                          </Grid>
                        </ControlTemplate>
                      </RepeatButton.Template>
                    </RepeatButton>
                  </Track.IncreaseRepeatButton>
                </Track>
              </Grid>
            </ControlTemplate>
            """";
        using var sr = new StringReader(xaml);
        using var xr = System.Xml.XmlReader.Create(sr);
        return (ControlTemplate)System.Windows.Markup.XamlReader.Load(xr);
    }

    private static Color? ParseColor(string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return null;
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch (Exception ex) { Logger.Warn($"Failed to parse color '{hex}': {ex.Message}"); return null; }
    }

    public void UpdateAll()
    {
        if (!_mediaInit)
        {
            _mediaInit = true;
            InitMediaMonitor();
        }
        foreach (var list in _widgetsByBar.Values)
            foreach (var we in list)
            {
                UpdateWidget(we);
                if (we.Text != null) we.CachedText = we.Text.Text;
            }
    }

    private void UpdateWidget(WidgetEntry we)
    {
        switch (we.Config.Type)
        {
            case "workspace":    UpdateWorkspace(we); break;
            case "layout":       UpdateLayout(we); break;
            case "active_window": UpdateActiveWindow(we); break;
            case "clock":        UpdateClock(we, "HH:mm:ss  yyyy-MM-dd"); break;
            case "time_only":    UpdateClock(we, "HH:mm:ss"); break;
            case "date_only":    UpdateClock(we, "yyyy-MM-dd"); break;
            case "cpu":          UpdateCpu(we); break;
            case "memory":       UpdateMemory(we); break;
            case "battery":      UpdateBattery(we); break;
            case "network":      UpdateNetwork(we); break;
            case "media":        UpdateMedia(we); break;
            case "gpu":          UpdateGpu(we); break;
            case "disk":         UpdateDisk(we); break;
            case "disk_usage":   UpdateDiskUsage(we); break;
            case "uptime":       UpdateUptime(we); break;
            case "wifi_ssid":    UpdateWifiSsid(we); break;
            case "ip_address":   UpdateIpAddress(we); break;
            case "public_ip":    UpdatePublicIp(we); break;
            case "vpn_status":   UpdateVpnStatus(we); break;
            case "volume":       UpdateVolume(we); break;
            case "audio_device": UpdateAudioDevice(we); break;
            case "world_clock":  UpdateWorldClock(we); break;
            case "countdown":    UpdateCountdown(we); break;
            case "window_count": UpdateWindowCount(we); break;
            case "bluetooth":    UpdateBluetooth(we); break;
            case "microphone":   UpdateMicrophone(we); break;
            case "camera":       UpdateCamera(we); break;
            case "window_tabs":  UpdateWindowTabs(we); break;
            case "launcher":     UpdateLauncher(we); break;
            case "taskbar":      UpdateTaskbar(we); break;
            case "systray":      UpdateSystray(we); break;
            case "script":       UpdateScript(we); break;
            case "label":        UpdateLabel(we); break;
            case "button":       UpdateButton(we); break;
            case "weather":      UpdateWeather(we); break;
            case "idle_time":    UpdateIdleTime(we); break;
            case "clipboard":    UpdateClipboard(we); break;
            case "process_monitor": UpdateProcessMonitor(we); break;
            case "stock":        UpdateStock(we); break;
            case "todo":         UpdateTodo(we); break;
            case "custom_command": UpdateCustomCommand(we); break;
            case "power_plan":   UpdatePowerPlan(we); break;
            case "language":     UpdateLanguage(we); break;
            case "recycle_bin":  UpdateRecycleBin(we); break;
            case "brightness":   UpdateBrightness(we); break;
            case "server_monitor": UpdateServerMonitor(we); break;
            case "notifications": UpdateNotifications(we); break;
            case "window_controls": UpdateWindowControls(we); break;
            case "power_menu":   UpdatePowerMenu(we); break;
            case "pomodoro":     UpdatePomodoro(we); break;
            case "cava":         UpdateCava(we); break;
            case "home":         UpdateHome(we); break;
        }
    }

    private void UpdateClock(WidgetEntry we, string fmt)
    {
        var f = string.IsNullOrEmpty(we.Config.Format) ? fmt : we.Config.Format;
        if (we.Text != null) we.Text.Text = DateTime.Now.ToString(f);
    }

    private void UpdateCpu(WidgetEntry we)
    {
        try
        {
            if (_cpuCounter == null) return;
            var pct = (int)(_cpuCounter.NextValue() / Environment.ProcessorCount);
            if (we.Text != null) we.Text.Text = $"{(we.Config.Format == "simple" ? "" : "◈ ")}{pct,3}%{ProgressBar(pct)}";
        }
        catch (Exception ex) { Logger.Warn($"Widget {we.Config.Type} update failed: {ex.Message}"); if (we.Text != null) we.Text.Text = " --%"; }
    }

    private void UpdateMemory(WidgetEntry we)
    {
        try
        {
            if (_memCounter == null) return;
            var pct = (int)_memCounter.NextValue();
            if (we.Text != null) we.Text.Text = $"{(we.Config.Format == "simple" ? "" : "◉ ")}{pct,3}%{ProgressBar(pct)}";
        }
        catch (Exception ex) { Logger.Warn($"Widget {we.Config.Type} update failed: {ex.Message}"); if (we.Text != null) we.Text.Text = " --%"; }
    }

    private void UpdateBattery(WidgetEntry we)
    {
        try
        {
            if (!GetSystemPowerStatus(out var ps)) { if (we.Text != null) we.Text.Text = "🔋 --"; return; }
            if (ps.BatteryFlag == 128) { if (we.Text != null) we.Text.Text = "⚡ AC"; return; }
            var pct = ps.BatteryLifePercent;
            if (pct > 100) { if (we.Text != null) we.Text.Text = "🔋 --"; return; }
            if (we.Text != null) we.Text.Text = $"{(pct >= 90 ? "🔋" : pct >= 30 ? "🔋" : "🪫")} {pct,3}%";
        }
        catch (Exception ex) { Logger.Warn($"Battery widget update failed: {ex.Message}"); if (we.Text != null) we.Text.Text = "🔋 --"; }
    }

    private void UpdateNetwork(WidgetEntry we)
    {
        try
        {
            if (_netDownCounter == null || _netUpCounter == null) return;
            var now = Stopwatch.GetTimestamp();
            var elapsed = (now - _lastNetSample) / (double)Stopwatch.Frequency;
            if (elapsed < 0.9) return;
            _lastNetSample = now;
            _lastNetDown = _netDownCounter.NextValue();
            _lastNetUp = _netUpCounter.NextValue();
        }
        catch (Exception ex) { Logger.Warn($"Network widget update failed: {ex.Message}"); }
        if (we.Text != null) we.Text.Text = $"▼ {FormatSpeed(_lastNetDown)}";
        if (we.SecondaryText != null) we.SecondaryText.Text = $"▲ {FormatSpeed(_lastNetUp)}";
    }

    private double _mediaScrollX;
    private double _mediaHalfWidth;
    private string _mediaLastText = "";

    private void UpdateMedia(WidgetEntry we)
    {
        if (string.IsNullOrEmpty(_cachedMedia))
            _ = PollWinRtMediaAsync();
        else
            UpdateWinRtMedia();

        if (we.Dot != null)
            we.Dot.Background = new SolidColorBrush(
                string.IsNullOrEmpty(_cachedMedia) || _mediaPaused
                    ? Color.FromRgb(0x56, 0x5f, 0x89)
                    : Color.FromRgb(0x4f, 0xbf, 0x6f));

        if (_cachedMedia != _mediaLastText)
        {
            _mediaLastText = _cachedMedia;
            _mediaScrollX = 0;
            if (!string.IsNullOrEmpty(_cachedMedia))
            {
                var display = $"  {_cachedMedia}     {_cachedMedia}  ";
                if (we.Text != null) we.Text.Text = display;
                we.Text?.Measure(new System.Windows.Size(double.PositiveInfinity, 16));
                _mediaHalfWidth = (we.Text?.DesiredSize.Width ?? 0) / 2;
            }
        }

        if (string.IsNullOrEmpty(_cachedMedia))
        {
            if (we.Text != null) we.Text.Text = "";
            we.CachedText = "";
        }
        else if (_mediaPaused || _mediaHalfWidth <= 170)
        {
            if (we.Text != null) we.Text.Text = _cachedMedia;
            if (we.Text?.RenderTransform is TranslateTransform tt) tt.X = 0;
        }

        we.CachedText = _cachedMedia;
    }

    private void StepMarqueeAll()
    {
        if (string.IsNullOrEmpty(_cachedMedia) || _mediaPaused || _mediaHalfWidth <= 170) return;
        _mediaScrollX -= _marqueeSpeed / 6.67;
        if (_mediaScrollX <= -_mediaHalfWidth) _mediaScrollX = 0;

        foreach (var list in _widgetsByBar.Values)
            foreach (var we in list)
                if (we.Config.Type == "media" && we.Text?.RenderTransform is TranslateTransform tt)
                    tt.X = _mediaScrollX;
    }

    private void UpdateGpu(WidgetEntry we)
    {
        try
        {
            if (_gpuCounter == null) return;
            var pct = (int)_gpuCounter.NextValue();
            if (we.Text != null) we.Text.Text = $"GPU {pct,3}%";
        }
        catch (Exception ex) { Logger.Warn($"GPU widget update failed: {ex.Message}"); if (we.Text != null) we.Text.Text = "GPU --"; }
    }

    private void UpdateDisk(WidgetEntry we)
    {
        try
        {
            if (_diskReadCounter == null || _diskWriteCounter == null) return;
            var read = _diskReadCounter.NextValue();
            var write = _diskWriteCounter.NextValue();
            if (we.Text != null) we.Text.Text = $"💿 {FormatSpeed(read)} {FormatSpeed(write)}";
        }
        catch (Exception ex) { Logger.Warn($"Disk widget update failed: {ex.Message}"); if (we.Text != null) we.Text.Text = "💿 --"; }
    }

    private void UpdateDiskUsage(WidgetEntry we)
    {
        try
        {
            var drive = string.IsNullOrEmpty(we.Config.Args) ? "C" : we.Config.Args;
            var di = new DriveInfo(drive);
            if (!di.IsReady) { if (we.Text != null) we.Text.Text = $"{drive}: --"; return; }
            var free = di.TotalFreeSpace / (1024 * 1024 * 1024);
            var total = di.TotalSize / (1024 * 1024 * 1024);
            if (we.Text != null) we.Text.Text = $"{drive}: {free}/{total}G";
        }
        catch (Exception ex) { Logger.Warn($"DiskUsage widget update failed: {ex.Message}"); if (we.Text != null) we.Text.Text = "💾 --"; }
    }

    private void UpdateUptime(WidgetEntry we)
    {
        var ts = TimeSpan.FromMilliseconds(Environment.TickCount64);
        if (we.Text != null)
            we.Text.Text = ts.TotalDays >= 1 ? $"⬆ {ts.Days}d {ts.Hours}h"
                : ts.TotalHours >= 1 ? $"⬆ {ts.Hours}h {ts.Minutes}m"
                : $"⬆ {ts.Minutes}m";
    }

    private void UpdateWifiSsid(WidgetEntry we)
    {
        try
        {
            var wifi = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211
                    && n.OperationalStatus == OperationalStatus.Up);
            if (we.Text != null)
                we.Text.Text = wifi != null ? $"📶 {wifi.Name}" : "📶 --";
        }
        catch (Exception ex) { Logger.Warn($"WiFi widget update failed: {ex.Message}"); if (we.Text != null) we.Text.Text = "📶 --"; }
    }

    private void UpdateIpAddress(WidgetEntry we)
    {
        try
        {
            var ip = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            if (we.Text != null)
                we.Text.Text = ip != null ? $"🌐 {ip.Address}" : "🌐 --";
        }
        catch (Exception ex) { Logger.Warn($"IP widget update failed: {ex.Message}"); if (we.Text != null) we.Text.Text = "🌐 --"; }
    }

    private string _publicIpText = "";
    private void UpdatePublicIp(WidgetEntry we)
    {
        if (we.Text != null) we.Text.Text = string.IsNullOrEmpty(_publicIpText) ? "🌍 ..." : $"🌍 {_publicIpText}";
        if (string.IsNullOrEmpty(_publicIpText))
        {
            Task.Run(async () =>
            {
                try
                {
                    _publicIpText = await _http.GetStringAsync("https://api.ipify.org");
                }
                catch (Exception ex) { Logger.Warn($"Public IP fetch failed: {ex.Message}"); _publicIpText = "N/A"; }
            });
        }
    }

    private void UpdateVpnStatus(WidgetEntry we)
    {
        try
        {
            var vpn = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.Name.Contains("VPN", StringComparison.OrdinalIgnoreCase)
                    || n.Name.Contains("PANGP", StringComparison.OrdinalIgnoreCase)
                    || n.Name.Contains("tun", StringComparison.OrdinalIgnoreCase));
            if (we.Text != null)
                we.Text.Text = vpn != null && vpn.OperationalStatus == OperationalStatus.Up
                    ? "🔒 VPN" : "";
        }
        catch (Exception ex) { Logger.Warn($"Widget {we.Config.Type} update failed: {ex.Message}"); if (we.Text != null) we.Text.Text = ""; }
    }

    private void UpdateVolume(WidgetEntry we)
    {
        try
        {
            if (!_volInit) { _volumeEndpoint = GetVolumeEndpoint(); _volInit = true; }
            if (_volumeEndpoint == null) { if (we.Text != null) we.Text.Text = ""; return; }
            var ep = (IAudioEndpointVolume)_volumeEndpoint;
            ep.GetMasterVolumeLevelScalar(out float vol);
            ep.GetMute(out bool mute);
            if (we.Text != null)
            {
                var icon = mute ? "🔇" : vol >= 0.66f ? "🔊" : vol >= 0.33f ? "🔉" : "🔈";
                var simple = we.Config.Format == "simple";
                we.Text.Text = mute ? (simple ? "MUTED" : "🔇 MUTED") : (simple ? $"{vol * 100:F0}%" : $"{icon} {vol * 100:F0}%");
            }
        }
        catch (Exception ex) { Logger.Warn($"Volume widget failed: {ex.Message}"); if (we.Text != null) we.Text.Text = ""; }
    }

    private void UpdateAudioDevice(WidgetEntry we)
    {
        try
        {
            if (!_volInit) { _volumeEndpoint = GetVolumeEndpoint(); _volInit = true; }
            var name = GetDefaultAudioDeviceName();
            if (we.Text != null) we.Text.Text = name ?? "";
        }
        catch (Exception ex) { Logger.Warn($"AudioDevice widget failed: {ex.Message}"); if (we.Text != null) we.Text.Text = ""; }
    }

    private void UpdateWorldClock(WidgetEntry we)
    {
        try
        {
            var zones = (we.Config.Args ?? "EST,PST,UTC").Split(',');
            var parts = new List<string>();
            foreach (var z in zones)
            {
                try
                {
                    var tz = TimeZoneInfo.FindSystemTimeZoneById(z.Trim());
                    parts.Add($"{z.Trim()} {TimeZoneInfo.ConvertTime(DateTime.Now, tz):HH:mm}");
                }
                catch (Exception ex) { Logger.Warn($"Widget update failed: {ex.Message}"); }
            }
            if (we.Text != null) we.Text.Text = string.Join(" · ", parts);
        }
        catch (Exception ex) { Logger.Warn($"Widget {we.Config.Type} update failed: {ex.Message}"); if (we.Text != null) we.Text.Text = ""; }
    }

    private void UpdateCountdown(WidgetEntry we)
    {
        try
        {
            if (DateTime.TryParse(we.Config.Args, out var target))
            {
                var remain = target - DateTime.Now;
                if (we.Text != null)
                    we.Text.Text = remain.TotalSeconds > 0
                        ? $"{(int)remain.TotalDays}d {(int)remain.Hours}h"
                        : "🎉 Done!";
            }
        }
        catch (Exception ex) { Logger.Warn($"WidgetManager operation failed: {ex.Message}"); }
    }

    private void UpdateWindowCount(WidgetEntry we)
    {
        if (ServiceLocator.TryResolve<WorkspaceManager>(out var wsm))
        {
            var ws = wsm.GetActiveWorkspace();
            if (we.Text != null)
                we.Text.Text = ws != null ? $"{ws.Windows.Count} windows" : "";
        }
    }

    private bool _btInit; private bool _btOn;
    private void UpdateBluetooth(WidgetEntry we)
    {
        try
        {
            if (!_btInit) { _btOn = CheckBluetooth(); _btInit = true; }
            if (we.Text != null) we.Text.Text = _btOn ? "🔵 BT On" : "BT Off";
        }
        catch (Exception ex) { Logger.Warn($"Bluetooth widget failed: {ex.Message}"); if (we.Text != null) we.Text.Text = ""; }
    }

    private void UpdateMicrophone(WidgetEntry we)
    {
        try
        {
            var muted = IsMicrophoneMuted();
            if (we.Text != null) we.Text.Text = muted ? "🎤 Muted" : "🎤 Live";
        }
        catch (Exception ex) { Logger.Warn($"Microphone widget failed: {ex.Message}"); if (we.Text != null) we.Text.Text = ""; }
    }

    private void UpdateCamera(WidgetEntry we)
    {
        try
        {
            var active = IsCameraActive();
            if (we.Text != null) we.Text.Text = active ? "📷 Active" : "📷 Off";
        }
        catch (Exception ex) { Logger.Warn($"Camera widget failed: {ex.Message}"); if (we.Text != null) we.Text.Text = ""; }
    }

    private void UpdateScript(WidgetEntry we)
    {
        try
        {
            var script = we.Config.Args ?? "";
            if (string.IsNullOrEmpty(script)) return;
            var interval = Math.Max(we.Config.FontSize > 0 ? we.Config.FontSize : 5, 1);
            if (script != we.ScriptPath || (DateTime.UtcNow - we.ScriptLastRun).TotalSeconds > interval)
            {
                we.ScriptPath = script;
                we.ScriptLastRun = DateTime.UtcNow;
                var captured = we;
                Task.Run(() =>
                {
                    try
                    {
                        using var p = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = "cmd.exe",
                                Arguments = "/c \"" + script + "\"",
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                CreateNoWindow = true,
                            }
                        };
                        p.Start();
                        captured.ScriptOutput = p.StandardOutput.ReadToEnd().Trim();
                        p.WaitForExit(5000);
                        if (!p.HasExited) p.Kill();
                    }
                    catch (Exception ex) { Logger.Warn($"Script failed: {ex.Message}"); captured.ScriptOutput = ""; }
                });
            }
            if (we.Text != null) we.Text.Text = we.ScriptOutput;
        }
        catch (Exception ex) { Logger.Warn($"Script widget failed: {ex.Message}"); }
    }

    private void UpdateLabel(WidgetEntry we)
    {
        if (we.Text != null) we.Text.Text = we.Config.Args;
    }

    private void UpdateButton(WidgetEntry we)
    {
        if (we.Text != null) we.Text.Text = we.Config.Args;
    }

    private void UpdateWorkspace(WidgetEntry we)
    {
        if (we.Pill?.Child is not StackPanel sp) return;
        if (!ServiceLocator.TryResolve<WorkspaceManager>(out var wsm) ||
            !ServiceLocator.TryResolve<ConfigRoot>(out var cfg))
            return;

        var wsCount = wsm.Workspaces.Count;
        var existingPills = sp.Children.OfType<Border>().ToList();
        var needRebuild = existingPills.Count != wsCount;

        if (needRebuild)
        {
            sp.Children.Clear();
            foreach (var ws in wsm.Workspaces)
                AddWorkspacePill(sp, wsm, ws, cfg);
            return;
        }

        for (int i = 0; i < wsCount; i++)
        {
            var ws = wsm.Workspaces[i];
            var isActive = ws.Id == wsm.ActiveWorkspaceId;
            var hasWindows = ws.Windows.Count > 0;
            Color c;
            if (isActive)
            {
                try { c = (Color)ColorConverter.ConvertFromString(cfg.Theme.Accent); }
                catch { c = Color.FromRgb(0x7a, 0xa2, 0xf7); }
            }
            else if (hasWindows)
            {
                try { c = (Color)ColorConverter.ConvertFromString(cfg.Theme.WorkspacePillInactive); }
                catch { c = Color.FromRgb(0x56, 0x5f, 0x89); }
            }
            else
            {
                try { c = (Color)ColorConverter.ConvertFromString(cfg.Theme.WorkspacePillEmpty); }
                catch { c = Color.FromRgb(0x2b, 0x2f, 0x44); }
            }

            var pill = existingPills[i];
            var stateClass = isActive ? "active" : (hasWindows ? "inactive" : "empty");
            pill.Tag = $"workspace-pill {stateClass}";
            AnimatePillWidth(pill, isActive ? 28 : 10);
            AnimatePillColor(pill, c);
        }
    }

    private void AddWorkspacePill(StackPanel sp, WorkspaceManager wsm, Workspace ws, ConfigRoot cfg)
    {
        var isActive = ws.Id == wsm.ActiveWorkspaceId;
        var hasWindows = ws.Windows.Count > 0;
        Color c;
        if (isActive)
        {
            try { c = (Color)ColorConverter.ConvertFromString(cfg.Theme.Accent); }
            catch { c = Color.FromRgb(0x7a, 0xa2, 0xf7); }
        }
        else if (hasWindows)
        {
            try { c = (Color)ColorConverter.ConvertFromString(cfg.Theme.WorkspacePillInactive); }
            catch { c = Color.FromRgb(0x56, 0x5f, 0x89); }
        }
        else
        {
            try { c = (Color)ColorConverter.ConvertFromString(cfg.Theme.WorkspacePillEmpty); }
            catch { c = Color.FromRgb(0x2b, 0x2f, 0x44); }
        }
        var wsId = ws.Id;
        var stateClass = isActive ? "active" : (hasWindows ? "inactive" : "empty");
        var pill = new Border
        {
            Width = isActive ? 28 : 10, Height = 10,
            CornerRadius = new CornerRadius(5),
            Margin = new Thickness(3, 0, 3, 0),
            Background = new SolidColorBrush(c),
            Cursor = System.Windows.Input.Cursors.Hand,
            Tag = $"workspace-pill {stateClass}"
        };
        pill.MouseLeftButtonDown += (_, _) => wsm.SwitchToWorkspace(wsId);
        if (Styling.StyleEngine.HasStyles)
            Styling.StyleEngine.ApplyToElement(pill);
        sp.Children.Add(pill);
    }

    private static void AnimatePillWidth(Border pill, double targetWidth)
    {
        if (Math.Abs(pill.Width - targetWidth) < 0.5) return;
        var anim = new System.Windows.Media.Animation.DoubleAnimation(
            pill.Width, targetWidth,
            TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
            }
        };
        pill.BeginAnimation(Border.WidthProperty, anim);
    }

    private static void AnimatePillColor(Border pill, Color toColor)
    {
        if (pill.Background is SolidColorBrush current)
        {
            if (current.Color.R == toColor.R && current.Color.G == toColor.G
                && current.Color.B == toColor.B && current.Color.A == toColor.A)
                return;
        }

        var toBrush = new SolidColorBrush(toColor);
        var anim = new System.Windows.Media.Animation.ColorAnimation(
            toColor, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
            }
        };
        pill.Background = toBrush;
        pill.Background.BeginAnimation(SolidColorBrush.ColorProperty, anim);
    }

    private void UpdateLayout(WidgetEntry we)
    {
        if (ServiceLocator.TryResolve<LayoutManager>(out var lm))
        {
            if (we.Text != null) we.Text.Text = lm.CurrentMasterFactor.ToString("F0") + "%";
            we.Pill!.Cursor = System.Windows.Input.Cursors.Hand;
            we.Pill.MouseLeftButtonDown += (_, _) => lm.CycleLayout();
        }
    }

    private void UpdateActiveWindow(WidgetEntry we)
    {
        if (ServiceLocator.TryResolve<FocusManager>(out var fm) && fm.ActiveWindow != null)
        {
            var title = fm.ActiveWindow.Title;
            if (we.Text != null) we.Text.Text = title.Length > 40 ? title[..40] : title;
        }
        else { if (we.Text != null) we.Text.Text = ""; }
    }

    private void UpdateWindowTabs(WidgetEntry we)
    {
        if (we.Panel == null) return;
        if (!ServiceLocator.TryResolve<WindowManager>(out var wm)) return;
        if (!ServiceLocator.TryResolve<WorkspaceManager>(out var wsm)) return;
        ServiceLocator.TryResolve<FocusManager>(out var fm);
        ServiceLocator.TryResolve<ConfigRoot>(out var cfg);

        var activeWs = wsm.GetActiveWorkspace();
        if (activeWs == null) return;
        var windows = activeWs.Windows.ToList();
        foreach (var ow in wsm.Workspaces.Where(w => w.Id != activeWs.Id))
            windows.AddRange(ow.Windows.Where(w => w.IsSticky).Except(windows));

        var existing = new Dictionary<IntPtr, Border>(we.TabPills);
        foreach (var mw in windows)
        {
            if (!IsWindow(mw.Hwnd)) continue;
            if (existing.TryGetValue(mw.Hwnd, out var oldPill)) { existing.Remove(mw.Hwnd); continue; }

            var isActiveTab = fm?.ActiveWindow == mw;
            var stateClass = isActiveTab ? "active" : "inactive";
            var h = we.Config.Height > 0 ? we.Config.Height : 28;
            var hwnd = mw.Hwnd; var captured = mw;
            var title = captured.Title.Length > 25 ? captured.Title[..25] : captured.Title;
            var stack = new StackPanel { Orientation = Orientation.Horizontal };

            var btn = new Button { Content = title, Height = h, FontSize = 15,
                Padding = new Thickness(10, 0, 2, 0), Background = Brushes.Transparent,
                BorderThickness = new Thickness(0), ToolTip = captured.Title,
                Cursor = System.Windows.Input.Cursors.Hand,
                Foreground = new SolidColorBrush(isActiveTab
                    ? ParseColor(cfg?.Theme.Accent) ?? Colors.Cyan
                    : ParseColor(cfg?.Theme.Foreground) ?? Colors.White) };
            btn.Click += (_, _) => { ShowWindow(hwnd, SW_RESTORE); SetForegroundWindow(hwnd); fm?.SetActiveWindow(captured); };
            btn.MouseDown += (_, e) => { if (e.MiddleButton == System.Windows.Input.MouseButtonState.Pressed) PostMessage(hwnd, WM_CLOSE, 0, 0); };
            stack.Children.Add(btn);

            var cb = new Button { Content = "✕", Width = 20, Height = h, FontSize = 12,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
                Cursor = System.Windows.Input.Cursors.Hand, Tag = "window-tab-close",
                Foreground = new SolidColorBrush(ParseColor(cfg?.Theme.Muted) ?? Colors.Gray) };
            cb.Click += (_, _) => PostMessage(hwnd, WM_CLOSE, 0, 0);
            stack.Children.Add(cb);

            var pill = new Border { CornerRadius = new CornerRadius(h / 2), Height = h,
                Background = new SolidColorBrush(ParseColor(cfg?.Theme.TaskButtonBackground) ?? Color.FromRgb(0x24, 0x28, 0x3e)),
                Child = stack, Margin = new Thickness(2, 0, 2, 0),
                Tag = $"window-tab {stateClass} type:window_tab" };
            we.TabPills[mw.Hwnd] = pill;
            if (Styling.StyleEngine.HasStyles)
            {
                Styling.StyleEngine.ApplyToElement(pill);
                Styling.StyleEngine.ApplyToElement(cb);
            }
            we.Panel.Children.Add(pill);
        }
        foreach (var kv in existing) { we.Panel.Children.Remove(kv.Value); we.TabPills.Remove(kv.Key); }
    }

    private void UpdateLauncher(WidgetEntry we)
    {
        if (we.Panel == null || we.Panel.Children.Count > 0) return;
        if (!ServiceLocator.TryResolve<ConfigRoot>(out var cfg)) return;
        foreach (var entry in cfg.Launcher)
        {
            var name = string.IsNullOrEmpty(entry.Name) ? entry.Path : entry.Name;
            var h = we.Config.Height > 0 ? we.Config.Height : 28;
            var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            try
            {
                var icon = System.Drawing.Icon.ExtractAssociatedIcon(entry.Path);
                if (icon != null)
                {
                    var img = new System.Windows.Controls.Image { Source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon.Handle, System.Windows.Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromWidthAndHeight(16, 16)), Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0) };
                    content.Children.Add(img);
                }
            }
            catch (Exception ex) { Logger.Warn($"Widget update failed: {ex.Message}"); }
            content.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            var btn = new Button { Content = content, Height = h, FontSize = 15, Padding = new Thickness(12, 0, 12, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand, Foreground = new SolidColorBrush(ParseColor(cfg.Theme.Foreground) ?? Colors.White) };
            var cmd = entry.Path;
            btn.Click += (_, _) => { try { Process.Start(new ProcessStartInfo { FileName = cmd, UseShellExecute = true }); } catch (Exception ex) { Logger.Warn($"Launch failed: {cmd}: {ex.Message}"); } };
            var pill = new Border { CornerRadius = new CornerRadius(h / 2), Height = h, Background = new SolidColorBrush(ParseColor(cfg.Theme.TaskButtonBackground) ?? Color.FromRgb(0x24, 0x28, 0x3e)), Child = btn, Margin = new Thickness(2, 0, 2, 0) };
            we.Panel.Children.Add(pill);
        }
    }

    private void UpdateTaskbar(WidgetEntry we)
    {
        if (we.Panel == null) return;
        if (!ServiceLocator.TryResolve<WindowManager>(out var wm)) return;
        if (!ServiceLocator.TryResolve<WorkspaceManager>(out var wsm)) return;
        ServiceLocator.TryResolve<FocusManager>(out var fm);
        ServiceLocator.TryResolve<ConfigRoot>(out var cfg);

        var mode = (we.Config.Args ?? "").Trim().ToLowerInvariant();
        var showHidden = mode == "hidden";
        var showAll = mode == "all";

        var hwnds = new HashSet<IntPtr>();
        foreach (var mw in wm.ManagedWindows.Values)
        {
            if (!IsWindow(mw.Hwnd)) continue;
            if (mw.SwallowedByHwnd != IntPtr.Zero) continue;

            if (showHidden)
            {
                if (!IsWindowVisible(mw.Hwnd))
                    hwnds.Add(mw.Hwnd);
            }
            else if (showAll)
            {
                if (IsWindowVisible(mw.Hwnd) || mw.IsSticky)
                    hwnds.Add(mw.Hwnd);
            }
            else
            {
                var activeWs = wsm.GetActiveWorkspace();
                if (activeWs == null) continue;
                if (activeWs.Windows.Contains(mw) && IsWindowVisible(mw.Hwnd))
                    hwnds.Add(mw.Hwnd);
                else if (mw.IsSticky && IsWindowVisible(mw.Hwnd))
                    hwnds.Add(mw.Hwnd);
            }
        }

        var existing = new Dictionary<IntPtr, Border>(we.TabPills);
        var activeHwnd = fm?.ActiveWindow?.Hwnd ?? IntPtr.Zero;

        foreach (var hwnd in hwnds)
        {
            if (existing.TryGetValue(hwnd, out var oldPill))
            {
                existing.Remove(hwnd);
                var pillActive = hwnd == activeHwnd && !showHidden;
                var pillBorder = pillActive
                    ? (ParseColor(cfg?.Theme.Accent) ?? Color.FromRgb(0x7a, 0xa2, 0xf7))
                    : (ParseColor(cfg?.Theme.Muted) ?? Color.FromRgb(0x56, 0x5f, 0x89));
                oldPill.BorderThickness = new Thickness(pillActive ? 2 : 1);
                oldPill.BorderBrush = new SolidColorBrush(pillBorder);
                oldPill.Tag = pillActive ? "taskbar-icon active" : "taskbar-icon";
                if (oldPill.Child is Button oldBtn)
                    oldBtn.Foreground = new SolidColorBrush(pillActive
                        ? ParseColor(cfg?.Theme.Accent) ?? Color.FromRgb(0x7a, 0xa2, 0xf7)
                        : ParseColor(cfg?.Theme.Foreground) ?? Color.FromRgb(0xc0, 0xca, 0xf5));
                continue;
            }

            var mw = wm.GetManagedWindow(hwnd);
            if (mw == null) continue;

            var iconSize = we.Config.Height > 0 ? we.Config.Height : 28;
            var isActive = mw.Hwnd == activeHwnd && !showHidden;
            var captured = mw;

            var stack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var hasIcon = false;

            try
            {
                var exePath = Win32.WindowHelper.GetProcessPath(mw.Hwnd);
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                    if (icon != null)
                    {
                        var img = new System.Windows.Controls.Image
                        {
                            Source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                                icon.Handle, System.Windows.Int32Rect.Empty,
                                System.Windows.Media.Imaging.BitmapSizeOptions.FromWidthAndHeight(iconSize - 6, iconSize - 6)),
                            Width = iconSize - 6,
                            Height = iconSize - 6
                        };
                        stack.Children.Add(img);
                        hasIcon = true;
                    }
                }
            }
            catch { }

            if (!hasIcon)
                stack.Children.Add(new TextBlock
                {
                    Text = captured.Title.Length > 0 ? captured.Title[..1].ToUpper() : "?",
                    FontSize = iconSize * 0.45,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(isActive
                        ? ParseColor(cfg?.Theme.Accent) ?? Color.FromRgb(0x7a, 0xa2, 0xf7)
                        : ParseColor(cfg?.Theme.Foreground) ?? Color.FromRgb(0xc0, 0xca, 0xf5))
                });

            var btn = new Button
            {
                Content = stack,
                MinWidth = iconSize + 4,
                Height = iconSize,
                Padding = new Thickness(2),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = showHidden ? $"[Hidden] {mw.Title} ({mw.ProcessName})" : $"{mw.Title} ({mw.ProcessName})",
                Tag = "taskbar-icon"
            };

            btn.Click += (_, _) =>
            {
                ShowWindow(captured.Hwnd, SW_RESTORE);
                SetForegroundWindow(captured.Hwnd);
                fm?.SetActiveWindow(captured);
            };
            btn.MouseRightButtonDown += (_, e) =>
            {
                e.Handled = true;
                var rect = Win32.WindowHelper.GetWindowRectSafe(captured.Hwnd);
                int cx = rect.Left + rect.Width / 2;
                int cy = rect.Top + rect.Height / 2;
                PostMessage(captured.Hwnd, 0x007B, (IntPtr)(-1), (IntPtr)((cy << 16) | (cx & 0xFFFF)));
            };
            btn.MouseDown += (_, e) =>
            {
                if (e.MiddleButton == System.Windows.Input.MouseButtonState.Pressed)
                    PostMessage(captured.Hwnd, WM_CLOSE, 0, 0);
            };

            var borderThickness = isActive ? 2 : 1;
            var pillBg = ParseColor(cfg?.Theme.TaskButtonBackground) ?? Color.FromRgb(0x1a, 0x1a, 0x3e);
            var borderColor = isActive
                ? (ParseColor(cfg?.Theme.Accent) ?? Color.FromRgb(0x7a, 0xa2, 0xf7))
                : (ParseColor(cfg?.Theme.Muted) ?? Color.FromRgb(0x56, 0x5f, 0x89));

            var pill = new Border
            {
                MinWidth = iconSize + 8,
                Height = iconSize + 4,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(pillBg),
                BorderBrush = new SolidColorBrush(borderColor),
                BorderThickness = new Thickness(borderThickness),
                Child = btn,
                Margin = new Thickness(1, 0, 1, 0),
                Tag = isActive ? "taskbar-icon active" : "taskbar-icon"
            };
            ApplyHover(pill);
            we.TabPills[hwnd] = pill;
            we.Panel.Children.Add(pill);
        }

        foreach (var kv in existing)
        {
            we.Panel.Children.Remove(kv.Value);
            we.TabPills.Remove(kv.Key);
        }
    }

    private void UpdateSystray(WidgetEntry we)
    {
        if (we.Panel == null) return;
        if (we.DockInit) return;
        we.DockInit = true;

        var args = (we.Config.Args ?? "").Trim();
        if (string.IsNullOrEmpty(args)) return;

        var paths = args.Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

        if (paths.Count == 0) return;

        ServiceLocator.TryResolve<ConfigRoot>(out var cfg);
        var iconSize = we.Config.Height > 0 ? we.Config.Height : 28;
        var bg = ParseColor(cfg?.Theme.TaskButtonBackground) ?? Color.FromRgb(0x1a, 0x1a, 0x3e);
        var fg = ParseColor(cfg?.Theme.Foreground) ?? Color.FromRgb(0xc0, 0xca, 0xf5);

        foreach (var cmd in paths)
        {
            var name = Path.GetFileNameWithoutExtension(cmd);
            if (string.IsNullOrEmpty(name)) name = cmd;

            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            var exePath = cmd;
            if (!File.Exists(exePath))
            {
                try
                {
                    var found = FindExeInPath(exePath);
                    if (found != null) exePath = found;
                }
                catch { }
            }

            if (File.Exists(exePath))
            {
                try
                {
                    using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                    if (icon != null)
                    {
                        content.Children.Add(new System.Windows.Controls.Image
                        {
                            Source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                                icon.Handle, System.Windows.Int32Rect.Empty,
                                System.Windows.Media.Imaging.BitmapSizeOptions.FromWidthAndHeight(iconSize - 6, iconSize - 6)),
                            Width = iconSize - 6,
                            Height = iconSize - 6
                        });
                    }
                }
                catch { }
            }

            if (content.Children.Count == 0)
                content.Children.Add(new TextBlock
                {
                    Text = name.Length > 0 ? name[..1].ToUpper() : "?",
                    FontSize = iconSize * 0.4,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(fg)
                });

            var btn = new Button
            {
                Content = content,
                MinWidth = iconSize + 8,
                Height = iconSize + 2,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = cmd,
                Tag = "dock-icon"
            };
            btn.Click += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo { FileName = cmd, UseShellExecute = true }); }
                catch { }
            };

            var pill = new Border
            {
                MinWidth = iconSize + 10,
                Height = iconSize + 4,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(bg),
                Child = btn,
                Margin = new Thickness(1, 0, 1, 0)
            };
            ApplyHover(pill);
            we.Panel.Children.Add(pill);
        }
    }

    private static void ApplyHover(Border pill)
    {
        if (pill.Background is not SolidColorBrush bg) return;
        var c = bg.Color;
        var hoverColor = Color.FromArgb(c.A,
            (byte)Math.Min(255, c.R + 40),
            (byte)Math.Min(255, c.G + 40),
            (byte)Math.Min(255, c.B + 40));
        pill.MouseEnter += (_, _) => pill.Background = new SolidColorBrush(hoverColor);
        pill.MouseLeave += (_, _) => pill.Background = bg;
    }

    private static string? FindExeInPath(string name)
    {
        if (name.Contains('\\') || name.Contains('/')) return null;
        var search = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe";
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(';'))
        {
            try
            {
                var full = Path.Combine(dir.Trim(), search);
                if (File.Exists(full)) return full;
            }
            catch { }
        }
        return null;
    }

    private void UpdatePowerPlan(WidgetEntry we)
    {
        try
        {
            using var active = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powercfg", Arguments = "/GETACTIVESCHEME",
                UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true
            });
            if (active == null) return;
            var output = active.StandardOutput.ReadToEnd().Trim();
            active.WaitForExit(2000);
            if (!active.HasExited) { active.Kill(); }
            else
            {
                var name = output;
                var idx = output.LastIndexOf('(');
                if (idx > 0) name = output[(output.IndexOf(':') + 1)..idx].Trim();
                if (we.Text != null) we.Text.Text = $"⚡ {name}";
            }
        }
        catch { if (we.Text != null) we.Text.Text = "⚡ --"; }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    private void UpdateLanguage(WidgetEntry we)
    {
        try
        {
            var tid = GetWindowThreadProcessId(GetForegroundWindow(), out uint tidOut);
            if (tid == 0) tid = GetCurrentThreadId();
            var layout = GetKeyboardLayout(tid);
            if (layout == IntPtr.Zero) layout = GetKeyboardLayout(0);
            var langId = (int)((long)layout & 0xFFFF);
            if (langId == 0 && layout == IntPtr.Zero)
            {
                var ci = System.Globalization.CultureInfo.CurrentUICulture;
                if (we.Text != null) we.Text.Text = $"🔤 {ci.TwoLetterISOLanguageName.ToUpper()}";
                return;
            }
            var name = langId switch
            {
                0x0409 => "EN",
                0x0809 => "EN-UK",
                0x0c09 => "EN-AU",
                0x1009 => "EN-CA",
                0x0804 => "中文",
                0x0404 => "中文-TW",
                0x0411 => "JP",
                0x0412 => "KO",
                0x040c => "FR",
                0x0407 => "DE",
                0x0410 => "IT",
                0x0c0a => "ES",
                0x0416 => "PT",
                0x0419 => "RU",
                0x0405 => "CZ",
                0x040e => "HU",
                _ => $"{langId:X4}"
            };
            if (we.Text != null) we.Text.Text = $"🔤 {name}";
        }
        catch { if (we.Text != null) we.Text.Text = "🔤 --"; }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }

    private string _recycleText = "";
    private void UpdateRecycleBin(WidgetEntry we)
    {
        try
        {
            if (string.IsNullOrEmpty(_recycleText))
            {
                Task.Run(() =>
                {
                    try
                    {
                        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
                        SHQueryRecycleBin("C:\\", ref info);
                        if (info.i64NumItems > 0)
                            _recycleText = $"🗑 {info.i64NumItems}";
                        else
                            _recycleText = "🗑 Empty";
                    }
                    catch { _recycleText = "🗑 --"; }
                });
            }
            if (we.Text != null) we.Text.Text = _recycleText;
        }
        catch { if (we.Text != null) we.Text.Text = "🗑 --"; }
    }

    [DllImport("dxva2.dll")]
    private static extern bool GetMonitorBrightness(IntPtr hMonitor, out uint min, out uint cur, out uint max);

    private void UpdateBrightness(WidgetEntry we)
    {
        try
        {
            var hMon = MonitorFromWindow(GetDesktopWindow(), 1);
            if (GetMonitorBrightness(hMon, out _, out uint cur, out uint max))
            {
                var pct = max > 0 ? (int)(cur * 100 / max) : 0;
                if (we.Text != null) we.Text.Text = $"☀ {pct}%";
            }
            else if (we.Text != null) we.Text.Text = "☀ --";
        }
        catch { if (we.Text != null) we.Text.Text = "☀ --"; }
    }

    private string _serverStatus = "";
    private void UpdateServerMonitor(WidgetEntry we)
    {
        var url = we.Config.Args ?? "";
        if (string.IsNullOrEmpty(url)) { if (we.Text != null) we.Text.Text = "🌐 --"; return; }
        try
        {
            if (string.IsNullOrEmpty(_serverStatus))
            {
                Task.Run(() =>
                {
                    try
                    {
                        var resp = _http.GetAsync(url).Result;
                        _serverStatus = resp.IsSuccessStatusCode ? "🟢 Online" : "🔴 Offline";
                    }
                    catch { _serverStatus = "🔴 Offline"; }
                });
            }
            if (we.Text != null) we.Text.Text = _serverStatus;
        }
        catch { if (we.Text != null) we.Text.Text = "🌐 --"; }
    }

    private void UpdateNotifications(WidgetEntry we)
    {
        if (we.Text != null) we.Text.Text = "🔔";
        if (we.Pill != null && we.Pill.Tag?.ToString() != "notif-ready")
        {
            we.Pill.Tag = "notif-ready";
            we.Pill.Cursor = System.Windows.Input.Cursors.Hand;
            we.Pill.MouseLeftButtonDown += (_, _) =>
            {
                try { System.Diagnostics.Process.Start("explorer", "shell:::{05d7b0f4-2121-4eff-bf6b-ed3f69b894d9}"); }
                catch { }
            };
            we.Pill.ToolTip = "Click to open Action Center";
        }
    }

    private void UpdateWindowControls(WidgetEntry we)
    {
        if (we.Panel == null || we.Panel.Children.Count > 0) return;
        var h = we.Config.Height > 0 ? we.Config.Height : 22;
        var fgColor = Colors.White;
        if (ServiceLocator.TryResolve<ConfigRoot>(out var cfg))
            fgColor = ParseColor(cfg.Theme.Foreground) ?? Colors.White;

        void AddBtn(string text, string label, Action action)
        {
            var btn = new Button
            {
                Content = text, Width = h + 4, Height = h, FontSize = h * 0.55,
                Padding = new Thickness(0), Background = Brushes.Transparent,
                BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand,
                Foreground = new SolidColorBrush(fgColor), ToolTip = label
            };
            btn.Click += (_, _) => { try { action(); } catch { } };
            we.Panel!.Children.Add(btn);
        }
        AddBtn("─", "Minimize", () =>
        {
            var fg = GetForegroundWindow();
            if (fg != IntPtr.Zero) ShowWindow(fg, 6);
        });
        AddBtn("□", "Maximize", () =>
        {
            var fg = GetForegroundWindow();
            if (fg != IntPtr.Zero) ShowWindow(fg, 3);
        });
        AddBtn("✕", "Close", () =>
        {
            var fg = GetForegroundWindow();
            if (fg != IntPtr.Zero) PostMessage(fg, 0x0010, 0, 0);
        });
    }

    private void UpdatePowerMenu(WidgetEntry we)
    {
        if (we.Text == null) return;
        if (we.Pill == null) return;
        we.Pill.Cursor = System.Windows.Input.Cursors.Hand;

        if (!string.IsNullOrEmpty(we.PowerPendingAction) && (DateTime.UtcNow - we.PowerPendingTime).TotalSeconds > 4)
        {
            we.PowerPendingAction = "";
        }

        if (string.IsNullOrEmpty(we.PowerPendingAction))
            we.Text.Text = "⏻";
        else
            we.Text.Text = "Sure?";

        if (we.Pill.Tag?.ToString() == "power-menu-ready") return;
        we.Pill.Tag = "power-menu-ready";
        we.Pill.MouseLeftButtonDown += (_, _) =>
        {
            if (string.IsNullOrEmpty(we.PowerPendingAction))
            {
                we.PowerPendingAction = "shutdown";
                we.PowerPendingTime = DateTime.UtcNow;
            }
            else if (we.PowerPendingAction == "shutdown")
            {
                we.PowerPendingAction = "";
                try { System.Diagnostics.Process.Start("shutdown", "/s /t 10"); }
                catch { }
            }
            else { we.PowerPendingAction = ""; }
        };
        we.Pill.MouseRightButtonDown += (_, e) =>
        {
            e.Handled = true;
            if (string.IsNullOrEmpty(we.PowerPendingAction))
            {
                we.PowerPendingAction = "restart";
                we.PowerPendingTime = DateTime.UtcNow;
            }
            else if (we.PowerPendingAction == "restart")
            {
                we.PowerPendingAction = "";
                try { System.Diagnostics.Process.Start("shutdown", "/r /t 10"); }
                catch { }
            }
            else { we.PowerPendingAction = ""; }
        };
        we.Pill.ToolTip = "Left=Shutdown | Right=Restart | Double-click to confirm";
    }

    private bool _pomodoroRunning; private int _pomodoroSecs;
    private int _pomodoroTick; private int _pomodoroWorkSecs = 25 * 60;

    private void UpdatePomodoro(WidgetEntry we)
    {
        var args = (we.Config.Args ?? "25,5").Split(',');
        if (args.Length >= 1 && int.TryParse(args[0].Trim(), out var wm))
            _pomodoroWorkSecs = wm * 60;

        _pomodoroTick++;
        if (_pomodoroTick % 2 != 0) return;
        if (_pomodoroRunning) _pomodoroSecs++;

        var remain = _pomodoroWorkSecs - _pomodoroSecs;
        if (remain <= 0 && _pomodoroRunning)
        {
            _pomodoroRunning = false;
            _pomodoroSecs = 0;
        }
        var mins = remain / 60;
        var secs = remain % 60;
        var icon = _pomodoroRunning ? "🍅" : "⏸";
        if (we.Text != null) we.Text.Text = $"{icon} {mins:D2}:{secs:D2}";
        if (we.Pill != null)
        {
            we.Pill.Cursor = System.Windows.Input.Cursors.Hand;
            if (we.Pill.Tag?.ToString() != "pomodoro-ready")
            {
                we.Pill.Tag = "pomodoro-ready";
                we.Pill.MouseLeftButtonDown += (_, _) =>
                {
                    _pomodoroRunning = !_pomodoroRunning;
                    if (!_pomodoroRunning) _pomodoroSecs = 0;
                };
            }
        }
    }

    private bool _settingCavaVolume;

    private void UpdateCava(WidgetEntry we)
    {
        if (we.Panel == null || we.Text == null || we.Pill == null) return;

        try
        {
            if (!_volInit) { _volumeEndpoint = GetVolumeEndpoint(); _volInit = true; }
            if (_volumeEndpoint == null) { we.Text.Text = "🔇 --"; return; }
            var ep = (IAudioEndpointVolume)_volumeEndpoint;
            ep.GetMasterVolumeLevelScalar(out float vol);
            ep.GetMute(out bool mute);

            var pct = (int)(vol * 100);
            if (mute) pct = 0;
            var icon = mute ? "🔇" : pct >= 66 ? "🔊" : pct >= 33 ? "🔉" : pct > 0 ? "🔈" : "🔇";
            we.Text.Text = $"{icon} {pct}%";

            if (we.CavaExpanded && we.VolumeSlider != null)
            {
                _settingCavaVolume = true;
                if (Math.Abs(we.VolumeSlider.Value - pct) > 0.5)
                    we.VolumeSlider.Value = pct;
                _settingCavaVolume = false;
            }
        }
        catch { if (we.Text != null) we.Text.Text = "🔇 --"; }

        if (we.Pill.Tag?.ToString() != "cava-ready")
        {
            we.Pill.Tag = "cava-ready";
            we.Pill.MouseLeftButtonDown += (_, _) =>
            {
                we.CavaExpanded = !we.CavaExpanded;
                if (we.VolumeSlider == null) return;

                var sliderW = we.CavaExpanded ? (we.Config.Width > 0 ? (double)we.Config.Width - 58 : 100) : 0;

                if (we.CavaExpanded && _volumeEndpoint != null)
                {
                    try
                    {
                        var ep = (IAudioEndpointVolume)_volumeEndpoint;
                        ep.GetMasterVolumeLevelScalar(out float vol);
                        _settingCavaVolume = true;
                        we.VolumeSlider.Value = (int)(vol * 100);
                        _settingCavaVolume = false;
                    }
                    catch { }
                }

                var anim = new System.Windows.Media.Animation.DoubleAnimation(
                    we.VolumeSlider.Width, sliderW,
                    TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new System.Windows.Media.Animation.CubicEase
                    {
                        EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                    }
                };
                we.VolumeSlider.BeginAnimation(FrameworkElement.WidthProperty, anim);
            };
        }
    }

    private void UpdateHome(WidgetEntry we)
    {
        if (we.Panel == null || we.Text == null) return;
        we.Text.Text = we.HomeExpanded ? "✕" : "☰";

        var args = (we.Config.Args ?? "").Trim();
        var hasArgs = !string.IsNullOrEmpty(args);

        if (hasArgs && we.HomeButtons.Count == 0)
        {
            var paths = args.Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim()).Where(p => p.Length > 0).ToList();

            var h = we.Config.Height > 0 ? we.Config.Height : 22;
            var fg = Colors.White;
            if (ServiceLocator.TryResolve<ConfigRoot>(out var cfg))
                fg = ParseColor(cfg.Theme.Foreground) ?? Colors.White;

            foreach (var cmd in paths)
            {
                var name = Path.GetFileNameWithoutExtension(cmd);
                if (string.IsNullOrEmpty(name)) name = cmd;
                var captured = cmd;
                var btn = new Button
                {
                    Content = name, Height = h, FontSize = h * 0.45,
                    Padding = new Thickness(6, 0, 6, 0), Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand,
                    Foreground = new SolidColorBrush(fg), Visibility = Visibility.Collapsed
                };
                btn.Click += (_, _) =>
                {
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    { FileName = captured, UseShellExecute = true }); }
                    catch { }
                };
                we.HomeButtons.Add(btn);
                we.Panel!.Children.Add(btn);
            }
        }

        if (hasArgs)
        {
            var vis = we.HomeExpanded ? Visibility.Visible : Visibility.Collapsed;
            foreach (var b in we.HomeButtons) b.Visibility = vis;
        }

        if (we.Pill != null)
        {
            we.Pill.Cursor = System.Windows.Input.Cursors.Hand;
            if (we.Pill.Tag?.ToString() != "home-ready")
            {
                we.Pill.Tag = "home-ready";
                we.Pill.MouseLeftButtonDown += (_, _) =>
                {
                    we.HomeExpanded = !we.HomeExpanded;
                    if (hasArgs)
                    {
                        var vis = we.HomeExpanded ? Visibility.Visible : Visibility.Collapsed;
                        foreach (var b in we.HomeButtons) b.Visibility = vis;
                    }
                    we.Text!.Text = we.HomeExpanded ? "✕" : "☰";
                };
            }
        }
    }

    private static string ProgressBar(int pct)
    {
        int filled = Math.Clamp(pct * 8 / 100, 0, 8);
        return new string('▰', filled) + new string('▱', 8 - filled);
    }

    private static string FormatSpeed(float bps)
    {
        if (bps < 1024) return $"{bps,4:F0}B";
        if (bps < 1024 * 1024) return $"{bps / 1024,4:F1}K";
        return $"{bps / (1024 * 1024),4:F1}M";
    }

    private void InitPerformanceCounters()
    {
        try
        {
            foreach (var c in _allCounters) { try { c.Dispose(); } catch { } }
            _allCounters.Clear();

            _cpuCounter = AddCounter(new PerformanceCounter("Processor", "% Processor Time", "_Total"));
            _memCounter = AddCounter(new PerformanceCounter("Memory", "% Committed Bytes In Use"));
            _cpuCounter.NextValue(); _memCounter.NextValue();

            var netCat = new PerformanceCounterCategory("Network Interface");
            var iface = netCat.GetInstanceNames().FirstOrDefault(i =>
                i.Contains("Ethernet") || i.Contains("Wi-Fi") || i.Contains("WLAN"))
                ?? netCat.GetInstanceNames().FirstOrDefault() ?? "";
            if (!string.IsNullOrEmpty(iface))
            {
                _netDownCounter = AddCounter(new PerformanceCounter("Network Interface", "Bytes Received/sec", iface));
                _netUpCounter = AddCounter(new PerformanceCounter("Network Interface", "Bytes Sent/sec", iface));
            }
        }
        catch (Exception ex) { Logger.Warn($"WidgetManager operation failed: {ex.Message}"); }

        var widgetTypes = _widgetsByBar.Values.SelectMany(v => v).Select(w => w.Config.Type).ToHashSet();
        if (widgetTypes.Contains("gpu"))
        {
            Task.Run(() =>
            {
                try
                {
                    var gpuCat = new PerformanceCounterCategory("GPU Engine");
                    var gpuInst = gpuCat.GetInstanceNames().FirstOrDefault(i => i.Contains("engtype_3D"));
                    if (!string.IsNullOrEmpty(gpuInst))
                        _gpuCounter = AddCounter(new PerformanceCounter("GPU Engine", "Utilization Percentage", gpuInst));
                }
                catch (Exception ex) { Logger.Warn($"Widget update failed: {ex.Message}"); }
            });
        }
        if (widgetTypes.Contains("disk"))
        {
            Task.Run(() =>
            {
                try
                {
                    var diskCat = new PerformanceCounterCategory("PhysicalDisk");
                    var diskInst = diskCat.GetInstanceNames().FirstOrDefault(i => i == "_Total");
                    if (!string.IsNullOrEmpty(diskInst))
                    {
                        _diskReadCounter = AddCounter(new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", diskInst));
                        _diskWriteCounter = AddCounter(new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", diskInst));
                    }
                }
                catch (Exception ex) { Logger.Warn($"Widget update failed: {ex.Message}"); }
            });
        }
    }

    private PerformanceCounter AddCounter(PerformanceCounter c)
    {
        _allCounters.Add(c);
        return c;
    }

    private void InitMediaMonitor()
    {
        _ = PollWinRtMediaAsync();
    }

    private async Task PollWinRtMediaAsync()
    {
        try
        {
            var manager = await Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = manager.GetCurrentSession();
            if (session == null) { _cachedMedia = ""; return; }
            var pb = session.GetPlaybackInfo();
            _mediaPaused = pb.PlaybackStatus != Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var info = await session.TryGetMediaPropertiesAsync();
            if (info != null)
            {
                var title = info.Title ?? "";
                var artist = info.Artist ?? "";
                _cachedMedia = string.IsNullOrEmpty(artist) ? title : $"{artist} - {title}";
            }
        }
        catch (Exception ex) { Logger.Warn($"WinRT media poll failed: {ex.Message}"); _cachedMedia = ""; }
    }

    private int _mediaPollTick;
    private void UpdateWinRtMedia()
    {
        _mediaPollTick++;
        if (_mediaPollTick % 2 == 0)
            _ = PollWinRtMediaAsync();
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus sps);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte Reserved1;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    private void UpdateWeather(WidgetEntry we)
    {
        try
        {
            var city = we.Config.Args ?? "";
            if (string.IsNullOrEmpty(city)) city = "";
            if ((DateTime.UtcNow - _weatherLastFetch).TotalMinutes > 30 || _weatherCache == "")
            {
                _weatherLastFetch = DateTime.UtcNow;
                Task.Run(() =>
                {
                    try
                    {
                        var url = string.IsNullOrEmpty(city)
                            ? "https://wttr.in/?format=%C+%t+%w"
                            : $"https://wttr.in/{Uri.EscapeDataString(city)}?format=%C+%t+%w";
                        _weatherCache = _http.GetStringAsync(url).Result.Trim();
                    }
                    catch (Exception ex) { Logger.Warn($"Weather fetch failed: {ex.Message}"); _weatherCache = "N/A"; }
                });
            }
            if (we.Text != null) we.Text.Text = _weatherCache;
        }
        catch (Exception ex) { Logger.Warn($"Weather widget failed: {ex.Message}"); if (we.Text != null) we.Text.Text = ""; }
    }

    private void UpdateIdleTime(WidgetEntry we)
    {
        try
        {
            var li = new LASTINPUTINFO();
            li.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(li);
            if (GetLastInputInfo(ref li))
            {
                var idle = (uint)Environment.TickCount - li.dwTime;
                var ts = TimeSpan.FromMilliseconds(idle);
                if (we.Text != null) we.Text.Text = ts.TotalHours >= 1
                    ? $"{(int)ts.TotalHours}h {(int)ts.Minutes}m"
                    : ts.TotalMinutes >= 1
                        ? $"{(int)ts.TotalMinutes}m"
                        : $"{ts.Seconds}s";
            }
        }
        catch (Exception ex) { Logger.Warn($"IdleTime widget failed: {ex.Message}"); if (we.Text != null) we.Text.Text = ""; }
    }

    private void UpdateClipboard(WidgetEntry we)
    {
        try
        {
            try
            {
                _clipboardCache = System.Windows.Clipboard.GetText() ?? "";
                if (_clipboardCache.Length > 60)
                    _clipboardCache = _clipboardCache[..60] + "…";
            }
            catch { _clipboardCache = ""; }
            if (we.Text != null) we.Text.Text = string.IsNullOrEmpty(_clipboardCache) ? "📋 empty" : $"📋 {_clipboardCache}";
        }
        catch (Exception ex) { Logger.Warn($"Clipboard widget failed: {ex.Message}"); if (we.Text != null) we.Text.Text = ""; }
    }

    private void UpdateProcessMonitor(WidgetEntry we)
    {
        try
        {
            var procName = we.Config.Args ?? "";
            if (string.IsNullOrEmpty(procName)) { if (we.Text != null) we.Text.Text = ""; return; }
            var procs = Process.GetProcessesByName(procName.Replace(".exe", ""));
            var running = procs.Length > 0;
            foreach (var p in procs) p.Dispose();
            if (we.Text != null) we.Text.Text = running ? $"🟢 {procName}" : $"🔴 {procName}";
        }
        catch (Exception ex) { Logger.Warn($"ProcessMonitor widget failed: {ex.Message}"); if (we.Text != null) we.Text.Text = ""; }
    }

    private void UpdateStock(WidgetEntry we)
    {
        try
        {
            var symbol = we.Config.Args ?? "";
            if (string.IsNullOrEmpty(symbol)) { if (we.Text != null) we.Text.Text = ""; return; }
            if ((DateTime.UtcNow - _stockLastFetch).TotalMinutes > 10 || _stockCache == "")
            {
                _stockLastFetch = DateTime.UtcNow;
                Task.Run(() =>
                {
                    try
                    {
                        var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}";
                        var json = _http.GetStringAsync(url).Result;
                        var price = ParseStockPrice(json);
                        _stockCache = price ?? "N/A";
                    }
                    catch (Exception ex) { Logger.Warn($"Stock fetch failed: {ex.Message}"); _stockCache = "N/A"; }
                });
            }
            if (we.Text != null) we.Text.Text = string.IsNullOrEmpty(_stockCache) ? "" : $"${_stockCache}";
        }
        catch (Exception ex) { Logger.Warn($"Stock widget failed: {ex.Message}"); if (we.Text != null) we.Text.Text = ""; }
    }

    private void UpdateTodo(WidgetEntry we)
    {
        try
        {
            var path = we.Config.Args ?? "";
            if (string.IsNullOrEmpty(path)) { if (we.Text != null) we.Text.Text = ""; return; }
            if ((DateTime.UtcNow - _todoLastFetch).TotalSeconds > 10 || _todoCache == "")
            {
                _todoLastFetch = DateTime.UtcNow;
                try
                {
                    if (File.Exists(path))
                    {
                        var lines = File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).Take(3).ToArray();
                        _todoCache = lines.Length > 0 ? string.Join(" · ", lines) : "Done!";
                    }
                    else { _todoCache = ""; }
                }
                catch { _todoCache = ""; }
            }
            if (we.Text != null) we.Text.Text = string.IsNullOrEmpty(_todoCache) ? "📝 --" : $"📝 {_todoCache}";
        }
        catch (Exception ex) { Logger.Warn($"Todo widget failed: {ex.Message}"); if (we.Text != null) we.Text.Text = ""; }
    }

    private void UpdateCustomCommand(WidgetEntry we)
    {
        try
        {
            var cmd = we.Config.Args ?? "";
            if (string.IsNullOrEmpty(cmd)) return;
            var interval = Math.Max(we.Config.FontSize > 0 ? we.Config.FontSize : 10, 1);
            string? cached;
            if (!_cmdCaches.TryGetValue(cmd, out cached)) { _cmdCaches[cmd] = cached = ""; }
            if ((DateTime.UtcNow - _cmdLastFetch).TotalSeconds > interval || cached == "")
            {
                _cmdLastFetch = DateTime.UtcNow;
                Task.Run(() =>
                {
                    try
                    {
                        using var p = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = "cmd.exe",
                                Arguments = $"/c {cmd}",
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                CreateNoWindow = true,
                            }
                        };
                        p.Start();
                        _cmdCaches[cmd] = p.StandardOutput.ReadToEnd().Trim();
                        p.WaitForExit(5000);
                        if (!p.HasExited) { p.Kill(); _cmdCaches[cmd] = "TIMEOUT"; }
                    }
                    catch (Exception ex) { Logger.Warn($"CustomCommand failed: {ex.Message}"); _cmdCaches[cmd] = ""; }
                });
            }
            if (we.Text != null) we.Text.Text = _cmdCaches.GetValueOrDefault(cmd, "");
        }
        catch (Exception ex) { Logger.Warn($"CustomCommand widget failed: {ex.Message}"); }
    }

    private static string? ParseStockPrice(string json)
    {
        try
        {
            var idx = json.IndexOf("\"regularMarketPrice\"");
            if (idx < 0) return null;
            idx += 22;
            var end = json.IndexOfAny(new[] { ',', '}', '\n' }, idx);
            if (end < 0) return null;
            var num = json[idx..end].Trim().Trim('"');
            return double.TryParse(num, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var v)
                ? v.ToString("F2") : null;
        }
        catch { return null; }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [System.Runtime.InteropServices.ComImport,
     System.Runtime.InteropServices.Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }

    [System.Runtime.InteropServices.ComImport,
     System.Runtime.InteropServices.Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
     System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IntPtr ppDevices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
        int GetDevice([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
        int RegisterEndpointNotificationCallback(IntPtr pClient);
        int UnregisterEndpointNotificationCallback(IntPtr pClient);
    }

    [System.Runtime.InteropServices.ComImport,
     System.Runtime.InteropServices.Guid("D666063F-1587-4E43-81F1-B948E807363F"),
     System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref System.Guid iid, int dwClsCtx, IntPtr pActivationParams, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.IUnknown)] out object ppInterface);
        int OpenPropertyStore(int stgmAccess, out IntPtr ppProperties);
        int GetId([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] out string ppwstrId);
        int GetState(out int pdwState);
    }

    [System.Runtime.InteropServices.ComImport,
     System.Runtime.InteropServices.Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),
     System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr pNotify);
        int UnregisterControlChangeNotify(IntPtr pNotify);
        int GetChannelCount(out uint pnChannelCount);
        int SetMasterVolumeLevel(float fLevelDB, ref System.Guid pguidEventContext);
        int SetMasterVolumeLevelScalar(float fLevel, ref System.Guid pguidEventContext);
        int GetMasterVolumeLevel(out float pfLevelDB);
        int GetMasterVolumeLevelScalar(out float pfLevel);
        int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref System.Guid pguidEventContext);
        int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref System.Guid pguidEventContext);
        int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
        int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
        int SetMute([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] bool bMute, ref System.Guid pguidEventContext);
        int GetMute(out bool pbMute);
        int GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);
        int VolumeStepUp(ref System.Guid pguidEventContext);
        int VolumeStepDown(ref System.Guid pguidEventContext);
    }

    private static object? GetVolumeEndpoint()
    {
        try
        {
            var enumeratorType = Type.GetTypeFromCLSID(new System.Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"));
            if (enumeratorType == null) return null;
            var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(enumeratorType)!;
            var guid = new System.Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
            enumerator.GetDefaultAudioEndpoint(0, 0, out var device);
            device.Activate(ref guid, 1, IntPtr.Zero, out var ep);
            return ep;
        }
        catch { return null; }
    }

    private static string? GetDefaultAudioDeviceName()
    {
        try
        {
            var enumeratorType = Type.GetTypeFromCLSID(new System.Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"));
            if (enumeratorType == null) return null;
            var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(enumeratorType)!;
            enumerator.GetDefaultAudioEndpoint(0, 0, out var device);
            device.GetId(out var id);
            if (id == null) return null;
            var parts = id.Split('{');
            return parts.Length > 0 ? parts[0].TrimEnd('.') : id;
        }
        catch { return null; }
    }

    private static bool CheckBluetooth()
    {
        try
        {
            var t = Type.GetTypeFromCLSID(new System.Guid("21F52C1D-7396-440A-975C-98E18D5854C5"));
            if (t != null) return true;
            return false;
        }
        catch { return false; }
    }

    private static bool IsMicrophoneMuted()
    {
        try
        {
            var enumeratorType = Type.GetTypeFromCLSID(new System.Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"));
            if (enumeratorType == null) return false;
            var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(enumeratorType)!;
            enumerator.GetDefaultAudioEndpoint(1, 0, out var device);
            var guid = new System.Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
            device.Activate(ref guid, 1, IntPtr.Zero, out var ep);
            var vol = (IAudioEndpointVolume)ep;
            vol.GetMute(out bool muted);
            return muted;
        }
        catch { return false; }
    }

    private static bool IsCameraActive()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine
                .OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam");
            return key != null;
        }
        catch { return false; }
    }
}
