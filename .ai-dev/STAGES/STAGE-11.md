# Stage 11｜v1.0.3 产品细节与升级回归

## 当前状态

`Stage11 = CLOSED / S11-T01_CLOSED / USER_MANUAL_UPGRADE_ACCEPTED`。

Stage10 与 `S10-T01` 保持 `CLOSED`；v1.0.3 已于 2026-09-07 正式公开为 stable/latest，Release ID `383669847`，tag/source 为 `v1.0.3` → `651bd1074a6a95f9cfdad70a9e6e7df6ed0d6df7`。Stage11 当前只有一张已授权任务卡：`S11-T01｜图标、首页统计对齐、重置业务数据功能与 v1.0.3 升级回归`。

本阶段实现、发布前技术门禁和用户发布前 GUI 回执均已完成，v1.0.3 正式资产已经公开且匿名复核通过。用户先裁决停止后续自动化终验并采用 `USER_AUTHORIZED_MANUAL_UPGRADE_ACCEPTANCE`，随后于 2026-09-07 明确回执“人工升级成功”，确认本人 Windows 电脑上的实际 v1.0.2 → v1.0.3 升级体验通过；据此以 `USER_MANUAL_UPGRADE_ACCEPTED` 关闭 S11-T01 与 Stage11。该回执是用户人工验收，不写成自动化的 `REAL_GITHUB_V102_TO_V103_UPGRADE_VERIFIED`，也不补跑自动化。完整范围见 `../TASKS/S11-T01.md`，发布前技术记录见 `../ACCEPTANCE/S11-T01-PRE-RELEASE.md`。

## 阶段目标

- 用同一产品图标覆盖 EXE、WPF 标题栏、任务栏、桌面与开始菜单快捷方式、安装器和 Windows 卸载项；托盘图标可在不扩大范围时一并统一。
- 根治首页统计条在空数据和有数据状态下的 baseline、行高与垂直居中问题，保留现有业务颜色语义。
- 提供只清除业务数据的高风险重置功能；成功后回到“暂无导入数据”并可重新导入，同时保留安装身份、版本、安装目录、通用设置和重置前保护性备份。
- 从公开 v1.0.2 经真实检查更新、下载、切换、重开到未公开 v1.0.3 候选，验证数据保持和本阶段新功能，不改写任何 v1.0.2 Release/tag/资产字节。

## 硬边界

- 不改变业务、导入或提醒规则；不做大 UI 重构。
- 不新增 migration、Schema、索引或 ModelSnapshot 变化；生产 `migrationCount=9`。
- 不设计新的更新回滚协议；只复用并回归既有签名、下载、Updater、ACK、切换和恢复链。
- 不新增 Win10 专项门禁；本卡平台回归按既有 Win11 发行链执行。
- 自动化与安装/升级验证只允许 TEMP/GUID 合成 app/data/updater/install roots；禁止探测、读取、复制、哈希或修改正式数据库、正式数据根、正式安装根及其备份。
- Sol 只做治理、完整 diff 审查和独立验收，不写生产代码；生产代码、测试、图标资产和候选产物由全新 Terra 实施。
- 全部自动与人工门禁通过前维持 `IN_PROGRESS / NOT_ACCEPTED`；不得关闭 Stage11，不得发布 v1.0.3，不得创建后继卡或后继阶段。
