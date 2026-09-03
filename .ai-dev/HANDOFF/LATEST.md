# 最新交接

## 当前任务与状态

`V1-F03-I04｜WPF 双入口与端到端收口` GUI 重验 R3 增量返修已完成并通过 Sol 独立技术复验，等待用户再次重验。

当前状态：`GUI_RETEST_R3_REPAIR_TECHNICALLY_ACCEPTED / GUI_ACCEPTANCE_FAILED / WAITING_USER_RETEST`

R3 只收敛用户最新确认的 9 项增量：Shell 品牌区、StageBadge/五列表格对齐、大类 ComboBox/DatePicker 视觉、真实 Excel 中文阶段与“总库存”契约，以及“过期批次仍有正库存”强化提交警告。此前六列表、筛选、500+、导出/回导/提交修复均仅防回归。I04/F03 尚未最终 CLOSED。

## 当前 Git

- 分支：`master`；GUI blocker 治理、修复与技术验收按授权普通 push `master:main`，用户 GUI 通过后才允许最终收口。
- I04 批准前 GitHub 基线：`50fbc80fdaa9817f53a900691e6b14c152fc32ae`。
- I04 开工治理：`6a4cf8a`；原实现/返修：`a14eed7`、`d784f6f`、`39db873`；第一轮 GUI blocker：`c532583`、`e2f1b21`；R2 治理/修复：`ff85a27`、`2b1d021`、`62879a7`、`32c757d`、`b0e3f7c`；上一轮治理/返修：`4525eb3`、`a845737`、`c65fe5a`、`147eef4`；11 项治理/返修：`701bcc8`、`ed3c218`、`2366dde`；R3 治理/返修：`372fd4a`、`cd26879`、`55b0cf8`、`1da4667`。
- 当前验收文档提交以 `git rev-parse HEAD` 为准。

## I04 已交付

- 新增独立“今日排查”导航与页面；现有“数据导入”继续只负责商品源 Excel。
- 当前任务支持全选/取消/部分选择；标准 SaveFileDialog 调用 I01，不在 WPF 重算导出或覆盖文件。
- 标准 OpenFileDialog 调用 I02 零写入 Preview，显示 blank/0/正数、错误、陈旧/失效与原因；确认后只调用 I02 原子 Draft 应用。
- incomplete Task 明确保留 Draft 但不进入正式提交；完整 Task 只调用 I03 批量提交。
- 超库存集中显示商品/库存/排查合计，确认/stale 重试绑定同一当前事实与 UTC 提交意图。
- Submitted/AlreadySubmitted 等待刷新首页、今日任务、原任务、详情和历史；成功后清除旧会话，局部刷新失败不反转提交结果。
- 无 Schema、migration、依赖、Reminder、商品源导入算法、Revision、Stage 8/9 或全局 UI 重构。

## GUI 重验 R3 增量返修与 Sol 独立新鲜验收（2026-09-03）

- Shell 品牌区在非折叠侧栏完整显示；Today StageBadge 双向居中，大类 ComboBox 与真正 WPF DatePicker 使用局部模板，确认表五列内容统一垂直居中。
- I01 门店可见阶段复用唯一 canonical Stage 中文映射；“总库存”表头由真实 exporter 与 I02 Reader 同步锁定。数据行不合并，同商品多批次继续逐行保留商品总库存。
- 自动化真实生成并重开 `.xlsx`，验证中文阶段、“总库存”、中文大类、AutoFilter、隐藏稳定身份，并使用同一文件完成 I02 Reader round-trip。
- I03 前新增基于 canonical `expired` 的过期正库存强化警告；空白/0/其他 Stage 不触发，返回检查不调用 I03，确认后才继续；既有超库存/stale/失效门禁未削弱。
- Sol 新鲜 R3/I01/I04 32/32；I01～I04/WPF/UIUX 组合 98/98；相关业务回归 440/440；Release 全量 883/883；离线 Release build 0 warning / 0 error。
- EF 无漂移，migration=9；无 Schema、项目、依赖差异，`git diff --check` 通过。在线漏洞源 NU1900，未冒充在线审计成功。
- 未启动 WPF、未访问生产数据库，应用进程 0。本节不替代用户真实 GUI 重验，当前 GUI 结论仍为 FAILED。

## GUI 重验剩余问题返修与 Sol 独立新鲜验收（2026-09-03）

- Today 主表现在固定为选择、条码、商品名称、大类、当前最高阶段、总库存六列；完整网格使用既有浅灰分隔色，表头边框略深，虚拟滚动和全部合法任务保持不变。
- 新增基于当前合法任务中文大类的筛选；全选/取消仅作用于可见行，跨筛选选择按 TaskId 保留，导出只包含实际勾选任务。
- Excel 门店可见库存列与严格 reader 表头同步改为“总库存”，隐藏快照、格式版本、稳定身份与 I01/I02 业务规则不变。
- 导出默认文件名含秒；每次成功结果独立弹窗显示任务数、批次数和完整路径，打开操作绑定最新一次成功路径，连续 A/B 导出有自动化证据。
- 回导确认模态只保留五个业务列；异常继续通过顶部红色汇总、浅红行与 Tooltip 显示。日期改为原生 DatePicker；排查人/日期字段红态和回导/提交阻断弹窗使用门店业务语言。
- Submitted/AlreadySubmitted 后仍通过权威 reload 刷新 Today；completed Task 立即消失，只有填写结果但未正式提交的 Task 保留。
- Sol 新鲜专项 73/73，相关 ProductTask/生命周期/Reminder/导入/历史/UIUX 回归 449/449，Release 全量 877/877；build 0 warning / 0 error。
- EF 无漂移，migration=9；无 Schema、项目、依赖差异，`git diff --check` 通过。在线漏洞源 NU1900，未冒充审计成功。
- 未启动 WPF、未访问生产数据库，应用进程 0。本节不替代用户真实 GUI 重验，当前 GUI 结论仍为 FAILED。

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

完整证据：`.ai-dev/ACCEPTANCE/V1-F03-I04.md`；当前冻结契约：`.ai-dev/TASKS/V1-F03-I04-GUI-RETEST-R3-REPAIR.md`；前序契约继续保留于 `.ai-dev/TASKS/V1-F03-I04*.md`；决策：`.ai-dev/DECISIONS.md` D-035。

## 下一唯一门禁

由用户本人重开保留的隔离环境，只重验 R3 增量：左上品牌完整对齐；Today StageBadge 居中；大类下拉视觉且筛选逻辑不回归；新导出 Excel 使用中文阶段与“总库存”且无合并单元格；确认表五列垂直居中；DatePicker 视觉/日历/默认今天/禁止未来日期；过期正库存警告的“返回检查”与“确认无误，继续提交”。GUI 全部通过后才允许讨论 I04/F03 最终收口。

GUI 通过前不得：

- 将 I04 或 V1-F03 标记 CLOSED；
- 建立 V1-F03 总体最终收口记录；
- 进入 Stage 8、Stage 9 或其他功能；
- 修改 Schema、Reminder、商品源导入或 I01～I03 业务算法。
