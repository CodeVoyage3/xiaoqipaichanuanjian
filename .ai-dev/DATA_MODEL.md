# 当前数据模型

> 更新于 2026-08-27。以下为 Stage 1 数据底座及 Stage 2 整体验收后的代码与 SQLite schema 事实；Stage 3 业务状态转换尚未实现。权威结构以 8 条 migration 和 `StoreDbContextModelSnapshot` 为准。

## 总览

- 17 张业务表、17 个领域实体、17 个独立 EF 配置、17 个 DbSet。
- 所有主键均为整数 `id`；业务日期保存为 SQLite `TEXT`，时间戳为 UTC `TEXT`，数量为非负整数。
- 关系默认采用 `NO ACTION`，不依赖级联删除。
- 当前 migration：`InitialCreate`、`AddTasksAndDrafts`、`AddInspectionHistory`、`AddInventoryAdjustments`、`AddImportPersistence`、`AddBackupMetadata`、`AddSettingsAndAppState`、`AddLifecycleEvents`。

## 商品与批次

### `products`

字段：`id`、`product_code`、`current_name`、`current_barcode`、`category_code`、`policy_code`、`excel_stock_qty`、`effective_stock_qty`、`effective_stock_source`、`lifecycle_generation`、`is_stock_zero_terminated`、`last_seen_import_id`、`created_at_utc`、`updated_at_utc`。

- `product_code` Trim 后非空且唯一，是商品主体唯一身份。
- `current_name`、`current_barcode` 可空；名称或条码变化不产生新商品。
- 默认 `category_code = food`、`policy_code = food_v1`；当前只批准食品 V1。
- `last_seen_import_id` 可空并引用 `imports`，删除行为 `NO ACTION`。

### `batches`

字段：`id`、`product_id`、`production_date`、`expiry_date`、`shelf_life_value`、`shelf_life_unit`、`current_arrival_qty`、`max_arrival_qty`、`source_discount_reference`、`lifecycle_generation`、`tracking_status`、`stop_reason`、`stopped_at_utc`、`current_stage`、`next_trigger_date`、`attention_version`、`handled_attention_version`、`last_seen_import_id`、`created_at_utc`、`updated_at_utc`。

- 有生产日期的唯一键：`UNIQUE(product_id, production_date, expiry_date) WHERE production_date IS NOT NULL`。
- 无生产日期的唯一键：`UNIQUE(product_id, expiry_date) WHERE production_date IS NULL`。
- 这两条键对应产品规则中的“商品编码 + 生产日期 + 有效日期”与“商品编码 + 有效日期”；旧批次记录永久保留。
- `shelf_life_unit` 只允许 `M`、`D`、`Y`；当前/历史最高累计到货均非负。
- 已有 `(tracking_status, next_trigger_date)`、`product_id`、`expiry_date` 索引，支持后续避免启动全历史扫描。

## 当前任务与草稿

### `tasks`

字段：`id`、`product_id`、`status`、`highest_stage`、`created_at_utc`、`updated_at_utc`、`closed_at_utc`、`close_reason`。

- `status`：`open`、`completed`、`system_closed`。
- `highest_stage`：`discount_50`、`discount_20`、`withdraw`、`expired`。
- 部分唯一索引保证每个商品最多一条 `open` 任务。

### `task_items`

字段：`id`、`task_id`、`batch_id`、`product_id`、`stage`、`attention_version`、`requires_reconfirmation`、`created_at_utc`、`updated_at_utc`。

- `(task_id, batch_id)` 唯一。
- 组合外键保证任务、批次项和商品一致。

### `drafts`

字段：`id`、`task_id`、`inspector_name`、`check_date`、`is_invalid`、`invalid_reason`、`invalidated_at_utc`、`created_at_utc`、`updated_at_utc`。

- 每个任务最多一条草稿；失效时必须同时具有非空原因和失效时间。

### `draft_items`

字段：`id`、`draft_id`、`task_item_id`、`task_id`、`checked_qty`、`confirmed_attention_version`。

- `checked_qty` 可空；非空时不得为负。
- `(draft_id, task_item_id)` 唯一，组合外键保证草稿与任务项属于同一任务。

## 正式排查与修改历史

### `inspections`

字段：`id`、`task_id`、`product_id`、`product_code_snapshot`、`product_name_snapshot`、`barcode_snapshot`、`stage_snapshot`、`stock_qty_snapshot`、`inspector_name`、`check_date`、`submitted_at_utc`。

- 每个任务最多一条正式排查；商品编码、阶段、库存、人员等保存提交时快照。

### `inspection_items`

字段：`id`、`inspection_id`、`product_id`、`batch_id`、`production_date_snapshot`、`expiry_date_snapshot`、`stage_snapshot`、`arrival_qty_snapshot`、`checked_qty`、`updated_at_utc`。

- `(inspection_id, batch_id)` 唯一；组合外键保证排查、批次和商品一致。

### `inspection_item_revisions`

字段：`id`、`inspection_item_id`、`previous_checked_qty`、`new_checked_qty`、`changed_at_utc`。

