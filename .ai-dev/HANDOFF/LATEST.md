# 最新交接

## 当前任务与状态

- Stage 8 已获用户明确批准并正式启动：`IN_PROGRESS / S8-T01_CURRENT`。
- 当前唯一任务：`S8-T01｜高规模数据基线与性能压测基座`，已建立 Task 与 Acceptance，等待全新 GPT-5.6 Terra 实施。
- V1-F01、V1-F02、V1-F03、V1-F03-I04、V1-UI-01 继续保持 `CLOSED`；V1-UI-01 为 `GUI_ACCEPTANCE_PASSED`。
- Stage 9 与在线升级未启动。

## Git 与开工基线

- 2026-09-03 已重新 fetch GitHub `main`；建档前 `master == origin/main == 1e41876bfa9c203a88cf53955867f0c3dd639e84`，ahead/behind `0/0`，工作区干净。
- 最近已归档技术基线为 Release 894/894、Release build 0 warning / 0 error、EF 无模型漂移、migration=9，最后一条 `20260901155124_AddPolicyAndBaselineFoundation`；尚未作为 S8-T01 新鲜复跑证据。

## S8-T01 边界

- 目标为完全隔离、可重复生成并核对 100,000 Batch / 300,000 Inspection 历史的 SQLite 压测环境。
- 必测 Dashboard、待排查首载/分页/搜索/Stage/大类/组合、任务详情、今日排查、历史列表/详情/Revision、ProductTask、Reminder 与大库 snapshot。
- 记录 median 与 p95/max、实际 SQL、command count、query plan、索引及 full scan/N+1/materialization/内存筛选/timeout/lock/crash/OOM。
- 本卡先测量，不优化；不新增索引、migration、Schema、依赖，不修改生产业务或全局 UI。
- 不访问或修改正式数据库；所有生成、查询和 snapshot 只能位于带 S8-T01 marker 的唯一临时目录。

## 停止门禁

- S8-T01 完成并经 Sol 独立验收后停止，不自动创建 S8-T02。
- S8-T02～T06 仅在 Stage 文档中作为候选方向；当前没有正式 Task 或实施授权。
- 不启动 Stage 9，不实施在线升级。
