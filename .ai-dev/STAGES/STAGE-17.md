# Stage17｜v1.0.7 国内备用更新通道

日期：2026-09-09（Asia/Shanghai）

Stage17 = `CLOSED / S17_T01_RELEASED`

当前唯一任务：`S17-T01｜v1.0.7 国内备用版本检测与夸克手动下载兜底`。

## 范围

- GitHub 继续作为唯一主更新通道；既有签名、manifest、SHA256、包下载、Updater、回滚与 ZIP 审计协议冻结。
- 仅当 GitHub 检查结果为 `NetworkUnavailable` 或 `RateLimited` 时，后台读取固定 Gitee Raw `latest.json`。
- Gitee 只提供版本、更新说明与人工夸克下载入口；不自动下载、安装、退出应用或启动 Updater。
- App/Updater 候选版本为 `1.0.7`；用户已授权按冻结发布门禁创建正式 tag/Release、夸克版本目录和 Gitee 正式 metadata。

## 边界

- Stage16 继续预留“未来效期风险总览”，本阶段不得修改或占用。
- 不改数据库 Schema、migration、ModelSnapshot、安装结构、业务页面、导航、托盘或每日提醒。
- 当前 Sol 只负责治理与独立技术验收；生产实施必须由本 Task 全新 GPT-5.6 Terra 在 fresh clean worktree 完成。
- 用户 GUI 验收及全部正式发布门禁均已完成。

## 当前进度

- Terra 实现：`ee83b0c10f252d1ee136464e0a2bb6d09452a34f`，已由 Sol 独立审查并快进纳入 `origin/main`。
- S17 专项 `27/27 PASS`；S9T04/S14T01/S15T01/S17 组合 `56/56 PASS`；S9T03 非 STA/WPF `21/21 PASS`。
- S9T03 单一 synthetic WPF core-ready 用例在候选与治理基线均约 26 秒同样超时，记为基线同现 `NON_BLOCKER`，不冒充 PASS。
- Release App/Updater build 均 `0 warning / 0 error`；EF 无漂移；migration `9`；App/Updater FileVersion 均 `1.0.7.0`。
- 用户 GUI A/B/C 三场景均 `PASS`，S17-T01 = `GUI_ACCEPTED / RELEASE_AUTHORIZED`。
- 真实 Release App 入口程序集、FileVersion 均为 `1.0.7.0`，ProductVersion 为 `1.0.7+ee83b0c...`；左下角读取入口程序集三段版本，隔离真实 App 启动稳定。一次性 harness 的 `v1.0.0` 来自 harness 自身入口程序集，不是生产缺陷。
- 唯一候选四资产已从精确产品 source 冻结且 production signature `PASS`；夸克真实 Setup 大文件预检 `PASS`。
- annotated tag `v1.0.7` 精确指向 `ee83b0c10f252d1ee136464e0a2bb6d09452a34f`；GitHub Release ID `385663956` 已公开，四项公网资产 bytes/SHA256 与冻结候选逐项全等，production RSA-PSS/SHA256 复验 `PASS`。
- 同一冻结 Setup 已上传 `门店效期排查软件/v1.0.7/StoreExpiryInspector-Setup-1.0.7.exe`，重新读取为 `75340490` bytes；正式分享链接 <https://pan.quark.cn/s/fb380ba2b338>。
- Gitee `master/latest.json` 已由 SSH commit `ba957a0e024633ef2177ca44c994672142149951` 发布；固定 Raw 匿名 HTTP `200`，三字段精确一致。`FULL = NOT_RUN / NO_FULL`；Stage16 与正式 dirty 工作区未触碰。

任务与验收见 `../TASKS/S17-T01.md` 与 `../ACCEPTANCE/S17-T01.md`。