- 修改前后数量均非负且必须不同；按明细、修改时间、ID 建稳定历史索引。
- 正式记录不提供删除业务；“只有最近一次正式结果可影响当前状态”是后续业务规则，不在 EF 配置中实现。

## 库存与生命周期留痕

### `inventory_adjustments`

字段：`id`、`product_id`、`excel_stock_qty_snapshot`、`adjusted_stock_qty`、`adjusted_at_utc`。

- 两个数量均非负；记录永久留存。
- 当前只保证调整记录引用商品；真正修改库存及归零联动尚未实现。

### `lifecycle_events`

精确 9 字段：`id`、`product_id`、`batch_id`、`event_type`、`reason`、`occurred_at_utc`、`source_import_id`、`source_inspection_id`、`source_adjustment_id`。

- 商品必填、批次可空；批次和正式排查来源用组合外键保证属于同一商品。
- 三个来源最多一个非空，也允许全部为空；库存修正来源只在数据库层保证记录存在。
- 事件类型仅为 `product_stock_zero`、`batch_checked_zero`、`batch_tracking_resumed`、`task_auto_closed`、`draft_invalidated`。
- 所有外键为 `NO ACTION`。本表不是通用事件框架，也不执行状态转换。

## 导入、工作簿与异常

### `imports`

字段：`id`、`source_file_name`、`source_file_sha256`、`parsed_at_utc`、`confirmed_at_utc`、`status`、`product_count`、`batch_count`、`new_product_count`、`new_batch_count`、`updated_batch_count`、`issue_count`、`unsupported_category_count`、`new_task_product_count`、`pre_import_snapshot_path`、`is_undone`、`undone_at_utc`。

- 文件名、64 位小写十六进制 SHA-256、状态必填；计数均非负。
- `is_undone` 与 `undone_at_utc` 有一致性约束；Stage 2 批准的正式状态只有 `Succeeded`、`Undone`，解析、预览、失败、取消和无变化不写正式记录。
- `new_task_product_count` 当前为非空字段；Stage 2 不实现任务引擎并固定暂写 `0`，该值在 Stage 3 前不具备新增任务数量的业务含义，且不得在 Stage 2 预览展示。
- 最近成功导入时间必须从 `imports` 查询，不在设置或运行状态表重复保存。
- S2-T08 只读候选固定为最新 `Succeeded && !is_undone` 且有确认时间的 Import，以 `confirmed_at_utc DESC, id DESC` 排序；当前没有 BackupRecord FK，故通过唯一规范化快照路径、类型、验证状态、时间和 SHA 建立关联。真正恢复和 Undone 写入未实现。

### `import_workbooks`

字段：`id`、`import_id`、`original_file_name`、`content`、`sha256`、`saved_at_utc`。

- 每条导入记录最多一个非空工作簿 BLOB；SHA-256 格式受约束，外键 `NO ACTION`。
  - S2-T06 在成功导入事务中保存确认契约冻结的原始工作簿；S2-T07 已在同一事务内按 Succeeded Import 的确认时间和 ID 只保留最近两条 Workbook，旧 Import 记录不删除。S2-T08 不改变工作簿，Undone 的最终工作簿语义仍未定义。

### `import_issues`

字段：`id`、`import_id`、`row_number`、`issue_type`、`field_name`、`safe_summary`。

- 行号可空；非空时必须为正数。问题类型和安全摘要 Trim 后非空。

## 备份、设置与运行状态

### `backups`

字段：`id`、`backup_type`、`file_path`、`sha256`、`created_at_utc`、`verification_status`。

- 类型仅允许 `auto`、`manual`、`pre_import`、`pre_restore`、`pre_upgrade`。
  - S2-T05 已实现导入前快照文件创建、哈希和验证；S2-T06 已在成功导入事务中保存 `pre_import` / `verified` 元数据。恢复和自动保留 7 份仍未实现。

### `settings`

字段：`id`、`reminder_minute_of_day`、`auto_start_enabled`。

- 单例约束 `id = 1`；默认提醒时间为 600 分钟，即 10:00；自启动偏好默认开启。

### `app_state`

字段：`id`、`last_reminder_date`、`last_normal_run_date`。

- 单例约束 `id = 1`；两个运行日期允许为空。
- 软件版本和数据目录运行时读取，不落重复字段。

## 不可混淆的业务规则（尚待后续实现）

- Excel 是局部增量数据；未出现的商品或批次不得因此改变任何状态。
- 商品唯一主体只认 `商品编码`；名称和条码变化只更新当前展示值，历史快照不改。
- 批次一旦按唯一键出现过就是旧批次，停止跟踪后也不得删除或冒充新批次。
- 正式排查某批次为 0 件，只停止该批次；商品明确库存为 0 则结束该商品全部批次，后者优先级更高。
- 真正新到货只发生在当前累计到货首次大于历史最高值；下降或回升到旧最高值均不算。
- 仅因批次 0 件停止、商品从未归零且当前库存大于 0 时，突破历史最高累计到货才允许恢复同批次；商品曾归零后旧批次永久不得恢复。
