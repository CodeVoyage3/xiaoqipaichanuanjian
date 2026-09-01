# 最新交接

## 当前任务

`V1-F01-I03｜方案 C 首次冷启动与历史补查` 已完成实现和 Sol 独立技术验收。

## 当前状态

`I03_TECHNICALLY_ACCEPTED / WAITING_I04_APPROVAL`

## 当前 Git

- 分支：`master`。
- I03 最终实现修复 HEAD：`9859988bb00e0b4cd93871dbe5d3368a381c1597`。
- 本文件所在治理提交为最终交接 HEAD；具体 SHA 与 GitHub `main` 以本次 push 后实时引用为准。

## I03 交付事实

- V1 永久固定 `policy_version = 1`；非 version 1 及无关成功 Import 在写入前明确拒绝并保持业务事实不变。
- 确认导入后按 `scope_key + policy_code + 1` 首次冷启动；完成范围幂等 no-op，不同 canonical scope 相互独立。
- 方案 C：5折/2折只建基线；收仓、到期当天、窗口内历史过期生成 open ProductTask；窗口外历史过期和库存 0 只建基线。
- 历史窗口为 `clamp(ceil((expiry_date - production_date) * 3%), 3, 30)`，端点包含；无法得到正的真实保质期时写稳定 ImportIssue，不猜测补查窗口。
- BatchBaseline 使用 I01 七类 disposition 和适用的 Task/Catchup 来源；ProductTask 继续由既有 Aggregator 聚合，不伪造 Inspection、Revision、LifecycleEvent、completed Task 或 HandledAttentionVersion。
- 冷启动、Task、审计事实和 ScopeBaseline 完成均处于确认导入事务内；异常整体回滚，回滚后可重试。

## Sol 独立验收

- I03 + 确认导入编排专项：23/23。
- I01/I02 相关回归：137/137。
- 真实 Excel + 隔离 SQLite：1/1；源文件前后为 2,522,641 bytes / SHA-256 `BBD91AE4E40E5381D749F8DB8F4CC0A600FB88D8C1CF6EA160C7C33EC1A3F0F6`。
- 真实样本 open ProductTask：583；5折 0、2折 0、收仓 210、过期 373；8 个 Managed ScopeBaseline 完成，Excluded / Unresolved 零 Task。
- Release 全量：735/735；Release build：0 warning / 0 error。
- EF 无 pending model changes；migration 仍为 9，最后一条为 I01 的 `20260901155124_AddPolicyAndBaselineFoundation`。
- 无 migration、ModelSnapshot、schema、依赖、`.csproj`、`.slnx`、WPF 或 Reminder 变更；`git diff --check` 通过。

## 环境与边界

- `StoreExpiryInspector` 进程为 0；未启动 WPF。
- 未访问、迁移或修改正式数据库；测试均使用临时隔离 SQLite。
- 未实现 I04、基线后完整正常生命周期收口或提前 3 天预提醒；未进入 V1-F02 或 Stage 8。

## 下一决策

等待产品经理单独批准 I04。不得提前创建 I04 正式任务卡，不得派发 I04 Terra。
