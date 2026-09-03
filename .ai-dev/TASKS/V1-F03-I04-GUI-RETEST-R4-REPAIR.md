# V1-F03-I04｜GUI 重验 R4 增量返修

## 1. 授权、基线与状态

- 日期：2026-09-03。
- 开工基线：`master@7c5fe71355f7ef6e58a335a0dba921f42c8537a5`；与 `origin/main` 同步，工作区开工前干净。
- 当前状态：`GUI_ACCEPTANCE_FAILED / NEED_INCREMENTAL_REPAIR / WAITING_USER_RETEST`；I04 与 V1-F03 不得关闭。
- 本卡只处理用户 R4 真实 GUI 重验确认的 4 项 UI 增量；R3 已通过内容只做防回归。
- 本卡提交后必须在当前 Sol 话题内创建全新 GPT-5.6 Terra（medium、平台标准速度）；禁止复用旧 Terra 或另开独立执行话题。
- Sol 只负责治理、完整 diff 审查、自动化测试和技术验收，不直接修改生产代码。
- 禁止 Stage 8/9、Schema、migration、依赖、Reminder、商品源导入、ProductTask/Inspection/Revision 生命周期及 I01～I03 核心算法变化。

## 2. 冻结范围

- Today 六列表、大类筛选与跨筛选 TaskId 选择、全选/取消、500+ 全量加载和虚拟化均不变。
- StageBadge 业务颜色/中文、品牌完整显示、Excel 中文阶段/“总库存”/无合并单元格/AutoFilter/隐藏身份均不变。
- I02 Reader、blank/0/positive、独立确认 Modal、DatePicker 日期门禁、过期正库存警告、超库存/stale/失效、提交后权威刷新均不变。
- ProductTask 生命周期、Reminder、商品源导入只做回归，不实施。

## 3. R4-01｜大类 ComboBox 全区域可点击

- 修正当前局部 ComboBox Template 的 HitTest/ToggleButton 覆盖，使选中值文字区、主体区和箭头区均可展开。
- Hover/Press/Focus 继续表现为一个统一控件；视觉风格不变。
- 不改 ItemsSource、SelectedCategory、默认“全部”、合法大类集合、TaskId、跨筛选选择或全选/取消逻辑。
- 静态/UI 契约须证明交互模板覆盖整个区域，并证明筛选业务逻辑未改。

## 4. R4-02｜Today 商品名称真正垂直居中

- 只修商品名称 DataGridCell/ContentPresenter/TextBlock 的垂直居中，使其与条码、大类、StageBadge、总库存处于同一视觉中线。
- 不用不合理 Padding 补偿；保留 Ellipsis、Tooltip、现有列宽策略、行高、virtualization/recycling 和非整行蓝色选择。
- 增加静态契约锁定商品名称采用与其他业务列一致的垂直居中规则。

## 5. R4-03｜字段标签对齐与 DatePicker 日历图标

- “排查人”标签与 TextBox、“排查日期”标签与 DatePicker 分别垂直居中，二者使用一致间距体系；文字内容和 Validation 红态不变。
- 只修当前 DatePicker 日历图标：清晰可识别、水平/垂直居中、无乱码/旋转/压缩异常，点击后仍打开真正 Calendar。
- 保留真正 WPF DatePicker、默认今天、日历选择、禁止未来日期、CheckDate 门禁和 Validation；禁止重做整套 DatePicker 或退回 TextBox。
- 自动化锁定标签对齐、DatePicker/DisplayDateEnd、真正 `PART_Button`/`PART_Popup` 交互契约。

## 6. R4-04｜确认表新增“当前阶段”

- 确认表固定六列且顺序唯一：条码、商品名称、当前阶段、生产日期、有效日期、本次排查数量。
- “当前阶段”只读，来自当前 Preview 已有 canonical Stage，复用唯一 canonical Stage → 中文业务文案映射；禁止按日期重算或复制 Stage 算法，禁止显示内部英文值。
- 优先复用 StageBadge 体系；必须水平/垂直居中、不挤压商品名称，1024×600 下保留纵向滚动且不产生不可达横向区域。
- “当前阶段”不是“校验状态”；禁止恢复常驻“校验状态”列。
- 顶部异常汇总、异常浅红行、Tooltip 与 I02 stale/失效/身份校验保持不变。
- 自动化精确锁定六列顺序、无“校验状态”、`expired → 过期`、`withdraw → 收仓` 及唯一映射复用。

## 7. 禁止范围

- 禁止修改 I01 核心导出算法、Excel 稳定身份/格式版本、I02 Reader/陈旧/Draft、I03 Bulk Submission、blank/0/positive、超库存、MaxArrivalQty。
- 禁止修改 Inspection/Revision、ProductTask 生命周期、Reminder、商品源导入、Schema、ModelSnapshot、migration、NuGet、`.csproj`、`.slnx`。
- migration 必须保持 9；不得进入 Stage 8/9。

## 8. Terra 交付与停止

- 只提交本卡最小 production+test commit；不得 push，不得修改 Sol acceptance/handoff。
- 返回完整 SHA、文件清单、根因/复用点、精确测试计数和未验证项。
- 检查范围、`git diff --check`、工程/依赖/migration 和应用进程；不启动 WPF、不访问生产数据库。
- 提交后停止，等待 Sol 独立验收。

## 9. Sol 独立验收与停点

- 完整审查本治理基线后的 diff、所有相关调用者和禁止范围，确认大类筛选逻辑及 I01～I03 核心算法未改。
- 新鲜运行 R4 专项、I01～I04、WPF/UIUX、ProductTask/生命周期/Reminder/商品导入/历史回归、Release 全量与 Release build。
- EF pending model changes 必须无漂移，migration list 必须为 9；工程、依赖、Schema、ModelSnapshot、migration 无差异，`git diff --check` 通过，应用进程为 0。
- 不启动 WPF、不访问生产数据库。
- 通过后只登记 `GUI_RETEST_R4_REPAIR_TECHNICALLY_ACCEPTED / GUI_ACCEPTANCE_FAILED / WAITING_USER_RETEST`，更新 I04 acceptance 与 latest handoff，commit 并普通 push `master:main`。
- 用户本人只重验本轮 4 项；通过前不关闭 I04/F03、不建立最终收口、不进入 Stage 8/9。
