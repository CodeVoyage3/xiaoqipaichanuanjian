# Stage 4 中途 PM 交接｜停在 S4-T05 创建前

- 交接日期：2026-08-28
- 交接性质：Stage 4 中途产品经理交接，不是 Stage 4 完成或整体验收
- 当前门禁：S4-T01～S4-T04 已正式验收通过，UI/UX Pro Max 基线已归档，S4-T05 尚未创建
- 权威原则：新任 PM 必须以仓库事实、S4-T01～S4-T04 任务卡与验收记录、实现源码及 `.ai-dev/UI/STAGE-4-UI-BASELINE.md` 为权威；不得根据本交接文件单独推断业务规则

## 1. Git 与本次重新核验基线

- branch：`master`
- 本次交接核验起点 HEAD：`fc89842463606d2084e22a2a9c4bab654c9a7f9e`
- 起点提交：`docs: define Stage 4 UI baseline`
- 起点 `git status --short`：无输出，工作区干净
- `.ai-dev/TASKS/S4-T05.md`：不存在
- 本交接文档归档后，仓库 HEAD 将是紧随上述基线的治理提交；完整 SHA 由交接完成报告返回

## 2. Stage 4 当前整体状态

当前真实进度：

`Stage 3 整体通过`

→ `S4-T01 只读查询面通过`

→ `S4-T02 草稿保存与重新确认通过`

→ `S4-T03 手工库存修正与商品归零编排通过`

→ `S4-T04 正式排查提交事务通过`

→ `Stage 4 UI/UX Pro Max 设计治理完成并归档`

→ **停在 S4-T05 创建前**

Stage 4 后台 Application 已具备从任务查询、草稿、库存修正到正式提交的业务闭环；WPF 仍只有占位主窗口，尚未形成用户可操作闭环。Stage 4 未完成，也未进行阶段总验收或人工完整闭环验收。

## 3. S4-T01～S4-T04 交付与验收链

### S4-T01｜排查任务只读查询面

- 任务卡：`.ai-dev/TASKS/S4-T01.md`
- 验收：`.ai-dev/ACCEPTANCE/S4-T01.md`
- 任务卡提交：`d1409880892aa76e087675f04c0c31fe5ee9f849`
- 实现提交：`21536bef8549c9e4775f881eebbfde5143d8c10e`
- 只读补证提交：`7566c973cbb0359e3d7a39202690659eef82398f`
- 验收归档提交：`51a0bdc9669813b9a8ae39e32b8151b7f94593b9`
- 最终结论：通过；真实 SQLite 专项 10/10，查询完整业务图逐字段无写入

### S4-T02｜排查草稿保存与重新确认

- 任务卡：`.ai-dev/TASKS/S4-T02.md`
- 验收：`.ai-dev/ACCEPTANCE/S4-T02.md`
- 任务卡提交：`e056181f4a46c56587a0d6843b7c286a928ae8d2`
- 实现提交：`408ae7aad78e440c19ec3e18e8d7994c2570e014`
- 验收修正提交：`23c2b07b266a5df5686cc735f6f6b4be9f0112e2`
- 验收归档提交：`c205eb02c12d85318a29859a28e0d3e01968c50e`
- 最终结论：通过；真实 SQLite 专项 30/30，明确 null 保存和完整业务图无越界写入已补证

### S4-T03｜手工库存修正与商品归零编排

- 任务卡：`.ai-dev/TASKS/S4-T03.md`
- 验收：`.ai-dev/ACCEPTANCE/S4-T03.md`
- 任务卡提交：`016e6305d9b3b426014502aeca689142b02fd0eb`
- 实现提交：`7e4ad06c47c79d8bf29e4e4f779cebf8f3c71e80`
- 验收补证提交：`1e03e7da474b34121e7f51ecc49af3a434e16306`
- 验收归档提交：`0ffa31db7c8f206d250c0f549c7516af95045251`
- 最终结论：通过；真实 SQLite 专项 30/30，手工恢复后真正新批次仍由 S3-T05 接管已实证

### S4-T04｜正式排查提交事务

- 任务卡：`.ai-dev/TASKS/S4-T04.md`
- 验收：`.ai-dev/ACCEPTANCE/S4-T04.md`
- 任务卡提交：`8fe4c1dbecc3d64fb706dda2a60efdb164cb3aae`
- Luna 初始实现提交：`5b343e58d32db09d7cf8ea25c075a07bc65401e9`
- Sol 审查修正提交：`93218b5f25873683659d5139a32d73a4476e307c`
- 验收归档提交：`12b0a96464e7f9992d285ea853c1103572419b09`
- 最终结论：通过；真实 SQLite 专项 25/25，覆盖任务卡冻结的 54 项最低事实

