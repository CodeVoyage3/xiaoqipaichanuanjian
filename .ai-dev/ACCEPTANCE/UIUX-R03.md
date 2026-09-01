# UIUX-R03 最终验收｜剩余页面全局 UI/UX 统一铺开

日期：2026-09-01。结论：**实现、Sol 独立技术验收、用户真实 WPF GUI 验收和正式数据库恢复均已通过，UIUX-R03 正式完成并归档。**

## 范围与实现

- Excel 导入、排查历史、Revision、Reminder 设置、备份/恢复、应用内部 Dialog 和 Shell 使用 UIUX-R01 / R02 已批准的浅色原生 WPF 设计系统统一收口。
- Dashboard / 待排查搜索恢复既有商品名、条码和编码查询；未新增客户端筛选、排序或分页。
- 导入页移除与真实流程不符的 Stepper，区分解析与正式导入状态，成功反馈统一为单一区域并显示本次结果摘要。
- 详情页输入焦点不再改变布局；最终结构为固定顶部商品/排查信息、独立纵向滚动的中间批次列表和固定底部操作栏，10、20 条及更多批次均可访问填写。
- 提交失败区域靠近底部操作栏；已知验证错误映射为中文可操作提示，未知异常不向门店暴露内部英文异常。
- 历史与 Revision 使用“批次”用户文案、去除 UTC 展示、移除用户可见内部批次 ID；“批次 / 累计到货 / 正式排查数量”表头和内容居中。
- 历史详情使用 UI 层展示行，按当前正式排查记录生成从 1 连续编号的 `DisplayBatchNumber`；表格、选中提示、编辑标题和确认弹窗统一使用该序号。原 `InspectionHistoryItemDetail` 与 `InspectionItemId` 保持不变，Revision、保存和刷新仍以内部身份执行。
- 应用内部确认、警告、危险和错误提示统一为 WPF Dialog；系统文件选择器保持原生。备份列表和恢复确认去除 GUID、raw bytes 等低价值技术信息，底层 SHA/integrity/migration/rollback 权威未改变。
- 导航支持折叠/展开及 Tooltip；首页移除重复导入按钮；分页、列宽、长商品名、条码/编码与状态反馈按用户清单完成收口。

## 最终独立技术结果

| 门禁 | 结果 |
|---|---|
| UIUX-R03 最终相关定向组合 | 31/31，通过；其中 UIUX-R03 静态审计 3/3 |
| Stage 3 | 170/170 |
| Stage 4 | 186/186 |
| Stage 5 | 52/52 |
| Stage 6 | 52/52 |
| Stage 7 | 51/51 |
| Release 全量 | 首轮 691/692，S7-T03 既有异步等待测试偶发超时；该测试单独复跑 1/1，随后完整复跑 692/692，0 失败、0 跳过 |
| Release build | 0 warning / 0 error |
| EF model drift | 无漂移 |
| migration | 仓库 8 条；正式基线同已验证 8 migration 身份 |
| dependency / project / schema | 无 dependency、项目文件、Application、Domain、Infrastructure、schema 或 migration 变化 |
| `git diff --check` | 通过；仅有工作区 LF/CRLF 提示 |

构建和测试使用既有离线依赖；本轮不声明在线 NuGet 漏洞审计成功。自动化测试使用测试路径，恢复正式基线后未重新运行 WPF。

## 用户 GUI 验收

- 用户完成多轮真实 WPF 验收；统一返修清单、后续八项定向返修、详情多批次滚动、历史三列居中以及批次展示序号均逐项复验。
- 最终用户回执为“02通过”，至此所有已知 GUI 项全部通过。
- 详情滚动最终确认顶部固定、中间独立滚动、底部固定；历史批次从 1 连续编号，不再暴露内部明细 ID，Revision 与确认弹窗同步使用展示序号。

## 正式数据库恢复

- 恢复前 WPF 进程为 0，测试 `app.db` 为 2617344 bytes。
- 已验证基线源为 `C:\Users\39037\AppData\Local\StoreExpiryInspector.s4t09-backup-20260828\data\app.db`，大小 299008 bytes，SHA-256 `F3D423DF14B882D7BFE87780A81CF5879F074AF4880601CBEDB6B475A964F522`。
- Codex 进程的直接路径视图仍保留历史损坏 Junction，常规读取返回 `ERROR_MOUNT_POINT_NOT_RESOLVED`；未删除、修复或重建 Junction。
- 使用本机管理员共享访问同一用户现场普通目录，先重新读取测试库身份，再复制基线到同目录 staging 并校验大小/SHA，使用 `File.Replace` + 显式 rollback 原子替换；成功核验后删除仅含可丢弃测试库的 rollback。
- 最终正式 `app.db` 精确为 299008 bytes，SHA-256 精确一致；`-wal`、`-shm`、`-journal`、staging 和 rollback 均不存在；WPF 进程为 0。
- 恢复后未再次启动 WPF。

## 最终范围与归档

- 生产修改限定为 WPF resources、presentation ViewModel、窗口/code-behind 和纯 UI 对话/历史展示行；对应静态/contract/ViewModel 测试同步更新。
- 没有任务外业务、导出、Stage 8、schema、migration、dependency 或项目文件变化；工作区中的全部差异均属于 UIUX-R03 实现、回归测试或必要归档记录。
- UIUX-R03 正式归档后停止；不创建 UIUX-R04，不进入 Stage 8，不启动下一张业务任务。
