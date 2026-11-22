using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace NetSpeed.GameBoost.Desktop.Networking;

public sealed record TcpEndpointProbeResult(
    string RemoteEndpoint,
    bool Connected,
    double? ConnectLatencyMs,
    string Detail)
{
    public string ToDisplayString() => Connected
        ? $"{RemoteEndpoint,-42} TCP 建连 {ConnectLatencyMs:0.0} ms"
        : $"{RemoteEndpoint,-42} TCP 不通：{Detail}";
}

/// <summary>
/// Performs a bounded TCP connect check only against TCP endpoints already observed
/// for the selected game. It never scans arbitrary ports and does not send application data.
/// </summary>
public static class TcpEndpointProbe
{
    public static async Task<IReadOnlyList<TcpEndpointProbeResult>> ProbeObservedAsync(
        IEnumerable<GameConnectionEndpoint> endpoints,
        int maxTargets = 5,
        int timeoutMs = 1500,
        CancellationToken cancellationToken = default)
    {
        if (maxTargets is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(maxTargets));
        if (timeoutMs is < 100 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(timeoutMs));

        var targets = endpoints
            .Where(endpoint => endpoint.Protocol.Equals("TCP", StringComparison.OrdinalIgnoreCase))
            .Select(endpoint => TryParseEndpoint(endpoint.RemoteAddress, out var address, out var port)
                ? (Address: address, Port: port, Text: endpoint.RemoteAddress)
                : default)
            .Where(target => target.Address is not null)
            .DistinctBy(target => (target.Address!.ToString(), target.Port))
            .Take(maxTargets)
            .ToArray();

        var results = new List<TcpEndpointProbeResult>(targets.Length);
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(timeoutMs);
            using var client = new TcpClient(target.Address!.AddressFamily);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                await client.ConnectAsync(target.Address, target.Port, timeout.Token);
                stopwatch.Stop();
                results.Add(new TcpEndpointProbeResult(
                    target.Text, true, stopwatch.Elapsed.TotalMilliseconds, "连接成功"));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                results.Add(new TcpEndpointProbeResult(target.Text, false, null, $"超时（{timeoutMs} ms）"));
            }
            catch (SocketException ex)
            {
                results.Add(new TcpEndpointProbeResult(target.Text, false, null, ex.SocketErrorCode.ToString()));
            }
        }

        return results;
    }

    private static bool TryParseEndpoint(string endpoint, out IPAddress address, out int port)
    {
        address = IPAddress.None;
        port = 0;
        if (string.IsNullOrWhiteSpace(endpoint) || endpoint == "*:*")
            return false;

        var value = endpoint.Trim();
        string host;
        string portText;
        if (value.StartsWith("[", StringComparison.Ordinal))
        {
            var closing = value.IndexOf(']');
            if (closing <= 1 || closing + 2 > value.Length || value[closing + 1] != ':')
                return false;
            host = value[1..closing];
            portText = value[(closing + 2)..];
        }
        else
        {
            var separator = value.LastIndexOf(':');
            if (separator <= 0 || value.IndexOf(':') != separator)
                return false;
            host = value[..separator];
            portText = value[(separator + 1)..];
        }

        return IPAddress.TryParse(host, out address!) &&
               int.TryParse(portText, out port) &&
               port is > 0 and <= 65535 &&
               !IPAddress.IsLoopback(address) &&
               !address.IsIPv6Multicast;
    }
}
