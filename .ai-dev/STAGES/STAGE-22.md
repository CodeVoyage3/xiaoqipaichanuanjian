# Stage22｜门店执行与异常反馈增强

日期：2026-09-13（Asia/Shanghai）

Stage22 = `IN_PROGRESS / S22-T01_GOVERNANCE_FROZEN / IMPLEMENTATION_DISPATCH_AUTHORIZED`

fresh 起点：`origin/main@a7b1f34ce935cd306825279d5b33444c5244b114`

## 产品背景

门店不拥有订货权；商品由总部依据门店动销与库存统一配货。因此 Stage22 不继续建设“未来风险趋势、峰值预测、订货建议、AI 动态折扣”等门店无法直接采取行动的能力。现有 7/14/30 天未来效期风险总览继续保留，不扩展为趋势预测。

Stage22 聚焦三个门店可控制的问题：总部给到的数据发生了什么、门店现场库存为什么被修正、反复效期风险如何形成可反馈证据。

## 固定顺序

1. `S22-T01｜库存修正原因与留痕增强`：`GOVERNANCE_FROZEN / IMPLEMENTATION_DISPATCH_AUTHORIZED / NOT_IMPLEMENTED / NOT_ACCEPTED`。
2. `S22-T02｜导入差异与异常中心`：`PLANNED / NOT_CREATED / NOT_DISPATCHED`；S22-T01 用户 GUI 关闭前不得实施。
3. `S22-T03｜重复效期风险商品与一键反馈`：`PLANNED / NOT_CREATED / NOT_DISPATCHED`；依赖前两项形成稳定数据口径后再设计。

## S22-T01 产品合同摘要

- 复用现有排查详情“修正库存”入口，不新增独立库存模块或导航页。
- 实际库存发生变化时必须记录结构化原因；固定原因：`已销售`、`调拨`、`退仓`、`报损`、`盘点差异`、`总部库存数据滞后`、`其他`。
- `其他` 必须补充简短说明；其他原因允许说明为空。说明只作为短备注，不引入长文本流程。
- 新产生的库存修正记录必须明确保存“修正前有效库存 → 修正后库存”，不能用 Excel 库存快照冒充修正前库存。
- 历史旧记录保持原样；不得猜测或回填旧记录不存在的原因、修正前库存。旧数据允许显示为“旧版记录未保存原因/修正前库存”。
- 修正为 0 的现有二次确认、商品终止、任务关闭、草稿失效和批次生命周期语义全部保持；原因留痕不能绕过这些门禁。
- 修正值与当前有效库存相同仍为 `NoChange`：不得新增 adjustment 记录，也不得制造虚假原因历史。
- 本卡不新增用户登录、人员账号、权限或“操作人”字段；当前产品没有可靠身份源，不允许伪造操作人。

## Schema 裁决

S22-T01 批准 **一条最小 migration 11**，只服务库存修正留痕：

- `inventory_adjustments.previous_effective_stock_qty`：nullable integer；历史行保持 null，新写入必须保存真实修正前有效库存。
- `inventory_adjustments.reason_code`：nullable text；历史行保持 null，新写入必须为允许的稳定 code。
- `inventory_adjustments.reason_note`：nullable text；仅短说明；`other` 新写入必须非空。

允许增加与上述字段直接相关的最小 check constraint / EF configuration / ModelSnapshot / `CurrentSchemaIdentity` 第 11 项。禁止新增其他表、索引、业务字段或修改既有 migration 1～10。

因为 migration 从 10 变为 11，本 Stage 的下一正式候选属于 schema-changed candidate；后续 Release 阶段必须走明确批准的 cross-schema release contract。Stage21 的 `SAME_SCHEMA_SLIM` 能力仍成立，但不得误用于本次 schema-changing 产品版本。

## 固定边界

- 不修改 5折/2折/收仓/过期规则、3 天预提醒、冷启动、任务生成、库存 0 停止跟踪、今日排查、历史 Revision、未来风险总览。
- 不修改在线更新协议、Updater、Installer、Release Builder、版本号、tag、GitHub Release、Gitee/夸克远端状态。
- 不新增依赖。
- 正式数据库 `NO ACCESS`；只允许隔离数据库/迁移副本验证。
- `FULL=NOT_RUN / NO_FULL`；只跑 S22-T01 直接专项、必要迁移验证、Release App build、EF no-drift 与隔离 GUI。

## 治理方式

- Sol 负责产品/技术治理、fresh 审查、自动化证据和验收范围，不直接写生产代码。
- 实施必须由本卡全新的 GPT-5.6 Terra（reasoning medium、标准速度）从治理提交后的 fresh `origin/main` 建立 clean worktree。
- Terra 不得自判 `ACCEPTED/CLOSED`，不得发布、tag、上传资产或修改正式远端元数据。
- 技术通过后只能进入 `IMPLEMENTED / TECHNICAL_ACCEPTANCE_READY / NOT_ACCEPTED`；用户真实 WPF GUI 验收通过后才允许关闭 S22-T01。
