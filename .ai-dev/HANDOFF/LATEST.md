# 2026-09-09：S17-T01 治理冻结，批准派发独立 Terra

fresh fetch 确认 `HEAD == origin/main == a6a47f2a255f3eaad5687a7700d72818d0ee5898`、ahead/behind `0/0`；stable/latest 仍为 v1.0.6，Stage15 `CLOSED`，Stage16 未启动且继续预留“未来效期风险总览”。正式主工作区原 1 个修改测试文件与 4 个未跟踪用户文件保持未触碰。

Stage17 = `IN_PROGRESS / GOVERNANCE_FROZEN / IMPLEMENTATION_DISPATCH_AUTHORIZED`；S17-T01 = `GOVERNANCE_FROZEN / IMPLEMENTATION_DISPATCH_AUTHORIZED / NOT_IMPLEMENTED`。本卡只允许全新 GPT-5.6 Terra 从 fresh clean worktree 实现 GitHub 指定网络失败后的 Gitee metadata + 夸克人工下载兜底；当前 Sol 禁止直接修改生产代码，只负责独立治理和技术验收。

本轮只冻结 Task/Acceptance，不修改生产代码，不运行测试/build/EF，不访问正式数据库，不修改 Stage16，不 tag/Release，不上传夸克正式安装包，不把 Gitee `latest.json` 改为正式 1.0.7。详见 `../STAGES/STAGE-17.md`、`../TASKS/S17-T01.md`、`../ACCEPTANCE/S17-T01.md`。


# 2026-09-09：门店效期排查软件 v1.0.6 正式发布

GitHub stable/latest Release `v1.0.6` 已正式发布：<https://github.com/CodeVoyage3/xiaoqipaichanuanjian/releases/tag/v1.0.6>。Release ID `385388292`，`draft=false`、`prerelease=false`；annotated tag object `2dfb272a2590902f67665a1c96368897bacfab76` 解引用到精确产品 source `93affffaa98b17e61ecd79970b450937a5c15380`，不指向后续治理提交。

发布后从 GitHub 公网 fresh 下载四项资产，bytes/SHA256 与冻结候选逐项全等：ZIP 109412006 bytes / `80827A771FCF287E28BD3A4FDAE488216FCB2D42D357B7A557C91A8C893A85EE`；Setup 75334222 bytes / `830E0A27779EBECA5E2DFFCD61B8FD297F3F679D785FF5586176B1F5ED63FFB3`；manifest 869 bytes / `D8EDC896CB4B88BFFED84AD9502002F76BB17148B593E647418ACE9AB07015BE`；signature 384 bytes / `E9C277D92E3321A87A95A20878F260CB58EA1D08FF344E073B9FE8C62E94568E`。`PUBLIC_V106_RELEASE_ASSET_EQUIVALENCE = PASS`，公开 manifest production signature 验证 `PASS`。

manifest 合同为 target `1.0.6`、source `1.0.4..1.0.5`、migrationCount `9`；支持 `v1.0.4 -> v1.0.6` 与 `v1.0.5 -> v1.0.6`。本轮未重建、重新 ZIP、重签或替换候选，`FULL = NOT_RUN / NO_FULL`，正式数据库访问 `NO`。Stage15 继续 `CLOSED`，S15-T01/S15-T02 继续 `GUI_ACCEPTED / CLOSED`；Stage16 未启动。


# 2026-09-09：S15-T02 GUI 验收通过，Stage15 正式关闭

用户已明确回执 `S15-T02 GUI 验收通过`：确认“本次更新”区域、完整门店版更新说明、长内容滚动、窗口尺寸、当前/最新版本及“稍后提醒 / 立即更新”均正常。

S15-T01 = `GUI_ACCEPTED / CLOSED`；S15-T02 = `GUI_ACCEPTED / CLOSED`；Stage15 = `CLOSED`。S15-T02 既有技术证据继续继承：technical review `PASS`、targeted `10/10 PASS`、Release build `0 warning / 0 error`、`FULL = NOT_RUN / NO_FULL`、正式数据库访问 `NO`。

本轮仅治理文档收口，未重跑 targeted、Release build、Full 或 GUI，未修改生产代码；不 tag、不 Release、不发布 v1.0.6、不创建 S15-T03。临时 acceptance Harness 与远端 Terra 分支均不作为 Stage15 关闭门禁。


# 2026-09-09：S15-T02 技术接受并集成，等待用户 GUI 验收

Sol 已独立读取并审查真实 Terra diff：`TECHNICAL_REVIEW = PASS`，`REWORK = 0`。Terra `0afddbdd5cb42f1f7f9d02ca84d1267842a30e00` 已从 fresh `origin/main@7308d8ff35503b6ebefc699bca3a2f6baacf50f3` 无冲突 cherry-pick 为 integration commit `f3cb2451442e5dba540796743f038072be7fd32c`；三个生产文件与 Terra 实现等价，原提交未 amend/squash/rebase。`origin/codex/s15-t02-terra` 保留至 Stage15 `CLOSED`。

S15-T02 = `TECHNICALLY_ACCEPTED / WAITING_USER_GUI_ACCEPTANCE`；Stage15 = `IN_PROGRESS / S15_T01_CLOSED / S15_T02_TECHNICALLY_ACCEPTED / WAITING_USER_GUI_ACCEPTANCE`。targeted = `INHERITED 10/10 PASS / NOT_RERUN`；Release build = `INHERITED 0 warning / 0 error / NOT_RERUN`；`FULL = NOT_RUN / NO_FULL`；正式数据库访问 `NO`。此前一次过宽 S9T03 `29/30` 的 synthetic WPF core-ready 27 秒超时保留为 `NON_BLOCKER`，本轮未追加测试。

GUI 尚未执行。下一步只等待用户验收“发现新版本”一屏的完整更新说明、滚动、版本、按钮和窗口尺寸；不重复要求 S15-T01 三阶段，不自判 GUI 通过，不 tag、不 Release、不发布 v1.0.6、不创建 S15-T03。


# 2026-09-09：S15-T02 已实现并推送独立评审分支

S15-T02 = `IMPLEMENTED / TARGETED_AND_RELEASE_BUILD_PASSED / READY_FOR_REVIEW`；Stage15 = `IN_PROGRESS / S15_T01_CLOSED / S15_T02_IMPLEMENTED / READY_FOR_REVIEW`。全新 Terra 从 fresh `origin/main@1ddb809bff6c21d816b21ddc63397608c5204dfe` 在隔离 worktree 提交 `0afddbdd5cb42f1f7f9d02ca84d1267842a30e00`，并普通 push 到 `origin/codex/s15-t02-terra`；远端 SHA 相同，worktree clean，尚未合并 main。

修改严格为 5 文件：`GitHubReleaseUpdateChecker.cs`、`UpdateNotificationViewModel.cs`、`WpfDialogService.cs`、`S9T03UpdateCheckTests.cs`、`S15T01UpdateProgressUiTests.cs`。实现仅移除 ReleaseNotes 的 1000 字符截断、直接只读暴露完整正文，并在真实更新窗口加入初始态可见的固定 `MaxHeight` 滚动区；空白说明不创建区域，进入更新进度后隐藏，不恢复 DiagnosticBanner。metadata 256KB 上限、控制字符清理及 S15-T01 三阶段/取消边界保持原样。

精确专项 `10/10 PASS`；Release App build `0 warning / 0 error`，本次无 `NU1900`；`FULL = NOT_RUN / NO_FULL`，正式数据库访问 `NO`，scope check `PASS`。过程中过宽的 S9T03 类组合过滤曾为 `29/30`，唯一未过为既有 synthetic WPF core-ready 27 秒超时；未把该运行作为本卡门禁，随后只执行精确专项并全部通过。当前停止等待 Sol 独立技术评审；不自判技术/GUI 通过，不合并实现，不 tag、不 Release、不发布 v1.0.6、不创建 S15-T03。


# 2026-09-09：S15-T01 GUI 验收关闭，S15-T02 治理冻结并授权实施

用户已明确回执 `S15-T01 GUI 验收通过`。S15-T01 = `GUI_ACCEPTED / CLOSED`；人工确认初始更新窗口简化、下载百分比/进度界面、更新中/安装中显示及稍后提醒流程均正确。既有技术证据继续继承：technical review `PASS`、targeted `6/6 PASS`、Release build `PASS / 0 error`，`NU1900 ×3 = network metadata warning / NON_BLOCKER`，`FULL = NOT_RUN / NO_FULL`；本轮未重跑测试、build 或 GUI。

Stage15 = `IN_PROGRESS / S15_T01_CLOSED / S15_T02_CURRENT`；新增唯一当前任务 `S15-T02｜更新提示恢复完整门店版更新说明`，状态为 `GOVERNANCE_FROZEN / IMPLEMENTATION_AUTHORIZED / NOT_IMPLEMENTED`。发现新版窗口恢复展示经过现有安全字符清理的完整 GitHub Release Body，固定最大高度并内部滚动；空白说明时整个区域隐藏。客户端不摘要、不删减、不按技术词过滤或改写内容，也不重复显示 DiagnosticBanner。

S15-T02 唯一必要底层调整为移除 `GitHubReleaseUpdateChecker.SanitizeNotes` 的 `.Take(1000)`，保留控制字符清理与 GitHub metadata 整体 256KB 上限；其余更新、协议、数据库及业务逻辑均冻结。下一次正式 Release Body 必须由发布治理直接写成完整、门店可理解的更新说明。当前仅完成治理，不改生产代码、不运行测试/build/Full、不访问正式数据库，不 tag、不 Release、不发布 v1.0.6、不创建 S15-T03。


# 2026-09-09：S15-T01 技术接受并集成，等待用户 GUI 验收

Sol 已独立读取并审查真实 Terra diff，技术审查 `PASS`、无返修项。Terra `2fce2c999c06302622fd3b77623109c7e0ecd5ba` 已从 fresh `origin/main@9747588fbf7856a1da784e73250c5f4c3058452a` 无冲突 cherry-pick 为 integration commit `46890b83e15b5a971a6dc6e8418c85de6b841d5a`；六个实现文件逐文件等价，Terra 原提交未 amend/rebase/squash，`origin/codex/s15-t01-terra` 保留。

Stage15 = `IN_PROGRESS / S15_T01_TECHNICALLY_ACCEPTED / WAITING_USER_GUI_ACCEPTANCE`；S15-T01 = `TECHNICALLY_ACCEPTED / WAITING_USER_GUI_ACCEPTANCE`。

原专项 `6/6 PASS`、原 Release build `0 error` 均为 `INHERITED / NOT_RERUN`；`NU1900 ×3` 是网络漏洞元数据警告、非 blocker；`FULL = NOT_RUN / NO_FULL`。GUI 最终验收尚未执行。不 tag、不 Release、不发布 v1.0.6、不创建 S15-T02。


# 2026-09-09：S15-T01 已实现，专项与 Release build 通过

Terra 在全新隔离 worktree 提交 `2fce2c999c06302622fd3b77623109c7e0ecd5ba`，未 push/tag/Release。Stage15 = `IN_PROGRESS / S15_T01_IMPLEMENTED / READY_FOR_REVIEW`；S15-T01 = `IMPLEMENTED / TARGETED_AND_RELEASE_BUILD_PASSED / READY_FOR_REVIEW`。

实际修改 6 文件：App 只增加独立 Updater 启动前的 UI 回调；更新窗口/ViewModel/状态映射实现初始极简、下载整数百分比、更新中不确定进度、安装中交接提示，以及只在真实下载阶段可见的现有安全取消；新增最小 S15 专项并调整一个直接相关 S9-T04 断言。原有稍后提醒固定二次告知保持不变。

独立复跑最小专项 `6/6 PASS`；Release App build `0 error`，3 个 `NU1900` 仅为无法获取 NuGet 漏洞元数据。`NO_FULL`；无更新底层协议、Updater/journal/rollback、manifest/signature、SQLite/migration、导航、业务 UI、托盘或每日提醒逻辑变化。不发布 v1.0.6，不创建 S15-T02。


# 2026-09-09：Stage15 / S15-T01 在线更新进度界面简化治理冻结

fresh fetch 已确认 `main == origin/main == 7cfbcd340aec97c55da880002779177c5a27834e`、ahead/behind `0/0`；Stage14 = `CLOSED`，S14-T01 = `V105_RELEASED / REAL_V104_TO_V105_ONLINE_UPDATE_VERIFIED / CLOSED`，stable/latest = `v1.0.5`。

Stage15 = `IN_PROGRESS / GOVERNANCE_FROZEN / IMPLEMENTATION_AUTHORIZED`；S15-T01 = `GOVERNANCE_FROZEN / IMPLEMENTATION_AUTHORIZED / NOT_IMPLEMENTED`。范围仅为发现新版与点击立即更新后的现有更新 UI：初始只显示版本和“稍后提醒 / 立即更新”；更新后只显示“下载中 / 更新中 / 安装中”；下载仅百分比；取消只复用安全下载阶段。固定稍后提醒二次告知不变。

默认 `NO_FULL`；只做最小专项和必要 Release build。不改导航、业务 UI、更新底层、协议、SQLite/migration、托盘或提醒；不访问正式数据；不 tag、不 Release、不发布 v1.0.6、不创建 S15-T02。治理普通 push 并确认同频后，直接从该基线创建全新隔离 worktree 和全新 Terra。


# 2026-09-09：S14-T01 / Stage14 正式关闭

用户已完成正式 v1.0.4 -> 新 v1.0.5 真实在线升级验收：在线升级成功、安装完成后 v1.0.5 自动启动成功、主界面与系统托盘正常出现、原数据正常。`REAL_V104_TO_NEW_V105_ONLINE_UPDATE_VERIFIED = PASS`。

S14-T01 = `V105_RELEASED / REAL_V104_TO_V105_ONLINE_UPDATE_VERIFIED / CLOSED`；Stage14 = `CLOSED`。`PUBLIC_V105_RELEASE_ASSET_EQUIVALENCE = PASS`、`NO_FULL` 继续保留；`163/164` 唯一缺项继续为 `S9_T07_REAL_OLD_PUBLISH = NOT_RUN / TEST_FIXTURE_UNAVAILABLE / NON_BLOCKER`。

本轮仅做 docs-only 治理收口：未修改生产代码，未重跑测试/build/Full，未访问正式 SQLite，未创建 S14-T02 或 Stage15。


# 2026-09-09：新版 v1.0.5 已发布，等待用户真实在线升级

S14-T01 = `V105_RELEASED / WAITING_USER_REAL_V104_TO_V105_UPGRADE / NOT_ACCEPTED`；Stage14 = `IN_PROGRESS`。

GitHub stable/latest Release `v1.0.5` 已发布：<https://github.com/CodeVoyage3/xiaoqipaichanuanjian/releases/tag/v1.0.5>。Release ID `385203812`，`draft=false`、`prerelease=false`；annotated tag object `118d180882f29c9a8a0c4539e024a401b4a6c312` 解引用到精确产品 source `cddd897d72c4e95c6fd974d5ea44bc24d9c985c7`，不指向治理提交。

