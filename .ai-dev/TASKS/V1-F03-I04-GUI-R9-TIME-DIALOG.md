# V1-F03-I04｜GUI R9 Reminder TimePicker 方案替换：直接输入 + 独立时间选择小窗

## 状态与基线

- 批准日期：2026-09-03。
- GitHub `main` 唯一开工基线：`c5651519f054db34a313979971f1ad14dbdb5dc7`。
- 当前主线：`V1-F03-I04｜WPF 双入口与端到端收口`。
- 用户真实 WPF GUI 确认 R8 失败；本卡废弃自定义 Popup，只允许替换为可编辑输入框、明确文本按钮和独立模态时间选择小窗。

## 协作治理

- Sol 只负责治理、完整 diff 审查、自动化测试和技术验收，不实施生产代码。
- 生产代码由本 Sol 话题内全新 GPT-5.6 Terra（reasoning medium）实施；不得复用旧 Terra或新建独立 Codex 话题。
- Terra 完成后提交并停止，不更新本卡、Acceptance/Handoff、不 push；Sol 独立复验后负责治理记录与普通 push。
- 用户本人负责真实 WPF GUI 验收；技术结果不得写成 GUI PASSED。

## R9-01：设置页固定交互

- 左侧继续是可编辑时间 TextBox，保留 `9:30`、`09:30`、`14:33`、`23:59` 输入、`HH:mm` 格式化、字段级非法提示、上一合法值及 DB 前置阻断。
- 右侧使用普通次级 Button，明确显示“选择时间”，高度与输入框相同或接近；不得只显示时钟图标或保留小热点。
- 点击“选择时间”打开独立模态 WPF Window/Dialog；设置页尺寸、布局和“保存”按钮不得因小窗打开而改变或被遮挡。

## R9-02：独立“选择提醒时间”小窗

- 标题为“选择提醒时间”，结构紧凑，与现有 WPF 黑/灰/白和低饱和视觉一致；不引入新视觉体系、复杂阴影或第三方控件。
- 小时范围 `00..23`、分钟范围 `00..59`；打开时定位当前设置页合法时间，支持点击、滚轮，并在成本合理时保留键盘上下键。
- 取消/关闭/Esc 不修改设置页时间、不保存、不 Reschedule；确定才以 `HH:mm` 回填设置页并只更新待保存状态。
- 小窗确定不得写数据库或直接 Reschedule；最终持久化继续只能由设置页“保存”调用既有 `ReminderSettingsUseCase`，再由既有成功事件触发 scheduler `Reschedule`。
- 保留清晰选中态、明确 Automation 名称、合理焦点顺序和可取消路径。

## 旧 Popup 退役

- 移除设置页 Reminder TimePicker 的 `Popup`、`CustomPopupPlacementCallback`、上下定位/屏幕/DPI计算、保存按钮避让、动态尺寸和旧图标触发区。
- 移除只服务旧 Popup 的临时布局、状态和测试，不得继续补丁式保留隐藏 Popup 路径。

## 冻结边界

- Reminder 输入格式、非法值门禁、保存/Reschedule、同日幂等、启动补提醒、托盘、自启动全部冻结。
- R5 Confirmation 摘要/高度、R6 StageBadge/蓝色降权、Today、大类筛选、I01～I03、数量语义、过期正库存警告、ProductTask、Excel 均不得修改。
- Settings 数据结构、Schema、migration、ModelSnapshot、`.csproj`、`.slnx`、NuGet、第三方 UI 框架、全局 Theme 均不得修改；migration 必须仍为 9。
- 不进入 Stage 8/9，不实施在线升级，不自动关闭 I04/F03。

## 自动化与静态契约

- 锁定设置页 TextBox 可编辑、合法格式化、非法字段错误及非法值不保存/不 Reschedule。
- 锁定“选择时间”文本按钮存在并打开独立 Window/Dialog，不再仅依赖图标或旧 Popup。
- 锁定小窗打开定位当前值、小时/分钟完整范围、取消不修改、确定 `HH:mm` 回填，且确定本身不访问数据库或调度器。
- 锁定 Settings 页面不再含 Reminder TimePicker Popup、旧定位/避让/DPI计算和旧图标热点代码。
- 既有 Settings 保存与 Reminder Reschedule 回归继续通过；无 Schema、migration、项目或依赖变化。

## Terra 停止门禁

- 完成最小生产 diff与专项测试后提交并停止；不得 push、不得修改治理文档。
- 报告提交 SHA、改动文件、测试结果、未启动/操控 WPF、未访问生产数据库及已知 GUI 风险。

## Sol 独立验收门禁

- 完整审查本卡治理提交至实现 HEAD，确认旧 Popup 真正退役、新增独立时间选择 Window/Dialog，Reminder 核心和 Settings 保存权威无 diff。
- 独立运行 R9 专项、Settings/Reminder、I01～I04/UIUX、Release 全量与 Release build。
- Release build 必须 0 warning / 0 error；EF 无漂移；migration=9；项目、依赖、migration、ModelSnapshot 无变化；`git diff --check` 通过。
- 不启动/操控 WPF，不访问生产数据库。
- 通过后更新 `.ai-dev/ACCEPTANCE/V1-F03-I04.md` 与 `.ai-dev/HANDOFF/LATEST.md`，状态写为：
  `GUI_R9_TIME_DIALOG_TECHNICALLY_ACCEPTED / GUI_ACCEPTANCE_FAILED / WAITING_USER_RETEST`

## 用户最终 GUI 门禁

- 用户只验三点：设置页可直接输入且有明确“选择时间”；按钮打开独立紧凑小窗且无 Popup 遮挡；小窗确定回填后设置页保存正常。
- 用户通过前，I04/F03 不得 CLOSED。
