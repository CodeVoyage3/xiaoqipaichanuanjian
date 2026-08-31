# Stage 6 Closeout｜Windows 每日提醒、托盘与自启动

> 归档日期：2026-08-31。Stage 6 已整体验收通过。本文件冻结 S6-T01～S6-T04 产品契约；后续阶段不得绕过或复制这些权威边界。

## 一、阶段结论

- S6-T01～S6-T04 均已有独立实现提交和验收提交。
- 最终专项为 13/13、10/10、12/12、17/17；Stage 5 51/51、Stage 4 179/179、Stage 3 170/170、Release 635/635。
- Release build 0 warning / 0 error；EF 无模型漂移；migration 8；工程依赖相对 Stage 5 收口无变化。
- 用户本人完成 Windows GUI、提醒、托盘和自动启动验收；正式数据库已恢复，应用进程为 0。

## 二、S6-T01 Daily Reminder 权威边界

- 候选只来自现有 open `ProductTask`、商品身份及权威 `HighestStage`；completed / system_closed 不提醒，候选查询不写库。
- 提醒时间读取既有 Settings，默认 10:00；本地业务日使用既有 AppState 的 `LastReminderDate` 作为同日一次权威门禁。
- 未到点、今日已提醒、无候选和时钟回拨各自有明确结果；只有通知成功后才登记，失败可重试且不遗留错误成功状态。
- Stage 3 生命周期和 Stage 4/5 正式业务状态不在 Reminder 层复制或重算。

## 三、S6-T02 Windows Reminder Channel 权威边界

- `DailyReminderRuntimeCoordinator` 只编排 S6-T01 判断、Windows 提醒渠道和成功登记。
- Windows 原生集中提醒一次显示商品数、最高紧急阶段和待排查任务入口提示；多商品不逐条弹窗。
- 通知失败、登记失败或运行异常写入既有日志且不得阻断主程序；失败不得误登记为成功。
- 当前交付不包含 Toast、AppUserModelID、COM 注册、通知点击导航、Snooze、声音或提醒历史。

## 四、S6-T03 Tray / Scheduler 权威边界

- 当前用户会话命名 Mutex 保证单实例；重复启动不得产生第二套 scheduler、托盘图标或主窗口。
- 关闭主窗口只隐藏并驻留托盘；托盘打开恢复同一个窗口，显式退出停止 scheduler、移除图标并终止应用。
- 单个可取消 WPF `DispatcherTimer` 负责启动检查、到点检查、下一本地业务日安排和失败重试；scheduler 不维护第二套“今日已提醒”状态。
- 前台、最小化、隐藏或托盘状态不改变 S6-T01/S6-T02 业务结果。

## 五、S6-T04 Reminder Settings / Autostart 权威边界

- 每日提醒时间只使用现有 `Settings.ReminderMinuteOfDay`；UI 不维护第二份值。合法 `HH:mm` 保存后运行中 scheduler 重新调度。
- 改到今天已过去时间、同日已提醒和跨日行为仍由 S6-T01 判断，设置 UI 不复制幂等规则。
- 开机自启动只使用 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 的本应用值，目标为当前应用 EXE；Windows 实际项是 UI 状态权威。
- 开启/关闭幂等且只影响本应用值；无需管理员权限，不使用 Service、计划任务、机器级注册表或额外后台 EXE。

## 六、冻结的既有业务与数据边界

- Stage 3 Lifecycle、Stage 4 Draft / Reconfirm / InventoryAdjustment / Submission、Stage 5 History / Revision 全部保持权威，Stage 6 不重写。
- Excel 继续是局部增量数据而非全量快照；Stage 6 不改变导入、库存、批次、任务聚合或正式提交语义。
- schema、EF Configuration、DbContext、8 条 migration、target framework 和工程依赖没有因 Stage 6 扩大。
- 正式库不得用于写测试；GUI 验收必须使用隔离环境，结束后恢复并核对大小、SHA-256、sidecar、进程和临时目录。

## 七、已知 UI debt

- Stage 6 提醒、托盘和设置 UI 只冻结功能与基本可用性，不追求统一视觉重构。
- 纯视觉不满意不构成 Stage 6 阻断，不创建 S6-T05。
- 既定策略保持：Stage 7 功能完成并验收后，再统一进行全局 UI/UX 重构与视觉收口。

## 八、不属于 Stage 6 的内容

- 数据导出、本地备份、数据恢复、备份完整性与恢复演练属于 Stage 7。
- 安装器、发布签名、Toast 深度系统集成、云同步、多用户同步和性能专项不由本 closeout 授权。
- 本归档不创建、编号、派发或实施 S7-T01；Stage 7 必须由用户另行批准。

## 九、后续不得重写

后续阶段必须复用 S6-T01 的候选与同日幂等、S6-T02 的通知成功登记、S6-T03 的单实例/托盘/scheduler，以及 S6-T04 的 Settings 与当前用户级自启动权威。任何导出、备份、恢复或 UI 工作不得建立平行 Reminder 状态机、后台 Service 或第二套业务数据解释。
