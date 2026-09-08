# Stage13｜强制升级与合法跨版本升级链

> 2026-09-08 最终裁决：`Stage13 = CLOSED / SUPERSEDED_BY_PRODUCT_DECISION`。v1.0.5/v1.0.6 实现废弃，产品树已恢复到 v1.0.4；本文件下方强制升级设计与历史状态仅作审计，不再是现行产品要求。后续 v1.0.5 将另行按非强制异步更新规则设计，本轮未创建或实施。详见 `../ACCEPTANCE/S13-T01-ROLLBACK-TO-V104.md`。

Stage13 = IN_PROGRESS
S13-T01 = REAL_V104_TO_V105_GUI_FAILED / NORMAL_LAUNCH_FIX_TECHNICALLY_ACCEPTED / V106_HOTFIX_CANDIDATE_PENDING / NOT_ACCEPTED
当前 hotfix 目标版本：v1.0.6；v1.0.5 仍为已发布 stable。

v1.0.5 production source 为 `3b483ef772d6442febfbcdd16ab87c6965839445`；治理提交不得冒充产品 source。真实 v1.0.4 -> v1.0.5 已暴露安装后自动启动失败；same-schema normal-launch 最小修复已由 Terra 提交并经 Sol 技术接受。v1.0.6 候选、发布及真实 v1.0.5 -> v1.0.6 验收均未授权，详见 `../ACCEPTANCE/S13-T01-SOL.md`。

## 2026-09-08 normal-launch hotfix 技术接受

- 实现提交：`a66db7e829b7508da439ef8f6a70d11954e574a8`、`a8983ed6fe3d7f6a76bf2545e09b6e64d2d3a355`。
- same-schema `Committed` 路径复用既有 intent/token、进程身份、candidate tree、Loaded/ACK 与超时清理；schema evidence 区分仍 fail-closed。
- Sol：专项 `43/43`、相邻高风险 `8/8`、双 Release build、EF/migration 9、PE subsystem 2、diff/secret/scope 门禁 PASS。
- `NO_FULL`；仅隔离 TEMP/GUID；无候选、push、tag、Release、S13-T02 或 Stage14。

## 开工现场（2026-09-07，Asia/Shanghai）

- fresh fetch 后 `HEAD == origin/main == 13156822a0873c47c6bd24a3d3a4d75e40beb494`，ahead/behind `0/0`。
- GitHub `releases/latest` 为 stable `v1.0.4`，`draft=false`、`prerelease=false`，产品 source 为 `c2b3f699408f422b3aeaf5b96321a6df08a038c5`；四项公开资产仍在。
- Stage12 与 S12-T01 已 CLOSED；开工前不存在 Stage13/S13 文件。
- 既有 `tests/StoreExpiryInspector.Tests/Stage4ViewModelTests.cs` 仅换行状态及未跟踪 `docs/门店效期排查软件_操作手册.html` 均非本轮产物，禁止恢复、删除、格式化、覆盖、add 或 commit。

## 唯一交付

S13-T01 实现并验收：可信新版一经确认即持久化强制升级；仅临时网络故障可使用 24 小时离线宽限；首次启动宽限不可因普通重启或单文件删除轻易续期；安全失败与无合法路径 fail-closed；启动与每 6 小时检查；强制 modal/托盘/业务命令无绕过；一次确认后逐跳自动继续；从 Stage13 客户端起解析每跳均获签名 manifest 授权的确定性升级路径。

Stage9 pending recovery、candidate ACK、rollback、maintenance、schema recovery、Updater recovery 先于普通 Stage13 门禁执行，既有冻结状态机不得被改写或绕过。

## v1.0.5 发布后停止点

stable v1.0.5 与公开资产等价门禁已通过；Release/tag/资产不得改写。用户使用正式 v1.0.4 完成真实 v1.0.4 -> v1.0.5 六项 GUI/升级验收前，S13-T01 与 Stage13 不关闭。v1.0.2/v1.0.3 已由产品裁决移出生产兼容范围，历史环境阻断不记兼容失败。

## 边界与停止点

- production migration 固定 `9`；默认禁止 migration/ModelSnapshot/业务 SQLite 变更。认为必须新增时，Terra 立即停止交 Sol 裁决。
- 默认 `NO_FULL`；只跑新专项、focused 与直接相关 S9-T03/T04/T06/upgrade-safety 回归。只有冻结 diff 触及任务卡列明的核心事务/恢复/Schema 范围或出现无法解释的跨模块回归，Sol 才重新裁决是否最终仅跑一次 fresh unfiltered Release full。
- 自动化仅用 `TEMP/GUID` 隔离 app/data/updater/operationId 与合成数据库。禁止探测、打开、查询、复制、hash 或比较正式数据库、安装根、数据根或备份；`HISTORICAL_TEST_ISOLATION_UNCERTAINTY` 保留。
- 不修改、覆盖、重建或替换 v1.0.4/v1.0.5 Release 资产；不创建 S13-T02、不实现 Setup 万能修复。
- 用户已于 2026-09-07 授权实施；从本治理提交创建全新隔离 worktree 与全新 Terra。不得复用 Stage12 或更早 Terra，Sol 不写生产代码。

任务、设计和验收分别见 `../TASKS/S13-T01.md`、`../ANALYSIS/S13-T01-FORCED-UPDATE-DESIGN.md`、`../ACCEPTANCE/S13-T01.md`。
