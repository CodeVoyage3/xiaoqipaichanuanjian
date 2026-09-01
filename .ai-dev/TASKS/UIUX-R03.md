# UIUX-R03｜剩余页面全局 UI/UX 统一铺开

## 授权与角色

- 日期：2026-09-01。
- 基线：`master`，开工 HEAD `c2e62c2ae2644178486b99891ac0e3669333d9bd`；UIUX-R02 已归档，Stage 8 未开始。
- Luna 负责实现，Sol 负责独立范围与技术验收，真实 WPF GUI 由用户本人验收。
- 原 Luna 多次上下文压缩后停止，用户明确批准全新的 GPT-5.6 Luna（max）接管同一 UIUX-R03；不得重置或丢弃既有有效修改。

## 实施范围

把 UIUX-R01 / UIUX-R02 已批准并验证的设计系统铺到剩余 WPF 页面：

1. Excel 数据导入；
2. 排查历史；
3. Revision 查看与数量修改；
4. Reminder 设置；
5. 数据备份与恢复；
6. 应用内部 Confirmation / Warning / Danger / Error / Empty 状态；
7. Shell 与共享资源的必要一致性收口。

允许修复用户明确指出的 Dashboard、待排查和排查详情回归，但不得重新设计 UIUX-R02 三类代表页。

## 冻结边界

- 不新增业务功能、筛选、排序、分页、导出或业务状态。
- 不改变 Excel 局部增量、Draft / Reconfirm / Submission、History / Revision、Reminder、Backup / Restore 权威逻辑。
- 不修改 Domain、Application 业务契约、Infrastructure、schema、migration、dependency 或 target framework。
- 不创建 UIUX-R04，不进入 Stage 8。

## 用户返修闭环

统一返修覆盖搜索回归、提交失败反馈、导入流程表达、输入焦点稳定、详情三段式滚动、历史编辑上下文、自定义 WPF Dialog、备份/恢复去技术化、导航折叠、首页重复入口、字段对齐和用户文案等问题。

后续定向返修进一步完成：

- 折叠导航顶部样式；
- 已知提交错误中文可操作提示，未知异常使用通用安全提示；
- 详情中间批次列表独立滚动，顶部与底部固定；
- Revision 全部用户文案统一为“批次”，去除用户可见 UTC；
- 待排查分页底部安全间距；
- 备份主列表最后一列自适应；
- 导入成功反馈统一并显示结果摘要；
- 历史详情“批次 / 累计到货 / 正式排查数量”表头和单元格居中；
- “批次”改为当前正式排查记录内从 1 连续编号的纯展示序号，表格、Revision、编辑标题和确认弹窗均不暴露内部明细 ID；内部 `InspectionItemId` 继续用于 Revision 与保存。

## 完成门禁

必须由 Sol 重新执行 UIUX-R03 专项、UIUX-R02 回归、Stage 3～7、Release 全量、Release build、EF drift、migration、dependency 和 `git diff --check`，并由用户完成真实 GUI 复验。正式数据库恢复为 299008 bytes 指定 SHA、无 sidecar、进程为 0，恢复后不得再次启动 WPF。全部通过后才可提交和归档。
