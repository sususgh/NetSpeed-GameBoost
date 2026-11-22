# Roadmap

## Phase 1 — desktop foundation
- [x] .NET 8 WPF shell
- [x] Live local upload/download rate sampling
- [x] Least-privilege application manifest
- [ ] Build and run verification on Windows 10/11

## Phase 2 — diagnostics
- [x] System-configured DNS resolution timing with timeout handling
- [x] ICMP ping probe with timeout handling
- [x] Average latency, consecutive-sample jitter, and packet-loss metrics
- [ ] TCP endpoint probes
- [ ] Exportable local diagnostics report

## Phase 3 — game profiles
- [x] User-created and selected local game profiles
- [x] Game process presence observation (process name only; no injection)
- [x] Per-game local quality history for completed diagnostics
- [ ] Optional endpoint observation with explicit permissions
- [ ] Per-game historical charts and export

## Phase 4 — acceleration providers
- [ ] Provider abstraction and configuration format
- [ ] Integrate an established tunnel implementation
- [ ] Route selection with consent and rollback
- [ ] Fail-closed kill switch with automated leak tests

## Phase 5 — advanced network
- [ ] Authorized relay discovery and health checks
- [ ] Opt-in LAN sharing with device authentication
- [ ] Optional content cache for eligible, authorized content only
