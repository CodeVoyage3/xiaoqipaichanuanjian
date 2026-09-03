# 最新交接

## 当前任务与状态

`V1-F03-I04｜WPF 双入口与端到端收口`：`CLOSED`。

`V1-F03｜今日排查计划导出 + Excel 排查结果回导 + 确认提交`：`CLOSED`。

用户本人真实 WPF GUI：`PASSED`。当前已无 V1-F03 GUI blocker，V1-F03 已正式归档。

## 当前 Git

- 分支：`master`；V1-F03 最终归档按授权普通 push `master:main`。
- I04 批准前 GitHub 基线：`50fbc80fdaa9817f53a900691e6b14c152fc32ae`。
- I04 开工治理：`6a4cf8a`；原实现/返修：`a14eed7`、`d784f6f`、`39db873`；第一轮 GUI blocker：`c532583`、`e2f1b21`；R2 治理/修复：`ff85a27`、`2b1d021`、`62879a7`、`32c757d`、`b0e3f7c`；上一轮治理/返修：`4525eb3`、`a845737`、`c65fe5a`、`147eef4`；11 项治理/返修：`701bcc8`、`ed3c218`、`2366dde`；R3 治理/返修：`372fd4a`、`cd26879`、`55b0cf8`、`1da4667`；R4 治理/返修：`f4a585b`、`a988d14`、`bf9e896`、`a360aca`；R5 治理/实现/补正：`2ad9c64`、`8ffffea`、`b537387`；R6 治理/实现/补正：`067bcd0`、`f4fbf14`、`12c0954`、`b3b302b`、`c8f1e07`、`cf44e38`；R7 治理/实现/补正：`66b4980`、`1b5585f`、`3d15492`、`1102b8b`；R8 治理/实现/补正：`18c4428`、`8c0e825`、`91930e4`；R9 治理/实现/补正：`1bc41de`、`76ef405`、`75a4093`；R10 治理/实现：`cae5def`、`f4d4614`。
- 当前验收文档提交以 `git rev-parse HEAD` 为准。

## V1-F03 最终收口（2026-09-03）

- I01 今日排查查询/Excel 导出、I02 Result Reader/stale 校验/Draft Application、I03 Bulk Submission orchestration 均已技术验收。
- I04 WPF 双入口与端到端闭环的 Sol 技术结论为 `TECHNICALLY_ACCEPTED`；用户本人真实 WPF GUI 最终结论为 `PASSED`，两类证据分别记录。
- R10 Settings Footer、R9 时间输入与独立模态小窗、R6 StageBadge、R5 排查结果确认窗口及前序 R1～R8 已修复项均通过；当前无已知 GUI blocker。
- 最近正式技术基线：Release 891/891、Release build 0 warning / 0 error、EF 无漂移、migration=9、无 Schema/依赖/项目文件变化、`git diff --check` 通过。
- `V1-F03-I04` 与 `V1-F03` 均已 `CLOSED`；正式归档见 `.ai-dev/STAGES/V1-F03-CLOSEOUT.md`。

## I04 已交付

- 新增独立“今日排查”导航与页面；现有“数据导入”继续只负责商品源 Excel。
- 当前任务支持全选/取消/部分选择；标准 SaveFileDialog 调用 I01，不在 WPF 重算导出或覆盖文件。
- 标准 OpenFileDialog 调用 I02 零写入 Preview，显示 blank/0/正数、错误、陈旧/失效与原因；确认后只调用 I02 原子 Draft 应用。
- incomplete Task 明确保留 Draft 但不进入正式提交；完整 Task 只调用 I03 批量提交。
- 超库存集中显示商品/库存/排查合计，确认/stale 重试绑定同一当前事实与 UTC 提交意图。
- Submitted/AlreadySubmitted 等待刷新首页、今日任务、原任务、详情和历史；成功后清除旧会话，局部刷新失败不反转提交结果。
- 无 Schema、migration、依赖、Reminder、商品源导入算法、Revision、Stage 8/9 或全局 UI 重构。

## GUI R10 Settings Footer 紧凑收口与 Sol 独立新鲜验收（2026-09-03）

