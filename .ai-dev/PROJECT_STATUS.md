# 项目状态

- 项目：门店效期排查软件 V1
- 当前阶段：Stage 4｜S4-T05 已独立验收通过，停止并等待剩余任务重新拆分
- 状态：Stage 0～Stage 3 整体通过；S4-T01～S4-T05 已独立验收通过；UI/UX Pro Max 基线已批准并归档；未创建任何后续任务
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
- 当前 Release 全量 458/458；Stage 3 精确证据 170/170；Release build 0 警告/0 错误；EF 无漂移；migration 仍为 8 条。
- Release EXE 已实际启动验收：主窗口正常；新空库完成 migration、写入 `last_normal_run_date` 与启动完成日志。
- S4-T01 已交付 Application 层 Dashboard、开放任务列表和排查详情只读查询 DTO；真实 SQLite 专项 10/10，Release 全量 358/358，Stage 3 精确回归 170/170。
- S4-T02 已交付 Application 层草稿 patch 保存、显式重新确认、readiness 与用户主动清空；真实 SQLite 专项 30/30，S4-T01 回归 10/10，Stage 3 精确回归 170/170。
- S4-T03 已交付 Application 层手工库存修正、InventoryAdjustment 历史与复用 S3-T04 的 0 库存事务编排；真实 SQLite 专项 30/30，S4-T02 回归 30/30，S4-T01 回归 10/10，Stage 3 精确回归 170/170。
- S4-T04 已交付 Application 层正式提交事务：数据库重读、完整草稿与重新确认门禁、超库存事实绑定、Inspection/Item 快照、HandledAttentionVersion、S3-T06 复用、Task completed 与有效 Draft 原子处置；专项 25/25、前置回归 70/70、Stage 3 精确回归 170/170、Release 全量 443/443。
- S4-T05 已交付最小 WPF Shell、首页、待排查任务列表及最近成功导入时间只读契约；专项 25/25，S4-T01～T05 精确回归 110/110，Stage 3 精确回归 170/170，Release 全量 458/458。真实 Release WPF 已验证首页/列表/搜索/空状态/disabled导航/Ctrl+F/默认与最大化布局；当前 Windows 150% DPI 下可用。

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
- 当前 WPF 主窗已具备 S4-T05 Shell、首页和待排查列表；排查详情、草稿、库存修正、数据导入与正式提交 UI 仍不存在，不得把相关业务放进 ViewModel/code-behind。
- migration 前完整可恢复流程、安装、完整备份恢复、Windows 门店实机和 10 万批次/30 万历史性能仍待后续阶段。
- S3-T06 的多轮历史修订时序属于 Stage 5，不得在 Stage 4 顺带实现历史编辑。
- `HandledAttentionVersion` 已冻结为正式排查处理水位，只能在正式提交事务更新；正常成功提交在同一事务删除有效 DraftItem/Draft，系统失效 Draft 永久保留。
- Stage 4 UI/UX Pro Max 基线 `.ai-dev/UI/STAGE-4-UI-BASELINE.md` 已批准并用于 S4-T05 验收；规范只控制 WPF视觉与交互，不得改写Application或业务规则。

## 阶段归档

- Stage 3 总验收：`.ai-dev/ACCEPTANCE/STAGE-3.md`
- Stage 3 归档：`.ai-dev/STAGES/STAGE-3-CLOSEOUT.md`
- Stage 4 接任说明：`.ai-dev/STAGES/STAGE-4-HANDOFF.md`
- S4-T01 验收：`.ai-dev/ACCEPTANCE/S4-T01.md`
- S4-T02 验收：`.ai-dev/ACCEPTANCE/S4-T02.md`
- S4-T03 验收：`.ai-dev/ACCEPTANCE/S4-T03.md`
- S4-T04 验收：`.ai-dev/ACCEPTANCE/S4-T04.md`

## 下一步门禁

- S4-T05 验收见 `.ai-dev/ACCEPTANCE/S4-T05.md`；本卡通过后按用户要求立即停止，不自动创建下一任务。
- 下一轮先基于真实仓库重新拆分排查详情、数据导入 UI、正式提交 UI 与 Stage 4 Release 完整人工闭环；当前不创建、不编号、不派发。
- 当前不得修改已冻结的 S4-T01～T04、HandledAttentionVersion、Draft处置、Stage 3业务语义，或提前进入 Stage 5+。
