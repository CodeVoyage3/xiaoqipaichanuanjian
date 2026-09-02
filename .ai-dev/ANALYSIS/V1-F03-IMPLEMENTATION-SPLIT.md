# V1-F03｜生产实施拆分提案

- 日期：2026-09-02
- 状态：`PROPOSED_WAITING_I01_APPROVAL`
- 当前基线：`master@8fe9615f6781c9c76d27d95b0829a2d2edc31c21`，与 `origin/main` 同步，工作区分析前干净
- 当前动作：只做代码/Schema 审计和任务拆分；不修改生产代码，不创建 migration，不创建实现 Terra
- 执行治理：每张获批实现卡均在本话题创建一个全新的 GPT-5.6 Terra（medium，平台标准速度）；Terra 只实现当卡并停止，Sol 独立审查、复跑和验收

## 结论

V1-F03 最小安全拆分为四张顺序卡：

1. `V1-F03-I01｜今日排查计划查询与 Excel 导出`
2. `V1-F03-I02｜排查结果读取、陈旧校验与 Draft 应用`
3. `V1-F03-I03｜多任务正式提交编排`
4. `V1-F03-I04｜WPF 双入口与端到端收口`

当前 Schema 足够，不需要持久化“导出批次”或“回导暂存”，也不需要第 10 条 migration：

- 导出稳定身份可由现有 `ProductTask / ProductTaskItem / Product / Batch` 主键和 `AttentionVersion` 表达；任务集合与库存/到货快照随文件携带，回导时以当前数据库重新验证。
- 回导解析后的“待确认结果”只存在于当前 WPF 会话内；关闭或取消即丢弃，未确认前零数据库写入。
- 用户确认后只原子 patch 既有 `InspectionDraft / InspectionDraftItem`；空白保持 `null`，`0` 保持正式零值，正数保持原值。
- 重复回导同一内容由现有 `InspectionDraftUseCase.SaveDraft` 的 no-change 行为实现幂等；正式提交由现有 `InspectionSubmissionUseCase` 的事务与幂等语义负责。

若实施发现必须跨应用重启恢复“尚未确认的回导预览”、持久化导出清单或单独记录回导批次，现有 Schema 不再足够；当卡必须停止并提交 Schema 决策报告，未经新批准不得新增 migration、修改 ModelSnapshot 或借用 Import 表保存不相干语义。

## 本轮接任核对

- Git：`master@8fe9615f6781c9c76d27d95b0829a2d2edc31c21`，分析开始时与 `origin/main` ahead/behind=`0/0`。
- Release restore/build：新鲜通过，build 0 warning / 0 error；restore 使用 `NuGetAudit=false`，不构成在线漏洞审计。
- Release 全量测试：新鲜复跑 764/764，通过 764、失败 0、跳过 0。
- EF：`has-pending-model-changes` 无漂移；`--no-connect` 列出 9 条 migration，最后一条仍为 `20260901155124_AddPolicyAndBaselineFoundation`。
- 运行边界：`StoreExpiryInspector` 进程 0；未启动 WPF、未访问当前运行数据库，测试只使用隔离临时数据库。

## 现有权威与缺口

|职责|当前权威|V1-F03 最小复用|当前缺口|
|---|---|---|---|
|今日合法待排查任务|`InspectionTaskQuery`、open `ProductTask`、Managed/policy/scope baseline 生命周期门禁|按选定 Task 输出其全部当前 TaskItem|现有查询无导出 DTO，也没有全选/部分选择导出入口|
|批次稳定身份|`TaskId + TaskItemId + ProductId + BatchId`、`AttentionVersion`|作为系统列写入导出文件，回导逐项重读校验|现有商品源 Excel Reader 不适用于排查结果文件|
|草稿数量与人员日期|`InspectionDraftUseCase.SaveDraft`|确认后按 Task 分组，在一个外层事务中 patch 草稿|缺少结果文件解析、整批预检和整批应用编排|
|陈旧保护|TaskItem/Batch `AttentionVersion`、`RequiresReconfirmation`、Task 状态、Batch 状态、库存/到货事实|导出快照与当前事实不一致即拒绝该项自动应用；确认瞬间再次校验|不能仅凭旧 DTO 或文件显示字段写入|
|正式提交|`InspectionSubmissionUseCase`、`BatchCheckedZeroLifecycleUseCase`|外层事务内逐 Task 调用现有 Submit；任何失败整批回滚|缺少多 Task 原子编排和集中超库存确认结果|
|Excel 技术栈|已安装 `DocumentFormat.OpenXml 3.5.1`|新增独立的计划 writer 与结果 reader|不得复用商品源 `ExcelTemplateReader` 的业务表头/DTO|
|WPF|现有 Shell、待排查列表、详情、商品源数据导入页|新增独立“今日排查”业务页；商品源导入保持原入口|缺少选择导出、回导预览、确认和批量提交交互|

