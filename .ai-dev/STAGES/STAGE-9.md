# Stage 9｜安装、发布与在线升级交付

## 2026-09-04 S9-T02当前授权（覆盖下方历史状态）

IN_PROGRESS / S9-T02_CURRENT。用户仅批准T02，开工fetch main=origin/main=34c336a3f03e823048e5987d102001911527e5b2、clean/0/0，无既有T02后创建Task/Acceptance。全新Terra medium/priority实施提交后停，Sol仅治理/diff/独立复验。真实Inno6当前用户安装器及独立测试身份A-I矩阵、数据保留、自启动关闭偏好、降级和非健康migration9只读阻断均待验收。T03未创建，Updater/Release/重置/Undo禁止。T01与Stage8仍CLOSED。

2026-09-04 用户正式授权启动。状态：IN_PROGRESS / S9-T01_CLOSED / WAITING_NEXT_AUTHORIZATION；仅 S9-T01 已创建，S9-T02未创建。
开工重新 fetch：HEAD=origin/main=7c1fa2d4b0178314816e79663765f952c66d3095，clean、0/0；STAGES/TASKS/ACCEPTANCE 实际检查无既有 Stage 9 / S9 文件。

## 产品冻结

首次版本1.0.0，Windows 10/11 x64 当前用户安装EXE；后续在线检查GitHub→提示→用户立即更新→下载→校验→自动升级→重启，或稍后提醒。无提示后台强制升级不做。核心业务完全离线可用，只有版本检查、更新说明和更新包下载联网。
程序与业务数据分离；更新不得覆盖业务数据，卸载默认保留数据。禁止客户端PAT、长期secret、仓库写权限token。无总部管理、设备注册/上报、在线状态、远程强制更新。Import Undo永久取消，重置数据另行授权。

## 逐卡路线（仅方向，不代表已建Task或实施授权）

- S9-T01：TECHNICALLY_ACCEPTED / CLOSED；需求冲突治理、发布/版本/数据路径基座及安装升级架构完成。最终生产/测试0c6d0e4，Sol独立Release991/991、build0/0、EF无漂移/migration9、self-contained发布164508896bytes/移目录WPF双跑/隔离DB验证与本地runtime加载通过。详见Acceptance及RESULT.json。
- S9-T02仅建议：Inno Setup当前用户首次安装器；固定Programs/app目录与AppId、桌面/开始菜单快捷方式、首装默认HKCU Run且重装尊重关闭偏好、同版本重装、卸载默认保留合成业务数据、拒绝降级及无保护的旧Schema升级。只全新或兼容migration9数据，旧Schema保护另卡。不实现在线Updater，不触正式库。尚未创建，等待用户明确授权。
- 再逐卡考虑公开Release元数据及检查提示；下载/校验；独立Updater与文件回滚；升级前保护/migration恢复；最终安装升级卸载矩阵与阶段closeout。不得一次创建全部卡。

## 治理与停止点

Sol仅治理/审查/独立复验，不写生产代码；本卡全新GPT-5.6 Terra medium/priority实施，提交后停止，不push、不建S9-T02。验收后Sol普通push main并停，等待用户下一步授权。
所有测试显式TEMP/GUID合成SQLite，严禁正式库读取、哈希、复制、恢复或其他访问。Schema/index/migration/业务规则不变，migration仍9，末条20260901155124_AddPolicyAndBaselineFoundation。

Stage8保持TECHNICALLY_ACCEPTED / CLOSED，原历史文件不重开。合法外来WAL来源不可证明，结构/FK/migration健康仍可能业务漂移；严重坏当前不能生成健康保护快照时Restore fail-closed，不强制覆盖救援；真实物理断电、SSD controller、磁盘/文件系统损坏、bit rot及不可读介质均未证明。

GitHub匿名API确认public，无private token blocker；当前Release=0、latest404，真实资产下载未验证。当前用户安装与独立Updater架构可行但尚未实现，签名/回滚/跨版本保护及干净机器矩阵是后续交付门禁。S9-T01未制作最终安装器、未发布Release；版本1.0.0是发布基座元数据，不代表正式发行已完成。
