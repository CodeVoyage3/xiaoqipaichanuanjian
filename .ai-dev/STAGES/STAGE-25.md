# 2026-09-18 Stage25 最终治理收口｜CLOSED / ACCEPTED

真实用户最终回执：「S25-T03 最终 GUI 验收通过。」
用户确认 R2 首页优先处理已正常显示；本次问题仅为验收夹具状态大小写错误，不属于生产 BUG；生产代码无需修改。
S25-T03 = CLOSED / ACCEPTED / REAL_USER_GUI_PASS；USER_GUI_PENDING 已解除。
Stage25 = CLOSED / ACCEPTED；S25-T01 CLOSED / ACCEPTED / REAL_USER_GUI_PASS，S25-T02 CLOSED / ACCEPTED 全部保留，Stage25 三张计划 Task 完成。
最终 GUI 验收对象为 R2：生产代码沿用 R1 fdbd30f471996bcec9415bf4a4812d791bb637bf，R2 全部程序文件与 R1 哈希一致，仅模拟 ImportRecord 状态 succeeded→Succeeded；未发生生产修复。
保留已通过 Sol 专项23/23、UI小修1/1、模拟夹具1/1及 Production Release build0warning/0error历史证据，R2既有只读首页Query/VM对照与实际启动证据保留；本轮没有重新测试或build。
Compatibility Generation G1-m10-protocol2、Setup及Online最低兼容v1.1.0保持，previousRelease只负责source上限；旧Updater sidecar/WAL风险说明不撤销。99.25.2保持PRIVATE_RETAINED_FOR_EVIDENCE，不公开或作为正式版本。
本轮仅更新治理文件，未修改生产代码、测试源码或模拟数据；FULL=NOT_RUN / NO_FULL；未merge/push main、tag、Version/Release；未启动或授权下一Stage。
原dirty正式工作区与旧Stage25 clean continuation保持；最终Stage25 HEAD为包含本收口登记的本地治理提交，提交后由Sol读取汇报。
当前停止点：Stage25验收完成，等待用户后续明确指令；完成Stage不自动集成main或Release。
以下 USER_GUI_PENDING / NOT_ACCEPTED 等为历史记录，不代表当前状态。

以下为历史记录。
# 2026-09-18 S25-T03 首页只读诊断｜仅验收数据 R2

S25-T03 保持 TECHNICAL_PASS / USER_GUI_PENDING / NOT_ACCEPTED，不 CLOSED / ACCEPTED。
分类 A：GUI模拟夹具 ImportRecord 状态不合正式合同；不是此次现象对应的生产逻辑BUG。
首页HasNoImportData=成功加载但无LastSuccessfulImportAtUtc；DashboardDataGridStyle据此隐藏优先处理。Query仅取Status==ImportStatuses.Succeeded、未撤销、有ConfirmedAtUtc的导入。正式常量为Succeeded，正式ConfirmedImportExecutor使用该常量；夹具S25T01GuiFixtureTests写succeeded（小写），所以实际存在记录但不被认作成功导入。不是没有生成ImportRecord。
只修新模拟DB现有id1的status：succeeded→Succeeded；不新增重复导入、不改生产或测试源码、不重建生产程序。以后新模拟数据应复用R2 corrected fixture-after.db，不能盲用原夹具。
Sol外部只读探针对候选现有编译Query/VM对照：修正前HasNoImportData=true、优先数据5条但隐藏；修正后及实际启动后HasNoImportData=false、优先数据5条且全部显示条件满足；待排查54、收仓18、5折36保持。DB完整性ok、FK0。
探针仅checks内独立工具，引用冻结程序集并以Sqlite ReadOnly打开模拟DB；无生产ProjectReference或生产build。初引用相对路径错误日志保留，最终对照PASS。
R2候选路径及新数据根见S25-T03.json的fixtureRevisionR2。全部程序文件与R1逐文件SHA256一致；实际PID40540、窗口11473462、Responding=true，命令行显式全新模拟根。技术启动不代替真实GUI确认。
候选checks保留修正前后DB、fixture-correction.json、dashboard-before/after/after-launch.json、app-hash-equivalence.log及actual-launch.json。
FULL=NOT_RUN / NO_FULL；原23/23、UI1/1及R1生产build证据保留，不重复业务专项或生产build。T01/T02/导航/规则/Schema/migration/Version/Release保持；无main merge/push；原dirty与旧Stage25链不动。

