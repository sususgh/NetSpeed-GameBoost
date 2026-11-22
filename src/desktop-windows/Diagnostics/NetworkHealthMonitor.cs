using System.Net.NetworkInformation;

namespace NetSpeed.GameBoost.Desktop.Diagnostics;

public sealed record NetworkHealthSample(
    DateTime TimestampUtc,
    bool HasActiveInterface,
    double? AverageLatencyMs,
    double PacketLossPercent,
    string Status,
    string Detail);

public sealed class NetworkHealthMonitor
{
    private readonly Queue<NetworkHealthSample> _history = new();
    private string? _previousStatus;

    public IReadOnlyList<NetworkHealthSample> History => _history.ToArray();

    public async Task<NetworkHealthSample> SampleAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var hasInterface = NetworkInterface.GetAllNetworkInterfaces().Any(adapter =>
            adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
            adapter.OperationalStatus == OperationalStatus.Up &&
            adapter.GetIPProperties().UnicastAddresses.Any(address =>
                address.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ||
                address.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6));

        if (!hasInterface)
            return Add(new NetworkHealthSample(DateTime.UtcNow, false, null, 100, "断网/无活动接口",
                "未发现已启用的非回环网络接口"));

        var ping = await NetworkDiagnostics.MeasurePingAsync(
            "1.1.1.1", sampleCount: 2, timeoutMs: 1000, cancellationToken);
        if (double.IsNaN(ping.AverageLatencyMs))
            return Add(new NetworkHealthSample(DateTime.UtcNow, true, null, ping.PacketLossPercent,
                "探测目标无响应", "本机接口存在，但 ICMP 目标无响应；可能是目标或网络屏蔽 ICMP"));

        var status = ping.PacketLossPercent >= 50 || ping.AverageLatencyMs >= 250
            ? "网络质量异常"
            : ping.PacketLossPercent > 0 || ping.AverageLatencyMs >= 150
                ? "网络质量波动"
                : "网络探测正常";
        return Add(new NetworkHealthSample(DateTime.UtcNow, true, ping.AverageLatencyMs,
            ping.PacketLossPercent, status,
            $"ICMP 1.1.1.1 · 平均 {ping.AverageLatencyMs:0.0} ms · 丢包 {ping.PacketLossPercent:0.0}%"));
    }

    public bool HasStatusChanged(NetworkHealthSample sample)
    {
        var changed = _previousStatus is not null &&
            !string.Equals(_previousStatus, sample.Status, StringComparison.Ordinal);
        _previousStatus = sample.Status;
        return changed;
    }

    private NetworkHealthSample Add(NetworkHealthSample sample)
    {
        _history.Enqueue(sample);
        while (_history.Count > 120)
            _history.Dequeue();
        return sample;
    }
}
