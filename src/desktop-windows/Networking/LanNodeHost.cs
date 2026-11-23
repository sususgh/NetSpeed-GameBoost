using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace NetSpeed.GameBoost.Desktop.Networking;

/// <summary>
/// A small read-only LAN node endpoint. It advertises node health to a phone
/// on the same Wi-Fi/LAN; it is not a SOCKS proxy or a game-traffic forwarder.
/// </summary>
public sealed class LanNodeHost : IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;
    private long _requests;

    public bool IsRunning => _listener is not null;
    public int Port { get; private set; }
    public long Requests => Interlocked.Read(ref _requests);

    public void Start(int port)
    {
        if (port is < 1024 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "端口必须在 1024–65535 之间。");
        if (IsRunning)
            throw new InvalidOperationException("局域网节点已经启动。");

        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start(32);
        _listener = listener;
        _cts = new CancellationTokenSource();
        Port = port;
        _acceptLoop = AcceptLoopAsync(listener, _cts.Token);
    }

    public async Task StopAsync()
    {
        var listener = _listener;
        var cts = _cts;
        _listener = null;
        _cts = null;
        listener?.Stop();
        if (cts is not null)
        {
            cts.Cancel();
            try
            {
                if (_acceptLoop is not null)
                    await _acceptLoop.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (TimeoutException) { }
            cts.Dispose();
        }
        _acceptLoop = null;
        Port = 0;
    }

    public static IReadOnlyList<string> GetLanIpv4Addresses()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up &&
                              adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
            .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork &&
                              !IPAddress.IsLoopback(address.Address))
            .Select(address => address.Address.ToString())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(address => address.StartsWith("192.168.", StringComparison.Ordinal) ? 0 : 1)
            .ToArray();
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(token);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) when (token.IsCancellationRequested) { break; }

            _ = HandleClientAsync(client, token);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 3000;
                client.SendTimeout = 3000;
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                var requestLine = await reader.ReadLineAsync(token);
                if (string.IsNullOrWhiteSpace(requestLine))
                    return;

                // Consume bounded HTTP headers; no request body is accepted.
                for (var i = 0; i < 40; i++)
                {
                    var line = await reader.ReadLineAsync(token);
                    if (string.IsNullOrEmpty(line)) break;
                }

                Interlocked.Increment(ref _requests);
                var path = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1) ?? "/";
                var body = path switch
                {
                    "/health" or "/healthz" =>
                        "{\"status\":\"ok\",\"service\":\"NetSpeed-GameBoost LAN node\",\"role\":\"read-only discovery endpoint\",\"gameTrafficForwarding\":false}",
                    "/metrics" =>
                        $"{{\"status\":\"ok\",\"requests\":{Requests},\"port\":{Port},\"gameTrafficForwarding\":false}}",
                    _ => "<!doctype html><html lang=\"zh-CN\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>NetSpeed LAN Node</title><body style=\"font:16px sans-serif;background:#101a2b;color:#eaf7ff;padding:24px\"><h2>NetSpeed-GameBoost 局域网节点</h2><p>电脑节点在线，手机已成功访问本机。</p><p>状态：正常</p><p>请求计数：" + Requests + "</p><p>这是节点发现与健康检测端点，不是游戏流量代理。</p><a style=\"color:#27d7f2\" href=\"/health\">健康状态 JSON</a> · <a style=\"color:#27d7f2\" href=\"/metrics\">指标 JSON</a></body></html>"
                };
                var contentType = path is "/health" or "/healthz" or "/metrics"
                    ? "application/json; charset=utf-8"
                    : "text/html; charset=utf-8";
                var bytes = Encoding.UTF8.GetBytes(body);
                var header = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 200 OK\r\nContent-Type: {contentType}\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n");
                await stream.WriteAsync(header, token);
                await stream.WriteAsync(bytes, token);
                await stream.FlushAsync(token);
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (SocketException) { }
        }
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }
}