## Excel V1 契约

### 可见业务列

顺序固定为：序号、商品编码、条码、商品名称、大类、生产日期、有效日期、当前阶段、当前批次累计到货、历史累计到货最大值、商品当前库存、本次排查数量。

- 一批次一行，同商品多批次展开。
- “本次排查数量”是唯一允许门店填写的业务列：空白为未完成，`0` 为确认无库存，正整数为现场数量。
- 文件设置打印标题行、筛选、冻结首行、横向打印、适配单页宽度和合理列宽；不修改源商品 Excel。

### 系统身份列

文件同时携带格式版本、`TaskId`、`TaskItemId`、`ProductId`、`BatchId`、`AttentionVersion`、Task 更新时间、TaskItem 总数、Batch 当前状态及导出时数量快照。系统列可隐藏以减少误操作，但不得省略；回导不以商品名、条码或行号定位。

回导必须逐行验证系统身份列和可见身份/快照列。行顺序变化不影响匹配；任一身份字段被修改、系统列缺失或格式版本不支持时，该行不得应用。

## 文件边界的确定处理

|输入情况|处理|
|---|---|
|用户调整行顺序|允许；按稳定系统身份匹配|
|删除部分行|允许；缺失行不写 Draft、不视为 0、不视为完成|
|只填写部分行|允许进入待确认；空白保持 `null`，相关 Task 不具备正式提交 readiness|
|重复行|同一 `TaskItemId / BatchId` 的所有重复行均标错，不采用“最后一行覆盖”|
|非数字、小数、负数、超出 `Int32`|该行标错，不写 Draft|
|身份或快照字段被修改|该行标错或陈旧，不按显示字段猜测匹配|
|旧格式文件|文件级拒绝并提示重新导出|
|同文件重复回导|相同当前 Draft 值返回 no-change；不新增平行回导记录|
|提交后再次回导|Task 已完成，标为失效，不创建第二次 Inspection|
|空白数据行|忽略；有身份但数量空白的行保留为未完成|

## 陈旧与冲突门禁

回导预检和用户确认落 Draft 前必须各重读一次数据库。至少出现以下任一情况时，不得自动应用该 Task 的导入结果：

- Task 不存在、不再 open、已生成正式 Inspection、Task 的 Product 归属变化；
- 当前 TaskItem 集合数量或 Task 更新时间与导出时不一致（包括新 TaskItem 合并）；
- TaskItem / Batch / Product 身份不匹配；
- TaskItem 与 Batch 的当前 `AttentionVersion` 不一致，或不等于导出版本；
- Stage、Batch tracking status、当前/最大到货量、商品有效库存或必要显示身份与导出快照不一致；
- Batch 已正式 0 件停止、Product 已库存归零或源数据导入后事实发生变化；
- 当前有效 Draft 已系统失效。

本功能选择最保守的既有契约：陈旧 Task 整体拒绝自动应用，提示用户重新导出或回到详情处理；不在 Excel 回导中替用户自动 Reconfirm，也不静默把旧值绑定到新版本。

## 拟议任务

### I01｜今日排查计划查询与 Excel 导出

目标：从合法当前 open Task 生成稳定、可打印、可部分选择的“一批次一行”计划文件。

允许：

- 新增 Application 只读导出 DTO/查询；请求接受唯一正数 TaskId 集合，拒绝重复、失效、非 Managed、无匹配完成 ScopeBaseline、空 TaskItem 或非法 Batch；大类中文显示扩展现有 `ProductCategoryScopes` 单一映射，不复制第二张映射表。
- 使用已安装 Open XML 依赖新增独立 writer；写入上述可见列、系统列、格式版本及打印设置。
- 覆盖全量/部分 Task、多批次、确定性顺序、无任务、非法选择、文件占用/路径错误及生成文件可重新读取的测试。

排除：WPF、结果读取、Draft、Inspection、schema/migration、商品源导入、Reminder。

停止点：Sol 独立验收导出契约并 push；未经 I02 批准不创建下一 Terra。

