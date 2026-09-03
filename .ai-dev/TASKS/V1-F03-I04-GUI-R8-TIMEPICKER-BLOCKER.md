# V1-F03-I04｜GUI R8 Reminder TimePicker 最终阻断修复

## 状态与基线

- 批准日期：2026-09-03。
- GitHub `main` 唯一开工基线：`eafa579e5e0ce3bc0399635c06870d2ab68e6307`。
- 当前主线：`V1-F03-I04｜WPF 双入口与端到端收口`。
- 用户真实 WPF 录像确认 R7 GUI 失败；本卡只允许修复右侧触发按钮热区与 Popup 不改变窗口尺寸/不遮挡保存两个 blocker。

## 协作治理

- Sol 只负责治理、完整 diff 审查、自动化测试和技术验收，不实施生产代码。
- 生产代码由本 Sol 话题内全新 GPT-5.6 Terra（reasoning medium）实施；不得复用旧 Terra或新建独立 Codex 话题。
- Terra 完成后提交并停止，不更新本卡、Acceptance/Handoff、不 push；Sol 独立复验后负责治理记录与普通 push。
- 用户本人负责真实 WPF GUI 验收；技术结果不得写成 GUI PASSED。

## R8-01：冻结左右分区交互

- 左侧继续是可编辑 TextBox，只负责焦点、光标编辑、直接输入及既有 `H:mm`/`HH:mm` 格式化和非法值校验；点击文字或输入区不得打开 Popup。
- 右侧唯一按钮负责打开 Popup；按钮宽约 36～40px、高度与输入框一致，整个 Button 根矩形均为 HitTest 热区，不得只让图标响应。
- 右侧按钮须有轻量 Hover/Press/Focus 反馈及明确 Automation 名称；图标继续使用中性色，不恢复高饱和蓝，也不增加第二个按钮。

## R8-02：纯浮层与保存按钮可见

- Settings Window 在 Popup 打开、关闭前后尺寸必须完全一致；删除 R7 的临时 `Height`/`MinHeight` 修改及为 Popup 撑开父布局的逻辑。
- 继续使用真正 WPF `Popup`，不得参与主窗口 Measure/Arrange。
- Popup 根据输入框实际屏幕位置、设置窗口实际可见矩形、Popup 实际尺寸和当前 DPI 优先向下、空间不足向上；不得使用固定屏幕绝对坐标。
- Popup 必须在设置窗口可见范围内且不覆盖“保存”；保存按钮在 Popup 打开期间始终可见。优先缩小 Popup，不得改变 Window。
- 保留小时 `00..23`、分钟 `00..59`、滚轮、点击、当前值定位、取消、确定；窄列约显示 3～5 项，Popup 与操作区保持最小足够尺寸。

## 冻结边界

- 直接输入、格式化、非法值校验、Popup 暂存/取消/确定、Reminder 保存/Reschedule、R5 确认窗口、StageBadge、蓝色降权、Today、大类筛选全部冻结。
- I01～I03、ProductTask、Excel、Reminder 核心调度、托盘/自启动、Settings 数据结构、Schema、migration、ModelSnapshot、`.csproj`、`.slnx`、NuGet、第三方 UI 库、全局 Theme 均不得修改；migration 必须仍为 9。
- 不进入 Stage 8/9，不实施在线升级，不自动关闭 I04/F03。

## 自动化与静态契约

- 锁定 TextBox 仍可编辑且不绑定 Popup 打开；Popup 只由右侧完整 Button 打开，按钮不得退回 20～28px 小热点，图标不得是唯一 HitTest 对象。
- 锁定 Popup 打开/关闭不修改 Settings Window `Height`/`MinHeight`，不得存在 R7 的 `300→420` 临时增高或恢复逻辑。
- 锁定真实 Popup、受限最大尺寸及基于当前可用空间的上下定位；覆盖下方足够向下、下方不足向上、窗口内和不遮挡保存区域。
- 既有保存、Reschedule、取消不保存、确定回填、输入格式化与错误门禁继续通过；无 Schema、migration、项目或依赖变化。

## Terra 停止门禁

- 完成最小生产 diff与专项测试后提交并停止；不得 push、不得修改治理文档。
- 报告提交 SHA、改动文件、测试结果、未启动/操控 WPF、未访问生产数据库及已知 GUI 风险。

## Sol 独立验收门禁

- 完整审查本卡治理提交至实现 HEAD，确认只有 TimePicker 按钮热区、Popup 定位及对应测试变化，且不存在设置窗口临时高度逻辑。
- 独立运行 R8 专项、Settings/Reminder、I01～I04/UIUX、Release 全量与 Release build。
- Release build 必须 0 warning / 0 error；EF 无漂移；migration=9；项目、依赖、migration、ModelSnapshot 无变化；`git diff --check` 通过。
- 不启动/操控 WPF，不访问生产数据库。
- 通过后更新 `.ai-dev/ACCEPTANCE/V1-F03-I04.md` 与 `.ai-dev/HANDOFF/LATEST.md`，状态写为：
  `GUI_R8_TIMEPICKER_TECHNICALLY_ACCEPTED / GUI_ACCEPTANCE_FAILED / WAITING_USER_RETEST`

## 用户最终 GUI 门禁

- 用户只验两项：右侧为明显、好点的完整按钮热区；Popup 打开时窗口尺寸不变、保存始终可见且不被覆盖。
- 用户通过前，I04/F03 不得 CLOSED。
