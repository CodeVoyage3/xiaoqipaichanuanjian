# Stage14｜新版 v1.0.5 简化在线更新

日期：2026-09-08（Asia/Shanghai）

Stage14 = `IN_PROGRESS / GOVERNANCE_FROZEN / IMPLEMENTATION_NOT_AUTHORIZED`

S14-T01 = `GOVERNANCE_FROZEN / READY_FOR_IMPLEMENTATION_AUTHORIZATION / NOT_IMPLEMENTED`

目标版本：新版 `v1.0.5`。唯一产品基线为正式 `v1.0.4`；Stage13 的 v1.0.5/v1.0.6 强制升级实现已废弃，只保留历史审计证据。

## fresh 现场

- 已执行 `git fetch --prune origin`。
- `main HEAD == origin/main == 747fb69e316bbf3e428e6985edacec3cc8d8c65b`，ahead/behind `0/0`。
- GitHub 官方 API fresh 返回 latest stable `v1.0.4`，Release ID `383891004`，`draft=false`、`prerelease=false`；远端仅保留 `v1.0.4` tag，解引用到产品 source `c2b3f699408f422b3aeaf5b96321a6df08a038c5`，远端无 v1.0.5/v1.0.6 tags。
- `747fb69` 的非 `.ai-dev/**` 提交树与 `v1.0.4` 无差异。
- 主工作树已有用户文件：`tests/StoreExpiryInspector.Tests/Stage4ViewModelTests.cs` 已修改；两个操作手册 HTML、`tools/Reset-StoreExpiryInspectorTestHost.cmd`、`tools/Reset-StoreExpiryInspectorTestHost.ps1` 未跟踪。本轮均未触碰、恢复、删除、格式化、add 或 commit。

## 唯一交付

S14-T01 从 v1.0.4 重新实现非强制在线更新：主界面和托盘优先可用，启动后后台异步检查；发现新版只提供“立即更新 / 稍后提醒”，稍后提醒经一次明确告知后继续正常使用；网络失败不影响业务；托盘与每日提醒互不拖垮；安装器允许注册表 `DisplayVersion` 与实际 EXE 版本不一致；发布前只做技术候选验收，真实 v1.0.4 -> 新 v1.0.5 在线升级验收后置到发布后。

## 边界与停止点

- 不新增升级策略状态机、durable 强制状态、宽限计时、路径图或自动连跳。
- 不直接恢复或 cherry-pick Stage13 的 v1.0.5/v1.0.6 实现；只从 v1.0.4 当前代码按本卡重新做最小 diff。
- 不新增 migration、ModelSnapshot、业务 SQLite、依赖、更新协议字段或未经授权门禁。
- 既有签名 manifest、包校验、maintenance、journal、rollback、candidate ACK、pending recovery 继续复用，除本卡确认的 same-schema normal-launch 收口外不得扩写核心状态机。
- 默认 `NO_FULL`；自动化仅使用 TEMP/GUID 隔离 app/data/updater/operationId 与合成数据库，禁止访问正式 SQLite、安装根、数据根或备份。
- 当前只完成治理冻结。未创建 Terra，未写生产代码，未 build、未跑测试、未 Full、未生成候选、未 push/tag/Release。
- 已到可由用户单独授权实施的停止点；收到授权前保持 `IMPLEMENTATION_NOT_AUTHORIZED`。

任务、设计与验收边界见 `../TASKS/S14-T01.md`、`../ANALYSIS/S14-T01-SIMPLIFIED-ONLINE-UPDATE-DESIGN.md`、`../ACCEPTANCE/S14-T01.md`。
