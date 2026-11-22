# VPN tunnel and relay development

## Client-side foundation

- WireGuard integration uses the official WireGuard for Windows command-line interface; cryptography and the signed tunnel driver are not reimplemented here.
- The user must install the official WireGuard package and obtain a valid configuration from a trusted VPN operator. Tunnel service install/removal invokes Windows UAC.
- A service being present is not sufficient to claim that traffic is protected. The application must additionally verify interface state, handshake freshness, routes, DNS and IPv6 leak behavior.
- RelayNodeCatalog stores a bounded local node directory and performs TCP reachability probes. A successful TCP probe does not prove authenticated relay service, UDP forwarding, or game acceleration.

## Server-side requirements

No public relay nodes are fabricated or bundled. A real game relay requires deployed servers, a defined authenticated protocol, key rotation, replay protection, quotas, rate limits, abuse controls, and operational monitoring. Generic CDN caching does not accelerate arbitrary real-time UDP game traffic.

## Before claiming acceleration

1. Deploy and operate actual relay infrastructure.
2. Add signed node-directory updates and authenticated health/handshake checks.
3. Verify traffic traverses the selected tunnel/relay on Windows 10/11 x64.
4. Implement a fail-closed kill switch only after a privileged service can reliably restore firewall state across crashes, reboot, sleep/resume and uninstall.
5. Test IPv4, IPv6, DNS, split tunnelling, MTU, roaming and rollback.
6. Publish repeatable before/after latency and loss measurements; never promise a speed-up without evidence.
