using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetSpeed.GameBoost.Desktop.Networking;

public sealed record RelayNode(string Id, string Name, string Host, int Port, string Transport, string Region,
    string? WireGuardPublicKey = null, string? Notes = null);
public sealed record RelayProbeResult(string NodeId, bool Reachable, double? TcpConnectLatencyMs,
    string Detail, DateTimeOffset TestedAtUtc);

/// <summary>Local relay catalogue and bounded TCP probe. Reachability is not proof of relay forwarding.</summary>
public sealed class RelayNodeCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    { WriteIndented = true, PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public IReadOnlyList<RelayNode> Load(string path)
    {
        if (!File.Exists(path)) return [];
        var nodes = JsonSerializer.Deserialize<List<RelayNode>>(File.ReadAllText(path), JsonOptions) ?? [];
        Validate(nodes);
        return nodes;
    }

    public void Save(string path, IEnumerable<RelayNode> nodes)
    {
        var list = nodes.ToList();
        Validate(list);
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temp = full + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(list, JsonOptions));
        File.Move(temp, full, true);
    }

    public async Task<RelayProbeResult> ProbeTcpAsync(RelayNode node, int timeoutMs = 2500,
        CancellationToken token = default)
    {
        Validate([node]);
        if (timeoutMs is < 100 or > 10000) throw new ArgumentOutOfRangeException(nameof(timeoutMs));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(timeoutMs);
        using var client = new TcpClient();
        var watch = Stopwatch.StartNew();
        try
        {
            await client.ConnectAsync(node.Host, node.Port, timeout.Token);
            watch.Stop();
            return new(node.Id, true, watch.Elapsed.TotalMilliseconds,
                "TCP 可达；尚未验证中继协议认证、UDP 转发或游戏流量路径。", DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { return new(node.Id, false, null, $"TCP 连接超时（{timeoutMs} ms）。", DateTimeOffset.UtcNow); }
        catch (SocketException ex)
        { return new(node.Id, false, null, $"TCP 连接失败：{ex.SocketErrorCode}", DateTimeOffset.UtcNow); }
    }

    private static void Validate(IReadOnlyCollection<RelayNode> nodes)
    {
        if (nodes.Count > 256) throw new InvalidDataException("节点目录最多允许 256 个节点。");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in nodes)
        {
            if (string.IsNullOrWhiteSpace(n.Id) || n.Id.Length > 64 || !ids.Add(n.Id))
                throw new InvalidDataException("节点 ID 为空、过长或重复。");
            if (string.IsNullOrWhiteSpace(n.Name) || n.Name.Length > 80 ||
                string.IsNullOrWhiteSpace(n.Host) || n.Host.Length > 253 ||
                n.Host.Contains('/') || n.Host.Contains('\\'))
                throw new InvalidDataException($"节点 {n.Id} 名称或主机名无效。");
            if (n.Port is < 1 or > 65535) throw new InvalidDataException($"节点 {n.Id} 端口无效。");
            if (string.IsNullOrWhiteSpace(n.Transport) ||
                !new[] { "TCP", "UDP", "WireGuard" }.Contains(n.Transport, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException($"节点 {n.Id} 传输类型无效。");
            if (n.Region?.Length > 80 || n.Notes?.Length > 500)
                throw new InvalidDataException($"节点 {n.Id} 的区域或备注过长。");
        }
    }
}
