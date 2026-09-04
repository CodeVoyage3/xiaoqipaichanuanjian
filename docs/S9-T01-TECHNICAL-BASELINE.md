# S9-T01 技术基线

本卡只建立发布、版本和隔离启动底座；不实现安装器、GitHub 更新检查、下载器、Updater 或跨版本迁移回滚。完整在线升级信任、Updater journal 与 migration 专用保护契约见 `.ai-dev/ANALYSIS/S9-T01-UPDATE-ARCHITECTURE.md`。

## 发布与版本

`StoreExpiryInspector.csproj` 冻结产品版本为 `1.0.0`，程序集和文件版本为 `1.0.0.0`。导航栏从入口程序集读取版本，不保留第二个硬编码版本。`Properties/PublishProfiles/WinX64.pubxml` 冻结 `win-x64`、self-contained、多文件、非单文件、非 trimming；因此店端不需预装 .NET，且 SQLite native/EF 不受单文件或 trimming 风险影响。普通 build/test 不强加 RID。

首次安装建议使用 Inno Setup 的 `PrivilegesRequired=lowest`，安装根为 `%LOCALAPPDATA%\Programs\StoreExpiryInspector\app`；业务数据根保持 `%LOCALAPPDATA%\StoreExpiryInspector`，两者不能互相包含。安装器、桌面/开始菜单快捷方式、首次自启动、卸载行为属于后续卡，且卸载不得删除数据根。

## 数据根与隔离启动

默认根不变：`%LOCALAPPDATA%\StoreExpiryInspector`。数据库为 `data\app.db`，其 WAL/SHM/journal 在同目录；backups 为 `backups`，导入前快照为 `backups\pre-import`，日志为 `logs`。设置、运行状态和原始 Excel BLOB 位于 SQLite；用户导出由用户指定外部位置。代码通过 `RuntimeDataRoot` 集中解析这些路径。

发布 smoke 只能以 `--data-root <TEMP 下 GUID 目录> --s9-t01-smoke-exit` 启动。相对路径、TEMP 外目录、嵌套目录和 ReparsePoint 都会在任何数据库初始化之前 fail-closed；不会回退到默认根。隔离启动使用独立 mutex，并拒绝读取或写入 HKCU Run。`tests/S9T01-PublishSmoke.ps1` 发布真实 WPF EXE，等到 Shell 初始化记录 `s9_t01_smoke_ready` 后退出，再将 publish 目录移位并重跑；它核验隔离 SQLite/log 和安装目录文件不变。

## GitHub 与后续门禁

Sol 本卡匿名 HTTPS 检查确认源码仓库为 public，但 Releases 当前为 0，故没有可验证的 latest asset。客户端不得携带 PAT 或其他长期凭据。后续公开 Release 更新须使用签名 manifest、受控发布私钥与内置公钥；SHA-256 只校验完整性，不替代发布者身份。用户已接受未签名 EXE 的 SmartScreen 未知发布者提示，商业 Authenticode 证书不是首装 blocker。下载、校验、替换与回滚没有在本卡实现。

当前 Restore 只接受与当前程序集一致的 migration，并且先保护健康库，不能用于跨版本升级回滚。后续 Updater 必须在 WPF 之外运行：先停止业务写入、用 SQLite 在线备份生成并验证升级专用保护、在 staging 验证迁移、持久 journal 记录替换、收到新版启动 ACK 后才提交；任何失败先确认新版退出，再恢复旧程序和保护库。真实断电、介质损坏与严重坏库不能生成保护快照的既有 fail-closed 边界保持不变。