以下为历史记录。
# 2026-09-18 S25-T03 首页局部小修 R1｜USER_GUI_PENDING

S25-T03 = TECHNICAL_PASS / USER_GUI_PENDING / NOT_ACCEPTED；未 CLOSED / ACCEPTED，用户仅复验首页此处。
全新 Terra 提交 fdbd30f471996bcec9415bf4a4812d791bb637bf：仅 MainWindow.xaml 顶部 Dashboard.TomorrowWorkText 行删除、Padding16,12→16,10；Sol 精确重建预期文本比对 PASS，其他源内容全部相同，下方 TomorrowPlanText 明日提示保持。
Sol 最小界面专项1/1 PASS、0skip（既有XAML层级/Command/style/导航顺序用例，no-build）；此前23/23直接专项保留，不重复执行。Production Release build0warning/0error，测试模式false、Version1.1.3.0；FULL = NOT_RUN / NO_FULL。
新候选：C:\Users\39037\.codex\visualizations\2026\09\18\01a0b319-cbb0-73d1-88d9-0d513f92adaa\S25-T03-GUI-R1；启动验收.cmd、GUI复验清单.md。冻结上一候选启动前模拟DB复制至全新GUID根，源副本SHA匹配，不重跑夹具、不使用活跃旧DB或正式DB。
新模拟数据根 D:\DevCache\Temp\df276f1b-36a6-4124-9e8b-f7d9502ddffc；实际WinPS入口启动PID36868、窗口463920、Responding=true，命令行显式新根。技术启动不代替用户 GUI PASS。
新DLL SHA256：413627E812649D56FFE5259C31F11DD97B5FF89CB082F844BA81D5ECA0C2DE62。
技术证据在候选checks：sol-exact-diff.log、sol-ui-r1.trx、production-build.log、fixture-copy.json、actual-launch.json；完整回执 .ai-dev/ACCEPTANCE/S25-T03.json 的 uiRevisionR1。
导航、首页进入今日行为、次级待排查、今日/明日计算、T01/T02、Schema/migration、Version及发布链均不改；原dirty和旧Stage25链保持。不main/push/tag/Release；等待用户单处复验回执。

以下为历史记录。
# 2026-09-18 S25-T03 技术通过｜USER_GUI_PENDING