发布后匿名 fresh 下载四项公开资产，SHA256/size 与冻结候选全部一致：ZIP 109411185 bytes / `543032FC76947772B4CAD3CC2416D611EC7C78A0D2C82C4993C13428E49D1673`；Setup 75328700 bytes / `55680AFCD55D8603E9E3443D23AFCD946DA3FE534BD81A47C1A84D5F6C8DB843`；manifest 869 bytes / `996C366D938E019984C7FDD569B2FAEE72C01A95CE5DFDD5D2397816B06C5FC0`；signature 384 bytes / `05C3A65FF4E8323D5F49857A7612DAF5AC644A93FA9397FFD54D64847BAB8076`。`PUBLIC_V105_RELEASE_ASSET_EQUIVALENCE = PASS`。

本轮未重新 build、生成或签名候选，未运行测试/build/Full，未访问正式 SQLite，未创建 S14-T02。`NO_FULL`。不得声称 `v1.0.4 -> v1.0.5 ONLINE UPDATE VERIFIED`；下一步只等待用户使用当前正式 v1.0.4 执行一次真实在线升级。


# 2026-09-09：S14-T01 候选技术接受，等待发布授权

S14-T01 = `CANDIDATE_TECHNICALLY_ACCEPTED / RELEASE_AUTHORIZATION_PENDING`；Stage14 保持 `IN_PROGRESS`。精确产品 source `cddd897d72c4e95c6fd974d5ea44bc24d9c985c7` 已以普通 fast-forward 纳入 `main`，实现远端分支 `origin/codex/s14-t01-terra` 继续固定在该 SHA，未 force push、squash 或重写产品提交。

冻结候选位于 `D:\S14-TestAssets\v1.0.5-final\9b969d3b-0e99-4793-bf51-2342530eada4`：ZIP 109411185 bytes / `543032FC76947772B4CAD3CC2416D611EC7C78A0D2C82C4993C13428E49D1673`；Setup 75328700 bytes / `55680AFCD55D8603E9E3443D23AFCD946DA3FE534BD81A47C1A84D5F6C8DB843`；manifest 869 bytes / `996C366D938E019984C7FDD569B2FAEE72C01A95CE5DFDD5D2397816B06C5FC0`；signature 384 bytes / `05C3A65FF4E8323D5F49857A7612DAF5AC644A93FA9397FFD54D64847BAB8076`。manifest 合同严格为 `1.0.4 -> 1.0.5`、source migration `9 -> 9`、target migrationCount `9`。

Sol 独立静态候选门禁通过。既有 `163/164` 唯一缺项保持 `S9_T07_REAL_OLD_PUBLISH = NOT_RUN / TEST_FIXTURE_UNAVAILABLE / NON_BLOCKER`；`NO_FULL`。本轮仅做 fast-forward 与 docs-only 治理收口，未重建候选、未重跑测试/build/Full、未访问正式数据，未 tag、Release、上传资产或创建 S14-T02。下一步只等待用户单独发布授权。


# 2026-09-08：Stage14 / S14-T01 简化在线更新治理冻结

上一轮 Stage14 治理提交 `3548c9cb239aeccf064e0003b079eaac22d198e8` 已普通 fast-forward push；本轮修正开工 fresh 现场为 `main HEAD == origin/main == 3548c9cb239aeccf064e0003b079eaac22d198e8`，ahead/behind `0/0`。本次 docs-only 修正也只允许普通 fast-forward push；push 后当前 `main == origin/main`、ahead/behind `0/0`，最终 SHA 以本轮 fresh Git 回执为准。GitHub latest stable 仍为 v1.0.4（Release ID `383891004`），远端仅保留 v1.0.4 tag；v1.0.5/v1.0.6 Release/tags 均不存在。v1.0.4 是唯一当前可信稳定产品基线。此前 `747fb69e316bbf3e428e6985edacec3cc8d8c65b` 仅为上一轮治理创建前的当时现场，不代表当前状态。

Stage14 = `IN_PROGRESS / GOVERNANCE_FROZEN / IMPLEMENTATION_NOT_AUTHORIZED`；S14-T01 = `GOVERNANCE_FROZEN / READY_FOR_IMPLEMENTATION_AUTHORIZATION / NOT_IMPLEMENTED`。

最小范围冻结为：主窗和托盘优先、现有 5 秒超时检查在启动后后台异步执行、UI 线程不等待更新任务、立即更新/稍后提醒及固定二次告知、托盘与每日提醒失败域拆分、安装器容忍 DisplayVersion/EXE 版本不一致。正式 `v1.0.4 -> 新 v1.0.5` 由安装目录中的旧 v1.0.4 Updater 执行，真正验收责任是新 v1.0.5 App 兼容旧启动方式并可靠显示主窗/托盘；包内新 Updater 无法反向修复该事务。新版 Updater 的 same-schema Loaded/ACK/Completed 收口只供 `v1.0.5 -> 后续版本` 使用并单独验收。禁止新升级状态机、强制状态/24小时宽限/AutoContinue/业务锁死/断网退出/路径图/自动连跳、新 migration/依赖/门禁；不得恢复或 cherry-pick 废弃 v1.0.5/v1.0.6。

主工作树原有 1 个修改测试文件与 4 个未跟踪用户文件均未触碰。治理已推送；未创建 Terra，未写生产代码，未 build/测试/Full，未访问正式数据，未生成候选或发布。当前仍在“可授权实施”停止点，但实施仍未授权。详见 `../STAGES/STAGE-14.md`、`../TASKS/S14-T01.md`、`../ANALYSIS/S14-T01-SIMPLIFIED-ONLINE-UPDATE-DESIGN.md`、`../ACCEPTANCE/S14-T01.md`。


# 2026-09-08：已回退至 v1.0.4，Stage13 收口

Stage13 = `CLOSED / SUPERSEDED_BY_PRODUCT_DECISION`；S13-T01 = `IMPLEMENTATION_ABANDONED / PRODUCT_TREE_ROLLED_BACK_TO_V104 / CLOSED`。

v1.0.4 重新认定为最后可信稳定基线。产品恢复提交 `8a0d7368d26a2dc35b50e842e0f1dfb32b119f13` 排除 `.ai-dev/**` 后与 v1.0.4 产品树无差异；v1.0.5/v1.0.6 历史提交保留，未 force push 或改写历史。
GitHub v1.0.5/v1.0.6 Release 与远端 tags 已删除；fresh 复核 latest 为 v1.0.4（Release ID `383891004`），v1.0.4 Release/tag 保留。
新版 v1.0.5 仅保留新的产品方向，未创建任务或开始实现：先启动主界面和托盘、后台异步检查、立即更新/稍后提醒、允许继续使用、取消全部强制升级状态机、托盘与每日提醒解耦、安装器兼容 DisplayVersion/EXE 版本不一致、正式在线升级验收后置到发布后。
`NO_FULL`；未运行测试/build，未访问正式 SQLite/安装根/数据根/备份。详见 `../ACCEPTANCE/S13-T01-ROLLBACK-TO-V104.md`。


# 2026-09-08: v1.0.6 released; waiting for user manual bridge acceptance

Stage13 = `IN_PROGRESS`; S13-T01 = `V106_RELEASED / WAITING_USER_MANUAL_BRIDGE_ACCEPTANCE / NOT_ACCEPTED`.

GitHub Release `v1.0.6` ID `384770577` is the current latest stable release (`draft=false`, `prerelease=false`). Its annotated tag peels to final product source `a8983ed6fe3d7f6a76bf2545e09b6e64d2d3a355`.
`PUBLIC_V106_RELEASE_ASSET_EQUIVALENCE = PASS`: fresh public downloads of ZIP, Setup, manifest and signature match the frozen corrected candidate SHA256 values.
v1.0.6 remains `HOTFIX / MANUAL_BRIDGE_RELEASE`. The v1.0.5 -> v1.0.6 online update is `NOT_VERIFIED` and is not a conclusion of this release; the next formal online-upgrade acceptance remains v1.0.6 -> v1.0.7.
The only remaining acceptance is user execution of the official v1.0.6 Setup over the existing v1.0.5 installation, without uninstall by default, followed by confirmation of the main UI, tray, original data and normal operation.
No production code or Release asset changed. No Full, 43/43, 8/8, Release build, EF or migration rerun.



# 2026-09-08: v1.0.6 product decision - manual bridge release candidate accepted

S13-T01 = `V106_MANUAL_BRIDGE_RELEASE_CANDIDATE_TECHNICALLY_ACCEPTED / RELEASE_AUTHORIZATION_PENDING / NOT_ACCEPTED`; Stage13 remains IN_PROGRESS.

The blocked official v1.0.4/v1.0.5 online transactions are no longer v1.0.6 release prerequisites and are not candidate failures. Each transaction is owned by the already-installed source Updater; the target candidate cannot back-fix the v1.0.5 Updater that commits and completes immediately after starting the normal application without waiting for readiness.
The corrected candidate and all frozen asset identities remain unchanged and statically accepted. v1.0.6 is now `HOTFIX / MANUAL_BRIDGE_RELEASE`: close v1.0.5, run the official v1.0.6 Setup over the detected installation, require existing preflight/fail-closed checks, preserve the separate DataRoot, then verify App UI, tray, data and normal use.
Do not claim `v1.0.5 -> v1.0.6 ONLINE UPDATE VERIFIED`. The next formal online-upgrade acceptance is deferred to `v1.0.6 -> v1.0.7`.
No production code, candidate bytes, build or test result changed; NO_FULL. No push/tag/Release. Release and user Setup installation acceptance are still required before S13-T01 or Stage13 can close.


# 2026-09-08: v1.0.6 corrected candidate frozen; official dual-source path blocked

Final product source remains `a8983ed6fe3d7f6a76bf2545e09b6e64d2d3a355`; no production code changed. The corrected candidate is frozen at `D:\\S13-TestAssets\\v1.0.6-corrected-final\\a8983ed6`: ZIP `63D0BC...FAD100`, Setup `65E268...FFA73`, manifest `6D6508...BF923`, signature `655584...709E4`. Contract is target 1.0.6, source versions 1.0.4..1.0.5, source migration 9..9, target migration count 9. Production RSA-PSS/SHA256, ZIP tree/path/prohibited-entry checks, versions and Updater GUI subsystem all PASS. The old candidate remains `REJECTED / SUPERSEDED_BY_CORRECTED_MIGRATION_CONTRACT`.

Official clients have no production transport to an unpublished candidate, and the official Updater requires the current user's formal HKCU/%LOCALAPPDATA% identities. TEMP/GUID entries are test mappings only. A fake Release is forbidden and Sandbox/VM/CA/proxy was not separately authorized, so both official-client transaction paths are NOT_RUN and the v1.0.5 normal-launch bug is not yet proven fixed. S13-T01 = `V106_CORRECTED_CANDIDATE_STATICALLY_ACCEPTED / OFFICIAL_DUAL_SOURCE_TRANSACTION_BLOCKED / RELEASE_AUTHORIZATION_NOT_REACHED / NOT_ACCEPTED`; NO_FULL; no push/tag/Release. See `../ACCEPTANCE/S13-T01-V106-CORRECTED-CANDIDATE.md`.

# 2026-09-08：v1.0.4 -> v1.0.5 真实升级失败，normal-launch hotfix 技术接受

真实升级回执：更新提示、下载、安装程序启动、安装完成均 PASS；安装后自动启动新版 FAIL，用户手动启动 v1.0.5 PASS。事故限定为 same-schema 成功路径在 `Committed` 后直接启动并标记 `Completed`，未复用既有 `NormalLaunchHandshake`。

Terra 在隔离分支提交 `a66db7e829b7508da439ef8f6a70d11954e574a8` 与安全补丁 `a8983ed6fe3d7f6a76bf2545e09b6e64d2d3a355`：same-schema 路径现执行 intent/token、进程身份、candidate tree、Loaded/ACK 与超时清理；只有完整绑定验证通过的 same-schema intent 才不作为 schema evidence，畸形、错绑或 schema-bound evidence 继续 fail-closed。未新增协议文件、字段、状态、migration 或依赖。

Sol 独立门禁：专项 `43/43`、相邻高风险 `8/8`；App/Updater Release build 均 `0 warning / 0 error`；EF `NO_MODEL_DRIFT`；migration 9；App/Updater PE subsystem 2；`git diff --check`、限定 secret scan、schema/migration 与禁改范围检查均 PASS。`NO_FULL`；全部动态证据仅使用隔离 worktree 与 TEMP/GUID，未访问正式安装或正式数据库。

S13-T01 = `REAL_V104_TO_V105_GUI_FAILED / NORMAL_LAUNCH_FIX_TECHNICALLY_ACCEPTED / V106_HOTFIX_CANDIDATE_PENDING / NOT_ACCEPTED`；Stage13 = IN_PROGRESS。未生成 v1.0.6 候选，未 push、tag、Release，未创建 S13-T02/Stage14；下一步须另获候选授权，最终仍由用户执行真实 v1.0.5 -> v1.0.6 GUI/升级验收。

# 2026-09-08：stable v1.0.5 已发布，等待用户 v1.0.4 -> v1.0.5 真实升级

stable v1.0.5 已发布：<https://github.com/CodeVoyage3/xiaoqipaichanuanjian/releases/tag/v1.0.5>。Release `384564898` 为 latest、非 draft、非 prerelease；annotated tag object `2ac5961be79796ee3c9da140ff13139ee1218730` 解引用到冻结产品 source `3b483ef772d6442febfbcdd16ab87c6965839445`，治理 HEAD 不替代产品 source。

`PUBLIC_V105_RELEASE_ASSET_EQUIVALENCE = PASS`：四项公开匿名下载 bytes/SHA256 与冻结候选全等；production RSA-PSS/SHA256 PASS，公钥指纹 `565956021399C88A8B13DD0873D2A801F6675EAB44BEB4FC8EBE53C71FEFBADC`；公开 ZIP 618 项、tree SHA256 `25FC29E568E14AFB2D02241D83D00B958D2F2971E93B5DFB70ADC5334DD38E07`、禁入/不安全路径/重复项均 0；App/Updater `1.0.5.0`，Updater subsystem 2，migration 9。v1.0.4 Release/四资产身份未改变。详见 `../ACCEPTANCE/S13-T01-RELEASE-RESULT.json`。

S13-T01 = `V105_RELEASED / WAITING_USER_V104_TO_V105_UPGRADE / NOT_ACCEPTED`；Stage13 = IN_PROGRESS；NO_FULL。下一步只由用户使用正式 v1.0.4 完成真实升级，确认 modal 阻断主界面、点击更新正常启动、无黑色控制台、自动重启到 v1.0.5、数据保持、再次启动仍为 v1.0.5。v1.0.4 的旧 UI 不要求验证“无稍后提醒”；v1.0.5 新强制升级策略留待其发起的后续正式升级自然验收。

