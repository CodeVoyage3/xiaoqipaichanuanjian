# S12-T01 Updater 控制台窗口独立验收（Sol）

日期：2026-09-07（Asia/Shanghai）  
结论：**最小修复与限定自动化门禁通过；尚未重建 v1.0.4 candidate，也未发布。**

## 根因与最终差异

- 唯一生产差异：`src/StoreExpiryInspector.Updater/StoreExpiryInspector.Updater.csproj` 的 `<OutputType>Exe</OutputType>` 改为 `<OutputType>WinExe</OutputType>`。
- 原 Updater 是 Windows Console subsystem；主程序与 pending recovery 均直接启动该 EXE，生产链没有 `cmd.exe`、PowerShell、`.bat` 或 `.cmd` wrapper。这足以解释升级期间出现的黑色控制台窗口。
- 保留 `UpdaterLaunch.Create` 的 `UseShellExecute=false`、Updater 自身目录 `WorkingDirectory` 与 `--journal` 参数；未增加 `CreateNoWindow`、`WindowStyle`，未修改任何更新状态机、journal、ACK、切换或回滚逻辑。
- Updater 不读取 stdin。`Console.Error` 仅用于异常诊断，不参与状态推进；可验证事务异常仍写 journal `LastError`，人工恢复还写 `manual-recovery.log`。若人工恢复状态本身也无法持久化，`Program.cs:531` 的二级异常只会尝试 stderr，WinExe 下该诊断可能不可见；这是既有极端持久化失败限制，本轮未扩展协议。

## 独立 production publish 与 PE 门禁

命令：

```powershell
dotnet publish src\StoreExpiryInspector.Updater\StoreExpiryInspector.Updater.csproj -c Release --no-restore -p:DebugType=None -p:DebugSymbols=false -o C:\Users\39037\AppData\Local\Temp\S12-Console-Sol-Publish-ea424b73029e472297cfdc0f32d2cfe3\updater
```

结果：publish exit 0，无 warning/error。直接读取发布 EXE 的 PE Optional Header：

- EXE：`C:\Users\39037\AppData\Local\Temp\S12-Console-Sol-Publish-ea424b73029e472297cfdc0f32d2cfe3\updater\StoreExpiryInspector.Updater.exe`
- SHA-256：`C89AD6F1FC786EC02A672BAA39EF01585A4F710274080883A1D4D231BAE2628F`
- `Subsystem=2 / WindowsGui`；不是 Console subsystem 3。
- 发布目录没有 `.cmd`、`.bat`、`.ps1` 或 `.vbs` wrapper；被启动对象就是该 EXE。
- publish log：`C:\Users\39037\AppData\Local\Temp\S12-Console-Sol-Publish-ea424b73029e472297cfdc0f32d2cfe3\publish.log`，SHA-256 `4C345A2188AB9F0FC5C9EE3EEABC6F39AD9FB914E0A67ED6CF8EC6FC5C9D3C5E`。

## 独立成功与回滚专项

环境仅设置旧版 production publish 输入：

```powershell
$env:S9_T07_REAL_OLD_PUBLISH='C:\Users\39037\AppData\Local\Temp\8d8aa53b-4952-45ac-af76-53c545baec42'
dotnet test tests\StoreExpiryInspector.Tests\StoreExpiryInspector.Tests.csproj -c Release --no-restore -p:NuGetAudit=false --filter "FullyQualifiedName=StoreExpiryInspector.Tests.S9T07SchemaUpgradeSnapshotTests.RealAppMaintenancePreparerAndExternalUpdaterCommitFixture10|FullyQualifiedName=StoreExpiryInspector.Tests.S9T07SchemaUpgradeSnapshotTests.RealProductionOldRollbackRestoresSchema9AndLoadsOldShell" --logger "trx;LogFileName=S12-T01-Console-Sol.trx" --results-directory C:\Users\39037\AppData\Local\Temp\S12-Console-Sol-Targeted-5a75bb1037404e1186265b3cc024fbe3
Remove-Item Env:S9_T07_REAL_OLD_PUBLISH
```

结果：2/2 passed，0 failed，0 skipped，48 秒。

- `RealAppMaintenancePreparerAndExternalUpdaterCommitFixture10`：27.59 秒；外部 test-mode Updater 成功切换，candidate ACK，journal=`Completed`、schema=`CandidateCommitted`。
- `RealProductionOldRollbackRestoresSchema9AndLoadsOldShell`：21.02 秒；真实旧版 production schema9 输入在 fixture migration/health 失败后恢复旧树与 schema9，旧版 ACK、正常 shell 加载，journal=`RolledBack`、schema=`RolledBack`。
- TRX：`C:\Users\39037\AppData\Local\Temp\S12-Console-Sol-Targeted-5a75bb1037404e1186265b3cc024fbe3\S12-T01-Console-Sol.trx`，SHA-256 `6F7A55EC21AD0BCD19D60A30349E0705F13B9397C9E5245713E243172F3A7E30`。
- console log：同目录 `console-targeted.log`，SHA-256 `6A70A5DC6224C5FCD9D84300D2922C2993E1828ADFDE1A02465197B01627D6A4`。

Fresh journal 收据：

- 成功：`C:\Users\39037\AppData\Local\Temp\28cec020-9f74-40b2-b965-3a8caef465c4\updates\8bff9b29-e52a-4545-a167-4463d8d86a40\journal.json`，SHA-256 `E83C8D5736BFFAFF8BD4108A83179A27DC9177D22CB7F69497333A6F723D9E5B`，phase 10 / schema phase 8，health ACK 存在。
- 回滚：`C:\Users\39037\AppData\Local\Temp\bc56cd7a-7dc7-4f59-9704-34b52233fa58\updates\8ced77a4-8cad-48a1-adc2-32c8c411ef76\journal.json`，SHA-256 `39E46E76B0FCF5E78A1C6ACBD9B3A0456901217201F7A1E0230E9918A6E722E4`，phase 15 / schema phase 16，health ACK 存在，`Schema.LastError="Applied migration health acknowledgement was not verified."`。

两个事务用例实际启动 test-mode Updater EXE；其 PE Header 同样为 `Subsystem=2 / WindowsGui`。该 EXE apphost 与 production publish EXE 的 SHA-256 相同，但 test-mode DLL 含受控测试接缝，不等同于 production DLL。事务结果证明现有成功/回滚协议在 WinExe 输出类型下未退化；production publish 收据单独证明正式发布 EXE 的 Windows subsystem 身份。

Fixture10 是测试迁移目标，用于验证已有跨 schema 状态机，不代表 v1.0.4 生产新增 schema；本热修复仍无 Model/DbContext/migration 变化并沿用 production migration9。

## 范围与停止点

- 本轮运行根、data root、install root、journal 均为 TEMP/GUID；未探测正式安装或正式数据库。
- 先前安全宿主不确定性继续以 `HISTORICAL_TEST_ISOLATION_UNCERTAINTY` 记录，不以探测正式根消除，也不阻塞本项技术结论。
- 未重跑 178 项、full、modal 或旧 GUI 两项；未制作 candidate、Setup，未发布、打 tag 或上传。
- 下一步只能在包含此 WinExe 修复的新 candidate 上执行用户限定升级 GUI 验收；本报告不冒充实际 v1.0.3 → v1.0.4 正式升级。

