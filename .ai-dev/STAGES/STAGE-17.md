# Stage17｜v1.0.7 国内备用更新通道

日期：2026-09-09（Asia/Shanghai）

Stage17 = `IN_PROGRESS / S17_T01_TECHNICAL_ACCEPTANCE_READY / WAITING_USER_GUI_ACCEPTANCE`

当前唯一任务：`S17-T01｜v1.0.7 国内备用版本检测与夸克手动下载兜底`。

## 范围

- GitHub 继续作为唯一主更新通道；既有签名、manifest、SHA256、包下载、Updater、回滚与 ZIP 审计协议冻结。
- 仅当 GitHub 检查结果为 `NetworkUnavailable` 或 `RateLimited` 时，后台读取固定 Gitee Raw `latest.json`。
- Gitee 只提供版本、更新说明与人工夸克下载入口；不自动下载、安装、退出应用或启动 Updater。
- App/Updater 候选版本目标为 `1.0.7`；本阶段不 tag、不 Release、不上传正式夸克安装包、不把 Gitee `latest.json` 改为正式 1.0.7。

## 边界

- Stage16 继续预留“未来效期风险总览”，本阶段不得修改或占用。
- 不改数据库 Schema、migration、ModelSnapshot、安装结构、业务页面、导航、托盘或每日提醒。
- 当前 Sol 只负责治理与独立技术验收；生产实施必须由本 Task 全新 GPT-5.6 Terra 在 fresh clean worktree 完成。
- 技术验收后停止在 `IMPLEMENTED / TECHNICAL_ACCEPTANCE_READY / NOT_ACCEPTED`，等待用户本人 GUI 验收。

## 当前进度

- Terra 实现：`ee83b0c10f252d1ee136464e0a2bb6d09452a34f`，已由 Sol 独立审查并快进纳入 `origin/main`。
- S17 专项 `27/27 PASS`；S9T04/S14T01/S15T01/S17 组合 `56/56 PASS`；S9T03 非 STA/WPF `21/21 PASS`。
- S9T03 单一 synthetic WPF core-ready 用例在候选与治理基线均约 26 秒同样超时，记为基线同现 `NON_BLOCKER`，不冒充 PASS。
- Release App/Updater build 均 `0 warning / 0 error`；EF 无漂移；migration `9`；App/Updater FileVersion 均 `1.0.7.0`。
- 当前为 `TECHNICAL_ACCEPTANCE_READY / NOT_ACCEPTED`，只等待用户 GUI 验收；禁止 tag、Release、夸克正式上传或 Gitee 正式 1.0.7 写入。

任务与验收见 `../TASKS/S17-T01.md` 与 `../ACCEPTANCE/S17-T01.md`。
