using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Dwalia.Configuration;
using Dwalia.Infrastructure;
using Dwalia.Win32;
using static Dwalia.Win32.NativeMethods;
using static Dwalia.Win32.NativeConstants;
using static Dwalia.Win32.WindowStyles;

namespace Dwalia.Managers;

public class WidgetManager
{
    private readonly Dictionary<string, List<WidgetEntry>> _widgetsByBar = new();
    private DispatcherTimer? _updateTimer;
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
    private long _lastNetSample;
    private float _lastNetDown;
    private float _lastNetUp;

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
    }

    public void Initialize()
    {
        if (!ServiceLocator.TryResolve<ConfigRoot>(out var cfg)) return;
        _widgetsByBar.Clear();

        foreach (var w in cfg.Widgets.Where(w => w.Enabled))
        {
            var pages = w.BarPage == "All"
                ? new[] { "Docker", "Basic", "Advanced" }
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
        WidgetsChanged?.Invoke();
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
            we.Pill = BuildPill(we);
            switch (we.Config.Align)
            {
                case "left": left.Children.Add(we.Pill); break;
                case "center": center.Children.Add(we.Pill); break;
                default: right.Children.Add(we.Pill); break;
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
        };
        if (c.Width > 0) pill.Width = c.Width;

        switch (c.Type)
        {
            case "workspace":
                pill.Child = new StackPanel { Orientation = Orientation.Horizontal };
                break;
            case "layout":
                we.Text = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, Text = we.CachedText };
                pill.Child = we.Text;
                break;
            case "active_window":
                we.Text = new TextBlock { FontSize = 11, Text = we.CachedText };
                pill.Child = we.Text;
                break;
            case "clock":
            case "time_only":
            case "date_only":
                we.Text = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, Text = we.CachedText };
                pill.Child = we.Text;
                break;
            case "network":
                we.Panel = new StackPanel { Orientation = Orientation.Horizontal };
                we.Text = new TextBlock { FontSize = 10, FontFamily = new FontFamily("Consolas") };
                we.SecondaryText = new TextBlock { FontSize = 10, FontFamily = new FontFamily("Consolas") };
                we.Panel.Children.Add(we.Text);
                we.Panel.Children.Add(new TextBlock { Text = " · ", FontSize = 10 });
                we.Panel.Children.Add(we.SecondaryText);
                pill.Child = we.Panel;
                break;
            case "media":
                we.Dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 5, 0) };
                we.Canvas = new Canvas { ClipToBounds = true };
                we.Text = new TextBlock { FontSize = 10, FontFamily = new FontFamily("Consolas") };
                we.Canvas.Children.Add(we.Text);
                we.Text.RenderTransform = new TranslateTransform();
                var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                sp.Children.Add(we.Dot);
                sp.Children.Add(we.Canvas);
                pill.Child = sp;
                if (c.Width <= 0) pill.Width = 200;
                break;
            case "window_tabs":
                we.Panel = new StackPanel { Orientation = Orientation.Horizontal };
                pill.Child = we.Panel;
                pill.Padding = new Thickness(0);
                pill.Background = Brushes.Transparent;
                break;
            case "launcher":
                we.Panel = new StackPanel { Orientation = Orientation.Horizontal };
                pill.Child = we.Panel;
                pill.Padding = new Thickness(0);
                pill.Background = Brushes.Transparent;
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
                we.Text = new TextBlock { FontSize = 10, Text = we.CachedText };
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
            catch { }
        }
        return Color.FromArgb(0x44, 0xff, 0xff, 0xff);
    }

    private static Color? ParseColor(string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return null;
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return null; }
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
            case "script":       UpdateScript(we); break;
            case "label":        UpdateLabel(we); break;
            case "button":       UpdateButton(we); break;
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
        catch { if (we.Text != null) we.Text.Text = " --%"; }
    }

    private void UpdateMemory(WidgetEntry we)
    {
        try
        {
            if (_memCounter == null) return;
            var pct = (int)_memCounter.NextValue();
            if (we.Text != null) we.Text.Text = $"{(we.Config.Format == "simple" ? "" : "◉ ")}{pct,3}%{ProgressBar(pct)}";
        }
        catch { if (we.Text != null) we.Text.Text = " --%"; }
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
        catch { if (we.Text != null) we.Text.Text = "🔋 --"; }
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
        catch { }
        if (we.Text != null) we.Text.Text = $"▼ {FormatSpeed(_lastNetDown)}";
        if (we.SecondaryText != null) we.SecondaryText.Text = $"▲ {FormatSpeed(_lastNetUp)}";
    }

    private void UpdateMedia(WidgetEntry we)
    {
        UpdateWinRtMedia();
        if (string.IsNullOrEmpty(_cachedMedia)) return;
        if (we.Text != null) we.Text.Text = _cachedMedia;
    }

    private void UpdateGpu(WidgetEntry we)
    {
        try
        {
            if (_gpuCounter == null) return;
            var pct = (int)_gpuCounter.NextValue();
            if (we.Text != null) we.Text.Text = $"GPU {pct,3}%";
        }
        catch { if (we.Text != null) we.Text.Text = "GPU --"; }
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
        catch { if (we.Text != null) we.Text.Text = "💿 --"; }
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
        catch { if (we.Text != null) we.Text.Text = "💾 --"; }
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
        catch { if (we.Text != null) we.Text.Text = "📶 --"; }
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
        catch { if (we.Text != null) we.Text.Text = "🌐 --"; }
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
                catch { _publicIpText = "N/A"; }
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
        catch { if (we.Text != null) we.Text.Text = ""; }
    }

    private void UpdateVolume(WidgetEntry we)
    {
        if (we.Text != null) we.Text.Text = "";
    }

    private void UpdateAudioDevice(WidgetEntry we)
    {
        if (we.Text != null) we.Text.Text = "";
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
                catch { }
            }
            if (we.Text != null) we.Text.Text = string.Join(" · ", parts);
        }
        catch { if (we.Text != null) we.Text.Text = ""; }
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
        catch { }
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

    private void UpdateBluetooth(WidgetEntry we)
    {
        if (we.Text != null) we.Text.Text = "";
    }

    private void UpdateMicrophone(WidgetEntry we)
    {
        if (we.Text != null) we.Text.Text = "";
    }

    private void UpdateCamera(WidgetEntry we)
    {
        if (we.Text != null) we.Text.Text = "";
    }

    private void UpdateScript(WidgetEntry we)
    {
        if (string.IsNullOrEmpty(we.Config.Args)) return;
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
        if (we.Pill?.Child is StackPanel sp)
        {
            sp.Children.Clear();
            if (ServiceLocator.TryResolve<WorkspaceManager>(out var wsm) &&
                ServiceLocator.TryResolve<ConfigRoot>(out var cfg))
            {
                foreach (var ws in wsm.Workspaces)
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
                    var pill = new Border
                    {
                        Width = isActive ? 28 : 10, Height = 10,
                        CornerRadius = new CornerRadius(5),
                        Margin = new Thickness(3, 0, 3, 0),
                        Background = new SolidColorBrush(c),
                        Cursor = System.Windows.Input.Cursors.Hand,
                    };
                    pill.MouseLeftButtonDown += (_, _) => wsm.SwitchToWorkspace(wsId);
                    sp.Children.Add(pill);
                }
            }
        }
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

        var existing = we.Panel.Children.OfType<Border>().ToDictionary(b => b.Tag as IntPtr? ?? 0, b => b);
        foreach (var mw in windows)
        {
            if (!IsWindow(mw.Hwnd)) continue;
            if (existing.TryGetValue(mw.Hwnd, out var oldPill)) { existing.Remove(mw.Hwnd); continue; }

            var h = we.Config.Height > 0 ? we.Config.Height : 28;
            var hwnd = mw.Hwnd; var captured = mw;
            var title = captured.Title.Length > 25 ? captured.Title[..25] : captured.Title;
            var stack = new StackPanel { Orientation = Orientation.Horizontal };

            var btn = new Button { Content = title, Height = h, FontSize = 10,
                Padding = new Thickness(10, 0, 2, 0), Background = Brushes.Transparent,
                BorderThickness = new Thickness(0), ToolTip = captured.Title,
                Cursor = System.Windows.Input.Cursors.Hand,
                Foreground = new SolidColorBrush(fm?.ActiveWindow == mw
                    ? ParseColor(cfg?.Theme.Accent) ?? Colors.Cyan
                    : ParseColor(cfg?.Theme.Foreground) ?? Colors.White) };
            btn.Click += (_, _) => { ShowWindow(hwnd, SW_RESTORE); SetForegroundWindow(hwnd); fm?.SetActiveWindow(captured); };
            btn.MouseDown += (_, e) => { if (e.MiddleButton == System.Windows.Input.MouseButtonState.Pressed) PostMessage(hwnd, WM_CLOSE, 0, 0); };
            stack.Children.Add(btn);

            var cb = new Button { Content = "✕", Width = 20, Height = h, FontSize = 8,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Foreground = new SolidColorBrush(ParseColor(cfg?.Theme.Muted) ?? Colors.Gray) };
            cb.Click += (_, _) => PostMessage(hwnd, WM_CLOSE, 0, 0);
            stack.Children.Add(cb);

            var pill = new Border { CornerRadius = new CornerRadius(h / 2), Height = h,
                Background = new SolidColorBrush(ParseColor(cfg?.Theme.TaskButtonBackground) ?? Color.FromRgb(0x24, 0x28, 0x3e)),
                Child = stack, Tag = mw.Hwnd, Margin = new Thickness(2, 0, 2, 0) };
            we.Panel.Children.Add(pill);
        }
        foreach (var kv in existing) we.Panel.Children.Remove(kv.Value);
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
            catch { }
            content.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            var btn = new Button { Content = content, Height = h, FontSize = 10, Padding = new Thickness(12, 0, 12, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand, Foreground = new SolidColorBrush(ParseColor(cfg.Theme.Foreground) ?? Colors.White) };
            var cmd = entry.Path;
            btn.Click += (_, _) => { try { Process.Start(new ProcessStartInfo { FileName = cmd, UseShellExecute = true }); } catch { } };
            var pill = new Border { CornerRadius = new CornerRadius(h / 2), Height = h, Background = new SolidColorBrush(ParseColor(cfg.Theme.TaskButtonBackground) ?? Color.FromRgb(0x24, 0x28, 0x3e)), Child = btn, Margin = new Thickness(2, 0, 2, 0) };
            we.Panel.Children.Add(pill);
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
        catch { }

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
                catch { }
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
                catch { }
            });
        }
    }

    private void InitMediaMonitor()
    {
        Task.Run(async () =>
        {
            try
            {
                var manager = await Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                manager.SessionsChanged += (_, _) => { };
                if (manager.GetCurrentSession() != null)
                    PollWinRtMedia();
            }
            catch { }
        });
    }

    private void PollWinRtMedia()
    {
        Task.Run(() =>
        {
            try
            {
                var manager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager
                    .RequestAsync().GetAwaiter().GetResult();
                var session = manager.GetCurrentSession();
                if (session == null) { _cachedMedia = ""; return; }
                var pb = session.GetPlaybackInfo();
                _mediaPaused = pb.PlaybackStatus != Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                var info = session.TryGetMediaPropertiesAsync().GetAwaiter().GetResult();
                if (info != null)
                {
                    var title = info.Title ?? "";
                    var artist = info.Artist ?? "";
                    _cachedMedia = string.IsNullOrEmpty(artist) ? title : $"{artist} - {title}";
                }
            }
            catch { _cachedMedia = ""; }
        });
    }

    private void UpdateWinRtMedia()
    {
        if (DateTime.Now.Second % 3 == 0)
            PollWinRtMedia();
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
}
