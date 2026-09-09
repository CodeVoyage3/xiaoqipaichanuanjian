# Stage15｜在线更新进度界面简化

日期：2026-09-09（Asia/Shanghai）

Stage15 = `IN_PROGRESS / GOVERNANCE_FROZEN / IMPLEMENTATION_AUTHORIZED`

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
