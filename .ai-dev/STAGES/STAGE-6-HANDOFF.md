# 下一任 GPT-5.6 Sol 产品经理接任说明｜Stage 6

> 交接日期：2026-08-30。Stage 5 已整体验收并归档；本文只整理 Windows 提醒 / 托盘 / 自启动方向，不是 Task 卡或开发授权。当前未创建、编号或派发 S6-T01，未实施 Stage 6 代码。

## 一、接任时必须重新核对

- 分支、HEAD、`git status`、`git diff --check` 与最近提交。
- Stage 5 总验收 `.ai-dev/ACCEPTANCE/STAGE-5.md` 和 closeout `.ai-dev/STAGES/STAGE-5-CLOSEOUT.md`。
- Release 全量、Stage 5 四类专项、Stage 4 八类、Stage 3 十类、Release build 与 EF drift。
- schema 仍为 17 张业务表、8 条 migration；工程依赖与 Stage 5 归档一致。
- 正式数据库、进程、WAL/SHM/journal、隔离目录和历史备份的当前事实。

Stage 5 产品归档锚点为 `4e0635b90fdc25040e5602fe50e1a8f43d860f36`；接任必须以实际 checkout 为准。

## 二、已完成业务能力

- Stage 2：Excel 局部增量解析、规划、确认、安全快照、原子导入、Workbook 保留与最新 Import 撤销资格。
- Stage 3：效期计算、任务聚合、启动补算、商品归零、新批次/新到货/恢复、正式 0 件 Batch 停止和导入后置编排。
- Stage 4：Dashboard、任务、Draft、Reconfirm、库存修正、正式 Submission 事务及核心 WPF 工作流。
- Stage 5：completed 正式历史列表/详情/Revision 查询、受控单条数量修订，以及 WPF 历史查看与修改闭环。

## 三、不得重写的 Stage 3～5 权威边界

1. Stage 3 仍唯一拥有 canonical phase、任务聚合和 Lifecycle；提醒层只能读取结果，不能推进或重算业务状态。
2. Stage 4 `InspectionSubmissionUseCase` 仍是正式提交唯一入口；托盘/提醒不得创建 Inspection、完成 Task 或处置 Draft。
3. Stage 5 Revision 只记录正式明细数量修订；提醒层不得编辑历史、合并 Revision、重放 Submission 或触发生命周期。
4. UI/托盘只调用 Application 权威，不直查 EF、不复制排查或效期规则。
5. Excel 是局部增量，不是全量快照。

## 四、Stage 6 已知产品范围

仓库既有 D-005 只批准方向：WPF 主进程、关闭主窗仅隐藏、显式退出才结束、当前用户注册表自启动、电源恢复后触发到期检查、每个本地自然日最多主动提醒一次。

当前只有 `settings` 与 `app_state` 数据底座；没有提醒调度器、托盘图标、注册表自启动或电源事件实现。接任者必须先核实 Windows API、单实例、退出语义、失败恢复和测试边界，再提出最小拆卡，并等待用户批准。本文不批准具体 Task、代码或新增依赖。

## 五、正式数据与 GUI 门禁

- 不用正式数据库植入演示/提醒数据；需要 GUI/UAT 时使用独立隔离环境，正式库先旁置保护并在结束后按大小、SHA-256、migration、sidecar 和进程门禁恢复。
- 当前正式基线：299008 bytes，SHA-256 `F3D423DF14B882D7BFE87780A81CF5879F074AF4880601CBEDB6B475A964F522`；接任必须重新核实。
- Sol 不使用电脑操控代替 GUI；托盘、关闭隐藏、显式退出、自启动、电源恢复和通知均由用户本人在真实 Windows 环境验收。
- 恢复正式环境后不得为了查看结果再次启动应用。

## 六、UI debt 策略与停止门禁

- Stage 5 已满足功能验收；纯视觉不满意不在 Stage 6 返工，也不创建补充 Stage 5 Task。
- Stage 7 完成后统一进行全局 UI/UX 重构与视觉收口；Stage 6 只允许完成获批的提醒/托盘/自启动产品范围。
- 不创建 S6-T01，不派发 Stage 6 Luna，不实施 Stage 6；等待用户明确批准。

