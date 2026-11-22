using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Security.Cryptography;

namespace NetSpeed.GameBoost.Desktop.Diagnostics;

public sealed record DnsServerResult(
    string Server,
    string Hostname,
    double DurationMs,
    IReadOnlyList<IPAddress> Addresses,
    string? Error)
{
    public bool Succeeded => Error is null && Addresses.Count > 0;

    public string ToDisplayString() => Succeeded
        ? $"{Server,-16} {DurationMs,7:0.0} ms  {Addresses.Count} 个地址"
        : $"{Server,-16} 失败：{Error ?? "无地址记录"}";
}

/// <summary>
/// Sends a single bounded DNS A-record query to an explicitly selected resolver.
/// DNS traffic is ordinary unencrypted UDP/53; no system DNS settings are changed.
/// </summary>
public static class DnsResolverComparison
{
    private static readonly string[] DefaultResolvers = ["1.1.1.1", "8.8.8.8", "9.9.9.9"];

    public static async Task<IReadOnlyList<DnsServerResult>> CompareAsync(
        string hostname,
        IEnumerable<string>? resolverAddresses = null,
        int timeoutMs = 2500,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hostname))
            throw new ArgumentException("Hostname is required.", nameof(hostname));
        if (timeoutMs is < 250 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(timeoutMs));

        var resolvers = (resolverAddresses ?? DefaultResolvers)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(value => IPAddress.TryParse(value, out var address) ? address : null)
            .Where(address => address is not null && address.AddressFamily == AddressFamily.InterNetwork)
            .Cast<IPAddress>()
            .Take(5)
            .ToArray();

        var results = new List<DnsServerResult>(resolvers.Length);
        foreach (var resolver in resolvers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var timer = Stopwatch.StartNew();
            try
            {
                var addresses = await QueryAAsync(hostname, resolver, timeoutMs, cancellationToken);
                timer.Stop();
                results.Add(new DnsServerResult(
                    resolver.ToString(), hostname, timer.Elapsed.TotalMilliseconds, addresses, null));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                timer.Stop();
                results.Add(new DnsServerResult(resolver.ToString(), hostname,
                    timer.Elapsed.TotalMilliseconds, [], $"超时（{timeoutMs} ms）"));
            }
            catch (Exception ex) when (ex is SocketException or IOException or FormatException or InvalidDataException)
            {
                timer.Stop();
                results.Add(new DnsServerResult(resolver.ToString(), hostname,
                    timer.Elapsed.TotalMilliseconds, [], ex.Message));
            }
        }

        return results.OrderBy(result => result.Succeeded ? 0 : 1)
            .ThenBy(result => result.DurationMs)
            .ToArray();
    }

    private static async Task<IReadOnlyList<IPAddress>> QueryAAsync(
        string hostname, IPAddress resolver, int timeoutMs, CancellationToken cancellationToken)
    {
        var query = BuildQuery(hostname, out var transactionId);
        using var client = new UdpClient(AddressFamily.InterNetwork);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMs);

        await client.SendAsync(query, new IPEndPoint(resolver, 53), timeout.Token);
        var response = await client.ReceiveAsync(timeout.Token);
        if (!response.RemoteEndPoint.Address.Equals(resolver) || response.RemoteEndPoint.Port != 53)
            throw new InvalidDataException("DNS 响应来源地址不匹配");
        return ParseResponse(response.Buffer, transactionId);
    }

    private static byte[] BuildQuery(string hostname, out ushort transactionId)
    {
        var labels = hostname.TrimEnd('.').Split('.');
        if (labels.Length == 0 || labels.Any(label => label.Length is 0 or > 63))
            throw new FormatException("域名格式无效");

        transactionId = (ushort)RandomNumberGenerator.GetInt32(1, ushort.MaxValue);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)(transactionId >> 8));
        writer.Write((byte)transactionId);
        writer.Write((byte)0x01); writer.Write((byte)0x00); // recursion desired
        writer.Write((byte)0x00); writer.Write((byte)0x01); // QDCOUNT
        writer.Write(new byte[6]); // ANCOUNT, NSCOUNT, ARCOUNT

        foreach (var label in labels)
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            if (bytes.Length == 0 || bytes.Length > 63 || bytes.Any(value => value > 127))
                throw new FormatException("目前只支持 ASCII / punycode 域名");
            writer.Write((byte)bytes.Length);
            writer.Write(bytes);
        }
        writer.Write((byte)0);
        writer.Write((byte)0); writer.Write((byte)1); // A
        writer.Write((byte)0); writer.Write((byte)1); // IN
        return stream.ToArray();
    }

    private static IReadOnlyList<IPAddress> ParseResponse(byte[] packet, ushort expectedId)
    {
        if (packet.Length < 12 || BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(0, 2)) != expectedId)
            throw new InvalidDataException("DNS 响应头无效");

        var flags = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(2, 2));
        if ((flags & 0x8000) == 0)
            throw new InvalidDataException("收到的不是 DNS 响应");
        if ((flags & 0x0200) != 0)
            throw new InvalidDataException("DNS 响应被截断（TC）");
        var responseCode = flags & 0x000F;
        if (responseCode != 0)
            throw new InvalidDataException($"DNS RCODE={responseCode}");

        var questionCount = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(4, 2));
        var answerCount = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(6, 2));
        var offset = 12;
        for (var i = 0; i < questionCount; i++)
        {
            SkipName(packet, ref offset);
            Ensure(packet, offset, 4);
            offset += 4;
        }

        var addresses = new List<IPAddress>();
        for (var i = 0; i < answerCount; i++)
        {
            SkipName(packet, ref offset);
            Ensure(packet, offset, 10);
            var type = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(offset, 2));
            var recordClass = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(offset + 2, 2));
            var dataLength = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(offset + 8, 2));
            offset += 10;
            Ensure(packet, offset, dataLength);

            if (recordClass == 1 && type == 1 && dataLength == 4)
                addresses.Add(new IPAddress(packet.AsSpan(offset, 4)));
            offset += dataLength;
        }

        return addresses.Distinct().ToArray();
    }

    private static void SkipName(byte[] packet, ref int offset)
    {
        var cursor = offset;
        var jumped = false;
        var jumps = 0;
        while (true)
        {
            Ensure(packet, cursor, 1);
            var length = packet[cursor++];
            if (length == 0)
            {
                if (!jumped) offset = cursor;
                return;
            }
            if ((length & 0xC0) == 0xC0)
            {
                Ensure(packet, cursor, 1);
                var pointer = ((length & 0x3F) << 8) | packet[cursor++];
                if (pointer >= packet.Length || ++jumps > 16)
                    throw new InvalidDataException("DNS 名称压缩指针无效");
                if (!jumped) offset = cursor;
                cursor = pointer;
                jumped = true;
                continue;
            }
            if ((length & 0xC0) != 0 || length > 63)
                throw new InvalidDataException("DNS 名称标签无效");
            Ensure(packet, cursor, length);
            cursor += length;
            if (!jumped) offset = cursor;
        }
    }

    private static void Ensure(byte[] packet, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > packet.Length - count)
            throw new InvalidDataException("DNS 响应数据不完整");
    }
}