# 2026-09-08 历史：v1.0.5 最终候选技术接受，等待发布授权

最终产品 source 为 `3b483ef772d6442febfbcdd16ab87c6965839445`；治理 HEAD 不得替代该身份。全新 v1.0.4-only 最终候选已保存到 `D:\S13-TestAssets\v1.0.5-final\3b483ef7`：ZIP 109428193 bytes / `D5C7D5DF10C9E3D900C3B064C6C79A5A090E6FC2CBC9D50C218BAB725FDBA53E`，Setup 75342417 bytes / `AC1DD32726C9B2D8EDD39CBB386FD24D3F32EAA4CC05435EA68860BB3740DDAC`，manifest 869 bytes / `BE364995F262701DDF8362B0B8BE2E3260F303FEF5D7D346B4A8659B0AF55F95`，signature 384 bytes / `978E0A66292DB9980AFC4D4F861CED15BF5738A6C4D8855DB364D711844217FC`。构建输出与持久副本逐字节一致。

manifest 为 stable `v1.0.5`，严格 `minVersion=maxVersion=1.0.4`，source migration min/max 均为第 9 条 `20260901155124_AddPolicyAndBaselineFoundation`，target migrations 共 9 条。production RSA-PSS/SHA256 PASS，公钥 SPKI SHA256 `565956021399C88A8B13DD0873D2A801F6675EAB44BEB4FC8EBE53C71FEFBADC`。App/Updater `1.0.5.0`、Updater subsystem 2、Setup `1.0.5`；ZIP 618 项逐项 identity 匹配、禁入项 0；Release build 0/0、EF NO_MODEL_DRIFT、migration 9、内置 TEMP/GUID smoke、diff-check、secret scan 均 PASS。

S13-T01 = `FINAL_CANDIDATE_TECHNICALLY_ACCEPTED / RELEASE_AUTHORIZATION_PENDING / NOT_ACCEPTED`；Stage13 = IN_PROGRESS；v1.0.5 = NOT_RELEASED。NO_FULL，未修改生产代码，未 tag/Release/upload。下一步只等待用户单独授权发布；公开资产等价复核后再由用户执行真实 v1.0.4 -> v1.0.5 GUI/升级验收。

# 2026-09-08：产品兼容基线收敛为仅 v1.0.4 -> v1.0.5

用户撤销 v1.0.2/v1.0.3 -> v1.0.5 的生产兼容要求；两版未形成需继续维护的门店升级基线，既有 TEMP/Sandbox/TEST_TRANSPORT 记录仅作历史环境证据，不记 compatibility FAIL，也不再投入代理、CA、Sandbox 或独立账户方案。v1.0.5 正式 manifest 固定 `minVersion=maxVersion=1.0.4`，source migration min/max 仍严格为 production migration 9，除非后续另有产品决策。

现有 source `3b483ef772d6442febfbcdd16ab87c6965839445` 的 1.0.2..1.0.4 compatibility manifest/signature 已被本产品决策取代，不得作为最终发布 manifest；其冻结 bytes 与证据保留。下一步只准备 v1.0.4-only 最终候选，再由用户验收真实 v1.0.4 -> v1.0.5 GUI/升级，发布仍需独立授权。S13-T01 = `IMPLEMENTED / SOL_TECHNICAL_ACCEPTANCE_READY / V104_ONLY_SOURCE_RANGE_DECIDED / FINAL_CANDIDATE_PENDING / NOT_ACCEPTED`；Stage13 = IN_PROGRESS；NO_FULL。

# 2026-09-08：v1.0.5 compatibility candidate 已重建并持久冻结

旧 TEMP candidate 已被系统清理，记为 `LOST_BY_TEMP_CLEANUP / SUPERSEDED`，不记安全或兼容失败。新 candidate 从生产 source `3b483ef772d6442febfbcdd16ab87c6965839445` 重建并保存于 `D:\S13-TestAssets\v1.0.5-compatibility\3b483ef7`：ZIP 109428212 bytes / `F27E6F14ADE71FE6BABB364EBF023DF29237F14D6FFF04BFF5E88128AED18224`，manifest 869 bytes / `56F84C0078691B7631195285AEDC30CC0C5D9DA5348128A565E9B201FDC33CEC`，signature 384 bytes / `0351C893B55A727A37E1FB899FD357123C381D9E98DA143DD7CBCECCCA5D561A`。production RSA-PSS/SHA256 PASS；App/Updater 1.0.5.0，Updater subsystem 2，migrationCount 9，source `1.0.2..1.0.4` 且 migration min/max 严格为现有第9条；构建输出与持久副本全等。

本轮无生产代码修改、NO_FULL、未发布/tag/Release、未继续代理或三条人工升级。S13-T01 = `IMPLEMENTED / SOL_TECHNICAL_ACCEPTANCE_READY / WAITING_TEST_TRANSPORT_ENTRY / NOT_ACCEPTED`；下一轮只处理 TEST_TRANSPORT_READY。

# 2026-09-07：S13-T01 已实现，Sol 技术验收就绪，三源发布阻断保留

隔离分支 `codex/s13-t01-terra` 当前 HEAD `3b483ef772d6442febfbcdd16ab87c6965839445`；S13-T01 = IMPLEMENTED / SOL_TECHNICAL_ACCEPTANCE_READY / NOT_ACCEPTED，Stage13 = IN_PROGRESS。未 push/tag/release，未创建 S13-T02。

Sol 已完成完整 diff 与独立门禁：S13 + S9-T03/T04/T06 主集合90/90、normal-launch safety 7/7，Release App/Updater 0/0，EF NO_MODEL_DRIFT，migration9，TEMP/GUID candidate App/Updater 1.0.5.0 且 PE subsystem2，diff/secret PASS；NO_FULL 保持。完整记录见 `../ACCEPTANCE/S13-T01-SOL.md`。

仍不得发布：1.0.2/1.0.3/1.0.4 公开原客户端/旧 Updater 到 production-signed v1.0.5 候选的全链尚未分别证明。现有旧专项仅覆盖 1.0.3→fixture1.0.4，不能泛化；后续需单独授权生成/签名候选并完成三源 source/migration/manifest/ZIP/maintenance/journal/ACK/normal/data/rollback 验证，才能裁决 manifest source range。

# 2026-09-07 历史：Stage13 / S13-T01 治理冻结，实施已授权

fresh fetch 已确认 `HEAD == origin/main == 13156822a0873c47c6bd24a3d3a4d75e40beb494`、ahead/behind `0/0`；GitHub latest 为 stable v1.0.4，产品 source c2b3f699408f422b3aeaf5b96321a6df08a038c5；Stage12/S12-T01 CLOSED，开工前无 Stage13/S13 文件。

已建立 Stage13、S13-T01 TASK/ANALYSIS/ACCEPTANCE。目标 v1.0.5；范围冻结为 durable 强制升级、24小时仅网络宽限、首次启动/回拨/损坏 fail-closed、严格安全错误分类、启动与6小时检查、modal/托盘无绕过、AutoContinue 和签名合法路径。Stage9 pending recovery/candidate ACK/rollback/maintenance/schema/Updater recovery 必须先于普通门禁，禁止改写其冻结语义。

v1.0.5 的 `1.0.2 / 1.0.3 / 1.0.4 -> 1.0.5` 是 RELEASE BLOCKER；当前三 tag 均 migration9 不是放宽依据，须分别用原客户端/Updater与 TEMP/GUID 合成数据证明协议、签名、包、maintenance、journal、ACK、recovery/rollback、数据保持。全部通过后才可考虑 source 1.0.2..1.0.4，否则保留/设计安全桥梁。

production migration=9、默认禁止新增；默认 NO_FULL，只跑 S13 新专项、focused、直接相关 S9-T03/T04/T06/upgrade-safety。仅触及任务卡列出的核心事务/恢复/Schema 或发生无法解释跨模块回归时由 Sol 重评最终一次 full。

用户已正式授权实施。先仅提交冻结治理，再从干净隔离 worktree 创建全新 Terra；Sol 不写生产代码。仍禁止访问正式数据库/安装根/数据根/备份、修改 v1.0.4 Release、push、tag、Release 或创建 S13-T02；既有 Stage4ViewModelTests 换行状态与未跟踪操作手册继续隔离。

# 2026-09-07 最终状态：v1.0.4 RELEASED，Stage12 CLOSED

- S12-T01 = RELEASED_AND_ACCEPTED / CLOSED
- Stage12 = CLOSED
- PUBLIC_RELEASE_ASSET_EQUIVALENCE = PASS（Sol 匿名 fresh 下载独立验收）
- stable/latest = v1.0.4；draft=false；prerelease=false；Release ID 383891004。
- 产品 tag v1.0.4 → c2b3f699408f422b3aeaf5b96321a6df08a038c5；后续治理提交不改变产品 tag。
- 四项原始冻结资产公开下载 bytes/SHA256 全等；生产公钥 RSA-PSS/SHA256 PASS；ZIP tree diff 0；App/Updater 1.0.4.0，Updater Windows GUI(2)，migrationCount 9。v1.0.3 公开资产身份保持不变。
- 正式结果证据：.ai-dev/ACCEPTANCE/S12-T01-RELEASE.md 和 S12-T01-RELEASE-RESULT.json。
- 发布链接：https://github.com/CodeVoyage3/xiaoqipaichanuanjian/releases/tag/v1.0.4

本轮未重建候选、未运行 full/178/已通过专项、未访问正式数据或执行正式在线升级。HISTORICAL_TEST_ISOLATION_UNCERTAINTY 保留，不能宣称正式数据受损或历史风险已解决。

v1.0.3 → v1.0.4 使用旧主程序和旧 Updater，不能验证新版 modal/无黑框，也不阻塞本次关闭。首次由 v1.0.4 发起后续真实升级时，再验证 modal、关闭恢复、无黑框、自动重启和原数据正常；通过后追加 REAL_V104_TO_NEXT_UPDATE_UX_VERIFIED。该事项已保留于 BACKLOG，不创建假更新、新版本或 Stage13。

以下全部为历史过程记录；其中 IN_PROGRESS、NOT_ACCEPTED、NOT_RELEASED、旧 GUI/在线升级关闭条件均不再代表当前状态，以本节和正式 Release 结果为准。

---

## 2026-09-07 正式发布裁决（覆盖此前关闭阻断口径）
用户已授权发布冻结source c2b3f699408f422b3aeaf5b96321a6df08a038c5与最终原资产。发布后PUBLIC_RELEASE_ASSET_EQUIVALENCE通过即可S12-T01=RELEASED_AND_ACCEPTED/CLOSED、Stage12=CLOSED。
v1.0.3→v1.0.4由v1.0.3主程序/旧Updater发起，不能验证v1.0.4新增modal/WinExe；这两项不阻塞本次发布或关闭。第一次由v1.0.4发起后续真实升级时再确认modal、关闭恢复、无黑框、自动重启和数据保持，可追加REAL_V104_TO_NEXT_UPDATE_UX_VERIFIED。不创建假更新或v1.0.5来验证，不创建Stage13。
历史PRE_RELEASE_UPDATE_ENTRY_UNAVAILABLE不再作为当前发布阻断。HISTORICAL_TEST_ISOLATION_UNCERTAINTY保持，不访问正式数据追查。下方旧的GUI/实际v103→104升级待验关闭条件由本裁决替代，历史技术证据不改写。
## 最终Candidate已生成并独立验收（2026-09-07）
Source c2b3f699408f422b3aeaf5b96321a6df08a038c5；PRE_RELEASE_CANDIDATE，资产目录TEMP/dbe9da86-4b5e-4944-a511-5113521b9ca0。Release build0/0，EF NO_MODEL_DRIFT，migration9；App/Updater/Setup1.0.4，Updater PE Windows GUI(2)，RSA-PSS/SHA256独立验签、ZIP618项树一致、secret scan、隔离启动通过。详见 .ai-dev/ACCEPTANCE/S12-T01-FINAL-CANDIDATE.md。
ZIP SHA256=4598C9A608B4B048A1E97BACD4D22C86E7C8DC4DB2637B527631DBB88EFA079D；manifest=A86C73692CC54128A486BF59156A7C0EAA0BBC07175BC47988E07C643D1B80D0；signature=25A302A014AFB3A2C9164E69D273D8B1D497C820982685D5EDE587606DC704D1。
当前最终GUI更新4项为PRE_RELEASE_UPDATE_ENTRY_UNAVAILABLE：production1.0.4高于latest1.0.3，既有adapter版本固定；未擅改接线，不用普通启动代替更新回执。保持S12-T01=IN_PROGRESS/NOT_ACCEPTED、Stage12=IN_PROGRESS、v1.0.4=NOT_RELEASED。HISTORICAL_TEST_ISOLATION_UNCERTAINTY保持。v1.0.3公开API资产身份与基线一致，未写入GitHub。无full/178/2项重跑。
## 2026-09-07 最终Candidate准备（最新裁决）
已批准热修source冻结：即时刷新/布局USER_GUI_ACCEPTED、取消更新文案、更新通知modal及导出Owner、Updater WinExe。仅4个生产文件，无安全状态机/业务/模型/依赖扩大。modal1/1与console2/2 Sol证据继续有效，本轮不重跑。
当前仅Release build、EF NO_MODEL_DRIFT/migration9、全新App/Updater publish/Setup/ZIP/manifest/signature、PE2、签名/tree/secret与隔离启动。禁止full/178/旧GUI重验/公开发布。完成后停止待最终GUI与发布后真实公开升级。HISTORICAL_TEST_ISOLATION_UNCERTAINTY保持，禁止探测正式数据。
S12-T01=IN_PROGRESS/NOT_ACCEPTED，Stage12=IN_PROGRESS，v1.0.4=NOT_RELEASED。旧candidate保持SUPERSEDED_BY_GLOBAL_MODAL_FIX。
## 2026-09-07 升级黑框最小修复专项通过
黑框源码根因为Updater控制台OutputType；唯一新增生产改动csproj Exe→WinExe。正式启动无shell包装，UseShellExecute=false与Updater自身WorkingDirectory均保持。生产publish与实际runner PE subsystem=2。
Terra2/2、Sol独立成功链+关键rollback2/2通过；关键错误留journal LastError，旧版ACK恢复正常。测试夹具证据不冒充正式GitHub升级。无full/178/modal/已通过两项重验；未改安全状态机/rollback/maintenance/ACK/schema。详细独立证据见本卡console验收记录。
本次按用户停止点不生成candidate；S12-T01仍IN_PROGRESS/NOT_ACCEPTED。旧candidate继续SUPERSEDED_BY_GLOBAL_MODAL_FIX。待后续统一生成一次新的最终候选。
## 2026-09-07 当前裁决：modal技术收口，调查升级黑框
Modal事实已接受：31自定义+3原生入口；唯一业务Window.Show为更新通知；其余已有modal；导出失败提示补Owner。本轮modal生产仅WpfDialogService.cs，无业务modeless，主壳Show保留。Terra安全宿主21/21、Sol独立1/1、Release build0/0通过，不full。
历史测试风险只记HISTORICAL_TEST_ISOLATION_UNCERTAINTY：不能证明正式数据被修改，也不能绝对证明从未访问。禁止为追查而读/查/hash/copy/对比/修改正式DB、数据根或备份。该finding属测试流程，不是产品defect，不单独阻塞发布。继续使用安全Dispatcher与TEMP/GUID隔离。
唯一当前产品问题：升级黑色控制台窗口。先查OutputType/ProcessStartInfo/包装/Console与journal，最小WinExe或必要显示参数；保护Updater自身WorkingDirectory及全部安全状态机/协议。只成功链+一个rollback+PE/启动链/日志targeted，不full/178/旧两项重验。
停止点：调查、最小修复及独立专项后回报，先不生成candidate。旧candidate保持SUPERSEDED_BY_GLOBAL_MODAL_FIX，资产不复用不发布。S12-T01=IN_PROGRESS/NOT_ACCEPTED。
## 2026-09-07 用户GUI回执与新增modal finding（当前裁决）
用户已通过首次导入不重启即时出现今日排查、确认窗底部按钮完整可见与提交正常。前轮证据继续有效，不重新验收/机械重跑178项。取消更新文案保留。
唯一新增范围：业务弹窗统一模态。先完整盘点Show/ShowDialog/Owner及async链，再标准WPF Owner+ShowDialog最小修复；不得全局替换、增加主窗禁用状态机/锁/WindowManager，不动Domain/DB/Import事务/任务算法/Updater核心。
仅modal targeted、直接dialog回归、Release build、diff/secret；模型未动沿用NO_MODEL_DRIFT/migration9。默认不full，不能因公共DialogService而扩测。
旧candidate source94d3efc及TEMP/7f1607db-db80-4979-a150-21405e45f298资产标记SUPERSEDED_BY_GLOBAL_MODAL_FIX；原字节与证据保留，不得用于发布。
本次停止点：盘点、最小修复与独立targeted报告后停止，先不生成新candidate。S12-T01与Stage12仍IN_PROGRESS/NOT_ACCEPTED；不发布、不创建Stage13。
## 2026-09-07 最小实施与独立源码门禁通过
Sol独立专项/必要邻近回归178/178，Release build 0 warning/0 error，EF NO_MODEL_DRIFT，migrationCount9，diff-check与限定secret scan通过。生产仅3个UI文件；未跑full，无扩大理由。
详见 .ai-dev/ACCEPTANCE/S12-T01-SOL.md。S12-T01仍IN_PROGRESS/NOT_ACCEPTED；候选publish/package/signature/hash与候选升级未完成，尚未准备用户GUI候选；正式发布和POST-RELEASE真实在线升级亦未进行。
## 2026-09-07 当前：Stage12 / S12-T01 实施中

