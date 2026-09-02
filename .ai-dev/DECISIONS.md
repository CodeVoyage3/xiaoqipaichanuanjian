# 关键决策

> 更新于 2026-08-30。状态中的“已批准”不等于代码已实现；每项另列当前事实。

## D-001｜桌面技术栈

- 状态：已批准；Stage 1 骨架已实现。
- 决策：`.NET 10 LTS + WPF`，目标 `win-x64`，最终自包含发布。
- 当前事实：应用目标为 `net10.0-windows`，尚未制作自包含发布或安装包。
- 边界：不引入 Web 服务、Electron、Python 运行时或跨平台 UI 框架。
- 风险：门店 Windows 10 具体版本未知，必须实机验证；V1 接受未签名 EXE 和 SmartScreen“未知发布者”。

## D-002｜最小代码结构

- 状态：已批准；Stage 1 已落实。
- 决策：一个 WPF 应用项目和一个测试项目；按 `Domain`、后续 `Application`、`Infrastructure`、`UI` 分层。
- 当前事实：已有 Domain 纯效期计算、Application 导入/任务/生命周期/启动/排查/历史用例、Infrastructure 与 Stage 4/5 WPF UI；17 个实体各自使用一个 EF 配置。
- 边界：不创建单实现接口、Repository、UnitOfWork、通用事件总线或反射扫描；第二个真实实现出现前不抽象。

## D-003｜数据库与迁移

- 状态：已批准；Stage 1 数据底座已实现并整体通过。
- 决策：SQLite 单库、EF Core SQLite provider、版本化 migration、启用外键并使用 WAL；业务日期存 ISO 文本，时间戳存 UTC，数量使用整数。
- 当前事实：17 张业务表、8 条 migration；默认数据库路径、migration/WAL 初始化已接入真实应用启动并通过空库运行验收。
- 升级要求：正式升级前必须创建可恢复副本；迁移失败不得进入主界面继续写入。
- 已知边界：旧 migration 进行 SQLite 表重建时会提示 `PRAGMA foreign_keys` 操作不能包含在事务内。空库逐级升级及旧数据保留测试已通过，但异常中断风险仍要求升级前快照。

## D-004｜Excel 局部增量、隔离与格式保真

- 状态：Stage 2 八卡与整体验收已通过；S3-T07 已以独立外层编排接入 Stage 3 后置状态转换；真正撤销执行仍未实现。
- 最高原则：Excel 是局部增量数据，不是全量快照；未出现的历史商品/批次不得因此删除、停止跟踪、关闭任务、改变库存或修改历史。
- 识别：表头先 Trim 等规范化；规范化后重复表头按文件级异常拒绝。食品判定字段为 `商品大类`，规范化后的值必须为 `食品`。
- 架构：Excel I/O 集中于 `Infrastructure/Excel`，向上只交付普通 DTO；解析、变更规划、事务应用分离。
- 规划事实：`Application/Imports/ExcelImportPlanner` 只按本次商品编码与相关 Product ID 执行两类 `AsNoTracking` 查询，输出普通 DTO；未出现的历史数据不进入计划，预览不写库。
- 确认事实：`ImportConfirmationGuard` 以预览 SHA 重读验证同一路径文件，成功契约保存隔离的已验证字节；无变化、文件变化、缺失或不可用均不创建正式记录。
- 写入事实：`ConfirmedImportExecutor` 再次复核文件并先创建安全快照，只应用既有计划白名单；Import、BackupRecord、Product/Batch、Issue、Workbook 在单一事务提交，失败全部回滚且快照文件保留。
- 撤销资格事实：`ImportUndoEligibilityService` 只能选择最新成功且未撤销 Import，验证唯一快照关联和完整 SQLite schema/migration，并比较九张正式业务/草稿表；它不恢复数据库、不写 Undone、不接受历史 Import Id。
- 库选择：`.xlsx` 使用 Open XML SDK 3.5.1；S2-T01 已最小引入，S2-T02 未增加依赖。
- 格式保真：导出阶段原位回填最新原始工作簿最后三列，不重建工作簿；该能力不属于 Stage 2。
- 样表：仓库固定基线 `test-data/食品效期排查表.xlsx` 已完成真实只读解析和文件内分类回归；round-trip 不属于 Stage 2，尚未验证。

## D-005｜托盘、提醒与自启动

- 状态：已批准，尚未实现。
- 决策：WPF 主进程；关闭主窗仅隐藏，显式退出才结束；当前用户注册表自启动；电源恢复触发到期检查；每个本地自然日最多主动提醒一次。
- 当前事实：仅有 `settings`、`app_state` 数据底座，无调度、注册表、托盘或电源事件代码。

## D-006｜效期规则扩展

- 状态：V1 `food_v1` 纯计算已由 S3-T01 实现并验收；未来扩展仍未实现。
- 决策：商品保存 `category_code` 与 `policy_code`；V1 只实现 `food_v1`，未来新增策略与配置数据，不修改商品/批次主键和历史表。
- 边界：V1 不建动态 DLL 插件系统或门店可编辑规则界面。

## D-007｜安装与数据位置

