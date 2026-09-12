# Stage19｜首次冷启动与今日排查回导体验优化

日期：2026-09-12（Asia/Shanghai）

Stage19 = `CLOSED / ACCEPTED`

## 2026-09-12 Stage19 final closeout

- S19-T01｜首次冷启动与今日排查回导优化：`CLOSED / ACCEPTED`，历史验收事实保持不变。
- S19-T02｜在线更新下载失败国内兜底：`CLOSED / ACCEPTED`；final product source=`db36d34d538949504ce3bf3276ee70b8ea2b4fd1`，technical=`PASS`，用户 GUI=`PASS`。
- S19-T02 GUI 确认第二阶段网络失败、同窗三按钮、已验证 `pan.quark.cn` 打开、重试单 worker/fallback 清理及取消继续运行均 PASS。
- `FULL=NOT_RUN / NO_FULL`；本最终收口轮不重跑既有测试、GUI smoke 或 Release build。
- 未修改版本号，未 tag、未发布 Installer/GitHub Release/夸克正式文件或 Gitee 正式 metadata。
- 后续版本规划：废弃 `v1.0.10`，下一正式版本统一为 `1.1.0`（Updater `1.1.0.0`、tag/Release `v1.1.0`），须在 Stage19 进入 main 后另开正式发布流程；本轮不实施。

## 2026-09-12 S19-T02 technical acceptance ready

最终实现=`db36d34d538949504ce3bf3276ee70b8ea2b4fd1`，Sol 独立 `TECHNICAL_REVIEW=PASS`。最终范围精确为四个批准生产文件和一个新增专项；S19+S17+S15=`44/44 PASS`，final Release App build=`0 warning / 0 error`，`FULL=NOT_RUN / NO_FULL`。S17 第一阶段 fallback、GitHub 权威身份及全部签名/哈希/归档/Updater 安全链保持。

GUI candidate 技术就绪：生产程序集绑定 `db36d34`，独立 smoke PASS，未访问正式数据库或真实 GitHub/Gitee。S19-T02=`IMPLEMENTED / TECHNICAL_ACCEPTANCE_READY / NOT_ACCEPTED`，只等待用户 GUI；不得关闭 Stage19、push main 或发布。

## 2026-09-12 S19-T02 governance frozen

`S19-T02｜在线更新下载失败国内兜底` 已建立独立 Task/Acceptance，状态=`GOVERNANCE_FROZEN / IMPLEMENTATION_DISPATCH_AUTHORIZED / NOT_IMPLEMENTED / NOT_ACCEPTED`。仅 GitHub 已确认目标版本后，包准备/下载返回 `NetworkUnavailable` 或 `RateLimited` 才查询现有 Gitee metadata；只有 Gitee 同版本且 URL 已通过现有 `pan.quark.cn` HTTPS 门禁才显示 `[点击网盘下载] [重试在线更新] [取消]`。其余 outcome（含 `SizeMismatch`）全部 `NO_DOMESTIC_FALLBACK`。

S19-T02 允许修改 App 侧在线更新编排、现有更新 ViewModel/Dialog 与最小测试；禁止修改独立 `StoreExpiryInspector.Updater`、GitHub/Gitee checker、`SignedUpdatePackageDownloader`、安全协议、版本、Schema/migration、Installer 或发布状态。`FULL=NOT_RUN / NO_FULL`；技术通过后仍须等待用户 GUI。

## 2026-09-12 S19-T01 final acceptance

用户最终 GUI 验收=`PASS`；S19-T01=`CLOSED / ACCEPTED`，technical=`PASS`，GUI=`PASS`。R3 真实 GUI 180/80/100 闭环与 R4 三个最终 UI 画面全部通过；`FULL=NOT_RUN / NO_FULL`。Stage19 不关闭，等待用户在新 Codex 话题处理 `S19-T02｜在线更新下载失败国内兜底`；本轮不创建或实施 S19-T02。

## 2026-09-12 R4 final UI repair historical

用户复验发现默认绿色 ProgressBar、原生 ComboBox 与 hover 布局异常三项纯 UI blocker。repair=`27b73f6` 仅加入蓝色自定义不定进度条、五项横向轻量筛选及数量，并把确认窗口改为从创建即固定测量、受控列宽、无结果 ToolTip；不重新判断 valid/stale，不改 PreviewResult、Submit payload 或业务逻辑。精准专项 `4/4 PASS`，Release App build `0 warning / 0 error`，FULL=`NOT_RUN / NO_FULL`。当前 `R4_UI_REPAIR_IMPLEMENTED / FINAL_GUI_RETEST_READY / NOT_ACCEPTED`；用户只复验 A/B/C，Stage19 不关闭。

