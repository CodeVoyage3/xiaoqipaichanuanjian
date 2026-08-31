# Stage 7 最终总验收｜本地安全备份、恢复与 WPF 闭环

日期：2026-08-31。结论：**通过，Stage 7 按最终批准范围正式完成并收口。** 本轮为 Sol 治理总验收，没有新 Task、没有新 Luna、没有生产代码修改或 GUI 启动。

## 授权范围与起点

- 执行依据：用户提交的 `D:/下载/Stage-7-最终收口执行单.md`，SHA-256 `4ABFF8A49480F85D1A01312544C0C7CFFF3B86B4FD112D063891D0FE8EF056EC`。
- 最终产品范围只有安全备份、安全恢复、WPF 备份/恢复用户闭环。**正式排查历史 / 结果 Excel 导出暂不实施，登记为 Deferred Feature，不是 Stage 7 缺陷或阻断项。**
- 总验收前：`master @ 11ae76054f90c27839c9fa4e442334c7f7feef6c`，工作区 clean，`git diff --check` 通过，应用进程 0。
- Stage 6 收口比较基线：`cf5670751f4c925c00cf2627744a86407c761791`。
- S7-T01/T02 原执行单存在于 `D:/下载/S7-T01.md`、`D:/下载/S7-T02.md`；仓库 `.ai-dev/TASKS` 没有这两张历史卡。S7-T03 仓库任务卡存在。三份正式验收记录全部存在；本轮不补造历史任务卡。
- 最近提交、任务目录与差异核对未发现 S7-T04、S8-T01、其他 Stage 8 Task、UI/UX 实施 Task 或未提交生产代码。

## 三卡真实提交链

| Task | 实现 / 修复 | 验收 / 归档 | 状态 |
| --- | --- | --- | --- |
| S7-T01 | `30cd485258dcc415f7896a2a89ce880369232760` | `0d03d72cee8e4fb5211991e02ebbffdaba1d49dd` | 已归档 |
| S7-T02 | 初始 `24936161a0417a1a41ad1027dbea9f95969975f0`；清理门禁修复 `fea023ca3fda983329c77856ef3700a71d50691b` | `ade169b40fdfd0a4a091da91711c029feace1229` | 已归档 |
| S7-T03 | `da8b43233fe965784701283a5ecf0839f72750ed`；卡内修复已包含在此提交，无独立后续生产修复提交 | 技术记录 `c65000b087d142d23ba98a3c74013f221fddc2ae`；最终用户验收归档 `11ae76054f90c27839c9fa4e442334c7f7feef6c` | 已归档 |

S7-T02 治理偏差保持真实：原 Sol 在派发 Luna 前先产生未提交实现；随后停止直接开发，全新 T02 Luna 接任现有工作区，独立审查、修正并承担最终实现责任，Sol 独立验收。不得描述为 Luna 从零完成全部代码；本轮没有 reset 或重做。S7-T03 按规则使用本卡全新 Luna（max），同卡修复均沿用该 Luna。

## 本轮重新执行的技术总验收

所有结果均来自本轮 Release 运行的 TRX，失败和跳过均为 0；不是复用三卡历史数字。每组实际 TestMethod.className 集合已与下面登记的精确类集合比较一致。

| 门禁 | 本轮结果 |
| --- | --- |
| S7-T01 `S7T01LocalDatabaseBackupTests` | 6/6 |
| T01 + `PreImportSnapshotServiceTests` 共享快照组合 | 12/12（包含 T01 的 6 项，不是额外 12 项） |
| S7-T02 `S7T02DatabaseRestoreTests` | 9/9 |
| S7-T03 `S7T03DatabaseBackupRestoreViewModelTests` | 30/30 |
| Stage 6 四类 | 52/52 |
| Stage 5 四类 | 51/51 |
| Stage 4 八类 | 179/179 |
| Stage 3 十类 | 170/170 |
| Release 全量 | 680/680，0 failed / 0 skipped |
| Release build | 0 warning / 0 error |
| EF `has-pending-model-changes` | 无模型漂移 |
| 仓库 migration | 8 条，与 Stage 6 相同 |
| 包依赖 / target framework | 相对 Stage 6 无变化 |
| Domain / Configuration / DbContext / migration diff | 相对 Stage 6 无变化 |
| `git diff --check` | 通过 |

Stage 3 精确类：ApplicationStartupCoordinatorTests、BatchCheckedZeroLifecycleUseCaseTests、ConfirmedImportLifecycleOrchestratorTests、ExpiryStageCalculatorTests、PostImportLifecycleUseCaseTests、ProductStockZeroLifecycleUseCaseTests、ProductTaskAggregationTests、S3T07CombinationEvidenceTests、S3T07StartupEvidenceTests、StartupRecalculationTests。

Stage 4 精确类：InspectionDraftUseCaseTests、InspectionSubmissionUseCaseTests、InspectionTaskQueryTests、ManualInventoryAdjustmentUseCaseTests、S4T06ImportViewModelTests、S4T07InspectionDetailViewModelTests、S4T08InspectionSubmissionViewModelTests、Stage4ViewModelTests。

