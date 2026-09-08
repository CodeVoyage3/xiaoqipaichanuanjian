# S13-T01 latest Sol decision (2026-09-08)

Status: `V106_CORRECTED_CANDIDATE_STATICALLY_ACCEPTED / OFFICIAL_DUAL_SOURCE_TRANSACTION_BLOCKED / RELEASE_AUTHORIZATION_NOT_REACHED / NOT_ACCEPTED`.

## v1.0.6 corrected final candidate

A new independent candidate was generated from final product source `a8983ed6fe3d7f6a76bf2545e09b6e64d2d3a355` and frozen at `D:\\S13-TestAssets\\v1.0.6-corrected-final\\a8983ed6`. Manifest source versions are 1.0.4..1.0.5 and source migration min/max are both migration 9; target migration count is 9. Production RSA-PSS/SHA256, manifest/package identity, ZIP 618-entry tree, unsafe/duplicate/prohibited-entry checks, App/Updater/Setup versions and Updater subsystem 2 all PASS. See `S13-T01-V106-CORRECTED-CANDIDATE.md`.

The prior candidate remains `REJECTED / SUPERSEDED_BY_CORRECTED_MIGRATION_CONTRACT`. No production code, Schema or migration changed; existing 43/43, 8/8, Release build 0/0 and EF NO_MODEL_DRIFT remain valid. Full was not run.

Official clients only consume GitHub public Releases; the unpublished v1.0.6 has no production transport entry. The official Updater also validates the current user's fixed HKCU install identity and formal `%LOCALAPPDATA%` data root, while TEMP/GUID is test mapping only. With fake Release forbidden and no separate authorization for Sandbox/VM/HTTPS proxy/CA, v1.0.4->v1.0.6 and v1.0.5->v1.0.6 are both NOT_RUN. The normal-launch bug is not yet proven fixed by the official v1.0.5 source path, and the release-authorization boundary is not reached.

# S13-T01 Sol 独立技术验收（2026-09-07）

结论：`REAL_V104_TO_V105_GUI_FAILED / NORMAL_LAUNCH_FIX_TECHNICALLY_ACCEPTED / V106_HOTFIX_CANDIDATE_PENDING / NOT_ACCEPTED`。

## 2026-09-08 v1.0.4 -> v1.0.5 事故与 normal-launch hotfix

用户真实升级回执为：更新提示、下载、安装程序启动、安装完成均 PASS；安装后自动启动新版 FAIL；手动启动 v1.0.5 PASS。该回执不等同于升级成功，也不关闭 S13-T01。

Terra 在 fresh 隔离分支提交 `a66db7e829b7508da439ef8f6a70d11954e574a8`，使 same-schema `Committed` 成功路径复用既有 `NormalLaunchHandshake`。Sol 全 diff 复核发现首次实现会把 `normal-launch.json` 一概移出 schema evidence，退回后由同一 Terra 以 `a8983ed6fe3d7f6a76bf2545e09b6e64d2d3a355` 修复：只有 operation/data root/app path/candidate tree/role/phase 完整绑定的 same-schema intent 才可排除；畸形、错绑或 schema-bound evidence 继续 fail-closed。

最终 diff 共 8 个文件；未新增协议文件、字段、状态、migration 或依赖。Sol fresh 独立结果：专项 `43/43`，相邻高风险 `8/8`；App/Updater Release build 均 `0 warning / 0 error`；EF `NO_MODEL_DRIFT`；migration 9，末条 `20260901155124_AddPolicyAndBaselineFoundation`；App/Updater PE subsystem 2；`git diff --check`、限定 secret scan、schema/migration 与禁改范围检查均 PASS。工作树 clean，分支未 push。

`NO_FULL`。全部动态验证仅使用隔离 worktree 与 TEMP/GUID，未访问正式安装、正式数据库、正式数据根或备份。未生成 v1.0.6 候选，未 tag/Release，未创建 S13-T02/Stage14。下一步必须另获候选授权，并由用户完成真实 v1.0.5 -> v1.0.6 GUI/升级验收。

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

## 2026-09-08 v1.0.5 最终候选独立技术验收

