# UI/UX 设计 Token 草案

> UIUX-R01 治理文档。以下 token 是现有 WPF 视觉基线的归纳与少量收敛建议，不是对 `App.xaml` 的修改授权。实现时只允许通过已批准的 WPF 资源、Style、Template、Trigger 和 Binding 落地，不新增依赖、ResourceDictionary、业务状态或数据字段。

## 1. Token 使用规则

- `[现有]` 表示已在 `src/StoreExpiryInspector/App.xaml` 或 UI 基线中得到源码/验收证据；`[建议]` 表示可在用户确认后收敛的语义名或使用规则。
- 颜色只表达已有业务或交互语义，必须配合文字、位置、表头或焦点状态；不以颜色单独表达阶段、错误、锁定、保存结果或可执行性。
- 所有尺寸以 WPF DIP 表示；125% DPI 下不通过缩放字体到不可读来换取布局完整。
- Token 不得改变 `InspectionTaskQuery`、导入计划、草稿、提交、历史修订、提醒或备份恢复用例的权威行为。

## 2. 色彩 Token

### 2.1 基础与文字

| Token | 值 | 状态 | 用法 |
|---|---|---|---|
| `Canvas` | `#F5F7FA` | [现有] | 主窗口与页面背景。 |
| `Surface` | `#FFFFFF` | [现有] | 表格、主内容区、普通输入表面。 |
| `SurfaceSubtle` | `#F8FAFC` | [现有] | 次级区、辅助说明背景。 |
| `ElevatedSurface` | `#FFFFFF` | [建议] | 原生模态或确有层级需要的表面，不扩展为卡片墙。 |
| `PrimaryText` | `#172033` | [现有] | 标题、商品名、关键数值和正文。 |
| `SecondaryText` | `#475569` | [现有] | 表头、描述、标签和辅助信息。 |
| `MutedText` | `#64748B` | [建议，独立 token] | 非交互的时间、来源、补充说明和低优先级元数据；不是禁用态，不用于关键结论或替代焦点/可用性反馈。 |
| `DisabledText` | `#7C8799` | [现有] | 禁用控件和低优先级文字，仍需保持可读。 |
| `Border` | `#D6DCE5` | [现有] | 控件、面板和输入边界。 |
| `TableDivider` | `#E7EBF0` | [现有] | DataGrid/明细表行分隔线，比普通边框更轻。 |

### 2.2 交互与语义

| Token | 值 | 状态 | 用法 |
|---|---|---|---|
| `PrimaryAction` | `#1F5FBF` | [现有] | 当前页面唯一主要按钮、链接和关键选中边界。 |
| `PrimaryActionHover` | `#174A96` | [现有] | Primary hover/pressed 的深色反馈。 |
| `FocusRing` | `#2563EB` | [现有] | 键盘焦点 2 DIP 外环或高对比边界。 |
| `HoverSurface` | `#F0F5FC` | [现有] | 普通按钮、导航和行的悬停面。 |
| `SelectedSurface` | `#EAF2FF` | [现有/建议统一名] | DataGrid 行/单元格和选中导航项的低饱和选中面。 |
| `Info` | `#1F5FBF` | [建议] | 信息提示或中性进行中状态；不增加新业务状态。 |
| `Success` | `#16794C` | [现有] | 已保存、正常、备份验证通过、导入成功。 |
| `SuccessSurface` | `#EAF7F0` | [现有] | Success 文本的低对比背景。 |
| `Warning` | `#8A4B08` | [现有] | 库存风险、超库存、需要注意。 |
| `WarningSurface` | `#FFF7E6` | [现有] | Warning 信息承载面。 |
| `Danger` | `#B42318` | [现有] | 恢复、清空、错误或危险确认动作。 |
| `ErrorSurface` | `#FDECEC` | [现有] | 失败、严重错误或错误说明承载面。 |
| `Reconfirm` | `#6B3FA0` | [现有] | 需要重新确认的批次状态。 |
| `ReconfirmSurface` | `#F4ECFB` | [现有] | Reconfirm 行或说明的低饱和背景。 |

### 2.3 对比与使用约束

- `PrimaryText`、`SecondaryText` 与 `Canvas/Surface` 的对比应优先满足普通正文阅读；`MutedText` 只用于仍然可用但不抢注意力的元数据；`DisabledText` 不能用于唯一的关键信息。
- `MutedText` 与 `DisabledText` 必须保持语义分离：Muted 是信息优先级降权，Disabled 是控件不可用；不能用 DisabledText 冒充 MutedText。
- `PrimaryAction`/`Danger`/`Success`/`Warning`/`Reconfirm` 的色块必须同时有中文状态或动作名；阶段标签不能只显示色条。
- `FocusRing` 不得被圆角、裁切、表格选中面或模态边界吞掉；焦点态要与 hover/selected 可区分。
- `ErrorSurface` 和 `WarningSurface` 仅包裹相关消息，不以红/黄铺满整个页面；恢复锁定要有文字说明和退出动作。
- 不新增紫色品牌、霓虹色、渐变、玻璃透明度或大面积阴影；`Reconfirm` 仅作为已有风险语义色。

