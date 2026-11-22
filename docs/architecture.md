# Architecture

## Current scaffold
- **Desktop UI:** WPF on .NET 8 for Windows 10/11.
- **Telemetry:** Windows .NET network-interface counters sampled once per second.
- **Presentation:** XAML dashboard. The topology graphic is explicitly illustrative until real node telemetry is connected.

## Planned components
1. Diagnostics: interface counters, DNS timing, ICMP/TCP probes, jitter and loss calculations.
2. Game profiles: user-selected executable/process profiles; no packet interception in the first milestone.
3. Route engine: a narrow interface around future routing providers. Any route changes require explicit user consent and elevated service design.
4. Tunnel providers: separately audited WireGuard or other protocol integration; never implement cryptography ad hoc.
5. UI telemetry bus: typed snapshots delivered from background services to the WPF dispatcher.

## Design constraints
- The desktop app must remain useful without a phone.
- Measurements must come from actual operating-system/network observations.
- Missing data is displayed as unavailable, never fabricated.
- CDN caching is not a generic solution for real-time game UDP traffic. Game optimization focuses on path measurement, routing, and authorized relays.