- 状态：数据路径基础已实现；安装交付尚未实现。
- 决策：应用和业务数据分离，业务数据库位于当前用户本地应用数据目录；默认卸载不删除业务数据。
- 安装：Stage 9 使用 Inno Setup；V1 不做在线升级。

## D-008｜工作簿与数据库备份

- 状态：数据表、导入前快照文件服务、成功导入工作簿保存、最近两份保留及最近 Import 撤销资格判断已实现；恢复执行策略尚未实现。
- 决策：正式导入、恢复、版本升级前创建安全快照；自动备份保留最近 7 份；最近 2 份成功导入原始 Excel 以 BLOB 和 SHA-256 保存于 SQLite。
- 当前事实：已有 `backups`、`import_workbooks` 和相关约束；S2-T05 已实现 SQLite 在线快照，S2-T06 在成功导入事务中保存 BackupRecord 与原始工作簿，S2-T07 在同一事务内只保留最近两条 Succeeded Workbook，S2-T08 验证最新 Import 的原始快照和后续业务阻断。恢复及 Undone 工作簿语义仍未实现。
- 原则：快照先写临时文件、验证后原子改名；恢复前先备份当前状态。

## D-009｜商品与批次唯一身份

- 状态：已批准；数据库唯一约束、Stage 2 导入识别/写入和 Stage 3 新旧批次生命周期均已实现。
- 商品：`商品编码` 是唯一主体。名称和条码变化仍是同一商品并更新当前值；历史快照不改。编码变化是新商品；编码为空进入异常。
- 批次：有生产日期时唯一键为“商品编码 + 生产日期 + 有效日期”；无生产日期时为“商品编码 + 有效日期”。历史出现过即永远是旧批次，停止后再次出现不得冒充新批次。

## D-010｜库存归零、排查 0 件与累计到货恢复

- 状态：业务规则已批准；S3-T04 已实现商品库存归零，S3-T05 已实现导入后真正新批次、真正新增到货与批次恢复，S3-T06 已实现正式排查 0 件后的批次停止。
- 排查 0 件：正式 InspectionItem 当前检查量为 0 时只停止该批次，使用 `stopped + batch_checked_zero` 并清空下一触发日；Product、其他 Batch、Task/Draft 和正式历史不变，AttentionVersion 不变。
- 商品归零：只有本次 Excel 明确包含该商品且无库存冲突、库存明确为 0，才结束该商品全部历史及当前批次、关闭开放任务并使草稿失效；优先级高于新批次、累计到货和阶段触发。
- 生命周期代：首次商品级归零时 Product 的 `lifecycle_generation` checked 递增一次并永久保留 `is_stock_zero_terminated = true`；归零前已存在的 Batch 保留其历史代号，只停止跟踪并清空下一触发日。重复归零不再递增；库存重新大于 0 也不回退代号、不清除归零标记、不恢复历史 Batch。
- 真正新到货：仅当当前累计到货首次大于历史最高值；下降或回升到旧最高值不触发。
- 关注版本与幂等：真正新增到货使用现有 `Batch.attention_version` checked 加一；冻结请求以 expected/target compare-and-set 持久化区分首次应用、精确重放和冲突，不新增处理标记。真正新批次只允许从 Stage 2 新建后的原始默认 Batch 状态首次启动，离开该状态后的同一 new 事实重放不得降级阶段或恢复已停止 Batch。
- 批次 0 事实幂等：`batch_checked_zero + ProductId + BatchId + SourceInspectionId` 的既有 LifecycleEvent 是当前批准的持久化处理锚点；同一正式 0 事实重复调用无副作用，S3-T05 合法恢复后重放旧 0 事实也不得重新停止。Stage 5 已明确把历史数量 Revision 与 Lifecycle 隔离；未来如需联动必须另行批准，不得重放旧 0 事实。
- 恢复例外：仅因批次 0 件停止、商品从未发生商品级归零、当前库存大于 0且累计到货突破历史最高值时，才恢复同批次并直接计算当前最高阶段。
- 商品曾归零后：所有历史旧批次永久不得恢复；库存重新大于 0 时只允许数据库从未出现过的真正新批次进入新生命周期。

## D-011｜阶段门禁

- 状态：Stage 0～Stage 5 均已整体验收通过；当前暂停在 Stage 5 → Stage 6 门禁。
- 决策：Stage 5 已按 S5-T01 至 S5-T04 完成逐卡治理、实现、独立验收和必要用户 GUI 验收。
- 流程：已生成 Stage 6 handoff，但未创建、编号或派发 S6-T01；接任必须先复核事实，用户单独批准后才允许创建第一张最小任务卡。

## D-012｜Stage 2 正式导入状态与任务计数占位

- 状态：已批准；S2-T04 已在确认契约中落实，S2-T06 已写入正式成功导入记录。
- `imports.status` 在 Stage 2 的最小合法业务集合只有 `Succeeded`、`Undone`。
- 解析、预览、失败、取消和无变化过程不写入正式 `imports` 记录，不为这些过程扩充状态值。
- Stage 2 不实现任务引擎，也不计算 `new_task_product_count`。
- 当前 schema 中 `new_task_product_count` 为非空整数，因此 Stage 2 正式导入记录暂写 `0`；该值在 Stage 3 前没有“新增任务数量”的业务含义，Stage 2 预览不得展示为新增任务数量。
- 不为上述占位修改 Stage 1 已验收 schema。