## 3. 字体 Token

| Token | 值 | 用途 |
|---|---|---|
| `FontFamily.Ui` | `Microsoft YaHei UI, Segoe UI` | 全局 WPF 控件、中文正文和操作文字。 |
| `FontFamily.Numeric` | `Segoe UI, Microsoft YaHei UI` | 条码、商品编码、数量、日期和文件身份等扫描型内容。 |
| `FontSize.PageTitle` | 24 DIP | 页面标题，行高约 32。 |
| `FontSize.SectionTitle` | 18 DIP | 区段标题，行高约 26。 |
| `FontSize.Body` | 14 DIP | 正文、按钮、输入文字，行高约 22。 |
| `FontSize.Table` | 13 DIP | DataGrid 表头、单元格和批次表，行高约 20。 |
| `FontSize.Name` | 14 DIP + Medium/SemiBold | 商品名、文件名等身份主字段。 |
| `FontSize.Label` | 12 DIP | 辅助标签、说明和次级元数据，行高约 18。 |

约束：不引入 Inter、Display、等宽装饰字体或外部字体包；不使用巨型标题、长句斜体或全大写来制造层级；长中文、条码和文件名使用截断 + Tooltip 或可访问的完整文本。

## 4. 间距与尺寸 Token

### 4.1 Spacing scale

| Token | DIP | 典型用途 |
|---|---:|---|
| `Space.1` | 4 | 图标与文字、状态点与状态文本。 |
| `Space.2` | 8 | 行内控件、表格内小组、标签间距。 |
| `Space.3` | 12 | 输入内边距、表头/单元格左右留白。 |
| `Space.4` | 16 | 工具栏组、区段内边距、窄屏页面边距。 |
| `Space.5` | 24 | 桌面页面边距、主要区段间距。 |
| `Space.6` | 32 | 页面标题与主体之间的较大节奏。 |

规则：桌面宽度下内容边距 24 DIP，紧凑宽度下 16 DIP；密集表格内部优先 8/12 DIP；不要用连续 32/48 DIP 空白稀释数据。小于 1024x600 DIP 的布局应先压缩次级间距，而不是隐藏主信息。

### 4.2 控件与布局尺寸

| Token | 值 | 用途 |
|---|---:|---|
| `ControlHeight.Button` | 36 | 普通、Primary、Secondary、Danger 按钮。 |
| `ControlHeight.Input` | 32-36 | TextBox、DatePicker、搜索框内部控件。 |
| `ControlHeight.NavRow` | 48 | 左侧导航行，保持桌面点击和键盘可用。 |
| `TableHeight.Header` | 40 | DataGrid 表头基线。 |
| `TableHeight.Row` | 40-44 | 普通列表密度；待排查表按信息量采用 44。 |
| `TableHeight.DetailRow` | 48-56 | 详情输入、重确认或错误行，给状态和输入留空间。 |
| `PaginationHeight` | 50 | 待排查列表底部分页区。 |
| `DialogWidth.Settings` | 420-460 | 原生提醒设置模态，随内容而定。 |
| `DialogMinHeight.Danger` | 200 | 恢复、库存风险或清空确认需完整显示后果。 |

组件尺寸不能遮挡商品条码、编码、日期、数量或动作名。表格列宽应为内容优先级服务，不能以所有列 Auto 导致操作列离开可视区。

## 5. 圆角、边框、阴影和动效

| Token | 值 | 用法 |
|---|---:|---|
| `Radius.Control` | 4-6 | TextBox、Button、导航项和小标签。 |
| `Radius.Panel` | 6-8 | 主工作面或必要的区段边界。 |
| `Radius.Status` | 4-6 | 状态说明、提示面和阶段标签。 |
| `Border.Quiet` | 1 DIP | 普通控件、面板和表格外框。 |
| `Border.Focus` | 2 DIP | 键盘焦点、明确选中边界。 |
| `Shadow.Modal` | 单层、轻量 | 仅用于原生模态与确有浮层层级的表面。 |
| `Shadow.Default` | none | 页面、表格、统计摘要和普通 Border 默认无阴影。 |
| `Motion.Fast` | 0-100ms | hover、focus、选中反馈。 |
| `Motion.State` | 100-150ms | 状态面切换或轻量布局反馈。 |
| `Motion.Long` | 禁止 | 禁止营销式进入、连续滚动、GSAP、轮播和拖拽叙事。 |

圆角不能把每个模块变成胶囊或卡片；优先用边框、分隔线、对齐和留白层级。没有动效时业务状态仍须完整可判读。

## 6. 业务状态 Token 映射

