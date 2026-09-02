# 最新交接

## 当前任务与状态

`V1-F03-I04｜WPF 双入口与端到端收口` 已由全新 GPT-5.6 Terra（medium）实施，并经 Sol 两轮退回返修与独立技术验收通过。

当前状态：`V1-F03-I04_TECHNICALLY_ACCEPTED / WAITING_USER_GUI_ACCEPTANCE`

本轮没有启动 WPF、访问生产数据库、修改 Schema/依赖或进入 Stage 8、Stage 9。I04/F03 尚未最终 CLOSED。

## 当前 Git

- 分支：`master`，相对 `origin/main` 保留 I04 本地提交，等待用户 GUI 通过后统一普通 push。
- I04 批准前 GitHub 基线：`50fbc80fdaa9817f53a900691e6b14c152fc32ae`。
- I04 开工治理：`6a4cf8a`；Terra 实现/返修：`a14eed7`、`d784f6f`、`39db873`。
- 当前验收文档提交以 `git rev-parse HEAD` 为准。

## I04 已交付

- 新增独立“今日排查”导航与页面；现有“数据导入”继续只负责商品源 Excel。
- 当前任务支持全选/取消/部分选择；标准 SaveFileDialog 调用 I01，不在 WPF 重算导出或覆盖文件。
- 标准 OpenFileDialog 调用 I02 零写入 Preview，显示 blank/0/正数、错误、陈旧/失效与原因；确认后只调用 I02 原子 Draft 应用。
- incomplete Task 明确保留 Draft 但不进入正式提交；完整 Task 只调用 I03 批量提交。
- 超库存集中显示商品/库存/排查合计，确认/stale 重试绑定同一当前事实与 UTC 提交意图。
- Submitted/AlreadySubmitted 等待刷新首页、今日任务、原任务、详情和历史；成功后清除旧会话，局部刷新失败不反转提交结果。
- 无 Schema、migration、依赖、Reminder、商品源导入算法、Revision、Stage 8/9 或全局 UI 重构。

## Sol 独立新鲜验收

- I04 专项：9/9。
- I04 + I01～I03 + 关键 WPF/Application/UIUX 回归：245/245。
- V1-F01/F02 + ProductTask/PostImport/Startup/Reminder：148/148。
- Release 全量：861/861；build 0 warning / 0 error。
- EF 无漂移；`--no-connect` migration 列表 9 条。
- 无 `.csproj`、`.slnx`、migration、ModelSnapshot 或依赖差异；`git diff --check` 通过。
- 在线 NuGet 漏洞源本轮 NU1900，不冒充在线审计成功。
- `StoreExpiryInspector` 进程 0；未启动 WPF、未访问生产数据库。

完整证据：`.ai-dev/ACCEPTANCE/V1-F03-I04.md`；冻结契约：`.ai-dev/TASKS/V1-F03-I04.md`；决策：`.ai-dev/DECISIONS.md` D-035。

## 下一唯一门禁

由用户本人使用隔离环境完成 I04 真实 WPF GUI 验收。GUI 全部通过后才允许最终更新 I04/F03 收口、commit 并普通 push `master:main`。

GUI 通过前不得：

- 将 I04 或 V1-F03 标记 CLOSED；
- 建立 V1-F03 总体最终收口记录或 push 当前验收状态；
- 进入 Stage 8、Stage 9 或其他功能；
- 修改 Schema、Reminder、商品源导入或 I01～I03 业务算法。
