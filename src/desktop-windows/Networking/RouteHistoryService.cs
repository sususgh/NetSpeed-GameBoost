using System.Text.Json;
using System.IO;

namespace NetSpeed.GameBoost.Desktop.Networking;

public sealed record RouteHistoryEntry(
    DateTime TimestampUtc,
    string GameName,
    string RemoteAddress,
    double? IcmpLatencyMs,
    double? JitterMs,
    double PacketLossPercent,
    double QualityScore,
    double? TcpConnectLatencyMs,
    bool? TcpConnected);

/// <summary>Stores a bounded diagnostic history locally; no telemetry is uploaded.</summary>
public sealed class RouteHistoryService
{
    private const int MaximumEntries = 300;
    private static readonly string HistoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NetSpeed", "GameBoost", "route-history.json");

    public async Task<IReadOnlyList<RouteHistoryEntry>> LoadRecentAsync(
        string gameName, int count = 12, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(HistoryPath))
            return [];

        try
        {
            await using var stream = File.OpenRead(HistoryPath);
            var entries = await JsonSerializer.DeserializeAsync<List<RouteHistoryEntry>>(
                stream, cancellationToken: cancellationToken) ?? [];
            return entries
                .Where(entry => string.Equals(entry.GameName, gameName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(entry => entry.TimestampUtc)
                .Take(Math.Clamp(count, 1, 50))
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    public async Task SaveAsync(
        IEnumerable<RouteHistoryEntry> newEntries,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(HistoryPath)!);
        var existing = new List<RouteHistoryEntry>();
        if (File.Exists(HistoryPath))
        {
            try
            {
                await using var input = File.OpenRead(HistoryPath);
                existing = await JsonSerializer.DeserializeAsync<List<RouteHistoryEntry>>(
                    input, cancellationToken: cancellationToken) ?? [];
            }
            catch (JsonException) { }
            catch (IOException) { }
        }

        existing.AddRange(newEntries);
        var bounded = existing
            .OrderByDescending(entry => entry.TimestampUtc)
            .Take(MaximumEntries)
            .ToList();

        var temporaryPath = HistoryPath + ".tmp";
        await using (var output = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(output, bounded, cancellationToken: cancellationToken);
        }
        File.Move(temporaryPath, HistoryPath, overwrite: true);
    }
}
