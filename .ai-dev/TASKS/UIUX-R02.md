# UIUX-R02｜三类代表页生产 UI 落地

## 视觉门禁

UIUX-R01 视觉门禁已由用户放行，原句如下：

> 新的整体视觉方向认可，可以按该设计系统进入生产 UI 重构。

本任务是新的 Task，使用一个全新的 GPT-5.6 Luna（max）；不得复用 UIUX-R01 的 Luna。Sol 负责范围治理、代码审查、自动化测试、Release build、EF/migration/dependency/Git 门禁；真实 WPF GUI 验收由用户本人执行。

## 一、目标与范围

把 UIUX-R01 已批准的设计系统和高保真视觉方向落地到原生 WPF。本任务只实现三类代表页：

1. Dashboard / 首页
2. 待排查任务列表
3. 排查详情
4. 仅为三页服务的必要共享视觉资源、Style、Template 和布局适配

本任务不是全应用 UI 重构，不进入 Stage 8。

## 二、当前基线

- Stage 7 已正式完成并收口，Stage 8 尚未开始。
- UIUX-R01 已通过用户视觉门禁。
- 视觉方向以 `.ai-dev/UI-REFRESH/UI-AUDIT.md`、`DESIGN-DIRECTION.md`、`DESIGN-TOKENS.md`、`REPRESENTATIVE-PAGES.md`、`TASTE-REVIEW.md`、`VISUAL-DIRECTION.md` 及 `.ai-dev/UI-REFRESH/VISUALS/` 六张 PNG 为准。
- 开工前必须读取上述文件及当前生产 `App.xaml`、`MainWindow.xaml`、三页相关 ViewModel/测试；不得只依据任务卡自行想象。

## 三、已批准视觉尺寸

### 标准桌面

- 左侧导航：208 DIP
- Page Header：72 DIP
- 页面内容边距：24 DIP
- Button：36 DIP
- DataGrid Header：40 DIP
- 待排查列表行：44 DIP
- 详情批次行：48 DIP
- Bottom Action Bar：56 DIP
- 控件 Radius：4–6 DIP；Panel Radius：6–8 DIP

### 1024×600 / 125% 紧凑模式

- 左侧导航：176 DIP
- 页面内容边距：16 DIP
- Header / Button / DataGrid Header / 主要行高不缩小
- 通过减少次级间距、合并商品身份信息等方式适配；不得缩小关键字号、按钮、输入框硬塞内容。

## 四、全局实施原则

### 4.1 原生 WPF

允许使用现有 `Grid`、`Border`、`DataGrid`、`ItemsControl`、`Expander`、`ScrollViewer`、`TextBox`、`Button`、`Style`、`ControlTemplate`、`DataTrigger`、`MultiDataTrigger`、`Binding`、`AutomationProperties`。

不得引入 WebView、CSS、JS、GSAP、Framer Motion、新 UI 框架、新第三方设计系统或仅为视觉实现新增 NuGet 依赖。

### 4.2 业务权威不变

UI 不得重写或复制 Stage 3 生命周期、Stage 4 Draft / Reconfirm / Inventory Adjustment / Submission、Stage 5 History / Revision、Stage 6 Reminder / Tray、Stage 7 Backup / Restore。UI 只绑定现有 ViewModel / Application 返回的事实。

### 4.3 视觉方向不得走样

禁止 SaaS KPI 卡墙、Bento、Glassmorphism、大渐变、大阴影、发光、大圆角卡片墙、Hero 标题、大面积空白、装饰性图表、把表格改成卡片列表、hover 才显示关键字段、只靠颜色表达状态。

## 五、共享视觉资源

只允许对现有 WPF 资源做三页所需的最小整理和收敛：补齐批准的语义 Brush，统一 FontSize / Spacing / Radius，统一三页所需 Button / Input / DataGrid / Status / Navigation / Page Header / Toolbar / Section / Bottom Action Bar 的 Style、Template、Focus / Hover / Selected / Disabled。尽量复用现有资源，不大规模重写资源，不污染其他页面。

不得新增业务状态、schema/migration、dependency、ViewModel 语义或客户端计算。

## 六、Page A｜Dashboard / 首页

### 6.1 目标

第一眼看到数据是否正常/新鲜与当前待排查工作量；第二眼看到优先处理什么；第三眼进入详情/全部任务。

### 6.2 必须实现

- Page Header 标题为“效期排查”，短描述保持低权重，“导入数据”为页面唯一 Primary。
- 搜索继续支持既有商品名称、商品条码、商品编码，不改搜索逻辑。
- Freshness / Summary 使用单一紧凑信息带，不做多个独立 KPI 卡；真实表达数据状态、待排查、过期、收仓、2折、5折、最近导入时间。
- 优先处理以 DataGrid 为首页主工作面，至少保留阶段、商品名称、商品条码、商品编码、库存、最近有效期、排查入口；不得加入趋势图或假数据。

