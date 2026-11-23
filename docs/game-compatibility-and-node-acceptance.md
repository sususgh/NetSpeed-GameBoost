# Game compatibility and relay acceptance requirements

This document records the requested product target for NetSpeed-GameBoost. These are acceptance requirements, not a claim that the features are already implemented.

## Required game targets

1. **Steam on Windows PC: all Steam games.** The application should discover running game processes launched by Steam, including games launched through Steam's helper processes. Users must be able to confirm or exclude detected processes. Compatibility must be validated per game because launchers, anti-cheat systems, transports, and network architectures differ.
2. **和平精英 (Game for Peace) on PC through an Android emulator.** Detect the emulator and game process tree, distinguish game traffic from emulator background traffic where technically possible, and test login, matchmaking, and a sustained match. Do not claim support for every emulator until tested.
3. **原神 (Genshin Impact) Windows PC client.** Detect the game process and relevant launcher/updater processes. Test login, updates, and sustained gameplay separately.

## Two independent operating modes

- **Game-only acceleration:** intended to route only the selected game's traffic through a verified game relay. Other applications must retain their normal routes.
- **Global VPN:** a separate explicit toggle for a full-tunnel VPN. It must not be required for game-only mode.

Selecting a mode is not the same as establishing a connection. UI status must distinguish Off, Discovering, Testing, Connecting, Connected, and Failed.

## Node discovery and qualification

Node discovery may use a maintained, trusted directory and user-provided nodes. A discovered IP address is not automatically an acceleration node. A node may be marked game-capable only after protocol and service checks confirm:

- an authenticated relay/tunnel service is actually listening;
- TCP and UDP forwarding capabilities are explicitly declared and tested;
- latency, jitter, packet loss, route stability, and throughput are measured from the user's network;
- the relay can reach the relevant game service region;
- node load, region, operator, and last verification time are displayed;
- node failure triggers a tested fallback without silently leaking game traffic onto an unintended route.

Never ship fabricated public node addresses or label a TCP-only health endpoint as a game relay.

## Technical constraints and honest status

- Process detection alone does not provide per-process routing.
- Standard system-wide WireGuard routing is not game-only acceleration. Per-game routing requires a supported Windows packet-filtering/traffic-steering design and careful UDP handling.
- Emulator traffic may be split across multiple child processes and services; process-tree attribution must be tested.
- Anti-cheat compatibility must be evaluated for each title. Do not bypass, tamper with, or inject into anti-cheat components.
- Do not claim universal compatibility merely because a process was detected or a node's TCP port accepted a connection.

## Release acceptance checklist

- [ ] Detect a broad set of Steam-launched games without requiring manual executable entry for each one.
- [ ] User can confirm, ignore, and remember detected games.
- [ ] Test representative Steam games using both TCP and UDP.
- [ ] Detect and test 和平精英 through at least one explicitly supported Windows Android emulator.
- [ ] Detect and test the native Windows 原神 client.
- [ ] Verify game-only mode does not reroute unrelated application traffic.
- [ ] Verify global VPN mode is independent and uses a verified full-tunnel configuration.
- [ ] Display measured latency, jitter, packet loss, throughput, node region, and node health.
- [ ] Test automatic node failover and safe recovery.
- [ ] Document per-title and per-emulator limitations.

Until these checks pass, the feature must be labeled experimental or not yet supported rather than “accelerating successfully.”
