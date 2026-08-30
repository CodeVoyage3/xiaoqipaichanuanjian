# 项目状态

- 项目：门店效期排查软件 V1
- 当前阶段：Stage 5 已整体验收并归档；Stage 6 handoff 已生成但尚未开始
- 状态：Stage 0～Stage 5 整体通过；等待用户单独批准 Stage 6
- 当前分支：`master`
- 当前最新 HEAD：`refs/heads/master`（本文件所在归档提交；具体 SHA 以 `git rev-parse HEAD` 为准）
- Stage 3 最新实现 HEAD：`dd1a83b87082d80990a4ff2655788ecde91a3eca`
- Stage 2 整体验收归档基线：`64dd0c6d07b192ca246c77a604fb31065282e166`
- 需求基线：`docs/门店效期排查软件_V1_Codex开发总纲.md`

## 当前交付事实

- 单一 .NET 10 WPF 应用与测试项目；17 张业务表、17 个实体/配置/DbSet、8 条 migration。
- Stage 2：固定模板只读解析、局部增量规划、确认守卫、安全快照、原子导入、Workbook 保留与最新 Import 撤销资格。
- Stage 3：纯效期计算、商品任务聚合、启动补算、商品归零、新批次/新到货/恢复、正式 0 件停止、原子导入后置编排和真实 WPF 启动接入。
- canonical phase：`none / discount_50 / discount_20 / withdraw / expired`。
- Stage 4 最终验证：受影响 UI/S4-T10 回归 84/84，Stage 4 八个权威测试类 179/179，Stage 3 十个权威测试类 184/184，Release 全量 532/532；Release build 0 警告/0 错误；EF 无漂移；migration 仍为 8 条。
- Release EXE 已实际启动验收：主窗口正常；新空库完成 migration、写入 `last_normal_run_date` 与启动完成日志。
- S4-T01 已交付 Application 层 Dashboard、开放任务列表和排查详情只读查询 DTO；真实 SQLite 专项 10/10，Release 全量 358/358，Stage 3 精确回归 170/170。
- S4-T02 已交付 Application 层草稿 patch 保存、显式重新确认、readiness 与用户主动清空；真实 SQLite 专项 30/30，S4-T01 回归 10/10，Stage 3 精确回归 170/170。
- S4-T03 已交付 Application 层手工库存修正、InventoryAdjustment 历史与复用 S3-T04 的 0 库存事务编排；真实 SQLite 专项 30/30，S4-T02 回归 30/30，S4-T01 回归 10/10，Stage 3 精确回归 170/170。
- S4-T04 已交付 Application 层正式提交事务：数据库重读、完整草稿与重新确认门禁、超库存事实绑定、Inspection/Item 快照、HandledAttentionVersion、S3-T06 复用、Task completed 与有效 Draft 原子处置；专项 25/25、前置回归 70/70、Stage 3 精确回归 170/170、Release 全量 443/443。
- S4-T05 已交付最小 WPF Shell、首页、待排查任务列表及最近成功导入时间只读契约；专项 25/25，S4-T01～T05 精确回归 110/110，Stage 3 精确回归 170/170，Release 全量 458/458。真实 Release WPF 已验证首页/列表/搜索/空状态/disabled导航/Ctrl+F/默认与最大化布局；当前 Windows 150% DPI 下可用。
- S4-T06 已把既有 Excel 解析、校验、确认守卫与原子生命周期导入接入真实 WPF；专项 UI 回归 28/28，S4-T01～T06 精确回归 126/126，Stage 3 精确回归 170/170，Release 全量 474/474。真实 Release WPF 已验证选择/取消/P0 身份失效/错误/Loading/确认及 Dashboard、任务列表即时刷新。
- S4-T07 已把详情、草稿、逐项重新确认与库存修正接入真实 WPF；专项 24/24，S4-T01～T07 精确回归 150/150，Stage 3 精确回归 170/170，Release 全量 498/498。真实 Release WPF 已验证多批次详情、正常批次折叠、空白/0/正数、自动保存恢复、Reconfirm、ClearDraft、正库存/同值/0 库存与 Dashboard/列表刷新；原用户数据已恢复。
- S4-T08 已把 S4-T04 正式提交接入真实 WPF，交付保存稳定门禁、提交互斥、超库存确认、完成/陈旧状态及三处刷新；专项 27/27，S4-T01～T08 精确回归 177/177，Stage 3 精确回归 170/170，Release 全量 525/525。真实 Release WPF 已验证正常提交落库、0 件停止、超库存二次确认、completed、列表移除、Dashboard 刷新与 RequiresReconfirmation 拒绝；原用户数据已恢复。
- S4-T09 已完成最终 Release/UAT 缺陷修复；其人工验收门禁顺延到 S4-T10 最终 GUI 基线，由用户最终复测一并闭环。
- S4-T10 已完成核心 UI 定稿与最终收尾；用户 10/10 GUI 验收通过、发现问题为 0。正式数据库已恢复为 299008 bytes，SHA-256 `F3D423DF14B882D7BFE87780A81CF5879F074AF4880601CBEDB6B475A964F522`，隔离测试数据已清理且恢复后未再启动应用。
- S5-T01 已交付正式 `Inspection` 历史列表与单条详情 Application 只读查询；专项 9/9、Stage 4 179/179、Stage 3 实际十类 170/170、Release 541/541、build 0/0、EF 无漂移、migration 8，未实现 WPF、Revision 或 S5-T02。
- S5-T02 已交付正式明细数量修订、原子 Revision 留痕及当前值/修订链只读查询；实现 `33424ef6cddbd109b2e9c59941a7e182324270e7`。专项 16/16、S5-T01 9/9、Stage 4 179/179、Stage 3 170/170、Release 557/557、build 0/0、EF 无漂移、migration 8；无生命周期联动、WPF 或 S5-T03。
- S5-T03 已完成排查历史只读入口、列表、正式详情与 Revision 展示；初始实现 `3f44388193fc03cfde82ee169912f868ba32e219`，最终显示修复 `e4c3d4d8d6b18c3fc9e1f9773815d6fde11430ae`。Sol 独立专项 10/10、S5-T02 16/16、S5-T01 9/9、Stage 4 179/179、Stage 3 170/170、Release 567/567、build 0/0、EF 无漂移、migration 8、无依赖变更；没有历史编辑 UI 或 S5-T04。用户 GUI 通过，本机恢复回执确认正式库为 299008 bytes / 指定 SHA-256、进程 0；共享临时目录已清理，正式归档。
- S5-T04 已把既有 S5-T02 单条正式明细数量修订接入历史详情 UI；实现 `5818379d751c1aa479489d5febcad6208dd92e9b`。专项 16/16、S5-T03/T02/T01 合计 35/35、Stage 4 179/179、Stage 3 170/170、Release 583/583、build 0/0、EF 无漂移、migration 8、无依赖变更及范围越界。用户本人隔离 GUI 验收通过，确认数量 4→7 与 Revision；恢复回执为 `RESTORE_PASS`，正式库 299008 bytes / 指定 SHA-256、进程 0、隔离与暂存已移除，恢复后未再次启动应用。S5-T04 正式归档。
- Stage 5 最终总验收通过：S5-T01/T02/T03/T04 分别 9/9、16/16、10/10、16/16，Stage 4 179/179，Stage 3 170/170，Release 583/583，build 0/0；EF 无漂移，仓库/正式库 migration=8，依赖未变。正式数据与隔离清理门禁保持通过，无范围越界。

