# V1-F03-I04｜GUI R10 Settings Footer 紧凑收口

## 状态与基线

- 批准日期：2026-09-03。
- GitHub `main` 唯一开工基线：`51b225b5fb1875b10b19e5ff906e838416a10e28`。
- 当前主线：`V1-F03-I04｜WPF 双入口与端到端收口`。
- 用户已确认 R9 时间输入、明确“选择时间”按钮和独立模态小窗通过；本卡只处理设置窗内容与 Footer 之间的大块无意义空白。

## 协作治理

- Sol 只负责治理、完整 diff 审查、自动化测试和技术验收，不实施生产代码。
- 生产代码由本 Sol 话题内全新 GPT-5.6 Terra（reasoning medium）实施；不得复用旧 Terra或新建独立 Codex 话题。
- Terra 完成后提交并停止，不更新本卡、Acceptance/Handoff、不 push；Sol 独立复验后负责治理记录与普通 push。
- 用户本人负责真实 WPF GUI 验收；技术结果不得写成 GUI PASSED。

## 唯一实施范围

- 设置窗按内容紧凑排布：标签、说明、`[10:00] [选择时间]`、开机自启动、约 20～24px 的正常间距、右对齐“取消 / 保存”。
- 根布局使用内容区 `Auto` + Footer `Auto`；不得用 `*` 行、大型空白占位或固定过高窗口制造垂直空白。
- 优先由内容决定窗口高度；Footer 与窗口底部保留约 16～20px 安全边距。
- “保存”继续是主按钮，“取消”继续是次按钮；文字、默认/取消语义、点击行为与焦点顺序不变。

## 冻结边界

- R9 的可编辑时间 TextBox、88×32“选择时间”按钮、独立模态“选择提醒时间”小窗、小时/分钟范围、滚轮、键盘、取消和确定行为全部冻结；不得恢复 Popup。
- Reminder 保存/Reschedule、开机自启动业务、Settings 数据结构、R5/R6、Today、I01～I03、ProductTask、Excel、Schema、migration、ModelSnapshot、依赖、`.csproj`、`.slnx` 与全局 Theme 均不得修改；migration 必须仍为 9。
- 不进入 Stage 8/9，不实施在线升级，不自动关闭 I04/F03。

## 自动化与静态契约

- 锁定设置窗根布局的内容行与 Footer 行均为 `Auto`，不存在用于撑高的 `*` 行。
- 锁定窗口由内容决定高度或采用等效紧凑约束，Footer 上间距有限且底部安全边距明确。
- 锁定“取消 / 保存”右对齐、样式和语义不变。
- 锁定 R9 可编辑 TextBox、“选择时间”按钮、独立小窗及无 Popup 契约继续通过。
- Settings 保存、Reminder Reschedule、I01～I04/UIUX 与 Release 回归继续通过；无 Schema、migration、项目或依赖变化。

## Terra 停止门禁

- 只提交最小 Settings Footer 布局与直接静态测试差异后停止；不得 push、不得修改治理文档。
- 报告提交 SHA、改动文件、测试结果、未启动/操控 WPF、未访问生产数据库及已知 GUI 风险。

## Sol 独立验收门禁

- 完整审查本卡治理提交至实现 HEAD，确认只有 Settings Footer/垂直布局和直接测试差异，R9 时间控件与 Reminder/自启动业务无 diff。
- 独立运行 R10 专项、Settings/Reminder、I01～I04/UIUX、Release 全量与 Release build。
- Release build 必须 0 warning / 0 error；EF 无漂移；migration=9；项目、依赖、migration、ModelSnapshot 无变化；`git diff --check` 通过。
- 不启动/操控 WPF，不访问生产数据库。
- 通过后更新 `.ai-dev/ACCEPTANCE/V1-F03-I04.md` 与 `.ai-dev/HANDOFF/LATEST.md`，状态写为：
  `GUI_R10_SETTINGS_FOOTER_TECHNICALLY_ACCEPTED / GUI_ACCEPTANCE_FAILED / WAITING_USER_RETEST`

## 用户最终 GUI 门禁

- 用户只验两点：设置窗 Footer 上方不再有大块空白；R9 的时间输入、“选择时间”按钮和独立小窗仍正常显示。
- 用户通过前，I04/F03 不得 CLOSED。
