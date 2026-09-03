# V1-F03-I04｜用户 GUI 重验剩余问题返修

## 1. 授权、基线与硬边界

- 日期：2026-09-03。
- 开工基线：`master@d50b8c8712b6ad353e1b743eaa645b1496845353`，与 `origin/main` 同步，工作区开工前干净。
- 当前状态：`V1-F03-I04_GUI_REPAIR_TECHNICALLY_ACCEPTED / GUI_ACCEPTANCE_FAILED / WAITING_USER_RETEST`；I04 与 V1-F03 不得关闭。
- 当前技术基线：Release 871/871、build 0 warning / 0 error、EF 无漂移、migration=9。
- 本卡只处理用户最新重验确认的 11 项 GUI/交互问题；此前条码优先、最后可见行命中、500+ 不截断、闪烁/闪退、独立 Modal、日期 serial、0 语义、二次确认、中文大类/AutoFilter、隐藏/重排稳定身份等修复只做防回归。
- 本卡提交后必须在当前 Sol 话题内创建全新 GPT-5.6 Terra（medium、平台标准速度）；Sol 不直接修改生产代码，不复用此前 Terra。
- 禁止 Stage 8/9、Schema、migration、新依赖、Reminder、商品源导入、ProductTask/Inspection/Revision/0 件/超库存规则，以及 I01～I03 核心算法改动。

## 2. 浅灰完整网格与最终主表列

- 保留完整横竖网格，只弱化视觉：数据网格统一使用现有浅灰 `TableDividerBrush`，表头可使用现有略深 `BorderBrush`；不得增加高对比深色线或第二套颜色表。
- 今日主表与“排查结果确认”表都显式冻结横线、竖线和表头边框用色；不依赖系统主题默认值。
- 今日主表最终可见列且顺序唯一为：选择、条码、商品名称、大类、当前最高阶段、总库存。
- 删除可见“任务状态”；不得重新加入商品编码、批次数。底层 ProductCode、Draft 与 Task 状态不变。
- “商品当前库存”在本流程 WPF 与新导出的今日排查 Excel 中统一显示为“总库存”；底层仍使用现有 `EffectiveStockQty` 快照与校验。
- 为维持同一 `inspection_plan_v1` 文件可回导，允许且仅允许同步 I01 导出表头与 I02 Reader 期望表头中的该显示文字；禁止改变列位、格式版本、隐藏身份、快照、解析、陈旧校验或 Draft 语义。

## 3. 今日页大类筛选

- 使用现有 Today 已加载的全部合法任务和 `CategoryName`，在页面顶部增加原生 WPF 大类下拉框，默认“全部”。
- 选项从当前全部任务的 canonical 中文大类去重稳定生成，并包含“全部”；不得复制或硬编码第二套 Category 映射。
- 选择大类后仅改变主表视图，不改变完整 `Tasks` 集合、不重新查询、不改变 TaskId、排序或 500+ 全量语义。
- 全选/取消全选只作用于当前筛选结果；单项选择继续绑定原 TaskId。切换筛选保留各 Task 的真实 `IsSelected`，不得串项。
- 导出始终只使用全部任务中当前真正勾选的 TaskId；不同筛选下保留的勾选均按真实选择导出。

## 4. 提交后列表语义

- 未正式提交的任务即使已有内部 Draft，仍必须留在 Today 全量/筛选视图。
- I03 正式提交成功后继续使用现有刷新编排重读当前 open Task；已完成 Task 必须立即从 Today 的完整集合和当前筛选视图消失，并进入既有排查历史。
- 不在 UI 手动删除、completed 或伪造历史；新增测试以可变 loader 证明刷新依赖权威查询结果。

## 5. 导出成功、最新结果与默认文件名