- fresh：隔离 worktree `HEAD == origin/main == 576372ce84d8aa0dbc4c484d7c06812ddd543788`，ahead/behind `0/0`、clean；`3b483ef772d6442febfbcdd16ab87c6965839445..HEAD` 只有四个治理提交，排除 `.ai-dev/**` 后产品树无差异。GitHub `releases/latest` 302 到 `v1.0.4`，v1.0.4 Release 页面 HTTP 200。
- 最终 source 固定 `3b483ef772d6442febfbcdd16ab87c6965839445`。全新输出使用现有 Stage9 发布协议、win-x64/self-contained、生产 DPAPI CurrentUser RSA3072 身份及 Inno Setup；没有修改仓库生产源码。首次 publish 因隔离 worktree 缺少 win-x64 restore 资产在生成候选前 fail-closed，定向 restore App/Updater 后在新 GUID 输出成功。
- 持久目录 `D:\S13-TestAssets\v1.0.5-final\3b483ef7`。ZIP 109428193 bytes / `D5C7D5DF10C9E3D900C3B064C6C79A5A090E6FC2CBC9D50C218BAB725FDBA53E`；Setup 75342417 bytes / `AC1DD32726C9B2D8EDD39CBB386FD24D3F32EAA4CC05435EA68860BB3740DDAC`；manifest 869 bytes / `BE364995F262701DDF8362B0B8BE2E3260F303FEF5D7D346B4A8659B0AF55F95`；signature 384 bytes / `978E0A66292DB9980AFC4D4F861CED15BF5738A6C4D8855DB364D711844217FC`。四资产构建输出与持久副本 bytes/SHA256 全等；目录不含私钥或业务数据。
- manifest：schema 1、stable、version `1.0.5`、releaseTag `v1.0.5`、rid `win-x64`、`minVersion=maxVersion=1.0.4`；source migration min/max 均为 `20260901155124_AddPolicyAndBaselineFoundation`，target migrations 9。package bytes/SHA 与 ZIP 一致。
- production RSA-PSS/SHA256 独立验签 PASS；公钥 SPKI SHA256 `565956021399C88A8B13DD0873D2A801F6675EAB44BEB4FC8EBE53C71FEFBADC`。签名 SHA 与旧 candidate 不同符合 RSA-PSS 随机性，判断以生产公钥验签为准。
- App/Updater FileVersion 均 `1.0.5.0`，Updater PE subsystem `2 / Windows GUI`，Setup FileVersion/ProductVersion `1.0.5`。ZIP 618 个文件与 release evidence 逐项 bytes/SHA 全等，package tree SHA256 `25FC29E568E14AFB2D02241D83D00B958D2F2971E93B5DFB70ADC5334DD38E07`，allowlist unexpected 0、禁入项 0、路径/重复项 0。独立审计首轮仅因规则把 `SQLitePCLRaw.*.dll` 误当数据库文件而误报，修正为真实文件扩展匹配后同一候选通过，未重建资产。
- Release App/Updater build 均 0 warning/0 error；EF `NO_MODEL_DRIFT`；migrationCount 9。内置 `--s9-t01-smoke-exit` 使用全新 TEMP/GUID data root，exit 0、创建隔离 app.db、写入 `s9_t01_smoke_ready`。此前两次未传内置 smoke 参数的外部窗口观察被启动更新策略阻断并由测试宿主终止，均未创建数据库，不记产品 smoke 失败。
- `git diff --check`（source..HEAD 与 worktree）PASS；source/installer 与持久四资产 secret scan 0 命中；最终治理前 worktree clean。本轮没有 full/90/7/178/Stage9 矩阵，没有 production code、schema、dependency 或 migration 修改，没有 Sandbox/TEST_TRANSPORT/proxy/CA，没有 tag/Release/upload。
- 技术结论：`FINAL_CANDIDATE_TECHNICALLY_ACCEPTED`。S13-T01 = `FINAL_CANDIDATE_TECHNICALLY_ACCEPTED / RELEASE_AUTHORIZATION_PENDING / NOT_ACCEPTED`；Stage13 = IN_PROGRESS；v1.0.5 = NOT_RELEASED。停止等待用户单独发布授权；发布后公开资产等价复核和用户真实 v1.0.4 -> v1.0.5 GUI/升级通过前不得关闭。

## 2026-09-08 stable v1.0.5 发布与公开资产等价复核

- 发布前 fresh：`HEAD == origin/main == 037f9c63193d3d8bec1270cee31843bbf040acff`、ahead/behind `0/0`、clean；排除 `.ai-dev/**` 后 `3b483ef772d6442febfbcdd16ab87c6965839445..HEAD` 产品 diff 为 0。latest 仍为 stable v1.0.4，v1.0.5 tag/Release 均不存在；四项冻结资产与最终验收 bytes/SHA 全等，production RSA-PSS/SHA256 再验 PASS。
- stable Release `384564898`：<https://github.com/CodeVoyage3/xiaoqipaichanuanjian/releases/tag/v1.0.5>；latest、`draft=false`、`prerelease=false`。annotated tag object `2ac5961be79796ee3c9da140ff13139ee1218730` 解引用到产品 source `3b483ef772d6442febfbcdd16ab87c6965839445`。
- 匿名公开下载等价：ZIP 109428193 / `D5C7D5DF10C9E3D900C3B064C6C79A5A090E6FC2CBC9D50C218BAB725FDBA53E`；Setup 75342417 / `AC1DD32726C9B2D8EDD39CBB386FD24D3F32EAA4CC05435EA68860BB3740DDAC`；manifest 869 / `BE364995F262701DDF8362B0B8BE2E3260F303FEF5D7D346B4A8659B0AF55F95`；signature 384 / `978E0A66292DB9980AFC4D4F861CED15BF5738A6C4D8855DB364D711844217FC`。
- 公开 manifest/signature 使用 production 公钥 RSA-PSS/SHA256 PASS；SPKI SHA256 `565956021399C88A8B13DD0873D2A801F6675EAB44BEB4FC8EBE53C71FEFBADC`。manifest 严格 `minVersion=maxVersion=1.0.4`，source migration min/max 为第 9 条，target migrations 9。
- 公开 ZIP：618 项；tree SHA256 `25FC29E568E14AFB2D02241D83D00B958D2F2971E93B5DFB70ADC5334DD38E07`；禁入项、不安全路径、重复项均 0；App/Updater FileVersion `1.0.5.0`，Updater subsystem `2 / Windows GUI`；Setup `1.0.5`。v1.0.4 Release ID、发布时间及四资产 name/size/digest 均未改变。
- `PUBLIC_V105_RELEASE_ASSET_EQUIVALENCE = PASS`。本轮无生产代码修改，无 rebuild/full/90/7/178/Stage9 矩阵；NO_FULL。S13-T01 = `V105_RELEASED / WAITING_USER_V104_TO_V105_UPGRADE / NOT_ACCEPTED`；Stage13 = IN_PROGRESS。
