# Stage22｜商品数据查询与导入差异增强

日期：2026-09-14（Asia/Shanghai）

Stage22 = `IN_PROGRESS / S22-T01_CLOSED / S22-T02_GUI_R1_TECHNICAL_PASS_USER_GUI_RETEST_PENDING`

## 产品目标

“导入时看变化，平时按商品查全量，需要时再展开看批次。”

Stage22 只允许两张 Task：

1. S22-T01｜商品明细与商品详情：`CLOSED / ACCEPTED`。
2. S22-T02｜导入差异与异常中心：`IMPLEMENTED / GUI_R1_TECHNICAL_PASS / USER_GUI_RETEST_PENDING / NOT_ACCEPTED`。

不得创建 S22-T03。

## 当前冻结

- fresh baseline=`origin/main@d97e7bda89c2ab047bf09edb80073c069ed9090b`。
- 当前正式版本保持 `v1.1.0`；migrationCount=`10`。
- S22-T01 采用 `NO_SCHEMA_CHANGE / NO_MIGRATION11`。
- 当前总库存只取 `Product.EffectiveStockQty`；不得把批次累计到货相加冒充当前库存。
- 批次级不得显示“当前库存”；如展示 `CurrentArrivalQty`，只能命名为“累计到货”，空间不足可删除。
- 当前无业务批次号。批次明细以生产日期 + 到期日期识别，不生成虚构编号。
- 删除商品级“保质期 / 总效期”和独立“商品概览”卡片；最近排查日期降为辅助信息；不重复展示“当前是否有待处理”。
- 最近导入日期只在商品详情顶部身份区展示；商品列表不显示，详情底部不保留独立“最近导入信息”卡片。
- 无待办批次操作显示 `—`；不得制造无效“查看批次”入口。

## 永久边界

- 不修改 Product、Batch、Task、Import 等现有 Schema，不创建 migration11，不改 `CurrentSchemaIdentity`。
- 不修改现有导入、生命周期、Stage/Task、库存、排查提交、备份恢复业务语义。
- 不修改版本、tag、GitHub Release、latest、Quark、Gitee、Installer、Updater 或 Release Contract。
- 实现必须复用现有 WPF 控件、资源、样式和页面组织；禁止新增大型 UI 框架、WebView、浏览器渲染、复杂动画、玻璃/云母或主题大改。
- `FULL=NOT_RUN / NO_FULL`。S22-T02 只运行本卡专项、直接相关旧回归和必要 Release App build。
- S22-T01 已取得 Sol 技术 PASS 与用户真实 WPF GUI PASS，现为 `CLOSED / ACCEPTED`；Stage22 因 S22-T02 已冻结并授权实施而保持 `IN_PROGRESS`。

## S22-T02 冻结口径

- 2026-09-14 已从 fresh `origin/main@022ee5691cda9aa4184b7ba93599be0eab4bf3a6` 完成导入链路审计并获用户确认。
- 库存变化采用方案 A：本次 ExcelStockQty 对比上一次有效 ExcelStockQty；变 0 仅为已有商品 before `> 0`、after `= 0`。
- 缺失以最近一次成功未撤销导入为基准，主数按缺失批次；缺失待办为其 open task 只读子集。
- 首次导入无历史基准指标显示 `—`；异常以成功后实际 ImportIssue 数为准。
- 首版只显示本次成功导入即时摘要，不做历史回看、复杂钻取或新导航。
- migrationCount 保持 `10`，migration11 不创建；详见 `../TASKS/S22-T02.md` 与 `../ACCEPTANCE/S22-T02.md`。
- final implementation=`8ccc5e84b5aad8c3eae740c764506ef502662e7c`；Sol 独立定向门禁 `63/63 + 36/36 PASS`，Release App build `0 warning / 0 error`，禁止范围 diff=`0`。
- 当前只待用户本人真实 WPF GUI 验收；未获明确 GUI PASS 前不得 `CLOSED / ACCEPTED`。
- 用户首轮 GUI=`FAIL`；纯 GUI 返修 R1 已通过定向 32/32 与 Release build 0/0，并生成完整页、1024×600、七项指标隔离截图；当前等待用户复验，仍 `NOT_ACCEPTED`。