- 设置窗移除固定 300/280 高度，改为内容自适应高度；内容区与 Footer 均为 `Auto` 行，不再存在用于撑高的星号行。
- Footer 保持右对齐、“取消 / 保存”语义与样式不变；内容间距 22px，窗口底部安全边距 18px。
- R9 可编辑时间输入、88×32“选择时间”按钮、独立模态小窗及 Reminder/自启动业务无差异；相对 R10 治理基线只有 1 个 UI 文件和 1 个既有测试文件有差异。
- Sol 新鲜 R10/Settings/UIUX 27/27；Settings/Reminder 74/74；I01～I04/UIUX 122/122；Release 全量 891/891；离线 restore 后 Release build 0 warning / 0 error。
- EF 无漂移，migration=9；`git diff --check` 通过。未启动 WPF、未访问生产数据库，应用进程 0。用户后续真实 GUI 验收已 `PASSED`。

## GUI R9 独立时间选择小窗与 Sol 独立新鲜验收（2026-09-03）

- 设置页保留直接输入与既有格式/非法门禁；右侧为明确的 88×32“选择时间”次级按钮，不再使用时钟小图标热点。
- 按钮打开以提醒设置窗为 Owner 的独立紧凑模态 Window“选择提醒时间”；小时/分钟完整范围、滚轮、点击、键盘导航、当前定位、取消与确定均保留。
- 非法编辑文本不阻断打开小窗；取消不修改，确定只格式化回填待保存值，不创建 DbContext、不直接保存或 Reschedule。最终权威仍是设置页“保存”→既有 UseCase→既有成功事件。
- 旧 Popup、CustomPlacement、屏幕/DPI定位、保存避让和图标触发逻辑已删除；相对 R9 基线只有 1 个 UI 文件和 2 个既有测试文件有差异，无冻结业务、Schema、migration、依赖、项目或全局主题变化。
- Sol 新鲜 R9/Settings/UIUX 27/27；Settings/Reminder 73/73；I01～I04/UIUX 123/123；Release 全量 891/891；Release build 0 warning / 0 error。
- EF 无漂移，migration=9；`git diff --check` 通过。未启动 WPF、未访问生产数据库，应用进程 0。本节不替代用户真实 GUI 验收。

## GUI R8 TimePicker 阻断修复与 Sol 独立新鲜验收（2026-09-03）

- 左侧保持可编辑且只负责输入/光标；右侧 38×32 完整 Button 是唯一 Popup 入口，正常态有中性底色与左分隔线，整个根矩形可点，并具备 Hover/Press/Focus 与 Automation 名称。
- 已彻底删除 Popup 打开时把设置窗从 300 增至 420、关闭再恢复的逻辑；窗口尺寸始终不变，Popup 保持独立浮层。
- Popup 依据按钮、设置内容的实际屏幕矩形、实际尺寸及当前 DPI 上下定位，并以保存按钮顶部为底部边界；最大 180×208。R6 输入、滚轮、暂存/取消/确定、保存和 Reschedule 均未改。
- 相对 R8 治理基线只有 `MainWindow.xaml.cs` 和既有 Settings 测试文件有差异；无 Schema、migration、ModelSnapshot、依赖、项目文件、全局主题或冻结业务差异。
- Sol 新鲜 R8/Settings/UIUX 28/28；Settings/Reminder 74/74；I01～I04/UIUX 123/123；Release 全量最终 892/892；Release build 0 warning / 0 error。首次全量的 S7-T03 固定 5 秒超时已单项 1/1 与第二次全量 892/892 复跑通过。
- EF 无漂移，migration=9；`git diff --check` 通过。未启动 WPF、未访问生产数据库，应用进程 0。本节不替代用户真实 GUI 验收。

## GUI R7 TimePicker 最终增量返修与 Sol 独立新鲜验收（2026-09-03）