Stage 5 精确类：InspectionHistoryQueryTests、InspectionHistoryEditUseCaseTests、S5T03InspectionHistoryViewModelTests、S5T04InspectionHistoryEditViewModelTests。

Stage 6 精确类：DailyReminderUseCaseTests、S6T02DailyReminderRuntimeTests、S6T03TrayAndReminderSchedulerTests、S6T04SettingsAndAutoStartTests。

类命名空间统一为 `StoreExpiryInspector.Tests`。每个类使用 `FullyQualifiedName~StoreExpiryInspector.Tests.<完整类名>.`，同组使用 `|`；以类名后的点限制到该类的方法，并复核 TRX 实际类集合。不使用笼统 Stage 前缀凑数，Stage 3 历史 184/184 不作本轮口径。

运行命令与证据：

```powershell
dotnet build StoreExpiryInspector.slnx -c Release --no-restore -p:NuGetAudit=false
dotnet test tests/StoreExpiryInspector.Tests/StoreExpiryInspector.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~StoreExpiryInspector.Tests.S7T01LocalDatabaseBackupTests.' --logger 'trx;LogFileName=s7t01.trx' --results-directory obj/Stage7Closeout
dotnet test tests/StoreExpiryInspector.Tests/StoreExpiryInspector.Tests.csproj -c Release --no-build --no-restore --logger 'trx;LogFileName=release-all.trx' --results-directory obj/Stage7Closeout
dotnet ef migrations has-pending-model-changes --project src/StoreExpiryInspector --configuration Release --no-build
dotnet ef migrations list --project src/StoreExpiryInspector --configuration Release --no-build --no-connect
git diff cf5670751f4c925c00cf2627744a86407c761791 HEAD -- '*.csproj' global.json '*packages.lock.json' '*Directory.Packages.props'
git diff cf5670751f4c925c00cf2627744a86407c761791 HEAD -- src/StoreExpiryInspector/Migrations src/StoreExpiryInspector/Domain src/StoreExpiryInspector/Infrastructure/Configurations src/StoreExpiryInspector/Infrastructure/StoreDbContext.cs
```

上述列出 T01 专项示例；全部分组的完整命令与实际结果保存在 `STAGE-7-TECHNICAL-RESULT.json`。本机原始 TRX、build、EF 及数据保护读数保存在 `obj/Stage7Closeout/`，不是正式运行目录。

### 工程差异与兼容性

- Stage 7 的 `.csproj` diff **并非为空**：T02 提交 `24936161a0417a1a41ad1027dbea9f95969975f0` 增加 `InternalsVisibleTo Include="StoreExpiryInspector.Tests"`，使既有测试访问内部恢复检查点。没有增加 PackageReference、依赖版本或 target framework。本轮如实区分工程元数据与包依赖。
- build 使用已接受的离线门禁；本轮没有执行在线 NuGet 漏洞审计，不宣称在线审计成功。
- EF `--no-connect` 只列出仓库 migration，不读取正式库。实际目录是 `src/StoreExpiryInspector/Migrations`；其 8 个 ID 在技术结果 JSON 中完整保留，相对 Stage 6 无 diff。
- 正式库 migration 兼容性由 Stage 6 已验证的 8 migration 基线、最近 S7-T03 正式库同 SHA-256 回执及仓库 migration 未变共同支持；**本轮没有成功连接正式库重新读取 migration**。

## 产品边界总审查

| 执行单核心项 | 现有权威与审查证据 | 结论 |
| --- | --- | --- |
| 1～3 一致备份、身份/完整性及失败保护 | T01 复用 PreImportSnapshotService 的 SQLite BackupDatabase；临时验证发布、JSON/BackupRecord、SHA/integrity/schema/migration；T01 与共享快照测试 | 成立 |
| 4～6 可信输入、恢复前保护与安全替换 | T02 DatabaseRestoreUseCase：runtime 声明、输入校验、pre-restore、staging、File.Replace | 成立 |
| 7～10 失败保持、回退、最终验证与清理 | T02 9 项覆盖原库保持、失败回退、critical、最终 SHA/integrity/migration、精确 sidecar/quarantine 清理；清理不全不返回成功 | 成立 |
| 11～14 WPF 创建/列表/选择/确认及不复制核心 | T03 Query/BackupRestoreViewModel/MainWindow 调用 T01/T02，当前管理集合、重验、显式确认；30 项专项与已归档用户 GUI | 成立 |
| 15～16 旧运行态与生命周期 | DatabaseRuntimeGate 等待活动操作、禁止新操作；沿用 Draft/Submission 门禁；scheduler 暂停，成功/critical 保持锁定、显式退出清理 Tray | 成立 |
| 17 用户 GUI 恢复 | A 精确字节恢复、重开 10:00、正式环境恢复回执和用户最终确认，见 S7-T03 | 已通过 |
| 18～19 业务与 schema/依赖边界 | Stage 3～6 权威回归全部通过；生产 diff 仅已验收备份/恢复与必要 UI/runtime 接线；无模型、migration 或包扩大 | 成立 |

