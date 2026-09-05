# Sol 证据边界与复验入口

- `user-win11-verified-*` 位于相邻NETWORK-DIAG目录，是用户原失败实机的独立探针回执，不是GUI成功。
- `Program.cs` / `frozen100-gui-result.json`：正式100 DLL/runtime的System.Windows.Application隔离宿主，合成Shell loaders、不创建DB。将该单独源码与相邻S9-T06-SOL-VERIFY/SolVerify.csproj复制到新TEMP/GUID构建，PublishDirectory指向已核验的正式100完整解压目录，S9_T06_SOURCE_APP_XAML指向未变更的源App.xaml。工作目录必须TEMP/GUID；不是完整App生命周期。
- `Invoke-GuiCandidate.ps1`：完整102 App、自建TEMP/GUID数据库、实际UIAutomation按钮；Candidate为固定e5ecc65发布EXE，EvidenceDirectory为本次验收目录。CancelDownload执行取消负例。首次脚本仅查顶层窗口漏掉子窗口提示，修正后复用同一进程执行成功；此脚本故障不是产品网络故障。
- `NetworkChecks.csproj`：复制项目与NetworkChecks.cs到TEMP验收目录，以PublishDirectory指向固定候选发布目录构建。S9_T06_CHECK_ROOT为新TEMP/GUID结果根；S9_T06_LINK_ROOT为指向另一个本次空合成目录的TEMP/GUID junction。四个隔离handler负例与一个reparse拒绝共5/5；不请求GitHub、不启动Updater、不访问DB。
- `candidate-default-off.json`：无诊断flags的隔离smoke exit0。`candidate-synthetic-db.json`仅为本次诊断DB关闭后只读检查，不是升级Before/After。
- `first-full-release-result.json`保留1054/1055失败；`final-technical-result.json`为e5ecc65全量1055/1055及TRX hash。原失败因启动赋值三元表达式不满足旧静态契约，未删测试；Terra显式分支修复后重跑。
- `candidate-scan.json`逐项记录用户ZIP的423项SHA。初次头标记匹配已定位到既有ContainsSecret拦截字符串；完整私钥PEM/PAT/敏感用户路径/私钥容器检查0已知secret，未读取生产私钥。此扫描不证明所有未知编码安全。

开发机证据均不能关闭用户原安装GUI阻塞。1.0.2候选不是正式发行，不改100/101资产；下一步只接收原失败标准Win11的真实GUI日志。
