# Stage 6 整体验收记录｜Windows 每日提醒、托盘与自启动

- 验收日期：2026-08-31
- 结论：通过；Stage 6 正式完成，暂停在 Stage 6 → Stage 7 门禁
- 验收范围：`S6-T01` 至 `S6-T04`
- 总验收前基线：`master @ dd79fb57f10286af58ae4a2d788beeb28db216b2`，clean
- 独立总验收：Codex Sol
- Windows GUI 验收：用户本人

## 四卡交付结论

| Task | 已交付能力 | 实现提交 | 验收提交 | 结论 |
|---|---|---|---|---|
| S6-T01 | 每日提醒候选、到期判断、同日幂等与成功登记 | `5c0ab6a313813f28976ed4b9f4bc7e5c42aa4f3e` | `18003f74b7e09bc90ab3d221150d8f8a8b0f7b55` | 通过 |
| S6-T02 | Windows 集中提醒展示与应用启动运行时接入 | `673391ad2f6d0d940fa139680f7fc5635c4c0cb1` | `a65d7e91c36a7a43f314cfe346edad75b5f539d5` | 通过 |
| S6-T03 | 单实例、托盘常驻、主窗口恢复/退出与到点 scheduler | `ea2ae19ab92ab7096c96e1b2745cd6132fc72c0d` | `cbfb2876a84db5bbd8d6e1d65423583e8c00ac06` | 通过 |
| S6-T04 | 提醒时间设置、运行中重新调度与当前用户级开机自启动 | `f6fdd2495e5b42200b046153b3b65a06790d60d6` | `dd79fb57f10286af58ae4a2d788beeb28db216b2` | 通过 |

四份原始任务执行单 `S6-T01.md`～`S6-T04.md` 与仓库内四份验收记录均已核对。仓库未补建或改写历史任务卡；未创建 S6-T05、S7-T01 或其他 Stage 7 Task。

## 最终独立技术门禁

所有测试于 2026-08-31 使用 Release 配置和验收记录登记的精确测试类过滤重新运行；TRX 暂存于 `obj/Stage6Closeout`。

| 门禁 | 实际结果 |
|---|---|
| S6-T01 `DailyReminderUseCaseTests` | 13/13 |
| S6-T02 `S6T02DailyReminderRuntimeTests` | 10/10 |
| S6-T03 `S6T03TrayAndReminderSchedulerTests` | 12/12 |
| S6-T04 `S6T04SettingsAndAutoStartTests` | 17/17 |
| Stage 5 四个权威测试类 | 51/51 |
| Stage 4 八个权威测试类 | 179/179 |
| Stage 3 十个权威测试类 | 170/170 |
| Release 全量 | 635/635，0 失败、0 跳过 |
| Release build | 0 warning、0 error |
| EF 模型 | `has-pending-model-changes`：无漂移 |
| migration | 仓库 8 条；正式库与已验证同哈希基线一致，仍为 8 条 |
| dependency | 相对 Stage 5 收口基线工程依赖 diff 为空；未进行在线漏洞审计成功声明 |
| Git | 总验收开始时 clean；`git diff --check` 通过 |

Stage 5 精确类为 `InspectionHistoryQueryTests`、`InspectionHistoryEditUseCaseTests`、`S5T03InspectionHistoryViewModelTests`、`S5T04InspectionHistoryEditViewModelTests`。Stage 4 与 Stage 3 继续使用 Stage 5 总验收登记的八类与十类权威过滤；Stage 3 本次实际为 170/170，不改写 Stage 4 历史归档中的旧 184/184。

首次直接 build 因工具环境无法访问 NuGet 漏洞源出现一条 `NU1900`。随后按既有离线门禁以 `NuGetAudit=false` 刷新 restore 并重新 build，最终实际结果为 0 warning / 0 error；这不等同于在线漏洞审计成功。

## 产品边界总审查

1. 每日提醒候选只读取现有 open 待排查任务及其权威最高阶段，不复制 Stage 3～5 状态机。
2. 默认提醒时间保持 10:00，使用本地日期与时间，可通过现有 Settings 单例配置并持久化。
3. 同一业务日正常提醒最多一次；跨日后可重新提醒，时钟回拨不能绕过 `LastReminderDate` 门禁。
4. 多商品合并为一次 Windows 提醒；只有通知成功后才登记，失败或异常不误登记且不阻断应用启动。
5. 应用持续运行时由单个 scheduler 到点触发；前台、最小化或托盘状态不改变 Reminder 业务结果。
6. 主窗口关闭后驻留托盘；托盘恢复同一窗口，显式退出停止 scheduler、移除托盘图标并结束进程。
7. 当前用户会话单实例门禁防止产生第二套 scheduler 或托盘图标。
8. 提醒时间修改后 scheduler 使用同一份持久化配置重新调度；同日已提醒后修改时间不能产生第二次正常提醒。
9. 开机自启动使用当前用户级 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，可开启、关闭并以 Windows 实际值为权威；不创建 Service、计划任务或独立后台程序。
10. Stage 3 生命周期、Stage 4 排查提交与 Stage 5 历史/Revision 权威行为均保持，schema、migration 与依赖未发生未经批准的扩大。

上述二十一项执行单边界均由专项、回归、代码范围审查和用户 Windows 验收共同闭环，无阻断缺口。

## Windows GUI、正式数据与 UI debt

- S6-T02 用户验收：隔离数据首次启动出现一次 2 商品集中提醒，最高阶段“过期”；同日第二次启动无重复正常提醒。
- S6-T03 用户验收：关闭主窗口后托盘常驻、托盘恢复同一窗口、到点提醒、托盘显式退出和进程归零均通过。
- S6-T04 用户验收：提醒时间保存及再次读取、自动启动开启/关闭、Windows 实际项核验、同日不重复提醒和托盘退出均通过；开启/关闭回执分别为 `AUTOSTART_ON_PASS`、`AUTOSTART_OFF_PASS`。
- 最新恢复回执为 `RESTORE_PASS`：正式数据库 299008 bytes，SHA-256 `F3D423DF14B882D7BFE87780A81CF5879F074AF4880601CBEDB6B475A964F522`，应用进程 0，原自动启动状态已恢复。
- 恢复后未再次启动应用。S6-T02/T03/T04 GUI、Reminder、Tray、Scheduler 隔离目录及完成副本均已清理；历史正式备份继续保留。
- 工具侧默认 `LOCALAPPDATA` 路径仍受既有异常 Junction 限制，无法在本轮再次直读哈希或连接正式库；本记录采用用户本轮恢复回执、相同文件大小及恢复后未启动事实，不将工具侧失败冒充通过。正式库与已验证基线逐字节哈希一致，因此其已验证 8 migration 状态保持。
- Stage 6 UI 已满足功能与基本可用性。纯视觉问题只登记为 UI debt，不创建 S6-T05；Stage 7 完成后统一进行全局 UI/UX 重构与视觉收口。

## 范围与最终结论

- 本次总验收只新增治理文档并最小更新项目状态；未修改生产功能、schema、migration、依赖或历史验收记录。
- 未创建或派发新的 Luna；未创建 S6-T05、S7-T01 或其他 Stage 7 Task；未实施导出、备份、恢复或任何 Stage 7 生产代码。
- Stage 6 四张任务全部正式完成并通过最终总验收。Stage 6 正式归档；Stage 7 尚未开始，必须等待用户单独批准。
