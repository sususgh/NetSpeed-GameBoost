using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Threading;
using NetSpeed.GameBoost.Desktop.Diagnostics;

namespace NetSpeed.GameBoost.Desktop;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private long _lastReceived;
    private long _lastSent;
    private DateTime _lastSampleUtc;
    private bool _hasSample;
    private CancellationTokenSource? _diagnosticsCts;

    public MainWindow()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => UpdateNetworkStats();
        Loaded += (_, _) =>
        {
            UpdateNetworkStats();
            _timer.Start();
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
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
