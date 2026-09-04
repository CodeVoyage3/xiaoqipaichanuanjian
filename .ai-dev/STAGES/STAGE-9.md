# Stage 9｜安装、发布与在线升级交付

## S9-T03当前授权（2026-09-04，覆盖下方历史状态）

Stage9 IN_PROGRESS / S9-T03_CURRENT；S9-T03 IN_PROGRESS / NOT_ACCEPTED。开工fetch main=origin/main=82c3fd16423c9772e4c2f4f41a8b56cbbf67c669、clean/0/0，无既有T03，现已按用户授权创建Task/Acceptance。只做公开稳定元数据契约、匿名离线友好检查及轻量提示；全新Terra medium/priority实施，Sol治理/diff/独立复验。T01/T02和Stage8仍CLOSED；不创建T04、不下载/Updater/程序替换/正式Release/跨版本迁移/重置/Undo。正式数据禁止访问，版本仍1.0.0；S9-T02安装器仅历史产物，Stage9最终交付须重新构建。待独立门禁通过后普通push并停止。

## 2026-09-04 当前关闭状态

Stage9 IN_PROGRESS / WAITING_NEXT_AUTHORIZATION；S9-T02 TECHNICALLY_ACCEPTED / CLOSED。开工main=origin/main=34c336a3f03e823048e5987d102001911527e5b2、clean/0/0；全新Terra medium/priority实施提交后停，Sol治理/diff/独立两轮A-I各9/9、preflight12/12、Release996/996、build0/0、EF无漂移/migration9通过。真实Inno6.7.3当前用户安装器已生成；固定AppId、lowest、稳定路径、双快捷方式、首装Run on/重装off、卸载保全部数据/重装复用、降级和非健康migration9写前阻断已验收。正式数据及正式安装身份未访问/执行；未签名与干净机器GUI边界保留。详见S9-T02 Acceptance/INSTALLER-RESULT.json。T03未创建，Updater/正式Release/重置/Undo未实施；T01与Stage8仍CLOSED。

历史开工：2026-09-04 用户正式授权Stage9启动，当时状态IN_PROGRESS / S9-T01_CLOSED / WAITING_NEXT_AUTHORIZATION；仅S9-T01已创建，S9-T02未创建。现已由上方T02关闭状态覆盖。
开工重新 fetch：HEAD=origin/main=7c1fa2d4b0178314816e79663765f952c66d3095，clean、0/0；STAGES/TASKS/ACCEPTANCE 实际检查无既有 Stage 9 / S9 文件。

## 产品冻结

首次版本1.0.0，Windows 10/11 x64 当前用户安装EXE；后续在线检查GitHub→提示→用户立即更新→下载→校验→自动升级→重启，或稍后提醒。无提示后台强制升级不做。核心业务完全离线可用，只有版本检查、更新说明和更新包下载联网。
程序与业务数据分离；更新不得覆盖业务数据，卸载默认保留数据。禁止客户端PAT、长期secret、仓库写权限token。无总部管理、设备注册/上报、在线状态、远程强制更新。Import Undo永久取消，重置数据另行授权。

## 逐卡路线（仅方向，不代表已建Task或实施授权）

- S9-T01：TECHNICALLY_ACCEPTED / CLOSED；需求冲突治理、发布/版本/数据路径基座及安装升级架构完成。最终生产/测试0c6d0e4，Sol独立Release991/991、build0/0、EF无漂移/migration9、self-contained发布164508896bytes/移目录WPF双跑/隔离DB验证与本地runtime加载通过。详见Acceptance及RESULT.json。
- S9-T02：TECHNICALLY_ACCEPTED / CLOSED；Inno Setup当前用户首次安装器及固定Programs/app路径、AppId、双快捷方式、首装HKCU Run与重装关闭偏好、同版本修复、卸载保全部数据、重装复用、降级及旧/未知/坏Schema阻断完成。AppId={8F90E64E-5B0D-4FA8-A854-EEA2F4D1EC14}为后续1.x长期身份。
- S9-T03仅建议公开版本元数据与离线友好检查/提示，明确无Release/无更新/网络不可达，不下载或替换应用；未创建Task，需用户新授权。
- 再逐卡考虑公开Release元数据及检查提示；下载/校验；独立Updater与文件回滚；升级前保护/migration恢复；最终安装升级卸载矩阵与阶段closeout。不得一次创建全部卡。

## 治理与停止点

Sol仅治理/审查/独立复验，不写生产代码；T02全新GPT-5.6 Terra medium/priority实施后已提交停止、不push。验收后Sol普通push main并停，不建T03，等待用户下一步授权。
所有测试显式TEMP/GUID合成SQLite，严禁正式库读取、哈希、复制、恢复或其他访问。Schema/index/migration/业务规则不变，migration仍9，末条20260901155124_AddPolicyAndBaselineFoundation。

Stage8保持TECHNICALLY_ACCEPTED / CLOSED，原历史文件不重开。合法外来WAL来源不可证明，结构/FK/migration健康仍可能业务漂移；严重坏当前不能生成健康保护快照时Restore fail-closed，不强制覆盖救援；真实物理断电、SSD controller、磁盘/文件系统损坏、bit rot及不可读介质均未证明。

GitHub匿名API确认public，无private token blocker；Release=0，真实资产下载未验证。当前用户安装器已完成；独立Updater、签名/回滚/跨版本保护及干净机器矩阵仍是后续交付门禁。S9-T01未制作安装器，S9-T02已实际产出但未创建正式Release；版本1.0.0不代表公开发行已完成。
