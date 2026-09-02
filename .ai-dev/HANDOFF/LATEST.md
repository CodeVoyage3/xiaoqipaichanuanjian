# 最新交接

## 当前任务与状态

`V1-F03-I04｜WPF 双入口与端到端收口` 第二轮 GUI blocker R2 已完成最小修复并通过 Sol 独立技术复验，等待用户重验。

当前状态：`V1-F03-I04_R2_TECHNICALLY_REPAIRED / GUI_ACCEPTANCE_FAILED / WAITING_USER_RETEST`

第一轮 `HasValidDraft` 未处理异常已修复，第二轮没有新崩溃事件。R2 已把 576 行改为后台构建/单次发布，分离列表 loading 与 Shell action busy，避免重复进入重查，并交付八列高密度虚拟化表格。隔离会话继续保留，未执行结束/恢复脚本。I04/F03 尚未最终 CLOSED。

## 当前 Git

- 分支：`master`；GUI blocker 治理、修复与技术验收按授权普通 push `master:main`，用户 GUI 通过后才允许最终收口。
- I04 批准前 GitHub 基线：`50fbc80fdaa9817f53a900691e6b14c152fc32ae`。
- I04 开工治理：`6a4cf8a`；原实现/返修：`a14eed7`、`d784f6f`、`39db873`；第一轮 GUI blocker：`c532583`、`e2f1b21`；R2 治理/修复：`ff85a27`、`2b1d021`、`62879a7`、`32c757d`、`b0e3f7c`。
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

完整证据：`.ai-dev/ACCEPTANCE/V1-F03-I04.md`；冻结契约：`.ai-dev/TASKS/V1-F03-I04.md`、`.ai-dev/TASKS/V1-F03-I04-GUI-BLOCKER.md`、`.ai-dev/TASKS/V1-F03-I04-GUI-BLOCKER-R2.md`；决策：`.ai-dev/DECISIONS.md` D-035。

## 下一唯一门禁

由用户本人重开当前保留的隔离环境，先验证：进入 Today 不闪退、无整页闪烁/假死、576 条正常加载滚动、八列清晰可读、全选/取消/部分选择正常。五项通过后再继续 Excel 闭环验收；GUI 全部通过后才允许最终更新 I04/F03 收口。

GUI 通过前不得：

- 将 I04 或 V1-F03 标记 CLOSED；
- 建立 V1-F03 总体最终收口记录；
- 进入 Stage 8、Stage 9 或其他功能；
- 修改 Schema、Reminder、商品源导入或 I01～I03 业务算法。
