# Roadmap

## Phase 1 — desktop foundation
- [x] .NET 8 WPF shell
- [x] Live local upload/download rate sampling
- [x] Least-privilege application manifest
- [ ] Build and run verification on Windows 10/11

## Phase 2 — diagnostics
- [x] ICMP ping probe with timeout handling
- [x] Average latency, consecutive-sample jitter, and packet-loss metrics
- [x] Bounded TCP connect probes for observed game TCP endpoints
- [x] Exportable local diagnostics report (CSV)

## Phase 3 — game profiles
- [x] User-created and selected local game profiles
- [x] Game process presence observation (process name only; no injection)
- [x] Per-game local quality history for completed diagnostics
- [x] Bounded per-game local route history and trend chart
- [ ] Optional endpoint observation with explicit permissions
- [ ] Exportable per-game history

## Phase 4 — game network connection analysis
- [x] Read TCP / UDP endpoints from the Windows netstat connection table
- [x] Associate endpoints with game process IDs
- [x] Manual scan and endpoint list in the desktop UI
- [x] Detect newly observed and disappeared endpoints from periodic snapshots
- [ ] Validate visibility under different Windows permission levels
- [ ] Windows 10 / 11 real-machine verification

## Phase 5 — smart route diagnostics
- [x] Extract measurable unicast IPs from observed game endpoints
- [x] Bounded ICMP measurements for up to five targets
- [x] Calculate latency, jitter, packet loss, and heuristic quality score
- [x] Display ranked results in the desktop UI
- [x] Bounded TCP connect measurement for observed TCP endpoints only
- [x] Save local route history and draw a real measurement trend chart
- [ ] Windows 10 / 11 build and real-game validation

## Phase 6 — TCP connectivity and route history
- [x] Probe only TCP remote IP:port pairs observed in the selected game connection table
- [x] Apply per-target timeout; send no application payload and scan no arbitrary ports
- [x] Persist bounded local route-quality history
- [x] Display TCP connect results, recent history, and measured trend
- [ ] Windows 10 / 11 compile, UI layout, and real-game validation

## Phase 7 — connection change monitoring and diagnostics export
- [x] Read selected game Windows TCP / UDP connection-table snapshot every three seconds
- [x] Compare snapshots and record newly observed and disappeared endpoints
- [x] Bound in-memory change history to 1,000 entries
- [x] Reset comparison baseline when selected game changes
- [x] Export connection changes and recent route-quality records to CSV
- [ ] Windows 10 / 11 validation of sampling stability, CSV compatibility, and long-running behavior

## Phase 8 — encrypted DNS comparison and network health
- [x] Query DNS A records over DoH (DNS over HTTPS) with TLS
- [x] Domestic public DoH providers: Alibaba Cloud and DNSPod
- [x] International public DoH providers: Cloudflare, Google, and Quad9
- [x] Keep standard TLS certificate and hostname validation enabled
- [x] Never downgrade failed encrypted DNS queries to plaintext UDP/53
- [x] Display DoH response time, DNS answer, and fastest successful provider
- [x] Include DoH provider, HTTPS/TLS transport, and result in CSV
- [x] Do not change Windows system DNS settings; disclose possible OS-DNS bootstrap resolution
- [x] Check active network interface and ICMP probe state every eight seconds
- [x] Record network state changes, latency warnings, and probe failures
- [ ] Windows 10 / 11 validation of endpoint reachability, TLS checks, alerts, and long-running behavior
- [ ] Future: DoT, IPv6 DoH, and user-configured trusted DoH endpoints

## Phase 9 — downloadable Windows application
- [x] Add Windows GitHub Actions build workflow
- [x] Build and publish for Windows x64
- [x] Configure self-contained single-file publishing (no separate .NET runtime intended)
- [x] Package published output as a portable ZIP artifact
- [x] Verify the published executable exists and is a plausible size
- [ ] Run the workflow on GitHub and fix any real compiler/publish failures
- [ ] Test launch and core functions on Windows 10 and Windows 11
- [ ] Add code signing and installer/MSIX packaging after release validation

## Future acceleration features
- [ ] Provider abstraction and configuration format
- [ ] Integrate an established, security-reviewed tunnel implementation
- [ ] Route selection only with explicit user consent and rollback
- [ ] Fail-closed kill switch with automated IPv4/IPv6/DNS leak tests
- [ ] Authenticated, opt-in, quota-limited, revocable LAN relay sharing
- [ ] Authorized relay discovery and health checks

## Technical notes

- Current route score is a heuristic diagnostic value, not a promise of better FPS or lower latency.
- ICMP may be filtered; no ICMP response does not prove a game server is offline.
- TCP connect success does not represent UDP game traffic quality.
- DoH encrypts DNS traffic between this app and the selected provider; the provider can still see queried domains and the client network address.
- Initial resolution of a DoH provider hostname may use the operating system current DNS configuration.
- Current monitoring does not redirect traffic or modify system routing.
- Local route history is stored under `%LOCALAPPDATA%\NetSpeed\GameBoost\route-history.json` and bounded to 300 records.
- Connection change monitoring is based on three-second snapshots, so short-lived connections may be missed.
