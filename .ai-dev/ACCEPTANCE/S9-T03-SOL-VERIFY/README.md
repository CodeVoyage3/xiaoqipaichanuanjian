# S9-T03 Sol 独立证据

生产最终提交 `15434e53a6809fd654337fee0332c851c238a922`；主体实现 `523918b537d7d4fcebcf334e569cf31110b79741`。这里的 C# 只是 Sol 独立验收程序，不编入产品或 solution，不引入依赖。所有版本样例均为合成测试，真实检查的 current 来自产品程序集。

原件目录：`C:\Users\39037\AppData\Local\Temp\df25bf9d-0599-42e2-8979-3c2e6c30995a`。TEMP 可能清理；本目录保存小型原始结果及可重跑源，不保存发布二进制、数据库或大型 TRX。

## 重跑

在新的 TEMP/GUID 目录复制 `Program.cs`、`UiVerify.cs`，创建临时 `SolVerify.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="D:\wendang\ChatGPT\门店效期排查软件\src\StoreExpiryInspector\StoreExpiryInspector.csproj" />
  </ItemGroup>
</Project>
```

在该 TEMP 目录执行 `dotnet run -c Release -p:NuGetAudit=false` 跑合成协议；加 `-- --ui` 跑独立 WPF；加 `-- --real` 只读匿名调用真实生产客户端。使用相同 Windows 用户的 .NET/NuGet 缓存；NuGetAudit=false 不代表已通过在线漏洞审计。

真实模式断言当前无 Release；未来仓库发布后应重新解释实际结果，不能把它当永远固定的状态。UI 模式配置新的 TEMP/GUID，注入成功的空核心读取，不初始化数据库，不调用正常 App.OnStartup，不访问正式根；STA Dispatcher 有终止上限。

## 证据边界

- `protocol-initial-failures.json`：首次 37/40，保留末尾换行 tag、普通取消异常、正文 IO 异常三个真实失败；同一 Terra 修复后 `protocol-independent.json` 为 40/40。
- `real-client.json`：生产默认 HTTP 客户端实际匿名结果 NoPublishedRelease，2026-09-04 23:44:06 +08:00。原始 repo/list/latest HTTP 与限流安全头另见上级 `S9-T03-GITHUB-SMOKE.json`。
- `ui-independent.json`：最终 15434e5 的真实 WPF 16/16，包括仅新版提示、文字与键盘操作、白色主按钮文字、同版本并发抑制、明确未启用反馈、稍后关闭、主窗关闭后不显示。此前 Sol 实际视图发现全局 TextBlock 使主按钮黑字，Terra 只修新按钮，未改变全局资源。两张 PNG 在原件目录，已人工查看。
- `publish-smoke.json` / `publish-manifest.json`：最终提交 fresh win-x64 self-contained 多文件发布，420 文件/164235092 字节；显式新 TEMP/GUID 实际 EXE 启动，ready marker/exit0/程序树 SHA256 不变。本卡没有重建 T02 安装器。
- `publish-db-probe.json`：仅读取本次新建合成 DB 的临时副本，integrity ok、FK0、migration9；复用上级 T02 的 `probe.py probe <新合成根>`，没有使用 seed 分支。正式数据库未探测或访问。
- S9-T01 smoke-exit 模式在更新检查接入前返回，所以发布 smoke 仅证明发布后核心初始化；更新链证据是独立协议、生产客户端真实 HTTP、实际 WPF 与完整源码链审查，不能混称发布 EXE 已在 smoke 内检查更新。
- `tests/targeted-final.trx` 为最终 32/32，`final-release/full-release.trx` 为最终无 filter 全量的原件位置；最终计数及门禁见上级 Acceptance/RESULT。

Stage8 的高规模/真实 Excel/worker 门禁关闭时存在空 return；全量通过不能冒称本轮重跑高规模或真实 Excel。开发机 WPF 与隔离发布不替代门店干净 Win10/11 GUI、正式新版 Release 或实际更新升级验收。
