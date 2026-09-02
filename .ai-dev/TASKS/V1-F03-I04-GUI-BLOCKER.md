# V1-F03-I04 GUI blocker repair

## 1. 授权与状态

- 日期：2026-09-02。
- 基线：`master@3d5ac69a71789bbbaa363714b8f14b0512bfd60b`，与 `origin/main` 同步，工作区开工前干净。
- 当前结论：`V1-F03-I04 GUI ACCEPTANCE = FAILED / BLOCKED`；I04 与 V1-F03 不得关闭。
- 本卡只修复“导入真实数据后打开今日排查白屏、转圈并闪退”。禁止 Stage 8、Stage 9、I01～I03 业务重写、Schema、migration、依赖或其他功能。
- 本卡提交后，由当前 Sol 话题内全新 GPT-5.6 Terra（medium，平台标准速度）实施；Sol 不直接修改生产代码。

## 2. 已取得的现场证据

- 隔离验收会话仍保留；运行目录、marker、manifest、受保护原目录均存在，应用进程为 0，未执行结束/恢复脚本。
- Windows `.NET Runtime` 事件 1026（2026-09-02 14:20:15）记录未处理异常：
  `System.InvalidOperationException: 无法对 TodayInspectionTaskViewModel 类型的只读属性 HasValidDraft 进行 TwoWay 或 OneWayToSource 绑定。`
- 堆栈从 `PropertyPathWorker.CheckReadOnly` 经 `BindingExpression.Activate`、`DataBindEngine.OnLayoutUpdated`、WPF render/Dispatcher 到 `App.Main`；随后 Application Error 1000 与 WER 1001 确认进程终止。
- 直接触发点：`MainWindow.xaml` 的 `DataGridCheckBoxColumn` 对只读 `HasValidDraft` 使用默认绑定模式；`IsReadOnly="True"` 只限制列编辑，不把绑定改为 OneWay。
- 应用日志只有启动补算成功记录，没有第二个应用级异常；`App` 未注册 Dispatcher 未处理异常日志，但本卡不扩建全局异常体系，除非修复验证证明仍有同类崩溃且最小必要。
- 隔离 SQLite 只读 `quick_check=ok`：products=7007、batches=32402、tasks/open_tasks=576、task_items=785、open_task_items=785、drafts=0、migration=9。
- 当前数据确实超过旧 500 上限，但崩溃证据是首个只读属性绑定激活异常，不是查询截断取消、N+1、内存暴涨或 UI 线程数据加载超时。不得恢复 500 条业务截断。

## 3. 唯一修复目标

- 使用 WPF 现有绑定能力，在权威 XAML 绑定处做最小根因修复，使只读展示属性明确为 OneWay。
- 审查“今日排查”同页及相关 DataGrid 的只读属性绑定，只有存在同一确定缺陷模式时才一并修复；不得泛化重构。
- 保持全部 576 个及更多合法 open Task 可访问；全选、部分选择、导出、I02 Preview/Draft、I03 批量提交语义不变。
- 不改变 `InspectionTaskQuery`、I01～I03 Application 业务算法、Excel 格式、Schema、migration、依赖或产品交互。
- 若修复证明必须分页、改变交互或 Schema，立即停止并回报，不得自行扩展。

## 4. 最低自动化证据

- 新增能在修复前失败的静态 XAML/绑定契约测试：`HasValidDraft` 只读绑定必须明确 `Mode=OneWay`。
- 大量 open Task（至少超过 500）加载不异常且结果不截断；全选与部分选择保持正确。
- I04 专项；I01 导出；I02 Preview/Draft；I03 批量提交回归。
- Release 全量、Release build、EF pending model changes、migration=9、依赖与 `.csproj/.slnx`、`git diff --check`。
- 不以自动化替代用户重新执行真实 WPF GUI 验收。

## 5. Terra 交付与停点

Terra 必须提交一个最小生产修复与对应测试 commit，返回 SHA、完整 diff、测试命令/计数和未验证项，不 push。不得更新为 GUI 通过，不关闭 I04/F03，不进入 Stage 8/9。

Terra 停止后由 Sol 独立审查和执行全部技术门禁。技术通过后更新 I04 acceptance/handoff，commit、普通 push `master:main` 并停止；状态仍为 `GUI ACCEPTANCE FAILED / WAITING USER RETEST`，只有用户重新完成真实 GUI 验收后才能决定关闭。
