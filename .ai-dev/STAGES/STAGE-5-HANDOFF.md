# 下一任 GPT-5.6 Sol 产品经理接任说明｜Stage 5

> 交接日期：2026-08-29。Stage 4 已整体验收并归档；本文只提供 Stage 5 接任事实和目标边界，不是任务卡，也不是开发授权。当前未创建 S5-T01，未派发 Stage 5 Luna。

## 一、接任时必须重新核对

- 分支：`master`。
- Stage 4 最新实现提交：`0fd0190a4cb344ab7fad4b3eb5dd2cc847f3ce9f`。
- Stage 4 总验收：`.ai-dev/ACCEPTANCE/STAGE-4.md`。
- Stage 4 closeout：`.ai-dev/STAGES/STAGE-4-CLOSEOUT.md`。
- schema：17 张业务表、17 个实体/配置/DbSet、8 条 migration；Stage 4 无 EF model drift。
- Stage 4 最终门禁：UI/S4-T10 84/84、Stage 4 权威类 179/179、Stage 3 权威类 184/184、Release 532/532、Release build 0/0。
- 用户 GUI：10/10 通过，问题为 0。
- 正式数据库已恢复为 299008 bytes，SHA-256 `F3D423DF14B882D7BFE87780A81CF5879F074AF4880601CBEDB6B475A964F522`；恢复后未再次启动应用。

接任第一步必须重新执行 branch、HEAD、status、最近提交、Release test/build、EF drift、migration、依赖和正式数据库只读核验；不得把本交接记录当作未来 checkout 的永久事实。

## 二、最小必读文件

1. `.ai-dev/PROJECT_STATUS.md`
2. `.ai-dev/ACCEPTANCE/STAGE-4.md`
3. `.ai-dev/STAGES/STAGE-4-CLOSEOUT.md`
4. `.ai-dev/TASKS/S4-T04.md` 与 `.ai-dev/ACCEPTANCE/S4-T04.md`
5. `.ai-dev/TASKS/S4-T10.md` 与 `.ai-dev/ACCEPTANCE/S4-T10.md`
6. `.ai-dev/ARCHITECTURE.md`、`.ai-dev/DATA_MODEL.md`、`.ai-dev/DECISIONS.md`
7. `docs/门店效期排查软件_V1_Codex开发总纲.md`

## 三、Stage 4 提供的冻结输入

- 正式 Inspection/InspectionItem 只由 S4-T04 提交事务创建。
- completed Task、`HandledAttentionVersion`、S3-T06 批次停止与有效 Draft 删除均在该事务内完成。
- system_closed 与 completed 语义不同；系统失效 Draft 保留。
- WPF 已提供 Dashboard、任务列表、导入、详情、草稿、重新确认、库存修正、正式提交和完成状态。
- 用户已接受当前 Stage 4 视觉与兼容性基线。

## 四、Stage 5 目标方向

Stage 5 的产品目标仅登记为“排查历史与修改追溯”。接任后应先核对现有 Inspection、InspectionItem、InspectionItemRevision、Task、Batch、LifecycleEvent 与 Draft 的真实模型和历史约束，再提交最小拆卡建议。

本文不批准历史编辑规则、Revision 写入条件、回滚语义、状态重算、页面范围或任务拆分；这些必须由新任 Sol 基于当前仓库事实提出，并由用户另行批准。

## 五、不可违反的边界

1. 不得重新实现或绕过 S4-T04 `InspectionSubmissionUseCase`。
2. 不得在 WPF/ViewModel 复制 Stage 3 生命周期、阶段、聚合、Reconfirm 或库存规则。
3. 不得把历史修改解释为重新提交旧 Draft，或直接改写既有 InspectionItem。
4. S3-T06 多轮修订时序必须基于正式 Revision 另行治理，不得破坏现有幂等锚点。
5. Excel 仍是局部增量；历史页不得把缺失数据解释为删除。
6. 未经批准不得新增 schema、migration、依赖、Repository/UnitOfWork、事件总线或通用工作流框架。

## 六、当前停止门禁

- 不创建、编号或派发 S5-T01。
- 不派发 Stage 5 Luna。
- 不编写 Stage 5 业务代码或 UI。
- 等待用户单独批准下一阶段。
