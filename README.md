# NetSpeed GameBoost

NetSpeed GameBoost is a Windows 10/11 x64 desktop application built with .NET 8 and WPF.

## Current verified product scope

The current application provides:
- Live local network upload/download rate monitoring.
- User-managed game process profiles and process-presence observation.
- TCP/UDP endpoint observation through the Windows connection table (not packet payload capture).
- Bounded ICMP route diagnostics with latency, jitter, packet-loss measurements and a heuristic quality score.
- TCP connect checks for observed endpoints and user-entered relay endpoints.
- DNS-over-HTTPS comparisons using TLS certificate validation; no fallback to plaintext DNS.
- Local network-health monitoring, route history and CSV diagnostic export.

The WireGuard integration is an optional client-side adapter for the official WireGuard for Windows application. It imports a provider-supplied configuration and asks the official CLI to install the tunnel service. It does not provide a VPN subscription, server, relay fleet, kill switch, or proof that game traffic is routed through the tunnel. The user must verify tunnel handshake, routes, DNS and IPv6 behavior with their VPN provider.

The relay catalogue currently stores user-supplied endpoints and performs TCP reachability checks only. It is not a deployed CDN or UDP game relay. No fake public nodes are bundled, and TCP reachability is not presented as game acceleration.

## Download

Download the latest verified Windows x64 installer and portable ZIP from [GitHub Releases](https://github.com/sususgh/NetSpeed-GameBoost/releases). Each release includes SHA-256 checksums. The release workflow only publishes a GitHub Release when a version tag such as `v1.2.0` is pushed and the Windows build, self-contained publish, installer creation and package checks all succeed.

Pull requests and normal branch builds produce downloadable CI artifacts, not public releases.

## Requirements

- End users: Windows 10/11 x64. The installer is self-contained and is intended not to require a separately installed .NET runtime.
- Developers: .NET 8 SDK and Windows desktop/WPF build support.

## Developer build

```powershell
dotnet restore .\src\desktop-windows\NetSpeed.GameBoost.Desktop.csproj
dotnet build .\src\desktop-windows\NetSpeed.GameBoost.Desktop.csproj -c Release
dotnet run --project .\src\desktop-windows\NetSpeed.GameBoost.Desktop.csproj
```

## Privacy and security

- Network measurements and profile history are stored locally; no telemetry upload is implemented.
- DoH queries use HTTPS/TLS with normal certificate and hostname validation.
- Failed encrypted DNS checks do not downgrade to plaintext DNS.
- The application does not silently change Windows DNS, routes, proxy or firewall settings.
- Connection inspection uses Windows connection-table snapshots; short-lived connections may be missed.
- No custom root certificate is installed and TLS validation is not disabled.
- The application is not antivirus software and does not claim to provide a VPN kill switch.
- Do not import WireGuard configuration files from untrusted sources; they contain sensitive private-key material.

See `docs/security.md`, `docs/architecture.md` and `docs/vpn-relay-development.md` for implementation limits and security requirements.