## D-013｜Stage 3 规则权威与运行编排

- 状态：已实现并整体验收通过。
- canonical phase：`none`、`discount_50`、`discount_20`、`withdraw`、`expired`；不得新增平行命名，阶段优先级不得按字符串排序。
- 纯计算：业务日期由调用方显式传入；Domain/Application 不读取系统当前时间。离线跨阶段直接计算当前结果，不回放历史任务。
- 任务：批次触发、商品聚合，每商品最多一条开放任务；Item 保存自身阶段，HighestPhase 由权威优先级重算。
- 运行编排：完整确认导入由外层事务组合 Stage 2 与 S3-T04/S3-T05；WPF 启动只在入口取得时间并调用 S3-T03。`ConfirmedImportExecutor` 不承载 Stage 3 状态机。
- 边界：S3-T06 只提供正式 0 件 Batch 停止能力；创建 Inspection、完成任务和调用 S3-T06 的完整提交事务已由 S4-T04 唯一实现并冻结。

## D-014｜Stage 4 拆分与逐卡门禁

- 状态：Stage 4 已整体验收并归档。
- 顺序：S4-T01 查询、S4-T02 草稿、S4-T03 库存修正、S4-T04 正式提交、S4-T05 首页/任务列表、S4-T06 数据导入 UI、S4-T07 详情/草稿/重新确认/库存修正 UI、S4-T08 提交 UI、S4-T09 UAT、S4-T10 最终 UI 定稿。
- 门禁：Stage 4 业务与 UI 边界已冻结；Stage 5 不得重新实现 S4-T04 提交事务或 Stage 3 生命周期。
- S4-T01：只建立 Application 只读查询 DTO，不接 WPF、不写库、不修改 Stage 3、实体、EF、migration 或依赖。

## D-015｜Stage 4 WPF 视觉与交互治理

- 状态：`.ai-dev/UI/STAGE-4-UI-BASELINE.md` 与 S4-T10 刷新基线均已用于实现；用户最终 GUI 验收通过。
- 规范：从 S4-T05 开始统一以 UI/UX Pro Max 作为页面布局、信息层级、视觉层次、字号间距、列表密度、按钮层级、状态反馈、空/错/加载状态、Windows 可访问性、1366/1920 适配和长期桌面操作舒适度的参考规范。
- 目标：高信息密度、低学习成本、少点击、清晰状态、适合门店长期桌面操作；不是展示型 SaaS。
- 禁止：大面积无效留白、过度卡片化、大量动画、装饰性渐变、隐藏关键字段或让核心排查经过多层点击。
- 业务边界：UI/UX Pro Max 不得改写效期阶段、Task/Draft/Inspection、库存归零、批次 0 件停止、AttentionVersion、提交条件、数据模型、Application 状态机或 SQLite 事务边界。
- 门禁：S4-T01～S4-T04 完成后、创建 S4-T05 前，必须单独执行一次 UI/UX Pro Max 设计治理，覆盖主窗、导航、首页、任务列表、详情、正常批次、草稿/重新确认、库存修正、警告/确认/成功及空错加载状态，并形成统一 Stage 4 UI 基线；Luna 不得提前自行设计完整视觉系统。
- 基线方向：Windows原生浅色、数据密集、平面克制、低动效；Segoe UI/系统字体、语义资源、固定导航、表格优先、颜色加文字、键盘可达。拒绝营销式Bento、巨型卡墙、夸张留白、玻璃拟态和装饰动画。
- 开工缺口：S4-T01 Dashboard 尚无最近成功导入时间的职责匹配只读DTO，当前WPF也无真实数据导入页面；S4-T05不得用ViewModel直查EF、误用撤销资格服务或交付无响应入口，任务卡创建时必须明确最小边界。

## D-016｜Stage 4 正式提交前待核验语义

- 状态：S4-T04 创建前专项核验已完成并获用户批准。
- `HandledAttentionVersion`：canonical 语义固定为 Batch 最近已通过正式排查处理完成的 AttentionVersion 水位；S4-T04 只在正式 Inspection/Item 同一事务内以数据库当前值执行 `HandledAttentionVersion = AttentionVersion`。Draft、重新确认、自然阶段、S3-T05、S3-T06 不得提前更新；版本相等不单独证明已提交，幂等仍以 Task 状态、Inspection 和唯一约束为权威。
- 正式提交后的 Draft：正常成功提交在同一事务内先删除当前有效 DraftItem 再删除有效 Draft；系统失效 Draft 永久保留。失败完整回滚，AlreadySubmitted 不做异常残留草稿的隐式清理。

## D-017｜S4-T02 草稿写入与用户主动清空

