# S12-T01 Sol 独立技术验收

日期：2026-09-07

结论：`SOL_TECHNICAL_GATE = PASS`；`S12-T01 = IN_PROGRESS / NOT_ACCEPTED / USER_GUI_REQUIRED`。

本轮独立验收只覆盖冻结后的局部热修源码和必要邻近回归。未运行 full suite，未 publish，未访问正式安装、正式数据库、数据根或备份，未修改或加入既有未跟踪操作手册。

## 完整 diff 审查

生产 diff 仅 3 个 UI 文件：

- `src/StoreExpiryInspector/UI/Stage4ViewModels.cs`：导入执行返回成功后，既有页面刷新委托先等待 `PendingTasks.LoadAsync()`，再等待 `TodayInspection.LoadAsync()`。`ConfirmedImportLifecycleOrchestrator.Execute` 在返回成功结果前已完成 `transaction.Commit()`，因此今日页不会在提交前查询。未修改任务生成算法、导入事务、Domain、数据库层或错误协议。
- `src/StoreExpiryInspector/UI/TodayInspectionConfirmationWindow.xaml`：外层表格行由 `Auto` 改为 `*`，移除 DataGrid 的 `VerticalAlignment="Top"`；标题、表单和操作栏仍为 `Auto`，表格继续保留既有 `MaxHeight="280"`、虚拟化和内部滚动。全局 DataGrid 样式无 `MinHeight` 阻止压缩，code-behind 与 `SizeToContent` 切换未改。
- `src/StoreExpiryInspector/UI/WpfDialogService.cs`：仅把按钮文案从“取消准备”改为“取消更新”；同一 `cancel.Click` 仍调用 `model.CancelCommand.Execute(null)`，更新状态机、下载、Updater 和 rollback 均未改。

测试 diff 增加真实 TEMP/GUID SQLite + Shell 当前会话用例，覆盖先进入空今日页后首次导入、未先进入今日页、无应执行任务、库存 0、应季搭配、赠品小样、重复刷新/切页、二次导入关闭旧任务和首页数量。负向用例断言导入成功、商品分类与库存事实已入库，再断言没有 open task，避免“导入失败也通过”；主 Case 1 另断言没有刷新错误。两个既有分类测试只补等待异步加载完成，未改变生产筛选语义。

## 独立运行证据

生产 Release build：

```text
dotnet build src/StoreExpiryInspector/StoreExpiryInspector.csproj -c Release --no-restore -p:NuGetAudit=false
PASS：0 warning，0 error
```

专项与必要邻近回归：

```text
dotnet test tests/StoreExpiryInspector.Tests/StoreExpiryInspector.Tests.csproj -c Release --no-restore -p:NuGetAudit=false --filter "FullyQualifiedName~S4T06ImportViewModelTests|FullyQualifiedName~V1F03I04TodayInspectionViewModelTests|FullyQualifiedName~Stage4ViewModelTests|FullyQualifiedName~ConfirmedImportLifecycleOrchestratorTests|FullyQualifiedName~PostImportLifecycleUseCaseTests|FullyQualifiedName~ProductTaskAggregationTests|FullyQualifiedName~ProductStockZeroLifecycleUseCaseTests|FullyQualifiedName~V1F01I03ColdStartTests"
PASS：178/178，失败 0
```

本次 Sol 独立命令使用 console logger，没有生成独立 TRX；通过数和退出码保留在本话题实时命令输出。对应运行产物身份：

- `D:\wendang\ChatGPT\门店效期排查软件\src\StoreExpiryInspector\bin\Release\net10.0-windows\StoreExpiryInspector.dll`：1,760,256 bytes，2026-09-07 12:16:02，SHA-256 `39398F36F8DBB2A41D3108EFA8BABBCDAC0EB97CE84A1EB36CBB73D083BFECA6`。
- `D:\wendang\ChatGPT\门店效期排查软件\tests\StoreExpiryInspector.Tests\bin\Release\net10.0-windows\StoreExpiryInspector.Tests.dll`：2,101,248 bytes，2026-09-07 12:15:26，SHA-256 `A9B5ADE802B4BE522F181099013BF7DCA859A6E00228657DDFF293594B444F37`。