- SaveFileDialog 默认名改为 `今日排查计划_yyyyMMdd_HHmmss.xlsx`，使用本次打开对话框时的当前本地时间；不得改变文件内容、身份或覆盖契约。
- `ExportAsync` 每次成功都必须以 I01 返回的 `OutputPath/TaskCount/RowCount` 更新最新导出结果；失败不得把旧结果伪装成本次成功。
- 导出成功后显示显式模态，至少包含：导出成功、商品/任务数量、批次数、完整路径；提供“打开文件”“打开所在文件夹”“确定”。
- 打开动作使用 Windows 原生 shell；文件按钮打开当前最新成功路径，文件夹按钮选择/定位该最新文件。第二次不同选择导出 B 后，不得再引用第一次 A 的路径或内容。
- 文件/文件夹打开失败显示可理解错误，不改变导出成功事实；不得新增持久化 ExportRecord。

## 6. 回导确认表与异常表达

- 正常明细表最终只显示：条码、商品名称、生产日期、有效日期、本次排查数量；删除常驻“校验状态”列。
- I02 错误、陈旧、失效和不可应用事实全部保留。异常行使用浅红背景或等价非颜色单一表达，并通过行 Tooltip/异常详情区显示短状态和完整原因。
- Modal 顶部增加明显错误汇总/恢复指引；正常文件不得被常驻“可提交”列占用宽度。
- 不修改 I02 Result Reader、陈旧校验、身份或 Draft Application；只使用现有 Preview 的 `StatusText/Reason` 做展示。

## 7. DatePicker 与强错误反馈

- 排查日期改用原生 WPF `DatePicker`，默认业务今天，可由日历选择；`DisplayDateEnd`/校验共同禁止未来日期。
- ViewModel 可以增加最小 `DateTime?` UI 适配属性，但最终仍转换为既有 `DateOnly CheckDate` 并使用现有 BusinessDate 门禁；不得改变 I02/I03 请求含义。
- 排查人空、日期空/未来分别显示字段级红色状态和门店文案：`请输入排查人`、`排查日期不能晚于今天`（空日期可使用同类明确提示）。错误不能只靠颜色。
- 用户点击“提交数据”但被排查人、日期、未完成、陈旧/失效、回导失败或正式提交失败阻断时，必须显示标题为“暂时无法提交”或语义等价的明显对话框，并给出原因与恢复动作。
- 异步异常继续写现有日志；用户文案不得暴露 Task、Draft、异常类型或堆栈。

## 8. 最低自动化证据

- 主表六列精确顺序、无任务状态/商品编码/批次数、库存表头为“总库存”；Excel 同列显示“总库存”且 I02 可读取。
- 两表数据线为浅灰、表头略深或等价轻量契约，完整横竖网格保留。
- 大类选项来自实际任务；全部/单类/切换、筛选后全选/取消/部分选择、跨筛选选择保留、TaskId 不串项、500+ 不截断、导出 TaskId 正确。
- 未提交 Draft 任务仍在；提交成功刷新后 completed Task 从完整集合与当前筛选结果消失，历史刷新编排不回归。
- 连续导出 A/B 后最新结果、成功模态和文件/文件夹打开都引用 B；默认名含 `yyyyMMdd_HHmmss`；失败不伪成功。
- Preview 正常表仅五列且无校验状态；异常汇总、异常行非颜色单一表达、Tooltip/原因仍可见。
- DatePicker 默认今天、日历可用、未来日期受限；排查人/日期字段级错误与提交级阻断对话框均有契约。
- 此前 I04 GUI 修复、I01 导出、I02 Preview/Draft、I03 Bulk Submit、商品源数据导入及页面加载回归全部通过。

## 9. Terra 交付与停点

- Terra 只提交本卡最小 production+test commit，报告根因、文件清单、测试计数和未验证项，不 push，不把 GUI 标记通过。
- Terra 停止后由 Sol 独立审查完整 diff，执行本轮专项、I04、I01～I03、关键 WPF/Application/UIUX、ProductTask/生命周期/Reminder、Release 全量、Release build、EF drift、migration=9、依赖/项目文件及 `git diff --check`。
- Sol 更新 `.ai-dev/ACCEPTANCE/V1-F03-I04.md` 与 `.ai-dev/HANDOFF/LATEST.md`，commit 并普通 push `master:main` 后停止。
- 最终仍为 `GUI_ACCEPTANCE_FAILED / WAITING_USER_RETEST`；等待用户重验这 11 项，不关闭 I04/F03，不进入 Stage 8/9。