## 冻结业务红线

1. Excel 是局部增量，不是全量快照；未出现商品/批次没有任何归零、停止、恢复或任务含义。
2. 阶段、优先级、任务聚合和生命周期规则只复用 Stage 3 已验收权威实现，不在 UI/导入执行器/EF 配置复制。
3. 商品明确库存 0 优先于新批次、新到货、恢复与阶段任务；旧 Batch 不因库存恢复自动复活。
4. 真正新增到货只认首次突破导入前历史最高；只有正式 0 件停止的 Batch 具备受限恢复资格。
5. 正式排查 0 件只停止目标 Batch；完整 Inspection + 任务完成 + S3-T06 编排属于 Stage 4。
6. 生命周期事件只保存五类批准事实，不建设事件总线或扩展过程事件。

## 当前架构债与风险

- `ConfirmedImportExecutor.cs` 为 1,154 行；职责仍限于 Stage 2 持久化及外层事务参与，禁止再加入 Stage 3/4 规则。
- `ConfirmedImportLifecycleOrchestrator.cs` 为 467 行；当前只冻结/映射明确事实、调用 S3-T04/S3-T05 与拥有事务，无阻断级 God Service 债务。
- 当前 WPF 主窗已具备 Shell、首页、待排查列表、数据导入、排查详情、草稿、逐项重新确认、库存修正、正式提交、历史列表/详情/Revision 与受控历史数量修改；UI 未复制 Stage 3～5 业务规则或直接写正式业务图。
- migration 前完整可恢复流程、安装、完整备份恢复、Windows 门店实机和 10 万批次/30 万历史性能仍待后续阶段。
- Stage 5 已把历史数量 Revision 与 S3-T06 Lifecycle 明确隔离；历史修改不得重放旧 0 事实或触发状态重算。
- `HandledAttentionVersion` 已冻结为正式排查处理水位，只能在正式提交事务更新；正常成功提交在同一事务删除有效 DraftItem/Draft，系统失效 Draft 永久保留。
- Stage 4 UI/UX Pro Max 基线 `.ai-dev/UI/STAGE-4-UI-BASELINE.md` 已批准并用于 S4-T05 验收；规范只控制 WPF视觉与交互，不得改写Application或业务规则。
- S4-T10 定稿刷新基线 `.ai-dev/UI/STAGE-4-UI-REFRESH-BASELINE.md` 已建立，5 张用户定稿图已按实际页面内容校正文件名并登记哈希；它只覆盖纯表现，不覆盖既有业务/Application 权威。