Terra 实施收据只用于复核生产前后对照和实施阶段覆盖，不替代上述 Sol 178/178 独立运行：

- 旧接线 Case 1：`C:\Users\39037\AppData\Local\Temp\S12T01-3ec639a4c22b4d80b7c7e31b6820fdc5\S12T01-Case1-ReleaseOldChain.trx`，0/1，SHA-256 `593BF3EE494ABCB41D44466076DE1DBC1657B12D47719BC641685050F69F7A6F`。
- 修复接线 Case 1：`C:\Users\39037\AppData\Local\Temp\S12T01-3ec639a4c22b4d80b7c7e31b6820fdc5\S12T01-Case1-ReleaseFixed.trx`，1/1，SHA-256 `E2357DDC24F47F0E82CAB9DDB7222ED48F405EA575F9416F29F80795B6639111`。
- 最终 Case 2-8 与补强断言：`C:\Users\39037\AppData\Local\Temp\S12T01-3ec639a4c22b4d80b7c7e31b6820fdc5\S12T01-Case2-8-Release-Assertions.trx`，25/25，SHA-256 `58DA2D62AB22B792BEFEF64EA97AED0CF5A14EF6E6234200D5D2C461789A8D20`。
- Terra 直接相关组合：`C:\Users\39037\AppData\Local\Temp\S12T01-3ec639a4c22b4d80b7c7e31b6820fdc5\S12T01-DirectRegression-Release.trx`，85/85，SHA-256 `9EC9B2A47BC34D9D792BD1B641A4A205A05AD124FC0368F961CB155DA6A17EF9`。

该组合覆盖导入 ViewModel、今日排查 ViewModel、Dashboard/PendingTasks Shell 接线、确认导入事务、导入后生命周期、最高阶段与同日聚合、库存零和冷启动边界。最终 diff 未触及需要扩大到 full 的模块，因此按 S12-T01 裁决不运行 1000+ full suite。

EF 与 migration：

```text
dotnet ef migrations has-pending-model-changes --project src/StoreExpiryInspector/StoreExpiryInspector.csproj --configuration Release --no-build
PASS：No changes have been made to the model since the last migration.

dotnet ef migrations list --project src/StoreExpiryInspector/StoreExpiryInspector.csproj --configuration Release --no-build --no-connect
PASS：9 条；末条 20260901155124_AddPolicyAndBaselineFoundation
```

`--no-connect` 的 applied/pending 状态未知提示是预期结果；该命令未连接任何正式数据库。

其他门禁：

- `git diff --check`：PASS。
- 本轮治理、生产和测试文件限定 secret scan：PASS，0 命中。
- migration/ModelSnapshot、Domain、Application 任务算法、Infrastructure、Updater、UpdateSafety：0 生产 diff。

## 保留门禁

XAML 静态检查能够证明 `Auto/*/Auto/Auto`、表格滚动与按钮/表单仍存在，不能可靠证明 100%/125%/150% DPI 和低高度窗口下的真实像素可见性与点击。

下一步须先完成 v1.0.4 candidate App/Updater publish、package、manifest/signature/hash 与候选升级/数据保持验证，之后再交用户执行一次最小 GUI 候选验收：首次导入后不重启即出现今日排查；确认窗口底部按钮完整可见且提交可用；更新弹窗显示“取消更新”。用户候选验收通过后才进入正式发布及 POST-RELEASE 真实 GitHub latest 升级验证。

本轮未构建候选发布包，未验证 v1.0.3 → v1.0.4 候选升级，也未发布 v1.0.4；禁止记录 `REAL_GITHUB_V103_TO_V104_UPGRADE_VERIFIED`。
