# NetSpeed-GameBoost

A Windows desktop application for game-network monitoring and diagnostics, built with .NET 8 and WPF.

> **Current capability:** local network throughput monitoring, game process profiles, observed TCP/UDP endpoint monitoring, route-quality diagnostics, encrypted DNS-over-HTTPS comparisons, network-health checks, and local CSV export.
>
> **Important:** this is currently a diagnostics/monitoring application. It does not yet create a VPN tunnel, relay network, traffic interception layer, or guarantee lower game latency. The topology illustration is not a live global node map.

## Download

Open the repository [Releases](https://github.com/sususgh/NetSpeed-GameBoost/releases) page and download one of the Windows x64 assets:

- **NetSpeed-GameBoost-Setup-v1.0.0.exe** — standard installer; installs the app and creates Start Menu and optional desktop shortcuts.
- **NetSpeed-GameBoost-Windows-x64-Portable.zip** — portable build; extract it and run \`NetSpeed.GameBoost.Desktop.exe\`.
- **SHA256SUMS.txt** — SHA-256 checksums for verifying downloads.

The release workflow runs when a \`v*\` tag is pushed or manually from GitHub Actions. It builds the self-contained application on a Windows GitHub Actions runner, packages the installer with Inno Setup, and attaches both packages to the GitHub Release. Release assets are only available after that workflow finishes successfully.

## Requirements

- End-user package: Windows 10/11 x64. No separate .NET installation is intended.
- Developer build: .NET 8 SDK and the .NET desktop/WPF workload on Windows.

## Developer build

\`\`\`powershell
dotnet restore .\src\desktop-windows\NetSpeed.GameBoost.Desktop.csproj
dotnet build .\src\desktop-windows\NetSpeed.GameBoost.Desktop.csproj -c Release
dotnet run --project .\src\desktop-windows\NetSpeed.GameBoost.Desktop.csproj
\`\`\`

## Publish a portable Windows x64 package

\`\`\`powershell
dotnet publish .\src\desktop-windows\NetSpeed.GameBoost.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o .\publish\win-x64
\`\`\`

## Privacy and safety

- Network measurements are performed locally; diagnostic data is not uploaded by the application.
- DoH comparisons use HTTPS/TLS with normal certificate and hostname validation. Failed encrypted queries do not fall back to plaintext DNS.
- DoH comparison does not change Windows system DNS settings. The operating system may use its current DNS configuration to resolve a DoH provider hostname initially.
- No custom root certificate is installed and TLS validation is not disabled.
- No traffic is redirected in the current application.
- No administrator privileges are requested by the current manifest.
- Connection inspection is based on Windows connection-table snapshots, not packet payload capture.

See [docs/architecture.md](docs/architecture.md), [docs/security.md](docs/security.md), and [docs/roadmap.md](docs/roadmap.md).


## v1.1.0 dashboard update

- Sidebar navigation is wired to working actions for the control center, game diagnostics, node TCP probing, encrypted DNS checks, Windows Security Center, and sampling settings.
- Dashboard cards provide WireGuard configuration format validation, user-supplied node TCP reachability tests, DNS diagnostics, game route analysis, and security status.
- VPN tunneling and CDN traffic relay are not advertised as active until a real tunnel backend and relay infrastructure are deployed.
