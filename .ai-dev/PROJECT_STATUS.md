# 项目状态

- 项目：门店效期排查软件 V1
- 当前阶段：Stage 7 已正式完成总验收与收口
- 状态：S7-T01～S7-T03、UIUX-R01～R03、V1-F01-I01～I04 及 V1-F02 已归档；V1-F01、V1-F02 已完成；V1-F03-I01～I03 已技术验收并等待 I04 单独批准；Stage 8 尚未开始
- 当前分支：`master`
- 当前最新 HEAD：`refs/heads/master`（本文件所在归档提交；具体 SHA 以 `git rev-parse HEAD` 为准）
- Stage 3 最新实现 HEAD：`dd1a83b87082d80990a4ff2655788ecde91a3eca`
- Stage 2 整体验收归档基线：`64dd0c6d07b192ca246c77a604fb31065282e166`
- 需求基线：`docs/门店效期排查软件_V1_Codex开发总纲.md`

## 当前交付事实

- 单一 .NET 10 WPF 应用与测试项目；I01 已新增 policy / scope / batch baseline 持久化底座，当前 migration 为 9 条。
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
- S6-T01 已交付每日提醒候选、到期判断、同日幂等与成功登记；实现 `5c0ab6a313813f28976ed4b9f4bc7e5c42aa4f3e`，专项 13/13。
- S6-T02 已交付 Windows 集中提醒及应用启动运行时接入；实现 `673391ad2f6d0d940fa139680f7fc5635c4c0cb1`，专项 10/10，用户 Windows 验收通过。
- S6-T03 已交付单实例、托盘常驻、主窗口恢复/退出与到点 scheduler；实现 `ea2ae19ab92ab7096c96e1b2745cd6132fc72c0d`，专项 12/12，用户托盘与到点提醒验收通过。
- S6-T04 已交付提醒时间设置、运行中重新调度和当前用户级开机自启动；实现 `f6fdd2495e5b42200b046153b3b65a06790d60d6`，专项 17/17，用户自动启动启停、同日不重复提醒及数据恢复验收通过。
- Stage 6 最终总验收通过：S6-T01/T02/T03/T04 分别 13/13、10/10、12/12、17/17，Stage 5 51/51、Stage 4 179/179、Stage 3 170/170、Release 635/635，build 0/0；EF 无漂移，migration=8，依赖未变。正式数据库恢复至 299008 bytes / 指定 SHA-256，进程 0，无范围越界。
- S7-T01 已复用 Stage 2 SQLite 在线快照能力，新增本地数据库备份 Application 契约、独立备份目录、JSON/既有 `BackupRecord` 元数据、完整性/migration/SHA-256 核验和单次并发门禁；专项 6/6、共享快照回归 12/12、Stage 6 52/52、Stage 5 51/51、Stage 4 179/179、Stage 3 170/170、Release 641/641、build 0/0、EF 无漂移、migration=8、依赖未变。用户本机只读回执确认正式库 299008 bytes / SHA-256 `F3D423DF14B882D7BFE87780A81CF5879F074AF4880601CBEDB6B475A964F522`，Codex Sol 复核进程 0；S7-T01 已独立验收并正式归档。
- S7-T02 已交付可信 S7-T01 备份的离线安全恢复 Application 契约、恢复前保护备份、staging + 原子替换、SHA/integrity/migration 最终验证、sidecar 精确隔离清理、失败回退与共享并发门禁；最终实现基线 `fea023ca3fda983329c77856ef3700a71d50691b`。专项 9/9、S7-T01/共享快照 12/12、Stage 6 52/52、Stage 5 51/51、Stage 4 179/179、Stage 3 170/170、Release 650/650、build 0/0、EF 无漂移、migration=8、依赖未变。用户实施后只读回执确认正式库仍为 299008 bytes / 指定 SHA-256，进程 0、无 sidecar 或恢复残留；S7-T02 已独立验收并正式归档。

