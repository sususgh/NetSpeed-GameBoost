using System.Net;
using NetSpeed.GameBoost.Desktop.Diagnostics;

namespace NetSpeed.GameBoost.Desktop.Networking;

public sealed record GameRouteQuality(
    string RemoteAddress,
    int SampleCount,
    int Received,
    double AverageLatencyMs,
    double JitterMs,
    double PacketLossPercent,
    double QualityScore)
{
    public string ToDisplayString() =>
        double.IsNaN(AverageLatencyMs)
            ? $"{RemoteAddress,-42}  无响应  丢包 {PacketLossPercent:0}%"
            : $"{RemoteAddress,-42}  {AverageLatencyMs,6:0.0} ms  抖动 {JitterMs,5:0.0} ms  丢包 {PacketLossPercent,5:0.0}%  评分 {QualityScore,5:0.0}";
}

/// <summary>
/// Measures a small, bounded set of IP endpoints observed for a selected game.
/// Scores are diagnostic estimates, not guaranteed route performance or game-server latency.
/// </summary>
public static class GameRouteAnalyzer
{
    public static async Task<IReadOnlyList<GameRouteQuality>> AnalyzeAsync(
        IEnumerable<GameConnectionEndpoint> endpoints,
        int maxTargets = 5,
        CancellationToken cancellationToken = default)
    {
        if (maxTargets is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(maxTargets));

        var targets = endpoints
            .Select(endpoint => TryGetRemoteIp(endpoint.RemoteAddress, out var ip) ? ip : null)
            .Where(ip => ip is not null)
            .Cast<IPAddress>()
            .Where(IsUsableUnicast)
            .Distinct()
            .Take(maxTargets)
            .ToArray();

        var results = new List<GameRouteQuality>(targets.Length);
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ping = await NetworkDiagnostics.MeasurePingAsync(
                target.ToString(), sampleCount: 3, timeoutMs: 900, cancellationToken);
            var score = CalculateScore(ping.AverageLatencyMs, ping.JitterMs, ping.PacketLossPercent);

            results.Add(new GameRouteQuality(
                target.ToString(), ping.Sent, ping.Received,
                ping.AverageLatencyMs, ping.JitterMs, ping.PacketLossPercent, score));
        }

        return results.OrderByDescending(result => result.QualityScore).ToArray();
    }

    public static double CalculateScore(double latencyMs, double jitterMs, double packetLossPercent)
    {
        if (double.IsNaN(latencyMs) || double.IsInfinity(latencyMs) || packetLossPercent >= 100)
            return 0;

        var latencyPenalty = Math.Min(Math.Max(latencyMs, 0), 300) * 0.18;
        var jitterPenalty = double.IsFinite(jitterMs)
            ? Math.Min(Math.Max(jitterMs, 0), 100) * 0.25
            : 25;
        var lossPenalty = Math.Clamp(packetLossPercent, 0, 100) * 0.55;
        return Math.Round(Math.Clamp(100 - latencyPenalty - jitterPenalty - lossPenalty, 0, 100), 1);
    }

    private static bool TryGetRemoteIp(string endpoint, out IPAddress address)
    {
        address = IPAddress.None;
        if (string.IsNullOrWhiteSpace(endpoint) || endpoint == "*:*")
            return false;

        var value = endpoint.Trim();
        if (value.StartsWith("[", StringComparison.Ordinal))
        {
            var closingBracket = value.IndexOf(']');
            if (closingBracket <= 1)
                return false;
            value = value[1..closingBracket];
        }
        else
        {
            var lastColon = value.LastIndexOf(':');
            if (lastColon > 0 && value.IndexOf(':') == lastColon)
                value = value[..lastColon];
        }

        return IPAddress.TryParse(value, out address!);
    }

    private static bool IsUsableUnicast(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) ||
            address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any) ||
            address.IsIPv6Multicast)
            return false;

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] is not 0 and not 127 and not 224 and not 255;
        }

        return address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 &&
               !address.IsIPv6LinkLocal;
    }
}