- Reminder TimePicker 保持可编辑；右侧按钮热区由 28 扩至 52，输入框非文字空白区域也可打开 Popup，文字/插入点区域继续支持键盘编辑。
- Popup 使用 WPF 自定义原生定位，按窗口、输入框、Popup 与保存按钮的实际布局优先向下、空间不足向上，且不越过保存区；最大尺寸 180×208，小时/分钟列与操作区保持紧凑。
- 设置窗关闭态仍为 300 高，只在 Popup 打开时临时扩至 420，所有关闭路径恢复 300。保存、Reschedule、R6 其他 UI 与业务契约未改。
- 相对 R7 治理基线只有 `MainWindow.xaml.cs` 和既有 Settings 测试文件有差异；无 Schema、migration、ModelSnapshot、依赖、项目文件、全局主题或冻结业务差异。
- Sol 新鲜 R7/Settings/UIUX 28/28；Settings/Reminder 74/74；I01～I04/UIUX 123/123；Release 全量 892/892；Release build 0 warning / 0 error。
- EF 无漂移，migration=9；`git diff --check` 通过。未启动 WPF、未访问生产数据库，应用进程 0。本节不替代用户真实 GUI 验收。

## GUI R6 TimePicker 与 Stage 视觉统一新鲜验收（2026-09-03）

- 提醒时间使用可直接输入的文本框和右侧中性时钟图标；`9:30` 等合法值统一为 `09:30`，非法值字段级报错、保留上一合法值，并在任何数据库访问前返回。
- 时钟图标打开输入框附近的轻量原生 Popup；小时/分钟窄列支持滚轮、点击和居中定位。取消、Esc、点击外部不应用，确定才回填；最终保存与运行中 Reschedule 继续走既有权威。
- TimePicker/Popup 使用白、灰与低饱和浅灰蓝，图标和非主操作保持中性；未修改全局主题。图标按钮及小时/分钟列具有自动化名称。
- Today 原 `StageBadgeTemplate` 配色原样共享给 Confirmation；同一 canonical Stage 的中文文案、前景、背景、边框、圆角和 Padding 均来自同一模板，未修改 Stage 算法。
- 相对 R6 治理基线只修改 5 个 UI/展示文件和 3 个测试文件；I01～I03、Reminder 核心、R5 摘要/高度、Schema、项目、依赖和 migration 均未改。
- Sol 新鲜专项 57/57；I01～I04/UIUX 123/123；Reminder 链 73/73；业务回归 368/368；Release 全量 891/891；Release build 0 warning / 0 error。
- EF 无漂移，migration=9；无 `.csproj`、`.slnx`、migration、ModelSnapshot、依赖或 `App.xaml` 差异，`git diff --check` 通过。WPF 未启动/操控，生产数据库未访问，应用进程 0。本节不替代用户真实 GUI 验收。

## GUI R5 收口优化与 Sol 独立新鲜验收（2026-09-03）

- 确认窗摘要改为“本次共 X 个商品 / X 个批次，X 条可提交”；零异常不显示，未填写/错误/陈旧失效仅按需突出。辅助文案弱化，六列表与既有 Preview/Stage/校验事实不变。
- 窗口初始高度随少量行实际收缩，表格达到 280px 后内部滚动；virtualization/recycling、异常行、Tooltip、焦点和 1024×600 边界保留。排查人、DatePicker 与“不晚于今天”同行收紧，原绑定和日期门禁不变。
- 设置页提醒时间改为只读入口和原生小时/分钟双列滚动面板；当前值居中定位，选择态清晰，取消回滚、确定更新显示。最终保存继续使用既有 `ReminderSettingsUseCase`，运行中重新调度与所有 Reminder 规则不变。
- 相对 R5 治理基线只修改 4 个 UI/ViewModel 文件和 3 个测试文件；I01～I03、Reminder 核心算法、Schema、项目、依赖与 migration 均未改。
- Sol 新鲜专项 51/51；I01～I04/UIUX 123/123；Reminder 链 67/67；业务回归 368/368；Release 全量 885/885；独立输出 Release build 0 warning / 0 error。
- EF 无漂移，migration=9；无 `.csproj`、`.slnx`、migration、ModelSnapshot 或依赖差异，`git diff --check` 通过。既有用户侧 WPF PID 36256 未被本轮启动、关闭或操控，生产数据库未访问。本节不替代用户真实 GUI 验收。

## GUI 重验 R4 增量返修与 Sol 独立新鲜验收（2026-09-03）

