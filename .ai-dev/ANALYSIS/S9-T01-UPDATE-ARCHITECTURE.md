# S9-T01 安装与在线升级架构裁定

2026-09-04，Sol代码审查与官方资料研究。这里冻结后续实现必须满足的契约，不声称安装器、更新器或跨版本恢复已经实现/通过。源码基线7c1fa2d4b0178314816e79663765f952c66d3095；最终发布基座证据见Acceptance。

## 首装和目录

选用Inno Setup生成单个安装EXE；`PrivilegesRequired=lowest`，不提供all-users切换；当前用户Programs下固定安装根 `%LOCALAPPDATA%\Programs\StoreExpiryInspector`。程序子目录拟为`app`，桌面/开始菜单/HKCU Run指向稳定`app\StoreExpiryInspector.exe`。安装器仅拥有程序目录、当前用户快捷方式及本产品注册表值。固定AppId、卸载项、版本升级检测等由后续安装器卡明确和实测，不在本卡生成最终installer。

现有正式数据根 `%LOCALAPPDATA%\StoreExpiryInspector` 必须保持；与Programs安装树互不包含。首次安装不能携带开发app.db；卸载不可通配删除LocalAppData产品数据根。重装复用数据须先验证数据库版本兼容；拒绝旧安装器启动新Schema，不能静默降级。安装默认启用自启动只限真正首装，后续更新/重装应尊重用户关闭偏好，不每次覆盖HKCU Run。

发布为win-x64 self-contained、多文件EXE+DLL/runtime/native SQLite，不trim、不AOT、不强求单文件。运行时随包更新，因此.NET安全修复需后续发布新版包；S9-T01不升级任何NuGet依赖。Windows10/11实际支持版本、干净机器、普通用户、杀毒/SmartScreen、签名信任及长路径/中文路径矩阵须在最终交付卡验证，开发机启动不是实机矩阵。

## 实际数据和启动审计

仅从代码读取路径和契约，不探测正式目录/文件。

| 数据 | 当前持久化位置/来源 | 更新和卸载契约 |
|---|---|---|
| SQLite | 数据根`data\app.db`，DatabaseInitializer | 不作为程序包内容；保留 |
| WAL/SHM/journal | SQLite主库同目录同basename的sidecar | 不盲目删除/混配；须在已停止runtime的事务范围管理 |
| Backup | 数据根`backups`下独立.db与.metadata.json；BackupRecord在主库 | 保留，不以卸载清理 |
| PreImportSnapshot | 数据根`backups\pre-import` | 保留现有快照契约 |
| logs | 数据根`logs`；LocalFileLogger按日期和现有保留策略 | 不写安装目录 |
| settings | app_settings等既有主库表；ReminderMinuteOfDay、AutoStartEnabled字段 | 随数据库保护；实际自动启动状态另在HKCU |
| runtime state | app_states保存LastReminderDate/LastNormalRunDate；mutex、scheduler、维护gate是内存状态 | 数据随库保留；进程间升级需新增明确握手 |
| 原始Excel | ImportWorkbook.Content是主库BLOB；原文件名/hash也是表字段 | 保护完整DB即含留存原始Excel，不从历史来源路径重建 |
| 用户导出Excel | 用户明确选择的外部目标，临时文件在目标同目录 | 不归安装器/卸载所有 |
| Restore中间文件 | 主库同目录`.restore-{operationId}.tmp/.rollback/.failed`及隔离sidecar | 现有Restore契约，不伪装成程序升级机制 |
| 自启动 | HKCU\Software\Microsoft\Windows\CurrentVersion\Run，值StoreExpiryInspector，带引号EXE绝对路径 | 用户级；更新维持稳定路径，卸载只清自身值 |

当前App.OnStartup直接Initialize→EF Migrate→startup recalculation；无在线版本检查，无升级pending握手，无升级前保护编排。当前自启动设置按钮才读写注册表，不能仅凭AppSetting默认true就声称首次安装已注册。UI版本曾硬编码v1.0.0、工程缺显式版本，S9-T01负责统一程序集来源。

## GitHub更新源与信任

Sol无Authorization头的HTTPS GET实测：仓库API200/private=false/visibility=public；releases列表200、0条；releases/latest404。未使用Git凭据读API，无客户端token，没有private release token blocker；目前无实际Release资产，无法验证下载URL、SHA或店端网络，正式更新发布前为必过门禁。404无新版/尚未发布、超时、DNS/TLS、限流与断网只能影响更新功能，不阻断业务。

现有public源码仓库可作为发布源。若今后改private，应使用另一个只含可公开二进制/元数据的公开分发仓库（推荐），或安全的公开分发端，发布端凭据只在受控发布环境，客户端无长期secret。改变可见性、创建仓库、Release发布未在本卡授权。

后续协议基线：稳定通道只接受非draft/non-prerelease明确SemVer `MAJOR.MINOR.PATCH`，首次tag `v1.0.0`；tag/version/package内部版本必须相等，拒绝旧版本/重复版本/不支持协议或平台。不做字符串排序。联网有超时/取消、限流退避，缓存说明不得导致未校验包安装。

拟定资产：`StoreExpiryInspector-1.0.0-win-x64.zip`（只有app payload）、`StoreExpiryInspector-Setup-1.0.0.exe`（首装）、`update-manifest.json`及其分离签名。manifest schemaVersion=1，至少version/channel/rid、package文件名/长度/SHA256、目标migration ID列表、允许升级的源版本及源migration范围、最低Updater版本。说明取Release body，不执行HTML/命令。实际字段、签名格式和固定公钥指纹需后续协议卡通过正反例测试再最终落地；当前不伪造生产key或私钥。

