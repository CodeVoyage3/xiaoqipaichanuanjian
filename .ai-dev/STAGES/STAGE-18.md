# Stage18｜v1.0.9 今日提醒与现场排查体验优化

日期：2026-09-10（Asia/Shanghai）

Stage18 = `IN_PROGRESS / S18_T01_MINOR_UI_SIZE_REWORK`

## 固定顺序

1. `S18-T01｜今日提醒弹窗信息层级优化`：`FUNCTIONALLY_ACCEPTED / MINOR_UI_SIZE_REWORK_PENDING / NOT_CLOSED`。
2. `S18-T02｜排查详情扫码识别与信息区紧凑化`：`PLANNED / NOT_DISPATCHED`。

S18-T01 必须先完成技术验收、用户真实 WPF GUI 验收并 `CLOSED`，才允许启动 S18-T02。不得混合实施。

S18-T01 implementation `87dd576fc0a69fe567a246dd55debb3aaddd642b` 已通过 Sol 独立专项、直接回归、Release build、范围与原型静态审查。用户真实 GUI 已确认功能和信息层级，通过项保持冻结；当前只允许把提醒弹窗视觉体量从约 `960 × 715` 收紧至约 `880～900 × 620～640`，完成后等待用户尺寸复核。

## 固定边界

- S18-T01 只把现有 `DailyReminderUseCase`、`InspectionTaskQuery.GetReminderCandidates` 与 `DailyReminderRuntimeCoordinator` 已形成的真实提醒数据映射到批准原型 A；不得重新计算提醒、阶段或优先级。
- S18-T02 仅记录计划；不得在 S18-T01 用户验收前实施、派发或引入条码依赖。
- 两张批准原型是硬视觉合同，不是灵感参考；WPF 允许等价还原，但信息层级不得改变。
- Schema、migration、ModelSnapshot、库存、任务生成、提醒时间、3 天预提醒、重复提醒、Updater、Installer 与发布通道均不变；migration 保持现状。
- 本次尺寸返修仅做 layout 静态 diff 检查与真实 GUI 无裁切/无遮挡复核；原 `26/26`、Release build、reminder 业务测试与 `FULL` 均 `NOT_RUN`。
- Sol 不写生产代码；每张正式 Task 使用全新 GPT-5.6 Terra、reasoning `medium`、标准速度、独立 clean worktree。Terra 不得自判 `ACCEPTED/CLOSED`，不得 tag/Release。

任务与验收见 `../TASKS/S18-T01.md`、`../ACCEPTANCE/S18-T01.md` 与 `../TASKS/S18-T02.md`。

## 发布门禁

只有 S18-T01 与 S18-T02 均 `GUI_ACCEPTED / CLOSED` 后，才允许 Stage18 `CLOSED` 并进入 v1.0.9 `RELEASE_PREPARATION`。本轮禁止 tag、Release、夸克/Gitee 发布或更新通道改造。
