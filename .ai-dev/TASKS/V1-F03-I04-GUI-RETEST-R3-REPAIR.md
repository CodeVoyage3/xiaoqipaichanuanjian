# V1-F03-I04｜GUI 重验 R3 增量返修

## 1. 授权、基线与状态

- 日期：2026-09-03。
- 开工基线：`master@9838f99b824188c6eb88835b4c1a8b0834231adf`；与 `origin/main` 同步，工作区开工前干净。
- 当前状态：`V1-F03-I04_GUI_ACCEPTANCE_FAILED / NEED_INCREMENTAL_REPAIR / WAITING_USER_RETEST`；I04 与 V1-F03 不得关闭。
- 本卡只处理用户最新真实 WPF GUI 重验确认的 9 项增量问题；上一轮已通过内容只做防回归，不重新设计。
- 本卡提交后必须在当前 Sol 话题内创建全新 GPT-5.6 Terra（medium、平台标准速度）；禁止复用旧 Terra 或另开独立执行话题。
- Sol 只负责治理、完整 diff 审查、自动化测试和技术验收，不直接修改生产代码。
- 禁止 Stage 8/9、Schema、migration、依赖、Reminder、商品源导入、ProductTask/Inspection/Revision 生命周期、I01～I03 核心算法或全局 UI 重构。

## 2. 已通过内容与防回归边界

- Today 六列表继续固定为：选择、条码、商品名称、大类、当前最高阶段、总库存；不得恢复商品编码、批次数或任务状态。
- 大类筛选、跨筛选 TaskId 选择、当前可见集合全选/取消、实际勾选导出逻辑不变。
- 500+ Task 全量加载、虚拟化、滚动、末行 Checkbox、无整页 Loading/闪退保持不变。
- 独立五列确认 Modal、blank/0/positive、隐藏稳定身份、隐藏/重排行回导、AutoFilter、普通二次确认、Submitted/AlreadySubmitted 权威刷新保持不变。
- DatePicker 必须保持真正 WPF DatePicker，不得退回 TextBox。

## 3. R3-01｜Shell 品牌区对齐与完整显示

- 左上完整显示“门店效期排查软件”，不得截断、贴边或重叠。
- 图标与文字垂直居中，左右留白与既有侧栏一致。
- 1024×600 和用户当前窗口尺寸下成立；不改变导航行为，不建立 Today 专用 Shell。

## 4. R3-02｜Today StageBadge 居中

- “当前最高阶段”的 StageBadge 在 DataGridCell 内水平、垂直居中。
- 不改变中文、颜色或 canonical Stage 映射，不改变行高/虚拟化，不恢复整行蓝色业务选择。

## 5. R3-03｜Today 大类 ComboBox 视觉统一

- 保留现有 ItemsSource、SelectedCategory、默认“全部”、筛选与 TaskId 选择权威，只修视觉。
- 优先局部 Style/Template 或既有 token；高度、边框、圆角、字体、箭头、Hover/Focus 与现有 UI 协调。
- 禁止全局 ComboBox 重构或复制第二套大类映射。

## 6. R3-04｜Excel 当前阶段使用中文业务文案

- 门店可见“当前阶段”必须复用项目现有 canonical Stage 到中文业务文案权威；至少 `expired → 过期`、`withdraw → 收仓`。
- 禁止 exporter 私建可能漂移的 Stage 规则；不得改变 Stage 判断、Task 选择、批次计算、稳定身份、隐藏字段或格式版本。
- 自动化必须真实调用 I01 生成 workbook，重开文件并覆盖至少两个 Stage，断言可见列没有内部英文值且中文正确。

## 7. R3-05｜Excel 库存表头为“总库存”

- I01 门店可见库存表头精确为“总库存”，Today 保持“总库存”，I02 严格 Reader 与 exporter 同步。
- 底层仍是商品级 `EffectiveStockQty`；隐藏库存快照、稳定身份、格式版本及库存算法不变。
- 自动化必须用真实 I01 文件断言表头，并以同一文件经当前 I02 Reader 回读成功。
- 禁止真正 Merge Cells；同商品多批次每行保留相同商品总库存，不得按批次累加。
- 只有在不改变 Reader 契约时才可复用既有说明区域提示“同一商品多个批次共享商品总库存，请勿按批次累加”；否则不新增结构。

## 8. R3-06｜确认表五列内容垂直居中

