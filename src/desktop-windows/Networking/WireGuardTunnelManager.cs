using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace NetSpeed.GameBoost.Desktop.Networking;

public sealed record WireGuardConfigValidation(bool IsValid, string InterfaceName, string[] Errors, string[] Warnings);
public sealed record WireGuardBackendStatus(bool IsInstalled, string? ExecutablePath, string? TunnelName, string State, string Detail);

/// <summary>Adapter for the official WireGuard for Windows CLI. Uses UAC for service changes.</summary>
public sealed class WireGuardTunnelManager
{
    private static readonly Regex NamePattern = new(@"^[A-Za-z0-9_=+.-]{1,15}$", RegexOptions.Compiled);

    public WireGuardBackendStatus Detect(string? tunnelName = null)
    {
        var exe = FindExecutable();
        if (exe is null) return new(false, null, tunnelName, "Unavailable", "未检测到官方 WireGuard for Windows。");
        if (string.IsNullOrWhiteSpace(tunnelName))
            return new(true, exe, null, "Installed", "已检测到 WireGuard CLI；尚未指定隧道。");
        if (!NamePattern.IsMatch(tunnelName))
            return new(true, exe, tunnelName, "InvalidName", "隧道名称格式无效。");

        using var p = Process.Start(new ProcessStartInfo("sc.exe", $"query \"WireGuardTunnel${tunnelName}\"")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
        if (p is null) return new(true, exe, tunnelName, "Unknown", "无法查询隧道服务。");
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit(5000);
        var state = output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase) ? "Running" :
            output.Contains("STOPPED", StringComparison.OrdinalIgnoreCase) ? "Stopped" : "NotInstalled";
        return new(true, exe, tunnelName, state, output.Trim());
    }

    public async Task<IReadOnlyList<string>> ListTunnelsAsync(CancellationToken token = default)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "sc.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        foreach (var argument in new[] { "query", "type=", "service", "state=", "all" })
            process.StartInfo.ArgumentList.Add(argument);

