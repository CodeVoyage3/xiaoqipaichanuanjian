# Stage 9｜安装、发布与在线升级交付

2026-09-04 用户正式授权启动。状态：IN_PROGRESS；仅 S9-T01 已创建。
开工重新 fetch：HEAD=origin/main=7c1fa2d4b0178314816e79663765f952c66d3095，clean、0/0；STAGES/TASKS/ACCEPTANCE 实际检查无既有 Stage 9 / S9 文件。

## 产品冻结

首次版本1.0.0，Windows 10/11 x64 当前用户安装EXE；后续在线检查GitHub→提示→用户立即更新→下载→校验→自动升级→重启，或稍后提醒。无提示后台强制升级不做。核心业务完全离线可用，只有版本检查、更新说明和更新包下载联网。
程序与业务数据分离；更新不得覆盖业务数据，卸载默认保留数据。禁止客户端PAT、长期secret、仓库写权限token。无总部管理、设备注册/上报、在线状态、远程强制更新。Import Undo永久取消，重置数据另行授权。

## 逐卡路线（仅方向，不代表已建Task或实施授权）

- S9-T01：需求冲突治理、发布/版本/数据路径基座，安装器与在线升级架构及失败恢复契约。
- 后续第一卡建议：当前用户首次安装器与首装/卸载数据保留验证，编号与精确边界待T01审查后确定。
- 再逐卡考虑公开Release元数据及检查提示；下载/校验；独立Updater与文件回滚；升级前保护/migration恢复；最终安装升级卸载矩阵与阶段closeout。不得一次创建全部卡。

## 治理与停止点

Sol仅治理/审查/独立复验，不写生产代码；本卡全新GPT-5.6 Terra medium/priority实施，提交后停止，不push、不建S9-T02。验收后Sol普通push main并停，等待用户下一步授权。
所有测试显式TEMP/GUID合成SQLite，严禁正式库读取、哈希、复制、恢复或其他访问。Schema/index/migration/业务规则不变，migration仍9，末条20260901155124_AddPolicyAndBaselineFoundation。

Stage8保持TECHNICALLY_ACCEPTED / CLOSED，原历史文件不重开。合法外来WAL来源不可证明，结构/FK/migration健康仍可能业务漂移；严重坏当前不能生成健康保护快照时Restore fail-closed，不强制覆盖救援；真实物理断电、SSD controller、磁盘/文件系统损坏、bit rot及不可读介质均未证明。
