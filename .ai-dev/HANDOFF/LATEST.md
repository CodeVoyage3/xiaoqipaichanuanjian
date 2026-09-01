# 最新交接

## 当前任务

`V1-F01-I04｜基线后正常生命周期与端到端收口` 已完成 Sol 独立技术验收、隔离 WPF GUI 验收与 V1-F01 总体收口。

## 当前状态

`V1-F01_COMPLETED / WAITING_PRODUCT_MANAGER_NEXT_APPROVAL`

## 当前 Git

- 分支：`master`。
- I04 最终实现与测试 HEAD：`71c161469272c12a2088b804e0d401d8d6e92438`。
- 本文件所在治理提交为最终交接 HEAD；具体 SHA 与 GitHub `main` 以本次 push 后实时引用为准。

## I04 交付事实

- 完成匹配 ScopeBaseline 的 Managed 范围进入 policy-aware 正常生命周期；Post-import、startup、ProductTask、AttentionVersion / HandledAttentionVersion、正式提交与更高 Stage 继续复用既有权威。
- Excluded、Unresolved、无匹配完成基线或无有效 version 1 policy 的商品不进入 Stage、Task 或 Reminder。
- I03 首次 5折/2折基线不被历史追补，冷启动不重跑；库存 0、正式 0 件停止、MaxArrivalQty、ProductCode、BatchKey 等权威保持不变。
- Reminder 继续消费合法 Managed open Task；未实现提前 3 天预提醒。
- WPF 仅完成必要全品类文案收口，无新页面或 UI/UX 重构。

## Sol 独立验收

- I04 受影响专项 99/99；I01～I03 回归 159/159；库存回归 30/30。
- Release 全量 751/751；Release build 0 warning / 0 error。
- EF 无 pending model changes；migration 仍为 9，最后一条为 I01 的 `20260901155124_AddPolicyAndBaselineFoundation`。
- 无 migration、ModelSnapshot、schema、依赖、`.csproj` 或 `.slnx` 变化；`git diff --check` 通过。
- 真实全品类 Excel 在隔离 WPF/SQLite 导入 7,007 个商品、32,402 个批次；10 个 canonical 大类、8 个 Managed scope baseline 对账通过。
- 业务日期 `2026-09-02` 的 576 个 open Task 均为 Managed；实际到点 Reminder 显示 576。应季搭配、赠品小样与 180 天 Unresolved 边界样本均保存且零 Task/Reminder。

## 环境与边界

- GUI 使用同一 HEAD 源码的隔离路径测试构建；生产跟踪源码未改为测试路径。
- 正式 LocalApplicationData 入口未删除、修复或绕过；正式数据库未访问、迁移或修改，本轮不声称重新确认其 hash。
- 隔离数据库完整性为 `ok`，WPF 已退出，应用进程为 0；原始 Excel hash 前后不变。
- 未实现或创建 V1-F02、V1-F03、Stage 8 或其他后续任务。

## 下一决策

等待产品经理单独批准下一阶段。不得自动创建 V1-F02 任务卡，不得派发后续 Terra。
