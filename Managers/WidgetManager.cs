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
    private readonly Dictionary<string, PerformanceCounter> _perfCounters = new();
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

    private class WidgetEntry
    {
        public WidgetConfig Config = null!;
        public Border Pill = null!;
        public TextBlock? Text;
        public StackPanel? Panel;
        public Border? Dot;
        public Canvas? Canvas;
        public TextBlock? SecondaryText;
        public string CachedText = "";
        public Dictionary<IntPtr, Border> TabPills = new();
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
        if (c.Width > 0) pill.Width = c.Width;

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
                we.Text = new TextBlock { FontSize = c.FontSize > 0 ? (double)c.FontSize : 16, Text = we.CachedText };
                pill.Child = we.Text;
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
            case "volume":
            case "gpu":
            case "disk":
            case "disk_usage":
            case "uptime":
            case "wifi_ssid":
            case "ip_address":
            case "public_ip":
            case "vpn_status":
            case "audio_device":
            case "bluetooth":
            case "microphone":
            case "camera":
            case "window_count":
            case "world_clock":
            case "countdown":
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
                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                    _publicIpText = await client.GetStringAsync("https://api.ipify.org");
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

    private string _scriptOutput = ""; private string _scriptPath = ""; private DateTime _scriptLastRun;
    private void UpdateScript(WidgetEntry we)
    {
        try
        {
            var script = we.Config.Args ?? "";
            if (string.IsNullOrEmpty(script)) return;
            var interval = Math.Max(we.Config.FontSize > 0 ? we.Config.FontSize : 5, 1);
            if (script != _scriptPath || (DateTime.UtcNow - _scriptLastRun).TotalSeconds > interval)
            {
                _scriptPath = script;
                _scriptLastRun = DateTime.UtcNow;
                Task.Run(() =>
                {
                    try
                    {
                        using var p = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = "cmd.exe",
                                Arguments = $"/c {script}",
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                CreateNoWindow = true,
                            }
                        };
                        p.Start();
                        _scriptOutput = p.StandardOutput.ReadToEnd().Trim();
                        p.WaitForExit(5000);
                        if (!p.HasExited) p.Kill();
                    }
                    catch (Exception ex) { Logger.Warn($"Script failed: {ex.Message}"); _scriptOutput = ""; }
                });
            }
            if (we.Text != null) we.Text.Text = _scriptOutput;
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
            var borderColor = isActive
                ? (ParseColor(cfg?.Theme.Accent) ?? Color.FromRgb(0x7a, 0xa2, 0xf7))
                : (ParseColor(cfg?.Theme.Muted) ?? Color.FromRgb(0x56, 0x5f, 0x89));

            var pill = new Border
            {
                MinWidth = iconSize + 8,
                Height = iconSize + 4,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(
                    ParseColor(cfg?.Theme.TaskButtonBackground) ?? Color.FromRgb(0x1a, 0x1a, 0x3e)),
                BorderBrush = new SolidColorBrush(borderColor),
                BorderThickness = new Thickness(borderThickness),
                Child = btn,
                Margin = new Thickness(1, 0, 1, 0),
                Tag = isActive ? "taskbar-icon active" : "taskbar-icon"
            };
            we.TabPills[hwnd] = pill;
            we.Panel.Children.Add(pill);
        }

        foreach (var kv in existing)
        {
            we.Panel.Children.Remove(kv.Value);
            we.TabPills.Remove(kv.Key);
        }
    }

    private int _dockInit;

    private void UpdateSystray(WidgetEntry we)
    {
        if (we.Panel == null) return;
        if (_dockInit != 0) return;
        _dockInit = 1;

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

            we.Panel.Children.Add(new Border
            {
                MinWidth = iconSize + 10,
                Height = iconSize + 4,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(bg),
                Child = btn,
                Margin = new Thickness(1, 0, 1, 0)
            });
        }
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
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _memCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use");
            _cpuCounter.NextValue(); _memCounter.NextValue();

            var netCat = new PerformanceCounterCategory("Network Interface");
            var iface = netCat.GetInstanceNames().FirstOrDefault(i =>
                i.Contains("Ethernet") || i.Contains("Wi-Fi") || i.Contains("WLAN"))
                ?? netCat.GetInstanceNames().FirstOrDefault() ?? "";
            if (!string.IsNullOrEmpty(iface))
            {
                _netDownCounter = new PerformanceCounter("Network Interface", "Bytes Received/sec", iface);
                _netUpCounter = new PerformanceCounter("Network Interface", "Bytes Sent/sec", iface);
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
                        _gpuCounter = new PerformanceCounter("GPU Engine", "Utilization Percentage", gpuInst);
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
                        _diskReadCounter = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", diskInst);
                        _diskWriteCounter = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", diskInst);
                    }
                }
                catch (Exception ex) { Logger.Warn($"Widget update failed: {ex.Message}"); }
            });
        }
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
            var key = $"weather_{city}";
            if ((DateTime.UtcNow - _weatherLastFetch).TotalMinutes > 30 || _weatherCache == "")
            {
                _weatherLastFetch = DateTime.UtcNow;
                Task.Run(() =>
                {
                    try
                    {
                        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                        client.DefaultRequestHeaders.UserAgent.ParseAdd("Dwalia/1.0");
                        var url = string.IsNullOrEmpty(city)
                            ? "https://wttr.in/?format=%C+%t+%w"
                            : $"https://wttr.in/{Uri.EscapeDataString(city)}?format=%C+%t+%w";
                        _weatherCache = client.GetStringAsync(url).Result.Trim();
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
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    _clipboardCache = System.Windows.Clipboard.GetText() ?? "";
                    if (_clipboardCache.Length > 60)
                        _clipboardCache = _clipboardCache[..60] + "…";
                }
                catch { _clipboardCache = ""; }
            });
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
                        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                        client.DefaultRequestHeaders.UserAgent.ParseAdd("Dwalia/1.0");
                        var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}";
                        var json = client.GetStringAsync(url).Result;
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

    [System.Runtime.InteropServices.ComImport,
     System.Runtime.InteropServices.Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
     System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator2 { }

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
