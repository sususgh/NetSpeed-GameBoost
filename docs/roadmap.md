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


## 第四阶段：游戏网络连接分析

- [x] 通过 Windows netstat 连接表读取 TCP / UDP 端点
- [x] 按游戏进程名关联 PID
- [x] 在桌面端增加手动扫描与端点列表
- [ ] 验证不同 Windows 权限级别下的可见性
- [ ] 增加连接变化历史与远端地址归属信息
- [ ] 在 Windows 10 / 11 实机编译和运行测试

说明：netstat 输出是系统连接表快照，不是逐包捕获；UDP 不具有 TCP 式连接状态。远端 IP 的地理位置或运营商归属尚未查询。