## 2026-09-12 R4 initial UI polish historical

用户 GUI 已确认 R3 正式提交与业务闭环 PASS；5000 行 loading 功能 PASS，仅视觉反馈过弱，确认表仅靠颜色/Tooltip 不够可读。R4=`443e926` 只增加 TodayInspection 居中原生不定进度 overlay、可见处理结果/原因列和基于既有 Preview 分类的轻量筛选；不改 Preview 数据或提交 payload。精准专项 `4/4 PASS`，Release App build `0 warning / 0 error`，FULL=`NOT_RUN / NO_FULL`。当前 `R4_UI_POLISH_IMPLEMENTED / FINAL_GUI_RETEST_READY / NOT_ACCEPTED`；用户只复验两个画面，Stage19 不关闭。

## 2026-09-12 GUI blocker R3

R2 GUI 已通过预览 80/100/0，但正式提交仍因 `InspectionSubmissionUseCase` 把合法 cold-start 0/0 解释为已处理而跳过 80/80。R2 隔离库逐项核对证明其余提交门禁全部有效且未命中。repair=`f38f4cd` 删除 partial 提交的错误相等判断，并确保合法 0/0 剩余 item 可重建；handled 结构边界和其他 stale 保护保持。直接专项 `99/99 PASS`，Release App build `0 warning / 0 error`。新 GUI 只先重验正式提交成功、窗口自然关闭及 80 已提交/100 待排查；PASS 后再恢复其余 A～I，Stage19 不关闭。

## 2026-09-12 GUI blocker R2

用户现场 180 批次填写 80/空 100 后被全部 80 行 stale 阻断，状态曾退回 `GUI_BLOCKER_FOUND_R2 / REPAIR_REQUIRED / NOT_ACCEPTED`。隔离库逐字段证明唯一误杀条件为合法 cold-start `HandledAttentionVersion=0 >= AttentionVersion=0`。repair=`d754f2fff5bb07d98589d0f3d7269eb155671df6` 仅删除该冗余门禁，保留 open item、stage/attention 一致、reconfirmation、库存与 tracking 等保护；直接专项 `13/13 PASS`，Release App build `0 warning / 0 error`。新 GUI 先只重验预览 `80/100/0`，PASS 后再继续提交及 A～I；Stage19 不关闭。

## 2026-09-12 GUI blocker R1 historical

用户现场在 180 商品/批次均正常识别后，被漏列 migration 10 的导入前快照白名单误阻断；A～I 已停止，状态曾退回 `GUI_BLOCKER_FOUND / REPAIR_REQUIRED / NOT_ACCEPTED`。最小 repair=`536bd4126f137328573eaeafb8579e9ff4701bde`，直接专项=`23/23 PASS`，Release App build=`0 warning / 0 error`，无 migration/schema/业务/UI/版本/发布链变化。新 GUI 先只重验 180 批次正式导入，PASS 后再恢复 A～I；Stage19 不关闭。

## S19-T01 历史唯一任务合同

以下“唯一任务”约束只属于 S19-T01 当时的历史范围：`S19-T01｜首次冷启动工作量与排查计划分批回导优化`。S19-T01 已 `CLOSED / ACCEPTED`，该历史表述不阻止已单独授权的 S19-T02。

## fresh baseline

- fresh `origin/main=43d82ec3d0a6e09a0964a81ce0d531e888fd5abc`。
- stable/latest=`v1.0.9`；PRODUCT SOURCE=`9bf4ee71f1579097d816032d867041c0519cf789`。
- Stage18=`CLOSED`，不得重新打开；fresh tree 中不存在 Stage19/S19-T01。
- 正式主工作区历史 dirty：`1 modified + 4 untracked`，且本地 main 落后 origin/main 64；本阶段禁止 reset/clean/stash/restore/delete/commit 这些既有内容。

## 产品合同