- S7-T03 已交付备份/恢复 WPF 入口、经过校验的备份列表、显式选择/二次确认与恢复维护门禁；实现 `da8b43233fe965784701283a5ecf0839f72750ed`。Sol 独立专项 30/30、T02 9/9、T01/共享 12/12、Stage 6 52/52、Stage 5 51/51、Stage 4 179/179、Stage 3 170/170、Release 680/680、build 0/0、EF 无漂移、migration=8、依赖未变。用户恢复 A 字节核验通过、重开后提醒 10:00；正式环境回执确认 299008 bytes / 指定 SHA-256、进程 0、隔离清理及原自启动恢复。用户最终确认全部人工检查通过且未再次启动，S7-T03 正式归档。Sol 工具侧直接哈希仍受既有 Junction 限制，正式数据现场身份依据用户本机原始回执。

- Stage 7 最终总验收于 2026-08-31 重新运行通过：T01 6/6、T01/共享组合 12/12、T02 9/9、T03 30/30；Stage 6 52/52、Stage 5 51/51、Stage 4 179/179、Stage 3 170/170；Release 680/680、0 失败/跳过，build 0/0，EF 无漂移、migration=8，无包依赖变化。工程唯一额外元数据为 T02 已归档的 InternalsVisibleTo，不伪称整个 csproj diff 为空。