S25-T03 = TECHNICAL_PASS / USER_GUI_PENDING / NOT_ACCEPTED；真实用户 GUI PASS 前不得 CLOSED / ACCEPTED。
Stage25 = IN_PROGRESS；S25-T01 CLOSED / ACCEPTED / REAL_USER_GUI_PASS、S25-T02 CLOSED / ACCEPTED 均保持。
全新 Terra 生产提交：3ac6723dd22c950056d41c95299f0374bd3b40c7；冻结范围治理提交 c64f828；基于完整 continuation 626a114914fba6265a1e847f471f44b1e24dc716。
Sol 独立审查生产仅 MainWindow.xaml 与 Stage4ViewModels.cs，另直接 S25T03TodayEntryTests；导航原按钮块仅排序、普通 NavigateTodayInspectionCommand 不变；首页状态下/优先处理前紧凑主入口、明日弱辅助；原查看全部待排查保持 LinkButtonStyle。无新数据库查询、Badge或折叠代码；T01/T02/Schema/migration/Version/Installer/Updater/Builder无修改。
首页 OpenTodayTasksCommand 复用 SelectDayAsync(0)，忙时禁用并通知恢复；明天/后天回今日，普通导航保原日期。数量仅完整 Dashboard 成功后有效；加载/失败/未完整成功不把默认0显示成无任务。
Sol 独立专项23/23 PASS、0skip，覆盖本卡5项、首页直接回归、导航状态/折叠、待排查选择/导出/回导专项及日期/忙状态直接回归；模拟数据夹具单跑1/1 PASS。FULL = NOT_RUN / NO_FULL。
Production Release build：0warning / 0error，明确 S9T07TestMode=false / S9T07HardKillTestMode=false；程序集1.1.3.0。专项构建原NU1900网络审计warning保留；初GUI准备C TEMP权限失败日志保留，D TEMP新根成功，不算业务测试失败。
GUI候选：C:\Users\39037\.codex\visualizations\2026\09\18\01a0b319-cbb0-73d1-88d9-0d513f92adaa\S25-T03-GUI；双击启动验收.cmd，GUI验收清单.md含用户五项检查。
新模拟数据根：D:\DevCache\Temp\7202dab1-8306-4cce-b4b9-b07741e298fb；启动前DB备份及源副本SHA匹配；integrity=ok / FK=0 /完整有序10条migration，migration11未创建；未访问正式DB。
实际WinPS入口启动PID29448、窗口9048234、Responding=true，命令行显式新模拟根。技术启动不等于真实 GUI PASS。
生产DLL SHA256：56ADFC37FD55421F75FC10DD98C20C23B75B536BEA33F29F17B55656E9E7F37B。
完整技术回执：.ai-dev/ACCEPTANCE/S25-T03.json；外部checks保留专项TRX/构建/实际启动/模拟DB证据及初失败日志。
原dirty工作区HEAD a6a47f2及1modified+4untracked保持；旧Stage25隔离链626a114 clean保持；本轮分支codex/s25-t03-today-entry。未merge/push main、tag、Version/Release或后续任务启动。
下一步只等待用户真实GUI回执，收到问题则限定返修；没有 GUI PASS 不关闭 Stage/Task、不自动发布。

以下为历史记录。
# 2026-09-18 S25-T03 方案一授权实施

Stage25 = IN_PROGRESS；S25-T03 = AUTHORIZED / IN_PROGRESS / NOT_ACCEPTED。
用户锁定：导航仅排序、首页紧凑今日主入口、已有数量复用、首页按钮明确回今日、普通导航保持；无 Badge/新查询/折叠代码。
基线 626a114914fba6265a1e847f471f44b1e24dc716，T01 GUI PASS/CLOSED、T02 CLOSED/ACCEPTED 全部保留。
全新 Terra 实施、Sol 独立技术验收；FULL = NOT_RUN / NO_FULL；技术后 USER_GUI_PENDING，真实用户 GUI PASS 前不关闭。
隔离分支 codex/s25-t03-today-entry；原 dirty 工作区与旧 Stage25 链不动；不 main/push/Version/Release。
详见 .ai-dev/TASKS/S25-T03.md 与 .ai-dev/ACCEPTANCE/S25-T03.md。

以下为历史记录。
# 2026-09-18 S25-T02 用户治理裁决｜CLOSED / ACCEPTED

S25-T02 = CLOSED / ACCEPTED，用户最终治理裁决通过；USER_GOVERNANCE_PENDING 已解除。
Stage25 = IN_PROGRESS / WAITING_USER_NEXT_STEP；S25-T01 原 CLOSED / ACCEPTED / REAL_USER_GUI_PASS 保留；S25-T03 = NOT_STARTED / NOT_AUTHORIZED。
当前 Compatibility Generation = G1-m10-protocol2；已验证最低兼容来源 = v1.1.0；Setup minimumDirectVersion = 1.1.0；Online source.minVersion = 1.1.0，两条路径基线一致。
同一 Generation 后续版本继续继承 v1.1.0 minimum。previousRelease 只决定来源版本上限，不得自动抬高 minimum。只有明确 breaking change 才能建立新 Compatibility Generation，并保留既定 breaking/桥接证据及用户裁决门禁。
四源真实 Setup / Online 矩阵技术收口及已验证 minimum 获用户接受。旧 Updater sidecar / WAL 时序风险不宣称彻底消除；既有私有测试源范围、历史失败取证及风险说明全部保留。
99.25.2 = PRIVATE_RETAINED_FOR_EVIDENCE；不公开、不发布、不替换正式资产。
本次仅治理登记：未重新测试、未重新 build、未修改生产代码或发布契约；FULL = NOT_RUN / NO_FULL；未 merge / push main；未启动 S25-T03。
本地治理提交完成后停止，等待用户确认下一步。