Stage11 = CLOSED；v1.0.3 = stable/latest，source 651bd1074a6a95f9cfdad70a9e6e7df6ed0d6df7。
Stage12 = IN_PROGRESS；S12-T01 = IN_PROGRESS / NOT_ACCEPTED，目标v1.0.4。
用户已通过接任报告并授权全新Terra最小实施；专项先行，默认不跑full。范围和停止点见 TASKS/S12-T01.md。发布前候选验收与发布后正式在线升级分别记录。以下旧状态不代表当前状态。
# 最新交接

## 2026-09-07：S11-T01 与 Stage11 已按用户人工升级回执关闭

`S11-T01 = USER_MANUAL_UPGRADE_ACCEPTED / CLOSED`；`Stage11 = CLOSED / S11-T01_CLOSED / USER_MANUAL_UPGRADE_ACCEPTED`。

v1.0.3 已正式公开为 stable/latest：<https://github.com/CodeVoyage3/xiaoqipaichanuanjian/releases/tag/v1.0.3>。Release ID `383669847`，tag/source 为 `v1.0.3` → `651bd1074a6a95f9cfdad70a9e6e7df6ed0d6df7`。四资产已在 draft 阶段重新下载核对后公开：ZIP `109409826` bytes / SHA256 `725DBA97029DC9FC1B66CF4B8019144A66174FABC695439172B5E651F7F3D910`；Setup `75323323` bytes / SHA256 `B9FE900FE5A36166D11475C7C6333E4AA8F8DB4198D241A7C13F67438B5E6E14`；manifest `852` bytes / SHA256 `72DE8AC9E2062C1353A801C7071267EB735EF24E2E414C362CD07588588ACCD3`；signature `384` bytes / SHA256 `83D2B6DB71FCE793FC19DE8E3BA81E6601975D646D5C8F4099CC248C37DDA50F`。Manifest 仅允许 v1.0.2 → v1.0.3，生产 migrationCount 保持 9；v1.0.2 公开资产未覆盖或改写。

发布前 `PRE_RELEASE_PRODUCTION_EQUIVALENT`、最终 fresh full 1159/1159、Release build 0 warning/0 error、EF `NO_MODEL_DRIFT`、migration9、正式资产 RSA-PSS/SHA256 与 ZIP tree 独立复核均已通过。用户已回执重置生效、正式图标正常、首页统计条对齐正常、v1.0.3 可启动，并授权正式发布。

用户最终裁决：停止 S11-T01 后续所有自动化终验，不启动 Windows Sandbox，不再运行 full、focused/regression、build、publish、installer 或新升级 runner，不修改 production Updater，不增加 test adapter/验收基础设施。用户随后明确回执“人工升级成功”，确认本人 Windows 电脑上的实际 v1.0.2 → v1.0.3 升级体验通过；该回执以 `USER_MANUAL_UPGRADE_ACCEPTED` 记录并关闭 S11-T01/Stage11。此结论是用户人工验收，不写成自动化的 `REAL_GITHUB_V102_TO_V103_UPGRADE_VERIFIED`，不重新跑自动化。

## 2026-09-06 最终：S10-T01 与 Stage 10 已关闭

`S10-T01 = CLOSED`；`Stage10 = CLOSED`。v1.0.2 已作为首个正式对外版本发布：<https://github.com/CodeVoyage3/xiaoqipaichanuanjian/releases/tag/v1.0.2>。正式 tag/source 为 `v1.0.2` → `02ab6f291c7a52f9de05b19aa29ae9356dc9c676`，stable、非 draft、非 prerelease、latest，四资产实际匿名下载 size/SHA、production RSA-PSS/SHA256 验签及完整 ZIP 重验全部通过。

首轮发布前陈旧验收器 `VersionMismatch` 失败永久保留；当时未发布、未动旧版本。Terra 最小修复仅补齐 manifest 协议/source字段到验收对象，生产校验未改或放宽；Sol fresh 6/6 后先push同步，再从新source全新生成资产。最终四资产与清理证据见 `../ACCEPTANCE/S10-T01-RELEASE-RESULT.json`，完整结论见 `../ACCEPTANCE/S10-T01.md` 与 `../STAGES/STAGE-10-CLOSEOUT.md`。

v1.0.2 发布后匿名门禁全绿，才依序删除 v1.0.0、v1.0.1 Release；确认不存在后再删除其远端和本地 tags。最终匿名核对 releaseCount=1、tagCount=1、latest/tag均为v1.0.2；旧历史 commits 和S9证据保留。用户GUI回执仍与Sol自动化分开。未访问正式安装/数据/数据库，未创建Stage11。

## 2026-09-06 最终交接：S9-T07 与 Stage 9 已关闭

`S9-T07 = TECHNICALLY_ACCEPTED / CLOSED`；`Stage9 = CLOSED`。独立 Sol 最终完整 diff 与自动化验收为 `PASS`，没有 correctness/security/data-loss/process-lifecycle 阻断；无需新增人工 GUI 门禁。最终证据索引为 `../ACCEPTANCE/S9-T07-RESULT.json`，阶段总结为 `../STAGES/STAGE-9-CLOSEOUT.md`。下方所有 `IN_PROGRESS / NOT_ACCEPTED`、旧代理、施工与待验收内容均为历史记录。

可信边界不变：

> S9-T07 guarantees upgrade/rollback safety from a verified pre-upgrade trust boundary; it does not provide forensic detection of historical contamination already committed into the main database.

不得据此声称当前业务数据一定正确或能够识别所有历史篡改。生产 migration 仍为 9；未发布 v1.0.2，未创建 Stage10；14节点×3真实硬杀矩阵留在 backlog。S9-T07 实施与关闭提交 `0a5073563642c1cb74b9be5a9394036c5f81ee2c` 已普通 push 至 `origin/main`；其后核对 HEAD=origin/main=`0a5073563642c1cb74b9be5a9394036c5f81ee2c`、工作区 clean、ahead/behind `0/0`。本段之后仅允许提交本实际 Git 回执，不再改生产或测试。

## 2026-09-06 收口模式（当前裁决）

用户已明确将14节点×3真实硬杀耐久矩阵移入 `.ai-dev/BACKLOG.md`，不阻塞S9-T07/Stage9 CLOSED。保留全部确定性状态机/fault-injection覆盖；真实硬杀只做MigrationStarted后、MigrationApplied后ACK前、ACK后CandidateCommitted前、SnapshotRestore中、old app恢复后old ACK前，共5个边界各先1次。首失败或不稳才追加针对性重复，稳定通过不机械重复3次。

开发只跑S9-T07专项及必要S8/S9-T05/S9-T06回归；冻结后Sol最终只执行一次fresh无filter Release全量、build、EF/migration、secret scan、git门禁。仍为IN_PROGRESS / NOT_ACCEPTED，信任边界裁决、正式数据禁令和角色分离保持。旧文中的14×3及反复fresh全量要求由本节覆盖。

## S9-T07 当前实施进度（尚未验收）
最新唯一写者为 `/root/s9_t07_entry_terra`，正在完成实际App maintenance→签名Preparer→外部Updater成功链。已知新test seed预建data后未许可复用，首TRX实际30.838秒在seed第19行失败，非45秒install；另需签名包正确身份、所有TEST路径TEMP验证、Reminder实际启动后maintenance停写、等待Updater终态和全部actor清理。原 `s9_t07_race_terra` 已停止。平台 `/root/s9_t07_platform_sol` 已只读确认双PID normal竞态最新版本bounded静态闭合；最新race1/1 SHA `10DFCE1362478CDF5071C54F193B0E62F6F75D8644F4C897DD55E54D1869B6A6`，fake旧树fixture10/11 2/2 SHA `31CA3D3E89AE77AB822CB8269763B4987E18F0C9C926F4C6B13B605133CDD985`。实际old-prod9 rollback已1/1，仍不等于App maintenance链。完整失败/14节点hardkill×3、独立最终full gates、Terra commit-stop/Sol接受/普通push均未完成。NOT_ACCEPTED不变。
2026-09-06 最新：唯一写者`/root/s9_t07_real_terra`完成真实old-prod9 1.0.2/schema9→fixture1.0.3/schema10迁移后失败→恢复→真实old ACK/normal Loaded；含settings/BLOB/FK lifecycle history的新TRX `TEMP/386db299-6f0c-4e53-87d4-529bfbe10d71/real-old-final.trx` 根核1/1 SHA `4456BCE2AEED4BD16D2A2BA7057A8F45A13ACB25826FFBA639B607ED9BD54F35`。这不含App maintenance/签名Preparer实链，不含完整hard-kill。当前接续块：candidate-identity exact finally补齐并回归；实际外部Updater legacy10字段ACK control；双PID cleanup同步竞争反例。Sol独立只读，未fresh build/test。完整失败/14节点硬杀×3与最终全量门禁仍欠，NOT_ACCEPTED不变。
当前写者切换：`s9_t07_normal_terra` 已完成normal/legacy增量与marker1/1后停止；其后只修改fixture项目版本为1.0.3，未运行真实old回滚。新的唯一Terra写者 `s9_t07_real_terra` 接续一个实际old-prod9 rollback用例，Sol仍独立只读。最新10/10收据SHA `2E0FF19496E23155EC91873AF80C001B13041FCA171548998CD74A911809A5B7`，其后Updater观察marker1/1 SHA `CC21DF1657DFFEA70C96ECDCCB28608172151C99A682AB2C8F54A766D7655993`，不能交叉冒充覆盖。Sol最新指出cleanup需同时处理held spawned loser与已绑定intent权威PID不同的情形，已交新Terra最小修。真实old WPF、完整硬杀、最终独立门禁未完成，保持NOT_ACCEPTED。
接续最新：candidate committed 坏DB零normal启动负例已有实际TRX 1/1，协调者复核SHA `B35ABF7D4169B3568F6B9F70AAE535027C8F9563E8E7D786FB31D405BC04BB1A`；Sol静态确认当前确能到fresh migration门禁，但须增加确切错误断言。Sol另确认legacy10字段 health-ack 被新 HasSchemaEvidence误归为schema，影响Updater与Pending，需兼容修复和有效legacy对照。唯一Terra写者 `s9_t07_normal_terra` 正修normal grace身份记忆与异常后exact进程停止，并补真实重入反例，随后修legacy ACK。19个migration/ModelSnapshot文件再次核验0变化，生产migration9。状态仍 IN_PROGRESS / NOT_ACCEPTED；以下较早进度为历史增量。

最新接续：strict语义修复与新增有效基线反例已有70/70实施收据（`TEMP/s9t07-strict-final-3aefcaf3-a075-4aef-843f-144b29a0908f/S9T07StrictFinal.trx`，根复核SHA `AA6EE5058C6D158D269A8946846664941C55A4291B7538F020D5311834178475`）；normal身份/phase/root/exe、单次有界启动、fresh DB、old启动前logical FP等继续修复。仍欠三个新增真实反例：candidate已commit的坏DB必须零normal Start且不rollback、existingPending在途identity不能二启、old已Identified/Loaded合法数据变化后dead重入不能误强比原snapshot。当前唯一生产写者为 `/root/s9_t07_normal_terra`，只完成第一个新反例；此前 `/root/s9_t07_resume_terra` 已停止。`/root/s9_t07_resume_sol` 独立静态看normal重入/失败残留，不build。旧版真实WPF/完整失败与硬杀矩阵、最终独立full gates仍未完成。

用户“继续”后重新核验：HEAD=`d0112238bef66bef2c987f15ce1cba2b51ccb079`，fresh origin/main=`c4f7618c0dbdc0996ddfc183b9cb8e2cbf9d3803`，保留实施工作区；限定normal测试进程数0。先前工具host中断，旧代理均不在运行，接续唯一生产写者为 `/root/s9_t07_resume_terra`，独立只读 reviewer 为 `/root/s9_t07_resume_sol`。Terra完成尚欠strict五类真实入口反例及cleanup异常测试，Sol审阅normal编排；不并行build。

