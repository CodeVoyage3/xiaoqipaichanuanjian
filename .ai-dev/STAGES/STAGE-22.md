# Stage22｜商品数据查询与导入差异增强

日期：2026-09-14（Asia/Shanghai）

Stage22 = `IN_PROGRESS / S22-T01_UI_REWORK_IMPLEMENTED / WAITING_USER_GUI_RETEST`

## 产品目标

“导入时看变化，平时按商品查全量，需要时再展开看批次。”

Stage22 只允许两张 Task：

1. S22-T01｜商品明细与商品详情：当前唯一授权实施范围。
2. S22-T02｜导入差异与异常中心：`NOT_CREATED / NOT_STARTED`，只有 S22-T01 `CLOSED / ACCEPTED` 后才允许另行建立。

不得创建 S22-T03。

## 当前冻结

- fresh baseline=`origin/main@d97e7bda89c2ab047bf09edb80073c069ed9090b`。
- 当前正式版本保持 `v1.1.0`；migrationCount=`10`。
- S22-T01 采用 `NO_SCHEMA_CHANGE / NO_MIGRATION11`。
- 当前总库存只取 `Product.EffectiveStockQty`；不得把批次累计到货相加冒充当前库存。
- 批次级不得显示“当前库存”；如展示 `CurrentArrivalQty`，只能命名为“累计到货”，空间不足可删除。
- 当前无业务批次号。批次明细以生产日期 + 到期日期识别，不生成虚构编号。
- 删除商品级“保质期 / 总效期”和独立“商品概览”卡片；最近排查日期降为辅助信息；不重复展示“当前是否有待处理”。
- “最近导入信息”首版只展示最近一次。
- 商品明细列表不显示“最近导入”；该信息只在详情身份区及底部“最近导入信息”中呈现。
- 无待办批次操作显示 `—`；不得制造无效“查看批次”入口。

## 永久边界

- 不修改 Product、Batch、Task、Import 等现有 Schema，不创建 migration11，不改 `CurrentSchemaIdentity`。
- 不修改现有导入、生命周期、Stage/Task、库存、排查提交、备份恢复业务语义。
- 不修改版本、tag、GitHub Release、latest、Quark、Gitee、Installer、Updater 或 Release Contract。
- 实现必须复用现有 WPF 控件、资源、样式和页面组织；禁止新增大型 UI 框架、WebView、浏览器渲染、复杂动画、玻璃/云母或主题大改。
- `FULL=NOT_RUN / NO_FULL`。只运行 S22-T01 专项、直接相关旧回归和必要 Release App build。
- Sol 技术 PASS 与用户真实 WPF GUI PASS 缺一不可；用户 GUI PASS 前 S22-T01 不得 `CLOSED / ACCEPTED`。
