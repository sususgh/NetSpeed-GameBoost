using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;

namespace NetSpeed.GameBoost.Desktop.Diagnostics;

public sealed record PingDiagnostics(
    string Host,
    int Sent,
    int Received,
    double PacketLossPercent,
    double AverageLatencyMs,
    double JitterMs,
    double MinLatencyMs,
    double MaxLatencyMs);

public sealed record DnsDiagnostics(
    string Hostname,
    string Resolver,
    double DurationMs,
    int AddressCount);

public static class NetworkDiagnostics
{
    public static async Task<PingDiagnostics> MeasurePingAsync(
        string host,
        int sampleCount = 10,
        int timeoutMs = 1200,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("Host is required.", nameof(host));
        if (sampleCount is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(sampleCount));

        using var ping = new Ping();
        var times = new List<double>(sampleCount);
        var sent = 0;

        for (var i = 0; i < sampleCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sent++;

            try
            {
                var reply = await ping.SendPingAsync(host, timeoutMs)
                    .WaitAsync(TimeSpan.FromMilliseconds(timeoutMs + 250), cancellationToken);
                if (reply.Status == IPStatus.Success)
                    times.Add(reply.RoundtripTime);
            }
            catch (PingException) { }
            catch (TimeoutException) { }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }

            if (i + 1 < sampleCount)
                await Task.Delay(180, cancellationToken);
        }

        var loss = sent == 0 ? 100d : (sent - times.Count) * 100d / sent;
        if (times.Count == 0)
            return new PingDiagnostics(host, sent, 0, loss, double.NaN, double.NaN, double.NaN, double.NaN);

        var average = times.Average();
        var jitter = times.Count < 2
            ? 0d
            : times.Zip(times.Skip(1), (a, b) => Math.Abs(b - a)).Average();

        return new PingDiagnostics(
            host, sent, times.Count, loss, average, jitter, times.Min(), times.Max());
    }

    // Measures the operating system's configured DNS resolver, not a hard-coded public DNS server.
    public static async Task<DnsDiagnostics> MeasureSystemDnsAsync(
        string hostname,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hostname))
            throw new ArgumentException("Hostname is required.", nameof(hostname));

        var timer = Stopwatch.StartNew();
        var addresses = await Dns.GetHostAddressesAsync(hostname)
            .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        timer.Stop();

        var resolver = string.Join(", ", Dns.GetHostName() == Environment.MachineName
            ? System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up)
                .SelectMany(n => n.GetIPProperties().DnsAddresses)
                .Select(a => a.ToString())
                .Distinct()
                .Take(2)
            : Array.Empty<string>());

        return new DnsDiagnostics(
            hostname,
            string.IsNullOrWhiteSpace(resolver) ? "系统配置的 DNS（未识别地址）" : resolver,
            timer.Elapsed.TotalMilliseconds,
            addresses.Length);
    }
}