以下为历史技术收口及验证记录，历史等待裁决状态不代表当前状态。
# S25-T02 最终技术收口｜USER_GOVERNANCE_PENDING

当前状态：TECHNICAL_PASS / USER_GOVERNANCE_PENDING / NOT_ACCEPTED，等待用户最终裁决。
A Generation/Builder、B 四源真实 Setup、C 四源真实 Online 正常直升及故障恢复、D 已登记拒绝边界、E Generation minimum 技术验证均 PASS。
v1.1.0 / v1.1.1 / v1.1.2 / v1.1.3：Setup=PASS，Online=PASS。v1.1.2 恢复采用实际自然 AccessDenied 回滚证据，原计划进程故障未注入断言仍保留，不伪造受控注入 PASS。
v1.1.1 人工独立 Sandbox 恢复 operation=5134dac6-246e-46f2-b5ed-1847735ebbf2；发布版 Updater SHA076520E9707EAEE5F0B9A89DEEE49CF848EB746F15561870EB262520D998CA75；实际候选验证进程 ACK 前终止，Phase15 RolledBack、旧1.1.1健康 ACK及正常窗口、完整有序10条 migration、integrity=ok/FK=0/DataRoot一致，前后指纹5be06a9441979d3814bb2d1833e9f44f6d0388ed9257bbeacfb5fc5add2f4fbd保持。
原 v1.1.1 AccessDenied 归类 E 测试环境阻断，人工复核解除当前门禁；不支持协议 breaking/必须桥接结论。原具体拒绝文件和占用者未定位，不宣称取证根因已证明，原失败日志保留。
G1-m10-protocol2 minimum 技术状态 VERIFIED，minimumSourceVersion=1.1.0；Setup minimumDirectVersion=1.1.0，Online source.minVersion=1.1.0，两路径正式技术基线一致。用户治理尚未 ACCEPTED/CLOSED。
未来同 Generation 新版本必须继承1.1.0 minimum；previousRelease只决定谱系及source.maxVersion，不抬高minimum。改变minimum须新Generation及明确breaking/桥接证据和用户裁决。
若未来v1.1.4仍属G1且上一正式版为v1.1.3：source.minVersion=1.1.0、maxVersion=1.1.3、minMigration=maxMigration=20260912083448_AdjustCatchupWindowConstraint；minimumDirectVersion=1.1.0、minimumProtocolVersion=2、SAME_SCHEMA_SLIM、crossSchemaAllowed=false。仅推荐，不创建未来release条目/tag/候选。
独立核验：s25-t02-evidence/Verify-FinalTechnicalClosure.ps1；sandbox-matrix/technical-closure-verification.json=PASS；人工原始证据output-user-v111/v111-recovery。四源其余原始证据及冻结四资产SHA256重新核验有效。
Sidecar/WAL历史风险及私有源真实性范围保持原报告限定：不宣称所有旧Updater竞态消除，也不把AccessDenied归因WAL；不是公共GitHub线路验证。
99.25.2=PRIVATE_RETAINED_FOR_EVIDENCE，哈希不变，无公开上传/Latest/tag/门店交付/正式资产替换。正式私钥及宿主机原dirty工作区未动。
FULL=NOT_RUN / NO_FULL；S25-T03=NOT_STARTED；不merge/push main；S25-T01原GUI PASS/CLOSED保留。

以下为历史记录；此前等待人工复核状态已由上述最终技术结论取代。
# 2026-09-18 S25-T02 real-source matrix — WAITING_USER_MANUAL_ENV

