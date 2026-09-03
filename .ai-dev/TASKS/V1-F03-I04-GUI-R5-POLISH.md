# V1-F03-I04｜GUI R5 收口优化 + 设置页提醒时间滚动选择器

## 状态与基线

- 批准日期：2026-09-03。
- GitHub `main` 唯一开工基线：`b626bc1572575c1e9fcf43d272f507d39190465f`。
- 当前主线：`V1-F03-I04｜WPF 双入口与端到端收口`。
- 本卡只授权确认窗口收口与设置页提醒时间输入交互替换；不得自动关闭 I04/F03。

## 协作治理

- Sol 只负责治理、完整 diff 审查、自动化测试和技术验收，不实施生产代码。
- 生产代码由本 Sol 话题内全新 GPT-5.6 Terra（reasoning medium）实施；不得复用旧 Terra或新建独立 Codex 话题。
- Terra 完成后停止，不更新 Acceptance/Handoff、不 push；Sol 独立复验后负责治理记录与普通 push。
- 用户本人负责真实 WPF GUI 验收；自动化和构建不得写成 GUI PASSED。

## 范围 A：排查结果确认窗口收紧

1. 正常摘要改为“本次共 X 个商品 / X 个批次，X 条可提交”，数字只来自现有 Preview 事实，不重算业务。
2. 未填写、错误、陈旧/失效只在对应数量大于 0 时显示，并比正常辅助信息更明显；不恢复常驻“校验状态”列。
3. “预览完成，请填写排查人和日期后提交数据。”保留并弱化，与摘要紧凑排列。
4. 表格少行时随内容合理收缩，多行达到合理最大高度后内部纵向滚动；保留 virtualization/recycling、异常行、Tooltip、焦点与 1024×600 可用性。
5. 六列继续固定为“条码｜商品名称｜当前阶段｜生产日期｜有效日期｜本次排查数量”；阶段继续使用 Preview canonical Stage 与既有唯一中文映射。
6. 底部收紧为同一主行“排查人 [TextBox]　排查日期 [DatePicker]　不晚于今天”；错误提示可在下方对应字段显示。保留原字段绑定、必填红态、真正 WPF DatePicker、默认今天、禁止未来日期、CheckDate 门禁和现有日历图标。
7. 取消/提交数据保持右下对齐与既有命令、二次确认、过期正库存强化确认及 I02/I03 流程。

## 范围 B：设置页提醒时间滚动选择器

1. 现有提醒时间显示改为只读点击入口；不再允许自由文本输入。
2. 点击后打开紧凑浅色选择面板，左右两列为小时 `00..23`、分钟 `00..59`；支持鼠标滚轮、点击选择、滚动定位和清晰选中态。
3. 打开时定位当前保存值；取消不修改，确定后稳定格式化为 `HH:mm` 并进入现有保存流程，页面立即显示新值，再次打开仍定位该值。
4. 继续只使用既有 `Settings.ReminderMinuteOfDay` 与 `ReminderSettingsUseCase`；保存后继续触发既有运行中 scheduler 重新调度。
5. 不接受空值、任意字符串、`24:00` 或 `10:60`；不得新增第三方时间控件、NuGet 包或项目级 UI 框架。

## 冻结边界

- Today 六列、筛选与跨筛选 TaskId 选择、全选/取消、500+ 加载与虚拟化、I01 导出、I02 Reader/Draft、I03 批量提交、数量/超库存/过期警告、ProductTask 生命周期和 Submitted 权威刷新均冻结。
- Reminder 默认时间、每日一次、同日幂等、启动补提醒、scheduler、运行中重新调度、托盘、自启动、Application service 与数据表均冻结。
- 商品源 Excel 导入、Schema、migration、ModelSnapshot、`.csproj`、`.slnx` 和依赖均不得改变；migration 必须仍为 9。
- 不进入 Stage 8/9，不实施在线升级，不自动关闭 I04/F03。

## 自动化契约

- 确认窗口：正常摘要省略零异常；异常只在大于 0 时出现；六列顺序不变且无“校验状态”；字段与命令绑定不变；少行紧凑、多行限高滚动且虚拟化保留。
- 时间选择器：读取既有值；覆盖小时 00..23 和分钟 00..59；确定结果为 `HH:mm`；重开定位当前值；不依赖自由文本解析；既有保存、重新调度及 Reminder 回归通过。
- 若无法完整自动点击，至少提供 ViewModel/状态转换单测与 XAML 静态契约。

## Terra 停止门禁

- 完成最小生产 diff 与专项测试后提交并停止；不得 push、不得修改本卡、Acceptance 或 Handoff。
- 明确报告改动文件、提交 SHA、测试结果、未运行 WPF、未访问生产数据库及已知风险。

## Sol 独立验收门禁

- 完整审查本卡基线至实现 HEAD 的 diff，确认只落在批准的 UI/ViewModel/测试范围，I01～I03 与 Reminder 核心算法无改动。
- 独立运行 R5 专项、I01～I04/WPF/UIUX、Stage 6 Reminder/Settings、ProductTask/生命周期/历史/商品导入、Release 全量和 Release build。
- Release build 必须 0 warning / 0 error；EF 无模型漂移；migration=9；项目、依赖、migration、ModelSnapshot 无变化；`git diff --check` 通过。
- 不启动 WPF，不访问生产数据库。
- 通过后更新 `.ai-dev/ACCEPTANCE/V1-F03-I04.md` 与 `.ai-dev/HANDOFF/LATEST.md`，状态写为：
  `GUI_R5_POLISH_TECHNICALLY_ACCEPTED / GUI_ACCEPTANCE_FAILED / WAITING_USER_RETEST`

## 用户真实 GUI 门禁

- 用户只需重验：简化摘要与按需异常、少行收缩/多行滚动、底部紧凑布局，以及设置页时间滚动选择、保存、重开定位和 Reminder 无明显回归。
- 用户通过前，I04/F03 不得 CLOSED。
