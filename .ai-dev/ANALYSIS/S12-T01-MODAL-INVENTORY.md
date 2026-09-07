# S12-T01 WPF 弹窗盘点

日期：2026-09-07。范围：`src/StoreExpiryInspector` 的 `Show`、`ShowDialog`、`Owner`、自定义 Window、公共 Dialog 服务和 `MessageBox` 静态盘点。

## 计数与分类

| 机制 | 调用入口 | 当前 Owner | 当前行为 | 结论 |
|---|---:|---|---|---|
| `WpfDialogService.Show` | 25 | 21 个业务入口传实际窗口；4 个 App 启动/单实例失败前合法为 null | 服务内部 `ShowDialog()` | 已模态；不改通用 Owner fallback |
| 直接自定义 Window | 4 | 全部显式 Owner | `ShowDialog()` | 已模态：提醒时间、设置、库存归零、排查结果确认 |
| 导出成功 | 1 | 显式 Owner | `ShowDialog()` | 已模态；打开文件失败的二级错误改为以当前导出窗为 Owner |
| 更新通知 | 1 | `MainWindow` | `Show()` | 唯一业务 modeless 根因；改为 `ShowDialog()` |
| 原生文件选择器 | 3 | `this` | `ShowDialog(this)` | 原生模态，保持 |
| MainWindow 生命周期 | 3 | 不适用 | `MainWindow.Show()` | 主壳显示，不是业务弹窗，保持 |

业务窗口底层实际 `Window.Show()` 只有更新通知 1 处；另有 `MainWindow.Show()` 3 处，属于主壳生命周期。25 个 `WpfDialogService.Show(...)` 是服务方法调用名，内部实际为 `ShowDialog()`，不能计为 modeless。改后业务窗口不保留 modeless；源码底层 `ShowDialog()` 从 9 处变为 10 处。

未发现 `MessageBox.Show`、其他 `DialogService` 或 `WindowService`。没有明确需要与主界面并行操作的独立工具窗口。

## 窗口与入口清单

以下按源码调用入口计数。自定义业务弹窗共 31 个：公共服务 25、直接自定义窗口 4、导出成功窗 1、更新通知窗 1；另有原生文件选择器 3 个，因此用户可见的对话框入口合计 34 个。`MainWindow.Show()` 3 处不计入对话框。

### 公共 `WpfDialogService.Show`（25）

- App 启动前、Owner 合法为 `null`（4）：隔离数据根/参数配置失败（`App.xaml.cs:62`）、待处理更新恢复失败（`:83`）、重复实例提示（`:98`）、数据库初始化或启动补算失败（`:167`）。
- App 主窗口生命周期（3）：备份或恢复进行中时阻止关闭（`App.xaml.cs:471`）、数据保护锁定后提示正常退出（`:484`）、备份或恢复进行中时阻止托盘退出（`:526`）。
- 每日提醒（1）：到期提醒通知（`UI/WindowsMessageBoxReminderChannel.cs:25`）。
- 今日排查确认窗内（1）：提交被阻止提示（`UI/TodayInspectionConfirmationWindow.xaml.cs:33`）。
- 今日排查主窗口（6）：结果文件读取失败（`UI/MainWindow.xaml.cs:449`）、清空草稿确认（`:800`）、超库存确认（`:885`）、过期商品正库存确认（`:900`）、正式提交确认（`:911`）、确认恢复备份（`:920`）。
- 设置与数据维护（10）：暂不可打开设置（`UI/MainWindow.xaml.cs:458`）、重置业务数据第一次确认（`:630`）、重置业务数据第二次确认（`:637`）、重置失败（`:650`）、重置结果（`:665`）、读取自启动状态失败（`:727`）、保存自启动状态失败（`:742`）、设置保存成功（`:752`）、设置窗口打开失败（`:779`）、修改正式排查数量确认（`:874`）。

### 其他对话框入口（9）

- 直接自定义窗口（4）：提醒时间选择（`UI/MainWindow.xaml.cs:302,369`）、设置窗口（`:453,771`）、库存修正为零确认（`:809,863`）、今日排查结果确认（`:428,445`）。四者均显式设置 Owner 并调用 `ShowDialog()`。
- 导出成功窗（1）：今日排查计划导出完成（`UI/MainWindow.xaml.cs:425`）；打开导出文件或目录失败时，二级提示以该导出窗为 Owner。
- 更新通知窗（1）：检测到新版本（`UI/MainWindow.xaml.cs:100` -> `UI/WpfDialogService.cs:16-45`）；本轮将唯一业务 `Show()` 改为 `ShowDialog()`。
- 原生文件选择器（3）：导入商品 Excel（`UI/MainWindow.xaml.cs:401`）、导出今日排查计划（`:422`）、导入今日排查结果（`:440`）；均调用原生 `ShowDialog(this)`。

### 非对话框 `Show`（3）

- `App.xaml.cs:137,189,513` 的 `MainWindow.Show()` 用于显示或恢复主壳，不属于弹窗，也不参与上述 31/34 个入口计数。

## 根因与边界

用户观察证明存在后台可交互；源码只证明更新通知是唯一以 `Show()` 打开的业务对话框。其余业务入口已经使用标准 WPF `Owner + ShowDialog()`，不应作全局替换。

不为 `WpfDialogService.Show` 增加 `owner ?? Application.Current.MainWindow`：四个 null 调用在 MainWindow 尚未创建或显示时发生，不能构造有效 Owner。导出错误是唯一可证明漏传 Owner 的链，错误提示将以当前导出模态窗为 Owner。

更新准备从 UI Dispatcher 进入，后台工作使用 `Task.Run`，进度回到 Dispatcher；`ShowDialog()` 的嵌套消息循环会继续处理这些回调，没有同步等待链或 UI deadlock。已知既有行为：更新繁忙时关闭窗口不会明确取消后台准备；本轮不改变其取消语义。
