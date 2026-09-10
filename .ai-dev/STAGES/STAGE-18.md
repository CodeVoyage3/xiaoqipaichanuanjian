# Stage18｜v1.0.9 今日提醒与现场排查体验优化

日期：2026-09-10（Asia/Shanghai）

Stage18 = `IN_PROGRESS / WAITING_USER_S18_T01_GUI_ACCEPTANCE`

## 固定顺序

1. `S18-T01｜今日提醒弹窗信息层级优化`：`IMPLEMENTED / TECHNICAL_ACCEPTANCE_READY / NOT_ACCEPTED`。
2. `S18-T02｜排查详情扫码识别与信息区紧凑化`：`PLANNED / NOT_DISPATCHED`。

S18-T01 必须先完成技术验收、用户真实 WPF GUI 验收并 `CLOSED`，才允许启动 S18-T02。不得混合实施。

S18-T01 implementation `87dd576fc0a69fe567a246dd55debb3aaddd642b` 已通过 Sol 独立专项、直接回归、Release build、范围与原型静态审查；当前只等待用户 GUI 验收，不得把技术证据改写为 GUI 通过。

## 固定边界

- S18-T01 只把现有 `DailyReminderUseCase`、`InspectionTaskQuery.GetReminderCandidates` 与 `DailyReminderRuntimeCoordinator` 已形成的真实提醒数据映射到批准原型 A；不得重新计算提醒、阶段或优先级。
- S18-T02 仅记录计划；不得在 S18-T01 用户验收前实施、派发或引入条码依赖。
- 两张批准原型是硬视觉合同，不是灵感参考；WPF 允许等价还原，但信息层级不得改变。
- Schema、migration、ModelSnapshot、库存、任务生成、提醒时间、3 天预提醒、重复提醒、Updater、Installer 与发布通道均不变；migration 保持现状。
- `FULL = NOT_RUN / NO_FULL`。只跑每张卡的专项、直接相关回归与必要 Release build。
- Sol 不写生产代码；每张正式 Task 使用全新 GPT-5.6 Terra、reasoning `medium`、标准速度、独立 clean worktree。Terra 不得自判 `ACCEPTED/CLOSED`，不得 tag/Release。

任务与验收见 `../TASKS/S18-T01.md`、`../ACCEPTANCE/S18-T01.md` 与 `../TASKS/S18-T02.md`。

## 发布门禁

只有 S18-T01 与 S18-T02 均 `GUI_ACCEPTED / CLOSED` 后，才允许 Stage18 `CLOSED` 并进入 v1.0.9 `RELEASE_PREPARATION`。本轮禁止 tag、Release、夸克/Gitee 发布或更新通道改造。
