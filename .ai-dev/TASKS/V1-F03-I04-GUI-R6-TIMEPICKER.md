# V1-F03-I04｜GUI R6 提醒时间 TimePicker 重构 + 蓝色视觉降权 + Stage 视觉统一

## 状态与基线

- 批准日期：2026-09-03。
- GitHub `main` 唯一开工基线：`29f1a30aa4094259085f5af9b018e0aee5d2af8f`。
- 当前主线：`V1-F03-I04｜WPF 双入口与端到端收口`。
- 本卡只授权 TimePicker、直接相关蓝色降权与 Confirmation StageBadge 对齐；不得自动关闭 I04/F03。

## 协作治理

- Sol 只负责治理、完整 diff 审查、自动化测试和技术验收，不实施生产代码。
- 生产代码由本 Sol 话题内全新 GPT-5.6 Terra（reasoning medium）实施；不得复用旧 Terra或新建独立 Codex 话题。
- Terra 完成后停止，不更新 Acceptance/Handoff、不 push；Sol 独立复验后负责治理记录与普通 push。
- 用户本人负责真实 WPF GUI 验收；自动化和构建不得写成 GUI PASSED。

## 范围 A：紧凑 TimePicker

1. 废弃 R5 在设置页内撑高窗口的双大列表；最终为可编辑时间文本框与右侧中性时间图标，图标打开锚定输入框附近的轻量 WPF Popup。
2. 直接输入接受 `9:30`、`09:30`、`14:33`、`23:59`，提交后统一为 `HH:mm`；不要求无冒号快捷格式。
3. 空值、`24:00`、分钟越界、字母、缺分钟及其他非法值必须显示字段级“请输入有效时间（00:00–23:59）”，不得覆盖上一合法值、持久化或触发 Reschedule。
4. 输入过程中不持久化；Enter 或失焦只把合法编辑值提交到设置页待保存状态。最终持久化仍只发生在既有设置保存流程。
5. Popup 为小时 `00..23`、分钟 `00..59` 两个窄列，各显示约 3～5 个相邻值；当前值居中高亮，支持滚轮、点击和打开定位。
6. Popup 取消/Esc/点击外部均不得应用暂存选择；确定才以 `HH:mm` 写回同一个时间值。不得因失焦偷偷应用半完成选择。
7. Popup 不长期改变设置窗口高度，不新增第三方控件、NuGet 包或 UI 框架。

## 范围 B：直接相关蓝色降权

- 只调整 TimePicker、Popup、确认窗口及直接相关局部样式；黑灰白承担主体层级。
- 时间输入正常态白底、浅灰边框；Focus 仅允许细蓝描边。Popup 选中态使用低饱和浅灰蓝，时钟图标使用深灰/中性色；确定按钮可保留小面积主色，取消保持中性。
- 不修改全局 Accent/Theme token，不重构其他页面或已冻结视觉。

## 范围 C：StageBadge 完全统一

- Today 当前已通过的 `StageBadgeTemplate` 是权威；Confirmation 必须直接复用该模板，或在确有容器限制时让两处引用同一组共享视觉资源。
- 同一 canonical Stage 的中文文案、前景、背景、边框、圆角、Padding 必须一致；至少覆盖 `expired` 与一个非 expired Stage。
- Confirmation 不得保留独立 Stage 配色或根据中文字符串决定颜色，不得重新计算 Stage；只使用 Preview canonical Stage 与既有唯一中文映射。
- 不借机重新设计 Today 阶段颜色；共享化所需之外 Today 视觉保持不变。

## 冻结边界

- R5 摘要、少行收缩/多行滚动、确认六列；Today 六列、筛选、TaskId 选择与 500+ virtualization 均冻结。
- I01 导出、I02 Reader/stale/Draft、I03 Bulk Submission、数量语义、超库存、过期正库存警告、ProductTask 生命周期与 Submitted 权威刷新均冻结。
- Reminder 默认时间、每日一次、同日幂等、启动补提醒、scheduler/Reschedule、托盘、自启动、Application service 与数据表均冻结。
- 商品源导入、Schema、migration、ModelSnapshot、`.csproj`、`.slnx`、依赖与全局主题 token 均不得改变；migration 必须仍为 9。
- 不进入 Stage 8/9，不实施在线升级，不自动关闭 I04/F03。

## 自动化契约

- TimePicker：读取既有 Reminder 值；合法直接输入与 `9:30 → 09:30`；非法拒绝且上一合法值不变；Popup 打开定位、取消不应用、确定应用；结果稳定 `HH:mm`；既有保存与 Reschedule 回归通过。
- StageBadge：Today 与 Confirmation 共享同一模板/资源；`expired` 和至少一个非 expired Stage 视觉来源一致；Confirmation 无独立颜色硬编码。
- 蓝色降权：正常边框不使用强主蓝、选中态为低饱和浅色、图标中性，且全局 Accent/Theme 未改。

## Terra 停止门禁

- 完成最小生产 diff 与专项测试后提交并停止；不得 push、不得修改本卡、Acceptance 或 Handoff。
- 明确报告提交 SHA、改动文件、测试结果、未启动/操控 WPF、未访问生产数据库及已知风险。

## Sol 独立验收门禁

- 完整审查本卡治理提交至实现 HEAD，确认只修改 TimePicker UI/状态、相关局部视觉、StageBadge 共享资源及测试；I01～I03、Reminder 核心算法、Today Stage 业务逻辑无 diff。
- 独立运行 R6 专项、I01～I04/WPF/UIUX、Stage 6 Reminder/Settings、ProductTask/生命周期/历史/商品导入、Release 全量与 Release build。
- Release build 必须 0 warning / 0 error；EF 无漂移；migration=9；项目、依赖、migration、ModelSnapshot 无变化；`git diff --check` 通过。
- 不启动/操控 WPF，不访问生产数据库。
- 通过后更新 `.ai-dev/ACCEPTANCE/V1-F03-I04.md` 与 `.ai-dev/HANDOFF/LATEST.md`，状态写为：
  `GUI_R6_TIMEPICKER_TECHNICALLY_ACCEPTED / GUI_ACCEPTANCE_FAILED / WAITING_USER_RETEST`

## 用户真实 GUI 门禁

- 用户只需重验直接输入/格式化/非法错误、紧凑 Popup 的定位/滚动/取消/确定、保存后 Reminder，以及 Today/Confirmation 至少两个 Stage 的视觉一致和相关区域蓝色降权。
- 用户通过前，I04/F03 不得 CLOSED。
