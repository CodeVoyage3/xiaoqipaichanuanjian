# UIUX-R02 最终验收｜三类代表页生产 UI 落地

日期：2026-09-01。结论：**技术门禁与用户真实 WPF GUI 验收均通过，正式数据库已恢复，UIUX-R02 正式完成并归档。**

## 范围与实现

- UIUX-R01 已由用户以原句“新的整体视觉方向认可，可以按该设计系统进入生产 UI 重构。”放行。
- 全新的 GPT-5.6 Luna（max）只落地 Dashboard、待排查任务列表、排查详情及必要共享视觉资源；Sol 独立审查并回收正常态缺少文字、Refresh 层级和共享 Header 影响其他页面三项偏差。
- 生产差异限定为 `App.xaml`、`MainWindow.xaml`、`MainWindow.xaml.cs`；没有修改 ViewModel、Application、Domain、Infrastructure、schema、migration、PackageReference 或 target framework。
- Dashboard 使用单一数据状态/摘要带和优先任务 DataGrid；待排查页保留标准列与紧凑两行商品身份列；详情按商品身份、排查信息、批次处理、正常批次、56 DIP Bottom Action Bar 排列。
- 条码与商品编码保持可见、可选择复制；阶段和保存状态继续使用文字加语义色；固定 50 条分页、查询顺序、Draft/Reconfirm/库存修正/提交命令及 Stage 3～7 权威不变。

## 独立技术结果

| 门禁 | 结果 |
|---|---|
| 初次 UIUX-R02 + Stage 4 相关精确组合 | 69/69，通过 |
| 最终 UIUX-R02 精确测试 | 1/1，通过 |
| 最终 UIUX-R02 + S4-T10 组合 | 3/3，通过 |
| Release 全量 | 681/681，通过；0 失败、0 跳过 |
| Release build | 使用 `--ignore-failed-sources -p:NuGetAudit=false` 离线 restore 后，0 warning / 0 error |
| 在线 NuGet 漏洞审计 | 本轮不可用；离线构建成功不冒充在线漏洞审计通过 |
| EF model drift | 无漂移 |
| migration | 8 |
| dependency | `.csproj` 无差异，无新增依赖 |
| `git diff --check` | 通过 |
| 启动冒烟 | 只读绑定修复后 Release 进程与主窗口建立成功；该历史冒烟不替代用户 GUI 验收 |

自动化测试使用隔离测试数据库。用户 GUI/UAT 后正式库一度为 2,191,360 bytes；用户确认其中只有测试数据。最终恢复事实见下文，不沿用启动冒烟时的旧文件元数据代替收口结果。

## 启动故障回收

- 用户首次双击 Release EXE 后无窗口；Windows `.NET Runtime` 事件确认进程因 `InspectionDetailViewModel.ProductBarcode` 只读属性被 TextBox 默认 TwoWay 绑定而抛出 `InvalidOperationException`。
- 只将五处只读身份 TextBox 的绑定显式改为 `Mode=OneWay`，并在 UIUX-R02 静态门禁中锁定详情页条码和商品编码绑定模式。
- 修复后定向门禁 1/1 通过；实际启动 5 秒后进程未退出、`Responding=True`，且主窗口句柄与标题均已创建。该冒烟验证只证明可启动，不替代用户视觉与交互验收。

## 首轮 GUI 退回修正

- DataGrid 单元格选中态改用既有 `SelectedSurfaceBrush` 雾蓝表面与 `PrimaryTextBrush`；键盘焦点继续使用 `FocusRingBrush` 2 DIP 边界，与 selected 状态可区分。
- 详情“状态 / 操作”列从 300 DIP 收窄为 200 DIP；生产日期、有效日期、累计到货、上次排查和本次排查数量获得更合理宽度，表头与数据行列定义保持一致。
- 状态仅映射既有 `CheckedQtyStateText`、`HasCheckedQty`、`HasInputError`、`RequiresReconfirmation`；以 Warning/Success/Error/Reconfirm 低饱和标签显示。需要重新确认时，仅对应行在标签旁展示既有 `ReconfirmCommand`。
- Sol 定向回归 2/2、Release 全量 681/681 通过，`git diff --check` 通过，`.csproj` 无差异。未修改业务逻辑、ViewModel、schema、migration 或依赖。
- 后续不使用自动 GUI；1600×900、1024×600 / 125% 与键盘验收完全由用户人工执行。