        if (!process.Start()) return [];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var output = (await stdout) + Environment.NewLine + (await stderr);
            if (process.ExitCode != 0) return [];
            return Regex.Matches(output, @"SERVICE_NAME:\s*WireGuardTunnel\$(?<name>[A-Za-z0-9_=+.-]{1,15})",
                    RegexOptions.IgnoreCase)
                .Select(match => match.Groups["name"].Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            throw;
        }
    }

    public WireGuardConfigValidation ValidateConfig(string path)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var name = Path.GetFileNameWithoutExtension(path);
        if (!File.Exists(path)) return new(false, name, ["配置文件不存在。"], []);
        string[] lines;
        try
        {
            if (new FileInfo(path).Length is 0 or > 1024 * 1024)
                return new(false, name, ["配置文件为空或超过 1 MiB。"], []);
            lines = File.ReadAllLines(path);
        }
        catch (Exception ex) { return new(false, name, [$"无法读取配置文件：{ex.Message}"], []); }

        bool Section(string section) => lines.Any(x => x.Trim().Equals($"[{section}]", StringComparison.OrdinalIgnoreCase));
        bool TryGetValue(string section, string key, out string value)
        {
            var active = "";
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';')) continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    active = line[1..^1].Trim();
                    continue;
                }

                var separator = line.IndexOf('=');
                if (separator > 0 && active.Equals(section, StringComparison.OrdinalIgnoreCase) &&
                    line[..separator].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    value = line[(separator + 1)..].Trim();
                    return value.Length > 0;
                }
            }

            value = "";
            return false;
        }

        if (!Section("Interface")) errors.Add("缺少 [Interface] 段。");
        if (!Section("Peer")) errors.Add("缺少 [Peer] 段。");
        if (!TryGetValue("Interface", "PrivateKey", out var privateKey))
            errors.Add("Interface 缺少 PrivateKey 或其值为空。");
        else if (!IsValidWireGuardKey(privateKey))
            errors.Add("Interface PrivateKey 不是有效的 32 字节 Base64 WireGuard 密钥。");

        if (!TryGetValue("Peer", "PublicKey", out var publicKey))
            errors.Add("Peer 缺少 PublicKey 或其值为空。");
        else if (!IsValidWireGuardKey(publicKey))
            errors.Add("Peer PublicKey 不是有效的 32 字节 Base64 WireGuard 密钥。");

        if (!TryGetValue("Peer", "Endpoint", out var endpoint))
            errors.Add("Peer 缺少 Endpoint 或其值为空。");
        else if (!IsValidEndpoint(endpoint))
            errors.Add("Peer Endpoint 格式无效；请使用域名或 IP 加端口，例如 vpn.example.com:51820 或 [IPv6]:51820。");

        if (!TryGetValue("Interface", "Address", out var address))
            warnings.Add("未发现 Address，请核对服务端分配的隧道地址。");
        else if (!IsValidCidrList(address))
            errors.Add("Interface Address 中存在无效的 IP/CIDR 地址。");

        if (!TryGetValue("Peer", "AllowedIPs", out var allowedIps))
            warnings.Add("未发现 AllowedIPs；未配置路由范围时，隧道可能不会承载预期流量。");
        else if (!IsValidCidrList(allowedIps))
            errors.Add("Peer AllowedIPs 中存在无效的 IP/CIDR 地址。");

        if (!NamePattern.IsMatch(name))
            errors.Add("配置文件名（隧道名）必须为 1–15 位字母、数字或 _=+.-。");
        return new(errors.Count == 0, name, errors.ToArray(), warnings.ToArray());
    }

    private static bool IsValidWireGuardKey(string value)
    {
        try { return Convert.FromBase64String(value).Length == 32; }
        catch (FormatException) { return false; }
    }

    private static bool IsValidEndpoint(string value)
    {
        string host;
        string portText;
        if (value.StartsWith("[", StringComparison.Ordinal))
        {
            var close = value.IndexOf("]:", StringComparison.Ordinal);
            if (close <= 1) return false;
            host = value[1..close];
            portText = value[(close + 2)..];
            if (!IPAddress.TryParse(host, out var ipv6) || ipv6.AddressFamily != AddressFamily.InterNetworkV6)
                return false;
        }
        else
        {
            var separator = value.LastIndexOf(':');
            if (separator <= 0 || value.IndexOf(':') != separator) return false;
            host = value[..separator];
            portText = value[(separator + 1)..];
            if (!IPAddress.TryParse(host, out _) && Uri.CheckHostName(host) != UriHostNameType.Dns)
                return false;
        }

        return int.TryParse(portText, out var port) && port is >= 1 and <= 65535;
    }

    private static bool IsValidCidrList(string value)
    {
        var entries = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return entries.Length > 0 && entries.All(entry =>
        {
            var separator = entry.IndexOf('/');
            if (separator <= 0 || !IPAddress.TryParse(entry[..separator], out var address) ||
                !int.TryParse(entry[(separator + 1)..], out var prefix))
                return false;
            var maxPrefix = address.AddressFamily == AddressFamily.InterNetwork ? 32 :
                address.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : -1;
            return prefix >= 0 && prefix <= maxPrefix;
        });
    }

    public async Task<WireGuardBackendStatus> InstallTunnelAsync(string configPath, CancellationToken token = default)
    {
        var check = ValidateConfig(configPath);
        if (!check.IsValid) throw new InvalidOperationException("配置检查失败：" + string.Join("；", check.Errors));
        var exe = FindExecutable() ?? throw new InvalidOperationException("未安装官方 WireGuard for Windows（含签名驱动）。");
        await RunElevatedAsync(exe, $"/installtunnelservice \"{Path.GetFullPath(configPath)}\"", token);
        return Detect(check.InterfaceName);
    }

    public async Task<WireGuardBackendStatus> UninstallTunnelAsync(string tunnelName, CancellationToken token = default)
    {
        if (!NamePattern.IsMatch(tunnelName)) throw new ArgumentException("隧道名称无效。", nameof(tunnelName));
        var exe = FindExecutable() ?? throw new InvalidOperationException("未检测到官方 WireGuard for Windows。");
        await RunElevatedAsync(exe, $"/uninstalltunnelservice \"{tunnelName}\"", token);
        return Detect(tunnelName);
    }

    private static string? FindExecutable()
    {
        var paths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WireGuard", "wireguard.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "WireGuard", "wireguard.exe")
        };
        return paths.FirstOrDefault(File.Exists);
    }

    private static async Task RunElevatedAsync(string exe, string args, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var p = new Process { StartInfo = new ProcessStartInfo(exe, args)
            { UseShellExecute = true, Verb = "runas", CreateNoWindow = true } };
        try
        {
            p.Start();
            await p.WaitForExitAsync(token);
            if (p.ExitCode != 0) throw new InvalidOperationException($"WireGuard 命令失败，退出码：{p.ExitCode}");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        { throw new OperationCanceledException("用户取消了 Windows 管理员权限请求。", ex, token); }
    }
}
