# V1-F01 冷启动模型设计（产品经理最终批准）

## 范围级基线，而非全局一次导入

已批准的候选实现模型是 `ExpiryManagementScopeBaseline`：一条记录对应稳定的 `scope_key + policy_code + policy_version`，而不是应用级 `HasCompletedInitialImport`。`scope_key` 以源商品大类作为已批准管理范围；将来若中/小类需要不同规则，必须另行批准拆分，不得由显示文案或任意商品名生成。

每条基线至少需：范围键、policy code/version、建立导入 ID、建立业务日期/UTC、源文件 SHA、规则快照版本、完成状态和幂等键。初次建立用范围键+策略版本唯一约束；新增品类只创建其自身基线，既有范围不重置。策略版本升级须显式产品批准，不能借导入隐式重跑。

还需要仅记录“见过”的范围内 BatchKey 锚点（可为基线明细或可审计的已见记录），身份仍是既有 product code + production date + expiry date。它不是 Inspection、Task、Draft、Revision 或 LifecycleEvent，且不能覆盖现有 `LastSeenImportId` / `MaxArrivalQty` / attention 水位。

## 首次分类顺序

1. 先按现有导入质量和同 BatchKey 冲突组规则验证；无可靠字段或冲突：不分类、不造数据，记录导入问题。
2. 商品库存=0：最高优先，只登记基线/见过事实；不创建可执行任务。
3. 仍未到期：按已批准 policy 计算；首次既有 5折/2折只建立基线并等待更高阶段，当前收仓生成 open task。
4. 到期当天直接进入 `expired` open task；更早过期且在 `clamp(ceil(实际保质期天数 * 3%),3,30)` 动态窗口内进入 open task，来源可审计为“首次历史补查”。
5. 已过期且超过窗口：只建立历史基线/已见事实；不创建正式排查。
6. 首基线完成后第一次见到的 BatchKey：普通新 Batch，走现有 lifecycle；已有 BatchKey 的累计到货只有首次超历史最大值才走新增到货。

## 与现有生命周期的契约

- 基线不会提升 `AttentionVersion`，不会把历史基线解释为 arrival 或恢复。
- 基线后新 BatchKey 正常进入 `PostImportLifecycleUseCase`；新增到货继续使用 `CurrentArrivalQty > MaxArrivalQty`。
- 0 库存继续先走 `ProductStockZeroLifecycleUseCase`。
- 正式提交仍仅更新 `HandledAttentionVersion`；Stage 后续上升或新的到货 attention 仍可创建/更新可处理 Task，不能因历史基线或一次提交永久屏蔽。
- 旧的 completed Task 不能被基线回填或伪造；首次历史补查必须创建全新、可审计的任务来源，不回写历史 Inspection。

## 最终批准规则

- 范围键为 `scope_key + policy_code + policy_version`；同范围同版本只建立一次基线。
- `应季搭配`、`赠品小样`正常保存但 V1 不纳管；六类通用长效组 `<=180` 天正常保存为规则未覆盖，不生成效期任务。
- 公司 policy 是 Stage 唯一权威，月份固定 30 天；Excel `折扣日期` 不参与 Stage 证明或计算。
- 首次采用方案 C；基线后四阶段恢复正常生命周期。
- 设计阻断已解除。生产实现仍须按独立任务逐卡批准。
