using System.Collections.ObjectModel;
using System.Diagnostics;
using NetSpeed.GameBoost.Desktop.Profiles;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Threading;\nusing System.Windows.Media;\nusing System.Windows.Shapes;
using NetSpeed.GameBoost.Desktop.Diagnostics;
using NetSpeed.GameBoost.Desktop.Networking;

namespace NetSpeed.GameBoost.Desktop;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private long _lastReceived;
    private long _lastSent;
    private DateTime _lastSampleUtc;
    private bool _hasSample;
    private CancellationTokenSource? _diagnosticsCts;
    private readonly GameProfileService _profileService = new();
    private readonly ObservableCollection<GameProfile> _profiles = [];
    private readonly DispatcherTimer _profileTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _profilesReady;\n    private CancellationTokenSource? _routeAnalysisCts;\n    private readonly RouteHistoryService _routeHistoryService = new();

    public MainWindow()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => UpdateNetworkStats();
        Loaded += async (_, _) =>
        {
            UpdateNetworkStats();
            _timer.Start();
            await InitializeProfilesAsync();
            _profileTimer.Tick += (_, _) => RefreshSelectedGameStatus();
            _profileTimer.Start();
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            _profileTimer.Stop();
            _diagnosticsCts?.Cancel();
            _diagnosticsCts?.Dispose();
        };
    }

    private async void RunDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        RunDiagnosticsButton.IsEnabled = false;
        RunDiagnosticsButton.Content = "检测中…";
        _diagnosticsCts?.Cancel();
        _diagnosticsCts?.Dispose();
        _diagnosticsCts = new CancellationTokenSource();

        try
        {
            var token = _diagnosticsCts.Token;
            // A public anycast address is used only as a diagnostic target; ICMP may be filtered.
            var pingTask = NetworkDiagnostics.MeasurePingAsync("1.1.1.1", 10, 1200, token);
            var dnsTask = NetworkDiagnostics.MeasureSystemDnsAsync("example.com", token);
            await Task.WhenAll(pingTask, dnsTask);

            var ping = await pingTask;
            var dns = await dnsTask;

            PingText.Text = double.IsNaN(ping.AverageLatencyMs)
                ? "延迟    超时 / 无响应"
                : $"延迟    {ping.AverageLatencyMs:0.0} ms";
            JitterText.Text = double.IsNaN(ping.JitterMs)
                ? "抖动    -- ms"
                : $"抖动    {ping.JitterMs:0.0} ms";
            LossText.Text = $"丢包    {ping.PacketLossPercent:0.0}%";
            PingDetailText.Text = $"目标 {ping.Host} · {ping.Received}/{ping.Sent} 成功 · DNS {dns.DurationMs:0.0} ms";

            if (GameProfileCombo.SelectedItem is GameProfile selectedProfile)
            {
                GameProfileService.AddSample(selectedProfile, new GameQualitySample
                {
                    AverageLatencyMs = double.IsNaN(ping.AverageLatencyMs) ? null : ping.AverageLatencyMs,
                    JitterMs = double.IsNaN(ping.JitterMs) ? null : ping.JitterMs,
                    PacketLossPercent = ping.PacketLossPercent,
                    DnsResolutionMs = dns.DurationMs
                });
                await SaveProfilesAsync();
            }
        }
        catch (OperationCanceledException)
        {
            PingDetailText.Text = "检测已取消";
        }
        catch (Exception ex)
        {
            PingDetailText.Text = $"检测失败：{ex.Message}";
            Debug.WriteLine(ex);
        }
        finally
        {
            RunDiagnosticsButton.IsEnabled = true;
            RunDiagnosticsButton.Content = "重新检测网络质量";
        }
    }

    private async Task InitializeProfilesAsync()
    {
        try
        {
            var loaded = await _profileService.LoadAsync();
            _profiles.Clear();
            foreach (var profile in loaded)
                _profiles.Add(profile);

            GameProfileCombo.ItemsSource = _profiles;
            if (_profiles.Count > 0)
                GameProfileCombo.SelectedIndex = 0;
            _profilesReady = true;
            RefreshSelectedGameStatus();
        }
        catch (Exception ex)
        {
            GameProcessStatus.Text = $"档案加载失败：{ex.Message}";
            Debug.WriteLine(ex);
        }
    }

    private async void AddGameProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var name = ProfileNameInput.Text.Trim();
        var processName = ProcessNameInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || !GameProfileService.IsValidProcessName(processName))
        {
            MessageBox.Show("请填写游戏名称和有效的 EXE 进程名，例如：game.exe。", "无法添加档案",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_profiles.Any(p => string.Equals(p.ProcessName, processName, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("这个进程名已经存在于游戏档案中。", "重复档案",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var profile = new GameProfile { Name = name, ProcessName = processName };
        _profiles.Add(profile);
        GameProfileCombo.SelectedItem = profile;
        ProfileNameInput.Clear();
        ProcessNameInput.Clear();
        await SaveProfilesAsync();
        RefreshSelectedGameStatus();
    }

    private async void DeleteGameProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (GameProfileCombo.SelectedItem is not GameProfile profile)
            return;

        var answer = MessageBox.Show($"确定删除“{profile.Name}”档案及其本地质量历史吗？",
            "删除游戏档案", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;

        _profiles.Remove(profile);
        if (_profiles.Count > 0)
            GameProfileCombo.SelectedIndex = 0;
        await SaveProfilesAsync();
        RefreshSelectedGameStatus();
    }

    private void GameProfileCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_profilesReady)
            RefreshSelectedGameStatus();
    }

    private void RefreshSelectedGameStatus()
    {
        if (GameProfileCombo.SelectedItem is not GameProfile profile)
        {
            GameProcessStatus.Text = "尚未选择游戏档案";
            GameProcessStatus.Foreground = System.Windows.Media.Brushes.LightGray;
            return;
        }

        var running = GameProfileService.IsRunning(profile.ProcessName);
        GameProcessStatus.Text = running
            ? $"● {profile.Name} 正在运行 · 已记录 {profile.History.Count} 条样本"
            : $"○ {profile.Name} 未运行 · 已记录 {profile.History.Count} 条样本";
        GameProcessStatus.Foreground = running
            ? new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(101, 230, 194))
            : new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(243, 201, 105));
    }

    private async Task SaveProfilesAsync()
    {
        try
        {
            await _profileService.SaveAsync(_profiles.ToList());
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法保存游戏档案：{ex.Message}", "本地保存失败",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }


    private async void RefreshGameConnectionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (GameProfileCombo.SelectedItem is not GameProfile profile)
        {
            GameConnectionStatus.Text = "请先选择游戏档案";
            return;
        }

        RefreshGameConnectionsButton.IsEnabled = false;
        RefreshGameConnectionsButton.Content = "扫描中…";
        GameConnectionStatus.Text = $"正在读取 {profile.Name} 的 TCP / UDP 端点…";
        GameConnectionsList.Items.Clear();

        try
        {
            var endpoints = await GameConnectionMonitor.GetConnectionsForProcessAsync(profile.ProcessName);
            foreach (var endpoint in endpoints.Take(100))
                GameConnectionsList.Items.Add($"PID {endpoint.ProcessId}  {endpoint.ToDisplayString()}");

            GameConnectionStatus.Text = endpoints.Count == 0
                ? "未发现连接。游戏可能尚未联网，或系统未提供该进程的端点信息。"
                : $"发现 {endpoints.Count} 个端点，显示最多 100 条。仅为系统连接表快照。";
        }
        catch (Exception ex)
        {
            GameConnectionStatus.Text = $"扫描失败：{ex.Message}";
            Debug.WriteLine(ex);
        }
        finally
        {
            RefreshGameConnectionsButton.IsEnabled = true;
            RefreshGameConnectionsButton.Content = "扫描游戏连接";
        }
    }


    private async void AnalyzeGameRoutesButton_Click(object sender, RoutedEventArgs e)
    {
        if (GameProfileCombo.SelectedItem is not GameProfile profile)
        {
            GameRouteStatus.Text = "请先选择游戏档案";
            return;
        }

        _routeAnalysisCts?.Cancel();
        _routeAnalysisCts?.Dispose();
        _routeAnalysisCts = new CancellationTokenSource();
        var token = _routeAnalysisCts.Token;

        AnalyzeGameRoutesButton.IsEnabled = false;
        AnalyzeGameRoutesButton.Content = "分析中…";
        GameRouteStatus.Text = "读取游戏端点，执行 ICMP 测量与有限 TCP connect 检查…";
        GameRouteList.Items.Clear();
        TcpProbeList.Items.Clear();

        try
        {
            var endpoints = await GameConnectionMonitor.GetConnectionsForProcessAsync(profile.ProcessName, token);
            var routeTask = GameRouteAnalyzer.AnalyzeAsync(endpoints, maxTargets: 5, cancellationToken: token);
            var tcpTask = TcpEndpointProbe.ProbeObservedAsync(endpoints, maxTargets: 5, timeoutMs: 1500, cancellationToken: token);
            await Task.WhenAll(routeTask, tcpTask);
            var routes = await routeTask;
            var tcpResults = await tcpTask;

            foreach (var result in routes)
                GameRouteList.Items.Add(result.ToDisplayString());
            foreach (var result in tcpResults)
                TcpProbeList.Items.Add(result.ToDisplayString());

            var timestamp = DateTime.UtcNow;
            var historyEntries = new List<RouteHistoryEntry>();
            foreach (var route in routes)
            {
                var tcp = tcpResults.FirstOrDefault(item =>
                    item.RemoteEndpoint.StartsWith(route.RemoteAddress + ":", StringComparison.OrdinalIgnoreCase) ||
                    item.RemoteEndpoint.StartsWith("[" + route.RemoteAddress + "]", StringComparison.OrdinalIgnoreCase));
                historyEntries.Add(new RouteHistoryEntry(
                    timestamp, profile.Name, route.RemoteAddress,
                    double.IsFinite(route.AverageLatencyMs) ? route.AverageLatencyMs : null,
                    double.IsFinite(route.JitterMs) ? route.JitterMs : null,
                    route.PacketLossPercent, route.QualityScore,
                    tcp?.ConnectLatencyMs, tcp?.Connected));
            }

            if (historyEntries.Count > 0)
                await _routeHistoryService.SaveAsync(historyEntries, token);
            await RefreshRouteHistoryAsync(profile.Name, token);

            GameRouteStatus.Text = routes.Count == 0
                ? $"未发现可测量的单播 IP；TCP 检查 {tcpResults.Count} 项。游戏可能未联网或端点不可见。"
                : $"ICMP 测量 {routes.Count} 个 IP；TCP connect 检查 {tcpResults.Count} 个已观察 TCP 端点。仅为诊断，不会修改路由。";
        }
        catch (OperationCanceledException)
        {
            GameRouteStatus.Text = "线路分析已取消";
        }
        catch (Exception ex)
        {
            GameRouteStatus.Text = $"线路分析失败：{ex.Message}";
            Debug.WriteLine(ex);
        }
        finally
        {
            AnalyzeGameRoutesButton.IsEnabled = true;
            AnalyzeGameRoutesButton.Content = "分析游戏线路";
        }
    }

    private async Task RefreshRouteHistoryAsync(string gameName, CancellationToken cancellationToken = default)
    {
        var history = await _routeHistoryService.LoadRecentAsync(gameName, 12, cancellationToken);
        RouteHistoryList.Items.Clear();
        foreach (var entry in history.Take(8))
        {
            var latency = entry.IcmpLatencyMs.HasValue ? $"{entry.IcmpLatencyMs:0.0} ms" : "ICMP --";
            var tcp = entry.TcpConnectLatencyMs.HasValue ? $"TCP {entry.TcpConnectLatencyMs:0.0} ms" :
                entry.TcpConnected == false ? "TCP 不通" : "TCP --";
            RouteHistoryList.Items.Add(
                $"{entry.TimestampUtc.ToLocalTime():HH:mm:ss}  {entry.RemoteAddress}  {latency}  {tcp}  评分 {entry.QualityScore:0.0}");
        }

        RouteHistoryChart.Children.Clear();
        var scores = history
            .Where(entry => double.IsFinite(entry.QualityScore))
            .OrderBy(entry => entry.TimestampUtc)
            .Select(entry => entry.QualityScore)
            .ToArray();
        if (scores.Length < 2)
            return;

        var width = Math.Max(RouteHistoryChart.ActualWidth, 280);
        const double height = 58;
        var line = new Polyline
        {
            Stroke = new SolidColorBrush(Color.FromRgb(39, 215, 242)),
            StrokeThickness = 2,
            Opacity = 0.95
        };
        for (var index = 0; index < scores.Length; index++)
        {
            var x = 6 + (width - 12) * index / (scores.Length - 1);
            var y = height - 6 - Math.Clamp(scores[index], 0, 100) / 100d * (height - 12);
            line.Points.Add(new Point(x, y));
        }
        RouteHistoryChart.Children.Add(line);
    }

    private void UpdateNetworkStats()
    {
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Select(n => new
                {
                    Name = n.Name,
                    Stats = n.GetIPv4Statistics()
                })
                .ToArray();

            var received = interfaces.Sum(x => x.Stats.BytesReceived);
            var sent = interfaces.Sum(x => x.Stats.BytesSent);
            var now = DateTime.UtcNow;

            if (_hasSample)
            {
                var seconds = Math.Max((now - _lastSampleUtc).TotalSeconds, 0.001);
                var downBps = Math.Max(0, received - _lastReceived) / seconds;
                var upBps = Math.Max(0, sent - _lastSent) / seconds;
                DownloadText.Text = FormatRate(downBps);
                UploadText.Text = FormatRate(upBps);
            }
            else
            {
                DownloadText.Text = "采样中…";
                UploadText.Text = "采样中…";
                _hasSample = true;
            }

            _lastReceived = received;
            _lastSent = sent;
            _lastSampleUtc = now;
            AdapterText.Text = interfaces.Length == 0
                ? "未发现活动接口"
                : string.Join(", ", interfaces.Select(x => x.Name).Take(2));
        }
        catch (Exception ex)
        {
            DownloadText.Text = "不可用";
            UploadText.Text = "不可用";
            AdapterText.Text = ex.GetType().Name;
            Debug.WriteLine(ex);
        }
    }

    private static string FormatRate(double bytesPerSecond)
    {
        const double mb = 1024d * 1024d;
        const double kb = 1024d;
        return bytesPerSecond >= mb
            ? $"{bytesPerSecond / mb:0.00} MB/s"
            : $"{bytesPerSecond / kb:0.0} KB/s";
    }
}