- 大类 ComboBox 的完整透明 ToggleButton 根区域统一接收点击，并保留原筛选/选择权威；Today 商品名称与同行其他字段真正垂直居中。
- 确认窗排查人/日期标签与对应控件居中，错误文案使用独立行；真正 WPF DatePicker 复用既有清晰 `CalendarIcon`，默认今天/禁止未来日期不变。
- 确认表新增中文“当前阶段”轻量 Badge，最终六列为条码、商品名称、当前阶段、生产日期、有效日期、本次排查数量；阶段只复用 Preview canonical 映射，未恢复校验状态。
- 相对 R4 基线仅 3 个 UI 生产文件和 1 个 I04 测试文件有差异；I01～I03、大类筛选业务、Schema、项目与依赖均未改。
- Sol 新鲜 R4/I04 29/29；I01～I04/WPF/UIUX 137/137；相关业务回归 401/401；Release 全量 883/883；Release build 0 warning / 0 error。
- EF 无漂移，migration=9；`git diff --check` 通过。未启动 WPF、未访问生产数据库，应用进程 0。当轮 GUI 结论为 FAILED；最终结论已由文首归档覆盖。

## GUI 重验 R3 增量返修与 Sol 独立新鲜验收（2026-09-03）

- Shell 品牌区在非折叠侧栏完整显示；Today StageBadge 双向居中，大类 ComboBox 与真正 WPF DatePicker 使用局部模板，确认表五列内容统一垂直居中。
- I01 门店可见阶段复用唯一 canonical Stage 中文映射；“总库存”表头由真实 exporter 与 I02 Reader 同步锁定。数据行不合并，同商品多批次继续逐行保留商品总库存。
- 自动化真实生成并重开 `.xlsx`，验证中文阶段、“总库存”、中文大类、AutoFilter、隐藏稳定身份，并使用同一文件完成 I02 Reader round-trip。
- I03 前新增基于 canonical `expired` 的过期正库存强化警告；空白/0/其他 Stage 不触发，返回检查不调用 I03，确认后才继续；既有超库存/stale/失效门禁未削弱。
- Sol 新鲜 R3/I01/I04 32/32；I01～I04/WPF/UIUX 组合 98/98；相关业务回归 440/440；Release 全量 883/883；离线 Release build 0 warning / 0 error。
- EF 无漂移，migration=9；无 Schema、项目、依赖差异，`git diff --check` 通过。在线漏洞源 NU1900，未冒充在线审计成功。
- 未启动 WPF、未访问生产数据库，应用进程 0。当轮 GUI 结论为 FAILED；最终结论已由文首归档覆盖。

## GUI 重验剩余问题返修与 Sol 独立新鲜验收（2026-09-03）

- Today 主表现在固定为选择、条码、商品名称、大类、当前最高阶段、总库存六列；完整网格使用既有浅灰分隔色，表头边框略深，虚拟滚动和全部合法任务保持不变。
- 新增基于当前合法任务中文大类的筛选；全选/取消仅作用于可见行，跨筛选选择按 TaskId 保留，导出只包含实际勾选任务。
- Excel 门店可见库存列与严格 reader 表头同步改为“总库存”，隐藏快照、格式版本、稳定身份与 I01/I02 业务规则不变。
- 导出默认文件名含秒；每次成功结果独立弹窗显示任务数、批次数和完整路径，打开操作绑定最新一次成功路径，连续 A/B 导出有自动化证据。
- 回导确认模态只保留五个业务列；异常继续通过顶部红色汇总、浅红行与 Tooltip 显示。日期改为原生 DatePicker；排查人/日期字段红态和回导/提交阻断弹窗使用门店业务语言。
- Submitted/AlreadySubmitted 后仍通过权威 reload 刷新 Today；completed Task 立即消失，只有填写结果但未正式提交的 Task 保留。
- Sol 新鲜专项 73/73，相关 ProductTask/生命周期/Reminder/导入/历史/UIUX 回归 449/449，Release 全量 877/877；build 0 warning / 0 error。
- EF 无漂移，migration=9；无 Schema、项目、依赖差异，`git diff --check` 通过。在线漏洞源 NU1900，未冒充审计成功。
- 未启动 WPF、未访问生产数据库，应用进程 0。当轮 GUI 结论为 FAILED；最终结论已由文首归档覆盖。