## 4. 当前 Release、EF 与 migration 事实

在交接起点 HEAD `fc89842463606d2084e22a2a9c4bab654c9a7f9e` 重新执行：

- `.NET SDK`：`10.0.302`
- Release build：通过，0 警告、0 错误
- Release 全量测试：443/443 通过，0 失败、0 跳过
- EF model drift：`No changes have been made to the model since the last migration.`
- migration：8 条
- 当前模型：17 个 DbSet / 17 张业务表；S4-T01～S4-T04 未增加 migration、实体、配置或依赖
- `Inspection / InspectionItem` SQLite schema 与约束在 S4-T04 验收中 6/6 通过，本次 443 项全量回归再次覆盖

`dotnet ef migrations list` 对当前设计时默认数据库显示 8 条 `Pending`，表示该默认目标库未应用迁移，不是模型漂移或 migration 缺失；迁移文件与 ModelSnapshot 均完整存在。

## 5. S4-T01 查询能力与边界

生产入口：`src/StoreExpiryInspector/Application/Tasks/InspectionTaskQuery.cs`

- `Dashboard`：开放任务总数、四阶段商品数、固定最多 20 条紧急任务；同商品按开放 Task 的 `HighestStage` 计数一次。
- `SearchOpenTasks`：名称/编码/条码搜索、四阶段筛选、默认 50 条分页；排序复用 `ExpiryStageCalculator.GetStagePriority`，再按最近有效日期和 TaskId 稳定排序。
- `GetDetail`：区分 `open / not_found / completed / system_closed`；返回 Product、每个 TaskItem 自身阶段、当前到货量、版本、重新确认、Draft CheckedQty、正常活动 Batch 和每 Batch 最新正式 InspectionItem。
- 最新正式结果按 `SubmittedAtUtc DESC → InspectionId DESC → InspectionItemId DESC`。
- 全部 EF 查询 `AsNoTracking`，只返回普通 DTO/record；异常向上保留，正常空结果不伪装成失败。
- 明确边界：只读；不创建/补齐/失效/删除 Draft，不改 Task/Batch/版本，不接 WPF，不暴露 EF entity、DbSet 或 IQueryable。

## 6. S4-T02 Draft Save / Reconfirm / Clear

生产入口：`src/StoreExpiryInspector/Application/Tasks/InspectionDraftUseCase.cs`

- `SaveDraft`：对开放 Task 的唯一有效 Draft 原地 patch；请求未包含的 Item 保留，明确包含时严格保留 `null / 0 / 正整数` 区别。
- 顶部 `InspectorName / CheckDate` 是本次当前值写入，不是 Item patch；未来 UI 每次自动保存必须携带屏幕当前顶部值，避免传 null 意外清空。
- 旧观察版本下可保存用户输入，但普通保存不得更新 `ConfirmedAttentionVersion`、`RequiresReconfirmation`、TaskItem/Batch AttentionVersion 或阶段。
- `ReconfirmItem`：仅在开放 Task、当前 Item/Batch、有效 DraftItem、CheckedQty 非空、当前版本精确匹配且确实需要重新确认时原子确认；确认当前版本重放幂等。
- `ClearDraft`：只物理删除开放 Task 的有效 DraftItem/Draft；无有效 Draft 幂等；不删除系统失效 Draft，不处理已关闭 Task。
- `InspectionDraftReadiness` 只供 UI 提示，不是 S4-T04 正式提交授权。
- 用例拒绝带 pending changes 的 DbContext；自有事务失败整体回滚并清理 tracker，外层事务所有权不被抢占。

## 7. S4-T03 库存修正与 S3 生命周期关系

生产入口：`src/StoreExpiryInspector/Application/ManualInventoryAdjustmentUseCase.cs`

- 接受 ProductId、非负整数新库存、UTC 时间；0 库存必须显式二次确认，且门禁先于同值判断。
- 真实变化新增 `InventoryAdjustment`；Product 只更新 EffectiveStockQty、来源 `manual` 和 UpdatedAtUtc。同值返回 NoChange，不造历史。
- 正库存修正不改变 Batch、Task、Draft、Inspection、AttentionVersion 或 LifecycleEvent，也不把库存上升解释为新增到货。
- 修正为 0 时，先在同一事务保存 Adjustment 与 Product=0，再唯一调用 S3-T04 `ProductStockZeroLifecycleUseCase`，并传真实 AdjustmentId。
- S3-T04 是商品归零唯一状态机：停止全部 Batch、递增 Product generation、开放 Task 变 `system_closed`、有效 Draft 原地失效并保留、写批准事件。
- 手工恢复正库存不会恢复旧 Batch。之后只有 Stage 2 产生的真正新 Batch / 到货事实交给 S3-T05 `PostImportLifecycleUseCase`；旧批次恢复仍受 S3-T05 唯一例外和商品归零最高优先级约束。
- S4-T03 不调用 S3-T05/S3-T06，不复制任何生命周期规则。