1. 首次冷启动仅对“已历史过期”补查窗口改为真实总效期 `1%` 向上取整、最少 `1` 天、最多 `7` 天。当天刚过期、首次收仓、正常运行后的四阶段规则、提前 3 天预提醒和库存 0 规则不变；无生产日期的既有安全基线处理不变。
2. 正式今日排查 Excel 仅输出 A:L 十二个业务字段；M:Y 及一切系统身份/快照列全部删除。文件名不参与识别。
3. 回导业务身份只认当前数据库事实：有生产日期为 `ProductCode + ProductionDate + ExpiryDate`；无生产日期为 `ProductCode + ExpiryDate`。禁止商品名称、条码或模糊匹配。
4. 空白表示未排查并跳过；`0` 是已排查且数量为零。有效非空行独立接受，空白/填写错误/状态变化/无法唯一匹配只跳过本行，不拖垮其他有效行。
5. 每次导出都是当时数据库的当前快照，不是上次文件续表；旧 Excel 非空行必须重新核对商品、批次、open task item、库存、阶段和 attention 状态。
6. 文件不存在、非 xlsx、占用、损坏、工作表缺失或 A:L 核心结构破坏属于文件级失败；用户只见大白话原因和下一步，技术异常完整写日志。
7. 读取超过短暂阈值时显示不确定进度提示；成功、失败、取消均可靠关闭，不显示虚假百分比。
8. 完成反馈至少区分有效、未填写、状态变化、填写错误，并允许查看/复制不含异常堆栈的行级详情。

## 技术判断：MINIMAL SCHEMA CHANGE AUTHORIZED

实施后 isolated SQLite 专项证明：`BatchBaselineConfiguration` 与现有 migration 的 `CK_batch_baselines_catchup_window` 仍限定 `catchup_window_days BETWEEN 3 AND 30`，会拒绝新合同合法产生的 `1`、`2`、`7` 天。业务身份唯一索引判断仍成立，但冷启动合同无法在 `NO SCHEMA CHANGE` 下落地。

用户于 2026-09-12 明确批准新增第 10 条 migration：既有第 9 条 migration 不可修改；仅把 `CK_batch_baselines_catchup_window` 从 `BETWEEN 3 AND 30` 改为 `BETWEEN 1 AND 30`，同步 `BatchBaselineConfiguration` 与 `ModelSnapshot`。业务计算仍严格为 `ceil(actualShelfLifeDays * 1%)` 后 `Clamp(1,7)`。禁止其他 schema、索引、字段或业务表变化。

部分提交必须在现有正式提交事务中完成真正的生命周期拆分：只为本次有效且非空的 task items 形成 Inspection/InspectionItems、执行 0 数量生命周期并推进这些 batch 的 handled waterline；原 task 完成后，把仍然当前有效但未提交的 task items 原子迁移/重建到同商品唯一 successor open task，重新计算 HighestStage，且不携带旧 Draft。原单批次/全填流程继续走同一权威提交路径。禁止只删除 `AllItemsFilled` 检查，禁止复制 Stage 3/4 生命周期算法。

## 允许修改范围

- `ColdStartScopeBaselineUseCase.cs`。
- 今日计划导出、读取、行级当前事实解析、Draft 应用、批量/单任务提交与任务聚合相关现有 Application 文件。
- `TodayInspectionViewModel.cs`、`MainWindow.xaml/.cs` 及现有确认窗口，仅用于 loading、门店可读错误与汇总反馈。
- 直接相关最小测试与本 Stage19 治理/验收文件。

## S19-T01 历史禁止范围

- Schema、migration、ModelSnapshot、依赖、版本号、Updater、Installer、发布渠道。这里的“禁止 Updater”只属于 S19-T01 历史范围；S19-T02 可修改 App 侧在线更新编排，但仍禁止修改独立 `StoreExpiryInspector.Updater`。
- Stage18、正常生命周期阈值、3 天预提醒、库存规则、历史 Revision、备份恢复、其他页面视觉重构。
- 正式数据库访问；FULL、大库、无关回归；tag/Release、夸克或 Gitee 更新。

## 风险与门禁

- 最大风险是部分提交后误把同商品空白批次一并 completed/Handled，或让 completed task 的 item 覆盖与 Inspection 不一致；必须用事务级测试证明提交子集与 successor open task 精确互补。
- 第二风险是旧表状态竞态；Preview 不能作为提交权威，Apply/Submit 必须重新读取数据库并逐行降级。
- 第三风险是错误分类把损坏文件当行级继续；只有 A:L 可安全解析后才允许行级容错。
- 技术通过后只能为 `IMPLEMENTED / TECHNICAL_ACCEPTANCE_READY / NOT_ACCEPTED`。用户 GUI PASS 前 S19-T01/Stage19 不得 CLOSED。
- `FULL=NOT_RUN / NO_FULL`；仅 S19-T01 专项、必要直接回归与一次 Release App build。
