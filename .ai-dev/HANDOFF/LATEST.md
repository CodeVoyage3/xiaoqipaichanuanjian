# 最新交接

## 当前任务

产品经理已批准 `V1-F01-I03｜方案 C 首次冷启动与历史补查`，但开工前发现 I01 Schema 与 I03 强制“同 scope 不同 policy_version 独立”验收门禁冲突；等待产品经理决定 Schema/验收口径。

## 当前状态

`I03_APPROVED / BLOCKED_SCHEMA_APPROVAL`

I03 正式任务卡已创建为 `.ai-dev/TASKS/V1-F01-I03.md` 并记录阻断；I03 实现代理尚未创建或派发，生产实现未开始。

## 当前 HEAD

- 本文件所在提交即当前交接 HEAD。
- I02 治理提交：`7208b2c`；Terra 实现与修复链：`9976957`、`10212d7`、`c9489f1`、`e4436f8`。
- 本地 `master` 与 GitHub `main` 必须以最终 push 后的实时引用为准；不得把 push 前 tracking ref 冒充远端当前值。

## I03 开工阻断

- `CK_scope_baselines_v1_policy` 在 EF、I01 migration 与 ModelSnapshot 中均固定 `policy_version = 1`；Product Managed 约束也只允许 version 1。
- 当前 Schema 无法真实持久化同 scope 的 version 1 与 version 2 两个有效基线，因此不能执行产品经理要求的“同 scope 不同 policy_version 相互独立”持久化验收。
- I03 明确禁止新增/修改 migration/schema；按失败停点必须停止，不得以 mock、伪 version 或绕过约束宣称通过。
- 尚未创建 I03 Terra，未修改生产代码、migration、schema、依赖、WPF、Reminder 或正式数据库。

## I02 交付事实

- 10 个批准源大类均在现有导入链路正常识别并保存稳定 canonical CategoryCode；中文显示名不作为永久 identity。
- 食品为 `food_expiry / 1`，宠物为 `pet_expiry / 1`；六类通用长效商品总效期 `>180` 天为 `general_long_expiry / 1`。
- 应季搭配、赠品小样保存 Product / Batch，标记 Excluded、policy/version 为空；不产生生命周期 Task。
- 六类通用长效商品 `<=180` 天保存为 Unresolved、policy/version 为空，并持久化 `expiry_policy_unresolved`。
- 同 ProductCode scope/policy 冲突产生 `product_scope_policy_conflict`，不静默改绑、不复制、不写该商品的批次/库存动作或生命周期。
- 未完成匹配 ScopeBaseline 的 Managed 商品只保存导入和身份；导入后与启动重算均不提前产生 Task。

## Sol 独立验收

- 首轮代码审查退回冲突库存动作、生命周期防御及端到端测试缺口；修复后继续审查。
- 首次受影响专项 115/116，发现 unknown category 的既有 unsupported 统计丢失；修复后最终受影响链路 117/117。
- 首次 Release 全量 721/723：一项 I02 startup fixture 缺 baseline、一项既有 S7T03 5 秒超时；补 fixture，超时精确复跑未复现，最终 Release 723/723。
- Release build 0 warning / 0 error；EF 无 pending model changes；migration list 为 9；`git diff --check` 通过。
- 相对 I02 治理基线无 migration、ModelSnapshot、schema、`.csproj`、`.slnx`、依赖、WPF 或 Reminder 变化。

## GUI 与数据库状态

- I02 无生产 UI 变化，不需要用户 GUI 验收。
- 本轮未启动 WPF；收口检查 `StoreExpiryInspector` 进程为 0。
- 未访问、迁移或修改正式数据库；所有持久化验证均使用临时隔离 SQLite 测试库。

## 当前禁止事项

- 未解决上述 Schema/验收口径前，不创建或执行 I03 Terra，不开始生产实现。
- 不开始 I04，不进入 V1-F02 或 Stage 8。
- I03/I04 默认禁止新增 migration；schema 若不足必须停止并重新审批。
- 不执行方案 C、3% 历史过期补查或首次接管；这些属于 I03。
- 不实现提前 3 天预提醒；I04 仅允许保持现有 Reminder 消费 open Task 链路。
- 不操作正式数据库，不启动 WPF，不 squash、不重写历史、不 force push。

## 下一决策

产品经理需选择：批准新的 schema/migration 变更以支持未来 policy version，或明确 V1 的版本隔离验收只要求拒绝非 version 1 且不影响现有基线。取得明确决定后，Sol 才能继续 I03。
