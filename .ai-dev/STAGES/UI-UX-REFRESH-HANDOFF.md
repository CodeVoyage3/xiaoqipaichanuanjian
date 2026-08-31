# UI/UX 统一重构 Handoff

日期：2026-08-31。本文件只交接事实、已知体验债与冻结边界，**不是实施授权，不是 Task 卡**。用户决定 Stage 7 收口后先统一修改整体 UI/UX，再继续后续 Stage；实际开工仍须单独批准。本轮不创建 UI/UX 实施任务、不派发 Luna、不修改生产界面。

## 接任依据

- Stage 7 总验收：`../ACCEPTANCE/STAGE-7.md`；产品契约：`STAGE-7-CLOSEOUT.md`。
- S7-T03 最终实现：`da8b43233fe965784701283a5ecf0839f72750ed`；技术记录：`c65000b087d142d23ba98a3c74013f221fddc2ae`；用户验收归档：`11ae76054f90c27839c9fa4e442334c7f7feef6c`。
- 接任时重新执行 `git branch --show-current`、`git rev-parse HEAD`、`git status --short`，不得把上述实现或历史归档 SHA 当作届时最新 HEAD。
- 视觉参考：`../UI/STAGE-4-UI-REFRESH-BASELINE.md` 及其登记的五张用户定稿图；更早 `STAGE-4-UI-BASELINE.md` 只作来源索引。原 Stage 4 文档中历史/设置尚未开放的描述已经被 Stage 5～7 实际交付取代，不得按旧图移除现有功能。
- 主界面：`src/StoreExpiryInspector/UI/MainWindow.xaml`；Shell 与页面状态：`UI/Stage4ViewModels.cs`。下文 UI / Application / App.xaml.cs 入口均位于仓库的 `src/StoreExpiryInspector/` 下。

## 当前产品页面与不可丢失的操作

| 功能面 | 当前事实与代码入口 | 必须保持 |
| --- | --- | --- |
| Dashboard / 首页 | MainWindow + Stage4ViewModels；Application/Tasks/InspectionTaskQuery.cs | 汇总与优先处理来自权威查询；最近成功导入时间；条码和显式进入排查 |
| 待排查列表 | MainWindow + Stage4ViewModels | 名称/条码/编码搜索、阶段与库存事实、单元格复制、明确打开详情 |
| 排查详情 | UI/InspectionDetailViewModel.cs + MainWindow | 正常批次折叠、草稿自动保存、保存状态、重新确认、主动清空、库存修正、正式提交与超库存确认 |
| Excel 导入 | UI/ImportViewModel.cs + MainWindow | 选文件、预览/错误、身份与陈旧性守卫、取消/确认、忙碌禁重入；Excel 是局部增量，不是全量快照 |
| 排查历史 | UI/InspectionHistoryViewModel.cs + MainWindow | 正式历史列表、快照详情、返回/刷新、条码与编码复制，不从当前数据反推历史 |
| Revision 查看 / 修改 | 同一 History ViewModel，Application 既有查询/修订用例 | 明确单条目标、非负整数、二次确认、同值不写、Revision 留痕、反馈不随编辑容器隐藏 |
| Reminder 设置 | UI/MainWindow.xaml.cs 的现有模态设置窗口 | HH:mm 验证、保存后重新调度、Windows 实际自启动项为权威，不维护第二份开关状态 |
| 托盘 / Windows | App.xaml.cs、UI/WindowsTrayIcon.cs、Application/Reminders/DailyReminderScheduler.cs | 单实例、隐藏/恢复同一窗口、显式退出；备份/恢复忙碌与锁定时的例外门禁 |
| 数据备份与恢复 | UI/DatabaseBackupRestoreViewModel.cs、UI/DatabaseRuntimeGate.cs、Application/Backups | 已验证列表、手动备份、明确目标和默认取消的危险确认、维护态、恢复后要求退出/重启、严重失败锁定 |

上述路径用于定位当前代码，不允许为了对齐页面名称创建第二套 Shell、平行 DTO 或用例。下一任应先核对实际类名与调用，再形成获批的最小实施范围。

## 统一重构原则

1. 只调整视觉、布局、信息层级和必要交互表达；Application / Domain 仍是唯一业务权威。不得重新实现 Stage 3～7 算法、事务或状态机。
2. 不改 schema、migration，不新增业务功能；依赖变化须另行批准。不得顺便增加导出、自动备份、恢复历史或后台服务。
3. 保留门店高频识别字段**商品条码**，与名称/商品编码共同清晰可读、可复制；不能用美化为由移除必要列。
4. 保留高信息密度、明确动作、少点击和长期桌面操作习惯。表格层级、对齐、分隔与状态文本优先于装饰；不得用隐藏操作换取画面简洁。
5. 1024×600 与 Windows 125% 必须基本可用；不得提高窗口最小尺寸来规避。保留滚动、焦点、Tab/Enter/Esc、读屏名称及颜色之外的状态文字。
6. Primary / Secondary / 危险操作可统一视觉，但不能改变确认、取消默认值、忙碌互斥、公开方法守卫或当前操作目标。
7. 真实 Windows GUI 由用户本人验收，Sol / Luna 不使用电脑操控代验。静态 XAML、ViewModel 和 build 不等于 GUI 通过。