## 8. S4-T04 正式提交事务与冻结语义

生产入口：`src/StoreExpiryInspector/Application/Tasks/InspectionSubmissionUseCase.cs`

### 数据重读与硬门禁

- 每次 Submit 在事务内重读 Product、Task/全部 TaskItem、Batch、有效 Draft/全部 DraftItem、库存和既有 Inspection；不信任页面、S4-T01 DTO、S4-T02 readiness 或调用方提交集合。
- 首次提交只允许开放 Task、完整有效 Draft、全部 CheckedQty 非空、合法检查人/日期、无需重新确认，且每项满足：
  `ConfirmedAttentionVersion == TaskItem.AttentionVersion == Batch.AttentionVersion`。

### HandledAttentionVersion

- 唯一语义：Batch 最近已经通过正式排查处理完成的 AttentionVersion 水位。
- 正式成功时以数据库当前事实设置 `HandledAttentionVersion = AttentionVersion`；AttentionVersion 本身不变。
- 只能与正式 Inspection/InspectionItem 在同一提交事务更新；Draft、Reconfirm、阶段推进、S3-T05、S3-T06 均不得更新。
- 初始二者都可为 0，所以“相等”不是已提交凭证；幂等只认 Task 状态、正式 Inspection 和 `Inspection.TaskId` 唯一约束。

### RequiresReconfirmation / ConfirmedAttentionVersion

- `RequiresReconfirmation == true` 必须拒绝提交，S4-T04 不自动确认或清除。
- `ConfirmedAttentionVersion` 必须与 TaskItem/Batch 当前 AttentionVersion 三方一致；陈旧草稿不能绕过提交。
- 普通 SaveDraft 不推进 ConfirmedAttentionVersion，只有 S4-T02 显式 ReconfirmItem 可以确认当前版本。

### 超库存确认

- CheckedQty 合计不超过当前有效库存时直接继续。
- 超库存首次返回 `RequiresOverStockConfirmation` 及当前库存/合计，业务图零写入。
- 二次确认必须同时携带并精确匹配上次返回的库存与合计；任一事实变化使旧确认失效并返回新警告，不存在永久布尔授权。

### 正式写入、S3-T06 与 Task 状态

- 创建 1 条 Inspection 和全部当前 TaskItem 对应的 InspectionItem；每个 Item 保存自身实际阶段，0 件 Item 不遗漏。
- 取得真实 Inspection/Item ID 后，对每个 CheckedQty=0 的 Item 调用既有 S3-T06 `BatchCheckedZeroLifecycleUseCase`。
- S3-T06 是批次 0 件停止的唯一实现：停止目标 Batch、写 `batch_checked_zero` LifecycleEvent；S4-T04 不直接改停止字段或复制事件规则。
- 正常正式提交将 Task 置 canonical `completed`，写 ClosedAtUtc/UpdatedAtUtc，`CloseReason` 保持 null。
- `system_closed` 专属于 S3-T04 等系统生命周期关闭，必须有真实 CloseReason；不得与 completed 混用。

### Draft 处置与原子性

- 正常成功提交在同一事务内先删除当前有效 DraftItem，再删除有效 Draft。
- 系统失效 Draft 永久保留；失败时有效 Draft 完整保留；AlreadySubmitted 不隐式清理异常残留 Draft。
- Inspection/Item、S3-T06、Handled版本、Task completed 与 Draft删除处于同一 SQLite 外层事务，任一步失败整体回滚。

## 9. UI/UX Pro Max 治理结果

- 治理归档提交：`fc89842463606d2084e22a2a9c4bab654c9a7f9e`
- 权威文档：`.ai-dev/UI/STAGE-4-UI-BASELINE.md`
- 定位：Windows 原生浅色、数据密集、平面克制、低动效；面向门店长期生产操作，不是展示型 SaaS。
- 采用：Inventory & Stock Management、Data-Dense Dashboard、Flat Design、WPF 键盘/可访问性/DPI 指导。
- 排除：营销式 Bento、巨型 KPI 卡墙、巨量留白、玻璃拟态、装饰渐变、复杂动画和移动端式超大控件。
- UI/UX Pro Max 只治理视觉与交互，不得改写 Application、数据库或任何已冻结业务语义。

