# Stage 2 整体验收记录｜Excel 增量导入引擎

- 验收日期：2026-08-27
- 结论：通过，暂停在 Stage 2 → Stage 3 门禁
- 验收范围：`S2-T01` 至 `S2-T08`
- 最新实现提交：`4287384703c656fc91e5f509b52015250879f918`
- S2-T08 验收归档提交：`3bafa1b64669293f9c23b1aeb8e4d6c7604110fb`
- 独立总验收：GPT-5.6 Sol

## 八卡交付结论

| 任务 | 已交付能力 | 结论 |
|---|---|---|
| S2-T01 | 固定模板只读解析、表头规范化、文本标识保真、真实样表 SHA | 通过 |
| S2-T02 | 食品/跳过分类、字段异常、重复、批次冲突、库存冲突 | 通过 |
| S2-T03 | 只读 SQLite 商品/批次差异计划与预览 | 通过 |
| S2-T04 | 确认前文件身份复核、无变化不建正式 Import | 通过 |
| S2-T05 | 导入前 SQLite 在线快照、完整性/schema/migration 验证 | 通过 |
| S2-T06 | Import/Product/Batch/Issue/Workbook/Backup 单事务增量写入 | 通过 |
| S2-T07 | 成功导入原始 Workbook 最近两份同事务保留 | 通过 |
| S2-T08 | 最新成功 Import 撤销资格、原快照关联和后续业务阻断 | 通过 |

每卡均有 `.ai-dev/TASKS/S2-Txx.md` 与 `.ai-dev/ACCEPTANCE/S2-Txx.md`，具体开发由 GPT-5.6 Luna（max）执行，GPT-5.6 Sol 独立审查并补缺。

## 最高业务红线验收

Excel 始终作为局部增量而非全量快照处理：

- S2-T03 真实 SQLite 预存 A/B/C、本次只规划 A，B/C 及其全部批次不进入任何新增、更新、删除、库存或状态计划，规划前后逐字段不变。
- S2-T06 正式事务只导入 A 后，B/C 及其批次、`last_seen_import_id`、跟踪/生命周期字段逐字段不变，不生成 Task、Draft、Inspection、InventoryAdjustment 或 LifecycleEvent。
- 文件缺少历史商品/批次从不被解释为库存 0、删除、停止跟踪或关闭任务；库存冲突不任选值，包含 0 也不执行商品归零。
- S2-T06 只保存本次合法事实和历史最高累计到货基础，不执行库存 0 生命周期、累计到货恢复、阶段计算或任务生成。

结论：局部增量红线由预览层和正式写入层两套独立真实 SQLite 证据同时证明，Stage 2 通过的核心条件满足。

## 解析、分类与真实样表

- Open XML SDK 3.5.1 是唯一 Excel 依赖；工作簿只读打开，不修改固定样表。
- 商品编码与条码按文本保真，覆盖长数字、科学计数法风险和前导零；表头先 Trim，规范化后重名整文件拒绝。
- 真实样表为 397,308 字节，SHA-256 `20fe1898dba98f48bcd8b83673f2001fe3ed0dfc01a89aa4ebdff9d1af6cacce`。
- 固定画像：3,712 食品数据行、3,709 批次键、3,706 正常批次、3 组/6 行批次冲突；冲突行号为 812/813、2284/2285、2610/2611。
- 非食品行跳过而非异常；最后三个人工排查字段不进入正式排查 DTO 或业务记录。

## 事务、快照与工作簿

- 确认前重新读取源文件并复核 SHA；文件变化、缺失、锁定、无变化或计划陈旧均在正式业务写入前阻断。
- 快照先通过 SQLite 在线备份写临时文件，验证 quick/foreign-key、表、列、8 条 migration、大小与 SHA 后原子发布；失败不得进入正式事务。
- 正式导入只消费既有确认契约和差异计划，在一个明确 SQLite 事务中写 Import、BackupRecord、Product/Batch、Issue、Workbook；各写入节点与 Commit 前故障均证明全量回滚，快照继续存在。
- 最近两份 Workbook 裁剪与新导入处于同一事务；第 1～5 次成功导入后 Workbook 数为 1、2、2、2、2，所有 Import 历史永久保留。

## 撤销资格基础

- 唯一候选为最新 `Succeeded && !IsUndone` 且有确认时间的 Import，按 `ConfirmedAtUtc DESC, Id DESC`；生产 API 不接受 Import Id。
- 原快照必须关联唯一 `pre_import/verified` BackupRecord，并通过路径、时间、SHA、完整 SQLite schema 和 migration 验证；不得临时重建替代快照。
- Import 后九张正式业务/草稿表任一新增、修改或删除均阻断；Import 前既有且未变事实不阻断。Product/Batch 使用更新时间补充门禁。
- Import/Issue/Workbook/Backup 基础设施变化和 S2-T07 Workbook 裁剪不被误判为业务操作。
- 本阶段只做资格判断，不执行恢复、不写 `Undone`、不定义 Undone 工作簿语义。

## 构建、测试、数据库与依赖

- Stage 2 八组件组合专项：120/120。
- Release 全量：178/178；S2-T08 测试池竞态修复后 Luna 连续 3 轮、Sol 连续 2 轮均通过。
- 数据库/schema 专项：49/49；七段历史升级保留：7/7。
- Release build：0 警告、0 错误。
- EF：8 条 migration；`has-pending-model-changes` 报告无模型漂移；Domain、EF 配置、migration 与 ModelSnapshot 在 Stage 2 未改变。
- 官方 NuGet 源联网漏洞审计：应用与测试项目均无已知漏洞。
- `git diff --check` 通过；固定样表未变化；无 `.db/.db-wal/.db-shm/.tmp` 工作区残留。

## 架构债检查

- 解析、校验、差异规划、确认、快照、事务持久化、保留策略和撤销资格边界成立；DTO 与 EF 实体隔离，预览不修改 tracked entity。
- 未发现 Repository、UnitOfWork、单实现接口、反射扫描、事件总线、通用导入/文件版本/恢复框架、插件系统或 Stage 3 提前实现。
- 无阻断级架构债。当前最大生产文件 `ConfirmedImportExecutor.cs` 为 1,150 行，但只承担持久化阶段的契约/计划白名单门禁、明确字段应用和单事务编排；Stage 3 禁止继续向其加入状态机。若该类下一次出现实质职责增长，应先按现有私有职责拆分具体协作者，不建立通用 ImportService。
- Planner、SnapshotService、UndoEligibilityService 也偏长，且有少量路径/SHA/SQLite 辅助重复；当前保持显式代码比抽象通用框架安全，只有出现第三个真实业务调用方或实际重复缺陷时再提取。
- 测试 helper 的全局连接池清理竞态已修复为按当前临时数据库清池，不属于遗留债务。

## 未实现与阶段门禁

- 未实现食品效期计算、当前最高阶段、下一触发日期、任务聚合/升级、库存 0 生命周期、排查 0 件、累计到货恢复或任何 Stage 3 业务。
- 未实现真正撤销恢复、恢复前备份、Undone 写入、完整备份恢复、UI、提醒、托盘、自启动、Excel 导出回填、安装或性能验收。
- Stage 2 整体验收通过，但不得创建 Stage 3 任务。已生成事实性交接文件，等待用户确认是否更换下一任 Sol 产品经理进入 Stage 3。
