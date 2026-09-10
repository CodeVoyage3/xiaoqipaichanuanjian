# Stage16｜v1.0.8

日期：2026-09-10（Asia/Shanghai）

Stage16 = `IN_PROGRESS / S16_T01_CLOSED / S16_T02_READY`

用户已确认 S16-T01 GUI 主体验收通过，最终单列收口亦已通过 Sol 独立核验。S16-T01 = `GUI_ACCEPTED / CLOSED`。S16-T02 已具备启动条件，但仍保持 `PLANNED / NOT_DISPATCHED`，等待单独授权。

## 当前范围与顺序

1. `S16-T01｜未来效期风险总览与风险明细`：当前唯一实施任务。
2. `S16-T02｜安装完成后自动启动主程序`：`PLANNED / NOT_DISPATCHED`，不得与 T01 混合实施。

Stage16 是此前预留的“未来效期风险总览”阶段；Stage17 已关闭后回到 Stage16，不改名为 Stage18。

## 固定边界

- S16-T01 只读预测，完全复用当前商品、批次、库存、已生效 policy 与阶段规则；不得创建 `ProductTask`、提前提醒、修改阶段/库存/批次或改变今日排查、冷启动及 5折/2折/收仓/过期规则。
- 首页仅在当前“优先处理”之后加入轻量 `7/14/30 × 5折/2折/收仓/过期` 总览；点击数字进入无左侧导航入口的只读明细。
- S16-T02 只允许以后单独使用 Inno Setup 最小能力实现普通人工安装完成后的单实例启动，并与现有 Updater 重启职责隔离。
- 不改 Schema、migration、ModelSnapshot、新数据库、新依赖、Updater、更新协议、Gitee 或夸克；migration 保持 `9`。
- `FULL = NOT_RUN / NO_FULL`。S16-T01 技术验收后必须停在等待用户按批准原型完成 GUI 验收；不得开始 S16-T02 或发布 v1.0.8。

任务与验收见 `../TASKS/S16-T01.md`、`../ACCEPTANCE/S16-T01.md` 与 `../TASKS/S16-T02.md`。
