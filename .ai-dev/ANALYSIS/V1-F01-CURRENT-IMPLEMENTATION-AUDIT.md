# V1-F01 当前实现审计

审计日期：2026-09-01。此文只描述现状，不构成实现授权。

|范围|现有事实与调用链|食品限定|全品类风险 / 可复用权威|
|---|---|---|---|
|Excel 读取|`ExcelTemplateReader` 仅读首工作表，表头 `Trim()` 后要求 11 个字段，生成 `ExcelRowDto`。|否（读取层）|可复用表头和只读解析；不会保留中/小类、门店、折扣日期、距到期日、检查字段。|
|导入分类|`ExcelFileClassifier` 首先仅接受 `商品大类 == 食品`，其余行进入 `SkippedRows`；随后验证日期、保质期单位。|是|全品类的首要阻断；其 BatchKey、冲突组整体排除、库存冲突事实是权威，不应复制。|
|Product / Batch|`Product` 以 `ProductCode` 唯一；`Batch` 以 product + nullable production date + expiry date 唯一。|值默认 `food` / `food_v1`|`CategoryCode`、`PolicyCode` 已是可扩展字符串，但导入创建和一致性校验硬编码 food。|
|效期 / 单位 / Stage|`ExpiryStageCalculator.Calculate`：D/M/Y=1/30/365；总天数>270 用 90/60/14，否则 30/14/7；到期当天为 expired。|否（算法）|可按 policy 复用，但当前阈值是单一 food_v1 算法，不能直接当作所有品类政策。|
|导入写入|`ExcelImportPlanner` 规划新商品为 `food` / `food_v1`；`ConfirmedImportExecutor` 要求已有商品也正好是该对值。|是|必须把分类到管理范围/策略的映射放在导入规划边界，且保留现有局部增量、BatchKey 与累计到货水位。|
|StartupRecalculation|只重算 active 且 `NextTriggerDate <= BusinessDate` 的 Batch，随后调用 `ProductTaskAggregator`。|否（按已存 Batch）|一旦非食品能合法存入，生命周期代码可复用；首基线不能让历史 Batch 获得触发日期。|
|ProductTask|每商品一个 open task，批次按最高 Stage 聚合；只允许 Stage/AttentionVersion 上升。|否|正式提交不会永久封死更高 Stage：后续上升仍可更新/再开权威 task。|
|新增到货 / 归零|`PostImportLifecycleUseCase` 只在本次累计到货首次超过导入前 `MaxArrivalQty` 时递增 attention；`ProductStockZeroLifecycleUseCase` 在商品有效库存=0 时优先停止。|否|这是必须保留的六项生命周期权威；首基线只登记 seen，不能伪造成 arrival 或恢复。|
|提醒|`DailyReminderUseCase` 从 `InspectionTaskQuery.GetReminderCandidates` 获取 open task，按日幂等发送。|否（任务来源受限）|无需独立规则；新范围进入 lifecycle 后自动进入既有提醒查询。|
|UI|WPF 通过 Application 查询展示任务、历史、导入；文案仍有“食品效期 Excel”（`MainWindow.xaml`、`Stage4ViewModels.cs`）。|是（文案）|后续全品类实施需调整展示和筛选，但 UI 不能复制业务算法。|
|全局首次导入|`AppState` 仅含运行/提醒日期；代码中未找到 `HasCompletedInitialImport` 或等价全局初始化标志。|不适用|不能新增全局一次开关；需要范围级、策略版本级幂等记录。|

关键来源：`src/StoreExpiryInspector/Infrastructure/Excel/ExcelTemplateReader.cs`、`ExcelFileClassifier.cs`、`Application/Imports/ExcelImportPlanner.cs`、`ConfirmedImportExecutor.cs`、`Domain/ExpiryStageCalculator.cs`、`Application/StartupRecalculationUseCase.cs`、`Application/Tasks/ProductTaskAggregator.cs`、`Application/PostImportLifecycleUseCase.cs`、`Application/ProductStockZeroLifecycleUseCase.cs`、`Application/Tasks/InspectionSubmissionUseCase.cs`。

## 不可破坏的现有权威

1. Excel 为局部增量；未见行不表示归零或删除。
2. Seen BatchKey 由导入的 `LastSeenImportId` / 唯一键维护；不得因历史基线重置。
3. 累计到货只有首次超过历史 `MaxArrivalQty` 才是新增到货。
4. 商品库存=0 优先终止，优先于新 Batch、到货、恢复和 Stage。
5. 正式提交更新处理水位，不应永久屏蔽后续更高 Stage。
6. 历史基线不得创建 `Inspection`、`InspectionItem`、Revision 或 completed `ProductTask`。
