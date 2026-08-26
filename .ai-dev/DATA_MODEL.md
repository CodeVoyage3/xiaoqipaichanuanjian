# 数据模型草案

> Stage 0 逻辑模型；字段名可在 Stage 1 任务卡中做一次最小收敛，业务语义不可漂移。

## 主数据

### products

- `id`
- `product_code`：唯一且非空，商品唯一身份
- `current_name`、`current_barcode`
- `category_code`、`policy_code`
- `excel_stock_qty`、`effective_stock_qty`、`effective_stock_source`
- `lifecycle_generation`：每次商品从非零生命周期进入明确库存归零时递增
- `is_stock_zero_terminated`
- `last_seen_import_id`
- `created_at_utc`、`updated_at_utc`

约束：`UNIQUE(product_code)`。

### batches

- `id`、`product_id`
- `production_date`：可空
- `expiry_date`
- `shelf_life_value`、`shelf_life_unit`
- `current_arrival_qty`、`max_arrival_qty`
- `source_discount_reference`
- `lifecycle_generation`
- `tracking_status`、`stop_reason`、`stopped_at_utc`
- `current_stage`、`next_trigger_date`
- `attention_version`：阶段升级、真正新到货或合法恢复时递增
- `handled_attention_version`
- `last_seen_import_id`
- `created_at_utc`、`updated_at_utc`

批次键：

- 有生产日期：`商品编码 + 生产日期 + 有效日期`
- 无生产日期：`商品编码 + 有效日期`

约束使用 SQLite 原生部分唯一索引，不拼接字符串键：

- `production_date IS NOT NULL`：`UNIQUE(product_id, production_date, expiry_date)`。
- `production_date IS NULL`：`UNIQUE(product_id, expiry_date)`。

批次一旦出现，停止跟踪后仍永久保留记录；不同商品允许日期相同。

关键索引：`(tracking_status, next_trigger_date)`、`product_id`、`expiry_date`。

## 当前任务与草稿

### tasks

- `id`、`product_id`
- `status`：open / completed / system_closed
- `highest_stage`
- `created_at_utc`、`updated_at_utc`、`closed_at_utc`
- `close_reason`

约束：SQLite 部分唯一索引保证每个商品最多一条 `open` 任务。

### task_items

- `id`、`task_id`、`batch_id`
- `stage`
- `attention_version`
- `requires_reconfirmation`
- `created_at_utc`、`updated_at_utc`

约束：同一开放任务中一个批次最多一项；版本字段用于幂等生成和草稿重新确认。

### drafts / draft_items

- `drafts`：`task_id` 唯一、排查人、检查日期、更新时间、是否失效、失效原因
- `draft_items`：`draft_id`、`task_item_id`、排查件数、已确认的 `attention_version`

草稿失效保留记录，不转换为正式排查。

## 正式排查与修改历史

### inspections

- `id`、`task_id`
- 商品编码/名称/条码快照
- 排查阶段快照、商品库存快照
- 排查人、检查日期、提交时间

### inspection_items

- `id`、`inspection_id`、`batch_id`
- 生产日期/有效日期/阶段/累计到货快照
- `checked_qty`
- `updated_at_utc`

### inspection_item_revisions

- `id`、`inspection_item_id`
- 修改前值、修改后值、修改时间

正式记录不提供删除路径。只有批次最近一次正式排查项的当前值变化可以触发当前跟踪状态重算。

## 库存与生命周期留痕

### inventory_adjustments

- `id`、`product_id`
- Excel 原始库存、修正后库存、修正时间

### lifecycle_events

- `id`、`product_id`、`batch_id`（可空）
- `event_type`、`reason`、`occurred_at_utc`
- `source_import_id`、`source_inspection_id`、`source_adjustment_id`（按来源可空）

用于记录商品归零、批次 0 件停止、合法恢复、任务系统自动结束和草稿失效，不代替正式排查记录。

## 导入、文件、备份与设置

### imports

- `id`、文件名、文件哈希
- 解析/确认时间、结果状态
- 商品数、批次数、新增数、更新数、异常数、非食品跳过数、新增任务商品数
- 导入前快照路径、是否已撤销、撤销时间

导入记录长期保存；不长期保存每份原始工作簿。

### import_workbooks

- `id`、`import_id`
- 原始文件名、内容 BLOB、SHA-256 哈希、保存时间

只保留最近两次成功导入对应的两行。工作簿与导入业务数据在同一 SQLite 事务写入，导出前重新校验哈希。

### import_issues

- `id`、`import_id`、行号、问题类型、字段与安全摘要

### backups

- `id`、类型（auto/manual/pre_import/pre_restore/pre_upgrade）
- 路径、哈希、创建时间、验证结果

### settings / app_state

- 提醒时间（默认 10:00）
- 开机自启动（默认开启）
- 最近成功导入时间
- `last_reminder_date`
- `last_normal_run_date`
- 软件版本与数据目录只读信息

## 事务边界

- 一次确认导入：单事务，失败整次回滚。
- 一次商品排查提交：任务、排查主表/明细、批次状态、草稿状态单事务。
- 一次历史修改：修订历史与必要的当前状态重算单事务。
- 商品归零：商品、全部批次、开放任务、草稿、生命周期事件单事务。
