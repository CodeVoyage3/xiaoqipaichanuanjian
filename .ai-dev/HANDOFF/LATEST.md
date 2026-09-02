# 最新交接

## 当前任务与状态

`V1-F03｜今日排查计划导出 + Excel 排查结果回导 + 确认提交` 已完成本地事实审计、Schema 判断和最小实施拆分。

当前状态：`V1-F03_SPLIT_PROPOSED / WAITING_I01_APPROVAL`

本轮没有创建 Terra，没有修改生产代码，没有进入 Stage 8、Stage 9 或其他功能。

## 当前 Git

- 分支：`master`，跟踪 `origin/main`。
- 接任分析基线：`8fe9615f6781c9c76d27d95b0829a2d2edc31c21`；分析开始时 ahead/behind=`0/0`、工作区干净。
- 本交接所在治理提交将在普通 push 后成为 GitHub `main` 最新基线。

## V1-F03 拆分结论

实施顺序固定为四张最小卡：

1. `V1-F03-I01｜今日排查计划查询与 Excel 导出`
2. `V1-F03-I02｜排查结果读取、陈旧校验与 Draft 应用`
3. `V1-F03-I03｜多任务正式提交编排`
4. `V1-F03-I04｜WPF 双入口与端到端收口`

详细契约：`.ai-dev/ANALYSIS/V1-F03-IMPLEMENTATION-SPLIT.md`；功能总卡：`.ai-dev/TASKS/V1-F03.md`；决策：`.ai-dev/DECISIONS.md` D-031。

## 核心技术决策

- 当前 Schema 足够，V1-F03 默认不新增 migration；migration 基线保持 9。
- 导出稳定身份复用现有 `TaskId / TaskItemId / ProductId / BatchId / AttentionVersion` 和任务/库存/到货快照；不以商品名、条码或行号定位。
- 回导解析成功只形成当前会话内的待确认结果，数据库零写入；确认后只原子 patch 既有 Draft。
- 空白保持未完成，`0` 保持正式零值，正整数保持现场数量；删除行不应用，重复/非法/身份修改/陈旧项不自动应用。
- 最终多任务提交只允许用薄外层事务调用现有 `InspectionSubmissionUseCase`；任何冲突整批回滚，不复制 Inspection、0 件停止、Task 完成或生命周期逻辑。
- 商品源 Excel 导入与排查结果回导保持两个清晰入口；Excluded、Unresolved、非法 Managed lifecycle 零进入。

## 本轮新鲜技术核验

- Release restore/build 通过，build 0 warning / 0 error；restore 使用 `NuGetAudit=false`，不冒充在线漏洞审计。
- Release 全量 764/764，通过 764、失败 0、跳过 0。
- EF 无模型漂移；`--no-connect` migration 列表为 9 条，最后一条仍为 `20260901155124_AddPolicyAndBaselineFoundation`。
- `git diff --check` 通过；`StoreExpiryInspector` 进程 0。
- 未启动 WPF、未访问当前运行数据库；本轮测试只使用隔离临时数据库。

## Schema 停机门禁

若实现证明必须跨应用重启恢复未确认预览、持久化导出清单或回导批次，立即停止并提交 Schema 决策报告。未经产品经理新批准，不得新增 migration、修改 ModelSnapshot 或滥用 Import 表。

## 下一唯一审批点

等待产品经理明确批准：`V1-F03-I01｜今日排查计划查询与 Excel 导出`。

批准前不得：

- 创建 Terra 或任何实现代理；
- 创建正式 I01 实施卡或修改生产代码；
- 开始 I02～I04；
- 进入 Stage 8、Stage 9 或其他功能。

I01 获批后，Sol 必须在本话题创建一个全新的 GPT-5.6 Terra（medium，平台标准速度）只负责 I01；Terra 完成并停止后由 Sol 独立审查、测试和验收。