资源事件后的单case：normal已改单次Start后bounded等identity/Loaded；fixture normal为data-root mutex，测试以Start记录PID/start兜底finally。target10新GUID单case1/1，TRX `TEMP/s9t07-normal-one-03996e51-f76d-44a7-b644-1eb47d5896a0/S9T07NormalOneCase.trx` SHA256 `932C1CD83160276DD3B62224699C634CB332238EF19CCF0E2C6B12658D679BD2`，根实际核对通过及normal进程0残留。此前enum读取异常曾另留1个fixture PID41964，根核验exactTEMP路径/start后清理；该失败保留。单case通过不等于old/硬杀/整卡通过。

资源恢复事件：normal 初稿的 Pending 分支 Start 后立即递归、identity未落盘可重复启动；根已静态确认，随后测试发现 OOM/创建进程1455。根以仅含本轮新normal参数的CIM查询发现6个TEMP case共13个重复fixture，逐一核验完整exe路径+PID/start后停止，复核限定进程数0，default shell恢复。证据 `TEMP/s9t07-resource-recovery-be6df249-80f3-43bd-b288-80c05cd075e7/owned-before.json`。没有按进程名宽杀或访问正式DB；重复子进程已确认，但不声明OOM唯一根因。当前Terra先修单次Start后的bounded identity/Loaded等待，以及每case完整进程采集与finally清理；只准先跑一个新GUID normal case证明启动1/清理1，再恢复批量测试。此前normal2/2不得计作幂等或无泄漏证明。

最新接续：strict 初轮实施已有91/91合并回归（原始与失败TRX保存在 `TEMP/s9t07-protocol-receipt-21ef12cb-ad8a-4545-af84-9181881d705c/`），Sol 静态复核仍检出3个P0：Pending terminal语义旁路、OldCandidateHealthVerified缺少identity/ACK复核、snapshot metadata/实体检查晚于tree switch；另有restore quarantine重复键/enum契约问题。当前 Terra 已暂停 normal新推进，先修这5项。normal仅未验收初稿（shared intent/App loaded/Updater/fixture），candidate专项2/2不算本项完成，old/hardkill未做。下段较早的“strict仅helper”已由本段覆盖，不能据91项绿宣布接受。

用户下方信任边界裁决已生效，不再等待该产品裁决。治理提交 `d011223` 已在本地；生产/测试仍在工作区实施，未提交接受或 push。恢复意图、部分 staging 重入、持久替换、默认 EF Pooling=false、migration9 源结构/完整显式索引集合已有增量实现。上一 Terra 最新 focused 41/41 是实施收据，不是独立全量结论。

当前唯一生产写者为接续 `/root/s9_t07_protocol_terra`，此前 takeover/restore Terra 已停止。接管增量经独立 `/root/s9_t07_sol_current` 静态复审：在已裁决受控边界内未发现新的数据安全 P0，但还没有独立 fresh 实跑。最新接管实施收据46/46，协调者重新读取全执行通过，TRX `TEMP/s9-t07-takeover-9c277cad-7728-4317-b8fb-7bd41a6b570d/takeover.trx` SHA256 `B897CFE9589E9A29A2172FCE11F9D3BCA014763155F30CD97E0A0F2FA061F1B6`。严格 JSON/phase 当前只有 helper、Pending outer、authorization 初步接入与 phase-pair/old参数分流，尚未完整接入或测试，接续 Terra 正补齐；Sol 不并行 build。

剩余门禁包括：可信接管、commit/rollback 终态重验与正常启动顺序、严格嵌套 JSON/phase、实际生产 App/旧版 ACK、完整失败及真实硬杀矩阵、Terra 提交停止后 Sol fresh 完整验收。保持 `S9-T07 = IN_PROGRESS / NOT_ACCEPTED`、`Stage9 = IN_PROGRESS`。19个 migration/ModelSnapshot 文件已重新对照开工哈希全部一致，生产 migration 仍9，Domain/Migrations 无 diff。无正式数据访问、无 v1.0.2 发布、无 Stage10。

## 2026-09-05 用户裁决：解除暂停，继续实施

`S9-T07 = IN_PROGRESS / NOT_ACCEPTED`；`Stage9 = IN_PROGRESS / S9-T07_CURRENT`。用户已明确确认以下信任边界并要求继续实施，覆盖下方历史 PAUSED_PRODUCT_REVIEW；原独立验收与禁止扩范围门禁继续有效。恢复时重新 fetch，origin/main 仍为 c4f7618c0dbdc0996ddfc183b9cb8e2cbf9d3803。

本卡从本次升级事务开始前已建立并验证的可信状态开始负责：maintenance、所有本软件权威业务写停止、Reminder/background 等受控写入者停止、SQLite 连接正常关闭；快照前不得存在非空或来源不可证明的 WAL。WAL/SHM 不满足冻结规则即 fail-closed，禁止直接 checkpoint 未知 WAL 后继续；应尽可能取得数据库独占访问证明。快照继续验证完整 migration、integrity、FK、fingerprint 与 operation 身份。建立可信边界期间残留或新出现且来源不可证明的 WAL 必须拒绝，不能静默吸收。

不追溯在可信边界之前已经由 SQLite 合法回放并固化到主库的历史污染。不能解释为当前业务数据一定正确，不能声称检测所有历史外部篡改。未来可信基线/数据来源证明/审计链单独处理，不阻塞 Stage9，不在本卡创建后继任务。

准确能力表述：

> S9-T07 guarantees upgrade/rollback safety from a verified pre-upgrade trust boundary; it does not provide forensic detection of historical contamination already committed into the main database.

Terra 继续本卡生产实施和实施测试，提交后停止、不 push；Sol 独立完整 diff 与复验。未通过全部门禁前不关闭；不访问正式 DB，不创建真实 migration10，不发布 v1.0.2，不创建 Stage10。


## 2026-09-05 历史暂停（已由上方用户裁决解除）

`S9-T07 = PAUSED_PRODUCT_REVIEW / NOT_ACCEPTED`；`Stage9 = IN_PROGRESS / S9-T07_CURRENT / PAUSED_PRODUCT_REVIEW`。

全新 Terra 与独立 Sol 完成实施前源码审查，生产/测试零修改。Sol 用现有 Release 产物独立执行既有外来 WAL 单项，1/1 通过仅表示限制复现：12,392-byte 非空合法外来 WAL 被接受，integrity/FK/migration 健康，但业务指纹改变、provenanceProtected=false。这不是 S9-T07 防护通过，也不是本卡 fresh 全量或 build。

待裁决的具体边界：以本次可信正常会话维护停写、干净关闭后状态为升级保护起点；无法解释的残留/晚到 sidecar 阻断升级，失败必须恢复该起点完整快照；不承诺识别此前已经回放入主库的历史外来 WAL 污染。若要求覆盖该历史污染，需先定义首次读取前的持续来源认证契约，不能靠事后快照 metadata 证明。

依据 TASKS/S9-T07.md 第十二节停止门禁，对“可验证的一致性快照”的来源范围存在实质待决解释，当前不默默降低门禁。可审阅方案：ANALYSIS/S9-T07-SNAPSHOT-TRUST-BOUNDARY.md；独立证据：ACCEPTANCE/S9-T07-WAL-PREFLIGHT.json。

未访问正式安装/DB，未创建真实 migration10，未提交或 push 本轮治理，未发布 v1.0.2，未关闭 Stage9，未创建 Stage10。main=origin/main=c4f7618c0dbdc0996ddfc183b9cb8e2cbf9d3803；工作区只保留本轮治理文档。下方开工及旧记录是历史。


## 2026-09-05 当前：S9-T07 已获实施授权

用户本轮明确授权仅 S9-T07 实施、独立验收及全部通过后 Stage9 最终收口、普通 push main。fresh fetch 后本地 main 已从 54850b8 干净快进至 c4f7618c0dbdc0996ddfc183b9cb8e2cbf9d3803。

`S9-T07 = IN_PROGRESS / NOT_ACCEPTED`；`Stage9 = IN_PROGRESS / S9-T07_CURRENT`。全新 Terra medium/priority 实施并提交后停止、不 push；独立 Sol 审查和复验，不写生产代码。当前尚未完成本卡门禁，不提前关闭。

S9-T01～T06 及 Stage8 保持 CLOSED。S9-T06 Win11 成功是既有用户回执，A 保持 FROZEN_HISTORICAL_INTERMITTENT，Win10 NOT_VERIFIED。本轮匿名只读确认公开 latest=v1.0.1，两版 tag 与四资产元数据保持既有身份；未改公开资产。

只允许 TEMP/GUID 合成验证，不访问正式安装/DB；生产 migration 仍须 9，无真实 migration10/ModelSnapshot/业务 Schema 变化。不发布 v1.0.2，不创建 Stage10。细节见 TASKS/S9-T07.md 与 ACCEPTANCE/S9-T07.md。下方等待下一授权等表述为历史记录。


## 当前：S9-T06 CLOSED；真实 Win11 bridge 端到端验收通过

2026-09-05，用户在本话题明确回执：正式 v1.0.0 经 S9-T06 bridge 自动升级至公开 v1.0.1；自动重新打开并显示1.0.1；再次关闭、重新打开仍为1.0.1；原有数据正常。本次未出现“无法连接更新服务器”。这是用户真实Win11人工回执，非Sol重跑GUI或独立读取正式DB的结论。

- **S9-T06 = CLOSED**；最终人工门禁已满足，解除此前 NOT_ACCEPTED / USER_GUI_BLOCKED。用户无需再执行任何GUI测试。
- **B = ROOT_CAUSE_TECHNICALLY_CONFIRMED + REAL_WIN11_END_TO_END_VERIFIED**。真实验收范围是桥接不可变100→公开101；不冒称未公开102已在线安装。
- **A**：历史偶发GUI准备阶段连接失败，本次最终真实升级未复现；未独立确认根因，不再阻塞S9-T06。保持冻结，未宣称已修复。
- 本轮重新读取并验证既有fresh无filter Release **1056/1056**、零失败/跳过；build **0 warning / 0 error**；EF无漂移；migration **9**、末条固定；冻结对照9/9、rollback/recovery **40/40**、实际WPF事务4/4、PS5桥接边界6/6。与验收生产源8c6ebc0相比，当前代码/测试/构建未变，后续仅治理变更；本轮为证据复核，不冒称重新运行。
- 开工fetch确认main=origin/main=fe68adb24259a71072fe3b95f64f16977d4e01a6，clean、0/0。人工回执、门禁判据及证据hash见 `.ai-dev/ACCEPTANCE/S9-T06-WIN11-CLOSEOUT.json`。
- **Stage9 = IN_PROGRESS / S9-T06_CLOSED / WAITING_NEXT_AUTHORIZATION**，不自动关闭阶段、不创建S9-T07。不发布或创建v1.0.2 tag/Release；公开100/101字节和tag不变。Win10仍NOT_VERIFIED，不扩展本次Win11结论。

以下为历史记录；旧等待人工测试、未验收或A前置要求不再代表当前状态。

## 历史停止点（已被本轮收口规则取代）：GUI 1.0.2 诊断候选

S9-T06 `IN_PROGRESS / NOT_ACCEPTED / USER_GUI_BLOCKED`；Stage9仍 `IN_PROGRESS / S9-T06_CURRENT`。根因未确认，未宣布修复；Win10 NOT_VERIFIED，不创建S9-T07。

- 全新Terra实施并停止，Sol完整diff/独立复验；候选源 `e5ecc65762671f3a29cbbee589aead57714c7e63`，实际产品/FileVersion=1.0.2/1.0.2.0，未创建1.0.2 tag/Release/Setup。公开产品仍1.0.1，既有两版tag/资产不改写。
- 真实完整App启动→更新提示→实际“立即更新”→GitHub metadata/manifest/sig/ZIP→production验证Verified；ZIP107875191字节/SHA689F7A872ECE50F2177A6349EF6DEE9637A8AB655798899EFD0E3C76BDDDD169。仅TEMP/GUID合成数据，模拟source100→101，安装delegate硬关闭；用户包移除Updater目录。这是开发机诊断准备成功，不是原实机正式升级成功。
- GUI取消返回Cancelled并正常退出；关闭提示、Hide、Closed、CTS/check CTS和App退出事件可读。独立阶段/超时/取消/重定向/脱敏/reparse边界5/5；默认诊断关闭smoke exit0。合成DB integrity=ok/FK0/migration9，不是升级After证据。
- 最终fresh无filter Release1055/1055，failure/error/timeout/aborted/skipped0；build0/0，EF无漂移，migration9末条固定。第一轮1054/1055的启动源码静态契约失败保留；Terra显式分支修正后全量重跑通过。
- 诊断ZIP 71011157字节，SHA256 `529936669cf06ee274497e60dad359940dda8dad873517c7890c822b4d689f07`；423项扫描0已知secret；私钥头字面量仅为既有包验证器拦截规则，已独立辨明。无私钥读取、正式数据访问或Windows安全设置改变；EXE仍无Authenticode。
- 用户下一步：原失败标准Win11解压诊断ZIP，运行Start-GuiDiagnostic.cmd，确认实际102/source100/只准备横幅，点击一次立即更新，托盘退出后回传JSONL与结果。无需重复独立网络探针，不做After。具体清单与全部证据在S9-T06-GUI-SOL-VERIFY。

以下内容保留为历史阶段记录。

## 当前：实机独立核心Verified，转真实GUI差异诊断

用户原标准Win11 JSONL已独立读取，SHA256=345AC20CB228D01455CEDEAEEB56A820B9EFE199F7BB15C6F572C1F25936538D。原正式1.0.0 DLL SHA匹配、.NET10.0.10，Check/Refresh200，manifest852/sig384/ZIP107875191全部完整，Prepare=Verified，Updater未启动。四项代理环境变量不存在。此成功反证不支持继续将GitHub、DNS/TLS、CDN、冻结规则、签名、资产或开发机代理环境差异当根因。

状态保持IN_PROGRESS / NOT_ACCEPTED / USER_GUI_BLOCKED；不要求用户重跑独立网络探针/After。下一步仅真实WPF GUI诊断候选。原1.0.0/1.0.1公开资产与tag不可改，不创建S9-T07。

候选实施边界：全新Terra medium/priority，先在真实GUI调用点及原下载器内增加默认关闭的安全诊断；保留原handler结构、TLS/代理、超时、CTS行为，不推测性修复。记录请求阶段/状态/redirect及脱敏异常，GUI操作与CTS身份/取消来源/生命周期、single-flight、线程与Dispatcher、handler创建/参数和progress。无敏感URL query/token/私钥/路径/业务数据。独立TEMP/GUID验收。

