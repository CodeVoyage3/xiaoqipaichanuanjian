# 架构事实与阶段边界

> 更新于 2026-08-31。Stage 7 已总验收并收口；下方保留 Stage 5 架构图及早期债务记录，Stage 6～7 当前边界见本文末节。不得将历史开工缺口当作当前未交付功能。

## Stage 5 整体通过时的代码结构

```text
StoreExpiryInspector.slnx
├─ src/StoreExpiryInspector                 单一 .NET 10 WPF 应用项目
│  ├─ Domain/ExpiryStageCalculator         纯效期阶段与下一触发日
│  ├─ Application
│  │  ├─ Imports                           Stage 2 导入 + Stage 3 原子后置编排
│  │  ├─ Tasks/InspectionTaskQuery         Stage 4 只读 Dashboard/任务/详情查询
│  │  ├─ Tasks/InspectionDraftUseCase      Stage 4 草稿、重新确认与主动清空
│  │  ├─ ManualInventoryAdjustmentUseCase  Stage 4 手工库存修正
│  │  ├─ Tasks/InspectionSubmissionUseCase Stage 4 正式提交事务
│  │  ├─ Tasks/InspectionHistoryQuery       Stage 5 正式历史/详情/Revision 只读查询
│  │  ├─ Tasks/InspectionHistoryEditUseCase Stage 5 单条正式明细数量修订事务
│  │  ├─ Tasks/ProductTaskAggregator       商品唯一开放任务聚合
│  │  ├─ StartupRecalculationUseCase       活动到期 Batch 启动补算
│  │  ├─ ProductStockZeroLifecycleUseCase  商品归零生命周期
│  │  ├─ PostImportLifecycleUseCase        新批次、新到货与恢复
│  │  ├─ BatchCheckedZeroLifecycleUseCase  正式 0 件停止目标 Batch
│  │  └─ ApplicationStartupCoordinator     补算、时钟回拨与运行日期
│  ├─ Infrastructure                       SQLite/EF、日志、备份、Excel
│  ├─ Migrations                           8 条 EF Core migration
│  ├─ App.xaml.cs                          初始化、入口时钟、启动编排与日志
│  └─ UI                                   Stage 4 工作流 + Stage 5 历史查看/修改
└─ tests/StoreExpiryInspector.Tests         583 项 Release 测试
```

- schema 仍为 17 张业务表、17 个实体/配置/DbSet 和 8 条 migration；Stage 4/5 未新增依赖或 schema。
- canonical phase 为 `none / discount_50 / discount_20 / withdraw / expired`；阶段与下一触发日只由 Domain 计算器定义，优先级只由其权威映射定义。
- `ConfirmedImportLifecycleOrchestrator` 在同一事务中组合 Stage 2 与 S3-T04/S3-T05；`ConfirmedImportExecutor` 没有 Stage 3 状态机。
- WPF 启动已接入 DatabaseInitializer、ApplicationStartupCoordinator 与 LocalFileLogger；系统时钟只在 App 边界读取。
- S4-T05～S4-T10 已完成 WPF Shell、首页、待排查列表、数据导入、详情、草稿、重新确认、库存修正、正式提交及最终 UI 定稿；UI 只调用既有 Application 权威。
- S5-T01～S5-T04 已完成 completed 正式历史列表/详情/Revision 查询、单条数量修订及 WPF 查看/修改；历史修改不调用 Stage 3 Lifecycle 或 Stage 4 Submission。

## Stage 2 历史基线

```text
StoreExpiryInspector.slnx
├─ src/StoreExpiryInspector                 单一 .NET 10 WPF 应用项目
│  ├─ Domain                               17 个数据实体；当前无业务状态转换方法
│  ├─ Application/Imports                  只读差异规划、文件身份守卫、原子导入执行与撤销资格判断
│  ├─ Infrastructure
│  │  ├─ Configurations                    17 个独立 EF Core 实体配置
│  │  ├─ StoreDbContext                    17 个 DbSet 与显式配置注册
│  │  ├─ DatabaseInitializer               SQLite 路径、外键、migration、WAL 基础能力
│  │  ├─ Logging/LocalFileLogger           JSON Lines 本地日志基础能力
│  │  ├─ Backups/PreImportSnapshotService  SQLite 在线快照、验证与原子发布
│  │  └─ Excel                             固定 `.xlsx` 只读解析、纯内存校验分类、普通 DTO 与 SHA-256
│  ├─ Migrations                           8 条 EF Core migration
│  └─ UI                                   仅有占位主窗口
└─ tests/StoreExpiryInspector.Tests         178 项测试
```