### I02｜排查结果读取、陈旧校验与 Draft 应用

目标：独立读取 V1-F03 文件，形成零写入待确认结果；用户确认后只把有效结果原子 patch 到既有 Draft。

允许：

- 新增独立 result reader/parser，严格保留空白/0/正整数语义并实现上表全部文件边界。
- 新增 Application 预检结果：商品数、批次数、各行数量、空白、格式错误、陈旧/失效原因和可应用 Task。
- 确认请求必须含排查人和不晚于 BusinessDate 的排查日期；在一个外层 SQLite 事务中再次验证全部目标，再按 Task 调用现有 `InspectionDraftUseCase.SaveDraft`。
- 任一确认瞬间冲突使本次确认零写入并返回刷新后的冲突；不部分写入、不自动 Reconfirm、不创建 Inspection。

排除：WPF、正式提交、ImportRecord/ImportWorkbook/ImportIssue 复用、schema/migration、新回导持久化实体。

停止点：Sol 独立验收 parse/preview/apply 与零写入/回滚；未经 I03 批准不创建下一 Terra。

### I03｜多任务正式提交编排

目标：对本次已确认且完整的 Draft 实现集中、原子正式提交，业务结果与逐商品现有 Submit 一致。

允许：

- 新增薄 Application orchestrator；以确定性 TaskId 顺序在同一外层事务内调用现有 `InspectionSubmissionUseCase.Submit`，不得复制 Inspection/生命周期逻辑。
- 提交前重读本次 Task 集合；任一 Task 缺项、陈旧、需 Reconfirm、失效或异常时整批零写入。
- 首次发现超库存时整批回滚并集中返回每个 Task 的当前库存/排查合计；只有用户精确确认这些当前值后才整批重试。
- 全部已提交的相同请求可幂等返回；open/completed 混合等非完整重放视为冲突，不能继续提交剩余项。
- 覆盖多商品、多批次、0 件停止、正数继续跟踪、超库存二次确认、第二项失败导致第一项回滚、重复点击及 Revision/历史可查询性。

排除：WPF、平行批量提交数据库逻辑、schema/migration、修改现有单 Task 提交语义。

停止点：Sol 独立验收原子编排；未经 I04 批准不创建下一 Terra。

### I04｜WPF 双入口与端到端收口

目标：接通“选择导出 → Excel 回填 → 独立回导 → 本次确认 → 正式提交”的门店桌面闭环。

允许：

- 保留现有“数据导入”作为商品源 Excel 入口；新增明确独立的“今日排查”导航/页面，不混用商品源 ImportViewModel。
- 今日任务表格支持全选/部分选择并用 SaveFileDialog 导出；回导使用独立 OpenFileDialog。
- 待确认界面展示商品数、批次数、每批数量、空白、陈旧/失效和错误原因；排查人必填，日期必填且默认今天、不得晚于今天。
- 有空白/冲突的 Task 可保存有效草稿，但不得正式提交；完整且当前的 Task 才能进入集中提交。提交后刷新 Dashboard、任务列表和历史。
- 保持 1024×600、Windows 125%、键盘可达、表格优先、文字+颜色状态表达；由用户本人执行真实 WPF GUI 验收。

排除：商品源导入改造、Reminder 新规则、Stage 8/9、UI 全局重构、schema/migration/依赖。

停止点：Sol 技术门禁通过后等待用户 GUI 验收；通过后仅收口 V1-F03，不自动进入任何后续功能。

## 全系列技术门禁

- 每卡定向测试及受影响既有回归；最终 Release 全量测试和 Release build 0 warning / 0 error。
- EF `has-pending-model-changes` 无漂移；migration 数量保持 9；无 ModelSnapshot、依赖、`.csproj`、`.slnx` 或 target framework 变化。
- `git diff --check`；不启动或访问正式运行数据库，不改旧旁置数据。
- Excluded、Unresolved、无合法完成 ScopeBaseline 的商品零导出、零回导生命周期影响。
- 商品源 Excel 原表只读且零修改；V1-F02 Reminder 规则不变。

## 当前审批停点

当前只形成拆分提案。下一唯一可批准任务为 `V1-F03-I01｜今日排查计划查询与 Excel 导出`。

在产品经理明确批准 I01 前：不创建 Terra、不创建正式 I01 实施卡、不修改生产代码、不新增 migration、不进入 I02～I04、Stage 8 或 Stage 9。