SHA256只证明与所取元数据一致，不能单独证明发布者身份。冻结额外信任门禁：manifest使用离线保管/受控发布端私钥签名，客户端仅含公钥；具体采用.NET自带RSA-PSS/SHA256（原始manifest字节签名，避免重序列化歧义），私钥永不入repo/包/门店。Windows Authenticode另用于EXE发行身份与SmartScreen体验，不替代manifest信任链，证书采购/签名发布另需授权。

只访问固定HTTPS GitHub仓库/Release与其真实资产重定向目标；API返回的任意URL不能直接被当本机执行输入。下载字节上限、声明长度与实际长度、SHA256、manifest签名、版本和目标平台全部一致后才能解包。解包拒绝绝对路径、`..`、ADS、ReparsePoint/link、重复/大小写冲突条目及解压体积越界；包不能包含数据根、任意脚本或任意安装命令。对最终解包文件清单再次核验。

## 独立Updater事务

WPF发现新版→用户确认→下载与校验到用户TEMP本次GUID→进入已有维护gate，等待Import/正式提交/History编辑/草稿保存完成并停止Reminder→生成并验证升级前保护→启动独立Updater→主程序正常退出→Updater验证PID和启动时间、全部DB连接与文件占用释放→程序替换/数据库升级→验证新版→重启。

Updater不得从正在替换的app目录运行：使用同一用户可写的升级工作目录中的已验证独立副本；无服务、提权或系统计划任务。程序staging/old放在Programs安装根的同一卷受控子目录，跨目录改名不是整体原子操作，必须有持久化journal逐步记录与重启恢复，不能声称两次Move是原子更新。数据根`updates\<GUID>`持久保管journal和数据库保护（TEMP只作可丢弃下载缓存）。所有操作绑定固定产品/安装根/数据根/旧新版本/包hash/操作ID，拒绝客户端随意指定待覆盖目录或任意PID。

保留旧程序完整目录和已验证数据保护，直到新版启动握手完成；握手包含本次操作ID与进程身份、目标版本、migration与数据库健康、核心只读查询、UI可用，无业务写入开放前才提交升级成功。失败或超时须确认新版进程退出再恢复旧程序和升级前DB，验证后启动旧版；不让新旧进程同时写DB。仅凭Process.Start成功或等待固定秒数不算健康。

进程崩溃/用户重启可能落在每个journal阶段；下次入口必须先处理pending状态，不能先普通Initialize/Migrate/写业务后才恢复。具体互斥、ACK与启动恢复协议留Updater卡实现及故障注入；当前普通单实例mutex不是跨进程升级事务锁。

## migration与回滚（关键差异）

当前Restore要求目标migration与当前程序集一致，且先保护当前健康主库；因此不能直接拿它当跨版本/migration失败回滚器，也不能为了升级弱化Stage8的fail-closed。S9-T01不修改Restore或Migrate。

冻结后续升级专用契约：旧版停止业务写入后从健康源生成完整已验证保护（SQLite在线备份，不能在WAL活跃时裸复制主文件），记录旧版本/完整migration列表/hash/长度与操作ID。保护失败则取消升级，旧版继续运行。先在独立staging副本执行新migration和完整性/FK/目标migration/业务只读验证；不对原本唯一可用库试错。候选通过后，在持有升级独占和可恢复journal的状态下进行切换。

新版验证前不允许用户业务写入；失败仅恢复本次受控升级前保护，恢复后以旧版migration和只读业务验证确认可用。不得拿已迁移新库直接交给旧版，也不依赖EF Down逆迁移代替保护快照。首次空库/无源数据需单独分支契约，不能冒称已有保护。

如果切换后候选损坏，本次专用升级事务可以依据预先冻结的可信保护与journal恢复，不能泛化为“任意严重坏当前库强制Restore救援”。若保护/旧程序丢失、回滚文件操作失败或身份不一致，必须保留全部证据并阻止业务进入，明确需要人工处理；不伪造“永不失败”的物理介质保证。后续故障矩阵必须验证软件可控故障恢复旧版，且区分真实磁盘/文件系统不可用仍未证明的边界。

## 后续门禁和本卡边界

当前未实现：正式安装器、可下载Release、签名发布链、Updater及跨版本恢复、Windows干净机器/普通用户/卸载最终矩阵。这些是后续交付工作，不是Stage8未修生产blocker。不能用S9-T01发布基座通过宣称可向门店投放在线升级。
下一卡建议只做当前用户首次安装器：包装已验收publish、固定目录/AppId/版本、快捷方式、首装默认自启动且升级保留偏好、卸载默认保留合成数据、拒绝不兼容降级；只TEMP/GUID或另行批准的隔离用户环境。正式库迁移/安装验收必须另获用户授权。实际S9-T02不得自动创建。

## 官方资料（2026-09-04读取）

- .NET self-contained携带runtime，无需目标机预装.NET：[Microsoft部署说明](https://learn.microsoft.com/en-us/dotnet/core/deploying/)。
- Inno `lowest`不申请管理员权限：[PrivilegesRequired](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm)。
- 公共资源Release资产API可匿名访问：[GitHub Release Assets](https://docs.github.com/en/rest/releases/assets)；稳定latest定义：[GitHub Releases](https://docs.github.com/en/rest/releases/releases)。
- 生产migration必须控制执行时机/并发和回退：[EF Core applying migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying)。
