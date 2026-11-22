using System.Diagnostics;

namespace NetSpeed.GameBoost.Desktop.Networking;

public sealed record GameConnectionEndpoint(
    string Protocol,
    string LocalAddress,
    string RemoteAddress,
    string State,
    int ProcessId)
{
    public string ToDisplayString() =>
        $"{Protocol,-4}  {LocalAddress}  →  {RemoteAddress}  {State}";
}

/// <summary>
/// Reads the Windows netstat process/endpoint table. This is observational only:
/// it does not capture packets, change routes, or modify firewall settings.
/// </summary>
public static class GameConnectionMonitor
{
    public static async Task<IReadOnlyList<GameConnectionEndpoint>> GetConnectionsForProcessAsync(
        string processName,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidProcessName(processName))
            return [];

        var normalizedName = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;

        var processIds = new HashSet<int>();
        foreach (var process in Process.GetProcessesByName(normalizedName))
        {
            try { processIds.Add(process.Id); }
            catch (InvalidOperationException) { }
            finally { process.Dispose(); }
        }

        if (processIds.Count == 0)
            return [];

        var tcpTask = ReadNetstatAsync("tcp", cancellationToken);
        var udpTask = ReadNetstatAsync("udp", cancellationToken);
        await Task.WhenAll(tcpTask, udpTask);

        return tcpTask.Result.Concat(udpTask.Result)
            .Where(endpoint => processIds.Contains(endpoint.ProcessId))
            .OrderBy(endpoint => endpoint.Protocol)
            .ThenBy(endpoint => endpoint.RemoteAddress, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<List<GameConnectionEndpoint>> ReadNetstatAsync(
        string protocol,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "netstat.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.StartInfo.ArgumentList.Add("-ano");
        process.StartInfo.ArgumentList.Add("-p");
        process.StartInfo.ArgumentList.Add(protocol);

        if (!process.Start())
            return [];

        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = await outputTask;
            _ = await errorTask;

            if (process.ExitCode != 0)
                return [];

            var results = new List<GameConnectionEndpoint>();
            foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var columns = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (columns.Length < 4 ||
                    !columns[0].Equals(protocol, StringComparison.OrdinalIgnoreCase) ||
                    !int.TryParse(columns[^1], out var pid))
                    continue;

                var local = columns[1];
                var remote = columns[2];
                var state = protocol.Equals("tcp", StringComparison.OrdinalIgnoreCase) && columns.Length >= 5
                    ? columns[3]
                    : "端点（UDP 无连接状态）";

                results.Add(new GameConnectionEndpoint(
                    protocol.ToUpperInvariant(), local, remote, state, pid));
            }

            return results;
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            throw;
        }
    }

    private static bool IsValidProcessName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
            return false;

        var name = value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? value[..^4]
            : value;

        return name.Length > 0 &&
               !name.Contains(Path.DirectorySeparatorChar) &&
               !name.Contains(Path.AltDirectorySeparatorChar) &&
               name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }
}
