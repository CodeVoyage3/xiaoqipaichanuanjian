# 最新交接

## 当前任务

产品经理已批准 `V1-F01-I03｜方案 C 首次冷启动与历史补查`，并明确 V1 永久固定 `policy_version = 1`；Schema 阻断已解除，等待全新 Terra 仅执行 I03。

## 当前状态

`I03_APPROVED / READY_FOR_IMPLEMENTATION`

I03 正式任务卡已创建并按产品经理 version 1 决定修订；I03 实现代理尚未创建或派发，生产实现未开始。

## 当前 HEAD

- 本文件所在提交即当前交接 HEAD。
- I02 治理提交：`7208b2c`；Terra 实现与修复链：`9976957`、`10212d7`、`c9489f1`、`e4436f8`。
- 本地 `master` 与 GitHub `main` 必须以最终 push 后的实时引用为准；不得把 push 前 tracking ref 冒充远端当前值。

## I03 Version 契约决定

- 产品经理确认现有效期规则不会通过 v2/v3 方式变更，V1 固定只支持 `policy_version = 1`。
- 不再要求持久化同 scope 的 version 1 / version 2 两套基线；非 version 1 请求必须明确拒绝且零污染已有 version 1 业务事实。
- I01 Schema、migration、ModelSnapshot 保持不变，migration 总数继续为 9；原阻断已解除。

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

- 仅允许创建一个全新的 GPT-5.6 Terra（medium）执行 I03；不得复用旧 Terra。
- 不开始 I04，不进入 V1-F02 或 Stage 8。
- I03/I04 默认禁止新增 migration；schema 若不足必须停止并重新审批。
- 不执行方案 C、3% 历史过期补查或首次接管；这些属于 I03。
- 不实现提前 3 天预提醒；I04 仅允许保持现有 Reminder 消费 open Task 链路。
- 不操作正式数据库，不启动 WPF，不 squash、不重写历史、不 force push。

## 下一决策

按已批准任务卡创建全新 Terra 仅执行 I03；完成后由 Sol 独立验收并停止，不创建 I04。