| 现有业务事实 | 推荐视觉组合 | 说明 |
|---|---|---|
| `expired` | `Danger` + “已过期” | 主风险，不只用红色。 |
| `withdraw` | `Warning` + “临期/下架”对应现有阶段文案 | 以现有 `StageLabels` 为准，不在 UI 改名。 |
| `discount20` / `discount50` | 对应既有低饱和 Warning/Info 层级 + 文字 | 具体颜色按已有阶段资源，不新增计算。 |
| `none` / 正常 | `Success` + “正常” | 正常批次默认折叠但标题和数量可见。 |
| 需要重新确认 | `Reconfirm` + “需要重新确认” + 行内动作 | 只有对应行显示“重新确认”。 |
| 草稿已保存/保存中/失败 | Success/Info/Error + 文本 | 底部动作栏固定显示，不能只放圆点。 |
| 无数据/筛选无结果 | `SurfaceSubtle` + 中性文本 + 下一步 | 两者文案和动作不同。 |
| 业务错误/严重失败锁定 | ErrorSurface 或 Danger + 原因 + 重试/退出 | 不伪装成空数据；锁定由 ViewModel 返回。 |

Dashboard、待排查列表、详情、历史、导入和备份恢复只展示各自 ViewModel/Application 返回的状态；视觉 token 不产生新状态。

## 7. WPF 可实现映射

| Token 层 | WPF 映射建议 | 本轮边界 |
|---|---|---|
| 颜色 | `SolidColorBrush` 语义资源，沿用现有 `CanvasBrush`、`SurfaceBrush` 等命名习惯。 | 不在本文写入或修改 `App.xaml`。 |
| 字体/尺寸 | `Style.Setter` 的 `FontFamily`、`FontSize`、`LineHeight`、`Height`、`Padding`。 | 不修改现有 Style，须经用户确认和实施任务。 |
| 圆角/边框 | 控件 `ControlTemplate` 内 `Border.CornerRadius`、`BorderThickness`、`BorderBrush`。 | 不使用 Web CSS、玻璃滤镜或第三方控件。 |
| 状态 | `DataTrigger`/`MultiDataTrigger` 绑定已有布尔或枚举属性，同时保留文本。 | 不从颜色反推业务状态。 |
| 表格 | `DataGrid` 的 Header/Cell/Row Style、列宽、`SelectionUnit` 和焦点样式。 | 不在 UI 增加排序、分页或业务查询。 |
| 可访问性 | `AutomationProperties.Name`、键盘焦点样式、可读的 Content/ToolTip。 | 不以 Tooltip 替代首屏关键字段。 |
| 布局 | `Grid` 行列、`ScrollViewer` 边界、`MinWidth/MinHeight` 和现有滚轮转发。 | 不引入 CSS Grid、WebView 或 Canvas 动画。 |

## 8. 窄屏、DPI 与键盘验收约束

### 8.1 1024x600 DIP

- Shell、页面标题、搜索/筛选、主表和当前主要动作必须可到达；次级说明可收紧但不能遮挡关键身份。
- 待排查页表格优先保留阶段、商品名、条码、编码、库存、最近有效期和详情动作；必要时通过横向访问保持完整字段，而不是压缩到不可读。
- 详情页优先保留商品身份、库存、待排查表、草稿状态和完成排查；正常批次可折叠。
- 导入页优先保留文件身份、影响摘要、异常行结论和确认/取消；备份页优先保留目标身份、验证状态和恢复后果。

### 8.2 125% DPI

- 所有颜色、文字、焦点环和边界必须随 WPF DIP 正常缩放，不使用固定像素位图作为关键状态。
- 按钮文字、表头、条码、编码、文件名、日期和错误文案不能被截断到失去含义；超长内容使用 Tooltip 或横向滚动。
- 圆角和边框在缩放后仍保持 1/2 DIP 的层级差；不可用极细线表达唯一分隔。

### 8.3 键盘与焦点

- Tab 顺序遵循导航、标题动作、工具栏、表格、分页、底部动作；焦点必须可见且不被滚动裁掉。
- Ctrl+F 聚焦待排查搜索并全选；Enter 执行当前明确动作；Esc 关闭/取消原生对话框且不提交。
- DataGrid 单元格复制、行/单元格选择和详情操作必须能被键盘发现；悬停不能是唯一入口。
- 对话框默认焦点放在取消或安全动作上；Danger 确认需要清楚的动作名和后果文本。

## 9. 变更门禁

这些 token 只有在用户确认以下文字后，才可进入生产 UI 重构：

> 新的整体视觉方向认可，可以按该设计系统进入生产 UI 重构。

确认前不修改 `App.xaml`、`MainWindow.xaml`、任何 `.cs/.csproj`、依赖、migration 或 schema；不创建 UIUX-R02，不进入 Stage 8 实施。确认后的生产任务仍须先对 Dashboard、待排查列表、排查详情做真实 WPF、1024x600、125% DPI、键盘和表格密度验收。
