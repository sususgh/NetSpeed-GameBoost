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
- [x] Bounded TCP connect probes for observed game TCP endpoints
- [x] Exportable local diagnostics report (CSV)

## Phase 3 — game profiles
- [x] User-created and selected local game profiles
- [x] Game process presence observation (process name only; no injection)
- [x] Per-game local quality history for completed diagnostics
- [ ] Optional endpoint observation with explicit permissions
- [x] Bounded per-game local route history and trend chart
- [ ] Exportable per-game history

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


## 第五阶段：智能游戏线路分析

- [x] 从已识别的游戏端点提取可测量的单播 IP
- [x] 对最多 5 个目标进行有限次数 ICMP 测量
- [x] 计算延迟、抖动、丢包和启发式质量评分
- [x] 在桌面界面展示排序结果
- [x] 增加 TCP connect 测量作为 ICMP 的辅助指标（仅观察到的 TCP 端点）
- [x] 保存本地线路历史并绘制最近质量趋势图
- [ ] Windows 10 / 11 编译与真实游戏联机验证

评分说明：当前分数是便于比较的启发式诊断值，不代表游戏实际帧率或必然的线路改善；ICMP 无响应不能直接判定服务器不可用。此阶段不修改路由、不自动切换线路。


## 第六阶段：TCP 连通性与线路历史

- [x] 仅对游戏连接表中观察到的 TCP 远端 IP:端口执行有限 connect 测试
- [x] 每个 TCP 目标设置超时；不发送应用层数据、不扫描任意端口
- [x] 将 ICMP 与 TCP 测量结果写入本机有界历史文件
- [x] 桌面端显示 TCP connect 结果、最近历史记录与真实评分趋势折线
- [ ] Windows 10 / 11 编译、UI 布局与真实游戏环境验证
- [ ] 导出诊断报告与多次采样统计

注意：TCP connect 成功只说明该 TCP 端口可建立连接，不代表 UDP 游戏流量质量；游戏使用 UDP 时不会产生 TCP 测试结果。历史文件保存在 `%LOCALAPPDATA%\\NetSpeed\\GameBoost\\route-history.json`，最多保留 300 条记录，不上传云端。


## 第七阶段：连接变化监控与诊断导出

- [x] 每 3 秒读取一次所选游戏的 Windows TCP / UDP 连接表快照
- [x] 对比相邻快照，记录新建与消失的连接端点
- [x] 限制内存中的变化记录最多 1,000 条
- [x] 游戏档案切换时重置变化基线，避免不同游戏数据混淆
- [x] 导出 CSV，包含连接变化、端点信息及最近 50 条线路质量记录
- [ ] Windows 10 / 11 实机验证采样稳定性、CSV 兼容性与长时间运行
- [ ] 后续可增加 DNS 多解析器对比和网络中断告警

说明：连接变化监控基于每 3 秒一次的 netstat 快照，短于采样间隔的瞬时连接可能漏记；它不捕获数据包内容，也不代表精确的逐连接生命周期。CSV 仅在用户选择保存位置后写入本机。


## 第八阶段：加密 DNS 对比与网络健康监测

- [x] 使用 DoH（DNS over HTTPS）通过 TLS 加密通道查询 A 记录
- [x] 内置中国大陆公共加密 DNS：阿里云 DoH、DNSPod DoH
- [x] 内置国际公共加密 DNS：Cloudflare、Google、Quad9
- [x] 保持系统 TLS 证书和主机名验证，不接受无效证书
- [x] 加密查询失败时明确报错，不回退到明文 UDP/53
- [x] 界面显示加密 DNS 响应耗时、解析结果与最快成功服务
- [x] CSV 记录 DoH 服务商、HTTPS/TLS 传输方式与查询结果
- [x] 不修改 Windows 系统 DNS；明确说明 DoH 服务域名引导解析可能使用系统现有 DNS
- [x] 每 8 秒检测活动网络接口及 ICMP 探测状态
- [x] 记录网络状态变化、延迟异常和探测目标无响应
- [ ] Windows 10 / 11 实机验证 DoH 端点可达性、TLS 校验、告警准确性与长时间运行
- [ ] 后续增加 DoT（DNS over TLS）独立测试、IPv6 DoH 和用户自定义可信 DoH 端点

说明：DoH 查询使用 HTTPS/TLS 加密传输，DNS 服务提供商仍能看到查询域名和客户端网络地址。首次连接 DoH 服务域名时，其引导解析可能由操作系统当前 DNS 配置完成；本阶段不宣称消除所有引导解析泄漏。ICMP 被屏蔽时可能显示目标无响应，即使其他网络服务仍可用。