## 已知体验债与回归注意点

下表区分用户希望统一的方向、已修复的历史问题及本次实际操作事实；不能把所有项目写成当前功能缺陷。

| 方向 | 已知来源 / 事实 | 后续只在获批范围评估 |
| --- | --- | --- |
| 整体视觉一致性 | S4-T10 为五张定稿页面建立基线；Stage 5～7 沿用控件但以功能交付为主，Stage 6 closeout 明确保留统一视觉债 | 跨页面字体、间距、边框、状态和控件一致性 |
| 信息密度 | S4-T10 已收紧详情表格留白并通过用户验收，用户重视产品图紧凑表格 | 维持密度，不退回大留白、卡片套卡片或大量点击 |
| 页面层级 | 首页、列表、详情、历史、Revision 与数据保护承担不同任务 | 标题、上下文、返回位置和主操作层级的一致表达，不合并业务状态 |
| 表格与详情阅读性 | S5-T03 曾有列头裁切和选择绑定问题，已修复并验收；S5-T04 要求结果消息始终可见 | 列宽、长文件名、数字/日期对齐及窄窗口可达；不得回退已修复的选择/复制语义 |
| 导航一致性 | Stage 5～7 已增加历史、设置及备份恢复；旧 Stage 4 disabled 项只属历史 | 保持功能可发现、当前位置清晰；不能绕过离开保存或维护锁 |
| 设置 / 数据保护 | 设置为原生模态窗口，数据保护为 Shell 页；两者都已功能验收 | 统一表达与操作层级，但保留设置保存实际语义及恢复退出要求 |
| Primary / Secondary | 页面同时包含刷新、确认、返回、保存、清空、库存修正与恢复 | 主次清楚、危险动作可辨；不把恢复变成普通默认动作 |
| 空 / 错误 / 高风险状态 | T03 已区分无备份、加载错误、普通失败、critical 与恢复成功锁定 | 统一表达且保留可区分性；不得把目录错误渲染为空列表，或把 critical 当作可继续操作 |
| 备份身份辨认 | T03 人工演练首次当前库与 B 一致而核验目标为 A；再次核对确认框 A 后字节核验通过。首次实际选中项缺少证据，未认定绑定缺陷 | 评估列表选中项、创建时间、完整身份与确认框的可读性；不得默认选择最新备份或自动恢复 |
| 验收指引 | 用户曾直接运行示例占位文件名或裸文件名，产生终端错误 | 后续用户材料应区分可执行命令与参考文件名；这不是数据库恢复逻辑缺陷 |

来源：`../ACCEPTANCE/S4-T10.md`、`S5-T03.md`、`S5-T04.md`、`S6-T04.md`、`S7-T03.md`，以及 `STAGE-6-CLOSEOUT.md`。当前无已证实、未处理的核心 GUI 阻断项；本文件不生成新的设计定稿。

## 业务与数据保护红线

- Stage 3 Lifecycle 与 Stage 4 Draft/Reconfirm/InventoryAdjustment/Submission 的唯一权威不能改写；UI 展示的 readiness 不等于正式提交授权。
- Stage 5 历史与 Revision 不重放旧提交或旧 0 件生命周期。Stage 6 的同日一次、通知成功登记、单实例、scheduler 与 HKCU 自启动不建立平行状态。
- Stage 7 查询/备份/恢复必须调用既有 Application。维护前等待现有操作与 Draft 稳定保存；暂停 scheduler，阻止新 DB 操作。成功或 critical 后保持锁定并显式退出，不使用旧运行态继续写入。
- 不能为减少提示移除备份身份校验、恢复前保护、默认取消的二次确认、staging/回退、sidecar 清理或最终 SHA/integrity/migration 核验。
- 用户 GUI 使用隔离运行目录。正式目录和历史备份保护规则、原自启动状态恢复、进程归零、Finish 后不再启动均保留；不要重跑已完成的 T03 Prepare。Junction 异常不由 UI 重构擅自修复。

## 后续门禁

- “正式排查历史 / 结果 Excel 导出”已 Deferred，仅未来用户重新批准后才可讨论实施；不做今日待排查任务、Draft 或数据库原始表导出。
- 下一步只能先由用户单独批准 UI/UX 统一重构范围。本 handoff 不创建、编号或派发任何实施 Task。
- 新的已批准 Stage Task 仍遵守一张任务对应全新 Luna（max），同卡修复沿用同一 Luna，Sol 独立验收；本轮不创建任何 Luna。
- Stage 8 原规划是稳定性 / 性能，必须等待 **Stage 7 收口 → UI/UX 统一重构完成 → 用户另行批准**。不创建 S8-T01，不把 UI/UX 偷并入 Stage 8。