## GUI blocker repair 与 Sol 独立新鲜验收

- 根因修复：`HasValidDraft` 明确 `Mode=OneWay`；没有恢复 500 条截断，没有查询、业务、Schema、依赖或交互变化。
- I04 专项：10/10；I01～I04：98/98；关键 WPF/Application/UIUX 回归：246/246。
- Release 全量：862/862；离线 `NuGetAudit=false` restore 后 build 0 warning / 0 error。
- EF 无漂移；`--no-connect` migration 列表 9 条。
- 无 `.csproj`、`.slnx`、migration、ModelSnapshot 或依赖差异；`git diff --check` 通过。
- 在线 NuGet 漏洞源本轮 NU1900，不冒充在线审计成功。
- `StoreExpiryInspector` 进程 0；未启动 WPF、未访问或修改隔离/生产数据库；隔离 runtime、marker 和受保护原目录仍在。

## R2 新鲜技术验收

- 3 次批量查询、576 行单次发布、重复进入不重查、加载不禁用 Shell、全选/取消批量通知、单项选择与 Preview 可观察契约均有自动化覆盖。
- 八列表头、大类 canonical 映射、StageBadge、名称截断/全文提示、数字对齐、文字状态、纵向可达和 DataGrid virtualization/recycling 均有静态契约。
- 专项 33/33；I01～I04 103/103；关键 WPF/Application/UIUX 266/266；V1-F01/F02 相关 161/161；Release 868/868；build 0/0。
- EF 无漂移，migration=9，依赖/项目/Schema 无变化，`git diff --check` 通过；隔离现场完整且 WPF 进程 0。

## 本轮 GUI 验收返修与新鲜技术验收

- Today 主表仅保留选择、条码、商品名称、大类、当前最高阶段、商品当前库存、任务状态；完整横竖网格、显式表头边框、垂直居中、名称省略/Tooltip 与 StageBadge 均保持 UIUX-R03 体系。
- 移除 Today 外层纵向 ScrollViewer 和固定 240 高度，DataGrid 占用剩余空间并自行虚拟滚动；单元格焦点替代整行蓝色选择，CheckBox/TaskId 仍是唯一业务选择权威，500+ 不截断。
- 回导后打开独立“排查结果确认”窗口；仅显示条码、名称、生产日期、有效日期、数量、校验状态。日期为 `yyyy-MM-dd`，0 为可提交数量，长原因通过 Tooltip 展示。
- 删除本流程用户可见 Draft/Task/“集中正式提交”术语和单独保存按钮；“提交数据”内部先用 I02 应用 Draft，再经二次确认调用 I03。取消二次确认零 I03 调用。
- Excel 中文大类与标准 AutoFilter 保持；新增隐藏数据行并改变物理顺序的回归，I02 仍按隐藏稳定身份匹配。
- Sol 新鲜专项 110/110，ProductTask/生命周期/Reminder 158/158，Release 全量 871/871；Release build 0 warning / 0 error。
- EF 无漂移，migration=9；无 Schema、依赖、项目文件变化，`git diff --check` 通过；WPF 未启动、生产数据库未访问、应用进程 0。在线 NuGet 漏洞源 NU1900，未冒充在线审计成功。

完整证据：`.ai-dev/ACCEPTANCE/V1-F03-I04.md`；最终归档：`.ai-dev/STAGES/V1-F03-CLOSEOUT.md`；R1～R10 历史契约保留于 `.ai-dev/TASKS/V1-F03-I04*.md`；决策：`.ai-dev/DECISIONS.md` D-035～D-036。

## 后续门禁

V1-F03 已正式归档。后续任何 UI 小微调不得继续追加 R11/R12，必须在用户新的 Codex 话题中定义为新的工作项。

未经用户另行批准不得：

- 创建或开始 Stage 8 Task；
- 创建或开始 Stage 9 Task；
- 实施在线升级或新的 UI 微调；
- 修改 Schema、Reminder、商品源导入或 I01～I03 业务算法。
