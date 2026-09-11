# Stage18｜v1.0.9 今日提醒、现场排查与导航体验优化

日期：2026-09-10（Asia/Shanghai）

Stage18 = `IN_PROGRESS / S18_T03_R1_MINOR_GUI_REWORK`

## S18-T03-R1 当前返修

用户首轮真实 GUI 验收=`NOT_ACCEPTED`：收起态丢失业务分组节奏，当前页面的菜单 Selected 映射不正确。R1 仅授权在导航 XAML / Style 中增加轻量收起态分组表现并修正 Selected/Hover 状态；`220 / 72 DIP`、Logo、品牌文字、七项 Geometry/语义、同一菜单、ToolTip、持久化、Shell/业务/右侧页面、数据库、Schema/migration 与版本全部冻结，`BUSINESS LOGIC DIFF=0`。

技术完成后 S18-T03 只能为 `R1_TECHNICAL_ACCEPTANCE_READY / NOT_ACCEPTED`，等待用户重验 A～D；Stage18 保持 `IN_PROGRESS`。验证只含两项最小静态契约、必要一次 Release App build 与纯 GUID 隔离 GUI；`FULL=NOT_RUN / NO_FULL`。

## S18-T03 首轮技术验收（历史）

S18-T03=`IMPLEMENTED / TECHNICAL_ACCEPTANCE_READY / NOT_ACCEPTED`。批准原型 `D:\下载\ChatGPT Image 2026年9月10日 23_36_49.png`（SHA256 `2FB55AF5C17454A73ED9C963B347C96212A130B7456DCC5EF7C1C8AA7D908E22`）保持 HARD VISUAL BASELINE。implementation=`35ae161ae11ad80c7acfa0cc2fd610cd62663367`；Sol 独立专项=`2/2 PASS`、Shell 直接回归=`1/1 PASS`、Release App build=`0 warning / 0 error`、隔离主窗口启动成功；FULL=`NOT_RUN / NO_FULL`。

当前只等待用户 GUI A～H；不得关闭 S18-T03/Stage18、进入 v1.0.9 发布准备或发布任何渠道。

## S18-T02-R2 收口

R1 的分割线、数字复制、手机扫码、排查信息、按钮及其他详情功能已由用户确认通过并冻结。R2 已平衡左侧商品名称/编码的垂直位置，并删除条码控件自身外围矩形框；商品卡高度、中/右栏及 EAN-13/业务逻辑无变化。

R2 baseline=`32ab9d4b99c2202abc10b970e58f5d8e2266e2c4`，implementation=`5269ca4af330888eb262655e0b9c23305f1f70c1`；用户已确认最终 GUI A～C 全部 PASS。S18-T02=`GUI_ACCEPTED / CLOSED`。本轮未再运行测试或 build，FULL=`NOT_RUN / NO_FULL`。

下一张任务：`S18-T03｜左侧导航栏展开/收起与企业 SaaS 视觉重构`，状态=`PLANNED / NOT_DISPATCHED`。必须在新的 Codex 话题中，由 Sol fresh fetch 后单独建立治理、Task、Acceptance 和实施合同；本话题不创建、不派发、不冻结详细技术方案。

## S18-T02-R1 当前返修

用户已通过扫码、排查信息紧凑化和按钮颜色；R1 已补顶部三条浅灰竖向分割线，并把条码数字换为复用既有无边框透明样式的只读可选 TextBox。其余生产逻辑和页面冻结。该轮证据已由 R2 最终 GUI 通过收口覆盖，S18-T02=`GUI_ACCEPTED / CLOSED`。

R1 governance=`cb02504b981b8b97d491881d7dc713b39a55e2e1`，implementation=`bb9c4afd7ceef975752f7493bfe66d46e6ebc1ba`；Sol 独立 `46/46 + fixture 1/1` 与 Release `0 warning / 0 error`。FULL=`NOT_RUN`，只等待用户 A～D。

## S18-T02 当前治理冻结

S18-T02 正式名称为“排查详情扫码识别、信息区紧凑化与按钮视觉一致性收口”，范围固定为 A 真实 ProductBarcode/EAN-13、B 排查信息同排紧凑化、C 共享 Button 模板视觉根治。状态=`IMPLEMENTED / TECHNICAL_ACCEPTANCE_READY / NOT_ACCEPTED`。