## 10. 已冻结的 Stage 4 UI 基线摘要

以下仅为摘要，具体数值与门禁以 `.ai-dev/UI/STAGE-4-UI-BASELINE.md` 为准：

- Shell：固定左侧导航 + 右侧 `*` 工作区；页面标题和全局加载/错误有固定位置。
- 首页：数据新鲜度、紧凑阶段统计条、最多 20 条紧急任务；不做满屏卡片墙。
- 待排查列表：搜索、四阶段筛选、50 条分页；优先显示阶段、商品、最近有效日期和“去排查”；Loading/Empty/FilterEmpty/Error 分离。
- 排查详情：高可见商品头、连续录入 DataGrid、正常批次默认折叠、固定底部“完成排查”。空白、0、正数必须视觉与语义分离。
- 草稿状态：`有未保存更改 / 正在保存 / 已保存 / 保存失败` 安静行内显示；同 Task 保存请求串行。
- RequiresReconfirmation：保留 CheckedQty，用独立紫色语义和完整文字提示，用户显式重新确认；普通编辑不解除。
- 库存修正：库存默认只读、次级入口；修正为 0 使用强二次确认，取消为默认焦点，危险按钮不响应默认 Enter。
- 超库存警告：显示当前库存、排查合计和超出量；提供返回修改、修正库存、确认仍提交，不伪装成不可继续的红色错误。
- 正式提交：详情页唯一 Primary 为“完成排查”；UI 先定位问题，最终门禁只认 S4-T04；成功后不能保留可编辑旧页。
- DPI/分辨率：默认 1280×760 DIP，最小 1024×600 DIP；1366×768/125% 保持完整操作，1920×1080 扩展表格而非增加空白；必测 100%/125%/150%。
- 键盘连续录入：Tab/Shift+Tab 顺序一致，Enter 下一件数、Shift+Enter 上一行、Esc 只撤销当前单元编辑、Ctrl+S 只保存草稿、Ctrl+F 聚焦列表搜索；无直接正式提交快捷键。
- 视觉 token：Segoe UI/Microsoft YaHei UI；Canvas `#F5F7FA`、Surface `#FFFFFF`、PrimaryAction `#1F5FBF`、Danger `#B42318`、Warning/Success/Reconfirm 独立语义色；4～32 DIP 间距，4～8 DIP 圆角，44～56 DIP 表格行高。

## 11. 两个尚未解决的 UI 契约缺口

### Dashboard 最近成功导入时间

S4-T01 `InspectionDashboardResult` 当前不含最近成功导入时间，仓库没有职责匹配的轻量 Dashboard 导入状态查询。`ImportUndoEligibilityService` 会执行撤销资格及文件校验，不适合仅为显示时间而调用。

### “导入最新 Excel”真实承接页面

当前 WPF 只有占位 MainWindow，没有真实数据导入页面。首页若直接显示可点击“导入最新 Excel”，会形成无响应入口或诱使 S4-T05 越界实现完整导入 UI。

### 强制边界

- 不得让 ViewModel/code-behind 直接查询 EF、DbSet 或 IQueryable。
- 不得误用 `ImportUndoEligibilityService` 绕路取得展示时间。
- 不得用无响应按钮、伪造时间或假导航冒充功能存在。
- 是否以最小 Application 只读 DTO 补充、延期展示或只在真实目标存在后启用入口，必须在 S4-T05 任务卡创建前由新任 PM 明确，但本交接不作新决策。

## 12. 新任 PM 首轮重新核验入口

### 治理文件

1. `.ai-dev/PROJECT_STATUS.md`
2. `.ai-dev/STAGES/STAGE-4-PM-HANDOFF-S4-T05.md`
3. `.ai-dev/UI/STAGE-4-UI-BASELINE.md`
4. `.ai-dev/DECISIONS.md`
5. `.ai-dev/ARCHITECTURE.md`
6. `.ai-dev/DATA_MODEL.md`
7. `.ai-dev/TEST_STRATEGY.md`
8. `.ai-dev/TASKS/S4-T01.md`～`S4-T04.md`
9. `.ai-dev/ACCEPTANCE/S4-T01.md`～`S4-T04.md`
10. `.ai-dev/ACCEPTANCE/S3-T04.md`～`S3-T06.md`

### 生产源码

