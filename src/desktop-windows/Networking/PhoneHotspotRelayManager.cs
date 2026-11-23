using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NetSpeed.GameBoost.Desktop.Networking;

public enum PhoneRelayMode
{
    AllPhoneTraffic,
    GameOnly
}

public sealed record NetworkAdapterSummary(
    string Name,
    string Description,
    bool HasIpv4Gateway,
    string Ipv4Addresses);

public sealed record PhoneHotspotSnapshot(
    DateTime CheckedAtLocal,
    bool SharedAccessServiceRunning,
    IReadOnlyList<NetworkAdapterSummary> ActiveAdapters,
    IReadOnlyList<NetworkAdapterSummary> HotspotAdapters,
    IReadOnlyList<NetworkAdapterSummary> CandidateUpstreamAdapters);

/// <summary>
/// Read-only Windows Mobile Hotspot diagnostics. It deliberately does not change
/// ICS, routes, firewall rules, or adapter settings.
/// </summary>
public sealed class PhoneHotspotRelayManager
{
    public PhoneHotspotSnapshot GetSnapshot()
    {
        var adapters = NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up &&
                              adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Select(adapter =>
            {
                var properties = adapter.GetIPProperties();
                var hasGateway = properties.GatewayAddresses.Any(gateway =>
                    gateway.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !gateway.Address.Equals(System.Net.IPAddress.Any));
                var addresses = properties.UnicastAddresses
                    .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(address => address.Address.ToString());
                return new NetworkAdapterSummary(
                    adapter.Name,
                    adapter.Description,
                    hasGateway,
                    string.Join(", ", addresses));
            })
            .ToArray();

        var hotspots = adapters
            .Where(adapter => IsHotspotAdapter(adapter.Name, adapter.Description))
            .ToArray();

        var upstream = adapters
            .Where(adapter => adapter.HasIpv4Gateway &&
                              !IsHotspotAdapter(adapter.Name, adapter.Description))
            .ToArray();

        return new PhoneHotspotSnapshot(
            DateTime.Now,
            IsSharedAccessServiceRunning(),
            adapters,
            hotspots,
            upstream);
    }

    public static bool IsHotspotAdapter(string? name, string? description)
    {
        var combined = $"{name ?? string.Empty} {description ?? string.Empty}";
        return combined.Contains("Microsoft Wi-Fi Direct Virtual Adapter", StringComparison.OrdinalIgnoreCase) ||
               combined.Contains("Microsoft Wi-Fi Direct Virtual Adapter #", StringComparison.OrdinalIgnoreCase);
    }

    public static string DescribeMode(PhoneRelayMode mode) => mode switch
    {
        PhoneRelayMode.AllPhoneTraffic =>
            "全部流量：手机连接 Windows 移动热点后，由 Windows Internet 连接共享（ICS）转发。必须在 Windows 热点设置中启用热点，并确认共享上行网络。",
        PhoneRelayMode.GameOnly =>
            "仅游戏流量：Windows 热点本身不能识别 Android 手机上的游戏进程。需要手机端支持按应用分流的 VPN 客户端，以及可用的 WireGuard/中继服务器配置；当前不会假装已经实现分流。",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    public static void OpenMobileHotspotSettings()
    {
        Process.Start(new ProcessStartInfo("ms-settings:network-mobilehotspot")
        {
            UseShellExecute = true
        });
    }

    private static bool IsSharedAccessServiceRunning()
    {
        try
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
            process.StartInfo.ArgumentList.Add("query");
            process.StartInfo.ArgumentList.Add("SharedAccess");
            if (!process.Start())
                return false;

            if (!process.WaitForExit(3000))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            var output = process.StandardOutput.ReadToEnd();
            return process.ExitCode == 0 &&
                   output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
