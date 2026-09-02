# V1-F03-I04 GUI blocker repair R2

## 1. 授权与硬边界

- 日期：2026-09-02。
- 开工基线：`master@9387ce9c0aa6021d309145b7a9c7794f04bde598`，与 `origin/main` 同步，工作区开工前干净。
- 当前结论：第二轮用户真实 GUI 验收失败，`V1-F03-I04 GUI ACCEPTANCE = FAILED / BLOCKED`；I04/F03 不得关闭。
- 本卡只修复今日排查页面切换闪烁/假死感与任务表格不可用；禁止 Stage 8、Stage 9、I01～I03 业务重写、Schema、migration、依赖或其他功能。
- 本卡提交后，在当前 Sol 话题内创建全新 GPT-5.6 Terra（medium，平台标准速度）；Sol 不直接修改生产代码，不复用上一 Terra。

## 2. 第二轮现场与根因证据

- 用户录像显示：进入“今日排查”时左侧导航整体变灰、Loading/明显重绘；任务表格列拥挤、字段层级不清，无法用于门店快速判断。
- 当前隔离 runtime、marker、受保护原目录继续保留；不得执行结束/恢复脚本，不得修改现场数据库。
- 第二轮应用在 14:58 启动，应用日志只有 `startup_recalculation_completed`；Windows Application Event Log 没有第二轮新的 StoreExpiryInspector 崩溃。14:20 的旧 `HasValidDraft` TwoWay 异常属于第一轮且已修复。本卡仍须保持异常回归，但不得虚构第二次闪退。
- 隔离 SQLite：products=7007、batches=32402、open tasks=576、open task items=785、drafts=0、quick_check=ok、migration=9。
- `ShellViewModel` 只创建一个 `TodayInspectionViewModel`，不存在每次进入重新创建 ViewModel。
- 每次从其他页面重新进入 TodayInspection 都无条件 fire-and-forget 调用 `LoadAsync()`，即便已有有效列表。
- 查询在 `Task.Run` 内运行，不在 UI Dispatcher；`InspectionTaskQuery.QueryTaskList` 为三次 `AsNoTracking` 批量查询（task header、全部 task item、valid draft），不是 N+1。对当前隔离库等价三查询只读实测 5 次为 6.51～15.81 ms，中位数 7.76 ms。
- 查询完成后回到 UI 线程，当前实现 `Tasks.Clear()` 并逐个 `Tasks.Add()` 576 次，逐条触发集合变化、行绑定和布局；这是录像中渐进重绘/卡顿感的直接代码路径。
- `TodayInspection.IsBusy` 被纳入 Shell `CanNavigate`，加载开始即让所有导航命令失效，因此左侧导航整体变灰与代码完全对应。页面自己的命令禁用是正确的，但任务列表加载不应锁住整个 Shell。
- Today 页面外层为横向/纵向 ScrollViewer + StackPanel，任务 DataGrid 未显式冻结 virtualization 契约；列宽总和较大且唯一 `*` 列处于横向可滚动测量环境。现有列还缺少大类和清晰文字任务状态，包含非本页最低必要的条码/最近有效期/只读复选框，实际层级拥挤。

## 3. 唯一修复目标

### 加载与导航

- 首次进入异步加载全部合法任务，数据查询仍走现有 Application query；不得恢复 500 条截断。
- 把查询结果和行 ViewModel 在后台准备后，以单次列表替换/单次通知（或等价最小机制）交给 UI，禁止逐条 500+ ObservableCollection 重绘。
- 将“任务列表加载中”与会改变业务状态的导出/Preview/Draft/提交 busy 门禁区分；列表加载只禁用今日排查内容区相关操作，不得禁用整个 Shell 导航。
- Today ViewModel 继续由 Shell 单例持有；已有成功列表时离开再返回不得无意义全量重载。显式刷新、提交成功后刷新仍必须取得当前事实。
- Loading 仅出现在今日排查内容区；无整窗白屏、无左栏整体闪烁。

### 高密度任务表格

- 表格固定列语义：选择、商品编码、商品名称、大类、当前最高阶段、批次数、商品当前库存、任务状态。
- 大类只复用 `ProductCategoryScopes` canonical mapping；不得复制映射表。
- 商品名称单行合理截断并以 ToolTip/等价方式查看全文；数字列统一对齐；选择固定最左。
- 阶段必须使用已有 `StageBadgeTemplate` 或同一现有视觉权威，保持文字 + 视觉，不新增第二套颜色映射。
- 任务状态使用明确文字（例如待排查/已有草稿），不再用只读复选框让用户猜测。
- 显式启用行 virtualization/recycling、内容滚动；外层测量不得破坏 DataGrid 的可读列宽与纵向虚拟滚动。576 条及更大集合必须全部可访问。
- 不把批次明细塞入任务列表；Excel/Preview 保持批次级权威。

## 4. 范围约束

- 允许最小修改 `InspectionTaskListItem/QueryTaskList` 以携带 canonical 大类显示所需事实；不得改变 open task 资格、排序、分页或 I01 导出资格。
- 允许最小拆分 Today 的 loading/action busy 状态与一次性列表替换；不得引入新框架、事件总线、分页基础设施或全局 UI 重构。
- 不改 Excel 格式、I02 Preview/Draft 算法、I03 Submission、Reminder、商品源导入、Schema、ModelSnapshot、migration、依赖。
- 若必须改变产品交互或 Schema，立即停止并报告。

## 5. 最低自动化证据

- 500+（建议 576 或更大）open Task 首次加载全部保留，单次发布，不逐条 UI collection churn。
- 重复进入已有列表不重复查询；显式刷新与提交后刷新仍重新查询。
- 加载时 Shell 导航保持可用，Today 内容操作正确禁用；异常被记录/显示且不闪退。
- 全选、取消、部分选择及导出 TaskId 全量正确。
- 查询保持固定批量次数/无 N+1，至少以 EF command interceptor 或等价可执行证据锁定。
- XAML 静态契约覆盖八个表头、商品名截断/全文提示、数字对齐、StageBadge、任务状态文字、显式 virtualization/recycling、Loading 仅在 Today 区域。
- I01 导出、I02 Preview/Draft、I03 批量提交不回归。
- GUI blocker/I04、关键 WPF/Application/UIUX、V1-F01/F02、Release 全量、Release build、EF drift、migration=9、依赖/项目文件、`git diff --check`。

## 6. Terra 交付与最终停点

Terra 必须提交最小 production+test commit，返回 SHA、完整文件清单、调用/加载路径、精确测试计数与未验证项，不 push；不得修改为 GUI 通过或关闭 I04/F03。

Terra 停止后由 Sol 独立审查和执行全部技术门禁，更新 acceptance/handoff/project status，commit 并普通 push `master:main` 后停止。最终状态仍为 `GUI ACCEPTANCE FAILED / WAITING USER RETEST`。用户先重验：不闪退、无整页闪烁/假死、576 条可滚动、字段清晰、全选/取消/部分选择；通过后才继续原 Excel 闭环验收。