1. `Application/Tasks/InspectionTaskQuery.cs`
2. `Application/Tasks/InspectionDraftUseCase.cs`
3. `Application/ManualInventoryAdjustmentUseCase.cs`
4. `Application/Tasks/InspectionSubmissionUseCase.cs`
5. `Application/ProductStockZeroLifecycleUseCase.cs`
6. `Application/PostImportLifecycleUseCase.cs`
7. `Application/BatchCheckedZeroLifecycleUseCase.cs`
8. `Domain/ExpiryStageCalculator.cs`
9. `Application/Imports/ImportUndoEligibilityService.cs`
10. `UI/MainWindow.xaml`、`UI/MainWindow.xaml.cs`、`App.xaml`、`App.xaml.cs`

接手时必须重新执行 branch、HEAD、status、最近提交、Release build/全量、EF drift、migration 数量及 S4-T05 不存在检查；不得把本次 443/443 当作未来 HEAD 的永久事实。

## 13. S4-T05 候选边界（不是任务卡）

候选范围仅限：

- WPF Shell。
- 首页 Dashboard。
- 待排查任务列表。
- S4-T01 查询 DTO 接入、搜索、筛选、50 条分页和加载/空/筛选空/错误状态。
- UI 基线的语义资源、按钮层级、阶段徽标、键盘焦点和 1366/1920/DPI 基础验收。
- 在创建任务卡前明确两个 UI 契约缺口的最小处置；不得默认扩展完整数据导入 UI。

候选排除：排查详情编辑、Draft 写入、Reconfirm、库存修正、正式提交、超库存确认、Stage 5+、新业务状态机、ViewModel 直查 EF、新 migration 或新依赖。

本节不构成批准，不得据此直接实施。新任 PM 必须先完成仓库复核并获得用户对 S4-T05 的明确创建授权。

## 14. Stage 4 剩余路线与完成门禁

1. `S4-T05`：WPF Shell + 首页 + 待排查列表。
2. `S4-T06`：WPF 排查详情 + Draft/重新确认/库存修正。
3. `S4-T07`：正式提交 UI + 超库存确认 + 成功/陈旧状态 + Release 人工完整闭环验收。

只有 S4-T05～S4-T07 分卡正式验收通过，并完成 Stage 4 Release 全量、真实 WPF 人工闭环、阶段整体验收与风险收口后，才允许 Stage 4 归档并生成 Stage 5 handoff。当前不得提前进行 Stage 4 总验收、归档或 Stage 5 规划。

## 15. 已知风险、架构债与禁止重复实现

### 已知风险与债

- S4-T01 查询类为具体单类并会在过滤后 materialize 以复用权威阶段优先级；当前正确性已验收，大数据性能留后续性能阶段，不得为 S4-T05 先造 Query Framework。
- 每 Batch 最新 InspectionItem 会读取目标商品相关正式历史；10 万 Batch/30 万历史性能尚未实测。
- Application 业务拒绝多以异常表达；UI 不得解析异常字符串推导业务状态，应在失败后重新调用 GetDetail 并保留通用可恢复错误。
- 自动保存顶部字段为当前值、Items 为 patch，且并发请求可能乱序；UI 必须串行保存并明确 dirty/saving/saved/failed。
- 当前未发现 PerMonitorV2 manifest；若后续需要补 manifest，必须写入对应任务卡并真实验证 DPI，不能顺手修改。
- 当前 MainWindow 是 960×640 占位页，尚无 ViewModel、导航或导入页面；不要把占位启动成功误报为 Stage 4 UI 可用。

### 禁止在 WPF/ViewModel 重复实现

- 效期阶段计算和 `ExpiryStageCalculator.GetStagePriority`。
- Task 生成/聚合、HighestStage、AttentionVersion 变化与 RequiresReconfirmation 状态机。
- Product 归零、generation、旧 Batch 永不恢复与唯一恢复例外。
- 正式排查 0 件停止和 LifecycleEvent。
- HandledAttentionVersion、ConfirmedAttentionVersion、超库存确认、Draft处置、completed/system_closed 判定和正式提交事务。
- UI readiness 只能帮助定位问题，不能替代 S4-T04 重新读库授权。

## 16. 交接停止点

- S4-T05 不存在，未创建、未派发、未实现。
- 本次只允许交接文档及必要状态同步；无生产代码、WPF、数据库、migration、依赖或产品规则变化。
- 当前产品经理完成本交接提交后停止 Stage 4 职责。
- 新任 GPT-5.6 Sol 接任后，必须先依据仓库事实重新核验，再等待用户明确授权创建 S4-T05。

再次强调：**本文件是导航与审计索引，不是业务规则的新权威来源。若本文件与任务卡、验收、源码或 UI 基线不一致，以仓库中的原始权威证据为准，并先报告差异，不得自行推断。**
