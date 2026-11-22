using System.Buffers.Binary;
using System.Net.Http;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Security.Cryptography;
using System.IO;

namespace NetSpeed.GameBoost.Desktop.Diagnostics;

public sealed record DnsServerResult(
    string Provider,
    string Server,
    string Region,
    string Hostname,
    double DurationMs,
    IReadOnlyList<IPAddress> Addresses,
    string? Error)
{
    public bool Succeeded => Error is null && Addresses.Count > 0;

    public string ToDisplayString() => Succeeded
        ? $"{Region} {Provider,-10} {Server,-16} {DurationMs,7:0.0} ms  {Addresses.Count} 个地址"
        : $"{Region} {Provider,-10} {Server,-16} 失败：{Error ?? "无地址记录"}";
}

/// <summary>
/// Sends bounded DNS A-record queries using DNS-over-HTTPS (DoH).
/// TLS certificate validation remains enabled; no plaintext fallback or system DNS changes are made.
/// </summary>
public static class DnsResolverComparison
{
    private sealed record ResolverEndpoint(string Provider, string Address, string Region);

    private static readonly HttpClient DoHClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectTimeout = TimeSpan.FromSeconds(4),
        AllowAutoRedirect = false
    })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    // Public recursive DNS services. Mainland entries are listed first; addresses are not
    // assumed to be reachable from every ISP or region and are measured on the user's network.
    private static readonly ResolverEndpoint[] DefaultResolvers =
    [
        new("阿里云 DoH", "https://dns.alidns.com/dns-query", "中国大陆"),
        new("DNSPod DoH", "https://doh.pub/dns-query", "中国大陆"),
        new("Cloudflare DoH", "https://cloudflare-dns.com/dns-query", "国际"),
        new("Google DoH", "https://dns.google/dns-query", "国际"),
        new("Quad9 DoH", "https://dns.quad9.net/dns-query", "国际")
    ];

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

        var selected = resolverAddresses is null
            ? DefaultResolvers
            : resolverAddresses
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(value => new ResolverEndpoint("自定义 DoH", value, "自定义"))
                .ToArray();

        var resolvers = selected
            .Select(endpoint => (Endpoint: endpoint,
                Uri: Uri.TryCreate(endpoint.Address, UriKind.Absolute, out var uri) &&
                     uri.Scheme == Uri.UriSchemeHttps ? uri : null))
            .Where(item => item.Uri is not null)
            .Take(16)
            .ToArray();

        var tasks = resolvers.Select(async item =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var timer = Stopwatch.StartNew();
            try
            {
                var addresses = await QueryAAsync(hostname, item.Uri!, timeoutMs, cancellationToken);
                timer.Stop();
                return new DnsServerResult(item.Endpoint.Provider, item.Uri!.ToString(),
                    item.Endpoint.Region, hostname, timer.Elapsed.TotalMilliseconds, addresses, null);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                timer.Stop();
                return new DnsServerResult(item.Endpoint.Provider, item.Uri!.ToString(),
                    item.Endpoint.Region, hostname, timer.Elapsed.TotalMilliseconds, [],
                    $"超时（{timeoutMs} ms）");
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or FormatException or InvalidDataException)
            {
                timer.Stop();
                return new DnsServerResult(item.Endpoint.Provider, item.Uri!.ToString(),
                    item.Endpoint.Region, hostname, timer.Elapsed.TotalMilliseconds, [], ex.Message);
            }
        });

        var results = await Task.WhenAll(tasks);
        return results.OrderBy(result => result.Succeeded ? 0 : 1)
            .ThenBy(result => result.DurationMs)
            .ToArray();
    }

    private static async Task<IReadOnlyList<IPAddress>> QueryAAsync(
        string hostname, Uri endpoint, int timeoutMs, CancellationToken cancellationToken)
    {
        var query = BuildQuery(hostname, out var transactionId);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMs);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dns-message"));
        request.Content = new ByteArrayContent(query);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");

        // HTTPS enforces TLS certificate and hostname validation through the platform defaults.
        // No plaintext DNS fallback is attempted if the encrypted request fails.
        using var response = await DoHClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentType?.MediaType != "application/dns-message")
            throw new InvalidDataException("DoH 响应 Content-Type 无效");

        var packet = await response.Content.ReadAsByteArrayAsync(timeout.Token);
        return ParseResponse(packet, transactionId);
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