实现 `bf32c192554d3e890b3c7126adb9d5196654d289` 与后续 R1/R2 已完成技术及用户 GUI 验收；当前 S18-T02=`GUI_ACCEPTED / CLOSED`，Stage18 保持 `IN_PROGRESS`。

fresh baseline=`origin/main@1979fdd93a6aa766bb5b33e6086df11b995aeae6`。NO NEW DEPENDENCY；Schema/migration/ModelSnapshot=`NO CHANGE / 9`；正式数据库=`NO ACCESS`；`FULL=NOT_RUN / NO_FULL`。从本治理提交后的 fresh origin/main 派发全新 GPT-5.6 Terra（medium）clean worktree；技术通过后仍须等待用户 GUI。

## S18-T01-R5 历史收口

最终批准视觉基线为 `D:\下载\ChatGPT Image 2026年9月10日 21_43_57.png`（SHA256 `AFC6A06E691F3755CD7B4868F00E39308753E08C9EEC072DC98F089EFC182B7B`）。R1～R4 只保留为拒绝历史。R5 从本治理提交后的 fresh `origin/main` 由全新 GPT-5.6 Terra（medium）在 clean worktree 实施，Sol 独立真实 GUI 门禁后等待用户验收。

S18-T01=`GUI_ACCEPTED / CLOSED`；最终 R5 视觉锁定为用户通过的 `660 × 360 DIP` 版本。S18-T02 原 `PLANNED / NOT_DISPATCHED` 门禁已由本话题用户授权和上方治理冻结取代。

## 固定顺序

1. `S18-T01｜今日提醒弹窗信息层级优化`：`GUI_ACCEPTED / CLOSED`。
2. `S18-T02｜排查详情扫码识别、信息区紧凑化与按钮视觉一致性收口`：`GUI_ACCEPTED / CLOSED`。
3. `S18-T03｜左侧导航栏展开/收起与企业 SaaS 视觉重构`：`IMPLEMENTED / TECHNICAL_ACCEPTANCE_READY / NOT_ACCEPTED`。

S18-T01、S18-T02 已完成技术验收及用户真实 WPF GUI 验收并 `CLOSED`。S18-T03 已按上方冻结合同获准派发。

S18-T01 最终 product implementation `0f3bdf287b73263f72f4c4500b82e38da41c89c6` 已通过用户 GUI 验收；其 `660 × 360 DIP` 视觉、业务映射、数据口径和导航证据全部冻结。此前 R1～R4 拒绝结论只作历史保留。

## 固定边界

- S18-T01 只把现有 `DailyReminderUseCase`、`InspectionTaskQuery.GetReminderCandidates` 与 `DailyReminderRuntimeCoordinator` 已形成的真实提醒数据映射到批准原型 A；不得重新计算提醒、阶段或优先级。
- S18-T02 仅记录计划；不得在 S18-T01 用户验收前实施、派发或引入条码依赖。
- 两张批准原型是硬视觉合同，不是灵感参考；WPF 允许等价还原，但信息层级不得改变。
- Schema、migration、ModelSnapshot、库存、任务生成、提醒时间、3 天预提醒、重复提醒、Updater、Installer 与发布通道均不变；migration 保持现状。
- 本次视觉返修仅做静态 diff/XAML 检查、一次必要 build（若 GUI harness 未覆盖）与隔离真实 GUI；原 `26/26`、Release build、reminder 业务测试与 `FULL` 均 `NOT_RUN`。
- Sol 不写生产代码；每张正式 Task 使用全新 GPT-5.6 Terra、reasoning `medium`、标准速度、独立 clean worktree。Terra 不得自判 `ACCEPTED/CLOSED`，不得 tag/Release。
- S18-T03 只改现有左侧 Navigation，右侧业务页面、Shell 页面映射、业务逻辑、Schema/migration、版本及发布链全部冻结。

任务与验收见 `../TASKS/S18-T01.md`、`../ACCEPTANCE/S18-T01.md`、`../TASKS/S18-T02.md`、`../ACCEPTANCE/S18-T02.md`、`../TASKS/S18-T03.md` 与 `../ACCEPTANCE/S18-T03.md`。

## 发布门禁

只有 S18-T01、S18-T02 与 S18-T03 均 `GUI_ACCEPTED / CLOSED` 后，才允许 Stage18 `CLOSED` 并进入 v1.0.9 `RELEASE_PREPARATION`。本轮禁止 tag、Release、夸克/Gitee 发布或更新通道改造。
