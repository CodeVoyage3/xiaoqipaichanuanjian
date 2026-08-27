# Stage 3 整体验收记录｜效期计算与任务引擎

- 验收日期：2026-08-27
- 结论：通过，暂停在 Stage 3 → Stage 4 门禁
- 验收范围：`S3-T01` 至 `S3-T07`
- 最新实现提交：`dd1a83b87082d80990a4ff2655788ecde91a3eca`
- 独立总验收：GPT-5.6 Sol

## 七卡交付结论

| 任务 | 已交付能力 | 结论 |
|---|---|---|
| S3-T01 | 纯效期阶段、canonical phase 与下一触发日 | 通过 |
| S3-T02 | 显式批次结果聚合为商品唯一开放任务 | 通过 |
| S3-T03 | 活动且到期批次的启动补算 | 通过 |
| S3-T04 | 商品库存明确归零的商品级生命周期结束 | 通过 |
| S3-T05 | 导入后新批次、新到货与唯一恢复例外 | 通过 |
| S3-T06 | 正式排查 0 件后的批次停止 | 通过 |
| S3-T07 | 原子导入后置编排与真实 WPF 启动入口 | 通过 |

每卡均有任务卡与单卡验收记录；具体开发由 GPT-5.6 Luna（max）执行，GPT-5.6 Sol 独立审查和验收。

## 业务不变量验收

- canonical phase 只有 `none`、`discount_50`、`discount_20`、`withdraw`、`expired`；优先级权威实现为 `expired > withdraw > discount_20 > discount_50`。
- 效期算法只消费显式业务日期、有效日期、保质期值和 D/M/Y 单位；不读取系统时间、不要求或反推生产日期。270/271 天分界、两套全部边界和下一触发日均已覆盖。
- 同商品最多一条开放任务；批次保留自身阶段，商品 HighestPhase 由全部待排查 Item 重算；重复调用、阶段升级、AttentionVersion 与 Draft 重新确认均保持幂等。
- 启动补算只查询 `active + next_trigger_date <= today`，直接计算当前阶段，不逐日回放、不补历史任务。
- 商品明确库存 0 优先于新批次、新到货、恢复和阶段任务；所有旧 Batch 停止，任务自动结束、Draft 失效并留痕，库存恢复不复活旧 Batch。
- 真正新增到货只认“本次累计到货 > 导入前历史最高”；只有 `batch_checked_zero`、商品从未归零、库存大于 0 且首次突破历史最高时旧 Batch 才可恢复。
- 正式排查 0 件只停止目标 Batch 并写批准事件，不终止 Product、不修改其他 Batch/Task/Draft；旧事实重放不能覆盖后续合法恢复。
- Excel 始终是局部增量。本次未出现 Product/Batch 不进入后置处理；缺失、空白、非法或冲突库存不被解释为 0。

## 事务与真实入口

- 导入确认入口在同一外层 SQLite 事务中执行 Stage 2 持久化与 S3-T04/S3-T05；任一后置失败会撤销 Succeeded Import 及全部相关状态。
- `ConfirmedImportExecutor` 只支持外层事务所有权，没有承载 Stage 3 状态机。
- WPF 启动入口初始化/migrate 数据库，在入口取得本地业务日期并调用 S3-T03；`LastNormalRunDate` 与补算同事务，时钟回拨不降级状态。
- Release EXE 已由 Sol 实际启动：主窗口正常显示；新库应用 8 条 migration、写入正常运行日期和 `startup_recalculation_completed` 日志。

## 构建、测试与数据库

- S3-T07 专项：23/23；S3-T01～S3-T06 精确回归：147/147；Stage 3 精确证据合计：170/170。
- Release 全量：348/348；Release build：0 警告、0 错误。
- EF 无模型漂移；migration 仍为 8 条；真实启动库确认 20 张表（含 EF migration history）和 8 条已应用 migration。
- 无新实体、字段、表、migration 或依赖；`git diff --check` 通过，验收前工作区干净。

## 架构债结论

- 效期计算、任务聚合、启动补算、商品归零、导入后生命周期、批次 0 停止与运行编排边界独立，未发现第二套规则实现。
- `ConfirmedImportExecutor.cs` 为 1,154 行，仅比 Stage 2 基线增加事务所有权适配；禁止后续继续加入生命周期或 UI 规则。
- `ConfirmedImportLifecycleOrchestrator.cs` 为 467 行，偏长但只做显式事实冻结/映射、既有 UseCase 调用与事务；当前不存在必须拆分的 God Service 债务。
- 未出现 Repository/UnitOfWork、单实现接口、EventBus/Outbox、通用状态机/工作流或未来式抽象。
- 无阻断级架构债。Stage 4 应新增排查查询/草稿/提交用例，不得把提交事务塞回上述编排器。

## 阶段门禁

Stage 3 整体验收通过。未创建 Stage 4 任务卡；已生成 Stage 3 closeout 与 Stage 4 handoff，等待用户决定是否更换下一任 GPT-5.6 Sol。Stage 4 开工前必须重新核验仓库事实并先提交最小拆卡方案。
