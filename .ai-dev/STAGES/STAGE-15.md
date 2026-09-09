# Stage15｜在线更新界面优化

日期：2026-09-09（Asia/Shanghai）

Stage15 = `IN_PROGRESS / S15_T01_CLOSED / S15_T02_TECHNICALLY_ACCEPTED / WAITING_USER_GUI_ACCEPTANCE`

当前唯一任务：`S15-T02｜更新提示恢复完整门店版更新说明`。

## S15-T01 已关闭范围

- 初始界面只显示“发现新版本”、当前版本、最新版本、“稍后提醒 / 立即更新”；不显示取消更新、Release Body 或技术状态。
- 点击“立即更新”后，门店可见状态仅为“下载中 / 更新中 / 安装中”。下载只显示整数百分比和正常进度条；更新中使用不确定进度；交给独立 Updater 前显示安装中。
- “取消更新”和关闭窗口取消只复用现有安全下载 cancellation；进入更新中或安装中后隐藏/禁用，不新增取消状态机。
- “稍后提醒”及其固定二次告知保持原样。

## 禁止扩围

- 不改左侧导航及其他业务 UI，不改非强制更新规则、检查频率、NormalLaunchHandshake、Updater phase/journal/rollback、manifest/signature、SQLite/migration/ModelSnapshot、托盘或每日提醒。
- 不新增状态机、依赖、后台服务、协议字段或数据库字段。
- 默认 `NO_FULL`；只运行直接相关专项与必要 Release build。动态测试不得访问正式 SQLite、正式安装根、数据根或备份。
- 不 tag、不 Release、不发布 v1.0.6、不创建 S15-T03。

任务与验收见 `../TASKS/S15-T01.md`、`../ACCEPTANCE/S15-T01.md`、`../TASKS/S15-T02.md`、`../ACCEPTANCE/S15-T02.md`。

## 当前执行结果

- Terra 隔离提交：`2fce2c999c06302622fd3b77623109c7e0ecd5ba`，未 push/tag/Release。
- 最小专项 `6/6`；Release App build `0 error`，仅 3 个无法获取 NuGet 漏洞元数据的 `NU1900` 网络警告。
- `NO_FULL`；无更新底层协议、Updater/journal/rollback、manifest/signature、SQLite/migration、导航或业务 UI 变化。

## 技术评审与集成

- Sol 已独立读取并审查真实 Terra diff；技术审查 `PASS`，无返修项。
- Terra 实现已无冲突 cherry-pick 为 integration commit `46890b83e15b5a971a6dc6e8418c85de6b841d5a`；六个实现文件与 Terra 提交逐文件等价。
- 原专项 `6/6 PASS`、原 Release build `0 error` 均继承且未重跑；`NU1900 ×3` 为网络漏洞元数据警告，非 blocker；`FULL = NOT_RUN / NO_FULL`。
- 用户已明确回执 `S15-T01 GUI 验收通过`：初始更新窗口简化、下载百分比/进度、更新中/安装中显示及稍后提醒流程均正确。
- S15-T01 = `GUI_ACCEPTED / CLOSED`；既有技术证据继承，本轮未重跑测试、build 或 GUI。

## S15-T02 治理冻结

- S15-T02 = `GOVERNANCE_FROZEN / IMPLEMENTATION_AUTHORIZED / NOT_IMPLEMENTED`。
- 发现新版窗口保持 S15-T01 已验收结构，并增加“本次更新”区域，完整展示经过现有安全字符清理的 GitHub Release Body。
- 客户端不限制条数、不摘要、不删除、不按关键词过滤、不改写 Release 内容；空或仅空白时隐藏标题和内容区，不重复显示 DiagnosticBanner。
- 更新说明区使用固定最大高度和内部滚动，避免长说明无限撑高窗口。
- 允许移除 `SanitizeNotes` 的 1000 字符截断；保留控制字符清理和 GitHub metadata 整体 256KB 安全上限，不改变请求、版本、网络错误或更新底层语义。
- 从下一次正式版本开始，Release Body 本身必须完整记录门店实际可感知的新增、优化与修复，不写开发过程、内部治理证据或仅开发者需要的技术术语。
- 实施只做直接专项与必要 Release App build，`FULL = NO_FULL`；技术接受后，用户只重验“发现新版本”屏，不重复验 S15-T01 三阶段。

## S15-T02 实现结果

- Terra：`0afddbdd5cb42f1f7f9d02ca84d1267842a30e00`；远端分支 `origin/codex/s15-t02-terra` 指向同一 SHA，隔离 worktree clean，尚未合并 main。
- 修改文件：`GitHubReleaseUpdateChecker.cs`、`UpdateNotificationViewModel.cs`、`WpfDialogService.cs`、`S9T03UpdateCheckTests.cs`、`S15T01UpdateProgressUiTests.cs`。
- 完整 ReleaseNotes 保留现有控制字符清理与 metadata 256KB 上限；初始窗口显示固定 `MaxHeight` 滚动区，空白时无区域，进入进度态后隐藏；不恢复 DiagnosticBanner。
- 精确专项 `10/10 PASS`；Release App build `0 warning / 0 error`，无 `NU1900`；`FULL = NOT_RUN / NO_FULL`；正式数据库访问 `NO`；scope check `PASS`。
- Terra 实现阶段状态为 `IMPLEMENTED / TARGETED_AND_RELEASE_BUILD_PASSED / READY_FOR_REVIEW`，当时等待 Sol 独立技术评审。

## S15-T02 技术接受与集成

- Sol 独立真实 diff 审查：`PASS / REWORK 0`。
- Terra `0afddbdd5cb42f1f7f9d02ca84d1267842a30e00` 已从 fresh `origin/main@7308d8ff35503b6ebefc699bca3a2f6baacf50f3` 无冲突 cherry-pick 为 `f3cb2451442e5dba540796743f038072be7fd32c`；生产代码与 Terra 等价。
- targeted：`INHERITED 10/10 PASS / NOT_RERUN`；Release build：`INHERITED 0 warning / 0 error / NOT_RERUN`；`FULL = NOT_RUN / NO_FULL`；正式数据库访问 `NO`。
- 过宽 S9T03 `29/30` 的 synthetic WPF core-ready 27 秒超时继续为 `NON_BLOCKER`，未追加测试。
- S15-T02 = `TECHNICALLY_ACCEPTED / WAITING_USER_GUI_ACCEPTANCE`。GUI 尚未执行；`origin/codex/s15-t02-terra` 保留至 Stage15 `CLOSED`。