- 技术栈：`net10.0-windows`、WPF、EF Core SQLite、Open XML SDK 3.5.1；除此之外未增加 Excel 依赖。
- 数据库默认路径已实现为 `%LOCALAPPDATA%/StoreExpiryInspector/data/app.db`；连接启用外键，`DatabaseInitializer.Initialize` 可执行 migration 并切换 WAL。
- `App.xaml.cs` 当前为空，尚未把数据库初始化、日志或业务用例接入真实启动流程。
- `Application/Imports/ExcelImportPlanner` 只查询本次涉及的商品与其批次并使用 `AsNoTracking`，不调用 SaveChanges；持久化只由独立 `ConfirmedImportExecutor` 执行，当前不存在 Stage 3 状态机、提醒、托盘、自启动、完整恢复服务或业务 UI。
- `ImportConfirmationGuard` 在无数据库依赖下绑定预览 SHA，确认前重读并冻结已验证字节；文件变化、缺失、不可用或计划无变化均不会产生可确认契约。
- `PreImportSnapshotService` 以 SQLite 在线备份从只读源连接创建临时快照，核对完整性、外键、表、migration、SHA 和大小后原子发布；结果和追溯元数据只留在内存，不写业务库。
- `ConfirmedImportExecutor` 消费已冻结确认契约与既有计划，再次复核文件、创建快照、拒绝陈旧计划，并在单一 SQLite 事务内写 Import、BackupRecord、Product/Batch 增量、Issue 与原始 Workbook；不调用前序解析/分类/规划器。
- 同一确认事务在新 Workbook 保存后，按 Succeeded Import 的 `ConfirmedAtUtc DESC, Id DESC` 保留最近两条并删除更旧 Workbook 子记录；Import 及其他历史不裁剪，失败时新写入和旧删除一起回滚。
- `ImportUndoEligibilityService` 只选择最新 `Succeeded && !IsUndone` Import，复用快照服务验证唯一 BackupRecord、SHA、完整 schema 和 migration；再将九张正式业务/草稿表与导入前快照逐字段比较。它不执行恢复、不写 Undone，也不允许指定历史 Import。
- 本地日志器已实现 UTF-8 无 BOM JSON Lines、按本地自然日滚动、仅保留最近 14 个合法命名日志文件；尚未接入具体业务日志。
- 单实例运行是已批准架构方向，但当前尚未实现进程互斥。

## 已落实的数据架构

- SQLite 当前有 17 张业务表；一实体一配置，具体映射不堆入 `StoreDbContext`。
- 外键普遍使用 `NO ACTION`，防止级联删除历史或审计数据。
- 商品编码唯一、两种批次部分唯一索引、每商品最多一条开放任务等关键不变量由 SQLite 约束承担。
- 商品/批次的确认导入、导入记录/工作簿/异常与导入前备份元数据已由 S2-T06 完成事务编排；任务/草稿、正式排查/修改历史、库存修正、设置/运行状态和生命周期事件仍只有持久化底座，Stage 3+ 业务编排尚未实现。
- 生命周期事件不是通用事件总线，只保存五类已批准事件事实；事件创建条件与状态转换不得下沉到 EF 配置。

## Stage 2 已完成边界（S2-T01 至 S2-T08）

后续 Excel 增量导入仍应保持三段式：

1. `解析`：S2-T01 已实现只读打开固定模板首工作表、表头 Trim、必要列/重名拒绝、普通 DTO 与文件哈希；尚不做业务分类。
2. `规划`：S2-T02 已完成文件内分类；S2-T03 已只读查询相关 Product / Batch 并生成新增、更新、无变化和问题预览，不修改数据库实体。
3. `确认与导入基础`：S2-T04 已实现确认前文件哈希复核和内存契约；S2-T05 已实现导入前安全快照；S2-T06 已实现单个 SQLite 写事务及原始工作簿保存；S2-T07 已实现最近两份成功工作簿裁剪；S2-T08 已实现最新 Import 的只读撤销资格与快照关联。真正恢复和 Undone 写入仍未实现，Stage 2 未顺带执行 Stage 3 状态机。

最高优先级边界：Excel 是局部增量数据，不是全量快照。未出现在本次文件中的商品或批次不得进入变更集，不得被删除、停止跟踪、关闭任务、修改库存或改变历史。

Open XML SDK 3.5.1 已由 S2-T01 最小加入并通过官方源漏洞审计；不得增加第二个 Excel 依赖或提前实现导出回填。

## 业务规则归属

- UI 只负责展示、输入和调用用例，不承载业务规则。
- 后续 Application 用例负责事务编排和跨实体一致性。
- 可纯计算的效期阶段与状态转换规则属于 Domain；Domain 不依赖 WPF、EF Core 或 Open XML。
- EF 配置只表达字段、索引、值域、关系和删除行为，不实现导入决策、库存归零、排查 0 件或恢复规则。
- 解析 DTO、变更计划和数据库实体必须分离；预览不得直接持有可写 DbContext。

## 后续事务边界