手动备份成功登记既有 BackupRecord 属批准功能；恢复前保护不写该业务记录。共享快照仅扩展前缀/当前 migration 入参并提升 integrity 检查，既有导入语义未重写。UI 没有第二套备份复制、文件替换或完整性判断。

## 用户 GUI、正式数据保护与现场限制

- 用户本人已经完成 S7-T03 隔离验收；最后明确回复“通过，未再次启动软件”。本轮不重复 GUI，不创建隔离运行环境，不启动 WPF。
- 原始 `S7-T03-GUI-RESTORE-RESULT.json`：UTC `2026-08-31T07:07:29.3879991Z`，A SHA `d860efda5f051b1070f5e27cd594ad6fb13276a2336b7577a1e188bdd567f173`，保护备份 2 份；随后截图确认重开后提醒时间 10:00。
- 原始 `S7-T03-RESTORE-RESULT.json`：UTC `2026-08-31T07:09:38.4102733Z`，正式库 299008 bytes / SHA-256 `F3D423DF14B882D7BFE87780A81CF5879F074AF4880601CBEDB6B475A964F522`，进程 0，隔离目录和保护暂存已清理、原自启动状态已恢复。Finish 的成功路径包括正式 sidecar/staging 拒绝检查。
- 首轮 A 核验时当前库与 B 一致的经过、第二次明确确认 A 后通过、占位命令错误等均保留在 S7-T03 记录中；本轮不把首轮失败改写成通过，也不擅自归因绑定缺陷。

| 保护项目 | 本轮重新确认 | 最近已验收事实 / 限制 |
| --- | --- | --- |
| 应用进程 | 0 | 总验收未启动软件 |
| 正式库存在 / 大小 | 存在，299008 bytes；LastWriteTime 2026-08-28 12:06:27 | 与保护基线一致 |
| 正式库 SHA-256 | **本轮未重新确认**，Get-FileHash 遇既有装入点错误 | 以上 S7-T03 用户原始回执 SHA 与批准基线一致 |
| 正式库 migration / integrity | **本轮未重新确认**，不在异常路径上连接 SQLite | 历史已验证同哈希基线支持兼容性，仓库仍 8 migration |
| WAL / SHM / journal / staging / quarantine | 正式 data 目录枚举被 Junction 阻断，**本轮未重新确认完整无残留** | S7-T03 Finish 回执及实现检查支持最近已清理事实 |
| S7-T03 两个隔离旁置目录 | 工具 Test-Path 返回 false | 用户回执确认 IsolatedRuntimeRemoved / ProtectedStagingRemoved=true；不据工具视图宣称修复 Junction |
| 历史正式备份保留 | 没有移动、删除或覆盖；完整目录事实**本轮未重新确认** | 工具视图 backups 返回路径不存在；不据此推断用户备份丢失，原运行目录由已验收 Finish 整体恢复 |

本轮自动化使用隔离测试路径。BackupTests / RestoreTests 临时根目录下无子项，`obj/Stage7Closeout` 无数据库或 restore 临时文件。工作区保留的 JSON/TRX/截图是审计证据，不是待清理的 GUI 数据库。正式目录读取限制按执行单允许的历史证据方式登记，不尝试修复/绕过 Junction，不将不可读伪装成通过。

## Deferred Feature、后续顺序与最终结论

- 用户明确决定暂不实施“正式排查历史 / 结果 Excel 导出”。这是范围收敛，不是 Stage 7 缺陷；除非后续用户重新批准，任何后续 Stage 都不得自行补做。
- 不做今日待排查任务导出、Draft 导出或数据库原始表导出；不创建 S7-T04。
- Stage 7 已完成。交付 `../STAGES/STAGE-7-CLOSEOUT.md` 与 `../STAGES/UI-UX-REFRESH-HANDOFF.md`，仅作后续接任资料。
- 下一步先统一 UI/UX，**本轮不开始实施**。Stage 8 原规划稳定性/性能，必须等 Stage 7 收口、UI/UX 完成且用户单独批准后再进入；本轮不创建 S8-T01。
- 本轮只新增总验收、closeout、handoff、技术结果证据，并最小更新现有状态/决策/架构事实；不改三卡历史、生产代码、测试、schema、migration 或依赖，不创建/派发 Luna，无范围越界。

最终治理提交的完整 HEAD 在本轮最终回复及本机 `obj/Stage7Closeout/final-git.json` 登记；接任方以 `git log -1 --format=%H -- .ai-dev/ACCEPTANCE/STAGE-7.md` 定位该提交，再核对实时 HEAD。提交后要求 master / clean / diff-check 通过 / 应用进程 0，随后停止。