- 状态：S4-T02 已由 GPT-5.6 Sol 独立验收通过；其后 Stage 4 与 Stage 5 均已按独立任务门禁完成，本节只保留草稿契约。
- 保存：草稿允许不完整；SaveDraft 是 patch，只保存请求明确包含的 Item，并严格保留 null/0/正数语义。普通保存不得清除 RequiresReconfirmation 或推进既有 ConfirmedAttentionVersion。
- 陈旧页面：SaveDraft 允许保留观察版本落后于数据库当前版本的用户输入，但不得改变当前 AttentionVersion/RequiresReconfirmation；只有独立 ReconfirmItem 在请求版本同时匹配当前 Batch/TaskItem 时才可清除重新确认。
- 系统失效：S3-T04 留下的 IsInvalid Draft 是必须保留的系统痕迹，S4-T02 不得复活、覆盖或删除。
- 用户主动清空：只对仍开放 Task 的当前有效 Draft 生效，事务内先删除 DraftItem 再删除 Draft；无 Draft 为幂等无变化。该策略不决定 S4-T04 正式提交后的 Draft 处置，D-016 门禁保持不变。
- 边界：Application 显式接收 BusinessDate 与 UTC 操作时间，不接 WPF，不新增 schema/依赖，不调用 S3-T04/S3-T06，不创建 Inspection 或完成 Task。

## D-018｜S4-T03 手工库存修正与商品归零编排

- 状态：S4-T03 已由 GPT-5.6 Sol 独立验收通过；其后 S4-T04 已按 D-019 实现并验收。
- 库存事实：每次真实修正新增 InventoryAdjustment，ExcelStockQtySnapshot 固定取 Product 当前 ExcelStockQty；Product 只更新 EffectiveStockQty、EffectiveStockSource=`manual` 与 UpdatedAtUtc。连续手工修正不得把上次 Effective 值伪装成 Excel 快照。
- 同值：按重新读取的 EffectiveStockQty 判断；同值不写 Adjustment、不改时间、不触发生命周期。0 值确认门禁先于同值判断。
- 归零：0 值必须显式确认；本卡先保存 Adjustment 与 Product=0，再在同一外层事务内只调用 S3-T04，并把本次 AdjustmentId 作为唯一来源。S4-T03 不复制归零状态机。
- 恢复：曾归零商品修正为正数只改变当前库存事实，不清除商品历史、不恢复旧 Batch/Task/Draft；真正新批次仍只由 S3-T05 处理。
- 导入：本卡不修改 ConfirmedImportExecutor；下一次合法 Excel 明确出现商品时仍由 Stage 2 覆盖当前库存/来源，历史 Adjustment 永久保留。
- 边界：不接 WPF，不实现排查提交，不修改 schema/migration/依赖、S3-T04/S3-T05、HandledAttentionVersion 或正式提交后的 Draft 处置。

## D-019｜S4-T04 正式排查提交事务

- 状态：实现与 Sol 独立验收已通过；其后的 Stage 4 UI 治理、S4-T05～S4-T10 与 Stage 5 均已另行批准并完成，本节只冻结提交事务。
- 权威输入：每次 Submit 重读 Product、开放 Task/全部 Item、Batch版本、有效 Draft/Item、库存及既有 Inspection；S4-T01 DTO、S4-T02 readiness 和页面缓存均不是提交授权。
- 提交：全部当前 Item 完整且版本三方一致、无需重新确认后，单事务创建 Inspection/全部 Item，真实调用 S3-T06 处理 0 件，更新 HandledAttentionVersion，完成 Task，并删除正常有效 DraftItem/Draft。
- 超库存：首次只返回当前库存与合计且业务图零写入；确认必须精确绑定当前两值，任一变化使旧确认失效，不建设通用 token。
- 幂等：completed + Inspection 返回 AlreadySubmitted；版本相等不是提交凭证。系统关闭、completed 无 Inspection 或异常残留有效 Draft 不伪装成功、不隐式修复。
- 完成语义：正式完成只写 canonical `completed`、ClosedAtUtc 与 UpdatedAtUtc；CloseReason 保持空，不创造 `submitted` 原因码或混用 system_closed 语义。
- 边界：只实现 Application，不接 WPF，不创建 Revision，不修改库存、AttentionVersion、Stage 3、schema/migration/依赖或 ConfirmedImportExecutor。

## D-020｜Stage 5 正式历史与 Revision

- 状态：S5-T01～S5-T04 已整体验收并归档。
- 查询：只读列出 completed Task 下的正式 Inspection，读取快照详情、当前 InspectionItem 数量和按时间/ID 排序的 Revision；不从 Draft 或当前 Product/Batch 反推历史。
- 修改：只允许明确目标、非负整数和合法 UTC 时间；真实变化在单一事务新增 previous/new/time Revision 并更新当前 Item，同值不写 Revision，失败回滚。
- 权威隔离：历史修订不调用 Stage 3 Lifecycle，不更新 Batch/Task/Draft/AttentionVersion，不调用或重放 Stage 4 Submission。
- UI：WPF 只调用上述查询/修改用例，确认与提交期间固定目标并防重入；提交后重读正式详情和 Revision，不伪造结果。
- UI debt：纯视觉不满意延后到 Stage 7 完成后的全局 UI/UX 重构，不创建 S5-T05。

