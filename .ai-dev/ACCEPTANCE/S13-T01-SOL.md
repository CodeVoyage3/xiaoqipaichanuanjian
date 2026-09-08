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

## 2026-09-08 compatibility candidate 重建与持久冻结

- 旧 TEMP/GUID compatibility candidate：`LOST_BY_TEMP_CLEANUP / SUPERSEDED`。这是存储生命周期事件，不是生产安全失败或 compatibility FAIL。
- 新 candidate：`V105_COMPATIBILITY_CANDIDATE_REBUILT_AND_FROZEN`；生产 source 固定为 `3b483ef772d6442febfbcdd16ab87c6965839445`，持久目录 `D:\S13-TestAssets\v1.0.5-compatibility\3b483ef7`。
- ZIP：109428212 bytes，SHA256 `F27E6F14ADE71FE6BABB364EBF023DF29237F14D6FFF04BFF5E88128AED18224`；package tree SHA256 `AD3451BD8789F253EED4738823B1BD7FAA97B0627AE08E74D4E17DFE390D5166`；618 entries，禁入项 0。
- manifest：869 bytes，SHA256 `56F84C0078691B7631195285AEDC30CC0C5D9DA5348128A565E9B201FDC33CEC`；source version `1.0.2..1.0.4`，source migration min/max 均为 `20260901155124_AddPolicyAndBaselineFoundation`，target migrations 9。
- signature：384 bytes，SHA256 `0351C893B55A727A37E1FB899FD357123C381D9E98DA143DD7CBCECCCA5D561A`；production RSA-PSS/SHA256 PASS，公钥指纹 `565956021399C88A8B13DD0873D2A801F6675EAB44BEB4FC8EBE53C71FEFBADC`。
- App/Updater FileVersion 均为 `1.0.5.0`；Updater PE subsystem `2 / Windows GUI`。构建输出与持久副本 bytes/SHA256 全等；持久目录只含三项公开型资产及无 secret 的 `validation-receipt.json`，不含私钥或业务数据。
- 本轮未修改生产代码，未运行 full/90/90/7/7/178/Stage9 矩阵，未发布、tag 或创建 Release。下一状态：`IMPLEMENTED / SOL_TECHNICAL_ACCEPTANCE_READY / WAITING_TEST_TRANSPORT_ENTRY / NOT_ACCEPTED`。

## 2026-09-08 产品兼容范围裁决

- 用户撤销 v1.0.2/v1.0.3 -> v1.0.5 生产兼容要求；两版未形成需维护的门店升级基线。既有 TEMP/Sandbox/TEST_TRANSPORT 记录保留为历史环境证据，不记 compatibility FAIL，不再继续代理、CA、Sandbox、VM 或独立账户工作。
- v1.0.5 正式 manifest 固定 `minVersion=maxVersion=1.0.4`；source migration min/max 严格为 production migration 9。现有 1.0.2..1.0.4 compatibility manifest/signature 被此产品决策取代，不得作为最终发布 manifest，其冻结 bytes 与历史验签证据保留。
- 当前无 Sandbox、mitmproxy 进程或当前用户 mitmproxy CA；主机代理仍为原值 `127.0.0.1:7890`。未修改生产代码，NO_FULL。
- 下一状态：`IMPLEMENTED / SOL_TECHNICAL_ACCEPTANCE_READY / V104_ONLY_SOURCE_RANGE_DECIDED / FINAL_CANDIDATE_PENDING / NOT_ACCEPTED`。下一步只准备 v1.0.4-only 最终候选；用户真实 GUI/升级验收与发布收口继续分离。
