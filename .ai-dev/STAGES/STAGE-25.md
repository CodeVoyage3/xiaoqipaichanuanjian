# 2026-09-18 S25-T01 CLOSED / ACCEPTED — REAL_USER_GUI_PASS

真实用户最终回执：「S25-T01 最终 GUI 验收通过。」此前主功能选择/导出/回导GUI已通过，本次最终回执覆盖复选框视觉一致性和分页跳闪两项复验，USER_GUI_PENDING已解除。以下旧GUI_FAIL/REPAIR_REQUIRED/USER_GUI_PENDING均为历史记录，不是当前状态。

当前状态：
- Stage25 = IN_PROGRESS
- S25-T01 = CLOSED / ACCEPTED / REAL_USER_GUI_PASS
- S25-T02 = NOT_STARTED
- S25-T03 = NOT_STARTED（本轮仅登记状态，不定义或启动实施）

已验收生产提交：3aca2107d09302ae71f3dd6b40737b8f8c77e352；对应候选治理基线e2ea185fc5369b85d70e51d77ed61433ddfc5827。
GUI候选：C:\Users\39037\Documents\S25-T01-UI复验\app；DLL SHA256 F31AD1845E428DD794519D573AC78879495DE0B272B03B8FABF6A9236F283042。
沿用既有Sol独立专项19/19 PASS、Production Release build0warning/0error证据，本轮没有重新测试或build；FULL=NOT_RUN / NO_FULL。
本轮只治理收口，无生产代码修改、无merge/push main、无发布、无后续任务启动。原dirty正式工作区保留不动。
下一步：等待用户明确确认；不自动集成main或启动S25-T02/S25-T03。

以下为历史记录。
# 2026-09-18 两项UI返修技术通过 / USER_GUI_PENDING

用户已确认S25-T01主功能GUI通过，本次仅复验复选框视觉一致性与分页跳闪。S25-T01=USER_GUI_PENDING，禁止CLOSED/ACCEPTED；T02 NOT_STARTED；FULL=NOT_RUN / NO_FULL。
全新Terra提交3aca2107d09302ae71f3dd6b40737b8f8c77e352。仅MainWindow.xaml、Stage4ViewModels.cs与直接S25专项测试；SelectedTaskIds/跨页规则/Count/Export/Import/正式UseCase/Excel/Schema/migration/Version均无改动。
提取今日排查原实际CheckBox即时绑定、居中、Focusable规则为InspectionTaskCheckBoxStyle，今日和待排查两布局三处共用；待排查仅选择列模板固定透明背景，不绘制Cell选中蓝块和焦点边框，其他列/全局DataGrid样式不变。
分页移除IsLoading折叠整个待排查DataGrid的触发器；加载提示Hidden保留工具栏空间防止换行高度变化。查询期间旧页集合保持不动，新行全部准备后一次替换Items并发一次属性通知；原HashSet恢复逻辑不动。
Sol独立19/19 PASS、0skip，含真实STA共享样式/透明选中cell/计数/跨页/筛选刷新清选及blockedquery双向保持旧行且一次发布；Production Release build0warning/0error。真实肉眼无跳闪仍需用户两项GUI复验，不替代GUI回执。
候选C:\Users\39037\Documents\S25-T01-UI复验\app；启动验收.cmd及桌面「S25-T01 两项UI复验」。旧两种验收桌面入口也更新至此新候选。
隔离根C:\Users\39037\AppData\Local\Temp\2c3380ef-d0a8-4e71-85c7-00cbd7241306；复用上一候选启动前已验证synthetic fixture备份复制到全新GUID根，DB源/副本SHA匹配。本轮不额外重跑fixture业务测试；正式DB不读不改。
真实WinPS入口启动PID32912、窗口921470、RespondingTrue，命令行显式新根。DLL SHA256 F31AD1845E428DD794519D573AC78879495DE0B272B03B8FABF6A9236F283042。
证据checks/sol-ui-polish.trx、production-build.log、actual-launch.log；Version1.1.3/migration10/migration11未创建（相关源文件无diff）。原dirty正式工作区未变，不main/push/tag/Release。

以下保留历史记录。
# 2026-09-18 主功能GUI通过 / 两项UI返修

用户确认选择、导出、回导主功能GUI通过；S25-T01=USER_GUI_PENDING，仍不得CLOSED/ACCEPTED。只复验复选框视觉一致性和分页跳闪；S25-T02=NOT_STARTED；FULL=NOT_RUN / NO_FULL。
Sol只读：选择列模板仍绘制Cell Background导致选中蓝块；今日排查checkbox是已验收的行内模板，没有现成专用Style。提取共享视觉规则，保持今日行为，待排查仅选择列透明且无明显cell焦点框，其他列不动。
分页IsLoading会让PendingDataGridStyle VisibilityCollapsed；成功结果又Clear/Add重建行集合。仅修加载显示和页结果一次发布，不改选择规则、Count、Export/Import及其他正式合同。
本轮全新Terra /root/s25_t01_ui_polish_new；Sol独立最小专项和Production Release build，完成提供新隔离候选。原dirty正式工作区不触碰，不main/push/Release。

以下保留历史记录。
# 2026-09-18 选择链返修技术通过 / 新候选 USER_GUI_PENDING

