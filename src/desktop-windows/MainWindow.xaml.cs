using System.Collections.ObjectModel;
using System.IO;
using Path = System.IO.Path;
using System.Diagnostics;
using NetSpeed.GameBoost.Desktop.Profiles;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Text;
using Microsoft.Win32;
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
    private bool _profilesReady;
    private CancellationTokenSource? _routeAnalysisCts;
    private readonly RouteHistoryService _routeHistoryService = new();
    private readonly ConnectionChangeTracker _connectionChangeTracker = new();
    private readonly DispatcherTimer _connectionMonitorTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool _connectionPollRunning;
    private bool _connectionMonitoring;
    private string? _monitoredProcessName;
    private string? _monitoredGameName;
    private readonly NetworkHealthMonitor _networkHealthMonitor = new();
    private readonly DispatcherTimer _networkHealthTimer = new() { Interval = TimeSpan.FromSeconds(8) };
    private CancellationTokenSource? _networkHealthCts;
    private bool _networkHealthPollRunning;
    private IReadOnlyList<DnsServerResult> _lastDnsResolverResults = Array.Empty<DnsServerResult>();

    public MainWindow()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => UpdateNetworkStats();
        _connectionMonitorTimer.Tick += async (_, _) => await PollGameConnectionsAsync();
        _networkHealthTimer.Tick += async (_, _) => await PollNetworkHealthAsync();
        Loaded += async (_, _) =>
        {
            UpdateNetworkStats();
            _timer.Start();
            await InitializeProfilesAsync();
            _networkHealthTimer.Start();
            await PollNetworkHealthAsync();
            _profileTimer.Tick += (_, _) => RefreshSelectedGameStatus();
            _profileTimer.Start();
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            _profileTimer.Stop();
            _connectionMonitorTimer.Stop();
            _networkHealthTimer.Stop();
            _networkHealthCts?.Cancel();
            _networkHealthCts?.Dispose();
            _routeAnalysisCts?.Cancel();
            _routeAnalysisCts?.Dispose();
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
            // DNS benchmarks use DoH only. No plaintext DNS query is sent for the test hostname.
            // ICMP is independent of DNS and may be filtered by the network.
            var pingTask = NetworkDiagnostics.MeasurePingAsync("1.1.1.1", 10, 1200, token);
            var resolverTask = DnsResolverComparison.CompareAsync("example.com", timeoutMs: 4000, cancellationToken: token);
            await Task.WhenAll(pingTask, resolverTask);

            var ping = await pingTask;
            var resolverResults = await resolverTask;
            _lastDnsResolverResults = resolverResults;

            PingText.Text = double.IsNaN(ping.AverageLatencyMs)
                ? "延迟    超时 / 无响应"
                : $"延迟    {ping.AverageLatencyMs:0.0} ms";
            JitterText.Text = double.IsNaN(ping.JitterMs)
                ? "抖动    -- ms"
                : $"抖动    {ping.JitterMs:0.0} ms";
            LossText.Text = $"丢包    {ping.PacketLossPercent:0.0}%";
            PingDetailText.Text = $"目标 {ping.Host} · {ping.Received}/{ping.Sent} 成功 · DNS 测速使用 DoH/TLS";

            DnsComparisonList.Items.Clear();
            foreach (var result in resolverResults)
                DnsComparisonList.Items.Add(result.ToDisplayString());
            var fastest = resolverResults.FirstOrDefault(result => result.Succeeded);
            if (fastest is not null)
                DnsComparisonList.Items.Insert(0, $"最快加密 DNS：{fastest.Provider} · {fastest.DurationMs:0.0} ms");
            else
                DnsComparisonList.Items.Insert(0, "没有可用的加密 DNS 响应；未降级到明文 DNS。");

            if (GameProfileCombo.SelectedItem is GameProfile selectedProfile)
            {
                GameProfileService.AddSample(selectedProfile, new GameQualitySample
                {
                    AverageLatencyMs = double.IsNaN(ping.AverageLatencyMs) ? null : ping.AverageLatencyMs,
                    JitterMs = double.IsNaN(ping.JitterMs) ? null : ping.JitterMs,
                    PacketLossPercent = ping.PacketLossPercent,
                    DnsResolutionMs = fastest?.DurationMs ?? double.NaN
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

    private async Task PollNetworkHealthAsync()
    {
        if (_networkHealthPollRunning)
            return;

        _networkHealthPollRunning = true;
        _networkHealthCts?.Cancel();
        _networkHealthCts?.Dispose();
        _networkHealthCts = new CancellationTokenSource();
        var token = _networkHealthCts.Token;

        try
        {
            var sample = await _networkHealthMonitor.SampleAsync(token);
            NetworkHealthStatus.Text = $"{sample.Status} · {sample.Detail}";
            NetworkHealthStatus.Foreground = sample.Status switch
            {
                "网络探测正常" => new SolidColorBrush(Color.FromRgb(101, 230, 194)),
                "网络质量波动" => new SolidColorBrush(Color.FromRgb(255, 196, 92)),
                _ => new SolidColorBrush(Color.FromRgb(255, 128, 128))
            };

            if (_networkHealthMonitor.HasStatusChanged(sample))
            {
                NetworkHealthEventsList.Items.Insert(0,
                    $"{sample.TimestampUtc.ToLocalTime():HH:mm:ss}  状态变化 → {sample.Status}");
                while (NetworkHealthEventsList.Items.Count > 30)
                    NetworkHealthEventsList.Items.RemoveAt(NetworkHealthEventsList.Items.Count - 1);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            NetworkHealthStatus.Text = $"网络监测错误：{ex.Message}";
            Debug.WriteLine(ex);
        }
        finally
        {
            _networkHealthPollRunning = false;
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
        {
            if (GameProfileCombo.SelectedItem is GameProfile selected &&
                !string.Equals(_monitoredGameName, selected.Name, StringComparison.OrdinalIgnoreCase))
            {
                _connectionChangeTracker.Reset();
                _monitoredGameName = selected.Name;
                _monitoredProcessName = selected.ProcessName;
                ConnectionChangesList.Items.Clear();
                ConnectionChangeStatus.Text = "已切换游戏档案；连接变化记录已重置";
            }
            RefreshSelectedGameStatus();
        }
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


    private async void MonitorGameConnectionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_connectionMonitoring)
        {
            if (GameProfileCombo.SelectedItem is not GameProfile profile)
            {
                ConnectionChangeStatus.Text = "请先选择游戏档案";
                return;
            }

            _connectionChangeTracker.Reset();
            _monitoredProcessName = profile.ProcessName;
            _monitoredGameName = profile.Name;
            _connectionMonitoring = true;
            MonitorGameConnectionsButton.Content = "停止监控";
            ConnectionChangeStatus.Text = "正在建立基线快照（每 3 秒检查一次）…";
            _connectionMonitorTimer.Start();
            await PollGameConnectionsAsync();
            return;
        }

        _connectionMonitoring = false;
        _connectionMonitorTimer.Stop();
        MonitorGameConnectionsButton.Content = "监控变化";
        ConnectionChangeStatus.Text = "连接变化监控已停止；已记录的变化仍可导出。";
    }

    private async Task PollGameConnectionsAsync()
    {
        if (!_connectionMonitoring || _connectionPollRunning)
            return;

        if (GameProfileCombo.SelectedItem is not GameProfile profile)
        {
            _connectionMonitoring = false;
            _connectionMonitorTimer.Stop();
            MonitorGameConnectionsButton.Content = "监控变化";
            ConnectionChangeStatus.Text = "已停止：没有选中的游戏档案";
            return;
        }

        _connectionPollRunning = true;
        try
        {
            if (!string.Equals(_monitoredProcessName, profile.ProcessName, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(_monitoredGameName, profile.Name, StringComparison.OrdinalIgnoreCase))
            {
                _connectionChangeTracker.Reset();
                _monitoredProcessName = profile.ProcessName;
                _monitoredGameName = profile.Name;
                ConnectionChangesList.Items.Clear();
            }

            var snapshot = await GameConnectionMonitor.GetConnectionsForProcessAsync(profile.ProcessName);
            var changes = _connectionChangeTracker.Update(snapshot);
            foreach (var change in changes)
                ConnectionChangesList.Items.Insert(0, change.ToDisplayString());

            while (ConnectionChangesList.Items.Count > 100)
                ConnectionChangesList.Items.RemoveAt(ConnectionChangesList.Items.Count - 1);

            var total = _connectionChangeTracker.GetRecentChanges(1000).Count;
            ConnectionChangeStatus.Text = $"监控中 · {snapshot.Count} 个当前端点 · 已记录 {total} 次变化 · 3 秒采样";
        }
        catch (Exception ex)
        {
            ConnectionChangeStatus.Text = $"连接监控错误：{ex.Message}";
            Debug.WriteLine(ex);
        }
        finally
        {
            _connectionPollRunning = false;
        }
    }

    private async void ExportDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        if (GameProfileCombo.SelectedItem is not GameProfile profile)
        {
            ConnectionChangeStatus.Text = "请先选择游戏档案，再导出诊断";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "导出游戏网络诊断",
            FileName = $"NetSpeed-{SanitizeFileName(profile.Name)}-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            Filter = "CSV 文件 (*.csv)|*.csv",
            DefaultExt = ".csv",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var builder = new StringBuilder();
            builder.AppendLine("RecordType,TimestampLocal,Game,Protocol,LocalEndpoint,RemoteEndpoint,State,Metric,Value,Details");

            foreach (var change in _connectionChangeTracker.GetRecentChanges(1000).Reverse())
            {
                builder.AppendLine(string.Join(",", new[]
                {
                    Csv("ConnectionChange"), Csv(change.TimestampUtc.ToLocalTime().ToString("O")),
                    Csv(profile.Name), Csv(change.Endpoint.Protocol), Csv(change.Endpoint.LocalAddress),
                    Csv(change.Endpoint.RemoteAddress), Csv(change.Endpoint.State), Csv(change.ChangeType),
                    Csv(change.Endpoint.ProcessId.ToString()), Csv("Observed from Windows netstat snapshot")
                }));
            }

            var routeHistory = await _routeHistoryService.LoadRecentAsync(profile.Name, 50);
            foreach (var entry in routeHistory.OrderBy(entry => entry.TimestampUtc))
            {
                builder.AppendLine(string.Join(",", new[]
                {
                    Csv("RouteQuality"), Csv(entry.TimestampUtc.ToLocalTime().ToString("O")),
                    Csv(entry.GameName), Csv(""), Csv(""), Csv(entry.RemoteAddress), Csv(""),
                    Csv("ICMP latency ms"), Csv(entry.IcmpLatencyMs?.ToString("0.0") ?? ""),
                    Csv($"Jitter={entry.JitterMs:0.0}; Loss={entry.PacketLossPercent:0.0}%; Score={entry.QualityScore:0.0}; TCP={entry.TcpConnectLatencyMs?.ToString("0.0") ?? "n/a"}")
                }));
            }

            foreach (var result in _lastDnsResolverResults)
            {
                builder.AppendLine(string.Join(",", new[]
                {
                    Csv("DnsResolver"), Csv(DateTime.Now.ToString("O")), Csv(profile.Name),
                    Csv(result.Region), Csv(result.Provider), Csv("DoH/HTTPS TLS"), Csv(result.Succeeded ? "Success" : "Failed"),
                    Csv("ResolutionMs"), Csv(result.DurationMs.ToString("0.0")),
                    Csv($"Host={result.Hostname}; Addresses={string.Join(";", result.Addresses)}; Error={result.Error ?? ""}")
                }));
            }

            foreach (var sample in _networkHealthMonitor.History)
            {
                builder.AppendLine(string.Join(",", new[]
                {
                    Csv("NetworkHealth"), Csv(sample.TimestampUtc.ToLocalTime().ToString("O")),
                    Csv(profile.Name), Csv("ICMP"), Csv(""), Csv("1.1.1.1"), Csv(sample.Status),
                    Csv("AverageLatencyMs"), Csv(sample.AverageLatencyMs?.ToString("0.0") ?? ""),
                    Csv($"PacketLoss={sample.PacketLossPercent:0.0}%; {sample.Detail}")
                }));
            }

            await File.WriteAllTextAsync(dialog.FileName, builder.ToString(), new UTF8Encoding(true));
            ConnectionChangeStatus.Text = $"诊断 CSV 已导出：{dialog.FileName}";
        }
        catch (Exception ex)
        {
            ConnectionChangeStatus.Text = $"导出失败：{ex.Message}";
            Debug.WriteLine(ex);
        }
    }

    private static string Csv(string? value)
    {
        var text = value ?? "";
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "Game" : safe;
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
        // A trend line must compare repeated samples for one remote IP, not mix unrelated servers.
        var chartSeries = history
            .Where(entry => double.IsFinite(entry.QualityScore))
            .GroupBy(entry => entry.RemoteAddress, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Max(entry => entry.TimestampUtc))
            .FirstOrDefault();
        var scores = chartSeries?
            .OrderBy(entry => entry.TimestampUtc)
            .Select(entry => entry.QualityScore)
            .ToArray() ?? [];
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


    private void ControlCenterNavButton_Click(object sender, RoutedEventArgs e)
    {
        PingText.BringIntoView();
        PingDetailText.Text = "控制中心：本机流量、网络质量与游戏连接监测";
    }

    private void GameNavButton_Click(object sender, RoutedEventArgs e)
    {
        GameProfileCombo.BringIntoView();
        GameProfileCombo.Focus();
        if (GameProfileCombo.SelectedItem is GameProfile)
            AnalyzeGameRoutesButton_Click(sender, e);
        else
            GameConnectionStatus.Text = "先添加游戏档案：填写游戏名称和 EXE 进程名，再扫描连接与分析线路。";
    }

    private void NodesNavButton_Click(object sender, RoutedEventArgs e)
    {
        ShowNodeProbeWindow();
    }

    private void DnsNavButton_Click(object sender, RoutedEventArgs e)
    {
        DnsComparisonList.BringIntoView();
        RunDiagnosticsButton_Click(sender, e);
    }

    private void SecurityNavButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "NetSpeed 当前提供网络连接观测和 DoH/TLS 诊断，不是杀毒软件，也没有启用 VPN Kill Switch。\n\n是否打开 Windows 安全中心，查看 Defender 与防火墙的系统状态？",
            "安全防护", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (result == MessageBoxResult.Yes)
        {
            try { Process.Start(new ProcessStartInfo("windowsdefender:") { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show($"无法打开 Windows 安全中心：{ex.Message}"); }
        }
    }

    private void SettingsNavButton_Click(object sender, RoutedEventArgs e)
    {
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = "实时采样间隔（秒）", Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 8) });
        var intervalInput = new TextBox { Text = Math.Clamp((int)_timer.Interval.TotalSeconds, 1, 10).ToString(), Width = 100, Padding = new Thickness(8), Background = new System.Windows.Media.SolidColorBrush(Color.FromRgb(20,40,58)), Foreground = Brushes.White };
        panel.Children.Add(intervalInput);
        panel.Children.Add(new TextBlock { Text = "设置范围：1–10 秒。修改后立即生效。", Foreground = Brushes.LightGray, Margin = new Thickness(0, 8, 0, 0) });
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var save = new Button { Content = "应用", Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "取消", Padding = new Thickness(16, 7, 16, 7), IsCancel = true };
        buttons.Children.Add(save); buttons.Children.Add(cancel); panel.Children.Add(buttons);
        var dialog = new Window { Title = "NetSpeed 设置", Content = panel, Width = 360, Height = 210, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, Background = new System.Windows.Media.SolidColorBrush(Color.FromRgb(11,19,34)) };
        save.Click += (_, _) =>
        {
            if (!int.TryParse(intervalInput.Text, out var seconds) || seconds is < 1 or > 10)
            {
                MessageBox.Show(dialog, "请输入 1 到 10 之间的整数。", "设置无效", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _timer.Interval = TimeSpan.FromSeconds(seconds);
            dialog.DialogResult = true;
            dialog.Close();
        };
        dialog.ShowDialog();
    }

    private void ShowNodeProbeWindow()
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "输入你自己的节点地址（主机名或 IP:端口），进行 TCP 可达性测试。此测试不建立 VPN 隧道。", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, Margin = new Thickness(0,0,0,10) });
        var endpointInput = new TextBox { Text = "host.example.com:443", Padding = new Thickness(8), Margin = new Thickness(0,0,0,10) };
        panel.Children.Add(endpointInput);
        var output = new ListBox { Height = 170, Background = new System.Windows.Media.SolidColorBrush(Color.FromRgb(7,11,22)), Foreground = Brushes.White };
        panel.Children.Add(output);
        var probe = new Button { Content = "测试节点 TCP 延迟", Padding = new Thickness(12,8,12,8), HorizontalAlignment = HorizontalAlignment.Right };
        panel.Children.Add(probe);
        var dialog = new Window { Title = "节点网络检测", Content = panel, Width = 520, Height = 330, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, Background = new System.Windows.Media.SolidColorBrush(Color.FromRgb(11,19,34)) };
        probe.Click += async (_, _) =>
        {
            var raw = endpointInput.Text.Trim();
            string host; int port;
            if (raw.StartsWith("[", StringComparison.Ordinal) && raw.Contains("]:"))
            {
                var split = raw.IndexOf("]:", StringComparison.Ordinal);
                host = raw[1..split];
                if (!int.TryParse(raw[(split+2)..], out port)) port = -1;
            }
            else
            {
                var split = raw.LastIndexOf(':');
                if (split <= 0 || !int.TryParse(raw[(split+1)..], out port)) { output.Items.Insert(0, "格式错误，请使用 host:port 或 [IPv6]:port"); return; }
                host = raw[..split];
            }
            if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535) { output.Items.Insert(0, "主机名或端口无效。"); return; }
            probe.IsEnabled = false; probe.Content = "测试中…";
            var watch = Stopwatch.StartNew();
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(host, port).WaitAsync(TimeSpan.FromSeconds(4));
                watch.Stop();
                output.Items.Insert(0, $"可连接 · {host}:{port} · TCP {watch.Elapsed.TotalMilliseconds:0.0} ms");
            }
            catch (Exception ex) { output.Items.Insert(0, $"不可连接 · {host}:{port} · {ex.GetType().Name}: {ex.Message}"); }
            finally { probe.IsEnabled = true; probe.Content = "测试节点 TCP 延迟"; }
        };
        dialog.ShowDialog();
    }

    // Dashboard feature cards intentionally expose only capabilities that are implemented.
    // VPN/CDN remain unavailable until real relay infrastructure and a tunnel backend are configured.
    private void VpnFeatureButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 WireGuard 配置文件（仅验证，不会连接）",
            Filter = "WireGuard 配置 (*.conf)|*.conf|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var config = File.ReadAllText(dialog.FileName);
            var hasInterface = config.Contains("[Interface]", StringComparison.OrdinalIgnoreCase);
            var hasPeer = config.Contains("[Peer]", StringComparison.OrdinalIgnoreCase);
            var hasPrivateKey = config.Split('\n').Any(line => line.TrimStart().StartsWith("PrivateKey", StringComparison.OrdinalIgnoreCase));
            var hasPublicKey = config.Split('\n').Any(line => line.TrimStart().StartsWith("PublicKey", StringComparison.OrdinalIgnoreCase));
            var hasEndpoint = config.Split('\n').Any(line => line.TrimStart().StartsWith("Endpoint", StringComparison.OrdinalIgnoreCase));
            var valid = hasInterface && hasPeer && hasPrivateKey && hasPublicKey && hasEndpoint;
            MessageBox.Show(
                valid
                    ? "配置文件结构检查通过：已找到 Interface、Peer、PrivateKey、PublicKey 和 Endpoint。\n\n注意：本版本尚未集成 WireGuard 隧道驱动/服务，因此这里只完成本地格式检查，不会建立 VPN 连接，也不会上传或保存你的密钥。"
                    : "配置文件不完整。需要包含 [Interface]、PrivateKey、[Peer]、PublicKey 和 Endpoint。\n\n文件内容不会上传。",
                "WireGuard 配置检查", MessageBoxButton.OK,
                valid ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法读取配置文件：{ex.Message}", "配置检查失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CdnFeatureButton_Click(object sender, RoutedEventArgs e)
    {
        ShowNodeProbeWindow();
    }

    private void DnsFeatureButton_Click(object sender, RoutedEventArgs e)
    {
        RunDiagnosticsButton_Click(sender, e);
    }

    private void GameFeatureButton_Click(object sender, RoutedEventArgs e)
    {
        if (GameProfileCombo.SelectedItem is not GameProfile)
        {
            MessageBox.Show("请先在游戏档案栏添加或选择游戏，再进行线路分析。",
                "游戏加速诊断", MessageBoxButton.OK, MessageBoxImage.Information);
            GameProfileCombo.Focus();
            return;
        }

        AnalyzeGameRoutesButton_Click(sender, e);
    }

    private void SecurityFeatureButton_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "当前提供的是网络诊断与连接观测，不是杀毒软件。\n\n本程序不会擅自修改 Windows 防火墙、DNS 或路由，也尚未启用 VPN Kill Switch。相关保护功能必须在真实隧道后端接入并完成回滚测试后才能启用。",
            "安全防护 · 当前能力说明", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
