# 下一任 GPT-5.6 Sol 产品经理接任说明｜Stage 7

> 交接日期：2026-08-31。Stage 6 已整体验收并归档；本文只整理导出 / 备份 / 恢复方向，不是 Task 卡或开发授权。当前未创建、编号或派发 S7-T01，未实施 Stage 7 生产代码。

## 一、接任基线

- 分支：`master`
- Stage 6 总验收前基线：`dd79fb57f10286af58ae4a2d788beeb28db216b2`
- Stage 6 总验收：`.ai-dev/ACCEPTANCE/STAGE-6.md`
- Stage 6 closeout：`.ai-dev/STAGES/STAGE-6-CLOSEOUT.md`
- Stage 0～Stage 6 已完成；Stage 7 尚未开始。
- 当前 schema 为 17 张业务表，仓库 migration=8，Stage 6 无 schema 或依赖变化。
- 最新技术基线：S6-T01～T04 为 13/13、10/10、12/12、17/17；Stage 5 51/51、Stage 4 179/179、Stage 3 170/170、Release 635/635；build 0 warning / 0 error；EF 无漂移。

最终治理提交 SHA 必须以接任时 `git rev-parse HEAD` 重新核实，不得从本文件推测。

## 二、Stage 0～6 已完成核心能力

- Stage 0～2：项目骨架、SQLite 持久化、固定 Excel 模板读取、局部增量导入、确认守卫、快照、Workbook 保留和撤销资格。
- Stage 3：效期阶段、商品任务聚合、启动补算、库存 0、新批次/新到货/恢复、正式停止与生命周期编排。
- Stage 4：门店排查 Shell、Dashboard、任务列表、导入、详情、草稿、重新确认、库存修正和正式提交。
- Stage 5：正式排查历史、详情、Revision 链及受控单明细数量修订。
- Stage 6：每日集中提醒、Windows 提醒渠道、单实例、托盘常驻、到点 scheduler、提醒时间设置和当前用户级开机自启动。

## 三、不得重写的权威边界

- Excel 是局部增量，不是全量快照；缺失行不表示删除、归零、停止或恢复。
- Stage 3 的阶段、任务聚合与生命周期服务仍是唯一权威；导出/备份/恢复不得复制或改写其规则。
- Stage 4 的 Draft、Reconfirm、InventoryAdjustment 与 Submission 事务边界必须保持。
- Stage 5 的历史查询、当前数量修订和 Revision 留痕必须保持；恢复不得伪造或丢失审计链。
- Stage 6 的提醒候选、同日一次、成功登记、单实例、托盘、scheduler、Settings 和 HKCU 自启动必须复用，不建设第二套后台服务或提醒状态。

## 四、Stage 7 已知产品范围

Stage 7 主题限定为：

1. 数据导出：明确导出对象、格式、只读语义、隐私与失败反馈。
2. 本地备份：生成一致、可识别、可保留的本地备份，不破坏当前正式库。
3. 数据恢复：显式选择、校验、确认、失败回滚和成功后的应用状态恢复。
4. 数据保护 / 完整性：文件身份、schema/migration 兼容、SQLite 一致性、原子替换、sidecar 与进程门禁、可审计结果。

本 handoff 不决定具体文件格式、保留策略、恢复 UX 或 Task 拆分；这些必须在 Stage 7 获批后基于仓库事实形成最小任务。

## 五、正式数据库保护规则

- 正式数据库不得作为自动化或 GUI 写测试数据源；测试继续使用隔离副本。
- 任何备份/恢复工作前先确认应用进程为 0，并正确处理 `-wal`、`-shm`、`-journal` 与 SQLite 一致性。
- 当前正式基线为 299008 bytes，SHA-256 `F3D423DF14B882D7BFE87780A81CF5879F074AF4880601CBEDB6B475A964F522`，migration=8；接任时必须重新核实，不得盲信历史值。
- 历史正式备份必须保留；未经明确授权不得覆盖、删除或批量移动。
- 工具侧默认 `LOCALAPPDATA` 存在既有异常 Junction。若工具无法直读，不得绕过保护或把失败写成通过；使用用户本机回执或经批准的只读副本方案。
- 恢复成功后核对文件大小、SHA-256、migration、SQLite sidecar、应用进程和隔离目录；不得为了复看结果随意启动 GUI。

## 六、验收与 UI 治理

- Stage 7 的真实导出文件、备份、恢复、Windows GUI 与正式环境恢复仍由用户本人验收；Codex 不以源码测试替代真实结果。
- 技术验收必须包含精确专项、Stage 3～6 权威回归、Release 全量、build、EF、migration、dependency、Git 和正式数据保护。
- 当前 UI 的纯视觉问题继续记为 debt，不在 Stage 7 功能 Task 中顺手重构。
- Stage 7 功能完成并验收后，按既定策略统一进行全局 UI/UX 重构与视觉收口；不得让视觉重构改写 Stage 3～7 业务语义。

## 七、接任门禁

- 本文件不是开发授权。
- 不创建 S7-T01，不编号 Stage 7 Task，不派发 Stage 7 Luna，不实施 Stage 7 代码。
- 用户明确批准进入 Stage 7 后，先重新核对 branch、HEAD、status、Stage 6 closeout、数据库、migration、依赖、现有 BackupMetadata 能力及正式数据保护条件，再决定第一张最小任务卡。
