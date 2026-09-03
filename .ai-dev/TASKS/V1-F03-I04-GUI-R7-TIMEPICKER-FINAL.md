# V1-F03-I04｜GUI R7 Reminder TimePicker 最终增量返修

## 状态与基线

- 批准日期：2026-09-03。
- GitHub `main` 唯一开工基线：`8fe0e733d0d44306bd9f3ac023c3acbe296e667e`。
- 当前主线：`V1-F03-I04｜WPF 双入口与端到端收口`。
- 用户已确认 R6 其他内容通过；本卡只允许修复 TimePicker 点击热区与 Popup 遮挡保存按钮两个 GUI blocker。

## 协作治理

- Sol 只负责治理、完整 diff 审查、自动化测试和技术验收，不实施生产代码。
- 生产代码由本 Sol 话题内全新 GPT-5.6 Terra（reasoning medium）实施；不得复用旧 Terra或新建独立 Codex 话题。
- Terra 完成后停止，不更新 Acceptance/Handoff、不 push；Sol 独立复验后负责治理记录与普通 push。
- 用户本人负责真实 WPF GUI 验收；技术结果不得写成 GUI PASSED。

## R7-01：扩大 TimePicker Popup 触发热区

- TimePicker 必须继续是可编辑 TextBox，保留直接输入与 R6 的 `H:mm`/`HH:mm` 格式化、字段错误和待保存状态。
- 用户不应精准瞄准极小图标区域：时间文字、输入框空白和右侧图标构成清晰、自然的大热区。
- 若单击文字立即展开会干扰插入点/选择/键盘编辑，允许保留主体编辑语义，但必须扩大右侧完整图标按钮区，并让非文字空白区域可自然打开 Popup。
- 不增加新业务按钮，不修改 Reminder 数据或保存逻辑；保留焦点、键盘可达与明确自动化名称。

## R7-02：Popup 智能定位且不遮挡保存

- 输入框下方空间足够时向下展开；不足时自动向上展开。
- Popup 必须完全位于设置窗口可见区域内，不超出底部、不覆盖“保存”按钮，并保持与输入框明确锚定。
- 优先使用 WPF `Placement`、`CustomPopupPlacementCallback`、`PlacementRectangle` 或基于实际可用空间的定位；不得以固定屏幕坐标硬编码。
- Popup 继续保持小时/分钟窄列、每列约 3～5 项、紧凑取消/确定、无多余白块，不恢复 R5 大面板。

## 冻结边界

- 可直接输入、格式化、非法字段错误、Reminder 保存与 Reschedule、Popup 暂存/取消/确定、StageBadge 统一、蓝色降权、确认窗口、Today 页面全部冻结。
- I01～I03、ProductTask 生命周期、Reminder 核心调度、Settings 数据结构、Excel、Schema、migration、ModelSnapshot、`.csproj`、`.slnx`、依赖与全局 Theme 均不得修改；migration 必须仍为 9。
- 不进入 Stage 8/9，不实施在线升级，不自动关闭 I04/F03。

## 自动化与静态契约

- TimePicker 仍为可编辑 TextBox。
- Popup 触发不再只依赖极小图标热区，同时不破坏文字编辑。
- Popup 使用可根据实际空间上下调整的定位策略，而非固定向下或固定坐标。
- Popup 最大宽高受约束，列与按钮保持紧凑；定位逻辑至少覆盖“下方足够向下、下方不足向上、不得覆盖保存区域”。
- 既有 Settings 保存、Reminder Reschedule、R6 输入/Popup/可访问性测试继续通过。
- 无 Schema、migration、项目或依赖变化。

## Terra 停止门禁

- 完成最小生产 diff 与专项测试后提交并停止；不得 push、不得修改本卡、Acceptance 或 Handoff。
- 报告提交 SHA、改动文件、测试结果、未启动/操控 WPF、未访问生产数据库及已知 GUI 风险。

## Sol 独立验收门禁

- 完整审查本卡治理提交至实现 HEAD，确认只有 TimePicker UI、Popup 定位及对应测试变化。
- 独立运行 R7 专项、Settings/Reminder、I01～I04/UIUX、Release 全量与 Release build。
- Release build 必须 0 warning / 0 error；EF 无漂移；migration=9；`.csproj`、`.slnx`、migration、ModelSnapshot 与依赖无变化；`git diff --check` 通过。
- 不启动/操控 WPF，不访问生产数据库。
- 通过后更新 `.ai-dev/ACCEPTANCE/V1-F03-I04.md` 与 `.ai-dev/HANDOFF/LATEST.md`，状态写为：
  `GUI_R7_TIMEPICKER_TECHNICALLY_ACCEPTED / GUI_ACCEPTANCE_FAILED / WAITING_USER_RETEST`

## 用户最终 GUI 门禁

- 用户只验两项：不再需要精准点击小蓝框即可打开选择器；Popup 完全不遮挡设置页底部“保存”按钮。
- 用户通过前，I04/F03 不得 CLOSED。
