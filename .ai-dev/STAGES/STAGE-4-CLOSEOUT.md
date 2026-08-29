# Stage 4 交接归档｜排查工作流与核心桌面 UI

> 归档日期：2026-08-29。Stage 4 已整体验收通过。本文件冻结阶段交付事实；后续不得改写为 Stage 5 实现记录。

## 一、阶段基线

- 分支：`master`。
- Stage 4 最新实现提交：`0fd0190a4cb344ab7fad4b3eb5dd2cc847f3ce9f`。
- S4-T01～S4-T10：阶段范围全部闭环；S4-T09 的最终人工门禁由 S4-T10 GUI 验收一并完成。
- Stage 4 总验收：`.ai-dev/ACCEPTANCE/STAGE-4.md`。
- 最终验证：受影响 UI/S4-T10 84/84、Stage 4 权威类 179/179、Stage 3 权威类 184/184、Release 532/532、Release build 0/0、EF 无漂移、8 条 migration。
- 用户最终 GUI：10/10 通过，问题为 0。
- 正式数据库已恢复，299008 bytes，SHA-256 `F3D423DF14B882D7BFE87780A81CF5879F074AF4880601CBEDB6B475A964F522`；隔离数据已清理，应用进程为 0。

接任者仍必须以 `git rev-parse HEAD`、工作区和重新执行的门禁为准；上述 SHA 是实现锚点，不替代当前事实。

## 二、已交付边界

```text
Application
  InspectionTaskQuery                 Dashboard、任务列表与详情只读事实
  InspectionDraftUseCase              草稿、重新确认与主动清空
  ManualInventoryAdjustmentUseCase    手工库存修正与商品归零组合
  InspectionSubmissionUseCase         正式提交唯一事务入口
WPF
  Shell / Dashboard / PendingTasks
  Data Import / Detail / Draft / Reconfirm
  Inventory Adjustment / Submission / Over-stock
  Stage 4 final UI baseline
```

Stage 4 没有新增 schema、migration、业务状态、生命周期规则、通用框架或依赖。

## 三、冻结口径

1. Excel 是局部增量，不是全量快照。
2. canonical phase、任务聚合、归零、新到货、恢复与批次 0 停止继续由 Stage 3 权威实现。
3. `InspectionSubmissionUseCase` 是创建 Inspection、处理 0 件 Batch、更新 `HandledAttentionVersion`、完成 Task 和处置有效 Draft 的唯一事务入口。
4. UI 只展示、收集输入并调用 Application；不得直查 EF 或重建业务规则。
5. 正常批次只读；排查数量保持 `null / 0 / 正数` 三态；Reconfirm 继续依赖当前 AttentionVersion。
6. 用户已确认 1024×600、125%、滚轮、Tab、窗口恢复和最终视觉基线可用。

## 四、已知限制与后续边界

- 排查历史与修改追溯尚未实现，是 Stage 5 handoff 的目标方向，不是当前开发授权。
- S3-T06 的多轮 `0→正数→0` 历史修订时序仍需基于正式 Revision 治理，Stage 5 不得绕过现有旧事实重放保护。
- 排查记录、历史修改 UI 必须读取正式 Inspection/Item/Revision 事实，不得重新执行或复制 S4-T04 提交事务。
- 设置、提醒/托盘、自启动、导出、安装、完整恢复产品化及规模性能仍不属于 Stage 4。
- 在线 NuGet 漏洞审计因本机 SSL/TLS/凭据环境未完成，用户已接受风险；后续如环境恢复可重新审计，但不得改写历史结果。

## 五、归档门禁

- 不新增 S4-T11，不把 Stage 5 历史能力塞回 S4-T10。
- 不修改 Stage 3 生命周期、S4-T04 正式提交、schema 或 migration 来实现历史展示。
- 已生成 `.ai-dev/STAGES/STAGE-5-HANDOFF.md`；未经用户单独批准，不得创建或派发 S5-T01，不得进入 Stage 5 开发。