## D-021｜Stage 7 最终范围与后续 UI/UX 门禁

- 决策依据：2026-08-31 用户提交的 `Stage-7-最终收口执行单.md`。Stage 7 只以本地安全备份、安全恢复、WPF 备份/恢复闭环收口，S7-T01～S7-T03 及总验收均已完成。
- 用户决定暂不实施“正式排查历史 / 结果 Excel 导出”，登记为 Deferred Feature，不是 Stage 7 缺陷或阻断项；后续任何阶段都必须取得重新批准才能实施。今日待排查任务、Draft、数据库原始表导出仍不做，不创建 S7-T04。
- Stage 7 后先统一整体 UI/UX，再继续后续 Stage。本轮只生成总验收、closeout、UI/UX handoff 和必要治理更新，不创建新 Task / Luna，不修改生产代码。
- UI/UX 仅允许在后续批准范围内调整视觉、布局、信息层级和必要交互表达；保持条码、1024×600 / Windows 125%、用户本人 GUI 验收；不得改写 Stage 3～7 权威、schema、migration 或借机新增业务。
- Stage 8 原规划保持稳定性 / 性能，必须等 Stage 7 收口、UI/UX 完成和用户单独批准后再进入；不创建 S8-T01，不把 UI/UX 并入 Stage 8。
- 本决策不改写 S7-T02 治理偏差、各卡历史计数和 GUI 复验过程；最终证据索引为 `.ai-dev/ACCEPTANCE/STAGE-7.md`。

## D-022｜UIUX-R01 放行与 UIUX-R02 三页实施门禁

- 状态：UIUX-R01 视觉门禁已放行；UIUX-R02 技术、用户 GUI 与正式数据恢复门禁均已通过，2026-09-01 正式归档。
- 2026-09-01 用户通过附件明确确认“新的整体视觉方向认可，可以按该设计系统进入生产 UI 重构。”，UIUX-R01 视觉门禁正式通过。
- UIUX-R02 只允许 Dashboard、待排查任务列表、排查详情及必要共享视觉资源；使用全新的 GPT-5.6 Luna（max），不复用 UIUX-R01 Luna。
- 实施保持 Stage 3～7 业务权威、固定分页、查询顺序、阶段文案、Draft/Reconfirm/库存修正/提交语义、schema、migration 和依赖不变；共享 Header 的新尺寸只作用于三类代表页，未纳入页面保持旧布局。
- Sol 最终技术门禁：UIUX-R02 1/1、UIUX-R02 + S4-T10 3/3、Release 681/681；离线 Release build 0 warning / 0 error，在线 NuGet 漏洞审计本轮不可用；EF 无漂移、migration=8、dependency 无变化。
- 用户在全部已知视觉修正完成后明确 GUI“通过”；正式库最终由用户现场恢复并回执为 299008 bytes / SHA-256 `F3D423DF14B882D7BFE87780A81CF5879F074AF4880601CBEDB6B475A964F522`，进程 0，恢复后未启动 WPF。
- UIUX-R02 归档不授权后续任务；不自动创建 UIUX-R03，不进入 Stage 8，等待用户单独批准。

## D-023｜UIUX-R03 剩余页面统一与最终归档

- 状态：UIUX-R03 实现、Sol 独立技术验收、用户真实 WPF GUI 复验和正式数据恢复均已通过，2026-09-01 正式归档。
- 范围：只把 UIUX-R01 / R02 设计系统铺到 Excel 导入、历史/Revision、Reminder、备份/恢复、应用内部 Dialog 和 Shell 一致性；Dashboard、待排查、详情仅修复用户明确回归，不重新设计。
- 业务边界：Stage 3～7 生命周期、Draft/Reconfirm/Submission、History/Revision、Reminder、Backup/Restore 权威不变；无 schema、migration、dependency、项目文件或 Application/Domain/Infrastructure 变化。
- 最终展示决策：历史“批次”是当前正式排查记录内从 1 连续编号的 UI 序号；表格、Revision、编辑和确认弹窗均使用该序号，不暴露内部明细 ID。内部 `InspectionItemId` 仍是查询、Revision、保存和刷新身份。
- 最终门禁：相关定向 31/31，Stage 3～7 为 170/170、186/186、52/52、52/52、51/51，Release 完整复跑 692/692，build 0 warning / 0 error，EF 无漂移，migration=8，`git diff --check` 通过。
- 用户确认全部 GUI 项通过；正式库恢复为 299008 bytes / 指定 SHA-256，无 WAL/SHM/journal，进程 0，恢复后未启动 WPF。
- UIUX-R03 归档不授权后续任务；不创建 UIUX-R04，不进入 Stage 8，不启动下一张业务任务，等待用户单独批准。

## D-024｜V1-F01 全品类效期与冷启动最终规则

