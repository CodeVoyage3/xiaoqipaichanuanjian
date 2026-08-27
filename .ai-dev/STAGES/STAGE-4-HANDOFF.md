# 下一任 GPT-5.6 Sol 产品经理接任说明｜Stage 4

> 交接日期：2026-08-27。当前暂停在 Stage 3 → Stage 4 门禁。Stage 3 已整体验收通过，但 Stage 4 尚未获开工授权；本文件不是任务卡。

## 一、接任时必须重新核验

- 分支、工作区、当前 HEAD 与最近提交。
- `.ai-dev/ACCEPTANCE/STAGE-3.md`、`.ai-dev/STAGES/STAGE-3-CLOSEOUT.md` 及 S3-T01～S3-T07 单卡验收。
- Release build/test、EF model drift、8 条 migration 与真实 SQLite schema。
- ProductTask、ProductTaskItem、InspectionDraft、InspectionDraftItem、Inspection、InspectionItem、InventoryAdjustment、LifecycleEvent 的实际字段、约束和值域。
- Stage 3 的任务聚合、Draft 重新确认、商品归零、正式 0 件停止及外层事务契约。

接任基线参考为 S3-T07 最新实现 `dd1a83b87082d80990a4ff2655788ecde91a3eca`；必须以仓库重新核验结果为准。

## 二、Stage 4 目标边界

按 V1 总纲，Stage 4 只完成核心排查 UI 与提交事务：

- 首页任务概况与最紧急任务入口。
- 待排查任务列表、搜索、阶段筛选与分页。
- 商品排查详情、待排查批次和默认折叠的正常批次。
- 自动草稿：件数、排查人、检查日期，支持恢复、清空与重新确认门禁。
- 商品库存修正入口，明确为 0 时复用 S3-T04。
- 正式提交事务：创建 Inspection/Item、完成 ProductTask、按 0 件明细复用 S3-T06，并统一处理 Draft。

Stage 4 不应实现历史记录修改（Stage 5）、Windows 提醒/托盘（Stage 6）、Excel 导出（后续阶段）、安装或性能验收。

## 三、必须复用的 Stage 3 能力

- 不重新计算任务阶段；列表/详情读取 ProductTask 与 Item 当前事实。
- 不另写商品归零逻辑；人工库存修正为 0 必须调用 S3-T04。
- 不另写批次 0 件停止；正式提交事务对合法 0 件 InspectionItem 调用 S3-T06，并加入同一外层事务。
- 不直接修改 AttentionVersion、TrackingStatus、StopReason 或 LifecycleEvent 来绕过 UseCase。
- Draft 重新确认继续使用既有字段和 S3-T02 已确认语义，不创建第二份 Draft。

## 四、正式提交的首要风险

1. 正式提交必须将 Inspection、InspectionItem、任务完成、Draft 处理与每个 0 件 Batch 的 S3-T06 动作置于同一事务；任一步失败全部回滚。
2. 提交前必须重新读取开放任务及 Item 当前阶段/AttentionVersion，拒绝陈旧或仍需重新确认的 Draft，不能只相信 UI 缓存。
3. 所有待排查 Item 必须填写；空白与 0 含义不同。检查日期不得晚于业务日期。
4. 排查合计超过商品库存只警告，不禁止提交；库存修正是独立明确动作，不能从排查件数反推库存。
5. 任务可能在用户填写期间被 Stage 3 启动补算或导入后置升级；必须保存草稿内容并要求重新确认，不得静默覆盖。
6. S3-T06 当前以正式 Inspection 来源事件作为幂等锚点；Stage 4 必须先持久化当前事务可见的正式记录，再调用它。

## 五、UI 与架构门禁

- WPF/ViewModel 只负责显示、输入校验提示和调用 Application；EF 查询、提交事务及生命周期规则不得进入 code-behind/ViewModel。
- 先建立最小 Application 查询/草稿/提交用例，再接页面；不要创建万能 `InspectionService`、Repository/UnitOfWork、导航框架、事件总线或通用表单引擎。
- 保持当前单应用/单测试项目，不新增依赖或 migration，除非现有 schema 被证明无法承载并先向用户报告。
- 不改变 `ConfirmedImportExecutor` 或 Stage 3 编排器来迁就 UI。

## 六、建议的接任顺序

1. 重新核验仓库与现有 Inspection/Draft/Task schema。
2. 向用户提交 Stage 4 前置条件、风险和最小任务拆分，不创建任务卡。
3. 用户批准后只创建第一张最小任务卡，再派发 GPT-5.6 Luna（max）。
4. 当前卡未正式验收前不得创建下一卡。

在用户确认前：不得创建 Stage 4 任务卡、不得派发 Luna、不得修改 Stage 4 代码。
