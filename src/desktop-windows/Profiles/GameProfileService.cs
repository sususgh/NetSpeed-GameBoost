using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace NetSpeed.GameBoost.Desktop.Profiles;

public sealed class GameProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public List<GameQualitySample> History { get; set; } = [];
}

public sealed class GameQualitySample
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public double? AverageLatencyMs { get; set; }
    public double? JitterMs { get; set; }
    public double? PacketLossPercent { get; set; }
    public double? DnsResolutionMs { get; set; }
}

public sealed class GameProfileService
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public GameProfileService()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetSpeed", "GameBoost");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "game-profiles.json");
    }

    public async Task<List<GameProfile>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
            return [];

        try
        {
            await using var stream = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync<List<GameProfile>>(
                stream, _jsonOptions, cancellationToken) ?? [];
        }
        catch (JsonException)
        {
            // Preserve a damaged file for manual recovery rather than overwriting it.
            var backup = _filePath + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            File.Move(_filePath, backup, overwrite: false);
            return [];
        }
    }

    public async Task SaveAsync(
        IReadOnlyCollection<GameProfile> profiles,
        CancellationToken cancellationToken = default)
    {
        var tempPath = _filePath + ".tmp";
        await using (var stream = new FileStream(
            tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
            4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, profiles, _jsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(tempPath, _filePath, overwrite: true);
    }

    public static bool IsValidProcessName(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName) || processName.Length > 128)
            return false;

        var name = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;

        return name.Length > 0 &&
               name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
               !name.Contains(Path.DirectorySeparatorChar) &&
               !name.Contains(Path.AltDirectorySeparatorChar);
    }

    public static bool IsRunning(string processName)
    {
        if (!IsValidProcessName(processName))
            return false;

        var name = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;

        try
        {
            return Process.GetProcessesByName(name).Length > 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static void AddSample(GameProfile profile, GameQualitySample sample, int maxSamples = 500)
    {
        profile.History.Add(sample);
        if (profile.History.Count > maxSamples)
            profile.History.RemoveRange(0, profile.History.Count - maxSamples);
    }
}
