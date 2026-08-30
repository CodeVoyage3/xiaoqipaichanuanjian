# Stage 5 交接归档｜排查历史与修改追溯

> 归档日期：2026-08-30。Stage 5 已整体验收通过。本文件冻结阶段交付事实；后续不得改写为 Stage 6 实现记录。

## 一、阶段基线

- 分支：`master`。
- Stage 5 最终产品/单卡归档基线：`4e0635b90fdc25040e5602fe50e1a8f43d860f36`。
- S5-T01～S5-T04：全部实现、独立技术验收、必要用户 GUI 验收、正式数据恢复与隔离清理闭环。
- Stage 5 总验收：`.ai-dev/ACCEPTANCE/STAGE-5.md`。
- 最终验证：四卡专项 9/9、16/16、10/10、16/16；Stage 4 179/179；Stage 3 170/170；Release 583/583；Release build 0/0；EF 无漂移；8 条 migration；依赖未变。
- 正式数据库：299008 bytes，SHA-256 `F3D423DF14B882D7BFE87780A81CF5879F074AF4880601CBEDB6B475A964F522`；进程 0，无 WAL/SHM/journal，GUI 隔离数据已清理，历史正式备份保留。

接任者仍必须重新执行 branch、HEAD、status、测试/build、EF、依赖、进程及正式数据只读核验；上述 SHA 是 Stage 5 产品归档锚点，不替代未来 checkout 事实。

## 二、已完成产品能力

```text
Application
  InspectionHistoryQuery
    completed Inspection 列表
    单条正式 Inspection / Item 快照详情
    当前数量与 Revision 链只读查询
  InspectionHistoryEditUseCase
    单条 completed InspectionItem 数量修订
    changed / no_change / not_found
    当前值 + 结构化 Revision 原子一致
WPF
  排查历史入口、列表、详情、Revision
  条码/编码复制、返回与表格访问
  明细数量编辑、非负整数校验、显式确认
  提交互斥、固定目标、结果反馈和权威重读
```

## 三、Revision 最终契约

1. 历史列表和详情只认 completed Task 下的正式 Inspection/InspectionItem，不读取或重放 Draft。
2. Revision 保存 `InspectionItemId`、修改前数量、修改后数量和 UTC 修改时间；查询按时间、ID 稳定排序。
3. 修改请求必须是明确 InspectionId + InspectionItemId、非负整数和 UTC 时间；时间不得早于正式提交、当前 Item 更新时间或最新 Revision。
4. 同值返回 `no_change`，不写 Revision；真实变化在单一事务内新增一条 Revision 并更新 Item 当前数量/时间；失败全部回滚。
5. UI 的成功、同值、目标不存在和异常反馈必须基于用例结果；提交后重新读取正式详情与 Revision，不伪造当前值或修订链。

## 四、冻结权威边界

- Stage 3 继续唯一拥有阶段计算、任务聚合、商品归零、新批次/新到货/恢复和正式 0 件 Batch 停止规则。
- Stage 4 `InspectionSubmissionUseCase` 继续唯一拥有创建正式 Inspection/Items、处理正式 0 件、更新 `HandledAttentionVersion`、完成 Task 与处置有效 Draft 的事务。
- Stage 5 历史修改只修订正式 `InspectionItem.CheckedQty` 并留下 Revision；不触发 Lifecycle，不修改 Batch/Task/Draft/AttentionVersion，不重新提交、不重新计算旧业务结果。
- WPF 只展示、收集输入并调用 Application；不得直查 EF 或复制 Stage 3～5 业务规则。
- Excel 始终是局部增量，不是全量快照；历史页不得将缺失数据解释为删除。

## 五、已知 UI debt 与非 Stage 5 内容

- 当前历史 UI 已通过功能和可用性验收；纯视觉不满意延后到 Stage 7 完成后的全局 UI/UX 重构，不以 S5-T05 返工。
- 批量修改、Revision 编辑/删除、回滚/撤销、历史重新提交、生命周期联动、导出和历史性能专项不属于 Stage 5。
- Windows 提醒、托盘、关闭隐藏、显式退出、自启动和电源恢复属于 Stage 6；本归档没有实现这些能力。
- 安装、完整恢复产品化、规模性能与最终全局视觉收口仍属于后续阶段。

## 六、停止门禁

- 已生成 `.ai-dev/STAGES/STAGE-6-HANDOFF.md`，仅作接任资料。
- 不创建 S5-T05，不创建或编号 S6-T01，不派发 Stage 6 Luna，不实施 Stage 6 代码。
- 等待用户单独批准下一步。

