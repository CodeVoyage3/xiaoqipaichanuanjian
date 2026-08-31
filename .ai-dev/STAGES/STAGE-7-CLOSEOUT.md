# Stage 7 Closeout｜本地安全备份、恢复与 WPF 闭环

归档日期：2026-08-31。Stage 7 已按用户最终范围完成，技术与已归档用户 GUI 验收通过。完整提交链、本轮结果及数据读取限制见 `../ACCEPTANCE/STAGE-7.md`。

## 已交付范围

- S7-T01：一致、安全、带身份和完整性核验的本地 SQLite 备份。
- S7-T02：可信备份输入、恢复前保护、staging / 原子替换、最终验证、失败回退与严重失败反馈。
- S7-T03：WPF 手动备份、可恢复列表、明确选择 / 二次确认、维护态协调、恢复后显式退出和重开。
- 本轮重新验证：三卡 6/6、9/9、30/30；T01/共享组合 12/12；Stage 6～3 为 52/52、51/51、179/179、170/170；Release 680/680，build 0/0，EF 无漂移，migration=8，无包依赖变化。

## Backup 最终契约

- 权威入口：`Application/Backups/LocalDatabaseBackupUseCase.cs`；底层复用 `Infrastructure/Backups/PreImportSnapshotService.cs` 的 SQLite BackupDatabase，不用普通文件复制代替运行中的一致性快照。
- 默认正式库为 `%LOCALAPPDATA%/StoreExpiryInspector/data/app.db`，备份目录为同一运行根下独立 `backups`；备份目录不能与正式 data 目录相同。
- 先创建临时快照，核对 integrity、关键 schema 与当前 migration，再发布带时间/GUID 身份的文件；SHA-256、大小、UTC 时间和 migration 等元数据写入同名 JSON。普通手动备份成功登记既有 BackupRecord。
- 单进程共享互斥阻止重复备份/恢复。备份失败不发布成功集合，清理本次产物，不覆盖旧有效备份。底层清理有权限限制的现实边界，不能由 UI 隐瞒错误或将临时文件列为有效备份。
- 不提供定时备份、retention、备份删除、云备份或外部文件任意导入。

## Restore 最终契约

- 权威入口：`Application/Backups/DatabaseRestoreUseCase.cs`。调用方必须声明并真正完成 runtime 停止；bool 入参不是替代真实维护流程的许可。
- 在任何正式替换前验证来源身份、JSON、大小、SHA-256、SQLite integrity、schema 和 migration；拒绝自身、临时半成品、缺失、损坏、被占用或不兼容来源。
- 先复用 Backup 创建 `pre-restore` 保护快照及 JSON；该保护不追加业务 BackupRecord。保护失败不替换正式库。
- 清连接池并检查排他访问，创建同目录 staging，再验证，精确隔离目标库 sidecar，使用 Windows File.Replace 原子替换。不是对正式库直接粗暴复制覆盖。
- 成功前再次核验 SHA/integrity/schema/migration，清理本次 rollback、staging、failed、sidecar/quarantine；清理不完整不返回成功。
- 最终验证失败尝试原子回退并验证原始身份与结构。回退失败或清理失败返回 critical；不能承诺断电、外部篡改、权限故障下绝对自动恢复。
- 此为同版本兼容恢复，不提供跨版本 migration 升级、导入撤销执行、恢复历史页面或任意外部 SQLite 恢复 UI。

## UI 与运行态调用边界

- `LocalDatabaseBackupQuery` 只读列举当前应用管理目录中的已验证手动/恢复前保护备份，复用 T02 内部校验，按创建时间倒序；坏文件、临时文件、缺元数据及重解析点不作为可恢复项。目录读取错误与空目录明确区分。
- WPF `DatabaseBackupRestoreViewModel` 只负责显示、输入和调度；不复制 hash/integrity/migration 或文件替换算法。
- 恢复必须明确选中目标，恢复前重验，确认框显示身份与替换风险、恢复前保护和退出要求，默认取消。禁止点击行即恢复、自动选最新并恢复或启动自动恢复。
- 维护前沿用 Draft 稳定保存及 Import/Submission/History 忙碌门禁；停止 Reminder scheduler，`DatabaseRuntimeGate` 拒绝新操作并等待在途连接离开作用域。
- 恢复成功进入重启必需状态；critical、未知失败或恢复开始后异常也保持业务锁定。仅已知安全失败可恢复运行，不进行复杂热重载。
- 显式退出清 scheduler、Tray 与单实例资源；重开应用从恢复后数据库正常初始化。必须在重开前核验精确字节，因为正常启动会写 AppState 等既有状态。

## 正式数据与真实验收

- 用户在隔离环境完成备份、取消/确认、恢复 A、显式退出、重开 10:00、托盘及 1024×600 / 125% / 键盘验收；最后确认“通过，未再次启动软件”。
- 最近用户 Finish 原始回执：正式库 299008 bytes，SHA-256 `F3D423DF14B882D7BFE87780A81CF5879F074AF4880601CBEDB6B475A964F522`；进程 0，隔离与保护暂存已清理，原自启动值已恢复。
- 本轮重新确认进程 0、正式库存在和大小 299008 bytes。工具侧 Junction 仍阻断直接哈希和完整目录枚举，正式 SHA、实际 migration、sidecar/quarantine 与完整历史备份目录本轮未重新确认；依据最近已验收回执和不再启动事实，不宣称修复环境。
- 以后仍不得使用正式库做自动化/GUI 恢复测试。保护整个原运行目录及历史备份，验收后恢复原自启动状态，核对哈希/大小/sidecar/进程并清理隔离目录；恢复后不要为复看再次启动。

## 冻结 Stage 3～6 权威

- Excel 是局部增量；未出现商品或批次不产生删除、归零、停止或恢复含义。
- Stage 3：阶段、任务聚合、库存 0、新批次/新到货/受限恢复及正式 0 件停止只调用原权威用例。
- Stage 4：Draft、重新确认、库存修正、正式提交、超库存与事务边界不因备份、恢复或视觉改版而复制。
- Stage 5：正式历史与 Revision 保持审计链，数量修订不重放 Lifecycle / Submission。
- Stage 6：候选、同日一次、通知成功登记、单实例、Tray、scheduler、Settings 与 HKCU 自启动保持唯一权威。
- schema / migration 未变；工程仅有已归档 T02 测试程序集可见性声明，不是新增运行依赖。

## 范围收敛与后续

用户决定暂不实施“正式排查历史 / 结果 Excel 导出”，记为 Deferred Feature，不是 Stage 7 缺陷或阻断项；未来必须重新批准，任何后续阶段不得自动补做。不做今日待排查任务、Draft 或数据库原始表导出，不创建 S7-T04。

当前 UI debt 是跨阶段视觉一致性、密度/层级、表格与长身份可读性、导航、设置/数据保护和操作/状态表达的统一整理。S4/S5 已修复的裁切、选择绑定、反馈隐藏不得回退；T03 的第一次 A/B 核验不一致保留真实证据，不冒充已证实的绑定缺陷。详细来源与边界见 `UI-UX-REFRESH-HANDOFF.md`。

下一步先统一 UI/UX，但本轮只交接，不开始实施。不得为了视觉优化破坏备份身份、恢复保护/确认/排他/回退/清理、Draft 保存或运行态退出。Stage 8 仍是稳定性/性能，需等待 UI/UX 完成与用户单独批准；不创建 S8-T01，不把视觉工作并入 Stage 8。治理提交后停止。
