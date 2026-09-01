# V1-F01｜生产实施拆分提案

- 状态：`PROPOSED_WAITING_FIRST_TASK_APPROVAL`
- 设计权威：产品经理 2026-09-01 最终批准的 V1-F01 十项规则
- 当前动作：仅拆分任务，不修改生产代码，不创建 migration，不派发实现代理
- 执行治理：每张获批实现卡均创建一个全新的 GPT-5.6 Terra（medium，平台标准速度）；不得复用分析 Terra。Terra 实现后停止，由 Sol 独立审查、复跑和验收。

## 拆分原则

依赖顺序固定为“持久化与 policy 契约 → 全品类导入 → 首次冷启动 → 基线后正常生命周期与端到端收口”。共四张卡；不为 Reminder 单建规则系统，不为 policy 建插件/表达式引擎，不把 UI 计算业务规则。

`scope_key` 在 V1 复用现有 `Product.CategoryCode` 的持久化位置与“源商品大类”语义，避免再存一份同值字段；范围基线的唯一身份仍显式为 `scope_key + policy_code + policy_version`。

## 拟议任务顺序

|拟议任务|目标|前置|schema / migration|完成后停止点|
|---|---|---|---|---|
|V1-F01-I01｜Policy 与范围基线持久化底座|冻结 policy/version、管理状态、范围基线与批次基线审计契约|V1-F01 设计批准|允许且只允许本系列唯一一次 migration|Sol 验收底座，不接全品类导入|
|V1-F01-I02｜全品类导入与管理范围映射|10 大类均可正常导入；受管、V1 不纳管、规则未覆盖三种状态可审计|I01 通过|禁止新增 migration|Sol 验收导入；尚不执行首次冷启动|
|V1-F01-I03｜方案 C 首次冷启动与历史补查|按范围幂等建立基线，执行 3% clamp(3,30) 与方案 C，创建可审计高风险任务|I02 通过|禁止新增 migration|Sol 验收首次启用；不扩展 UI/Reminder 体系|
|V1-F01-I04｜基线后正常生命周期与端到端收口|恢复完整 5折/2折/收仓/过期生命周期，接通启动重算、提醒和最小 UI 表达|I03 通过|禁止新增 migration|技术门禁 + 用户真实 WPF 验收，V1-F01 实现收口|

## I01｜Policy 与范围基线持久化底座

### 允许范围

- 建立最小 policy 解析契约，支持食品、宠物、六类通用长效组，月份固定按 30 天；保留 canonical Stage 与既有优先级。
- 为 Product 补足 `policy_version` 和明确的效期管理状态；现有 `category_code` 作为 `scope_key`，不新增同义副本。
- 新增范围基线实体，唯一约束为 `scope_key + policy_code + policy_version`；保存建立 Import、业务日期、UTC 时间、完成状态和必要规则快照标识。
- 新增批次基线明细，唯一关联范围基线 + Batch，记录首次接管分类与历史补查来源；该记录不是 Inspection、Revision、Task 或 LifecycleEvent。
- 一次 migration、model snapshot、配置及最小数据模型文档；已有食品数据升级后身份稳定，不自动生成 Task。

### 明确排除

- 不移除 Excel 食品过滤，不接 ConfirmedImportLifecycleOrchestrator，不创建冷启动 Task。
- 不修改 WPF、Reminder、正式数据库，不实现通用规则引擎或配置后台。
- 不用 completed Task、Inspection、Revision 或 HandledAttentionVersion 表示基线。

### 最低验收

- policy 端点：固定 30/60/90/180/360 天、到期当天 expired、食品 9 个月边界、宠物和通用长效组节点。
- 唯一范围键、批次基线幂等、来源字段约束、旧库 migration 升级、空库创建、EF 无漂移。
- 既有八条 migration 全量升级到新增 migration；升级过程不创建业务 Task/Inspection。

## I02｜全品类导入与管理范围映射

### 允许范围

- Reader / Classifier / Planner / Executor 最小扩展，保留 ProductCode、BatchKey、冲突组整体排除、局部增量和库存事实权威。
- 去除“只接收食品”的硬过滤；按源商品大类映射并保存 scope/policy/version/管理状态。
- `应季搭配`、`赠品小样`保存为 V1 不纳管：不计算 Stage、不进生命周期、不生成 Task/Reminder/历史补查。
- 六类通用长效组总效期 `<=180` 天保存为 `policy unresolved / 规则未覆盖`；导入预览/结果提供稳定提示，不生成效期任务。本卡用合成边界样本测试，不虚构其 policy。
- 已有 ProductCode 出现范围或 policy 身份冲突时返回可审计问题，不静默改绑、不复制商品。

### 明确排除