## 状态列对齐修正

- 根据用户人工截图反馈，普通状态标签改为在 200 DIP“状态 / 操作”列内水平居中；需要重新确认时，状态标签与既有“重新确认”按钮以 8 DIP 间距组成整体后居中。
- 保留两处 200 DIP 列定义、全部状态 Token、现有绑定与 `ReconfirmCommand`；没有拆列或修改业务状态、行高、其他列宽。
- Luna 定向门禁 1/1 通过；Sol 独立复核的 UIUX-R02 + S4-T10 定向门禁 3/3、Release 全量 681/681 通过，`git diff --check` 通过。
- 用户运行中的生产实例一度占用 Release EXE，Sol 未擅自关闭；先以独立临时输出完成同配置构建，实例自行退出后再完成常规输出目录 Release build：0 error，仅既有离线 NuGet `NU1900` 警告。
- 未启动或控制 GUI；该对齐仍等待用户在 1600×900、1024×600 / 125% 下人工确认。

## 三列对齐修正

- 根据用户人工截图反馈，Dashboard、待排查标准表与待排查紧凑表的“库存 / 最近有效期 / 操作”统一采用列内居中：共 9 个居中表头、6 个居中数值单元格和 3 个居中“排查 →”动作。
- 新增的两个 keyed Style 仅服务上述三张表；批次数/批次继续使用既有右对齐数值 Style。列宽、绑定、复制、选择、焦点和 `OpenDetailCommand` 均未改变。
- 原 UIUX-R02 Luna 完成实施；Sol 独立复核的 UIUX-R02 + S4-T10 定向门禁 3/3、Release 全量 681/681、常规 Release build 0 error、`git diff --check` 均通过。仅有既有离线 NuGet `NU1900` 警告。
- 未启动或控制 GUI；三张表的最终视觉间距仍等待用户在 1600×900、1024×600 / 125% 下人工确认。

## 用户 GUI 验收通过

用户在状态列整组居中及 Dashboard、待排查标准表、待排查紧凑表三列居中修正后明确回复“通过”。该回复发生在全部已知视觉修正完成之后，UIUX-R02 GUI 门禁正式通过，无尚待重新验收的已知视觉项。

## 正式数据库恢复收口

- 用户侧管理员 CMD 最终确认 `%LOCALAPPDATA%\StoreExpiryInspector` 已是普通目录，登记为 `USER_DIR_PASS`；工具进程仍保留旧路径视图，不用其旧 Junction 视图否定用户现场回执。
- 已验证历史正式原件继续独立保留；其 `app.db` 为 299008 bytes，SHA-256 `F3D423DF14B882D7BFE87780A81CF5879F074AF4880601CBEDB6B475A964F522`。
- 从该原件生成的 `D:\UIUX-R02-BASELINE-app.db` 工作副本经工具侧重新核验为相同大小和 SHA；用户侧复制到正式 `data\app.db` 后回执为 `SIZE=299008`，`certutil` SHA-256 与基线逐字一致。
- 收口只读核对应用进程为 0，精确 `-wal`、`-shm`、`-journal` 均不存在，UIUX-R02 staging / quarantine / pre-restore 路径均不存在；恢复后未启动 WPF。
- 工具侧仍不能枚举用户现场普通目录，故正式 size/SHA 采用本轮用户原始 CMD 回执；不把工具路径视图异常写成现场恢复失败。

## 最终归档

- 生产差异仍只涉及 `App.xaml`、`MainWindow.xaml`、`MainWindow.xaml.cs`；没有业务逻辑、ViewModel、Application、Domain、Infrastructure、schema、migration、dependency 或 target framework 变化。
- `git diff --check` 通过；最终提交后工作区 clean。
- UIUX-R02 正式归档并停止；未创建 UIUX-R03，未进入 Stage 8，等待用户单独批准下一步。