## 七、Page B｜待排查任务列表

### 7.1 目标

表格必须成为绝对视觉主体。

### 7.2 标准桌面

优先独立展示阶段、商品名称、商品条码、商品编码、批次数、库存、最近有效期、排查。条码 / 商品编码必须可见、可复制，不依赖 hover 或 Tooltip 才能查看。

### 7.3 紧凑模式

1024×600 / 125% 下可按视觉稿把商品名称、商品条码、商品编码合并成一个高权重两行商品身份列：第一行商品名称；第二行 `条码 xxxxx   编码 xxxxx`。完整值仍需可复制，不得简单隐藏条码 / 编码列。

### 7.4 Toolbar

顺序为搜索、阶段筛选、清空筛选、Refresh（低权重）。不得新增客户端排序，分页语义保持现有固定页大小。

### 7.5 状态

必须保留加载、真实无任务、筛选无结果、读取失败、有结果五种可区分状态。

## 八、Page C｜排查详情

### 8.1 目标

形成 `商品身份 → 排查信息 → 批次处理 → 正常批次 → 草稿/提交` 的单向视觉顺序。

### 8.2 商品身份

商品名称、商品条码、商品编码、当前库存、待排查批次数必须高可见；条码 / 编码保持可复制。

### 8.3 排查信息

展示排查人、排查日期、草稿保存状态，辅助信息不得抢商品身份层级。

### 8.4 批次表

批次表为主工作区，保持阶段、生产日期、有效期、累计到货、上次排查、本次排查数量、状态 / 操作。输入继续保持空白、0、正数三态业务语义，不在 UI 重新解释。

### 8.5 Reconfirm

需要重新确认时使用 Reconfirm 视觉，同时显示中文文字；行内动作只影响对应批次。

### 8.6 正常批次

默认折叠，标题必须显示正常批次数量和明确“正常”语义。

### 8.7 Bottom Action Bar

高度 56 DIP，稳定表达草稿保存状态、清空草稿、修正库存、完成排查。完成排查为唯一 Primary，修正库存为 Secondary，清空草稿为 Tertiary / 更低权重。

## 九、状态与颜色

继续采用 `文字 + 语义色` 双表达，覆盖过期、收仓、2折、5折、正常、已填写、待填写、需要重新确认、草稿已保存、保存失败、Error、Warning。禁止只有色点 / 色块没有状态文字。

## 十、键盘 / DPI / 窄屏

真实 WPF 验收覆盖 1024×600、Windows 125% 和键盘：主标题可见、主表可见、主动作可达、条码 / 编码不消失、表格可操作、Bottom Action Bar 不裁切；文本、按钮、输入、表头、焦点环可读；至少验证 Tab、Enter、Esc、Ctrl+F、DataGrid 焦点、输入焦点、Dialog 安全默认焦点。

## 十一、不得纳入本任务的页面

不得重构 Excel 导入、排查历史、Revision、Reminder 设置、数据备份与恢复、其他 Dialog、托盘视觉或 Stage 8。这些等待代表页真实落地验收后另行批准。

## 十二、自动化与技术验收

Sol 独立执行 UIUX-R02 新增 / 修改相关测试、Stage 4 UI 自动化、Stage 3–7 权威回归和 Release 全量；Release 0 warning / 0 error；EF 无漂移；migration = 8；dependency 无新增；`git diff --check`；工作区状态明确。不得删除或弱化现有测试迁就布局。

## 十三、Sol 独立代码审查

重点核对：不得把业务逻辑塞入 XAML code-behind；不得改变 ViewModel 行为、客户端排序、分页、阶段文案/含义；不得隐藏条码/编码；不得破坏 1024×600；不得让共享 Style 污染其他页面；不得未经批准自由发挥；不得越界修改其他页面。

## 十四、用户 GUI 验收

技术验收通过后由用户本人运行真实 WPF，至少检查 Dashboard 的搜索/Summary/优先处理层级和唯一 Primary；待排查列表的表格主体、标准/紧凑身份列、复制、筛选/空/错误/分页；排查详情的身份、批次表、输入、Reconfirm、正常批次折叠、草稿状态、唯一 Primary 和 Bottom Action Bar；环境覆盖 1600×900、1024×600、Windows 125%、Keyboard。

## 十五、完成门禁

UIUX-R02 只有在 Luna 实施、Sol 独立技术验收、Release/build/regression、EF 无漂移、migration = 8、dependency 无变化、用户真实 GUI 验收、三页无明显视觉走样、生产功能结果无变化、正式数据库未被测试污染且工作区 clean 全部满足后才可完成。

完成后正式归档 UIUX-R02，然后停止；不得自动创建 UIUX-R03，不得进入 Stage 8，等待用户单独批准下一步。