可准备未公开1.0.2 GUI诊断候选，但不能把候选当前版本伪报1.0.0或允许真实降级安装。若需对既有1.0.1执行准备诊断，必须显式启用且失败即拒绝启动的隔离入口、清晰标注模拟source版本、硬禁止Updater与安装，不进入正式Release；正常生产入口保持真实版本规则。若最小实现需扩大范围，先报告，不自行引入迁移/业务/安装身份变化。Terra不发布tag/Release、不push；Sol验收治理后普通push。修复根因尚未确认，不称为已修复。

## 当前：正式在线升级人工阻塞，诊断中

- 用户报告标准Win11实机与Windows Sandbox均可运行正式1.0.0、发现GitHub1.0.1，但“立即更新”显示“正在准备更新包”约十几秒后报“无法连接更新服务器”。S9-T06现为 `IN_PROGRESS / NOT_ACCEPTED / USER_GUI_BLOCKED`；Stage9仍 `IN_PROGRESS / S9-T06_CURRENT`。此前本机自动化成功仅保留其隔离证据范围，不能覆盖此次人工失败。不要继续After，不关闭本卡，不创建S9-T07。
- 实机人工回执：正式Setup可安装、无需以管理员身份运行、桌面快捷方式及中文应用名正常、1.0.0正常启动。向导通用英文文本是未本地化，不是乱码，本轮非阻塞。Sandbox中文方框仅记录环境差异判断，不将实机升级失败归因于Sandbox。
- 用户保留Init/Before及运行、发现更新、连接失败、实机Setup截图；当前仅收到文字回执，未伪称已独立读取附件。完整正式GUI升级未成功，clean Win11为USER_GUI_BLOCKED，Win10仍NOT_VERIFIED。
- 本次fetch main=origin/main=b4a793fc79f4222ee75a2b73964fbfcb0744a725，clean/0/0。先只读审查生产下载链，增加仓库外/验收侧安全诊断，分辨RefreshRelease/Manifest/Signature/Redirect/Package及DNS/TLS/连接/timeout，不先猜根因。
- v1.0.0/v1.0.1及所有公开字节/tag保持不变；不删除失败证据，不先创建1.0.2、不先改生产代码。若证据确认需修复，另建全新Terra medium/priority实施；Sol仅治理/独立复验。修复版本仍同Schema9、不改业务/AppId/安装根/data root。正式新版本实机GUI成功后才恢复本卡验收。

## 当前停止点：两版已正式发布，等待独立 Win11 GUI 回执（2026-09-05）

- S9-T06 `IN_PROGRESS / NOT_ACCEPTED / USER_GUI_PENDING`；Stage9 `IN_PROGRESS / S9-T06_CURRENT`。此前无 clean OS 暂停已由用户独立 Win11 方案解除。Win10 `NOT_VERIFIED`，Win11 `USER_GUI_PENDING`；不关闭本卡/Stage9，不创建 S9-T07。以下早期暂停与预发布段落仅为历史，不代表当前状态。
- v1.0.0：https://github.com/CodeVoyage3/xiaoqipaichanuanjian/releases/tag/v1.0.0 ，tag/source `7044a984ddca757d8ae9350fbc523800bd769796`。v1.0.1：https://github.com/CodeVoyage3/xiaoqipaichanuanjian/releases/tag/v1.0.1 ，tag/source `99eb7510b3b1288e680551f92367e0ccc6c25755`。均 stable、非 draft/非 prerelease、4 项资产。代码版本提交 `81bc6703415c1186bc133432445f41371537628b`；当前产品1.0.1。不得改写 tag/替换同版本公开字节。
- Sol 独立匿名 repo/list/latest/两版 release/tag 接口 HTTP200，latest=v1.0.1；两版四资产实际匿名下载 size/SHA 匹配。production 公钥对两版原始 manifest 验签、完整 ZIP 重验、错误签名和 test-key 拒绝通过。所有 SHA/bytes/source 在 `S9-T06-RELEASE-RESULT.json` 与 `S9-T06-SOL-VERIFY/*release-evidence.json`。
- 真实 GitHub → 1.0.0 production checker/downloader → production 验签/完整包验证 → 独立 Updater → 真实1.0.1 candidate WPF ACK → Completed → 实际1.0.1 WPF重启及托盘退出通过。数据/程序均 TEMP/GUID；旧父进程是 Sol 验收宿主，Updater 启用隔离测试路径。此证据不替代用户正式 Setup 首装及点击“立即更新”的 GUI 链。
- 升级前后完整字段/BLOB fingerprint 均 `9f2e9ac0942675cff2e5f1b4fe5390a239f0f3c2919d7d3be9aa8d6aa3c581ce`，integrity=ok/FK0/migration9；原 Excel BLOB、设置、所有业务表、两份备份与创建 SHA 保持，Dashboard/Pending/Today/History/Reminder 权威读取通过。正常启动使 DB 文件原始 SHA 改变，完整逻辑字段/BLOB未变，未把文件字节相等冒充数据证据。
- 两版各自 fresh 无 filter Release 1050/1050，failure/error/timeout/aborted/skipped=0；两版 build0warning/0error，EF无漂移，9条migration，末条 `20260901155124_AddPolicyAndBaselineFoundation`。T04新鲜核心128/128、WPF12/12、退出1/1；真实版本受控切换失败回滚1/1（恢复1.0.0实际WPF和完整指纹）、Updater受控硬杀成功27/27、ACK回滚9/9、坏候选EXE回滚1/1；两版隔离TestMode安装器各5/5。它们不冒充干净OS正式GUI证据。
- RSA3072 公钥 SPKI SHA256：`565956021399C88A8B13DD0873D2A801F6675EAB44BEB4FC8EBE53C71FEFBADC`。私钥仓库外、非TEMP持久保存，DPAPI CurrentUser加密PKCS8+当前用户独占受保护ACL；绝对路径只在本机，不写治理/Release；没有独立恢复备份，依赖当前Windows用户DPAPI资料。客户端只有公钥。两版独立扫描各1227项，0已知secret命中。
- Setup/App/Updater Authenticode均NotSigned，未伪造自签Publisher；manifest RSA签名与Windows代码签名明确分开，SmartScreen行为待用户现场回执。
- 用户执行清单与采集器：`S9-T06-SOL-VERIFY/CLEAN-WIN11-README.md`、`Collect-CleanWin11.cmd/.ps1`。仅指定独立电脑、指定三行合成Excel；采集Before/After/Uninstalled关闭态数据副本，回传当前话题后由Sol独立核对。正式开发机数据/DB/backup未访问。
- 后续仅接收本卡人工回执、独立复核并判断是否可关闭。跨Schema migration保护与Stage9最终closeout仍待后续明确授权。

## S9-T06恢复执行（2026-09-05，覆盖下方历史暂停）

- 用户提供另一台独立Windows11 x64电脑并本人负责人工GUI验收；不要求Codex远控或Windows Sandbox。解除“完全无clean Windows环境”暂停。S9-T06 `IN_PROGRESS / NOT_ACCEPTED`；Stage9 `IN_PROGRESS / S9-T06_CURRENT`。clean Win11为USER_GUI_PENDING，Win10保持NOT_VERIFIED，不创建S9-T07。
- 恢复fetch：main=origin/main=f4e1fb62c293e1a228a28707c3536f911a02a33c，clean/0/0。生产签名、正式v1.0.0/v1.0.1 Release、真实匿名下载及自动化门禁继续执行。现有Git凭据仅在受控发布端内存调用GitHub，已只读确认目标repo public及admin/maintain/push=true，未输出/保存凭据。
- 用户独立Win11执行正式Setup首次当前用户安装、双快捷方式、自启动/重开、1.0.0显示、指定合成数据、发现1.0.1、立即更新/下载/验签/Updater/退出重启、1.0.1显示、数据设置历史保持、卸载保数据及Windows/SmartScreen实际行为；现场OS/build、无预装.NET状态仍需真实回执。
- Sol独立负责SHA/资产/manifest签名正负例/DB完整字段BLOB指纹/EF与migration/全量Release/build及失败回滚。用户GUI回执未返回前不关闭本卡；不能把自动化隔离证据冒充独立Win11真实GUI。最终给用户最精简清单和取证工具，禁止访问开发机正式数据库。
- 其余原Task安全边界、不可变发行、同Schema9、密钥保管和发布前门禁继续有效；此更新不授权Win10完成、Schema变化或S9-T07。

## S9-T06前置环境暂停（2026-09-05，当前权威停止点）

- S9-T06 `PAUSED_PRODUCT_REVIEW / NOT_ACCEPTED`；Stage9 `IN_PROGRESS / S9-T06_CURRENT / PAUSED_PRODUCT_REVIEW`。本卡已创建但未实施/未验收；Stage8及T01～T05保持CLOSED，不创建S9-T07。
- 精确阻塞：当前没有可执行的独立clean Windows环境。开发机为Windows11专业版23H2，10.0.22631.6199，x64；WindowsSandbox.exe不存在，未发现Get-VM/vmms或常见VM工具入口，用户明确回复“未安装过”。HypervisorPresent=true不证明可用clean OS；DISM功能状态查询需提升，未据此断言硬件不支持。未安装/启用VM、未重启系统、未关闭安全功能。
- 按用户第36节硬停止，在任何production key/版本变更/公开Release前暂停。正式身份安装器会预检固定数据根，不能在开发用户正式数据环境试装；未探测/访问正式安装或数据/DB/backup。
- 正式新Terra medium为`/root/s9_t06_fresh_terra_medium`，工具提供priority服务；只读审查后停止，零代码修改/零提交/零push。首次误用全历史fork的实例立即中断，不计正式实施者。治理角色独立核对了公钥未配置、安装器固定身份及硬编码1.0.0、migration源码9条。
- 新鲜匿名GitHub：repo200/public、releases200/0条、latest404；无Authorization、未下载生产资产。GitHub发布写权限和持久私钥保管尚未验证，不能将git fetch/push当Release权限证据。
- 产品仍1.0.0；无生产公钥fingerprint/私钥、无v1.0.0/v1.0.1 Release及asset SHA、无真实升级/DB指纹/回滚/clean Win10或Win11验收。Release1040/1040、build0/0、EF无漂移仍仅T05历史，本轮未重跑；文档diff检查通过。
- 已建立Task、Acceptance、Production Release Analysis和RELEASE-RESULT.json暂停证据。仅治理普通commit/push main，fetch确认clean/HEAD=origin/main/0/0后停止；最终SHA见Git回执。恢复前需用户提供或明确授权建立可用clean OS，再核实密钥与发布权限，不降低原门禁。

## S9-T06正式启动（2026-09-05，覆盖下方历史停止点）

- 用户正式授权首次production manifest签名、v1.0.0/v1.0.1正式GitHub Release及同Schema真实在线升级验收。Stage9 `IN_PROGRESS / S9-T06_CURRENT`；S9-T06 `IN_PROGRESS / NOT_ACCEPTED`。Stage8及T01～T05保持CLOSED，不创建T07。
- 新鲜fetch确认main=origin/main=fd541b88f071badd6a692373e82deaf6146c10ee、clean/0/0，原无T06，现已建立Task/Acceptance/Production Release Analysis。指定T05事务Analysis文件实际缺失，保留事实，不伪称已读。
- 治理角色只治理/完整diff/独立复验，新的Terra medium/priority实施并提交后停、不push。先核实持久私钥保管、发布权限、可用clean Windows与正式身份合成隔离；硬阻塞则PAUSED_PRODUCT_REVIEW / NOT_ACCEPTED，不创建公开资产。
- 产品当前仍1.0.0；未生成production key，未发布tag/Release/assets，未做真实升级，未访问正式数据。本卡新鲜Release/build/EF门禁尚未执行，1040/1040属于T05历史。
- 严禁Schema/migration10/Domain业务变化、正式门店数据、Reset/Undo、上报、强制静默升级、改AppId/安装根/数据根/lowest。详见S9-T06 Task与Acceptance；完成或暂停后记录真实证据，不自动关闭Stage9。

## S9-T05正式关闭（2026-09-05，覆盖下方历史停止点）

- S9-T05 `TECHNICALLY_ACCEPTED / CLOSED`；Stage9 `IN_PROGRESS / WAITING_NEXT_AUTHORIZATION`。开工 main=origin/main=`a8e1414030864c67e5cc82ee81c6062dc20c724d`、clean/0/0；fresh Terra 实施并提交、不 push，Sol 治理、完整 diff 与独立复验。产品仍 1.0.0，未创建 S9-T06。
- 已有独立 self-contained Updater、持久 journal、精确 operation 路径、完整树 staging/switch/rollback、父进程身份、mutex、候选只读 UI/DB ACK 与 manual recovery。正常流程只请求主程序优雅退出；ACK 前不执行 Initialize/Migrate/补算/reminder 写入。
- Sol 新鲜隔离证据：WPF self-contained smoke/verification exit0；preparer3/3、成功硬杀27/27、回滚硬杀9/9、失败矩阵8/8、live-parent及双Updater通过；migration9 DB SHA不变、无WAL/SHM、锁/ACL/junction无混树。
- 最终无过滤 Release 1040/1040、0 failure/skip、10m56s；build0/0；EF无漂移、migration9末条固定；diff/secret/禁止项通过。详见 `../ACCEPTANCE/S9-T05.md`、`../ACCEPTANCE/S9-T05-HARD-KILL-RESULT.json`、`../ACCEPTANCE/S9-T05-UPDATER-RESULT.json`。
- 未访问正式安装/数据/数据库/备份；未创建 GitHub Release/tag/asset，未配置生产 trust anchor/private key，未发布真实更新包，未执行 migration。开发机合成通过不代表门店投产。
- 下一卡仅建议 S9-T06 首次签名正式 Release 与干净 Win10/11 同 Schema 1.0.0→1.0.1 端到端升级验收；所有正式发布身份和版本变化须新授权，不含 Schema/migration、正式业务数据、重置或 Undo。

## S9-T04正式关闭（2026-09-05，当前权威状态）