- UIUX-R01 已完成审计、Token、Taste 二审及三类代表页高保真视觉门禁并获用户放行。UIUX-R02 已落地 Dashboard、待排查任务列表、排查详情及必要共享视觉资源；用户完成多轮真实 GUI 验收并在最终三列居中修正后明确“通过”。最终 UIUX-R02 1/1、UIUX-R02 + S4-T10 3/3、Release 681/681；离线 Release build 0 warning / 0 error，在线 NuGet 漏洞审计本轮不可用；EF 无漂移、migration=8、依赖不变、无业务逻辑变化。正式库经用户现场恢复回执确认为 299008 bytes / 指定 SHA-256，进程 0、无 sidecar 或 UIUX-R02 恢复残留，恢复后未启动 WPF。UIUX-R02 已正式归档。
- UIUX-R03 已把同一设计系统铺到 Excel 导入、历史/Revision、Reminder 设置、备份/恢复、应用内部 Dialog 与 Shell，并闭环用户统一返修及后续定向复验。最终相关定向组合 31/31、Stage 3～7 分别 170/170、186/186、52/52、52/52、51/51，Release 完整复跑 692/692、build 0/0、EF 无漂移、migration=8、依赖/项目/schema 不变。用户确认全部 GUI 项通过；正式库已恢复为 299008 bytes / SHA-256 `F3D423DF14B882D7BFE87780A81CF5879F074AF4880601CBEDB6B475A964F522`，进程 0、无 sidecar，恢复后未启动 WPF。UIUX-R03 已正式归档。
- V1-F01-I03 已交付 version 1 固定范围的一次性方案 C 冷启动、真实 3% 历史补查、BatchBaseline 审计与 ProductTask 聚合。Sol 最终专项 23/23、I01/I02 回归 137/137、真实样本 583（210 收仓 + 373 过期）、Release 735/735、build 0/0、EF 无漂移、migration=9；无 schema/依赖/WPF/Reminder 变化，未访问正式数据库。
- V1-F01-I04 已交付完成基线后的 policy-aware 正常生命周期、Managed Reminder 防御与最小全品类 WPF 文案。Sol 最终受影响专项 99/99、I01～I03 回归 159/159、库存回归 30/30、Release 751/751、build 0/0、EF 无漂移、migration=9；真实全品类 Excel 与隔离 WPF/SQLite 验收通过，正式数据库未访问或修改。V1-F01 已整体收口。

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
- 当前 WPF 已具备 Stage 4～5 排查与历史能力、Stage 6 Reminder/设置/托盘、自启动及 Stage 7 本地备份/恢复闭环；UI 不复制 Stage 3～7 权威算法，恢复遵守维护与退出边界。
- 同版本安全备份/恢复与用户隔离演练已由 Stage 7 完成；升级前完整可恢复流程、安装、Windows 门店实机和 10 万批次/30 万历史性能仍待后续批准。
- 正式排查历史 / 结果 Excel 导出由用户明确 deferred，不是 Stage 7 缺陷；未经后续重新批准不得补做。今日待排查任务、Draft、数据库原始表导出仍不做。
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
- S6-T01～S6-T04 验收：`.ai-dev/ACCEPTANCE/S6-T01.md`、`.ai-dev/ACCEPTANCE/S6-T02.md`、`.ai-dev/ACCEPTANCE/S6-T03.md`、`.ai-dev/ACCEPTANCE/S6-T04.md`
- Stage 6 总验收：`.ai-dev/ACCEPTANCE/STAGE-6.md`
- Stage 6 归档：`.ai-dev/STAGES/STAGE-6-CLOSEOUT.md`
- Stage 7 接任说明：`.ai-dev/STAGES/STAGE-7-HANDOFF.md`
- S7-T01 正式验收：`.ai-dev/ACCEPTANCE/S7-T01.md`
- S7-T02 正式验收：`.ai-dev/ACCEPTANCE/S7-T02.md`
- S7-T03 正式验收及用户证据：`.ai-dev/ACCEPTANCE/S7-T03.md`、`S7-T03-GUI-RESTORE-RESULT.json`、`S7-T03-RESTORE-RESULT.json` 与同目录 `S7-T03-EVIDENCE/`
- S7-T03 用户隔离 GUI 指南：`docs/S7-T03-GUI验收.md`
- Stage 7 总验收与技术结果：`.ai-dev/ACCEPTANCE/STAGE-7.md`、`.ai-dev/ACCEPTANCE/STAGE-7-TECHNICAL-RESULT.json`
- Stage 7 收口：`.ai-dev/STAGES/STAGE-7-CLOSEOUT.md`
- UI/UX 统一重构交接（非实施授权）：`.ai-dev/STAGES/UI-UX-REFRESH-HANDOFF.md`
- UIUX-R01 设计系统与视觉稿：`.ai-dev/UI-REFRESH/`
- UIUX-R02 任务与最终验收：`.ai-dev/TASKS/UIUX-R02.md`、`.ai-dev/ACCEPTANCE/UIUX-R02.md`
- UIUX-R03 任务与最终验收：`.ai-dev/TASKS/UIUX-R03.md`、`.ai-dev/ACCEPTANCE/UIUX-R03.md`
- V1-F01 I01～I04 验收：`.ai-dev/ACCEPTANCE/V1-F01-I01.md`、`.ai-dev/ACCEPTANCE/V1-F01-I02.md`、`.ai-dev/ACCEPTANCE/V1-F01-I03.md`、`.ai-dev/ACCEPTANCE/V1-F01-I04.md`
- V1-F03 功能总卡与实施拆分：`.ai-dev/TASKS/V1-F03.md`、`.ai-dev/ANALYSIS/V1-F03-IMPLEMENTATION-SPLIT.md`

## 下一步门禁