- 状态：产品经理于 2026-09-01 正式批准设计收口；生产实现尚未逐卡批准。
- 范围基线唯一身份为 `scope_key + policy_code + policy_version`；`应季搭配`、`赠品小样`导入保存但 V1 不纳管；六类通用长效商品 `<=6个月` 导入并提示规则未覆盖，不生成效期任务。
- 历史补查为 `clamp(ceil(实际保质期天数 * 3%),3,30)`，端点包含；首次采用方案 C，仅当前收仓、到期当天和窗口内历史过期创建 open Task，既有 5折/2折与窗口外历史只建基线。
- 历史补查来源必须可审计且不伪造正式历史；基线后恢复完整四阶段生命周期。月份固定 30 天，到期当天 expired。
- Seen BatchKey、MaxArrivalQty、库存 0、Task 聚合、AttentionVersion / HandledAttentionVersion 和更高 Stage 权威不变。
- 实施拟拆 I01--I04；每卡须单独批准并新建 GPT-5.6 Terra（medium，标准速度），Sol 独立验收。当前只批准拆分，不批准 I01 开工，不进入 Stage 8。

## D-025｜V1-F01-I01 Policy 与范围基线持久化底座

- 状态：产品经理于 2026-09-01 批准 I01；Terra 实现后由 Sol 两次退回根因缺陷并独立复跑，2026-09-02 技术验收通过。I02 未获批准。
- Product policy 身份冻结为独立 `policy_code + policy_version`；V1 为 `food_expiry / pet_expiry / general_long_expiry` + version 1。现有 `food_v1` 由唯一新增 migration 显式迁移，不改变 ProductCode、BatchKey 或既有生命周期身份。
- `ExpiryManagementStatus` 为 Managed / Excluded / Unresolved；Managed 必须有获准 policy/version，后两者 policy/version 必须为空。`scope_key` 复用 canonical `CategoryCode` 持久化位置。
- 范围基线唯一键为 `scope_key + policy_code + policy_version`；BatchBaseline 持久化 canonical Stage、七类首次接管 disposition、3..30 catchup 窗口及适用的 Task/Catchup 来源，不伪造正式历史。
- I01 新增唯一第 9 条 migration `20260901155124_AddPolicyAndBaselineFoundation`；I02--I04 默认禁止 migration，schema 不足时必须停下重新审批。
- Sol 最终门禁：I01 相关 25/25、Release 711/711、build 0 warning / 0 error、EF 无漂移、migration=9、脚本生成、依赖与 WPF/Reminder 范围未变、应用进程 0。
- I04 只保持现有 Reminder 消费 open Task 链路，不实现提前 3 天预提醒；该能力仍属于未批准的 V1-F02。
- 当前停止在 I01 技术验收完成；不创建或执行 I02，等待产品经理单独批准。

## D-026｜V1-F01-I02 全品类导入与管理范围映射

- 状态：产品经理于 2026-09-02 单独批准 I02；全新 GPT-5.6 Terra（medium）实施后经 Sol 三次缺陷退回并独立技术验收通过。I03 未获批准。
- 10 个源中文大类映射为稳定 canonical CategoryCode；食品、宠物和六类 `>180` 天通用长效商品为 Managed 并绑定批准的 policy/version；应季搭配、赠品小样为 Excluded；六类 `<=180` 天商品为 Unresolved 并留存规则未覆盖 ImportIssue。
- 同 ProductCode scope/policy 冲突不改绑、不复制、不写该商品批次/库存动作或生命周期；unknown category 保持既有 unsupported 统计并产生稳定 ImportIssue。
- 未完成匹配 ScopeBaseline 的 Managed 商品只保存导入与身份；导入后和启动重算均不得提前产生效期 Task。I02 不创建 ScopeBaseline/BatchBaseline 事实，首次接管仍只属于未批准的 I03。
- Sol 最终门禁：受影响链路 117/117、Release 723/723、build 0 warning / 0 error、EF 无漂移、migration=9、依赖/WPF/Reminder/schema 无变化、应用进程 0；未访问正式数据库。
- 当前停止在 I02 技术验收完成；不创建或执行 I03，不进入 I04、V1-F02 或 Stage 8，等待产品经理单独批准。

## D-027｜V1-F01-I03 Version 1 固定契约与开工解阻

- 状态：产品经理于 2026-09-02 确认 I03 继续获批并解除 Schema 阻断；仅批准 I03，不批准 I04、V1-F02 或 Stage 8。
- 现有效期规则不会通过 policy version v2/v3 变更；V1 永久固定 `policy_version = 1`。
- 不要求真实持久化同 scope 的 version 1 / version 2 两套 ScopeBaseline；非 version 1 请求必须明确拒绝，且不得改变已有 version 1 的 ScopeBaseline、BatchBaseline、ProductTask、Batch 状态或其他业务事实。
- I01 Schema、migration、ModelSnapshot 保持不变；migration 总数继续为 9。I03 不得新增 migration/schema。
- 其余方案 C、3% 动态历史补查、事务、幂等、范围隔离、真实样本与停止门禁不变；应创建全新 GPT-5.6 Terra（medium）仅执行 I03，Sol 独立验收后停止。

## D-028｜V1-F01-I03 首次冷启动技术验收