- 条码、商品名称、生产日期、有效日期、本次排查数量使用一致的单元格垂直对齐规则。
- 保持数字/日期可读性、五列业务含义、异常浅红行、Tooltip、顶部错误汇总与虚拟滚动；不得恢复校验状态列。

## 9. R3-07｜DatePicker 视觉统一

- 保留真正 WPF DatePicker、默认今天、日历选择、禁止未来日期及既有 CheckDate/BusinessDate 门禁。
- 仅通过局部 Style/Template 收敛高度、边框、圆角、背景、字体、文字垂直位置、日历按钮、Hover/Focus/ValidationError。
- 与排查人输入框协调；错误仍有红色字段反馈，不要求用户手输日期，不做全局 DatePicker 重构。

## 10. R3-08｜过期批次正库存强化提交警告

### 10.1 触发与事实来源

- 正式调用 I03 前，若当前准备提交的数据存在 canonical Stage=`expired` 且本次排查数量 `> 0`，触发强化警告。
- 空白、0、其他 Stage 正数均不触发；禁止按 UI 中文字符串判断，不改变 blank/0/positive 语义。
- 使用当前有效任务/Preview/Draft 已有 canonical Stage 事实，不削弱 I02/I03 陈旧、失效、不完整或超库存门禁。

### 10.2 交互

- 标题：“检测到过期商品仍有库存”。
- 正文至少包含命中的过期批次数 X 与正库存合计 Y，并提示复核现场库存和填写值。
- “返回检查”：停留当前确认窗口，不调用 I03，不清除填写结果。
- “确认无误，继续提交”：才进入既有正式提交编排。
- 命中本警告时替代普通泛化确认，禁止无意义双弹窗；若之后命中既有超库存确认，原门禁仍保留。

### 10.3 自动化

- 覆盖过期+空白、过期+0、过期+正数、非过期+正数。
- 覆盖返回检查时 I03 调用 0 次、确认后才进入既有编排。
- 覆盖多个过期正数批次的批次数和数量合计。

## 11. R3-09｜真实 I01 导出文件契约

- 新增至少一个从生产 I01 exporter 生成真实 `.xlsx` 的契约测试，不得只修改 fixture 或期待值。
- 重开实际 sheet，断言：当前阶段为中文、库存表头为“总库存”、中文大类仍存在、AutoFilter 仍存在、隐藏稳定身份仍存在。
- 将同一个 workbook 交给当前 I02 Reader 做 round-trip 验证。

## 12. 禁止范围

- 禁止改变 I01 稳定身份、Task 选择、批次/数量算法；I01 只允许本卡批准的 Stage 中文和“总库存”展示契约。
- 禁止改变 I02 blank/0/positive、陈旧校验、Draft Application；禁止改变 I03 Bulk Submission 核心编排和超库存规则。
- 禁止改变 MaxArrivalQty、Inspection/Revision、ProductTask 生命周期、Reminder、商品源导入、Schema、ModelSnapshot、migration、依赖、`.csproj`、`.slnx`。
- migration 必须保持 9；不得进入 Stage 8/9。

## 13. Terra 交付与停止

- 只提交本卡最小 production+test commit；不得 push，不得修改 Sol 验收/交接文档。
- 返回完整 SHA、文件清单、根因/复用点、精确测试命令和计数、未验证项。
- 检查范围、Schema/依赖/项目文件、`git diff --check` 和应用进程；不启动 WPF、不访问生产数据库。
- 提交后停止，等待 Sol 独立验收。

## 14. Sol 独立验收与停点

- 完整审查相对本治理基线的 diff、全部调用者和禁止范围；确认 I01～I03 核心算法未变。
- 新鲜运行 R3 专项、真实 I01 workbook 契约、I01～I04、ProductTask/生命周期/Reminder/商品导入/历史/UIUX 回归、Release 全量与 Release build。
- 运行 EF pending model changes、migration list=9；检查 `.csproj`、`.slnx`、migration、ModelSnapshot、依赖、`git diff --check` 和应用进程。
- 不启动 WPF、不访问生产数据库。
- 通过后只登记 `GUI_RETEST_R3_REPAIR_TECHNICALLY_ACCEPTED / GUI_ACCEPTANCE_FAILED / WAITING_USER_RETEST`，更新 I04 acceptance 与 latest handoff，commit 并普通 push `master:main`。
- 用户本人只重验本轮 9 项；全部 GUI 通过前不关闭 I04/F03、不建立最终收口、不进入 Stage 8/9。
