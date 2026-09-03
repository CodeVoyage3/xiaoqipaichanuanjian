# V1-UI-01 GUI R1｜品牌区紧凑收口与筛选下拉中文显示

## 状态与基线

- 批准日期：2026-09-03。
- GitHub `main` 唯一返修基线：`8202afb358631b63de820952dc0625b605c54a6b`。
- 用户本人真实 WPF GUI 结论：`GUI_ACCEPTANCE_FAILED / REPAIR_REQUIRED`。
- 本卡是尚未关闭的 V1-UI-01 第一次最小 GUI 返修；不创建 V1-UI-02，不重开 V1-F03/I04，不启动 Stage 8/9。

## 协作治理

- Sol 只负责记录失败事实、定义 Task/Acceptance、审查完整 diff和独立技术复验，不修改生产代码。
- 生产代码由本 Sol 话题内全新 GPT-5.6 Terra（reasoning medium、标准速度）实施；不得复用 V1-UI-01 前三名 Terra。
- Terra 完成最小实现、测试并提交后立即停止；不得 push，不得修改治理文档，不得自行写 GUI `PASSED`。

## 唯一返修范围

### 1. 左上品牌区紧凑收口

- 保留盾牌图标；“门店效期排查软件”文字继续不存在。
- 折叠按钮仍位于盾牌右侧，但不得锚定整个侧边栏最右缘。
- 顶部形成紧凑的 `[盾牌] [折叠按钮]` 控制组，使用正常 UI 间距；剩余侧边栏宽度仍供下方导航使用。
- 不修改侧边栏宽度、折叠命令、Tooltip、Automation、展开/收起行为，不重构 Shell 或导航布局。

### 2. 阶段与大类 ComboBox 中文显示

- 阶段默认及展开项必须显示：全部阶段、过期、收仓、2折、5折。
- 大类默认显示“全部大类”，真实项显示现有 `CategoryName`。
- 不得出现 `StageFilterOption`、`CategoryFilterOption`、canonical 英文值、类型名或调试文本。
- 优先复用现有 `Label` / `CategoryName` presentation 属性，通过明确 `ItemTemplate`、Display binding 或等效最小 WPF 绑定修复。
- `CanonicalStage`、`CategoryName` 筛选 Value/identity、真实数据来源、去重、搜索+阶段+大类交集、清空、计数、空态和分页全部保持不变。
- 不以重写业务对象 `ToString()` 为首选，不新增第二套 Stage 中文映射，不硬编码真实大类。

## 已通过内容冻结

- 今日排查位于待排查任务下面；蓝色整体降噪；Search/Refresh/分页中性视觉；阶段与大类 ComboBox 产品方案；组合筛选；清空、计数、分页；StageBadge；Primary/Danger 语义全部冻结。
- I01～I04、Excel 导入/导出/回导、blank/0/positive、ProductTask、Stage 算法、Reminder、History/Revision、Backup/Restore、Tray、单实例、开机自启动及“应季搭配/赠品小样”规则全部冻结。
- 禁止 Schema、ModelSnapshot、migration、NuGet dependency、`.csproj`、`.slnx`、Stage 8/9、在线升级变化。

## 最小自动化

1. 品牌文字仍不存在，盾牌仍存在。
2. 盾牌与折叠按钮位于同一紧凑顶部组，布局不含把两者推向两端的大跨度 `*` 列；按钮在盾牌右侧。
3. 阶段 Option display 为全部阶段/过期/收仓/2折/5折，筛选 Value 仍为原 canonical Stage。
4. 大类默认 display 为“全部大类”，真实 display 来自 `CategoryName`，Value 不变。
5. XAML 明确绑定 Option display，选中项和展开项不得回退到对象 `ToString()`。
6. 原搜索+阶段+大类组合筛选回归继续通过。

## Terra 停止门禁

- 只提交完成上述两项所需的最小 UI/presentation/test diff。
- 若必须触碰查询、业务算法、Schema、migration、依赖或项目文件，立即停止报告。
- 报告 commit SHA、文件、根因与修复、专项测试、`git diff --check`、是否启动 WPF/访问数据库；不得 push。

## Sol 独立技术门禁

- 审查 `8202afb358631b63de820952dc0625b605c54a6b..HEAD` 完整 diff，确认只含本轮 UI/presentation/test 差异。
- 运行 R1 专项、V1-UI-01 UI/ViewModel、三条件组合筛选、Release 全量、Release build、EF drift、migration list、冻结文件与 `git diff --check`。
- 预期：全量 0 failure；build 0 warning/0 error；EF 无漂移；migration=9，最后一条 `20260901155124_AddPolicyAndBaselineFoundation`；无 Schema/依赖/项目文件变化。
- 技术通过后状态只能为 `TECHNICALLY_ACCEPTED / WAITING_USER_GUI_RETEST`。

## 用户只重验两项

1. 盾牌与折叠按钮是否紧凑自然，不再有明显空洞感。
2. 阶段、大类当前值和展开项是否显示中文业务文案，不再显示 Option 类型名。

用户通过前不得 `CLOSED`，不得启动 Stage 8/9。