- 状态：全新 GPT-5.6 Terra（medium）完成 I03，Sol 多轮独立审查、退回和复跑后于 2026-09-02 技术验收通过；I04 未获批准。
- V1 冷启动只接受 version 1，并要求触发 Import 与至少一个完全匹配 Managed Product 的 `LastSeenImportId` 一致；无关 Import 和非 version 1 请求零污染拒绝。
- 方案 C、库存 0 优先、真实 3% 动态补查、BatchBaseline 审计、ProductTask 聚合、事务回滚与完成范围幂等均按 I03 任务卡实施；不伪造正式排查历史。
- Sol 最终门禁：I03/导入编排 23/23、I01/I02 回归 137/137、真实样本 1/1 且 583 = 210 收仓 + 373 过期、Release 735/735、build 0/0、EF 无漂移、migration=9。
- 无 schema、migration、ModelSnapshot、依赖、WPF 或 Reminder 变化；未访问正式数据库。停止在 I03，不创建 I04，不进入 V1-F02 或 Stage 8。

## D-029｜V1-F01-I04 与 V1-F01 最终收口

- 状态：产品经理单独批准 I04 并授权 Sol 自行进行隔离 GUI 验收；全新 GPT-5.6 Terra（medium）完成实现，Sol 独立技术与 GUI 验收于 2026-09-02 通过，V1-F01 整体收口。
- 正常生命周期只放行具有匹配完成 ScopeBaseline 的 Managed、批准 policy、version 1 商品；Post-import、startup、ProductTask、AttentionVersion / HandledAttentionVersion、Inspection / Revision 与 Reminder 继续复用既有权威。
- Excluded、Unresolved、无匹配完成基线或无有效 policy 的商品不进入 Stage、Task 或 Reminder；I03 首次 5折/2折基线不被追补，首次冷启动不重跑。
- Sol 最终门禁：I04 受影响专项 99/99、I01～I03 回归 159/159、库存回归 30/30、Release 751/751、build 0/0、EF 无漂移、migration=9；无 schema、依赖或项目文件变化。
- 隔离 WPF 使用真实 10 类 Excel 与 180 天 Unresolved 边界样本完成导入、列表、详情和实际到点 Reminder 验收；Excluded / Unresolved 零 Task/Reminder，正式数据库未访问或修改。
- I04 不包含提前 3 天预提醒。收口不授权 V1-F02、V1-F03 或 Stage 8；等待产品经理单独批准。

## D-030｜V1-F02 提前 3 天预提醒

- 状态：产品经理于 2026-09-02 单独批准 V1-F02；实现、Sol 独立技术验收和用户真实 WPF GUI 验收均已通过，V1-F02 已完成归档；不授权 V1-F03、Stage 8、Stage 9 或其他功能。
- 四节点 `ReminderDate = StageEffectiveDate - 3 个日历日`；候选窗口为 `ReminderDate <= BusinessDate < StageEffectiveDate`。正式节点到达前可按既有跨日每日提醒补入，到达后不得补发该节点提前提醒。
- 预提醒以 `(BatchId, TargetStage)` 派生识别，不提前改变 Stage，不创建或伪造 ProductTask、Inspection、Revision、LifecycleEvent、HandledAttentionVersion 或其他正式业务事实。
- 只有 Managed、批准 policy/version 1、匹配完成 ScopeBaseline、库存为正且 active 的 Batch 可进入；Excluded、Unresolved、应季搭配、赠品小样、规则未覆盖、库存 0 和 stopped Batch 零预提醒。
- 正式 Task 与预提醒进入同一次 Windows 集中提醒；继续使用 `LastReminderDate`、日期回拨、通知成功登记、失败重试、托盘、scheduler、提醒设置和自启动权威，不建设第二套通知系统。
- 当前 Schema 足够，migration 保持 9。若实施发现必须持久化节点发送历史或改变 Schema，立即停止并提交最小 Schema / migration / 旧库兼容方案，等待单独批准。
- 使用全新 GPT-5.6 Terra（medium、标准速度）实施；Sol 独立技术验收，最终真实 WPF GUI 验收由用户本人执行。
- 最终门禁：定向 88/88、Release 新鲜复跑 764/764、build 0 warning / 0 error、EF 无漂移、migration=9；用户确认仅预提醒、同日幂等及正式 Task + 预提醒集中展示均通过。
- 用户明确旧正式数据后续不再使用；验收后 Fresh 将当前运行数据库替换为空库并移除隔离标记，旧数据旁置保留。V1-F02 关闭后停止，等待下一次单独批准。

## D-031｜V1-F03 实施拆分与 Schema 决策

