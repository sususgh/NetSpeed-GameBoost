# NetSpeed-GameBoost

A Windows-first game network diagnostics and acceleration platform built with .NET 8 and WPF.

> Status: early scaffold. This repository currently provides the desktop shell, diagnostics foundations, and architecture documentation. It does **not** yet implement a VPN tunnel, relay network, packet interception, or guaranteed game acceleration.

## Goals
- Live local network throughput monitoring using Windows performance counters.
- DNS resolver configuration and latency diagnostics (measurement first; no system DNS changes by default).
- Game-network quality probes: latency, jitter, and packet loss.
- Pluggable route and tunnel abstractions for future, separately reviewed implementations.
- A cinematic dark interface with a live topology/telemetry presentation driven by real measurements.

## Requirements
- Windows 10/11 x64
- .NET 8 SDK
- Visual Studio 2022 (optional) with the .NET desktop development workload

## Build
```powershell
dotnet restore .\src\desktop-windows\NetSpeed.GameBoost.Desktop.csproj
dotnet build .\src\desktop-windows\NetSpeed.GameBoost.Desktop.csproj -c Release
dotnet run --project .\src\desktop-windows\NetSpeed.GameBoost.Desktop.csproj
```

## Safety and privacy
- No traffic is redirected in this scaffold.
- No administrator privileges are requested.
- No TLS certificate validation is disabled.
- No custom root certificate is installed.
- Network measurements are local and are not uploaded.
- Future shared-relay features must be opt-in, quota-limited, revocable, and abuse-resistant.

See [docs/architecture.md](docs/architecture.md), [docs/security.md](docs/security.md), and [docs/roadmap.md](docs/roadmap.md).
