# 最新交接

## 当前任务与状态

`V1-F03-I04｜WPF 双入口与端到端收口` 用户 GUI 验收返修合并已完成并通过 Sol 独立技术复验，等待用户重验。

当前状态：`V1-F03-I04_GUI_REPAIR_TECHNICALLY_ACCEPTED / GUI_ACCEPTANCE_FAILED / WAITING_USER_RETEST`

第一轮 `HasValidDraft` 未处理异常及第二轮加载/闪烁问题均已修复。本轮把 Today 主表收敛为条码优先七列完整网格，移除父级滚动边界与固定表高，回导后改用独立确认模态，并完成“提交数据”二次确认和门店文案收口。隔离会话继续保留，未执行结束/恢复脚本。I04/F03 尚未最终 CLOSED。

## 当前 Git

- 分支：`master`；GUI blocker 治理、修复与技术验收按授权普通 push `master:main`，用户 GUI 通过后才允许最终收口。
- I04 批准前 GitHub 基线：`50fbc80fdaa9817f53a900691e6b14c152fc32ae`。
- I04 开工治理：`6a4cf8a`；原实现/返修：`a14eed7`、`d784f6f`、`39db873`；第一轮 GUI blocker：`c532583`、`e2f1b21`；R2 治理/修复：`ff85a27`、`2b1d021`、`62879a7`、`32c757d`、`b0e3f7c`；本轮治理/返修：`4525eb3`、`a845737`、`c65fe5a`、`147eef4`。
- 当前验收文档提交以 `git rev-parse HEAD` 为准。

## I04 已交付

- 新增独立“今日排查”导航与页面；现有“数据导入”继续只负责商品源 Excel。
- 当前任务支持全选/取消/部分选择；标准 SaveFileDialog 调用 I01，不在 WPF 重算导出或覆盖文件。
- 标准 OpenFileDialog 调用 I02 零写入 Preview，显示 blank/0/正数、错误、陈旧/失效与原因；确认后只调用 I02 原子 Draft 应用。
- incomplete Task 明确保留 Draft 但不进入正式提交；完整 Task 只调用 I03 批量提交。
- 超库存集中显示商品/库存/排查合计，确认/stale 重试绑定同一当前事实与 UTC 提交意图。
- Submitted/AlreadySubmitted 等待刷新首页、今日任务、原任务、详情和历史；成功后清除旧会话，局部刷新失败不反转提交结果。
- 无 Schema、migration、依赖、Reminder、商品源导入算法、Revision、Stage 8/9 或全局 UI 重构。

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

完整证据：`.ai-dev/ACCEPTANCE/V1-F03-I04.md`；冻结契约：`.ai-dev/TASKS/V1-F03-I04.md`、`.ai-dev/TASKS/V1-F03-I04-GUI-BLOCKER.md`、`.ai-dev/TASKS/V1-F03-I04-GUI-BLOCKER-R2.md`、`.ai-dev/TASKS/V1-F03-I04-GUI-ACCEPTANCE-REPAIR.md`；决策：`.ai-dev/DECISIONS.md` D-035。

## 下一唯一门禁

由用户本人重开当前保留的隔离环境，先验证：进入 Today 不闪退且无闪烁/假死、500+ 全量滚动、七列完整网格、最后可见行可勾选、全选/取消/部分选择。随后验证 Excel 中文大类/筛选/行重排回导、独立确认模态、日期/0 语义及提交二次确认；GUI 全部通过后才允许最终更新 I04/F03 收口。

GUI 通过前不得：

- 将 I04 或 V1-F03 标记 CLOSED；
- 建立 V1-F03 总体最终收口记录；
- 进入 Stage 8、Stage 9 或其他功能；
- 修改 Schema、Reminder、商品源导入或 I01～I03 业务算法。
