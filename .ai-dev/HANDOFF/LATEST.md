# 最新交接

## 当前任务与状态

- Stage 8：`IN_PROGRESS / S8-T02_PAUSED_ISOLATION_REVIEW`。
- 2026-09-04 用户正式批准 S8-T02，并允许全新 Terra / medium / priority；代理已实施部分查询改写，但当前停工停测、未验收。
- 最新停止原因：Terra 已执行过含 Shell 测试的定向组合，初版 category loader 未遵守测试注入隔离，可能进入默认正式库。CreateContext 会创建目录，SQLite 连接未限定只读；无法排除实际访问或文件系统变化。Sol 未打开正式库调查，不声称“未访问”。
- Terra 回执：查询专项 11失败/4通过，组合15失败/55通过；主要为 EF 翻译与旧UI测试期望。两个生产文件改动未提交，代理已停止；只归档治理状态，不推送生产改动。完整命令/风险见 S8-T02 Acceptance。
- S8-T01 已由本卡全新 GPT-5.6 Terra 实施，并经 Codex Sol 独立技术验收通过；本卡到此停止。
- V1-F01、V1-F02、V1-F03、V1-F03-I04、V1-UI-01 继续保持 `CLOSED`；V1-UI-01 为 `GUI_ACCEPTANCE_PASSED`。
- S8-T03～T06 仍只是候选方向，没有创建正式 Task 或 Acceptance；Stage 9 与在线升级未启动。

## Git 与实现

- S8-T02 重新 fetch 开工基线：`master == origin/main == 0ede8b901fb5e6cbc1c2f2824d6f8a6c7a54f901`，工作区干净。下列性能数据为 S8-T01 历史基线，不是 S8-T02 新实现证据。

- 2026-09-03 重新 fetch 后的 GitHub 开工基线：`master == origin/main == 1e41876bfa9c203a88cf53955867f0c3dd639e84`。
- Stage 8 / S8-T01 建档提交：`1706b0e432b4ed936e902d431070392566b9ee11`。
- Terra 最终实现提交：`806e14f43203a355c1aaab3251af724cf3b43bf3`。
- 实现仅新增 `tests/StoreExpiryInspector.Tests/S8T01PerformanceBaselineTests.cs`；无 `src/**`、索引、Schema、ModelSnapshot、migration、依赖、`.csproj`、`.slnx` 变化。

## 新鲜验收结果

- 高规模专项 1/1：真实 products/batches=100,000，inspections/inspection_items=300,000；SQLite 225,427,456 bytes；前后计数与逻辑指纹一致。
- 隔离 DB 与 snapshot 均 `integrity_check=ok`、FK=0、migration=9；“应季搭配 / 赠品小样”各有产品/批次但 open task=0、Reminder eligible=0。
- Release 897/897；Release build 0 warning / 0 error；EF 无模型漂移；migration 仍为 9，末条 `20260901155124_AddPolicyAndBaselineFoundation`。
- 证据 JSON：`C:\Users\39037\AppData\Local\Temp\StoreExpiryInspectorS8T01\S8-T01-47a593d3d8964c1c80be894c13914415\S8-T01-baseline.json`，SHA-256 `D00C028B43CA78D7E168B567DE96F7B2D6877C87B62996CA4C879CBF0508245C`。

## 已确认性能事实

- 100k 下 Dashboard、无搜索的待排查/今日排查、Reminder 和依赖全量列表的内存筛选路径均因 SQLite `too many SQL variables` 失败；这是 S8-T02 的第一阻断项。
- 成功路径中 Stage 筛选 median 6631.67 ms / max 6638.93 ms 最慢；历史列表 887.29 / 890.38 ms；snapshot 5713.83 ms。
- 计划显示 open-task 索引扫描、历史 correlated count 与 `USE TEMP B-TREE FOR ORDER BY`；过度 materialization 和 `PageSize=int.MaxValue` 后内存筛选已观察到，N+1 未证明。
- 失败发生在 provider 生成 DbCommand 之前，因此这些失败路径只有真实异常/耗时，没有可伪称的实际 SQL/EXPLAIN；成功路径已有完整 SQL、参数、command count 与 plan。

## 下一步停止门禁

- 当前等待用户决定是否允许先修复并验证测试隔离边界后继续 S8-T02；这不构成正式数据库访问授权。不得继续测试、关闭本卡或创建 S8-T03。
- 任何新增索引、Schema 或 migration 仍需明确批准；不得启动 Stage 9 或在线升级。
