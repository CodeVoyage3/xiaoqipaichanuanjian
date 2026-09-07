# S13-T01 Sol 独立技术验收（2026-09-07）

结论：`IMPLEMENTED / SOL_TECHNICAL_ACCEPTANCE_READY / NOT_ACCEPTED`。

生产实现已在隔离分支 `codex/s13-t01-terra` 提交至 `3b483ef772d6442febfbcdd16ab87c6965839445`，未 push、未 tag、未发布。Sol 完整 diff 复核后发现并退回的新阻断均由新的 Terra 修复：真实 MainWindow/Stage9 terminal 后的数据库维护时序、强制启动窗第三按钮、RequiredVersion 单调保护，以及 authoritative higher 已可信验签后路径查询失败仍必须先 durable 强制。

## 独立门禁

- Release App / Updater build：均 `0 warning / 0 error`。
- S13 + S9-T03/T04/T06 + 必要 normal-launch safety：最终通过；主集合 `90/90`，normal-launch 集合 `7/7`，两集合有重复用例。主 TRX SHA256：`298DBEF8CC98F52FCB960FCB30B9A739FC121DA1CB684D8EB66E51D2C31ABC53`；normal-launch TRX SHA256：`8B29DCB667C9DDF2125198C541A554F7EF6B03F8D92D8327B7F9BF2C7819EDE1`。
- 首轮 `81/85` 的 4 项 S9-T04 hash 失败保留：Sol 先重建 production App 后错误使用 `--no-build` 运行陈旧测试输出，测试目录受控 App 副本与 fresh production hash 不同；重建测试程序集后同范围通过，不计为产品缺陷。
- EF：`No changes have been made to the model since the last migration.`；production migration 清单为 9 条，末条 `20260901155124_AddPolicyAndBaselineFoundation`。
- TEMP/GUID candidate：App `1.0.5.0`、PE subsystem 2；Updater 以候选参数 `Version=1.0.5` 发布后为 `1.0.5.0`、PE subsystem 2。
- `git diff --check` PASS；本卡 diff 限定 secret scan 无命中；工作树 clean。
- `NO_FULL` 保持：本轮没有修改 Updater/UpdateSafety/Stage9 journal、rollback、ACK、schema state machine，也没有 migration/ModelSnapshot/业务 SQLite 变化；未出现需要 full 兜底的未知回归。
- 全部动态验证仅使用隔离 worktree 与 TEMP/GUID；未访问正式安装、正式数据库、正式数据根或备份。

## 仍开放的 Release blocker

`1.0.2 / 1.0.3 / 1.0.4 -> 1.0.5` 三源公开原客户端/旧 Updater 全链尚未完成。现有 `RealProductionOldRollbackRestoresSchema9AndLoadsOldShell` 只证明硬编码的 production `1.0.3 -> fixture 1.0.4`，不能泛化为三源到 1.0.5。公开原客户端还需要实际的、经 production key 签名的 v1.0.5 manifest/ZIP 候选才能证明 source range、旧客户端下载验签、旧 Updater、journal、candidate ACK、normal success、数据保持及代表性 rollback。

因此本轮只到 `SOL_TECHNICAL_ACCEPTANCE_READY`：生产功能与测试基础已实现并通过当前独立门禁，但 S13-T01 不标 `ACCEPTED/CLOSED`，v1.0.5 manifest 不批准 `minVersion=1.0.2/maxVersion=1.0.4`，也不允许发布。后续必须另获候选生成/签名与三源专项授权；不得创建 S13-T02 代替本 blocker。

`HISTORICAL_TEST_ISOLATION_UNCERTAINTY` 保留。
