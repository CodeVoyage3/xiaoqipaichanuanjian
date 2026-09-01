# 最新交接

## 当前任务

`V1-F02｜效期节点提前 3 天预提醒` 已获产品经理单独批准；Sol 已完成接任、Reminder / Scheduler / Startup 架构审计并创建正式实现卡，等待全新 Terra 按卡实施。

## 当前状态

`V1-F02_APPROVED / WAITING_NEW_TERRA_IMPLEMENTATION`

## 当前 Git

- 分支：`master`。
- V1-F01 最终 GitHub 基线：`e5b748bfd44bb2256e82c638e8d72d98ee03bf82`。
- 本文件所在治理提交为最终交接 HEAD；具体 SHA 与 GitHub `main` 以本次 push 后实时引用为准。

## V1-F02 开工事实

- 正式任务卡：`.ai-dev/TASKS/V1-F02.md`；V1-F02 可作为一张实现卡完成。
- 现有 Schema 足够：预提醒以 `(BatchId, TargetStage)` 和 policy 派生日期识别，继续复用全局 `LastReminderDate` 的每日集中提醒同日幂等；migration 保持 9。
- `StageEffectiveDate` 与 `ReminderDate` 分离；四节点均为正式生效日前 3 个日历日。
- 候选窗口为 `ReminderDate <= BusinessDate < StageEffectiveDate`；错过首日可在正式节点到达前按既有跨日每日提醒补入，节点正式生效后不得补发提前提醒。
- 不提前改变 Stage，不创建或伪造 ProductTask、Inspection、Revision、LifecycleEvent、HandledAttentionVersion 或其他正式业务事实。
- Excluded、Unresolved、非 version 1、无匹配完成 ScopeBaseline、库存 0 或 stopped Batch 零预提醒。
- 正式 Task 与预提醒合并为同一次 Windows 集中提醒；同日幂等、日期回拨、失败重试、托盘、scheduler、提醒时间和自启动继续复用 Stage 6 权威。

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

## 下一步与停点

- 必须使用全新的 GPT-5.6 Terra（medium、标准速度）只实施 `.ai-dev/TASKS/V1-F02.md`，完成提交后停止；Sol 独立验收。
- 真实 WPF GUI 最终验收恢复由用户本人执行；Sol 只提供精简隔离清单。
- 若发现 Schema 不足，立即停止并提交最小 Schema / migration / 旧库兼容方案，等待产品经理单独批准。
- 不得进入 V1-F03、Stage 8、Stage 9 或其他未批准功能。
