# 最新交接

## 当前任务与状态

`V1-F02｜效期节点提前 3 天预提醒` 已完成实现、Sol 独立技术验收、用户真实 WPF GUI 验收和空白运行库清理。

当前状态：`V1-F02_ACCEPTED / CLOSED / WAITING_NEXT_APPROVAL`

## 当前 Git

- 分支：`master`。
- 开工治理提交：`61a057e41cff5f0a04e5489efc90c38ae2a28078`。
- 实现提交：`ec5f897`、`2bd799b`。
- 本交接所在治理提交将在 push 后成为 GitHub `main` 最新基线。

## V1-F02 最终事实

- 四节点 `ReminderDate = StageEffectiveDate - 3 个日历日`，窗口为 `ReminderDate <= BusinessDate < StageEffectiveDate`。
- 预提醒不提前改变 Stage，不创建或伪造 ProductTask、Inspection、Revision、LifecycleEvent、HandledAttentionVersion 或其他业务事实。
- 只有 Managed、批准 policy/version 1、匹配完成 ScopeBaseline、库存为正且 active 的 Batch 可进入；Excluded / Unresolved 等非法范围零提醒。
- 正式 Task 与预提醒只形成一次集中 Windows 通知；同日幂等、日期回拨、失败重试、scheduler、托盘、提醒时间和自启动继续复用 Stage 6 权威。
- Schema 足够且未修改；migration 保持 9，无新增依赖或项目文件变化。

## 最终验收

- 定向回归 88/88；Release 新鲜完整复跑 764/764；build 0 warning / 0 error。
- EF 无漂移；migration=9；`git diff --check` 通过。
- 用户真实 WPF：仅预提醒四类各 1、总数 4、无假 Task；同日未重复；正式 Task + 四类预提醒只弹一次，总数 5、分区清楚。
- 用户退出后 Fresh 回执通过：当前运行数据库已替换为全新空库，隔离标记已移除；旧数据旁置保留，不删除。应用进程在替换前为 0。
- 详细证据：`.ai-dev/ACCEPTANCE/V1-F02.md`。

## 停点

- V1-F02 已关闭，不得自动进入 V1-F03、Stage 8、Stage 9 或其他任务。
- policy_version 继续固定为 1；migration 继续为 9。
- 等待产品经理下一次单独批准。
