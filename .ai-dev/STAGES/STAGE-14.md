# Stage14｜新版 v1.0.5 简化在线更新

日期：2026-09-08（Asia/Shanghai）

Stage14 = `IN_PROGRESS`

S14-T01 = `V105_RELEASED / WAITING_USER_REAL_V104_TO_V105_UPGRADE / NOT_ACCEPTED`

## 正式发布收口（2026-09-09）

- GitHub stable/latest `v1.0.5` 已发布：Release ID `385203812`，`draft=false`、`prerelease=false`。
- annotated tag object `118d180882f29c9a8a0c4539e024a401b4a6c312` 解引用到精确产品 source `cddd897d72c4e95c6fd974d5ea44bc24d9c985c7`。
- 发布后匿名 fresh 下载四项资产与冻结候选 SHA256/size 全等，`PUBLIC_V105_RELEASE_ASSET_EQUIVALENCE = PASS`；manifest 仍为 `1.0.4 -> 1.0.5`、migration `9 -> 9`。
- `NO_FULL`；未重新 build、生成、签名或修改候选，未访问正式 SQLite，未创建 S14-T02。
- 当前只等待用户使用正式 v1.0.4 执行真实在线升级；通过前不得声称 `v1.0.4 -> v1.0.5 ONLINE UPDATE VERIFIED`。

目标版本：新版 `v1.0.5`。唯一产品基线为正式 `v1.0.4`；Stage13 的 v1.0.5/v1.0.6 强制升级实现已废弃，只保留历史审计证据。

## 治理冻结时现场（历史）

- 已执行 `git fetch --prune origin`。
- 上一轮 Stage14 治理提交 `3548c9cb239aeccf064e0003b079eaac22d198e8` 已普通 fast-forward push；本轮修正开工 fresh 现场为 `main HEAD == origin/main == 3548c9cb239aeccf064e0003b079eaac22d198e8`，ahead/behind `0/0`。
- GitHub 官方 API fresh 返回 latest stable `v1.0.4`，Release ID `383891004`，`draft=false`、`prerelease=false`；远端仅保留 `v1.0.4` tag，解引用到产品 source `c2b3f699408f422b3aeaf5b96321a6df08a038c5`，远端无 v1.0.5/v1.0.6 tags。
- 上一轮治理前的 `747fb69e316bbf3e428e6985edacec3cc8d8c65b` 是当时现场；其非 `.ai-dev/**` 提交树与 `v1.0.4` 无差异，不代表本轮当前 main。
- 主工作树已有用户文件：`tests/StoreExpiryInspector.Tests/Stage4ViewModelTests.cs` 已修改；两个操作手册 HTML、`tools/Reset-StoreExpiryInspectorTestHost.cmd`、`tools/Reset-StoreExpiryInspectorTestHost.ps1` 未跟踪。本轮均未触碰、恢复、删除、格式化、add 或 commit。

## 唯一交付

S14-T01 从 v1.0.4 重新实现非强制在线更新：主界面和托盘优先可用，启动后后台异步检查；发现新版只提供“立即更新 / 稍后提醒”，稍后提醒经一次明确告知后继续正常使用；网络失败不影响业务；托盘与每日提醒互不拖垮；安装器允许注册表 `DisplayVersion` 与实际 EXE 版本不一致；发布前只做技术候选验收，真实 v1.0.4 -> 新 v1.0.5 在线升级验收后置到发布后。

正式 `v1.0.4 -> 新 v1.0.5` 的程序切换事务由 v1.0.4 安装目录内自带的旧 Updater 执行。新包内的 v1.0.5 Updater 不能反向改变已经开始的事务；该路径的兼容责任在新 v1.0.5 App：接受旧 Updater 的普通启动方式，可靠显示真实主界面并创建托盘，后台检查不得阻塞，也不得留下“进程/Mutex 存在但无界面、无托盘”。新 v1.0.5 Updater 的 same-schema Loaded/ACK/Completed 收口只供 `v1.0.5 -> 后续版本` 使用。

## 边界与停止点

- 不新增升级策略状态机、durable 强制状态、宽限计时、路径图或自动连跳。
- 不直接恢复或 cherry-pick Stage13 的 v1.0.5/v1.0.6 实现；只从 v1.0.4 当前代码按本卡重新做最小 diff。
- 不新增 migration、ModelSnapshot、业务 SQLite、依赖、更新协议字段或未经授权门禁。
- 既有签名 manifest、包校验、maintenance、journal、rollback、candidate ACK、pending recovery 继续复用。v1.0.5 App 兼容旧 Updater 与 v1.0.5 Updater 供后续版本使用的 same-schema 收口必须分开验收，不得把后者当成前者的保障，也不得扩写核心状态机。
- 默认 `NO_FULL`；自动化仅使用 TEMP/GUID 隔离 app/data/updater/operationId 与合成数据库，禁止访问正式 SQLite、安装根、数据根或备份。
- 实现与候选技术验收已完成；本次仓库收口不重建候选、不重跑测试/build/Full，且不访问正式数据。
- 当前停止点为 `V105_RELEASED / WAITING_USER_REAL_V104_TO_V105_UPGRADE / NOT_ACCEPTED`。

任务、设计与验收边界见 `../TASKS/S14-T01.md`、`../ANALYSIS/S14-T01-SIMPLIFIED-ONLINE-UPDATE-DESIGN.md`、`../ACCEPTANCE/S14-T01.md`。