用户GUI_FAIL回执仍有效，S25-T01=GUI_FAIL / REPAIR_REQUIRED，不CLOSED/ACCEPTED；新返修候选等待用户复验选择链。其他导出/回导人工验收保持暂停，选择链确认后再继续。Stage25 IN_PROGRESS；T02 NOT_STARTED；FULL=NOT_RUN / NO_FULL。
Terra选择链提交f8ca705419af745391b4550cb78ea43481b2c200；新Terra通知快照补修8f8ab2af89d006319e2f29be77a16d2449131c18。
Sol独立生产diff仅UI/Stage4ViewModels.cs、UI/MainWindow.xaml；测试修改S25T01PendingTasksViewModelTests.cs及测试csproj启用WPF/保留原IO、HTTP隐式using。正式UseCase/Excel/Schema/migration/Version/Installer/Updater未改变。
根因：默认CheckBox提交在BindingGroup中留下UI暂存值；真实模板旧版回归Count Expected1 Actual0 FAIL、新版PASS。行IsSelected现在由唯一HashSet.Contains派生，setter即时写HashSet；计数/命令与清选/分页恢复同源。仅选择列局部cell模板取消整块焦点框并居中CheckBox，其他列/全局样式不改。
Sol初次专项15PASS/1FAIL（筛选清选通知枚举Items时加载重建集合）；补修三个通知点使用行快照，并增加通知中Items.Clear确定性回归。最终独立18/18 PASS，0skip；精确GUI fixture1/1 PASS；Production Release build0warning/0error。没有重跑FULL或其他业务广域测试。
证据目录C:\Users\39037\Documents\S25-T01-选择链返修\checks；初失败sol-selection.trx与最终sol-selection-final.trx均保留，旧模板失败日志保留。
新程序C:\Users\39037\Documents\S25-T01-选择链返修\app，启动入口同目录启动验收.cmd；桌面「S25-T01 选择链返修验收」及原「S25-T01 隔离验收」均指向此新候选。
新隔离数据根C:\Users\39037\AppData\Local\Temp\b729db9c-a5e2-4a9f-a0fc-bfd33b7a8506，synthetic夹具54open+1completed，启动前备份保留，不使用正式DB。
真实WinPS入口启动成功：PID27608、非零窗口7407730、Responding=True，命令行显式新隔离根。技术启动不等于GUI PASS。
DLL SHA256 9B9D09D1084DBE881BEC0024AE01DCB5682363B879915B62E9A22C2E60C077AC；Version1.1.3、migration10、migration11未创建。freshmain588ea7c9417b32074214f268435638c30da995ed；Latestv1.1.3 Release390497014 draftfalse/prereleasefalse。原dirtymain与1modified+4untracked未改变。不main/push/tag/Release。

以下保留过程与失败记录。
# 2026-09-18 S25-T01 GUI_FAIL / REPAIR_REQUIRED

用户停止继续人工验收：跨页勾选丢失、UI勾选与计数/命令不一致、选择单元格蓝框及垂直偏上。其他导出/回导人工验收暂停。
Sol只读根因：两套CheckBox缺少显式UpdateSourceTrigger=PropertyChanged，在WPF BindingGroup中产生未提交UI值；独立探针Default checkbox=True/source=False，显式PropertyChanged source=True。行独立bool仍须改为HashSet.Contains派生。全局DataGridCell焦点触发器产生BorderThickness=2；仅选择列局部覆盖。
新Terra /root/s25_t01_selection_repair_new 已创建；仅选择状态链、选择列视觉、直接专项测试。正式UseCase/Excel/Schema/migration/Version/Installer/Updater禁止修改。
Stage25=IN_PROGRESS；S25-T01=GUI_FAIL / REPAIR_REQUIRED；S25-T02=NOT_STARTED。不得CLOSED/ACCEPTED。
FULL=NOT_RUN / NO_FULL；不main/push/Release。原dirty正式工作区不触碰。

以下为历史记录，不能替代本轮GUI失败回执。
# 2026-09-17 S25-T01 TECHNICAL_PASS / USER_GUI_PENDING

Stage25=IN_PROGRESS；S25-T01=USER_GUI_PENDING；S25-T02=NOT_STARTED。
Sol独立127/127 PASS、Production Release build0warning/0error、forbidden-scope PASS。
最终Terra1f4cf7dd974457bb736358861a17c0e85c01ef16；Version1.1.3、migration10、migration11 NOT_CREATED；FULL=NOT_RUN / NO_FULL。
隔离GUI入口及清单见.ai-dev/ACCEPTANCE/S25-T01.md；真实用户GUI回执前不CLOSED/ACCEPTED。
原dirty工作区不触碰；不main集成/push/tag/Release；T02不得自动启动。

以下旧状态仅为过程记录。

# Stage25｜门店排查工作流与跨版本升级能力优化

状态：IN_PROGRESS。fresh baseline：588ea7c9417b32074214f268435638c30da995ed。
S25-T01：AUTHORIZED / IN_PROGRESS；Scope 已获用户确认。
S25-T02：NOT_STARTED；仅登记，须 T01 用户真实 GUI PASS、CLOSED / ACCEPTED 后再获用户明确授权。
Sol 仅治理与独立验收；每张实施卡使用全新 Terra；原 dirty main 不触碰。
FULL = NOT_RUN / NO_FULL；正式 DB / 安装根 NO_ACCESS。
Version App/Updater=1.1.3；migration=10；migration11=NOT_CREATED。
不自动集成 main、push、tag、Release 或改变已发布资产。

S25-T02：Same-Schema｜跨版本直升策略与兼容基线治理。
同一兼容世代内，Schema、Updater、manifest/signature、持久化格式和必要转换均兼容时，旧版本默认直接升级当前最新版，无需逐版本升级。只有明确 breaking change 才提高最低兼容版本或要求桥接；未验证不等于技术不兼容。最低兼容版本由未来 T02 审计确定，不预先写死。已发布资产不可偷换。本轮不审计或实施 T02。

