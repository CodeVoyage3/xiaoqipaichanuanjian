# Stage 9｜安装、发布与在线升级交付

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

## 2026-09-04 S9-T02历史关闭状态

Stage9 IN_PROGRESS / WAITING_NEXT_AUTHORIZATION；S9-T02 TECHNICALLY_ACCEPTED / CLOSED。开工main=origin/main=34c336a3f03e823048e5987d102001911527e5b2、clean/0/0；全新Terra medium/priority实施提交后停，Sol治理/diff/独立两轮A-I各9/9、preflight12/12、Release996/996、build0/0、EF无漂移/migration9通过。真实Inno6.7.3当前用户安装器已生成；固定AppId、lowest、稳定路径、双快捷方式、首装Run on/重装off、卸载保全部数据/重装复用、降级和非健康migration9写前阻断已验收。正式数据及正式安装身份未访问/执行；未签名与干净机器GUI边界保留。详见S9-T02 Acceptance/INSTALLER-RESULT.json。T03未创建，Updater/正式Release/重置/Undo未实施；T01与Stage8仍CLOSED。

历史开工：2026-09-04 用户正式授权Stage9启动，当时状态IN_PROGRESS / S9-T01_CLOSED / WAITING_NEXT_AUTHORIZATION；仅S9-T01已创建，S9-T02未创建。现已由上方T03关闭状态覆盖。
开工重新 fetch：HEAD=origin/main=7c1fa2d4b0178314816e79663765f952c66d3095，clean、0/0；STAGES/TASKS/ACCEPTANCE 实际检查无既有 Stage 9 / S9 文件。

## 产品冻结

首次版本1.0.0，Windows 10/11 x64 当前用户安装EXE；后续在线检查GitHub→提示→用户立即更新→下载→校验→自动升级→重启，或稍后提醒。无提示后台强制升级不做。核心业务完全离线可用，只有版本检查、更新说明和更新包下载联网。
程序与业务数据分离；更新不得覆盖业务数据，卸载默认保留数据。禁止客户端PAT、长期secret、仓库写权限token。无总部管理、设备注册/上报、在线状态、远程强制更新。Import Undo永久取消，重置数据另行授权。

## 逐卡路线（仅方向，不代表已建Task或实施授权）

- S9-T01：TECHNICALLY_ACCEPTED / CLOSED；需求冲突治理、发布/版本/数据路径基座及安装升级架构完成。最终生产/测试0c6d0e4，Sol独立Release991/991、build0/0、EF无漂移/migration9、self-contained发布164508896bytes/移目录WPF双跑/隔离DB验证与本地runtime加载通过。详见Acceptance及RESULT.json。
- S9-T02：TECHNICALLY_ACCEPTED / CLOSED；Inno Setup当前用户首次安装器及固定Programs/app路径、AppId、双快捷方式、首装HKCU Run与重装关闭偏好、同版本修复、卸载保全部数据、重装复用、降级及旧/未知/坏Schema阻断完成。AppId={8F90E64E-5B0D-4FA8-A854-EEA2F4D1EC14}为后续1.x长期身份。
- S9-T03：TECHNICALLY_ACCEPTED / CLOSED；匿名版本元数据检查、离线友好提示与生命周期门禁完成，详见上方当前状态。
- 后续逐卡考虑下载/校验；独立Updater与文件回滚；升级前保护/migration恢复；首次正式Release、最终安装升级卸载矩阵与阶段closeout。不得一次创建全部卡。

## 治理与停止点

Sol仅治理/审查/独立复验，不写生产代码；T03全新GPT-5.6 Terra medium/priority实施后已提交停止、不push。验收后Sol普通push main并停，不建T04，等待用户下一步授权。
所有测试显式TEMP/GUID合成SQLite，严禁正式库读取、哈希、复制、恢复或其他访问。Schema/index/migration/业务规则不变，migration仍9，末条20260901155124_AddPolicyAndBaselineFoundation。

Stage8保持TECHNICALLY_ACCEPTED / CLOSED，原历史文件不重开。合法外来WAL来源不可证明，结构/FK/migration健康仍可能业务漂移；严重坏当前不能生成健康保护快照时Restore fail-closed，不强制覆盖救援；真实物理断电、SSD controller、磁盘/文件系统损坏、bit rot及不可读介质均未证明。

GitHub匿名API确认public，无private token blocker；Release=0，真实资产下载未验证。当前用户安装器已完成；独立Updater、签名/回滚/跨版本保护及干净机器矩阵仍是后续交付门禁。S9-T01未制作安装器，S9-T02已实际产出但未创建正式Release；版本1.0.0不代表公开发行已完成。
