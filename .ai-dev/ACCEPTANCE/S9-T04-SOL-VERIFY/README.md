# S9-T04 Sol独立复验资料

最终生产/测试提交：4dbf088ff024a9818418e33bc67868dd0447b604。全新Terra实施；Sol编写本目录独立测试与治理，不代写生产代码。本目录不加入solution，不随publish分发。

## 最终证据

- independent-results.json：Release核心128/128，真实TcpListener HTTP（注入transport仅路由生产逻辑URI），包含检测至完整420文件ZIP Verified、签名A-I、网络/重定向、格式/尺寸/hash/版本、ZIP安全、清理、取消、13并发单次下载。
- ui-independent.json：Release真实WPF12/12；截图为合成0.9.9→1.0.0，非真实新版。plain Application与实际资源/MainWindow/VM/对话框，合成Shell loaders，未启动业务DB。
- exit-independent.json：真实子进程App.ExitApplication及App.OnExit，覆盖Window.Closed主窗口引用消失的实际路径。2秒取消清理延迟；Exit前worker完成，进程exit0、无残留/异常。子类仅屏蔽业务OnStartup，未屏蔽生产退出逻辑。
- real-client.json：01:34:39 +08:00生产客户端真实匿名NoPublishedRelease。GitHub repo/list/latest及官方cli资产headers-only跳转另见上级JSON。
- publish-inventory.json：最终420文件/164297044字节，每文件SHA256。self-contained win-x64；FileVersion1.0.0.0，ProductVersion1.0.0+4dbf088...。候选EXE从未执行。
- secret-scan.json：已跟踪源码及完整publish已知PEM/PAT/密码/容器扫描，无命中；不是所有可能编码secret的完备证明。测试RSA只内存随机生成，未导出私钥。
- build-release.txt/publish-release.txt及最终测试/EF摘要由上级DOWNLOAD-RESULT.json索引。NuGetAudit=false不构成在线漏洞审计。

## 复现

只在新的当前用户TEMP/GUID目录操作。复制本目录四个源文件Program.cs、UiVerify.cs、ExitVerify.cs、make_fixtures.py，创建临时SolVerify.csproj，TargetFramework net10.0-windows、OutputType Exe、UseWPF/ImplicitUsings/Nullable启用，ProjectReference指向仓库src/StoreExpiryInspector/StoreExpiryInspector.csproj。UI源中资源文件路径需对应实际checkout（本次为D:/wendang/ChatGPT/门店效期排查软件）。不要运行归档的历史fallback发布目录；始终显式传新publish路径。

先在正式checkout构建Release，然后publish到该TEMP/GUID/publish-final：

```powershell
dotnet build StoreExpiryInspector.slnx -c Release --no-restore -p:NuGetAudit=false
dotnet publish src/StoreExpiryInspector/StoreExpiryInspector.csproj -c Release -p:PublishProfile=WinX64 -p:DebugType=None -p:DebugSymbols=false -p:NuGetAudit=false --ignore-failed-sources -o <TEMP-GUID>/publish-final
```

切到临时harness目录（所有输出在此），依次执行；不与全量测试并行build/publish：

```powershell
python -B make_fixtures.py <TEMP-GUID>/publish-final
dotnet run --project SolVerify.csproj -c Release -p:BuildProjectReferences=false -p:NuGetAudit=false
dotnet run --project SolVerify.csproj -c Release --no-build -- --ui
dotnet run --project SolVerify.csproj -c Release --no-build -- --exit-parent
dotnet run --project SolVerify.csproj -c Release --no-build -- --real
```

独立成功包直接使用最终1.0.0发布字节，模拟旧客户端0.9.9；不改项目为1.0.1。fixture-index.json记录61个ZIP输入（含有效完整包及恶意变体），ZIP/二进制/私钥不入本目录。缓存全TEMP/GUID；通过后成功候选可保留，失败缓存为空。cacheClean=false在Verified正例表示保留候选，不是清理失败。

## 不改写失败历史

independent-draft-first、d2e5779、core-draft3分别11/24/6失败，真实安全拒绝/分类缺口经Terra返修。04f295c、core-121为中间Debug成功，非最终Release。3875a87-parser-failures含3个签名合法畸形JSON分类失败，9124ac6及9b04b00修复/补测试。

ui-19c9b24及ui-cdedaf4-harness-startup-failure残余窗口主要是Sol工具错误构造业务App、自动排入OnStartup造成的隔离参数拒绝；保护在logger/DB前阻断。修正plain Application后cdedaf4-corrected通过，不把这次harness错误当生产bug。初次资源BAML派生App与Python默认GBK编码问题也属harness设置。实际取消提示、晚到订阅/任务等生产缺口另行修复；ui-cancel-message-failure保留取消文字不正确结果，4419b99后12/12。

exit-9660a6a-failure是真实生产缺口：OnExit时MainWindow已空、下载worker未完成、部分文件残留。bb2571f转到Closed取消并等待worker，4dbf088最终验证通过。根工具曾重复发送已发HttpRequestMessage，修正克隆后才作为实际网络证据。

## 限制

本目录自动化不替代用户真实GUI/干净Win10/11验收。生产trust anchor仍空，不能接收真实更新；无真实Release/tag/asset。无正式数据根访问，无候选执行、替换、升级migration、重置或Undo。全量现有测试只使用合成数据；高规模/真实Excel开关关闭的空return不算这些场景证据。T02旧安装器历史产物，最终发行另行重建。