- 一次确认导入：S3-T07 已用外层事务组合 Stage 2 导入事实与 S3-T04/S3-T05 后置生命周期，失败整次回滚；Stage 2 执行器本身不承载 Stage 3 规则。
- 一次商品排查提交已由 S4-T04 实现：任务、正式排查、明细、有效草稿处置与 S3-T06 批次状态/事件处于同一事务，WPF 只调用该入口。
- 一次历史修改：只在同一事务更新正式 InspectionItem 当前数量并新增 Revision；不做 Lifecycle、Batch/Task/Draft/AttentionVersion 或旧提交状态重算。
- 商品库存归零：商品、相关批次、开放任务、草稿和事件同事务。
- 导入撤销执行仍未实现，S2-T08 只提供只读资格判断；Stage 7 已另行完成本地整库安全备份/恢复，不等同于确认导入撤销，也不自行决定 Undone 与工作簿语义。

## 已知技术边界

- SQLite 部分旧 migration 在重建表时会输出 `PRAGMA foreign_keys` 不能位于事务内的 EF 提示；空库逐级升级和七段旧数据保留测试均通过，但进程中断可能留下部分迁移状态，后续升级流程仍必须先做可恢复快照。
- `LocalFileLogger` 使用进程级全局锁，只保证同一进程内写入完整；符合当前单实例方向，未来出现多实例或明显吞吐瓶颈时再调整。
- Windows 10 具体门店版本未知，必须实机验收；V1 未签名 EXE 的 SmartScreen“未知发布者”是已接受限制。
- Stage 6 托盘、提醒与当前用户自启动已由用户 Windows 验收；真实样表 round-trip、安装运行、休眠恢复专项和 10 万批次 + 30 万历史记录性能仍未在本轮验证。

## Stage 2 架构债检查

- 无阻断级债务：解析、分类、规划、确认、快照、事务写入、工作簿裁剪和撤销资格仍由不同组件承担；没有 Repository/UnitOfWork、单实现接口、反射扫描、通用事件总线、文件版本框架或第二个 Excel 依赖。
- `ConfirmedImportExecutor.cs` 为 1,150 行，是当前最大生产文件；其中包含结果 DTO、计划形状/陈旧性白名单校验、明确字段应用和单事务编排。职责仍限于持久化阶段，暂不为拆文件引入新抽象，但 Stage 3 状态机严禁继续加入该类；若后续再次修改其规则，应先按现有私有职责拆成少量具体协作者。
- `ExcelImportPlanner.cs`、`PreImportSnapshotService.cs` 和 `ImportUndoEligibilityService.cs` 也因 DTO/固定 schema 比较而偏长，但边界单一、测试充分。少量路径、SHA 与 SQLite 引用辅助代码存在重复；当前直接代码比通用文件/SQLite 框架风险更低，出现第三个真实业务调用方或实际缺陷时再提取。
- 测试基础设施曾因全局 `ClearAllPools` 产生并行竞态；已收窄为当前临时数据库连接池，连续全量回归未再复现。

## Stage 3 架构债检查

- S3-T01～T06 各自保持一个业务权威边界；S3-T07 只调用这些 UseCase，没有复制阶段、聚合、归零、新到货、恢复或停止规则。
- `ConfirmedImportExecutor.cs` 当前 1,154 行，较 Stage 2 只增加 12 行事务所有权适配；禁止继续加入生命周期或 UI 规则。
- `ConfirmedImportLifecycleOrchestrator.cs` 467 行，职责限于明确事实冻结/解析、两个已有 UseCase 的优先级调用与统一事务；偏长但没有第二职责或未来抽象，当前不拆。
- `ApplicationStartupCoordinator.cs` 86 行，`App.xaml.cs` 46 行；入口没有业务查询或状态机。
- 未发现 Repository/UnitOfWork、单实现接口、God Service、EventBus/Outbox、通用状态机/工作流、第二套效期算法或新依赖。无阻断级架构债。

## Stage 6～7 收口后的当前边界

- Stage 6 的 Application/Reminders、App 与原生 Windows Tray/设置负责提醒、同日幂等、单实例、scheduler 和 HKCU 自启动；详见 STAGE-6-CLOSEOUT。
- Stage 7 的 Application/Backups 复用共享快照，提供安全备份、最小只读可恢复列表与安全恢复；UI/DatabaseRuntimeGate 和现有 Shell/App 只协调调用，等待保存与在途操作、停止 scheduler，成功/critical 后锁定并退出。
- schema、8 条 migration 与包依赖相对 Stage 6 不变；工程仅增加已归档 T02 的测试可见性声明。Domain、既有业务事务及状态权威不得因视觉改版重写。
- Stage 7 已正式完成；正式历史/结果 Excel 导出 deferred，未经用户重新批准不得补做。下一步只交接 UI/UX 统一重构，不创建实施 Task，不进入 Stage 8。当前契约见 `.ai-dev/STAGES/STAGE-7-CLOSEOUT.md`，交接见 `.ai-dev/STAGES/UI-UX-REFRESH-HANDOFF.md`。