Current: IN_PROGRESS / WAITING_USER_MANUAL_ENV / BLOCKED_BY_TEST_ENV / NOT_ACCEPTED.
TEST_CANDIDATE_SIGNING=PASS (user accepted); real Setup4-source=PASS; real original App/Updater Online direct4-source=PASS; recovery3-source=PASS + v1.1.1 AccessDenied/FailedNeedsManualRecovery BLOCKED.
Setup verified minimum1.1.0; Online numeric candidate1.1.0, complete recovery gate unresolved; generation minimumStatus staysCANDIDATE_NOT_VERIFIED, no minimum raise/new generation/bridge.
Evidence and source truth scope: .ai-dev/ACCEPTANCE/S25-T02-MATRIX.md. Private99.25.2 retained with frozen/final hashes; no public release/tag/Latest/assets/main integration. Only next action is manual-v111-recovery.wsb for a fresh independent v1.1.1 recovery proof; no production fix or successor card.
A/B/D PASS at recorded scopes; C PARTIAL; E NOT_VERIFIED_PENDING_RECOVERY_GATE. Do not assert A-E complete or USER_GOVERNANCE_PENDING/CLOSED/ACCEPTED yet.
FULL=NOT_RUN / NO_FULL; S25-T01 CLOSED/ACCEPTED/REAL_USER_GUI_PASS retained; S25-T03 NOT_STARTED. Original dirty workspace untouched.

Historical records below.

# S25-T02 | Same-Schema 跨版本直升策略与兼容基线治理

Status: AUTHORIZED / IN_PROGRESS
Baseline: 5647c58f77c3cdfc9848639d1386c171ae1ad6ac
Stage25 IN_PROGRESS; S25-T01 CLOSED / ACCEPTED / REAL_USER_GUI_PASS; S25-T03 NOT_STARTED.

Scope frozen: Compatibility Generation in release contract + Builder + focused checks + governance only. One generation minimum drives Setup minimumDirectVersion and online manifest minimum source. previousRelease retains lineage/change-impact/maximum-source duties. Same-generation minimum cannot rise without a new generation and explicit breaking-change/bridge evidence. Historical release entries and assets remain unchanged. Existing manifest/protocol2/RSA-PSS/SHA256/package semantics retained.

v1.1.0 = minimum candidate, NOT verified baseline. Validate real released 1.1.0/1.1.1/1.1.2/1.1.3 binaries on BOTH Setup coverage and Online using each source's own App/Updater. No substitution by current updater. Synthetic data only; isolated test AppId/DataRoot/install roots; no formal data/registration access. Explicit nonpublic candidate identity only, formal csproj versions stay1.1.3. No public release/tag/upload/Latest/main integration.

Allowed implementation: tools/release/release-contract.json; Builder minimal generation/source/setup validation; directly related contract/Builder checks. Runtime App/Updater/Installer, migrations/CurrentSchemaIdentity, persistent format and signature/manifest wire changes forbidden. Required runtime changes => stop and report for user ruling.

Verification: same generation inheritance, maximum=previous release, no-evidence minimum raise blocked, evidence-backed new generation, historical entries unchanged, Setup/Online same minimum, old manifest unchanged. Real4-source x2-path matrix with data fingerprint/integrity/FK/full migrations/ACK/normal launch and failure-safe evidence. Reuse existing safety checks for below-min/schema/migration/downgrade/signature/package/protocol/root identity. Preserve failures and distinguish protocol/runtime/environment blockers. FULL NOT_RUN / NO_FULL. No ordinary WPF page acceptance required. Only after all A-E gates: USER_GOVERNANCE_PENDING; never auto CLOSED/ACCEPTED, never T03.

Sol governs and independently reviews; fresh Terra implements; no S25-T01 agent reuse. Generation switch needs breaking change, affected versions, new/path minima, bridge and real user route, and why old generation cannot continue. Published v1.1.3 untouched.
Historical records below.

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






