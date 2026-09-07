用户裁决：modal技术结果接受；历史宿主风险定名HISTORICAL_TEST_ISOLATION_UNCERTAINTY，无证据认定正式数据修改或损坏，不单独阻塞发布，禁止探测正式数据追查。

# S12-T01 全局模态修复独立验收（Sol）

日期：2026-09-07（Asia/Shanghai）  
结论：**代码与自动化专项验收通过；旧 v1.0.4 候选已作废，尚未生成包含本修复的新候选，也未发布。**

## 变更与边界

- 生产变更仅 `src/StoreExpiryInspector/UI/WpfDialogService.cs`：更新通知由 `Show()` 改为标准 WPF `Owner + ShowDialog()`；导出成功窗内“打开文件/文件夹失败”的二级提示改用当前导出窗作 Owner。
- 未加入全局弹窗状态机、Owner fallback 或新框架；4 个主窗口创建前的启动错误入口继续合法使用 `owner: null`。
- 测试变更仅 `tests/StoreExpiryInspector.Tests/S9T03UpdateCheckTests.cs`：在 STA 安全宿主中动态验证更新通知、公共确认/取消/右上角关闭、直接今日排查确认窗的 Owner 与 Win32 主窗口禁用/恢复行为。
- 盘点见 `.ai-dev/ANALYSIS/S12-T01-MODAL-INVENTORY.md`：自定义业务弹窗 31 个（公共服务 25、直接自定义窗口 4、导出成功 1、更新通知 1），另有原生文件选择器 3 个；34 个对话框入口逐项列名和源码行。`MainWindow.Show()` 3 处是主壳生命周期，不计入弹窗。
- 修改后没有业务 `Window.Show()`；更新通知是修复前唯一业务 modeless 入口。其余入口原本已经模态，未作无证据的全局替换。
- 更新准备仍从 UI Dispatcher 发起，后台工作使用 `Task.Run`；`ShowDialog()` 的嵌套 Dispatcher 继续处理进度与异步 continuation，静态链路没有同步互等。更新繁忙时关闭窗口不会明确取消后台准备，这是既有取消语义，本轮未改变。

文件 SHA-256：

- `WpfDialogService.cs`: `DC46820EBD88C7DE34BA9C5A7A94138FD731FE874ACDFE0EFDF992E19F27E353`
- `S9T03UpdateCheckTests.cs`: `A5F494339D058E02A7EC1DDB647D86969FB10D8228D1B0EFA437D7A230007040`
- `S12-T01-MODAL-INVENTORY.md`: `98364CE73CB9C1B88497295179F68967AE0EF82D25EAD439C8CC6139213C2BBF`

## 独立 fresh 结果

生产 Release 构建：

```powershell
dotnet build src\StoreExpiryInspector\StoreExpiryInspector.csproj -c Release --no-restore -p:NuGetAudit=false
```

结果：0 warnings，0 errors，1.30 秒。该次控制台收据未另存日志文件。

安全宿主的精确 modal 方法：

```powershell
dotnet test tests\StoreExpiryInspector.Tests\StoreExpiryInspector.Tests.csproj -c Release --no-restore -p:NuGetAudit=false --filter "FullyQualifiedName=StoreExpiryInspector.Tests.S9T03UpdateCheckTests.MainWindowCanReachCoreReadyOnStaWithoutAnyRuntimeDatabase" --logger "trx;LogFileName=S12-T01-Modal-Sol.trx" --results-directory C:\Users\39037\AppData\Local\Temp\S12-T01-Modal-Sol-e64ffa1611dc49a4ad5e27bb6bcf2bc7
```

结果：1/1 passed，0 failed，0 skipped，4 秒。

- TRX：`C:\Users\39037\AppData\Local\Temp\S12-T01-Modal-Sol-e64ffa1611dc49a4ad5e27bb6bcf2bc7\S12-T01-Modal-Sol.trx`
- SHA-256：`E518EC62523CB2AE07A8C390723CEBA043DC0DED9FBB865044335A467F39D6BA`
- 该一个方法实际覆盖：更新通知 modal、公共确认、公共取消、公共右上角关闭、直接 `TodayInspectionConfirmationWindow`；每个动态场景均验证 Owner，且验证 Win32 owner 在弹窗打开时禁用、关闭后恢复为 enabled。
- 测试仅实例化 `App` 并加载 `App.xaml` 资源；不调用 `app.Run()`，而以 `Dispatcher.Run()` 驱动消息循环，结束路径调用 `BeginInvokeShutdown(Background)`，因此不会进入 `App.OnStartup` 或运行时数据根初始化。未探测正式数据根来反证或确认历史访问。

## Terra 收据与旧宿主风险

Terra 最终安全宿主 R7：21/21 passed，0 failed。

- TRX：`C:\Users\39037\AppData\Local\Temp\S12-T01-Modal-SafeHost\S12-T01-Modal-SafeHost.trx`
- SHA-256：`D82ACCBEA6B562D188047F3EB0DDE4F3D5F1DBC802FBCFA1ECCE6A86E87991F8`

较早 R4-R6 使用 `app.Run()`。由于 `App` 覆写了 `OnStartup`，这些旧宿主可能初始化默认/正式 LocalAppData 数据根；此前“未访问正式 DB”的保证撤销。没有读取或探测正式数据来判断是否实际发生访问，R4-R6 也不作为安全隔离证据。R7 与 Sol fresh 结果均使用上述 `Dispatcher.Run()` 安全宿主。

## 其余门禁

- `git diff --check`：通过；仅出现工作区既有 LF/CRLF 转换提示，无空白错误。
- 限定 secret scan：对生产文件、测试文件、S12 任务/状态/交接与 modal 盘点扫描，0 matches（`rg` exit 1 表示无匹配）。
- 数据模型、Domain、DbContext 与迁移文件无本轮差异；沿用本卡此前已接受的 `NO_MODEL_DRIFT` 与 migrationCount=9。本轮没有重复 EF、178 项或 full。
- Sol 安全宿主 fresh 运行未进入正式数据初始化；旧 R4-R6 是否实际访问正式数据未知且不作探测。未安装 Setup、未生成新候选、未发布、未打 tag、未上传。
- 旧候选报告已标记 `SUPERSEDED_BY_GLOBAL_MODAL_FIX`，其资产不得发布。用户先前已通过的即时刷新与确认窗布局/提交结果保留；新 modal 修复仍需在重建的新候选上做一次聚焦 GUI 验收。
