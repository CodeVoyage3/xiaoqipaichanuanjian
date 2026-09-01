# UIUX-R01 高保真视觉定向

> 本文是 UIUX-R01 的视觉门禁补充，只把 `DESIGN-DIRECTION.md` 与 `DESIGN-TOKENS.md` 映射为可直接判断审美效果的静态样板。未修改生产 XAML/C#，不构成 UIUX-R02 或生产重构授权。

## 视觉稿

| 代表页 | 标准桌面 1600×900 | 紧凑 1024×600 / 125% |
|---|---|---|
| Dashboard / 首页 | [查看](./VISUALS/dashboard-standard.png) | [查看](./VISUALS/dashboard-compact.png) |
| 待排查列表 | [查看](./VISUALS/pending-standard.png) | [查看](./VISUALS/pending-compact.png) |
| 排查详情 | [查看](./VISUALS/detail-standard.png) | [查看](./VISUALS/detail-compact.png) |

## 尺寸映射

| 元素 | 标准桌面 | 1024×600 / 125% 紧凑策略 |
|---|---:|---:|
| 左侧导航 | 208 DIP | 176 DIP |
| Page Header | 72 DIP | 72 DIP；不牺牲标题与动作可读性 |
| 页面内容边距 | 24 DIP | 16 DIP |
| 普通按钮 | 36 DIP | 36 DIP |
| DataGrid Header | 40 DIP | 40 DIP |
| 待排查列表行 | 44 DIP | 44 DIP；商品名称、条码、编码合并为两行可复制身份列，不隐藏 |
| 详情批次行 | 48 DIP | 48 DIP |
| Bottom Action Bar | 56 DIP | 56 DIP；草稿状态与“完成排查”持续可见 |
| 控件 / 面板圆角 | 4–6 / 6–8 DIP | 不变 |

## 方向核对

- Dashboard 以检索、数据新鲜度和优先处理表格组成，不使用 KPI 卡墙、图表或营销式 Hero。
- 待排查页以 DataGrid 为绝对主体；标准稿将条码、商品编码独立成可复制列，紧凑稿将二者并入高权重、可复制的商品身份列。
- 排查详情按“商品身份 → 排查人/日期 → 批次填写 → 正常批次折叠 → 草稿/完成排查”建立单向视觉顺序。
- 色彩、字体、边框、状态、间距和按钮层级直接使用既定 token；无 Bento、Glass、渐变、大阴影、大圆角卡片或新业务状态。

## 门禁

当前仅供用户视觉确认。只有用户明确回复以下原句后，才允许进入生产 UI 重构：

> 新的整体视觉方向认可，可以按该设计系统进入生产 UI 重构。

确认前停止在 UIUX-R01；不得创建 UIUX-R02，不得修改任何生产 XAML/C#。
