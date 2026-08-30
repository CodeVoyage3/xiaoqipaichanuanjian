# Stage 5 整体验收记录｜排查历史与修改追溯

- 验收日期：2026-08-30
- 结论：通过；Stage 5 正式完成，暂停在 Stage 5 → Stage 6 门禁
- 验收范围：`S5-T01` 至 `S5-T04`
- 总验收前基线：`master @ 4e0635b90fdc25040e5602fe50e1a8f43d860f36`，clean
- 独立总验收：GPT-5.6 Sol
- 最终 GUI 验收：用户本人

## 四卡交付结论

| Task | 已交付能力 | 实现提交 | 最终验收提交 | 结论 |
|---|---|---|---|---|
| S5-T01 | completed 正式排查历史列表与单条详情只读查询 | `e741df4e128b43c2b45d0e97c320aae15471f424` | `28cd8f24349a2d463956a92290cd91f4b144797f` | 通过 |
| S5-T02 | 正式明细数量修订、原子 Revision 与修订链查询 | `33424ef6cddbd109b2e9c59941a7e182324270e7` | `b3c5515d2e6f3215aa79f5325fd00de77f2f8358` | 通过 |
| S5-T03 | WPF 历史列表、详情与 Revision 只读展示 | `3f44388193fc03cfde82ee169912f868ba32e219`；闪退修复 `4341c213dc0377a661992aecdde93ffead034b7d`；最终布局 `e4c3d4d8d6b18c3fc9e1f9773815d6fde11430ae` | `3241906d8fa7fb67a3b3d7167ff77c113b4fe40e` | 通过 |
| S5-T04 | WPF 从明确选中的正式明细发起受控数量修订并刷新当前值/Revision | `5818379d751c1aa479489d5febcad6208dd92e9b` | `4e0635b90fdc25040e5602fe50e1a8f43d860f36` | 通过 |

任务卡和验收记录均存在；未创建 S5-T05、Stage 6 Task 或 Stage 6 生产代码。

## 最终独立技术门禁

所有测试均使用 Release 配置和精确类过滤；TRX 暂存于 `obj/Stage5Closeout`。

| 门禁 | 实际结果 |
|---|---|
| S5-T01 `InspectionHistoryQueryTests` | 9/9 |
| S5-T02 `InspectionHistoryEditUseCaseTests` | 16/16 |
| S5-T03 `S5T03InspectionHistoryViewModelTests` | 10/10 |
| S5-T04 `S5T04InspectionHistoryEditViewModelTests` | 16/16 |
| Stage 4 八个权威测试类 | 179/179 |
| Stage 3 十个权威测试类 | 170/170 |
| Release 全量 | 583/583，0 失败、0 跳过 |
| Release build | 0 warning、0 error |
| EF 模型 | `has-pending-model-changes`：无漂移 |
| migration | 仓库与正式库均为 8 条 |
| dependency | 相对 Stage 4 收口基线工程依赖 diff 为空；当前直接/传递解析版本与 Stage 5 已归档记录一致 |
| Git | 总验收开始时 clean；`git diff --check` 通过 |

Stage 4 使用 `InspectionTaskQueryTests`、`InspectionDraftUseCaseTests`、`ManualInventoryAdjustmentUseCaseTests`、`InspectionSubmissionUseCaseTests`、`Stage4ViewModelTests`、`S4T06ImportViewModelTests`、`S4T07InspectionDetailViewModelTests`、`S4T08InspectionSubmissionViewModelTests`。Stage 3 继续使用 S5-T01～T04 统一的十个权威类，实际为 170 项；不改写 Stage 4 历史档案中的 184/184。

Restore 使用 `--ignore-failed-sources -p:NuGetAudit=false`；当前包版本检查是离线解析事实，不宣称在线漏洞审计成功。EF 先以 `--no-connect` 核对仓库八条 migration；再对正式旁置原件的字节一致只读副本连接核对，八条均已应用。工具侧默认 C 盘 ReparsePoint 仍会导致直接 EF 连接失败，此失败未被冒充通过。

## 产品边界总审查

1. `InspectionHistoryQuery` 只读列出 completed 正式排查，并可读取单条正式详情及稳定排序的 Revision 链。
2. `InspectionHistoryEditUseCase` 只允许 completed 正式明细、非负整数和合法 UTC 修改时间；真实变化时在单一事务内写一条 previous/new/time Revision 并更新当前值；同值不写 Revision。
3. WPF 已闭环历史列表、详情、Revision、复制、选择、显式确认、输入校验、提交互斥、结果反馈和数据库重读刷新。
4. 历史修改不调用 Stage 3 Lifecycle，不更新 Batch/Task/AttentionVersion；不调用或重放 Stage 4 `InspectionSubmissionUseCase`，不重走旧 Draft。
5. Stage 4 Draft、Reconfirm、InventoryAdjustment、Submission 与 Stage 3 生命周期权威类全部回归通过；Domain、Infrastructure、Migrations 和工程依赖相对 Stage 4 收口未改变。
6. 不提供批量修改、删除、撤销、回滚、Revision 编辑、历史重新提交或生命周期重算。

执行单十二项核心边界全部成立，无阻断缺口。

## GUI、数据保护与 UI debt

- S5-T03 用户 GUI 通过：历史入口、详情、三张只读表、Revision、复制、返回及 1024×600 / 125% 可操作性成立；真实缺陷修复过程保留在单卡验收记录。
- S5-T04 用户 GUI 通过：隔离数据中当前数量由 4 更新到 7，并正确显示 `4 → 7` Revision；同值、非法输入、取消、防重入和导航边界按用户接受的清单闭环。Sol 未用电脑操控代验。
- 最新本机恢复回执为 `.ai-dev/ACCEPTANCE/S5-T04-RESTORE-RESULT.json`：`RESTORE_PASS`、进程 0、隔离运行目录和保护暂存均已移除、恢复脚本未启动应用。
- 正式数据库旁置原件重新核验为 299008 bytes，SHA-256 `F3D423DF14B882D7BFE87780A81CF5879F074AF4880601CBEDB6B475A964F522`；无 WAL/SHM/journal。历史正式备份继续保留。
- `obj/S5T03GuiAcceptance`、`obj/S5T04GuiAcceptance` 均不存在；只读核验副本已删除。恢复后未启动应用。
- 当前 UI 已满足功能验收。纯视觉不满意只记为 UI debt，不创建 S5-T05；Stage 7 完成后再统一进行全局 UI/UX 重构与视觉收口。

## 范围与最终结论

- 本次总验收只新增治理文档，不修改生产功能、schema、migration 或依赖。
- 未创建或派发新 Luna；未创建 S5-T05、S6-T01 或任何 Stage 6 Task；未实施 Stage 6。
- Stage 5 整体验收通过并正式完成。已生成 closeout 与 Stage 6 handoff；Stage 6 尚未开始，等待用户另行批准。

