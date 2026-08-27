# Stage 3 交接归档｜效期计算与任务引擎

> 归档日期：2026-08-27。Stage 3 已整体验收通过。本文件冻结阶段交付事实；后续不得改写为 Stage 4 实现记录。

## 一、阶段基线

- 分支：`master`。
- S3-T07 最新实现提交：`dd1a83b87082d80990a4ff2655788ecde91a3eca`。
- S3-T01～S3-T07：7/7 通过，任务卡与单卡验收记录齐全。
- Stage 3 总验收：`.ai-dev/ACCEPTANCE/STAGE-3.md`。
- schema 保持 17 张业务表、17 个实体/配置/DbSet、8 条 migration；Stage 3 无模型漂移。
- 最终自动验证：Stage 3 精确 170/170、Release 全量 348/348、Release build 0/0。
- 真实入口：Release WPF 主窗口启动正常，新空库完成 8 条 migration，启动补算写入正常运行日期及本地日志。

接任者仍必须以 `git rev-parse HEAD`、工作区和重新执行的门禁为准；上述 SHA 是审计锚点，不替代当前事实。

## 二、已交付边界

```text
Domain
  ExpiryStageCalculator              纯阶段、边界与下一触发日
Application
  Tasks/ProductTaskAggregator        商品唯一开放任务聚合
  StartupRecalculationUseCase        到期活动 Batch 启动补算
  ProductStockZeroLifecycleUseCase   商品库存归零生命周期
  PostImportLifecycleUseCase         新批次、新到货与恢复例外
  BatchCheckedZeroLifecycleUseCase   正式 0 件停止目标 Batch
  ConfirmedImportLifecycleOrchestrator  原子导入后置编排
  ApplicationStartupCoordinator      启动补算与运行日期门禁
WPF App.xaml.cs
  数据库初始化、入口时钟、Coordinator 调用与日志
```

`ConfirmedImportExecutor` 仍只负责 Stage 2 导入持久化；Stage 3 只为它补充外层事务参与能力。

## 三、冻结口径

1. canonical phase 为 `none / discount_50 / discount_20 / withdraw / expired`，不得新增平行名称。
2. 所有效期计算显式传入业务日期；离线只算当前阶段，不回放历史阶段。
3. 批次触发、商品聚合；每商品最多一条开放任务。
4. AttentionVersion 只表达新的关注事实；自然效期推进不无条件增加版本。
5. 商品明确库存 0 是最高优先级，旧 Batch 永久不因库存恢复而复活。
6. 真正新增到货只认首次突破导入前历史最高；只有 batch_checked_zero 旧 Batch 具备受限恢复资格。
7. Excel 是局部增量；未出现、空白、非法或冲突事实没有归零/恢复含义。
8. 五类 LifecycleEvent 是审计事实，不是事件总线；不得扩展未批准事件类型。

## 四、已知限制与后续边界

- Stage 3 没有正式创建 Inspection、完成 ProductTask 或实现排查提交；S3-T06 只提供可供 Stage 4 组合的 Batch 生命周期能力。
- 当前 WPF 主窗仍是占位内容；首页、任务列表、详情、草稿交互、库存修正和提交事务属于 Stage 4。
- S3-T06 对同一 InspectionItem 多轮 `0→正数→0` 修订的时序治理属于 Stage 5，不得在 Stage 4 偷做历史编辑。
- 启动失败当前记录日志并继续显示主窗；用户可见错误页、后台提醒和托盘不属于 Stage 3。
- 迁移前可恢复快照、安装、完整恢复、性能与 Windows 10 门店实机仍是后续阶段门禁。

## 五、归档门禁

- 不新增 S3-T08，不把 Stage 4 UI/提交缺口塞回 S3-T07。
- 不修改 Stage 3 canonical phase、生命周期优先级或局部增量红线。
- 不向 `ConfirmedImportExecutor`、WPF ViewModel 或 EF Configuration 加业务状态机。
- 下一步只允许新任/获确认的 Sol 先完成 Stage 4 接任核验和最小拆分提案；未经用户批准不得创建 Stage 4 任务卡或派发 Luna。