- 不建立首次范围基线，不执行方案 C，不产生非食品首日 Task。
- 不新增 migration/依赖，不改 Stage 3--7 权威状态机。
- UI 只展示 Application 给出的导入状态与提示，不重算 policy。

### 最低验收

- 10 类导入、两类 V1 不纳管、六类 `<=180` 天规则未覆盖、现有食品回归。
- 冲突 BatchKey、库存冲突、重复行、局部增量、LastSeenImportId、MaxArrivalQty 和 ImportIssue 持久化不回归。
- 未完成范围基线时，受管商品仅保存，不因导入或启动重算提前产生 Task。

## I03｜方案 C 首次冷启动与历史补查

### 允许范围

- 在确认导入的既有外层事务边界内，对尚无完成基线的 `scope_key + policy_code + policy_version` 执行一次冷启动；完成基线后同范围不重跑。
- 顺序固定：库存 0 终止 → 规则适用性 → 当前 Stage / 历史窗口分类 → 批次基线记录 → 需要的 Task 聚合 → 范围基线完成。
- 方案 C：既有 5 折、2 折只记录基线并设置下一更高 Stage 触发点；当前收仓、到期当天生成 open Task。
- 历史补查窗口：`clamp(ceil((expiry_date - production_date)天数 * 3%), 3, 30)`，端点包含；窗口内 expired open Task，窗口外仅历史基线。
- 生产日期缺失、日期倒置或实际保质期无法得到正天数时，不得用声明单位反推历史窗口；只按既有导入质量契约处理并留下可审计提示，不进入历史补查。
- 历史补查来源通过批次基线事实可直接审计；不创建或伪造 Inspection、Revision、completed Task。
- 同商品多批次复用 ProductTaskAggregator，最高阶段和每商品最多一个 open Task 保持不变。

### 明确排除

- 不让两类 V1 不纳管或规则未覆盖批次进入历史补查。
- 不把首次 5 折/2 折写成“已排查”或推进 HandledAttentionVersion。
- 不新增 migration/依赖，不修改 Reminder/WPF 视觉，不使用正式数据库做演练。

### 最低验收

- 范围级首跑、重放、事务失败回滚、两个范围独立、policy_version 独立。
- 3% 取整与 3/30 上下限、端点包含、到期当天 expired、窗口外基线。
- 方案 C 四类处理、库存 0 最高优先、无正式历史伪造、Task 来源可追溯。
- 使用真实 Excel 的隔离数据库验收时，预期首日为 583 个 open ProductTask；该数字是验收对账，不写成生产常量。

## I04｜基线后正常生命周期与端到端收口

### 允许范围

- 已完成基线的范围恢复完整正常生命周期：后续新 BatchKey 和新增到货在 5折、2折、收仓、过期均按现有规则创建/升级 Task。
- 将 policy-aware 计算接入 PostImportLifecycleUseCase 与 StartupRecalculationUseCase；继续复用 ProductTaskAggregator、AttentionVersion、HandledAttentionVersion 和阶段优先级。
- V1 不纳管及规则未覆盖批次持续不计算 Stage、不生成 Task、不进入 Reminder；规则未覆盖状态在导入结果可见。
- Reminder 只消费既有 open Task，不创建第二套提醒判断；WPF 只做必要的全品类文案、状态和提示接入。
- 补齐端到端回归、隔离数据库升级/导入/重启验证及用户真实 WPF 验收说明。

### 明确排除

- 不新增 migration/依赖，不建设 policy 编辑器、人工季节规则、Stage 8、导出或 UI 重构。
- 不改变 Seen BatchKey、历史累计到货最大值、库存 0、批次 0、正式提交、Revision、备份恢复权威。

### 最低验收

- 基线后新 Batch、MaxArrivalQty 突破、阶段逐级升级、正式提交后更高 Stage、启动重算幂等。
- excluded / unresolved 在导入、启动、Reminder 全链路均为零 Task；managed open Task 正常提醒。
- Release 全量、build、EF 无漂移、migration 数量保持 I01 后数量、依赖不变、`git diff --check`。
- WPF 真实导入结果提示、任务展示和 Reminder 由用户在隔离环境验收；不得操作正式数据库。

## 审批与代理门禁

当前四张卡均为提案，尚未写入独立 `.ai-dev/TASKS/V1-F01-Ixx.md`，也未派发实现代理。产品经理批准 I01 后：

1. Sol 基于批准文本创建正式 I01 任务卡；
2. Sol 新建全新的 GPT-5.6 Terra，推理强度 medium、平台标准速度；
3. Terra 只实现 I01，提交后停止；
4. Sol 独立验收；
5. 未获下一卡批准，不创建或派发 I02。
