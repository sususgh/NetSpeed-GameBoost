using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Threading;

namespace NetSpeed.GameBoost.Desktop;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private long _lastReceived;
    private long _lastSent;
    private DateTime _lastSampleUtc;
    private bool _hasSample;

    public MainWindow()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => UpdateNetworkStats();
        Loaded += (_, _) =>
        {
            UpdateNetworkStats();
            _timer.Start();
        };
        Closed += (_, _) => _timer.Stop();
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
