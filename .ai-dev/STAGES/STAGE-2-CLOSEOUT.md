# Stage 2 交接归档｜Excel 增量导入引擎

> 归档日期：2026-08-27。Stage 2 已整体验收通过。本文件冻结阶段交付事实；后续不得改写为 Stage 3 的实现记录。

## 一、阶段基线

- 分支：`master`。
- Stage 2 最新实现提交：`4287384703c656fc91e5f509b52015250879f918`。
- S2-T08 独立验收归档提交：`3bafa1b64669293f9c23b1aeb8e4d6c7604110fb`。
- S2-T01～S2-T08：8/8 通过，任务卡与单卡验收记录齐全。
- Stage 2 总验收：`.ai-dev/ACCEPTANCE/STAGE-2.md`。
- 当前 schema：17 张业务表、17 个实体/配置/DbSet、8 条 migration，Stage 2 无 schema 漂移。
- 最终验证：Stage 2 专项 120/120、全量 178/178、数据库专项 49/49、历史升级 7/7、Release build 0/0、官方源无已知漏洞。

接任者必须以 `git rev-parse HEAD` 和工作区事实重新核对；上述提交是审计锚点，不代替当前仓库事实。

## 二、已交付代码边界

```text
Infrastructure/Excel
  ExcelTemplateReader          固定模板只读解析、规范化表头、文件 SHA
  ExcelFileClassifier          纯内存食品/跳过/异常/重复/冲突分类
  ExcelDateParser              日期解析
  普通 Excel DTO/结果

Application/Imports
  ExcelImportPlanner           只读查询本次商品/批次，生成内存 diff/preview
  ImportConfirmationGuard      确认前文件身份复核与冻结契约
  ConfirmedImportExecutor      快照后单事务应用正式增量事实
  ImportUndoEligibilityService 最新 Import 的只读撤销资格与快照关联

Infrastructure/Backups
  PreImportSnapshotService     SQLite 在线快照、原子发布、完整结构验证
```

解析、分类、规划、确认、快照和持久化未被重新包装成万能 ImportService。工作簿最近两份裁剪属于确认事务内的固定持久化不变量。

## 三、冻结业务口径

1. Excel 是局部增量，不是全量快照；未出现记录没有任何业务含义。
2. 商品主体只认商品编码；名称/条码变化不改变主体，不按名称/条码合并不同编码。
3. 批次键为“商品编码 + 可空生产日期 + 有效日期”；历史出现过即永远是旧批次。
4. 生产日期可空，有效日期不可空；M/D/Y 分别为月/天/年，不反推生产日期。
5. `商品大类 == 食品` 才进入 V1 食品处理；非食品跳过。
6. 同批次完全相同为重复，关键字段不同整组冲突；同商品库存冲突不任选值、不执行归零。
7. 最后三个人工排查字段在导入中忽略，不生成正式排查。
8. 正式 Import 状态只有 Succeeded/Undone；解析、预览、失败、取消、无变化不建 Import。
9. `new_task_product_count` 在 Stage 2 固定为 schema 占位 0，不具备任务数量含义。
10. 原始 Workbook 只保留最近两次 Succeeded Import 的内容，Import 事实永久保留。

## 四、已知限制与债务

- S2-T08 只判断资格，不执行恢复；资格结果不是长期授权，真正恢复前必须在排他/原子边界内重查并备份当前库。
- Product/Batch 同确认时间且绕过正式留痕的直接覆盖无法由当前 schema 准确识别；后续写路径必须持久化任务、排查、修正或生命周期事实。
- Undone 的工作簿占位/保留语义未定义；不得从 S2-T07 的 Succeeded 保留规则自行推断。
- `ConfirmedImportExecutor` 较长但职责仍限于持久化；Stage 3 不得向其中加入效期或生命周期状态机。
- DatabaseInitializer 尚未接入真实 App 启动；迁移前快照、迁移失败恢复、UI 进度与后台执行未实现。
- Excel round-trip、完整备份恢复、Windows 实机、安装、托盘、提醒和性能仍属于后续阶段。

## 五、归档门禁

- 不再新增 S2-T09，也不把后续缺口塞回已验收 S2-T01～S2-T08。
- 不修改固定样表；异常文件必须另建副本。
- 不为 Stage 3 预创建代码、任务卡、migration 或 DTO。
- 下一步只允许由新任/获确认的 Sol 先完成 Stage 3 接任核验和拆分提案；未经用户确认不得派发 Luna。
