using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace NetSpeed.GameBoost.Desktop.Networking;

public sealed record OpenVpnConfigValidation(bool IsValid, string[] Errors, string[] Warnings);
public sealed record OpenVpnStartResult(bool Started, string Detail);

/// <summary>
/// Starts a user-supplied OpenVPN profile with the official OpenVPN executable.
/// A running process is not proof of a completed VPN handshake or full-tunnel routing.
/// </summary>
public sealed class OpenVpnTunnelManager
{
    private static readonly object Sync = new();
    private static Process? _activeProcess;
    private static string? _activeConfigPath;

    public string? FindExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OpenVPN", "bin", "openvpn.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OpenVPN", "connect", "bin", "openvpn.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "OpenVPN", "bin", "openvpn.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    public OpenVpnConfigValidation ValidateConfig(string path)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        if (!File.Exists(path))
            return new(false, ["配置文件不存在。"], []);

        string[] lines;
        try
        {
            if (new FileInfo(path).Length is 0 or > 1024 * 1024)
                return new(false, ["配置文件为空或超过 1 MiB。"], []);
            lines = File.ReadAllLines(path);
        }
        catch (Exception ex)
        {
            return new(false, [$"无法读取配置文件：{ex.Message}"], []);
        }

        var activeRemote = lines.Any(line => Regex.IsMatch(line, @"^\s*remote\s+\S+\s+\d+", RegexOptions.IgnoreCase));
        var hasTun = lines.Any(line => Regex.IsMatch(line, @"^\s*dev\s+tun\b", RegexOptions.IgnoreCase));
        var hasProto = lines.Any(line => Regex.IsMatch(line, @"^\s*proto\s+(udp|udp4|udp6|tcp|tcp-client)\b", RegexOptions.IgnoreCase));
        if (!activeRemote) errors.Add("缺少有效的 remote 主机和端口配置。");
        if (!hasTun) warnings.Add("未显式声明 dev tun；请确认服务商提供的是 TUN 三层隧道配置。");
        if (!hasProto) warnings.Add("未显式声明 proto；将由 OpenVPN 按配置默认值处理。");

        var unsafeDirective = lines.FirstOrDefault(line =>
            Regex.IsMatch(line, @"^\s*(up|down|route-up|ipchange|plugin|management\s+)", RegexOptions.IgnoreCase));
        if (unsafeDirective is not null)
            errors.Add("配置包含脚本、插件或管理接口指令。为避免导入配置执行任意外部命令，本版本拒绝此类配置。");

        if (!lines.Any(line => Regex.IsMatch(line, @"^\s*(ca|<ca>)\b", RegexOptions.IgnoreCase)))
            warnings.Add("未发现内嵌或文件形式的 CA 指令；请确认服务商证书验证配置完整。");
        if (!lines.Any(line => Regex.IsMatch(line, @"^\s*(auth-user-pass|<auth-user-pass>)\b", RegexOptions.IgnoreCase)))
            warnings.Add("未发现用户名/密码认证指令；如服务商要求账号认证，请使用其官方配置方式。");

        return new(errors.Count == 0, errors.ToArray(), warnings.ToArray());
    }

    public OpenVpnStartResult Start(string configPath)
    {
        var executable = FindExecutable();
        if (executable is null)
            return new(false, "未检测到 OpenVPN Community 的 openvpn.exe。请先安装官方客户端。");

        var validation = ValidateConfig(configPath);
        if (!validation.IsValid)
            return new(false, string.Join(Environment.NewLine, validation.Errors));

        lock (Sync)
        {
            if (_activeProcess is { HasExited: false })
                return new(false, "本程序已启动一个 OpenVPN 进程。请先停止现有连接，再启动另一个配置。");

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(configPath) ?? Environment.CurrentDirectory
            };
            startInfo.ArgumentList.Add("--config");
            startInfo.ArgumentList.Add(Path.GetFullPath(configPath));
            startInfo.ArgumentList.Add("--verb");
            startInfo.ArgumentList.Add("3");

            try
            {
                _activeProcess = Process.Start(startInfo);
                _activeConfigPath = configPath;
                return _activeProcess is null
                    ? new(false, "Windows 未能启动 OpenVPN 进程。")
                    : new(true, $"已启动 OpenVPN 进程（PID {_activeProcess.Id}）。进程启动不代表握手成功；请继续验证日志、隧道路由、DNS 和 IPv6。");
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return new(false, "用户取消了管理员权限请求，未启动 VPN。");
            }
            catch (Exception ex)
            {
                return new(false, $"启动 OpenVPN 失败：{ex.Message}");
            }
        }
    }

    public string Stop()
    {
        lock (Sync)
        {
            if (_activeProcess is null)
                return "当前没有由本程序启动的 OpenVPN 进程。";

            try
            {
                if (!_activeProcess.HasExited)
                    _activeProcess.Kill(entireProcessTree: true);
                _activeProcess.Dispose();
                _activeProcess = null;
                _activeConfigPath = null;
                return "已向本程序启动的 OpenVPN 进程发送停止请求。请检查系统路由和 DNS 是否恢复。";
            }
            catch (Exception ex)
            {
                return $"停止 OpenVPN 失败：{ex.Message}";
            }
        }
    }

    public string GetStatus()
    {
        lock (Sync)
        {
            if (_activeProcess is null) return "本程序未启动 OpenVPN。";
            try
            {
                if (_activeProcess.HasExited)
                {
                    var code = _activeProcess.ExitCode;
                    _activeProcess.Dispose();
                    _activeProcess = null;
                    _activeConfigPath = null;
                    return $"OpenVPN 进程已退出，退出码：{code}。";
                }
                return $"OpenVPN 进程仍在运行（PID {_activeProcess.Id}），但尚未验证握手和全局路由。";
            }
            catch (Exception ex) { return $"无法读取 OpenVPN 状态：{ex.Message}"; }
        }
    }
}
