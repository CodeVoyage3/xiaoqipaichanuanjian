# Stage18｜v1.0.9 今日提醒与现场排查体验优化

日期：2026-09-10（Asia/Shanghai）

Stage18 = `IN_PROGRESS / S18_T01_CLOSED / S18_T02_PLANNED`

## R5 当前门禁

最终批准视觉基线为 `D:\下载\ChatGPT Image 2026年9月10日 21_43_57.png`（SHA256 `AFC6A06E691F3755CD7B4868F00E39308753E08C9EEC072DC98F089EFC182B7B`）。R1～R4 只保留为拒绝历史。R5 从本治理提交后的 fresh `origin/main` 由全新 GPT-5.6 Terra（medium）在 clean worktree 实施，Sol 独立真实 GUI 门禁后等待用户验收。

S18-T01=`GUI_ACCEPTED / CLOSED`；最终 R5 视觉锁定为用户通过的 `660 × 360 DIP` 版本。S18-T02=`PLANNED / NOT_DISPATCHED`，用户决定在新的 Codex 话题中单独开始，本话题禁止启动或派发。

## 固定顺序

1. `S18-T01｜今日提醒弹窗信息层级优化`：`GUI_ACCEPTED / CLOSED`。
2. `S18-T02｜排查详情扫码识别与信息区紧凑化`：`PLANNED / NOT_DISPATCHED`。

S18-T01 已完成技术验收及用户真实 WPF GUI 验收并 `CLOSED`。S18-T02 仍不得在本话题启动，等待用户开启新的 Codex 话题。

S18-T01 最终 product implementation `0f3bdf287b73263f72f4c4500b82e38da41c89c6` 已通过用户 GUI 验收；其 `660 × 360 DIP` 视觉、业务映射、数据口径和导航证据全部冻结。此前 R1～R4 拒绝结论只作历史保留。

## 固定边界

- S18-T01 只把现有 `DailyReminderUseCase`、`InspectionTaskQuery.GetReminderCandidates` 与 `DailyReminderRuntimeCoordinator` 已形成的真实提醒数据映射到批准原型 A；不得重新计算提醒、阶段或优先级。
- S18-T02 仅记录计划；不得在 S18-T01 用户验收前实施、派发或引入条码依赖。
- 两张批准原型是硬视觉合同，不是灵感参考；WPF 允许等价还原，但信息层级不得改变。
- Schema、migration、ModelSnapshot、库存、任务生成、提醒时间、3 天预提醒、重复提醒、Updater、Installer 与发布通道均不变；migration 保持现状。
- 本次视觉返修仅做静态 diff/XAML 检查、一次必要 build（若 GUI harness 未覆盖）与隔离真实 GUI；原 `26/26`、Release build、reminder 业务测试与 `FULL` 均 `NOT_RUN`。
- Sol 不写生产代码；每张正式 Task 使用全新 GPT-5.6 Terra、reasoning `medium`、标准速度、独立 clean worktree。Terra 不得自判 `ACCEPTED/CLOSED`，不得 tag/Release。

任务与验收见 `../TASKS/S18-T01.md`、`../ACCEPTANCE/S18-T01.md` 与 `../TASKS/S18-T02.md`。

## 发布门禁

只有 S18-T01 与 S18-T02 均 `GUI_ACCEPTED / CLOSED` 后，才允许 Stage18 `CLOSED` 并进入 v1.0.9 `RELEASE_PREPARATION`。本轮禁止 tag、Release、夸克/Gitee 发布或更新通道改造。
