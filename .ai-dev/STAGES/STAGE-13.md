# Stage13｜强制升级与合法跨版本升级链

Stage13 = IN_PROGRESS
S13-T01 = IMPLEMENTED / SOL_TECHNICAL_ACCEPTANCE_READY / V104_ONLY_SOURCE_RANGE_DECIDED / FINAL_CANDIDATE_PENDING / NOT_ACCEPTED
目标版本：v1.0.5。

candidate / production source 为 `3b483ef772d6442febfbcdd16ab87c6965839445`；当前 governance main 为包含本节的 `origin/main`，治理提交不得冒充 candidate source。用户已裁决仅维护 v1.0.4 -> v1.0.5：正式 manifest 固定 `minVersion=maxVersion=1.0.4` 且 source migration 严格为 production migration 9；v1.0.2/v1.0.3 与 TEST_TRANSPORT 不再是 blocker。现有宽范围 compatibility manifest/signature 仅留历史证据，不得发布。当前只待 v1.0.4-only 最终候选、用户真实 GUI/升级验收及后续独立发布授权。详见 `../ACCEPTANCE/S13-T01-SOL.md`。

## 开工现场（2026-09-07，Asia/Shanghai）

- fresh fetch 后 `HEAD == origin/main == 13156822a0873c47c6bd24a3d3a4d75e40beb494`，ahead/behind `0/0`。
- GitHub `releases/latest` 为 stable `v1.0.4`，`draft=false`、`prerelease=false`，产品 source 为 `c2b3f699408f422b3aeaf5b96321a6df08a038c5`；四项公开资产仍在。
- Stage12 与 S12-T01 已 CLOSED；开工前不存在 Stage13/S13 文件。
- 既有 `tests/StoreExpiryInspector.Tests/Stage4ViewModelTests.cs` 仅换行状态及未跟踪 `docs/门店效期排查软件_操作手册.html` 均非本轮产物，禁止恢复、删除、格式化、覆盖、add 或 commit。

## 唯一交付

S13-T01 实现并验收：可信新版一经确认即持久化强制升级；仅临时网络故障可使用 24 小时离线宽限；首次启动宽限不可因普通重启或单文件删除轻易续期；安全失败与无合法路径 fail-closed；启动与每 6 小时检查；强制 modal/托盘/业务命令无绕过；一次确认后逐跳自动继续；从 Stage13 客户端起解析每跳均获签名 manifest 授权的确定性升级路径。

Stage9 pending recovery、candidate ACK、rollback、maintenance、schema recovery、Updater recovery 先于普通 Stage13 门禁执行，既有冻结状态机不得被改写或绕过。

## v1.0.5 发布阻断

`1.0.2 / 1.0.3 / 1.0.4 -> 1.0.5` 是独立 `RELEASE BLOCKER`。三个公开 tag 当前均有相同 9 条 production migration，这只是兼容可能，不是放宽依据。必须分别证明旧客户端现有 protocol、签名 manifest、Updater、maintenance、journal、rollback、candidate ACK、pending recovery、安装布局、身份、数据保持和 migration 安全后，Sol 才可决定 v1.0.5 manifest 的 source range；任何一条不安全即采用明确兼容引导并保持 fail-closed。

## 边界与停止点

- production migration 固定 `9`；默认禁止 migration/ModelSnapshot/业务 SQLite 变更。认为必须新增时，Terra 立即停止交 Sol 裁决。
- 默认 `NO_FULL`；只跑新专项、focused 与直接相关 S9-T03/T04/T06/upgrade-safety 回归。只有冻结 diff 触及任务卡列明的核心事务/恢复/Schema 范围或出现无法解释的跨模块回归，Sol 才重新裁决是否最终仅跑一次 fresh unfiltered Release full。
- 自动化仅用 `TEMP/GUID` 隔离 app/data/updater/operationId 与合成数据库。禁止探测、打开、查询、复制、hash 或比较正式数据库、安装根、数据根或备份；`HISTORICAL_TEST_ISOLATION_UNCERTAINTY` 保留。
- 不修改、覆盖、重建或替换 v1.0.4 Release 资产；本卡不发布 v1.0.5、不创建 S13-T02、不实现 Setup 万能修复。
- 用户已于 2026-09-07 授权实施；从本治理提交创建全新隔离 worktree 与全新 Terra。不得复用 Stage12 或更早 Terra，Sol 不写生产代码。

任务、设计和验收分别见 `../TASKS/S13-T01.md`、`../ANALYSIS/S13-T01-FORCED-UPDATE-DESIGN.md`、`../ACCEPTANCE/S13-T01.md`。
