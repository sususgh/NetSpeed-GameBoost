using System.Diagnostics;
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
        bool Key(string section, string key)
        {
            var active = "";
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.StartsWith("[") && line.EndsWith("]")) active = line[1..^1];
                else if (active.Equals(section, StringComparison.OrdinalIgnoreCase) &&
                         line.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        if (!Section("Interface")) errors.Add("缺少 [Interface] 段。");
        if (!Section("Peer")) errors.Add("缺少 [Peer] 段。");
        if (!Key("Interface", "PrivateKey")) errors.Add("Interface 缺少 PrivateKey。");
        if (!Key("Peer", "PublicKey")) errors.Add("Peer 缺少 PublicKey。");
        if (!Key("Peer", "Endpoint")) errors.Add("Peer 缺少 Endpoint。");
        if (!Key("Interface", "Address")) warnings.Add("未发现 Address，请核对服务端分配的隧道地址。");
        if (!Key("Peer", "AllowedIPs")) warnings.Add("未发现 AllowedIPs，请核对路由范围。");
        if (!NamePattern.IsMatch(name)) errors.Add("配置文件名（隧道名）必须为 1–15 位字母、数字或 _=+.-。");
        return new(errors.Count == 0, name, errors.ToArray(), warnings.ToArray());
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
