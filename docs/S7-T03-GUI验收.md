# S7-T03 用户隔离 GUI 验收

状态：Sol 技术门禁已于 2026-08-31 通过，现由用户本人执行隔离 GUI 验收。脚本文件系统检查不代表 WPF 已通过，S7-T03 尚未正式归档。

## 隔离规则

- 从托盘选择“退出应用”，确认没有正在运行的门店效期排查软件。
- 只使用下面的 `Start` 命令启动。验收期间不要用旧快捷方式、不要开启自启动，也不要更改 Windows 的现有自启动项。
- `Prepare` 先核对正式库 299008 bytes / 已批准 SHA-256，并拒绝 sidecar、restore 残留或 ReparsePoint；随后把**整个正式运行目录**旁置保护，使用全新的空白目录。正式数据和历史备份不作为测试数据。
- 设置页保存提醒时间时也可能写入本应用自启动值，因此脚本会记录该值，结束时原样恢复；只处理 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 的 `StoreExpiryInspector` 值，不动其他程序。
- 若脚本报错，立即保留输出并反馈，不手动删目录、不修 Junction、不覆盖保护备份。Prepare 成功后如需中止验收，退出应用并执行 Finish。
- 电脑重启/注销可能启动既有自启动项；验收期间请不要重启 Windows。这里“重启应用”只指退出应用后再次执行 Start。

## 1. 准备并启动

在正常本机 PowerShell 执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File 'D:\wendang\ChatGPT\门店效期排查软件\docs\S7-T03-GUI.ps1' -Action Prepare
powershell -NoProfile -ExecutionPolicy Bypass -File 'D:\wendang\ChatGPT\门店效期排查软件\docs\S7-T03-GUI.ps1' -Action Start
```

必须先看到 `PREPARE_PASS`。全新空库由应用正常初始化，无需导入正式数据。

## 2. 备份与受控恢复

1. 进入“数据备份与恢复”，确认有明确空状态、未选择备份时不能恢复。
2. 设置中确认提醒时间为 `10:00`。点击“立即备份”得到备份 A，记录其完整文件名；核对时间、大小、身份、验证状态及成功提示。
3. 把提醒时间改为 `11:00`，再次备份得到 B；确认列表按时间倒序。快速重复点击不得产生重复提交。
4. **明确选中 A**，点击恢复，确认对话框说明数据替换、先做保护备份、恢复后退出/重启和不能无感撤销。先取消：应用应继续保持 `11:00`，没有恢复成功提示。
5. 再选 A 并确认恢复。观察进行中状态、按钮禁用；成功后应提示需要重新启动，不能继续普通业务操作。按提示正常退出，托盘图标应消失。
6. **重新启动前**执行下方哈希检查，把示例文件名替换成 A 的实际完整文件名：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File 'D:\wendang\ChatGPT\门店效期排查软件\docs\S7-T03-GUI.ps1' -Action VerifyRestore -BackupFileName 'backup-替换为A的完整文件名.db'
```

必须输出 `GUI_RESTORE_BYTES_PASS`，证明隔离当前库与 A 字节一致，并存在具有一致元数据的恢复前保护备份。此检查不替代程序自身的 SQLite integrity/migration 检验。

7. 再执行 `Start`；设置应恢复为 `10:00`，备份列表中应有 `pre-restore` 保护备份。没有异常第二实例或第二套托盘。
8. 检查 1024×600 窗口及 Windows 125% 缩放下，列表、状态、确认/取消/退出均可读、可操作；用 Tab、Enter 和 Esc 检查基本键盘操作。视觉偏好不作为本卡返工门槛。

不要在正式目录上做损坏备份、恢复失败或 critical 故障注入；这部分由隔离自动化覆盖。

## 3. 恢复正式环境并结束

先从托盘退出隔离应用，然后执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File 'D:\wendang\ChatGPT\门店效期排查软件\docs\S7-T03-GUI.ps1' -Action Finish
```

`Finish` 校验保护库后恢复完整正式运行目录和原自启动值；核对正式大小、SHA-256、无 sidecar / staging、进程为 0，再删除有本轮标记的隔离目录。旧备份保留，回执中 `AutoStartRestored` 应为 `true`。完成后**不要为了复看再次启动应用**。

提供以下结果即可进入最终归档审查：

- 第 1～8 项通过/失败，失败时附提示文字或截图；尤其注明 A 恢复后提醒时间是否为 `10:00`。
- `GUI_RESTORE_BYTES_PASS` 和 `RESTORE_PASS` 输出。
- 原始回执保存在 `obj/S7T03GuiAcceptance/gui-restore-bytes-result.json` 与 `formal-restore-result.json`。

本文件不宣称人工验收通过，不授权 S7-T04。

脚本的可运行安全检查为 `tests/S7T03GuiScriptCheck.ps1`：使用工作区临时假目录，并模拟注册表边界，不启动 WPF、不读写真实注册表。真实注册表恢复仍以用户本机 Finish 回执为准。
