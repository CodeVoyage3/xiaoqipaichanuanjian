# S12-T01 Updater 控制台窗口专项

## 根因与最小修复

- `StoreExpiryInspector.Updater.csproj` 原为 `<OutputType>Exe</OutputType>`，即 Windows Console subsystem。
- 主程序直接以 `UpdaterLaunch.Create` 启动 Updater：`UseShellExecute=false`、`WorkingDirectory=Updater 自身目录`、`ArgumentList --journal`。生产链未经过 `cmd.exe`、`powershell.exe`、`.bat` 或 `.cmd`。
- Updater 不读取 stdin 或 Console 输入。仅有的 `Console.Error` 输出是异常诊断，不参与状态推进。
- 生产唯一修改为 `<OutputType>WinExe</OutputType>`。没有变更 `UpdaterLaunch`，因此 Stage9 的 Updater 自身目录 `WorkingDirectory` 隔离保持不变。

## 诊断保存边界

已验证的事务异常会写入 journal 的 `LastError`；进入人工恢复时会同时写 `manual-recovery.log`。因此 Console 不是正常失败、rollback 或恢复的唯一诊断通道。若连人工恢复状态自身也无法持久化，二级异常仍只会尝试写 `Console.Error`；本轮不为该极端持久化失败扩展协议。

## 精准专项

- production publish Updater EXE PE Optional Header `Subsystem=2`（Windows GUI），不是 `3`（Console）。
- 实际外部 test-mode Updater EXE 亦为 `Subsystem=2`。
- `S12T01-ConsoleTargeted.trx`：2/2。
  - `RealAppMaintenancePreparerAndExternalUpdaterCommitFixture10`：外部 Updater 成功切换、Candidate ACK、Completed。
  - `RealProductionOldRollbackRestoresSchema9AndLoadsOldShell`：候选健康确认失败后 RolledBack；旧树、9 migrations、旧版 ACK 与正常启动均恢复。
- rollback journal 记录 `Schema.LastError=Applied migration health acknowledgement was not verified.`，并保留 rollback health ACK。

所有运行根均为 TEMP/GUID，使用显式 data root；未访问正式安装或正式数据库。未生成 candidate、未发布、未运行 full 或 178 项。