- S9-T04 TECHNICALLY_ACCEPTED / CLOSED；Stage9 IN_PROGRESS / WAITING_NEXT_AUTHORIZATION。Stage8、S9-T01/T02/T03继续CLOSED，S9-T05未创建。开工c6200ca8507c7f8d99f7f40047b6be291d6ff70b与origin一致、clean/0/0。全新Terra medium/priority完成生产/测试4dbf088ff024a9818418e33bc67868dd0447b604并停止不push；Sol只治理、完整diff和独立复验。最终main以普通push/fetch回执为准。
- schemaVersion1、stable/win-x64、严格三段数值SemVer；raw manifest bytes RSA-PSS/SHA256，固定repo/Release/tag/asset身份与有限批准CDN跳转。流式TEMP/GUID ZIP下载，256MiB包/512MiB展开/4096条目等硬上限，size/hash/受限ZIP/EXE及DLL版本/目标migration声明验证后只返回VerifiedUpdatePackage。
- 生产trust anchor仍未配置并在网络前fail-closed；测试RSA只内存，无私钥/PAT/secret进入repo/publish。产品仍1.0.0。立即更新显示准备/下载/校验及进度、可取消，成功明确后续版本才启用安装；重复点击single-flight，正常退出等待取消清理。没有程序替换、Updater事务、候选执行、更新migration、正式Release/tag/asset、重置或Undo，正式数据根未访问。
- Sol最终Release核心128/128（真实合成HTTP含完整publish ZIP）、实际WPF12/12、退出子进程1/1、专项27/27；fresh无filter Release1023/1023，0failure/error/timeout/aborted/skip，10m45s；build0warning/0error，EF无漂移，migration9末条20260901155124_AddPolicyAndBaselineFoundation，diffcheck通过。生产代码与Schema/依赖/installer身份边界审查通过。
- 最终self-contained publish420文件164297044字节；合成旧客户端0.9.9→原版1.0.0完整ZIP88096081字节Verified，未执行候选。真实匿名repo/public、Release0/latest404，01:34:39生产客户端NoPublishedRelease；成功不是实际GitHub Release下载证据。证据见ACCEPTANCE/S9-T04.md、S9-T04-DOWNLOAD-RESULT.json、S9-T04-SOL-VERIFY及ANALYSIS/S9-T04-UPDATE-PACKAGE-PROTOCOL.md；失败历史保留并区分harness错误与生产缺口。
- 限制：正式发行需配置生产签名公钥/离线私钥保管，严格ZIP及CDN策略变化需复审；Verified TEMP未来消费前须重验。自动化不替代用户GUI/干净机器；全量空return不算高规模/真实Excel，离线restore不算在线漏洞审计；T02旧安装器历史产物，最终发行重建。既有Stage8物理介质/恢复边界不变。
- 下一步仅建议S9-T05独立Updater身份/journal/隔离staging程序切换与回滚，真实数据与跨版本动作必须先满足S9-T01保护/握手及新授权；没有创建Task或开始实施。普通push main、fetch核对clean/HEAD=origin/0/0后停止，等待用户明确授权。

## S9-T03历史关闭（2026-09-05，由上方T04覆盖）

- S9-T03 TECHNICALLY_ACCEPTED / CLOSED；Stage9 IN_PROGRESS / WAITING_NEXT_AUTHORIZATION。Stage8、T01/T02保持CLOSED；T04未创建。开工main=origin/main=82c3fd16423c9772e4c2f4f41a8b56cbbf67c669、clean/0/0。全新GPT-5.6 Terra medium/priority实现523918b及可读性修复15434e53a6809fd654337fee0332c851c238a922，已提交停止；Sol只治理、完整diff、独立复验。最终main见普通push/fetch回执。
- Sol独立协议40/40、最终实际WPF16/16、相关回归32/32；fresh无filter Release1017/1017、0failure/error/timeout/aborted/skip，约11m01s；build0warning/0error、EF无漂移、migration9末条20260901155124_AddPolicyAndBaselineFoundation、diffcheck通过。空return不当高规模/真实Excel证据；初次37/40和修复过程保留。
- 当前版本从正式程序集读取，仍1.0.0；匿名固定HTTPS latest元数据，严格稳定三段tag和数值比较，8种结果，5秒总超时/256KiB响应/1000字符纯文本。核心初次读取完成后每进程一次非阻塞检查；退出取消/晚到保护，只有新版提示，无轮询或持久snooze。稍后本进程不再提示，下次启动可再查；立即更新明确显示尚未启用，不下载、不退出、不替换。
- 真实匿名HTTP：2026-09-04 23:27:41～44 +08:00，repo200/public、list200/0、latest404；无Authorization。生产客户端23:44:06实际NoPublishedRelease。新版由合成协议/WPF验证，没有创建Release/tag/资产。private与无Release可能同为404，仍静默安全、不索要token。
- 最终fresh self-contained发布420文件/164235092字节；显式TEMP/GUID实际WPF核心启动exit0/ready1/程序树SHA256不变；合成DB副本integrity ok/FK0/migration9。smoke-exit分支跳过更新检查，更新链由独立协议、真实客户端网络、实际WPF及源码链分别证明。正式数据根未探测/访问/哈希/复制，无Schema/依赖/installer契约变化。
- 证据：ACCEPTANCE/S9-T03.md、S9-T03-UPDATE-CHECK-RESULT.json、S9-T03-GITHUB-SMOKE.json、S9-T03-SOL-VERIFY；契约见ANALYSIS/S9-T03-PUBLIC-RELEASE-CONTRACT.md。TEMP产物可能被清理。T02旧49MB安装器仅历史产物，不是本卡最新可交付版本，Stage9最终须重新构建。
- 剩余边界：当前没有真实新版Release/资产下载/Updater/程序替换/跨版本保护；开发机WPF不替代干净Win10/11门店GUI，未签名/SmartScreen及Stage8既有风险不变。无重置/Undo/secret。
- 下一步仅建议更新包下载与校验：先冻结manifest/原始字节RSA-PSS签名及公钥信任，再以合成资产验证隔离下载、大小/版本/平台/签名/SHA256及失败清理；不做Updater/替换/迁移/正式Release。未创建T04，等待用户新授权。普通push main、fetch确认clean/HEAD=origin/0/0后停止。

## Stage9 / S9-T02正式关闭（2026-09-04，历史回执）

- S9-T02 TECHNICALLY_ACCEPTED / CLOSED；Stage9 IN_PROGRESS / WAITING_NEXT_AUTHORIZATION；Stage8、T01保持CLOSED。T03未创建。开工main=origin/main=34c336a3f03e823048e5987d102001911527e5b2、clean/0/0；最终生产/测试5a8fe57d53697afcec066c8e70d63a346d45c5df，之后仅治理收口。最终main以本次普通push/fetch回执为准。
- 全新GPT-5.6 Terra medium/priority实施并已提交停止；Sol仅治理、完整diff、独立复验。Sol两轮真实独立身份A-I各9/9、preflight12/12；最终无filter Release996/996，0failure/error/timeout/aborted/skip、10m35s；build0warning/0error；EF无漂移，migration9末条20260901155124_AddPolicyAndBaselineFoundation。门禁空return不当高规模或真实Excel证据。
- 正式产物StoreExpiryInspector-Setup-1.0.0.exe，49,294,439bytes，SHA256 AE1608F57CA66BCA08FC5545DD7674E8F4057BD4F46420EE6390E3B227F8F258；本地TEMP/4b26880c-ba60-471c-8bde-2afd6401e5ee/production-final。Inno6.7.3，未签名；AppId={8F90E64E-5B0D-4FA8-A854-EEA2F4D1EC14}，后续1.x不变。lowest/current-user，固定%LOCALAPPDATA%/Programs/StoreExpiryInspector/app/StoreExpiryInspector.exe；完整420文件/164218708bytes self-contained payload，包审计无DB/secret/开发路径命中，二进制不入Git。
- 首装双快捷方式/Run on、同版本修复且尊重Run off、卸载全部数据保留、重装读取原合成BLOB/设置、数字降级阻断及旧/未知/坏Schema写入前阻断均实测通过。源DB/sidecar只读复制，SQLite仅检查临时副本，不调用Migrate/修复/业务写入。正式身份EXE从未执行，正式数据根从未探测/访问/哈希/复制；测试安装/Run/双快捷方式/卸载项清理，合成数据可恢复保留。
- 证据见ACCEPTANCE/S9-T02.md、S9-T02-INSTALLER-RESULT.json与S9-T02-SOL-VERIFY。历史返修/沙箱安装失败/NuGet缓存问题保留；后续已独立复验，不冒充原始候选通过。最终源码无业务算法/Schema/ModelSnapshot/index/生产PRAGMA/依赖变化。
- 开发机Win11静默矩阵不替代干净Win10/11或门店GUI；未解决SmartScreen信誉。Stage8合法外来WAL来源、严重坏当前无法保护时Restore阻断、真实物理介质安全限制不变。TEMP产物可能被系统清理。
- 下一步仅建议S9-T03公开版本元数据及离线友好检查/提示，明确无Release/无更新/网络失败；不自动建卡、不下载/替换/实现Updater，不创建正式Release。重置数据另需授权，Undo永久取消。按授权普通push main核对clean/0/0后停止。

## Stage9 / S9-T02正式启动（历史，已被上方关闭状态覆盖）

- 用户仅授权S9-T02当前用户首次安装器与数据保留安全门禁；Stage9 IN_PROGRESS / S9-T02_CURRENT，T02 IN_PROGRESS / NOT_ACCEPTED。开工fetch main=origin/main=34c336a3f03e823048e5987d102001911527e5b2、clean/0/0，无既有T02，现已建立Task/Acceptance。
- Sol仅治理/完整diff/独立复验；全新Terra medium/priority实施并提交后停、不push。仅官方Inno6、lowest当前用户、稳定AppId/Programs/app入口、首装Run on与重装尊重off、卸载保留全部数据、降级及非健康migration9只读阻断。全部测试严格TEMP/GUID独立身份，禁止探测正式库。
- 待实际安装器A-I矩阵、包安全、fresh Release/build/EF/migration门禁；尚未验收。Stage8及T01仍CLOSED。不创建T03、不实现Updater/Release/重置/Undo。验收后普通push main并停止。
- 实施中回执：治理a389cd4；Terra候选c4acd2f及返修b6b60d8/62ef4d3均未获验收，尚未完整执行A-I；Sol已退回同一Terra补齐。已有11/11仅实施者局部单测，不是安装矩阵或Sol全量证据。无push、无正式身份安装。官方Inno6.7.3已以签名Valid/Pyrsys B.V.的portable模式准备，详见Acceptance。

## Stage9 / S9-T01正式关闭（2026-09-04，覆盖下方开工状态）

- S9-T01 TECHNICALLY_ACCEPTED / CLOSED；Stage9 IN_PROGRESS / WAITING_NEXT_AUTHORIZATION；S9-T02未创建。开工main=origin/main=7c1fa2d4b0178314816e79663765f952c66d3095、clean/0/0；最终生产/测试0c6d0e4e37b3d18f3fdefa65c7c80f0108d41b11。最终治理提交后普通push main、fetch核对clean/0/0，实际SHA以发布回执为准。
- 全新GPT-5.6 Terra medium/priority实施e86d91c/f1eb6c5/abb31bb/0c6d0e4并已停止；Sol治理、完整diff审查和独立复验，未代写生产代码。Sol最终无filter Release991/991、0failure/error/timeout/aborted/skip、10m38s；build0warning/0error、EF无漂移、migration9末条20260901155124_AddPolicyAndBaselineFoundation；无业务/Schema/index/包变动。
- 总纲第2/3/37/38节和D-007明确2026-09-04决策覆盖旧在线升级禁令；首次当前用户安装EXE、后续GitHub在线检查+用户确认后自动升级，核心业务离线，无静默强制升级。Version=1.0.0，Assembly/File=1.0.0.0，UI读程序集；发布为win-x64 self-contained多文件，不trim。
- Sol最终发布164508896bytes（156.8879MiB）；移目录前后真实WPF双跑，两个独立TEMP/GUID库与程序树hash门禁通过。另关闭全局.NET搜索、指向不存在DOTNET_ROOT直接启动成功，hostfxr/CoreCLR实际从发布目录加载；三份合成DB各339968bytes、integrity ok/FK0/migration9。不是干净机器或用户GUI验收。
- 数据逻辑默认根仍%LOCALAPPDATA%\StoreExpiryInspector（data/app.db、backups/pre-import、logs；settings/runtime/原始Excel BLOB在库内），与拟安装Programs\StoreExpiryInspector\app解耦。正式根/库未探测/访问/哈希/复制。隔离参数仅接受本次全新TEMP/GUID，拒绝已有根、未知/缺失参数和ReparsePoint祖先；隔离自启动读写拒绝。
- GitHub匿名仓库200/public、Release列表200/0、latest404，无private token blocker，但未有实际Release资产下载。客户端严禁PAT/secret。Inno Setup lowest当前用户首装方案冻结，保留用户接受未签名EXE的历史决策；完整安装器/Updater/签名协议/跨版本回滚尚未实现。
- 现有Restore不能直接当跨版本回滚器；后续升级专用保护+staging迁移+独立Updater/journal/健康ACK+旧程序/旧DB回退契约见ANALYSIS/S9-T01-UPDATE-ARCHITECTURE.md。Stage8继续CLOSED：合法外来WAL来源未证明，严重坏当前无法保护时Restore阻断，真实断电/SSD/磁盘/文件系统/bit rot/不可读介质安全未证明。
- 首次NuGet TLS失败、两条旧静态门禁返修、一次S7T03异步超时和原样单项1/1后新鲜全量通过的过程均保留于Acceptance。10个显式空return不算高规模/真实Excel验证。证据索引S9-T01-RESULT.json；TEMP原件保留但可能被系统清理。
- 下一步仅建议S9-T02：当前用户Inno首装、稳定路径/AppId、快捷方式、自启动偏好、同版本重装/卸载保留合成数据；旧Schema无保护升级/降级必须阻断。未创建，等待用户新授权。Undo永久取消、重置数据另立需求。现在停止。

## Stage9 / S9-T01正式启动（2026-09-04，覆盖下方阶段停止点）

- 用户授权Stage9及仅S9-T01：产品基线修订、win-x64 self-contained发布/版本/数据路径底座、安装与在线升级架构。状态IN_PROGRESS / NOT_ACCEPTED，未创建S9-T02。Sol先fetch：main=origin/main=7c1fa2d4b0178314816e79663765f952c66d3095，clean、0/0；治理目录实查无既有Stage9文件。
- 总纲保留旧规则并注明被2026-09-04新决策覆盖：首次当前用户安装EXE；后续GitHub在线检查→提示→用户立即更新/稍后提醒→自动下载校验升级重启；核心业务完全离线，无后台静默强制升级。禁止PAT/secret客户端分发、总部管理、Undo、重置。
- 正式实施者为全新GPT-5.6 Terra medium `/root/s9_t01_terra_medium`，提交后停、不push；Sol不写生产代码，待完整diff和独立门禁后验收。首次误用全历史fork的agent已立即中断，未作为正式实施者。
- 所有发布/运行验证仅TEMP/GUID合成SQLite，不访问正式库、不用旧Junction脚本。原数据路径已在用户LocalAppData；需集中路径及隔离启动验证，默认路径保持不变。
- GitHub匿名HTTPS实际返回仓库200/private=false/public，Release列表200/0条、latest404；当前无private token blocker，未证明资产下载。后续外部发布不在本卡范围。
- Stage8继续CLOSED，不重开。合法外来WAL来源不可证明、严重坏当前无法保护时Restore阻断及物理介质风险未证明，全部保留。最终验收后普通push main并停止，下一卡另需用户授权。

## Stage8 / S8-T06正式关闭（2026-09-04，覆盖下方历史状态）

