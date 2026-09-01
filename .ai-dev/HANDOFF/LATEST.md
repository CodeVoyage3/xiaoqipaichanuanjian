# 最新交接

## 当前任务

`V1-F01-I01｜Policy 与范围基线持久化底座` 已完成 Sol 独立技术验收；等待产品经理决定是否批准 `V1-F01-I02｜全品类导入与管理范围映射`。

## 当前状态

`TECHNICALLY_ACCEPTED / WAITING_I02_APPROVAL`

I02 正式任务卡尚未创建，I02 实现 Terra 尚未创建或派发，I02 未开始。

## 当前 HEAD

- 本文件所在提交即当前交接 HEAD；实施与修复提交链为 `609989f0e326ce9adcae4ed6f03ba7fd0cdbe1c5`、`67efc68b401117eab74b5a53aba0b123199f36de`、`1863fb55436beaa1a67d55b1ee4cdaf1bf394488`。
- 本地 `master` 与 GitHub `main` 必须以最终 push 后的实时引用为准；不得把 push 前的本地 tracking ref 冒充远端当前值。

## I01 交付事实

- Product 新增独立 `PolicyVersion` 和 Managed / Excluded / Unresolved；V1 policy code 为 `food_expiry / pet_expiry / general_long_expiry`，不含版本号。
- `CategoryCode` 继续作为 canonical scope_key 持久化位置；I01 只保留既有食品 canonical `food`，未实现 I02 的源大类映射。
- 新增 ScopeBaseline 与 BatchBaseline 持久化、唯一键、状态/来源/check constraint 及最小 V1 policy 计算。
- 唯一新增 migration 为 `20260901155124_AddPolicyAndBaselineFoundation`，migration 总数由 8 增至 9；旧 `food_v1` 显式迁移为 `food_expiry / 1 / managed`。
- 未改变 ProductCode、BatchKey、Seen BatchKey、MaxArrivalQty、库存 0、Task 聚合或 AttentionVersion / HandledAttentionVersion 权威。

## Sol 独立验收

- 第一轮相关专项 12/13，发现 Excluded 空 policy 被 store default 覆盖；退回修复后相关专项 25/25。
- 第一轮 Release 全量 709/711，发现旧 schema 测试 helper 丢失 tracked-entity 语义；退回修复后最终 Release 711/711，0 失败、0 跳过。
- Release build 0 warning / 0 error；EF 无 pending model changes；migration list 为 9；全量 migration script 已生成并人工核对 SQLite rebuild 顺序；`git diff --check` 通过。
- 相对 I01 治理基线无 `.csproj`、`.slnx`、依赖、WPF 或 Reminder 变化。
- restore 使用 `--ignore-failed-sources -p:NuGetAudit=false`；不构成在线漏洞审计通过。

## GUI 与数据库状态

- I01 无生产 UI 变化，不需要用户 GUI 验收。
- 本轮未启动 WPF；收口时 `StoreExpiryInspector` 进程为 0。
- 未访问、迁移或修改正式数据库；旧库升级只在临时隔离 SQLite 测试中验证。

## 当前禁止事项

- 不创建或执行 I02，不派发 I02 Terra；须等待产品经理单独批准。
- 不开始 I03/I04，不进入 V1-F02 或 Stage 8。
- I02--I04 默认禁止新增 migration；schema 若不足必须停止并重新审批。
- 不实现提前 3 天预提醒；I04 仅允许保持现有 Reminder 消费 open Task 链路。
- 不操作正式数据库，不启动 WPF，不 squash、不重写历史、不 force push。

## 下一决策

是否批准创建并执行：

`V1-F01-I02｜全品类导入与管理范围映射`

若获批准，Sol 先创建正式 I02 任务卡，再新建一个全新的 GPT-5.6 Terra（medium、平台标准速度）仅实现 I02；Terra 完成后停止，由 Sol 独立验收。