- 状态：产品经理于 2026-09-02 仅批准新 Sol 接任、V1-F03 分析和实施拆分；生产实现尚未批准。
- V1-F03 固定拆为 I01 今日计划查询/Excel 导出、I02 结果读取/陈旧校验/Draft 应用、I03 多任务正式提交编排、I04 WPF 双入口/端到端收口；每卡单独批准并使用全新 GPT-5.6 Terra（medium），Sol 独立验收。
- 导出文件以现有 Task/TaskItem/Product/Batch 主键、AttentionVersion、任务集合及库存/到货快照稳定定位；回导必须以当前数据库重读为准，行重排允许，删除/空白不视为 0，重复/非法/篡改/陈旧项不得自动应用。
- 未确认回导结果只存在当前 WPF 会话内，确认后只原子 patch 既有 Draft；正式提交通过薄外层事务编排复用既有 `InspectionSubmissionUseCase`，不建设平行 Inspection/生命周期逻辑。
- 当前 Schema 足够，V1-F03 默认禁止 migration，数量保持 9。若必须跨重启持久化待确认预览、导出清单或回导批次，立即停止并提交 Schema 决策报告，未经新批准不得修改 ModelSnapshot 或借用 Import 表。
- 下一唯一审批项为 `V1-F03-I01｜今日排查计划查询与 Excel 导出`；批准前不创建 Terra、不创建正式 I01 实施卡、不修改生产代码，不进入 I02～I04、Stage 8 或 Stage 9。

## D-032｜V1-F03-I01 今日排查计划查询与 Excel 导出

- 状态：产品经理于 2026-09-02 单独批准 I01；全新 GPT-5.6 Terra（medium）实施并返修后，Sol 独立技术验收通过。I02 未获批准。
- 导出支持全部合法 open Managed Task 或精确 TaskId 集合；选中集合含任何重复、非正数、不存在或非法 Task 时整体拒绝，不静默导出部分结果。
- 一批次一行，稳定顺序为 `ProductCode → TaskId → BatchId → TaskItemId`；A～L 为固定可见业务列，M～Y 隐藏保存格式版本、稳定主键、AttentionVersion、Task/TaskItem/Batch、库存与到货快照。
- 复用 Open XML 3.5.1 与 `ProductCategoryScopes`；同目录临时写入后不覆盖移动，不新增 ExportRecord、Schema、migration、依赖或第二套 Category 映射。
- Sol 最终门禁：I01 3/3、相关回归 142/142、Release 767/767、build 0/0、EF 无漂移、migration=9；无 WPF、生产数据库访问或数据库业务写入。
- 当前停止在 I01 技术验收完成；不创建或执行 I02～I04，不进入 Stage 8、Stage 9，等待产品经理单独批准 I02。

## D-033｜V1-F03-I02 排查结果读取、陈旧校验与 Draft 应用

- 状态：产品经理于 2026-09-02 单独批准 I02；全新 GPT-5.6 Terra（medium）实施并多轮返修后，Sol 独立技术验收通过。I03 未获批准。
- I02 独立读取 I01 `inspection_plan_v1`，严格区分 blank/null、0、正整数与非法数量；行重排/删除允许，重复身份全部标错，显示字段不用于纠正系统身份。
- 预览零写入并重读当前事实；确认事务内再次复检。Task/集合/更新时间、Inspection、身份、Attention、Stage、tracking、到货、MaxArrival、库存、Reconfirm、Draft 与 lifecycle 任一冲突均不得覆盖。
- 确认只按显式目标 Task 调用既有 `InspectionDraftUseCase.SaveDraft`；缺失行不修改，blank 可 patch null，0/正数保持；多 Task 任一失败整批回滚，不自动 Reconfirm。
- 现有 Schema 足够；无回导记录、ImportRecord/Issue、migration、ModelSnapshot、依赖、WPF 或正式 Inspection 逻辑。
- Sol 最终门禁：I02 38/38、相关回归 213/213、Release 805/805、build 0/0、EF 无漂移、migration=9；未启动 WPF 或访问生产数据库。
- 当前停止在 I02 技术验收完成；不创建或执行 I03/I04，不进入 Stage 8、Stage 9，等待产品经理单独批准 I03。

## D-034｜V1-F03-I03 多任务正式提交编排

- 状态：产品经理于 2026-09-02 单独批准 I03；全新 GPT-5.6 Terra（medium）实施并返修后，Sol 独立技术验收通过。I04 未获批准。
- 批量层只拥有全量预检、TaskId 稳定排序、单一外层事务、集中超库存确认和结果归并；所有正式落库逐 Task 复用既有 `InspectionSubmissionUseCase.Submit`。
- 任一 Task 非法、warning、确认陈旧或第 N 个保存失败时整批回滚；0 件停止、正数继续、HandledAttentionVersion、Task completed、Draft 删除与历史结果保持单 Task 权威。
- 超库存确认以 TaskId/ProductId/当前库存/当前排查合计完整集合绑定；漏项、多项或任一当前值变化均失效并零写入。
- 全部目标同请求重放可返回 AlreadySubmitted；open/completed 混合或 Inspector/CheckDate/SubmittedAtUtc 签名不一致整体冲突，不处理剩余 open 子集。
- 现有 Schema 足够；无 BulkSubmission 实体、migration、ModelSnapshot、依赖、WPF、Excel、Draft patch 或 Revision 变化。
- Sol 最终门禁：I03 47/47、相关组合 165/165、额外生命周期 128/128、Release 852/852、build 0/0、EF 无漂移、migration=9；未启动 WPF 或访问生产数据库。
- 当前停止在 I03 技术验收完成；不创建或执行 I04，不进入 Stage 8、Stage 9，等待产品经理单独批准 I04。
