using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;

namespace NetSpeed.GameBoost.Desktop.Networking;

public sealed record OpenRungRelay(
    string Id, string Name, string Location, string Host, int Port,
    string OperatorClass, long? ProbeLatencyMs, bool PunchCapable, DateTimeOffset ExpiresAt);

/// <summary>
/// Runs the upstream OpenRung client and its signed-directory discovery helper.
/// VPN shutdown closes stdin; the pinned upstream patch turns EOF into a graceful
/// engine stop so routes, DNS and the TUN device are cleaned up.
/// </summary>
public sealed class OpenRungClientManager : IDisposable
{
    private readonly string _directory = Path.Combine(AppContext.BaseDirectory, "OpenRung");
    private Process? _vpnProcess;
    private TaskCompletionSource<bool>? _connectResult;
    private readonly object _gate = new();

    public bool IsRunning
    {
        get { lock (_gate) return _vpnProcess is { HasExited: false }; }
    }

    public event Action<string>? LogReceived;

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static void RelaunchElevatedForVpn(string? relayId)
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法定位当前程序路径。");
        var start = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas" };
        start.ArgumentList.Add("--openrung-connect");
        if (!string.IsNullOrWhiteSpace(relayId))
        {
            start.ArgumentList.Add("--relay-id");
            start.ArgumentList.Add(relayId);
        }
        Process.Start(start);
    }

    public async Task<IReadOnlyList<OpenRungRelay>> DiscoverAsync(CancellationToken token = default)
    {
        var helper = Path.Combine(_directory, "OpenRungNodeDiscovery.exe");
        if (!File.Exists(helper))
            throw new FileNotFoundException("OpenRung 节点发现组件未随安装包提供。", helper);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(helper)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        if (!process.Start()) throw new InvalidOperationException("无法启动 OpenRung 节点发现组件。");

        var stdout = process.StandardOutput.ReadToEndAsync(token);
        var stderr = process.StandardError.ReadToEndAsync(token);
        try
        {
            await process.WaitForExitAsync(token).WaitAsync(TimeSpan.FromSeconds(35), token);
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        var json = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? $"OpenRung 节点发现失败，退出码 {process.ExitCode}。"
                : error.Trim());

        var rows = JsonSerializer.Deserialize<List<RelayRow>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        return rows.Select(row =>
        {
            var relay = row.Relay ?? throw new InvalidDataException("OpenRung 返回了无效节点记录。");
            var country = string.Join(" ", new[] { relay.City, relay.Country }.Where(x => !string.IsNullOrWhiteSpace(x)));
            var label = string.IsNullOrWhiteSpace(relay.Label) ? relay.Id : relay.Label;
            return new OpenRungRelay(
                relay.Id, label, string.IsNullOrWhiteSpace(country) ? relay.CountryCode : country,
                relay.PublicHost, relay.PublicPort,
                string.Equals(relay.NodeClass, "foundation", StringComparison.OrdinalIgnoreCase) ? "Foundation" : "Volunteer",
                row.ProbeMs, relay.PunchCapable, relay.ExpiresAt);
        }).ToArray();
    }

    public async Task ConnectAsync(string? relayId = null, CancellationToken token = default)
    {
        lock (_gate)
        {
            if (_vpnProcess is { HasExited: false })
                throw new InvalidOperationException("OpenRung VPN 已经在运行。");
        }

        var client = Path.Combine(_directory, "openrung-client.exe");
        if (!File.Exists(client))
            throw new FileNotFoundException("OpenRung 官方客户端组件未随安装包提供。", client);

        var start = new ProcessStartInfo(client)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("connect");
        start.ArgumentList.Add("--tun");
        start.ArgumentList.Add("-headless");
        if (!string.IsNullOrWhiteSpace(relayId))
        {
            start.ArgumentList.Add("-relay-id");
            start.ArgumentList.Add(relayId);
        }

        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _vpnProcess = process;
            _connectResult = connected;
        }

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            LogReceived?.Invoke(e.Data);
            if (e.Data.StartsWith("status: connected", StringComparison.OrdinalIgnoreCase))
                connected.TrySetResult(true);
            else if (e.Data.StartsWith("status: failed", StringComparison.OrdinalIgnoreCase))
                connected.TrySetException(new InvalidOperationException(e.Data));
        };
        process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) LogReceived?.Invoke(e.Data); };
        process.Exited += (_, _) =>
        {
            if (!connected.Task.IsCompleted)
                connected.TrySetException(new InvalidOperationException($"OpenRung 客户端提前退出，退出码 {process.ExitCode}。"));
        };

        try
        {
            if (!process.Start())
                throw new InvalidOperationException("无法启动 OpenRung VPN 客户端。");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await connected.Task.WaitAsync(TimeSpan.FromSeconds(90), token);
        }
        catch
        {
            if (process.Id > 0)
                await StopAsync(CancellationToken.None);
            else
                ClearProcess(process);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken token = default)
    {
        Process? process;
        lock (_gate) process = _vpnProcess;
        if (process is null) return;
        if (process.HasExited)
        {
            ClearProcess(process);
            return;
        }

        // The pinned OpenRung headless client treats stdin EOF as a graceful
        // disconnect. Do not kill the process: that could leave routes behind.
        try { process.StandardInput.Close(); } catch (InvalidOperationException) { }
        try
        {
            await process.WaitForExitAsync(token).WaitAsync(TimeSpan.FromSeconds(25), token);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException("OpenRung 尚未完成隧道清理；为避免遗留系统路由，本程序没有强制杀死 VPN 进程。");
        }
        finally
        {
            if (process.HasExited) ClearProcess(process);
        }
    }

    private void ClearProcess(Process process)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_vpnProcess, process))
            {
                _vpnProcess.Dispose();
                _vpnProcess = null;
                _connectResult = null;
            }
        }
    }

    public void Dispose()
    {
        try { StopAsync().GetAwaiter().GetResult(); }
        catch (Exception ex) { LogReceived?.Invoke("VPN 清理需要检查：" + ex.Message); }
    }

    private sealed class RelayRow
    {
        public RelayDescriptor? Relay { get; set; }
        public long? ProbeMs { get; set; }
    }

    private sealed class RelayDescriptor
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public string PublicHost { get; set; } = "";
        public int PublicPort { get; set; }
        public string City { get; set; } = "";
        public string Country { get; set; } = "";
        public string CountryCode { get; set; } = "";
        public string NodeClass { get; set; } = "";
        public bool PunchCapable { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
    }
}