- Stage8及S8-T06 TECHNICALLY_ACCEPTED / CLOSED，T01～T06 CLOSED；Stage9 NOT_STARTED。开工fetch main=f981211、clean/0/0；最终测试候选17ebb6c，之后只有治理归档。全新Terra medium/priority文档31d3522/b8644ce，已停止；Sol完整审查、独立复验，无生产/测试代码变化。
- 新鲜100k/300k读20路径无异常，首屏193.33ms/深页367.69/History344.05/Reminder784.25（median）；100k Excel50191.79ms，完整业务断言/integrity ok/FK0；无数量级回退。写中及跨250post故障回滚2/2，恢复代表6/6，大库225.43MB/Backup5.83s/Restore20.55s。
- 独立18/18 Kill（9pre/9post）；最终无filter Release984/984、0failure/skip/error/aborted，内含42/42常规Kill（33pre/9post）、Revision16/16、S8T05 39/39；历史48/48独立保留。10个空门禁不当压力或真实Excel证据。build0/0、EF无漂移、migration9末条固定；禁止项及diff check通过。
- 未发现本卡生产一致性bug。合法外来WAL可改变业务而结构/FK/migration仍健康，来源防护未实现；严重坏当前不能保护时Restore阻断，不直接救援；物理断电/磁盘/SSD/文件系统/介质安全未证明。所有实验合成TEMP/GUID，正式库未访问，旧隔离事件不调查、不改写。
- 已建立STAGE-8-CLOSEOUT.md及S8-T06-RESULT.json。发布前fetch远端仍f981211；本治理提交后普通push main、再fetch核对clean/0/0，最终SHA见发布回执/回复。停止，不创建Stage9/升级/安装器/重置/Undo/新防护任务。

## S8-T05正式关闭（2026-09-04，覆盖下方历史状态）

- S8-T05 TECHNICALLY_ACCEPTED / CLOSED；最终代码/测试e0e5a0dc6eaba2d347246daa571a35cbbbcf3004。Stage8仍IN_PROGRESS，S8-T01～T05关闭，等待后续授权；不创建S8-T06。
- Sol最终无filter Release984/984、0failure/skip、exit0，23m36s；独立专项100/100；新鲜48/48硬Kill（排查15/库存9/10k导入18/100k导入6），36提交前回滚、12提交后保留，全部integrity ok/FK0/migration9，48原worker均退出。
- build0warning/0error、EF无漂移、migration9末条20260901155124_AddPolicyAndBaselineFoundation；完整diff和diff --check通过，无Schema/ModelSnapshot/index/生产PRAGMA/dependency/csproj/slnx变化。源码最终仅已有库预检、启动失败终止和Restore测试接缝；最后两个治理前修复为旧换行断言及marker共享冲突测试同步。
- 安全边界：坏备份拒绝；严重坏当前无法保护时Restore安全阻断、不覆盖。合法外来WAL可在结构健康时改变业务状态，来源防护未实现，作为用户接受的known limitation保留；不新增provenance/sidecar删除/自动恢复模式。
- 真实100k Batch/300k History，DB和backup225427456bytes。原backup7258ms/restore23089ms保留；最终新样本6946ms/22552ms，额外integrity/FK2275ms，完整指纹一致。均单样本非SLA。
- 旧982/983全量、WAL原4/5及marker失败均保留，不改写为通过。详见S8-T05 Acceptance与S8-T05-CORRUPTION-RESULT.json（原始大DB只留TEMP）。未访问正式数据库，不声称物理硬盘/SSD/文件系统/真实断电安全，不冒称真实GUI验证。
- 此治理提交后按授权普通push main并核对clean/0/0；最终main SHA以提交/推送回执与最终回复为准，验收后不改生产/测试。停止，不启动S8-T06/Stage9/在线升级/重置/Undo。

## 本轮恢复验收过程（历史）

- 用户正式接受结构合法外来WAL来源不可识别为已知能力限制，不新增防护；旧失败及业务漂移事实保留，不算防护成功。S8-T05 IN_PROGRESS / NOT_ACCEPTED，待最终HEAD新鲜无filter Release全量0failure后关闭。
- 当前最终候选e0e5a0dc6eaba2d347246daa571a35cbbbcf3004；ad7ec3b全量982/983因旧marker共享冲突失败，已仅修复测试同步并新增无数据库专项。Sol最终专项100/100；新的无filter全量启用既有100k硬杀，正在运行，不能预报通过。
- 已重新fetch，origin/main仍80b2c57；本地已知aa27b18及治理/测试返修均保留。接续本卡Terra仅调整测试契约并提交，Sol独立验收，不代写生产代码。禁止正式库访问及新Task/Schema/PRAGMA/防护模式。

## 历史停止点（裁决前）

- S8-T05 = PAUSED_PRODUCT_REVIEW / NOT_ACCEPTED。真实非空外来WAL导致商品指纹漂移，但SQLite integrity ok/FK0/migration9且Initialize接受。Sol独立WAL4+UI1：4/5，mismatched失败，证据见S8-T05 Acceptance；不得改断言冒充安全。
- 用户header坏库无法保护时安全阻断裁决已落实；不解决本次结构健康的外来WAL来源错配。需独立明确风险边界/防护契约，不自行删sidecar、拒绝正常WAL、改Schema或PRAGMA。
- 无filter Release983总计982通过1失败（旧UI换行断言）；仅输入换行正规化后的专项通过，但随后真实WAL失败，未再声称全量通过。build0/0、EF无漂移、migration9、禁止项diff无变化、diff --check通过。
- HEAD aa27b1838759a39a18c0d2147f2fec78a12eb602；已知origin/main80b2c57，9/0，未push。治理及S8T05/Stage4测试返修未提交且保留；无reset，无生产数据库访问。两名实施者已停止，Sol不代写生产代码。不创建S8-T06。

## S8-T05正式开工（2026-09-04）

- 当前S8-T05：IN_PROGRESS / NOT_ACCEPTED；Task/Acceptance已建。开工重新fetch main=origin/main=80b2c57f599fec736a0e191b13e8ae923810a633，clean、0/0。全新Terra `/root/s8_t05_fresh_terra` medium/priority实施中；中间提交6b5a726/2568037，未push；Sol独立Release新测试17/17仅局部覆盖，不是整卡验收。详见Acceptance中间记录，继续完成剩余矩阵。
- 用户裁决：严重坏当前库无法生成合格保护快照时必须安全阻断，不staging/replace、不删改坏文件、不损坏健康备份、不伪造成功记录；属于预期，不要求现有Restore救援无法保护的坏库。不得绕过保护或新增Restore模式。
- 其余隔离损坏/备份拒绝/健康恢复/失败回退/大库/Release门禁按S8-T05 Task。仅TEMP/GUID合成数据，严禁正式库访问。S8-T01～T04仍关闭；下方旧停止点为历史回执。
- 实施交接：旧`s8_t05_fresh_terra`因未完成矩阵及WAL证据缺陷已停止；全新`s8_t05_completion_terra` medium/priority接续同卡，保留旧commit/diff，未push。详见Acceptance开发记录。
- 不创建S8-T06，不实施Undo/重置数据/Stage9/在线升级。完成独立验收后才关闭、普通push main并停。

## S8-T04正式关闭（2026-09-04）

- S8-T04：`TECHNICALLY_ACCEPTED / CLOSED`。Stage8仍IN_PROGRESS，S8-T01～T04关闭，WAITING_NEXT_AUTHORIZATION；S8-T05未创建。
- 本轮恢复核对：origin/main2142cf7、本地治理bfa87d9、clean、ahead3/behind0；实现9eff272，代码/测试均未变化。旧本地main61a057e仅落后、无分叉，归档只作正常fast-forward。
- 用户仅为Release全量特别放行4类既有TEMP/GUID SQLite损坏回归。Sol无filter全量944/944、0failure/skip、exit0，12.1949分钟；PreImportSnapshotServiceTests 6/6、S7T02DatabaseRestoreTests 9/9、S7T03DatabaseBackupRestoreViewModelTests 30/30、ImportUndoEligibilityTests 42/42，全部87项真实通过。不是旧838/838过滤回执。
- 既有48/48 Process.Kill矩阵及TRX哈希保留不变：36次提交前完整回滚、12次提交后完整保留，全部integrity ok/FK0/migration9/可重开可写。本轮没有额外重跑21分钟的100k专项；标准全量中的常规用例按原实现执行。
- Sol新鲜Release build0warning/0error，EF无漂移；migration --no-connect仍9，末条20260901155124_AddPolicyAndBaselineFoundation。生产、Schema/ModelSnapshot/index/PRAGMA策略/dependency/csproj/slnx不变，diff --check通过；未做在线NuGet漏洞审计。
- 未访问、复制、哈希、损坏、恢复或调查正式库；损坏仅发生在获准既有测试的TEMP/GUID对象。不新增损坏场景、不恢复Undo规划。未确认生产一致性bug；开发I/O及其他失败历史继续保留，不以本轮通过改写其原因。
- 不证明真实断电、磁盘/SSD、文件系统或介质损坏安全。不实施重置数据、Stage9、在线升级或S8-T05。
- 代码9eff272；本文件所在提交为最终治理收口。发布方式为正常fast-forward本地main及普通push origin main，不force；发布后核对HEAD=origin/main、clean、0/0，最终SHA以Git回执为准。
- Acceptance：`.ai-dev/ACCEPTANCE/S8-T04.md`；48次逐项摘要：`S8-T04-CRASH-RESULT.json`。全量TRX位于TEMP/StoreExpiryInspectorS8T04Sol/bc9bacb459a9413abaf455c5d3dc2811，SHA256 8097D52F940C1CEEC09494707AA2E551DF4FC4D5796E338DEC61BB58084F59BE。
- 停止。S8-T05仅为建议方向，等待用户新授权；下方S8-T03为历史回执。

## S8-T03历史状态与停止点

- Stage8：`IN_PROGRESS / S8-T03_CLOSED / WAITING_NEXT_AUTHORIZATION`。S8-T01、S8-T02、S8-T03技术验收关闭；S8-T04～T06仅候选，未创建Task。Stage9、在线升级未开始。
- V1-F01、V1-F02、V1-F03、V1-F03-I04、V1-UI-01继续CLOSED；V1-UI-01保持GUI_ACCEPTANCE_PASSED。本卡没有启动真实GUI，不冒充新的GUI人工验收。
- 用户永久取消“撤销上一次Excel导入”。不新增Undo executor/UI，不验证或规划Undo eligibility，不用Restore冒充Undo。既有代码未顺带删除。
- 设置页“重置数据”仅为未来独立需求，未建Task/实施；以后另定清理范围、自动备份、二次确认与设置保留。
- 正式数据库禁止调查/读取/哈希/修复。本卡全部显式TEMP/GUID，未访问正式库；S8-T02旧轮疑似访问仍未核实，不借本卡通过改写历史事件。

## Git / 角色

- 开工重新fetch：HEAD=origin/main=`e4c628c0e2df261c2da5761b8398c2ecd919452c`，clean、0/0。推送前重新fetch仍同SHA，无远端新提交。
- 治理`8b59746`；基座`01efa1b`、测量`2149d63`；生产优化`617e3dd`、zero跟踪修正`20fc8df`；真实矩阵`6ed428d`；强断言/计量最终`31ace5b70ee03969c11213c4aeb623de7bd196fc`。
- 本卡全新Terra medium/priority实施；中间交付不完整曾退回及更换全新Terra，详细链见Acceptance。未复用旧卡Terra，全部已停止。Sol只写治理并独立审查/运行验收，未写生产代码。
- 工作分支`codex/s8-t03-import-stability`，按用户原授权普通`git push origin HEAD:main`归档，不force。最新归档SHA以包含本记录的提交/推送回执为准，代码验收基线为31ace5b。

## Sol独立验收（2026-09-04）

- 隔离9/9，默认factory=0；相关Import/隔离/本卡回归158/158（其中4高规模门禁空返回不算压测）。
- 实际10k/50k/100k压测3/3；最新回滚专项10/10，含真实100k Stage2完成后失败和跨250商品后置分组失败。
- Release全量925/925，0failure；Release build0warning/0error；EF无漂移；migration代码清单9，末条20260901155124_AddPolicyAndBaselineFoundation。设计时`:memory:`，migration list用--no-connect，不连接正式库；未做在线NuGet漏洞审计。
- 成功/失败integrity_check=ok、FK=0。失败前后完整业务/BLOB指纹一致；snapshot失败阻断写入，已生成快照可依旧契约残留但业务全回滚。
- 完整diff、Schema/ModelSnapshot/index/dependency/csproj/slnx与git diff --check通过。生产仅4个Import链文件，无业务算法或S8-T02读取改变。

## 性能与局限

- 单样本n=1，10k4467.94ms、50k23510.82ms、100k49210.72ms；100k Excel7255524bytes，DB逻辑45010944bytes，物理main+wal+shm91611720bytes。
- 10k Release Before313705.59ms；100k Before在planner发生too many SQL variables。优化后均成功。热身/运行顺序不同，不宣称固定倍率或SLA；未测50k Before。
- 最慢100k post20760.58ms，Batch Save11150.99ms；仍有逐商品查询/SaveChanges。100k execute-context SQL321895、SaveChanges15146，最大参数500；没有宣称N+1全部消除。结束时working-set约1.07GB，不是峰值。未观测OOM/timeout/lock/crash。
- 数据分布为每商品2批、10大类、最多1000既有商品、含库存0；scope先由seed建立，不宣称覆盖100k首次scope冷启动或所有倾斜分布。真实强杀/断电/损坏未做，未来仍需单卡授权。

## 证据与后续边界

- Task：`.ai-dev/TASKS/S8-T03.md`；详细命令、JSON路径/SHA、Before/After、失败矩阵与统计局限：`.ai-dev/ACCEPTANCE/S8-T03.md`；阶段：`.ai-dev/STAGES/STAGE-8.md`。
- 原始产物只在本机TEMP下`StoreExpiryInspectorS8T03/<GUID>`与`StoreExpiryInspectorS8T03Sol/<GUID>`，不包含正式数据，不上传大SQLite/XLSX。S8-T02归档事实仍见其Acceptance，不冒充本卡新鲜压测。
- 完成本卡普通push后停止；不得创建S8-T04或重置数据Task，不做强杀/断电/人工损坏、不启动Stage9或在线升级。下一卡需用户明确批准。

诊断进展：本机使用正式1.0.0 DLL/.NET10.0.10逐阶段复验，Refresh200、Manifest/Signature/Package均302→CDN200且原版冻结规则通过，最终Verified。本机继承代理环境变量，失败实机代理/传输结果未知；根因未建立，不能把Sandbox或网络笼统当原因。已准备安全独立诊断包，下一步仅收标准实机JSONL，不做After。详见ANALYSIS/S9-T06-NETWORK-BLOCKER.md及ACCEPTANCE/S9-T06-NETWORK-DIAG。未修改生产代码/版本/公开资产。
