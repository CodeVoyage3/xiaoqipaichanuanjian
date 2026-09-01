# V1-F01 冷启动模型设计（待产品确认）

## 范围级基线，而非全局一次导入

建议候选模型是 `ExpiryManagementScopeBaseline`：一条记录对应稳定的 `scope_key + policy_code + policy_version`，而不是应用级 `HasCompletedInitialImport`。`scope_key` 应由已批准的管理范围映射产生（至少源商品大类；将来若中/小类需要不同规则，再拆分），不得由显示文案或任意商品名生成。

每条基线至少需：范围键、policy code/version、建立导入 ID、建立业务日期/UTC、源文件 SHA、规则快照版本、完成状态和幂等键。初次建立用范围键+策略版本唯一约束；新增品类只创建其自身基线，既有范围不重置。策略版本升级须显式产品批准，不能借导入隐式重跑。

还需要仅记录“见过”的范围内 BatchKey 锚点（可为基线明细或可审计的已见记录），身份仍是既有 product code + production date + expiry date。它不是 Inspection、Task、Draft、Revision 或 LifecycleEvent，且不能覆盖现有 `LastSeenImportId` / `MaxArrivalQty` / attention 水位。

## 首次分类顺序

1. 先按现有导入质量和同 BatchKey 冲突组规则验证；无可靠字段或冲突：不分类、不造数据，记录导入问题。
2. 商品库存=0：最高优先，只登记基线/见过事实；不创建可执行任务。
3. 仍未到期：按已批准 policy 计算 5折/2折/收仓；只有可执行阶段才交现有聚合器建立 open task。
4. 已过期且在产品批准的动态窗口内：建立“历史补查候选”，其是否转 open task 需产品确认；不能伪造已完成记录。
5. 已过期且超过窗口：只建立历史基线/已见事实；不创建正式排查。
6. 首基线完成后第一次见到的 BatchKey：普通新 Batch，走现有 lifecycle；已有 BatchKey 的累计到货只有首次超历史最大值才走新增到货。

## 与现有生命周期的契约

- 基线不会提升 `AttentionVersion`，不会把历史基线解释为 arrival 或恢复。
- 基线后新 BatchKey 正常进入 `PostImportLifecycleUseCase`；新增到货继续使用 `CurrentArrivalQty > MaxArrivalQty`。
- 0 库存继续先走 `ProductStockZeroLifecycleUseCase`。
- 正式提交仍仅更新 `HandledAttentionVersion`；Stage 后续上升或新的到货 attention 仍可创建/更新可处理 Task，不能因历史基线或一次提交永久屏蔽。
- 旧的 completed Task 不能被基线回填或伪造；历史补查若获批准应创建全新、可审计的候选来源，不回写历史 Inspection。

## 需要产品确认的决策

1. 管理范围是否严格等于当前“商品大类”，以及每个范围的 policy 映射与版本。
2. 历史补查是直接 open task，还是先进入人工复核队列；本卡不实现二者。
3. 采用 3%、5%、10% 或截断窗口；推荐候选为 `ceil(实际保质期天数 × 5%) clamp(3,60)`，但 3 天下限和 60 天上限均须经产品确认。3 天是未来短保品类的最低补查保护，60 天防止超长保批次把历史补查无限拉长；本表均未实际触发这两个边界，30 天上限则会少补查 74 批。
4. 到期当天和原表“是否该做临期折扣”为空时的产品语义；当前代码把到期当天定为 expired，而该标识在真实数据大量为空。