## 阶段归档

- Stage 3 总验收：`.ai-dev/ACCEPTANCE/STAGE-3.md`
- Stage 3 归档：`.ai-dev/STAGES/STAGE-3-CLOSEOUT.md`
- Stage 4 接任说明：`.ai-dev/STAGES/STAGE-4-HANDOFF.md`
- S4-T01 验收：`.ai-dev/ACCEPTANCE/S4-T01.md`
- S4-T02 验收：`.ai-dev/ACCEPTANCE/S4-T02.md`
- S4-T03 验收：`.ai-dev/ACCEPTANCE/S4-T03.md`
- S4-T04 验收：`.ai-dev/ACCEPTANCE/S4-T04.md`
- S4-T05 验收：`.ai-dev/ACCEPTANCE/S4-T05.md`
- S4-T06 验收：`.ai-dev/ACCEPTANCE/S4-T06.md`
- S4-T07 验收：`.ai-dev/ACCEPTANCE/S4-T07.md`
- S4-T08 验收：`.ai-dev/ACCEPTANCE/S4-T08.md`
- S4-T10 验收：`.ai-dev/ACCEPTANCE/S4-T10.md`
- Stage 4 总验收：`.ai-dev/ACCEPTANCE/STAGE-4.md`
- Stage 4 归档：`.ai-dev/STAGES/STAGE-4-CLOSEOUT.md`
- Stage 5 接任说明：`.ai-dev/STAGES/STAGE-5-HANDOFF.md`
- S5-T01 验收：`.ai-dev/ACCEPTANCE/S5-T01.md`
- S5-T02 验收：`.ai-dev/ACCEPTANCE/S5-T02.md`
- S5-T03 正式验收归档：`.ai-dev/ACCEPTANCE/S5-T03.md`
- S5-T03 用户本机恢复原始回执：`.ai-dev/ACCEPTANCE/S5-T03-RESTORE-RESULT.json`
- S5-T04 验收及恢复回执：`.ai-dev/ACCEPTANCE/S5-T04.md`、`.ai-dev/ACCEPTANCE/S5-T04-RESTORE-RESULT.json`
- Stage 5 总验收：`.ai-dev/ACCEPTANCE/STAGE-5.md`
- Stage 5 归档：`.ai-dev/STAGES/STAGE-5-CLOSEOUT.md`
- Stage 6 接任说明：`.ai-dev/STAGES/STAGE-6-HANDOFF.md`

## 下一步门禁

- Stage 5 技术、用户 GUI、正式环境恢复、隔离清理和最终总验收均已通过并正式归档；详见 `.ai-dev/ACCEPTANCE/STAGE-5.md` 与 `.ai-dev/STAGES/STAGE-5-CLOSEOUT.md`。
- 最新结论（2026-08-30）：用户 GUI 通过，并提供本机 `RESTORE_PASS` 截图及一致的共享回执；正式库大小 299008 bytes / SHA-256 `F3D423DF14B882D7BFE87780A81CF5879F074AF4880601CBEDB6B475A964F522`，进程 0，隔离运行目录和恢复暂存目录已删除。Sol 已清理共享 `obj/S5T03GuiAcceptance`，恢复后未启动应用。S5-T03 正式归档，停止。
- Stage 4 已完成总验收与归档；1024×600、125% 缩放、鼠标滚轮、Tab 与窗口恢复已由用户最终 GUI 验收通过。
- 在线 NuGet 漏洞审计因本机 SSL/TLS/凭据环境失败的风险已由用户在 S4-T09 接受；不得改写为在线审计成功。
- S5-T03 闪退修复 `4341c213dc0377a661992aecdde93ffead034b7d`、多列表头修复 `e4c3d4d8d6b18c3fc9e1f9773815d6fde11430ae` 均由原 Luna 执行，Sol 独立复验，用户最终 GUI 通过；各轮失败/修复/复验过程保留在 S5-T03 验收记录。本轮归档不修改生产代码，不将历史测试计数冒充重新运行。
- 工具侧默认 C 盘目录仍为旧异常 Junction，与用户本机正常恢复目录是不同视图。正式旁置原件及 S4-T09 历史备份保留；不宣称工具侧路径修复完成，未来通过工具启动前必须重新核实处理。此为独立环境限制，用户本机数据恢复已按回执闭环。Sol 不启动 WPF、不使用电脑操控验收。
- 不得创建 S5-T05；不得创建、编号、派发或实施 S6-T01 / Stage 6。Stage 6 handoff 不是开发授权，等待用户下一步明确批准。
