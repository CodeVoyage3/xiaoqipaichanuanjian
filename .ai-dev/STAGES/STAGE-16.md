# Stage16｜v1.0.8

日期：2026-09-10（Asia/Shanghai）

Stage16 = `CLOSED`

v1.0.8 = `RELEASED`

S16-T01 = `GUI_ACCEPTED / CLOSED`。S16-T02 = `GUI_ACCEPTED / CLOSED`。S16-T03 = `GUI_ACCEPTED / CLOSED`。Stage16 全部既定 Task 已完成。

## 当前范围与顺序

1. `S16-T01｜未来效期风险总览与风险明细`：`GUI_ACCEPTED / CLOSED`。
2. `S16-T02｜安装完成后自动启动主程序`：`GUI_ACCEPTED / CLOSED`。
3. `S16-T03｜首页优先处理摘要化与未来风险可见性优化`：`GUI_ACCEPTED / CLOSED`。

Stage16 是此前预留的“未来效期风险总览”阶段；Stage17 已关闭后回到 Stage16，不改名为 Stage18。

## 固定边界

- S16-T01 只读预测，完全复用当前商品、批次、库存、已生效 policy 与阶段规则；不得创建 `ProductTask`、提前提醒、修改阶段/库存/批次或改变今日排查、冷启动及 5折/2折/收仓/过期规则。
- 首页仅在当前“优先处理”之后加入轻量 `7/14/30 × 5折/2折/收仓/过期` 总览；点击数字进入无左侧导航入口的只读明细。
- S16-T02 只允许以后单独使用 Inno Setup 最小能力实现普通人工安装完成后的单实例启动，并与现有 Updater 重启职责隔离。
- 不改 Schema、migration、ModelSnapshot、新数据库、新依赖、Updater、更新协议、Gitee 或夸克；migration 保持 `9`。
- `FULL = NOT_RUN / NO_FULL`。S16-T01、S16-T02、S16-T03 均已分别完成技术与用户验收；Stage16 关闭本身不等于发布 v1.0.8。

任务与验收见 `../TASKS/S16-T01.md`、`../ACCEPTANCE/S16-T01.md`、`../TASKS/S16-T02.md`、`../ACCEPTANCE/S16-T02.md`、`../TASKS/S16-T03.md` 与 `../ACCEPTANCE/S16-T03.md`。

## Stage16 最终交付与证据

- S16-T01：未来风险 `7/14/30 × 5折/2折/收仓/过期` 矩阵、只读明细、当前/后续阶段、固定表头、内部滚轮及最终 7 列表格。
- S16-T02：全新/覆盖安装完成后自动启动，主窗口/托盘、单实例、原数据保持及 Installer Mutex 生命周期修复。
- S16-T03：首页优先处理最多 5 条，真实总数与“查看全部”完整列表保持，未来风险不再被长列表推离首屏。
- 技术证据全部继承，不重新执行：`FULL = NOT_RUN / NO_FULL`；Schema/migration `NO CHANGE`，migration `9`；正式数据库 `NO ACCESS`；正式 dirty 工作区保持。
- Stage18 / v1.0.9 的提醒弹窗与扫码详情方向不属于 v1.0.8，发版完成前不得实施。

## v1.0.8 正式发布结果

- 精确产品 source：`4027b6c195b6ff89d22c315c22a30b33a799079c`；annotated tag `v1.0.8` 精确指向该 source。
- GitHub Release ID `386129690`；四项公网资产与冻结候选 bytes/SHA256 逐项全等；production RSA-PSS/SHA256 signature `PASS`。
- ZIP：`109428664` bytes / `D4270B8D2B0ABCC50E860FAC05322B5DA79C746471DD088FBDD6AB373CFA369F`。
- Setup：`75327902` bytes / `FC6E4B974463056ABF3119AF512B97AAA3CD3F0DB4D19607232D8C71F010414B`。
- manifest：`869` bytes / `33CC069186830959BCD8773199D027DC03D4125E21A17736D157DAA123E93B15`；signature：`384` bytes / `34270FD7876FADA3D40E3803D071BBA1733DD3F67F5D38A7932A174E1CB0181C`。
- 同一冻结 Setup 的夸克真实预检与正式上传 `PASS`；正式分享 <https://pan.quark.cn/s/43763c1d96bf>。
- Gitee `master/latest.json` commit `50ab5ab133badfdacb153687fb6dc72ba5309fbe`；Raw 匿名 HTTP `200` 且三字段精确一致。
- `FULL = NOT_RUN / NO_FULL`；migration `9`；正式数据库 `NO ACCESS`；正式 dirty 工作区保持；Stage18 未实施。