- Stage 6 技术、用户 Windows GUI、正式环境恢复、隔离清理和最终总验收均已通过并正式归档；详见 `.ai-dev/ACCEPTANCE/STAGE-6.md` 与 `.ai-dev/STAGES/STAGE-6-CLOSEOUT.md`。
- Stage 6 历史结论（2026-08-31）：Stage 6 四卡及总验收全部通过；Release 635/635、build 0/0、EF 无漂移、migration=8。用户本机 `RESTORE_PASS` 回执确认正式库 299008 bytes / SHA-256 `F3D423DF14B882D7BFE87780A81CF5879F074AF4880601CBEDB6B475A964F522`、进程 0，Stage 6 隔离目录已清理且恢复后未再启动应用。
- 历史结论（2026-08-30）：S5-T03 用户 GUI 通过，并提供本机 `RESTORE_PASS` 截图及一致的共享回执；正式库大小 299008 bytes / 指定 SHA-256，进程 0，隔离运行目录和恢复暂存目录已删除。Sol 已清理共享 `obj/S5T03GuiAcceptance`，恢复后未启动应用。
- Stage 4 已完成总验收与归档；1024×600、125% 缩放、鼠标滚轮、Tab 与窗口恢复已由用户最终 GUI 验收通过。
- 在线 NuGet 漏洞审计因本机 SSL/TLS/凭据环境失败的风险已由用户在 S4-T09 接受；不得改写为在线审计成功。
- S5-T03 闪退修复 `4341c213dc0377a661992aecdde93ffead034b7d`、多列表头修复 `e4c3d4d8d6b18c3fc9e1f9773815d6fde11430ae` 均由原 Luna 执行，Sol 独立复验，用户最终 GUI 通过；各轮失败/修复/复验过程保留在 S5-T03 验收记录。本轮归档不修改生产代码，不将历史测试计数冒充重新运行。
- 用户侧管理员 CMD 已确认默认 C 盘运行目录为普通目录，登记 `USER_DIR_PASS`；工具进程仍保留旧路径视图，无法枚举该现场目录。正式旁置原件及 S4-T09 历史备份继续保留，正式数据库最终身份依据本轮用户 CMD 的 size/SHA 回执闭环。Sol 恢复后未启动 WPF。
- S7-T02 归档时正式库实施后只读回执为 299008 bytes / 指定 SHA-256，应用进程 0，无 sidecar 或恢复残留；这是历史验收，不冒充后续轮次的现场读取。
- S7-T03 归档事实（2026-08-31）：技术与用户 GUI 验收全部通过。恢复 A、重开后 10:00 及正式环境 RESTORE_PASS 原始证据已保存；用户最终确认“通过，未再次启动软件”。正式库 299008 bytes / 指定 SHA-256、隔离清理及原自启动恢复据用户本机回执通过。
- 最新结论（2026-08-31）：Stage 7 总验收与 closeout 已完成，最终范围为本地安全备份 / 恢复 / WPF 闭环，Excel 导出已 deferred。收口本轮确认正式库存在、299008 bytes、进程 0；SHA-256、实际 migration 与完整 sidecar/备份目录因既有 Junction 限制未重新确认，引用最近 S7-T03 用户原始回执，不冒充现场读取成功。
- 最新 UI/UX 结论（2026-09-01）：UIUX-R01 已获视觉放行；UIUX-R02 三类代表页实施、后续人工反馈修正、Sol 技术门禁、用户真实 WPF GUI 验收和正式数据库恢复均已闭环，UIUX-R02 正式归档。
- 最新 UI/UX 结论（2026-09-01）：UIUX-R03 剩余页面统一、全部返修、Sol 技术门禁、用户真实 WPF GUI 验收和正式数据库恢复均已闭环，UIUX-R03 正式归档。
- V1-F02 最终结论（2026-09-02）：提前 3 天预提醒已完成并归档；Sol 定向 88/88、Release 新鲜复跑 764/764、build 0/0、EF 无漂移、migration=9。用户真实 WPF 确认仅预提醒四类各 1、同日未重复、正式 Task + 预提醒单次集中展示正确；Fresh 已安装空白运行库并移除隔离标记，旧数据旁置保留。不得自动进入 V1-F03、Stage 8 或 Stage 9。
- V1-F01 最终结论（2026-09-02）：I01～I04 已按单卡授权全部完成并归档；最终 Release 751/751、build 0/0、EF 无漂移、migration=9，隔离 GUI 验收通过。不得据此自动进入 V1-F02、V1-F03 或 Stage 8。
- V1-F03 当前结论（2026-09-02）：I01 导出、I02 读取/陈旧校验/Draft 应用及 I03 多任务正式提交编排均已技术验收；I03 Sol 新鲜专项 47/47、相关组合 165/165、额外生命周期 128/128、Release 852/852、build 0/0、EF 无漂移、migration=9。无 Schema/依赖/WPF/生产数据库变化；下一唯一审批点为 I04，未经批准不得创建或执行 I04、Stage 8 或 Stage 9。
