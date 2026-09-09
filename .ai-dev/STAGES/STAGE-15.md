# Stage15｜在线更新进度界面简化

日期：2026-09-09（Asia/Shanghai）

Stage15 = `IN_PROGRESS / S15_T01_TECHNICALLY_ACCEPTED / WAITING_USER_GUI_ACCEPTANCE`

唯一任务：`S15-T01｜在线更新进度界面简化`。

## 冻结范围

- 初始界面只显示“发现新版本”、当前版本、最新版本、“稍后提醒 / 立即更新”；不显示取消更新、Release Body 或技术状态。
- 点击“立即更新”后，门店可见状态仅为“下载中 / 更新中 / 安装中”。下载只显示整数百分比和正常进度条；更新中使用不确定进度；交给独立 Updater 前显示安装中。
- “取消更新”和关闭窗口取消只复用现有安全下载 cancellation；进入更新中或安装中后隐藏/禁用，不新增取消状态机。
- “稍后提醒”及其固定二次告知保持原样。

## 禁止扩围

- 不改左侧导航及其他业务 UI，不改非强制更新规则、检查频率、NormalLaunchHandshake、Updater phase/journal/rollback、manifest/signature、SQLite/migration/ModelSnapshot、托盘或每日提醒。
- 不新增状态机、依赖、后台服务、协议字段或数据库字段。
- 默认 `NO_FULL`；只运行直接相关专项与必要 Release build。动态测试不得访问正式 SQLite、正式安装根、数据根或备份。
- 完成实现、最小专项和必要 Release build 后停止；不 tag、不 Release、不发布 v1.0.6、不创建 S15-T02。

任务与验收见 `../TASKS/S15-T01.md`、`../ACCEPTANCE/S15-T01.md`。

## 当前执行结果

- Terra 隔离提交：`2fce2c999c06302622fd3b77623109c7e0ecd5ba`，未 push/tag/Release。
- 最小专项 `6/6`；Release App build `0 error`，仅 3 个无法获取 NuGet 漏洞元数据的 `NU1900` 网络警告。
- `NO_FULL`；无更新底层协议、Updater/journal/rollback、manifest/signature、SQLite/migration、导航或业务 UI 变化。

## 技术评审与集成

- Sol 已独立读取并审查真实 Terra diff；技术审查 `PASS`，无返修项。
- Terra 实现已无冲突 cherry-pick 为 integration commit `46890b83e15b5a971a6dc6e8418c85de6b841d5a`；六个实现文件与 Terra 提交逐文件等价。
- 原专项 `6/6 PASS`、原 Release build `0 error` 均继承且未重跑；`NU1900 ×3` 为网络漏洞元数据警告，非 blocker；`FULL = NOT_RUN / NO_FULL`。
- GUI 最终验收尚未执行。保留 `origin/codex/s15-t01-terra`，直到 Stage15 最终 `CLOSED`。
