# S9-T07 Sol 独立静态审查（实施中）

## 2026-09-06 最终独立技术裁决（当前有效状态）

结论：`SOL PASS`；`S9-T07 = TECHNICALLY_ACCEPTED / CLOSED`；`Stage9 = CLOSED`。最终 Sol 已从头复核当前完整 dirty diff、UpdateSafety 条件编译与 MSBuild 项目图、可信接管/WAL fail-closed、signed strict-prefix permit、snapshot/journal/reentry、candidate/old 精确进程身份、normal Loaded、rollback/recovery 与 test-only fixture/harness；未发现 correctness、security、data-loss 或 process-lifecycle 阻断。

独立门禁包括唯一 replacement full 1148/1148、fresh solution Release build 0 warning/0 error、EF migrationCount=9/no model drift、普通 App/Updater self-contained publish 与测试 hook 隔离、secret scan、S8/S9-T05/S9-T06 142/142 回归及五个核心真实硬杀结果。首次 full 1130/1148 的失败及 SHA 保留；A-F harness 修复后没有挑绿重复。full 后仅收窄 test-only hard-kill hook，并由 fresh build、最新 point4 和普通/测试 DLL 与 publish 扫描闭合，未运行第二次 full。

本结论只覆盖已验证升级前可信边界，不提供历史污染取证：

> S9-T07 guarantees upgrade/rollback safety from a verified pre-upgrade trust boundary; it does not provide forensic detection of historical contamination already committed into the main database.

详细证据索引见 `S9-T07-RESULT.json`。下方所有 `NOT_ACCEPTED`、P0/P1、施工中与待运行文字均是当时真实但已被后续返修和最终验收覆盖的历史审查记录。

## 2026-09-06 接续 Sol：normal 双 PID cleanup 同步反例复核

- 状态仍为`NOT_ACCEPTED`。本轮只读最新版`CandidateLoadedMismatchStopsHeldLoserAndAuthoritativeNormalIdentity`、Updater `CompleteNormalLaunch`/`StopNormalApplication`/`StartNormalApplication`及fixture normal `beforeIdentify` gate；不build/test，不审正在施工的App maintenance test entry。审阅快照：`Updater/Program.cs=F30404337249230CBC1022B1D332152DE8B48D5DE95CAE13875AB42E07F233AC`、`S9T07SchemaUpgradeSnapshotTests.cs=3BF6D81F469690250F303628A0820A4339B24174DA52ECB010728AEED69FDB0A`、`S9T07Fixture/Program.cs=A949D1015D0EFC052F6F83292398B0A60F6F4CA6059711C07C3A0B79CBE7C09F`。

### bounded 静态闭合

- fixture先取得按隔离data root派生的normal mutex，写入真实PID/start的`beforeIdentify` marker并停在`Identify`之前；因此权威进程尚未读取SQLite，也尚未写intent身份。Updater创建合法Pending intent后调用真实`Process.Start`，test-only `started` marker来自该返回句柄；loser因拿不到同一mutex退出。测试要求loser PID不同于权威PID并确认其退出，随后才DELETE target migration、release权威进程。权威进程通过共享`Identify`逐项绑定journal operation/token/role/tree/outer/schema并写自己的PID/start，真实Window Loaded再写Loaded；Updater因此在Loaded分支读取坏DB并稳定抛出`Normal application database state is invalid.`。
- `CompleteNormalLaunch`在读取/创建intent并完成durable transaction字段绑定后才置`intentValidated=true`；tree、旧ACK、verification exit、Loaded DB复核都处于同一cleanup `try`。catch在本次Start句柄存在或intent已验证时调用cleanup，bare rethrow后外层manual recovery保留原异常。测试同时断言outer与Schema `LastError`包含原DB错误，避免cleanup结果冒充主失败。
- `StopNormalApplication`分别收集本次Start返回的PID/start和当前已验证intent的PID/start，同身份才去重；每个身份都重新核对live PID、启动时刻容差与规范化`journal.AppPath/StoreExpiryInspector.exe`，只杀exact主进程并有界等待。故已退出的held loser句柄不会再遮蔽Loaded权威normal；该同步反例还断言Updater exit 1、manual phase、权威PID退出和loser PID仍退出。
- test-only入口有边界：Updater marker/短wait仅在`S9T05TestMode=true`定义的`S9T05_TEST`内编译；fixture gate只存在独立测试项目。测试finally按Updater句柄、权威句柄、record中的loser身份顺序逐actor清理，均使用已记录PID/start/exe，不扫描或杀同名进程。本轮在指定范围内未发现新的可触发缺口。

### 剩余运行门禁

- 仍需独立fresh运行该单例并保存TRX及beforeIdentify、loser Start、最终Loaded intent三组PID/start证据，核对两个App PID与Updater均零残留、manual journal双层原错误未被覆盖。该运行证据以及整卡其他门禁不能由本轮静态闭合替代。

## 2026-09-06 接续 Sol：真实 old rollback 用例与双 PID cleanup 复核

- 状态仍为`NOT_ACCEPTED`。本轮只读`RealProductionOldRollbackRestoresSchema9AndLoadsOldShell`、fixture迁移marker、Updater source-verified marker、old/candidate参数分支及双PID cleanup，不build/test。最终审阅快照：`Updater/Program.cs=FC32E631C8283BFD082D7C6E795075C6127870A0D9636890F433065FA32B5A1C`、`S9T07SchemaUpgradeSnapshotTests.cs=C2685EF0FCEDD3FF8B2ED8E32F13D4D0F88473F025DADDADD43EE5E655D8E4C0`、`S9T07Fixture/Program.cs=A2367A01E2279E4727538A02D7860C470E9F65B82113F8279956FA176724F1A4`。root所述真实old rollback fresh 1/1仅作运行收据；本轮没有复跑。history断言已由正在施工的Terra补入，本节不把其历史欠项列为新发现。

### 已静态闭合

- 用例要求`S9_T07_REAL_OLD_PUBLISH`位于TEMP直属GUID目录，复制真实prod9到`app`，以真实exe在隔离data root执行seed；candidate来自fixture 1.0.3。`SchemaVerificationArguments(..., candidate:false)`不会追加`--s9-t07-fixture-target`，因此回滚验证实际启动六参数真实old程序；candidate=true且target严格多于source、总数10/11时才追加fixture参数。
- fixture先完成`TakeOverFrozenSource`内事务迁移，再用`VerifyCoreRead`核对target migration与BLOB/stage，随后用`UpgradeHealthAck.VerifyDatabase(app.db,true)`读取实际落库migration列表并持久化marker；`FAIL_AFTER_MIGRATION`位于这些观察之后。当前marker不再只是把静态target列表抄入文件。测试读取marker并要求等于target。
- rollback在snapshot restore后调用`VerifyCurrentSource`，成功后才写source-verified marker；测试要求marker内容精确等于snapshot `LogicalFingerprint`。phase顺序随后才允许真实old verifier、source ACK与普通normal启动。最终ACK断言真实old版本1.0.2、完整source migrations和`uiLoaded=true`；normal-process PID/start仍存活且intent为Loaded，证明真实old Shell的normal路径已加载。最终DB核对source9、settings、131073字节BLOB与lifecycle history，程序树恢复为真实old tree。
- 生产cleanup已不再用`started ?? intent`二选一：它分别收集本次Start句柄身份与经验证intent PID/start，身份相同去重，逐个核对PID/start/规范exe后只`Kill()`主进程并有界等待。此前“退出的spawn loser遮蔽live authoritative intent”缺口静态闭合；同步制造不同PID的运行反例仍是已知待补门禁。
- 用例已保存seed与Updater句柄/start并分别exact有界停止；candidate由本operation `candidate-identity.json`的PID/start/exe清理，normal由record的PID/start/exe清理。正常成功路径和测试超时路径已有四actor cleanup入口，不依赖扫描同名进程或kill tree。

### P1 cleanup健壮性缺口

- `StopCandidateIdentity`只吞`ArgumentException/JsonException/KeyNotFoundException`，而identity读取/typed getter/MainModule还可能抛`IOException`、`UnauthorizedAccessException`、`InvalidOperationException`、`FormatException`或`Win32Exception`；该异常从finally冒出会阻止后续normal cleanup并覆盖原测试异常。测试自有identity正常写入时不影响主成功路径，因此本轮不升为新P0；最小加固是让每个cleanup步骤独立never-throw并记录诊断，或用嵌套`try/finally`保证normal cleanup必执行，同时给`StopExactProcess`补`Win32Exception`。

### 剩余运行门禁

- 加固cleanup helper后fresh复跑该单例，保留TRX、真实prod9 publish身份、实际migration marker、source fingerprint marker、old ACK、normal PID/start/exe/Loaded、source9与三类业务事实及四actor清理证据。另行补`started.Id != intent.Pid`同步cleanup反例；两项都不能由现有old rollback 1/1互相替代。

## 2026-09-05 接续 Sol：最新 normal guard/marker 与 shared legacy ACK 复核

- 状态仍为`NOT_ACCEPTED`。本轮只读最新 bounded normal 修复与 shared legacy ACK，不build/test，也不审 Terra 随后的实际Updater legacy终态或真实old WPF施工。审阅快照：`Updater/Program.cs=7D346322C8D6061F5401D7453D49E3FDD794FED13DD94F0C82BE673459F49093`、`UpdateProtocolJson.cs=27752EF20581296414F7A82F011C4FE0DD240566858207056C453AE81C1BC19E`、`PendingUpdateRecovery.cs=60137A7CA954C75903C103072631C532477AD15945458456C6C599D29C680BD1`、`S9T07SchemaUpgradeSnapshotTests.cs=C86C65DFE588C55EBB6CD5460702040448AFF49FF14BAF9BD18F01C5C23583F3`、`S9T07ProtocolContractTests.cs=0334119182DC8032A5E1D6A46BC5D4026AB8FF9476B62EF08C71007E55E7648E`。

### 已静态闭合

- intent通过operation/token/role/tree/outer/schema逐项绑定后才置`intentValidated=true`；tree、ACK与verification-exit预检已进入同一`try`。合法intent对应的在运行normal遇到这些预检失败会进入exact cleanup，损坏/错配intent不会仅凭其中PID杀进程。
- `hadAuthorizedIdentity`在所有intent读回点累积；Identified内部wait观察到exact进程死亡后回外循环。existing prior进程允许一次有界接续/重启，本次已Start进程死亡仍由`started`门禁失败，不形成递归或无限spawn。
- Updater在真正进入live Identified分支后写test-only observed marker，grace测试等待该marker再kill；这已消除fixture自写marker和猜测sleep造成的分支歧义。当前测试仍应解析marker的PID/start并绑定被kill进程，并把replacement record绑定为不同且最终live/Loaded的PID；timeout与ACK/DB mismatch负例也应断言`LastError`保留原失败文本。这些是证据补强，不推翻上述静态闭合。
- shared `IsLegacyHealthAck`只把严格、无重复/未知/大小写漂移的旧10字段wire shape判为legacy；读取失败、畸形或schema 12字段ACK均返回非legacy并继续作为schema/可疑证据fail-closed。`PendingUpdateRecovery.HasSchemaEvidence`和Updater `HasSchemaEvidence`都调用同一helper，没有两份形状规则漂移。现有protocol测试覆盖Schema absent/null、Completed/RolledBack与真实10字段形状，并保留schema ACK移除Schema的拒绝；实际Updater legacy终态control仍属后续运行门禁，不能由静态结论替代。

### 当前仍可触发P0

1. **本次spawn句柄可能遮蔽intent所指的另一个权威normal，失败清理会漏掉实际writer。** `StopNormalApplication`当前选择`started ?? Process.GetProcessById(intent.Pid)`；只要`started`非null，就不会检查intent进程。可按现有允许的重复进程语义触发：已有Pending进程超过grace，Updater启动第二个进程；原进程恰在此时取得/保持mutex并写Identified/Loaded，第二个无副作用退出，最终intent的PID与`started.Id`不同。随后Loaded DB复核或ACK复核失败时，cleanup看到已退出的`started`便直接return，intent指向的live authoritative normal继续作为writer，而journal进入manual。最小修复是分别按精确PID/start/exe有界停止`started`和经验证intent所指进程；两者同身份时去重，只调用主进程`Kill()`，不kill tree。最小同步反例让两个PID稳定错开且由marker确认intent已指向原进程，再触发DB mismatch，断言两个已知进程均退出、无无关进程被杀，并断言原始`Normal application database state is invalid.`没有被cleanup覆盖。

### 剩余运行门禁

- root记录的此前10/10及后续guard/marker fresh 1/1只能作为实施收据；它们未覆盖`started.Id != intent.Pid`的cleanup分支。修复并加入同步反例后需fresh保留TRX、两个PID/start/exe、intent最终身份、原始失败文本及零残留证据。实际Updater legacy terminal control和真实old WPF rollback由后续独立复核，不在本节提前判定。

## 2026-09-05 接续 Sol：normal cleanup 与三条新负例复核

- 状态仍为`NOT_ACCEPTED`。本轮只读`CompleteNormalLaunch`、`StopNormalApplication`和三条新测试，不build/test，不重复legacy/tamper结论。源码身份：`Updater/Program.cs=311B3BC91C6CF2A37AC5F39A91D7530FF764298DDF8C0F9226063FD89AFFDF0A`、`S9T07SchemaUpgradeSnapshotTests.cs=3D8061800773D720E808138139C3525C488EE898E93E20987CF09E9A74E91055`。

### 已静态闭合

- `hadAuthorizedIdentity`已在外循环、Identified wait、existing Pending grace及Start后identity wait的每次读回更新；只要代码实际观察到Identified/Loaded，后续old重启就跳过原snapshot业务指纹，同时仍fresh检查完整source migrations/integrity/FK。
- `StopNormalApplication`优先使用本次`Process.Start`返回的进程句柄；否则用intent PID/start并核对规范exe路径，只调用主进程`Kill()`且最多等待5秒，不扫描同名进程、不kill tree。helper内部吞掉身份/清理异常并记录stderr，外层bare `throw`保留原始异常；静态链不会用cleanup异常覆盖业务失败。
- `CandidateNormalLoadTimeoutStopsExactStartedProcess`真实缩短timeout并延迟fixture Loaded，覆盖本次Start句柄清理；`CandidateLoadedDatabaseMismatchStopsExactLiveNormal`先让外部fixture达到Loaded，再删target migration，覆盖intent PID/start/exe清理。两者都核对exact进程退出和manual非terminal。

### 当前可触发P0

1. **Identified进程在内部Loaded等待期间死亡不会回到有界重启。** `OldPendingGraceIdentityAllowsLegitimateSettingsChangeAfterExactProcessExit`启动existing Pending grace，再启动fixture并kill Identified进程。若Updater在kill后才读到持久Identified，外循环看到dead并重启，测试可绿；若Updater在kill前先读到live Identified，它进入310–314行内部循环，该循环只等state变Loaded、不再检查PID活性，死亡后仍等到timeout并抛manual。测试没有marker证明Updater何时观察identity，因此结果依调度，不稳定证明grace修复。最小生产修复是在Identified wait每次读回后，若仍非Loaded且`IsLive`为false就立即break/continue外循环；existing prior进程允许同一Resume做一次precheck+重启，本次`started=true`进程死亡仍按单次Start上限失败。最小测试无需复杂框架：保留当前并发形态，但增加test marker证明Updater已经在Identified wait内观察到exact PID/start后再kill，断言最终重启成功、setting合法变化保留、非manual且只有一个authoritative normal。
2. **已有normal运行时，try外预检失败仍留下writer。** tree fingerprint、旧ACK复核和旧verification PID退出检查位于cleanup `try`之前。可复现：保留`Committed/CandidateCommitted`及合法Loaded intent，让exact candidate normal存活；随后删除/破坏health ACK或改变app tree，再重入Updater。预检直接抛出，外层写`FailedNeedsManualRecovery`，但`intent`尚未读取且`StopNormalApplication`从未执行，已授权normal继续运行。这个范围属于当前terminal重入契约：不能同时呈现manual journal与仍可业务写的本operation normal。最小修复是先只读现有intent并严格核对operation/token/role/expected phase/tree身份，再把tree/ACK/verification-exit预检纳入同一个cleanup `try`；任何预检失败都按已验证intent终止exact主进程。无intent的新事务仍先预检再创建intent，不凭不可信/坏intent杀进程。最小反例用合法Loaded normal分别破坏ACK和candidate tree，断言exact App退出、journal manual、未杀无关进程。

### 测试准确性缺口

- 三条新测试都只断言exit/phase，没有断言原始失败原因。至少timeout case应断言`LastError/manual-recovery.log`含`Normal application did not report loaded`，Loaded DB mismatch应含`Normal application database state is invalid`；这样才能固定cleanup不覆盖原错。grace成功case应断言没有manual log、replacement记录的PID不同于被kill PID且仍live/Loaded，而不只断言record存在。

## 2026-09-05 接续 Sol：candidate pre-Start 负例、legacy ACK 与最终入口复核

- 状态仍为`NOT_ACCEPTED`。本轮只读、不build/test、不改生产或测试。最终复读测试身份：`S9T07SchemaUpgradeSnapshotTests.cs=79F38AB6341503E2A019C90F1A206C2336B9FEE425544960EF37869338B55B32`；审阅legacy问题时Updater/Pending身份：`Updater/Program.cs=AD1DDDB472D83E13C393577C483D9A90A9425A6365DAF3EEC437F45D5B30EFF9`、`PendingUpdateRecovery.cs=7FFE73C2D6C38CE83E055E4608E5A6BED0B2A0F1FAAB97DF5901D6E7069C4172`。后续新增normal cleanup测试没有补该tamper case的准确failure-cause断言，所以下述测试结论不变。

### `CandidateCommittedTargetMigrationTamperBlocksNormalLaunchAndPreservesTargetDatabase`

- 静态路径能到达目标门禁。journal为合法`Committed/CandidateCommitted`，snapshot metadata/source-target strict prefix有效；`app`确为candidate fixture完整树且`CandidateTree`取自该树。`staging/old`为空不会在此phase触发tree gate；`ParentPid=0`也不会在Committed分支使用。synthetic 12字段ACK与operation/token/version/target migrations/candidate PID/start逐项相等；`cmd.exe`已退出使`WaitForCandidateExit`通过。随后删除target migration，`CompleteNormalLaunch`的pre-Start `VerifyDatabase`返回source列表并与target不等，发生在`StartNormalApplication` marker之前。就当前源码而言，失败原因确为fresh DB migration拒绝，不是上述前置门禁。
- 测试仍可能未来假绿，因为只断言manual log存在，没有断言错误文本；任何更早异常都满足exit1/manual/no marker。最小补强是在启动Updater前断言fresh `VerifyDatabase(database,true)`恰等于source且不等于target，结束后断言`manual-recovery.log`唯一相关错误包含`Normal application database state is invalid.`。这样已有完整成功链可作较宽positive control，无需再复制一套Committed harness。测试名中的`PreservesTargetDatabase`不准确：它保留的是已篡改DB字节和仍存在的target表，migration身份已不是target；建议改为`...PreservesTamperedDatabaseBytesAndCandidateTree`或在断言文字中明确。

### 新确认的 legacy S9-T05 回归

- `PendingUpdateRecovery.HasSchemaEvidence`与Updater同名helper都把任意`health-ack.json`视为schema evidence。真实legacy S9-T05 candidate/old ACK正是同名10字段文件，成功后不会删除；因此Schema absent或`Schema:null`的legacy `Completed/RolledBack` journal只要保留正常ACK，普通启动会在terminal分类前抛错，直接破坏既有兼容。当前`LegacySchemaAbsentOrNullStillKeepsTerminalCompatibility`没有创建真实legacy ACK，属于弱基线；`PendingRejectsSchemaEvidenceWhenSchemaWasRemoved("health-ack.json")`还把这一回归固化成期望行为。
- 最小修复应在两个helper共享区分ACK wire shape：精确legacy 10字段ACK不是schema证据；含schema专属`launchToken/migrations`、非legacy形状或解析失败仍视为schema/可疑证据并fail-closed。更小但仍安全的实现也可从evidence文件集合移除`health-ack.json`，因为合法S9-T07 ACK前本operation已经必有snapshot/schema-source及identity/authorization；选择前用现有阶段事实固定断言。有效control必须用`S9T05AckFixture`实际10字段ACK，分别覆盖Schema absent/null与`Completed/RolledBack`，断言Pending返回false且普通App不拉Updater；另保留12字段schema ACK+移除Schema会拒绝。Updater direct-read也需至少一个legacy terminal/pending样本，避免只修App入口。

### 最小实际 old WPF / 14节点矩阵可复用入口

- 真实old-prod9发布与Shell基线：`tests/S9T01-PublishSmoke.ps1`，复用TEMP/GUID、app外CWD、真实`MainWindow/Shell`和tree不变断言；它本身不进入maintenance/Updater。
- 真实UI maintenance/准备入口：`.ai-dev/ACCEPTANCE/S9-T06-GUI-SOL-VERIFY/Invoke-GuiCandidate.ps1`可复用UI Automation与`InstallPreparedUpdateAsync`前链；其prepare-only结果不能计作事务。实际事务仍须走生产`App.xaml.cs`的`BeginDatabaseMaintenanceAsync → UpdateInstallationPreparer.Prepare → InstallPreparedUpdateAsync → 外部Updater`，只加最小test-only触发，不复制编排。
- candidate 10/11与外部Updater：`tests/StoreExpiryInspector.S9T07Fixture/Program.cs`、`ExternalUpdaterCompletesSchemaFixtureMigration`及test-mode Updater publish路径。`SchemaVerificationArguments`现在只在target比source多且总数10/11时加fixture参数；old派生source9不再带fixture参数，旧args缺口已闭合。
- 真实old verifier/normal：恢复后的old-prod9直接使用生产`--data-root <TEMP/GUID> --allow-existing-isolated-data-root --s9-t07-verify <operation> <token>`取得完整source9 ACK，再用`--s9-t07-normal-launch`取得真实Shell Loaded；复用现有PID/start/exe、tree、ACK和normal intent校验，不再新增old fixture。
- 14节点定义直接沿用`S9-T07-SOL-PLAN.md` 10.5表。现有可复用marker为Updater `S9_T07_DURABLE_PHASE_MARKER`/`S9_T05_CHECKPOINT`、snapshot/restore `SchemaUpgradeSnapshots.TestCheckpoint`以及fixture identity/Loaded文件；缺少的actor节点只加对应test-build durable marker。`tests/S9T05-RunHardKill.ps1`与`S9T05-RunRollbackHardKill.ps1`只复用循环、PID核对和结果断言骨架，固定sleep与legacy journal不能计证据。
- observer按marker内operation/PID/start/exe杀精确actor并有界等待。生产normal失败清理只终止已知App主进程，不默认kill tree；fixture/probe逐个清理自己记录的PID。每case最终统一记录journal/schema phase、三棵tree、DB完整migrations/integrity/FK/逻辑指纹、snapshot/restore/quarantine、identity/authorization/ACK、启动计数及残留进程。

## 2026-09-05 接续 Sol：Pending grace 与失败清理复核

- 状态仍为`NOT_ACCEPTED`。本轮只读最新`CompleteNormalLaunch`，不build/test，不访问正式安装或DB。源码身份：`Updater/Program.cs=F469CF3A821D2BE790FAD97B99E345BEB8DD0A103EA72EFEC5C3DF7931626BEA`、`NormalLaunchHandshake.cs=2492357F351280C454363487B07588CC523C69F80C2BE6BF66D0337D51F71513`。candidate pre-Start完整migration/integrity/FK、existing Pending grace及初始Identified/Loaded跳old snapshot fingerprint已经存在；下节旧P0由此部分覆盖。

### 当前可触发P0

1. **grace内新出现的authorized identity没有更新本地证据。** `hadAuthorizedIdentity`只在进入方法读取一次。构造既有Pending intent，重入Updater进入30秒grace；原normal在grace内写Identified并立即退出。循环读到非Pending后`continue`，下一轮发现该PID已死，reset为Pending并准备重启，但`hadAuthorizedIdentity`仍为false，于是old路径再次`VerifyCurrentSource(snapshot)`。原normal在Identify后可能已执行startup coordinator并合法改变业务指纹，因此可稳定造成假manual。最小修复是在每次从文件观察到`Identified/Loaded`时执行`hadAuthorizedIdentity = true`，尤其在grace与Identified wait读回后、任何reset前；随后仍fresh检查source migrations/integrity/FK，只跳过原snapshot业务指纹。最小反例用marker控制`Pending → Identified → 合法业务值变化 → exit`发生在grace内，断言重启不假manual且最多一个authoritative normal。
2. **timeout/复核失败会留下已经通过Identify的normal继续运行。** `Identified`活进程30秒未Loaded直接抛Timeout；Start后30秒仍Pending也直接抛；Loaded且当前DB复核失败同样抛。外层对`CandidateCommitted/OldCandidateHealthVerified`会写`FailedNeedsManualRecovery`，但没有停止normal。前两种情况下进程可能在超时边缘已通过Identify，后一种已经Loaded；它们可以继续初始化或业务写，最终形成manual journal与活跃normal并存。最小修复是在`CompleteNormalLaunch`内用一个`try/catch`统一有界清理后再向外抛：`StartNormalApplication`返回并保留本次创建的`Process`句柄/PID/start；已有intent则只按PID+start+规范exe身份终止这个已知App主进程并最多等待5秒。生产不默认`Kill(entireProcessTree:true)`：本App已知业务writer是内部线程，强杀树可能误关用户在正常App启动后打开的Excel等外部孩子。fixture/probe由测试逐个清理自己记录的PID。清理失败本身仍manual并保留诊断，绝不能伪terminal；不要扫描或杀同名进程，也不要引入watchdog。最小反例覆盖Identified timeout、Loaded后DB migration错配、Start返回后延迟Identify；每例断言该operation记录的exact进程退出、无其他normal被杀、journal不terminal、无残留authoritative normal。

### 仍需运行证明

- Terra当前只新增candidate坏DB pre-Start负例，不能替代上述grace/cleanup反例。本轮未执行任何测试；修复后需由独立阶段以TEMP/GUID外部Updater+fixture或真实old-prod9运行，保留PID/start/exe、Start计数、退出状态和journal phase。

## 2026-09-05 接续 Sol：15:11 normal 热修后复核（覆盖下一节对应快照）

- 仍为 `NOT_ACCEPTED`，只读复核、不 build/test。最新身份：`Updater/Program.cs=8DF3FE4842A41B70E7D8925366EE38940EA5040541B2C8449B6090F804E8345F`、`NormalLaunchHandshake.cs=2492357F351280C454363487B07588CC523C69F80C2BE6BF66D0337D51F71513`；下一节四项P0是修改前快照，本节给当前裁决。
- 已闭合下一节第2、3、4项：`IsLive`除PID/start外fail-closed核对规范exe完整路径；`CompleteNormalLaunch`改成单次Resume最多一次Start的有界循环；normal Identify核对exact字段、role对应phase、operation/token/tree、规范化DataRoot/AppPath，复用`SchemaUpdateJournal.Validate`，生产App与fixture都回调snapshot metadata验证。`AppContext.BaseDirectory`尾分隔符误拒绝也已用`Path.TrimEndingDirectorySeparator(Path.GetFullPath(...))`修正。

### 当前仍可触发P0

1. **Identified/Loaded normal死亡后的重入仍错误重验原snapshot逻辑指纹。** 当前已补candidate/old首次Start前完整migrations+integrity/FK，old再比较snapshot逻辑指纹；既有Pending也先bounded等待在途进程Identify，因此上一快照两项已闭合。但若intent已经Identified或Loaded，normal在terminal前死亡，代码会把intent reset成Pending，随后再次执行old `VerifyCurrentSource(snapshot)`。App在Identify到Loaded之间已运行startup coordinator，可能合法改变业务逻辑指纹；Loaded后还可能进行已授权业务写。于是任务明确要求的identity/Loaded kill重入会因合法变化假进manual。初次Start严格位于pre-Start验证之后，故durable Identified/Loaded可证明该次开放前门禁已通过；最小修复是保留“原intent曾Identified/Loaded”的事实，死亡后重启只fresh验证source完整migrations+integrity/FK，不再强比原snapshot。只有从未形成identity的Pending才必须比较原snapshot指纹。测试让startup改变代表业务值，分别在Identified后和Loaded后kill normal，断言可重入、source migrations不变、无假manual；另保留migration/FK损坏仍阻断。

### 当前运行门禁

- 修复后至少fresh执行：pre-Start candidate/old DB漂移阻断；old受控打开后的raw SHA记录与逻辑指纹语义；normal journal DataRoot/AppPath/schema/snapshot错配；无关exe PID/start；单case无kill恰好一次Start；Start后、Identified后、Loaded后、terminal前kill重入。每例必须核对terminal状态、exact PID/start/exe、Start次数与最终authoritative normal数量。
- 真实old-prod9 WPF仍是未执行门禁；fixture hidden Window和target10 `1/1`实施收据不能替代。无需service/watchdog，也不要求跨OS `Process.Start` exactly-once。

## 2026-09-05 接续 Sol：normal launch / terminal 重入冻结复核

- 状态仍为 `NOT_ACCEPTED`。本轮只读生产/fixture，不 build、不运行测试、不访问正式安装或正式 DB；仅写本治理结论。审阅源码身份：`Updater/Program.cs=2121D0B47951B13B69D1FE8564CFFB2E9075E2E4CDEB8DFEEC9E573113C51568`、`NormalLaunchHandshake.cs=22ED42D896FF830ED7D57A8B581A6C636CD83CC76639030B1AA90A82F6BE19F3`、`S9T07Fixture/Program.cs=83C1CC16BD62AD5C9D1CD922654D2D05AA74286AC30DE0F6451965FDE7F101E2`。Terra 的 target10 `1/1`、TRX `S9T07NormalOneCase.trx` SHA256 `932C1CD83160276DD3B62224699C634CB332238EF19CCF0E2C6B12658D679BD2` 和根进程零残留只记为实施收据，不是本轮独立通过。

### 已静态闭合

- Updater 与 shared handshake 对程序树使用同一套相对路径、bytes、逐文件 SHA256 和有序聚合算法；normal App 必须先取得现有 data-root single-instance mutex，再以 token、Pending state、当前 `AppContext.BaseDirectory` tree hash 写 PID/start identity。竞争创建但未取得 mutex 的进程不会写 identity；这足以保证同 root 最多一个协议认可的 authoritative normal，不要求 Windows `Process.Start` 本身 exactly-once。
- 生产 App 的专用 normal 参数只允许 GUID operation/token；普通无 token 启动仍进入 `PendingUpdateRecovery`。带 token 的路径在任何 SQLite 初始化前执行 `Identify`，随后正常执行 `DatabaseInitializer`、startup coordinator，并在真实 `MainWindow.Loaded` 写 Loaded。fixture normal 也增加了 data-root mutex；测试记录 Start 返回的 PID/start，并在 `finally` 以二元身份清理残留。
- old health ACK 复核使用临时 source 派生视图：`TargetVersion=SourceVersion`、`TargetMigrations=SourceMigrations`，同时保留原 operation、launch token、PID/start；没有把签名目标或原 journal 改写成 source。candidate/old normal 都要求相应 candidate/old tree fingerprint。

### 仍可复现的 P0

1. **terminal normal launch 只重验旧 ACK 文件，没有 fresh 重验当前数据库。** `CompleteNormalLaunch` 在 `CandidateCommitted` 和 `OldCandidateHealthVerified` 只调用 `HasValidAck`、确认旧 verification PID 已退出并核对程序树；ACK 形成后若 `app.db` migration history、integrity/FK 或 source 内容改变，旧 JSON 仍可驱动 `Completed/RolledBack` 和 normal 启动。candidate normal 的生产 `Initialize` 还可能把缺失 migration 当普通启动补做；old normal 则可能直接面对非 source schema。最小修复是在每次 Start/terminal 前复用 `UpgradeHealthAck.VerifyDatabase` fresh 核对 integrity、FK 与完整 role migrations；old还必须用 shared只读逻辑验证器把当前完整业务逻辑指纹与snapshot绑定值比较。这里不能直接复用迁移前的 `VerifyFrozenSource(dataRoot, snapshot)` raw-SHA契约：old verifier已经受控执行 `InitializeOpened/Migrate`，SQLite header/locking等可能合法改变main字节；是否实际变化由真实old运行测试记录，但terminal门禁只允许忽略raw差异，不得放宽首次迁移前冻结边界。最小反例：先生成合法 ACK/退出 verification，再删最后一条 migration（candidate）或给 restored source 插入target migration/改变代表业务值（old），断言normal Start marker为0、journal不进terminal。
2. **Loaded 活性只绑定 PID/start，不绑定 executable。** `NormalLaunchHandshake.IsLive` 接受任意 `GetProcessById(pid)` 且 start time 在一秒容差内的进程。把合法 Loaded intent 的 PID/start替换为一个存活的 `cmd`/测试进程即可稳定模拟 PID reuse，Updater会把无关进程当成已加载 App并写 terminal。最小修复是把期望 exe 身份绑定到 intent，或由 caller 传入 `journal.AppPath/StoreExpiryInspector.exe`，并 fail-closed 核对 `Process.MainModule.FileName` 的规范完整路径；复用现有 PID/start检查。测试以真实无关进程证明不 terminal，再以 fixture exe 证明接受。
3. **App 的 normal 旁路没有核对 durable journal 当前 phase/role。** `Identify`只检查 intent token、Pending 和 tree hash；`ExpectedOuterPhase/ExpectedSchemaPhase/Role`仅由 Updater读回时比较。把一份结构合法 Pending intent与已经漂移到另一 outer/schema phase 或相反 role 的 journal并置，带文件内 token 启动 App，仍可绕过 `PendingUpdateRecovery` 并进入数据库初始化。最小修复是在 shared Identify 前复用严格 journal读取，要求 operation、当前 outer/schema phase、role及期望tree与 intent逐项相等；失败必须在 DB open前退出。反例应在业务初始化 marker 前断言0调用。
4. **交接所称 bounded wait 尚未体现在冻结源码。** `Pending`启动后只要观察到非 Pending便递归调用 `CompleteNormalLaunch`；若每个进程都完成 Identify 后在 Loaded 前死亡，方法会反复 reset Pending、spawn并递归，没有总尝试/总时限，最终可能无限拉起或栈耗尽。普通无 kill 单例确实只有一次 Start，但这不覆盖重复崩溃。最小修复是单次 Resume 最多一次 Start、一个总 deadline的迭代等待；已识别进程死亡即 fail-closed，外部重入再决定一次恢复，不在同一调用递归重启。测试连续终止两个已 Identify PID，断言 Start 次数有界、无第二 authoritative normal、journal不伪 terminal。

### 剩余运行门禁

- 以上四项闭合后，先用最小专项固定：candidate/old ACK 后 DB 漂移、无关 exe PID/start、intent/journal phase/role错配、Start后/Identified后/Loaded后/terminal前 kill，以及持续 pre-Loaded crash 的有界退出。每例都核对 exact PID/start/exe、normal Start 数、最终仅一个 authoritative normal、Updater/fixture全退出或仅保留已验证 normal。
- target10普通无 kill case必须 fresh保持一次 Start；candidate和rollback各自还需真实外部 Updater重入。真实生产 WPF old tree目前没有本轮运行证据，fixture hidden Window 的 Loaded不能替代；最终仍须 TEMP/GUID old-prod9 完整 publish、source9 DB、真实 Shell/core read/Loaded ACK。此限制是证据缺口，不扩大到跨 OS exactly-once、service或watchdog。

## 2026-09-05 接续 Sol：strict protocol 冻结增量复核

- 状态仍为`NOT_ACCEPTED`。本轮只读strict协议冻结范围，不build、不fresh复跑；Terra的91/91与`obj/S9T07Protocol/S9T07StrictProtocolRegression.trx`仅为实施收据。源码身份：`UpdateProtocolJson.cs=BFC707255849AD45E92F0DDB0EA0136C46A86E7EBA02247338E14643206D05A6`、`SchemaUpdateJournal.cs=5A587F13167B3F2B440610281329567DB18F4F9476949E955700DA334E78FA1B`、`Updater/Program.cs=5A06588D39590DDA01E82086EB50CF9BEFBF3207DD1EADE2C3DD160AABD5D9A6`、`PendingUpdateRecovery.cs=B663A075E9EBF0D7EC44C1FB2ECDFC748A9973679AF5AE8A3E8645F80FD2686C`、`UpgradeHealthAck.cs=24B94F52F6C7D29ED5E2195B4B79B944D1DECD8E0454940101FC729DC41BD90E`、`SchemaUpgradeSnapshot.cs=7356A30FFF34F38789EC918434CA341EF9BF4F989F823900B355C9DC8084A4DD`、`S9T07ProtocolContractTests.cs=5B5F4247B5ED9056707BD2889B84986F4E1D1005BD4F9E13C222DF387B70D54C`。

### 已静态闭合

- `UpdateProtocolJson.RequireObject`按ordinal精确集合拒绝missing/unknown/duplicate/case变体；`ReadEnum`只接受定义内Int32数字或与正式名称完全相等的字符串，拒绝数字字符串、空白、大小写错、小数和越界。没有引入通用schema framework。
- Updater root、OldTree/CandidateTree、Schema、Snapshot及candidate identity均在deserialize前走精确字段门禁；outer/schema enum均先严格读取。`Schema:null`或完全缺省继续作为legacy，同operation出现四类schema证据时拒绝降级；20个legacy字段仍全部必需。
- outer/schema组合表与此前规格一致，`RollbackVerified`保持legacy-only；Updater在任何phase dispatch前调用完整`Validate`和组合验证。authorization现在精确七字段后才deserialize/绑定identity；非法authorization不会进入takeover/SQLite open。ACK按legacy十字段或schema十二字段精确区分，类型错误由getter异常转为false，schema count/last也与完整migration列表自洽；非法ACK不会commit，而走rollback。restore root已精确五字段并严格读取Stage，missing/duplicate root Stage在任何restore move前拒绝。
- `PendingUpdateRecovery`已在terminal判断前拒绝root/Schema/Snapshot字段歧义、strict enum和非法phase pair；duplicate Phase不再能用last-wins直接绕过。真实无Schema旧journal和当前`Schema:null` terminal兼容被保留。

### 剩余可触发P0

1. **Pending只验形状与phase，仍会让语义非法terminal journal触发普通App启动。** 构造字段名/enum/组合全部正确的`outer=Completed, schema=CandidateCommitted`，但令`Schema.LaunchToken=null`、`SourceMigrations/TargetMigrations=null`、snapshot `OperationId`不等于outer operation、`CandidatePid=0/CandidateStartedUtc=null`。`Pending.Read`不deserialize或调用`SchemaUpdateJournal.Validate`，也不验证outer operation与目录/Schema/Snapshot绑定，最终返回non-pending；App继续普通启动。Updater若被调用会拒绝同一文件，因此两个权威入口结论不一致。最小修复是在UpdateSafety共享一个轻量wire DTO/semantic validator（或在Pending构造`SchemaUpdateJournal`后复用现有Validate），至少验证全部字段JSON type/null、operationId=目录名、ProductId/version/hash/path基本身份、migration列表、snapshot绑定、phase所需PID/start，再进行terminal分类。无须复制整个Updater状态机，也不要求Pending打开SQLite；任何语义非法terminal必须抛`InvalidOperationException`并使App Shutdown。
2. **`OldCandidateHealthVerified`没有强制old PID/start，能直接跳过old ACK启动旧App。** 当前`SchemaUpdateJournal.Validate`只对MigrationStarted/MigrationApplied/SchemaHealthVerified/CandidateCommitted要求正PID+start。构造合法路径/树/snapshot的`outer=OldAppRestored, schema=OldCandidateHealthVerified, CandidatePid=0, CandidateStartedUtc=null`，组合表接受；Updater直接进入该分支`StartNormalApplication`并推进RolledBack，不读取identity/authorization/health-ack。这是“非法日志触launch”的直接反例。最小修复：OldCandidateHealthVerified（以及terminal RolledBack证据）必须正PID/非null start，并在普通启动前重验old identity+完整source ACK；PID字段只是必要条件，不能单独替代ACK。下一normal-launch实现可复用该重验，但strict validator必须先拒绝0/null与半组。
3. **Snapshot仅验字段名，语义非法journal可先切程序树再被发现。** `SchemaUpdateJournal.Validate`目前只绑定snapshot OperationId/SourceVersion/SourceMigrations；不纯校验SnapshotPath/DataRootIdentity、三个SHA/fingerprint格式、CreatedUtc及固定operation路径。构造`outer=MainExited, schema=SnapshotVerified`且`SnapshotPath=null`或越界、hash为非法值，其余三项满足；完整Validate可通过，Updater随后推进CandidateStaged并移动`app→old`、`staging→app`，到CandidateActivated后的`SchemaUpgradeSnapshots.Verify`才失败。最小修复是在任何parent exit/tree move前调用纯metadata validator，固定dataRoot/operation/snapshot路径、普通/reparse/ADS边界、64位大写hex、非默认CreatedUtc和全部snapshot/source绑定；然后重SnapshotVerified阶段应在第一次程序树移动前重验snapshot实体。结构/metadata非法不能触open/move，实体被篡改也不应先切树。

### P1与测试缺口

- restore `Quarantined`是嵌套dictionary，root exact check不会发现同一suffix duplicate key；deserialize会last-wins。当前后续hash验证降低了数据误恢复风险，但严格协议仍应枚举该object，拒绝duplicate/case变体/非`-wal/-shm/-journal`键并验证每值string+64hex。另`ReadEnum`接受精确字符串Stage，而默认`JsonSerializer.Deserialize<RestoreState>`不配置enum converter，会再以JsonException拒绝；要么明确restore只允许数字，要么使用同一已读enum构造state，避免共享parser契约前后矛盾。
- `S9T07ProtocolContractTests`当前只有enum非规范、两个非法pair、Pending duplicate Phase、legacy absent/null、schema evidence removed、authorization duplicate SHA；restore另有missing/duplicate root Stage。它没有覆盖上述三个P0，也没有系统覆盖Schema/Snapshot/ACK/identity的unknown/duplicate/case/type/null、Quarantined duplicate key、合法数字/正式字符串enum及真实旧S9-T05每类terminal/pending。
- 高价值最小负例：Pending terminal的null migrations/错operation snapshot/0 PID必须不launch Updater或App；`OldAppRestored/OldCandidateHealthVerified` 0 PID且无ACK必须保持old-normal-launch marker为0；MainExited/SnapshotVerified越界SnapshotPath或坏SHA必须保持app/staging/old tree hash与目录位置不变；ACK number/string/null错型须返回false且不commit；restore duplicate `-wal` key须在任何quarantine/main move前拒绝。再保留真实legacy无Schema和`Schema:null`的pending与四个terminal数值/字符串样本，确认兼容。

异常传播总体是fail-closed：Updater在完整Validate前不会dispatch，ACK错误变false，restore strict错误在move前抛出；但上面三个入口/语义空洞确实能触发normal launch或程序树move，不能以91/91关闭。该结论不涉及同用户同步篡改所有可信文件的全能防御，只要求单个协议文件损坏/歧义不驱动危险动作。

## 2026-09-05 接续 Sol：冻结 takeover 独立静态复核

- 本轮仅复核冻结的共享`TakeOverFrozenSource`、生产App/`InitializeOpened`、test fixture同连接迁移及相关probe；未读取正在继续修改的restore parser，未build/未fresh复跑。Terra给出的46/46与`TEMP/s9-t07-takeover-9c277cad-7728-4317-b8fb-7bd41a6b570d/takeover.trx`只记为实施收据。源码身份：`SchemaUpgradeSnapshot.cs=9EB14E307A8B4A1991A86265DA9500E50B6DCB77C1F84402AD212DE23A8A1D2E`、`App.xaml.cs=F41EE3C7AE4F7CC935FC1DFC38FDC59A6191AE86BF92DB18A06F701DB9534ED1`、`DatabaseInitializer.cs=F52B9653423981B0A780BB791D479F52767152EAD22CF2BE07391BF209C9BC8C`、`S9T07Fixture/Program.cs=6C001E022C09FA11649C0DC8D7D130D33C62CF067C67D39C6EE95FF4F03ED596`、`S9T07SchemaUpgradeSnapshotTests.cs=20C5C0AEFAB808C242519BC289F3131EFE97289F0F242367F34328322352367A`。

### 明确通过的静态链

- 入口先要求main普通文件且三类sidecar完全不存在；随后用三个固定全路径`CreateNew + FileShare.None + DeleteOnClose` reservation占住`-wal/-shm/-journal`名称。reservation持有期间重新枚举sidecar，并在`frozen-source-reservation-held`后计算main SHA；该SHA立即与authorization中的source SHA比较。`frozen-source-before-vfs-open`位于这次hash/比较之后，所以当前same-migration business drift注入确实是prehash后的漂移，不是先改后取基线。
- 同一方法内打开唯一`Pooling=false` SQLite connection；在reservation仍持有时再次拒绝非空/额外sidecar，然后无外部回调地释放reservation并立即执行`PRAGMA locking_mode=EXCLUSIVE; BEGIN EXCLUSIVE; COMMIT;`。这不是虚称main `FileShare.None`锁住兄弟WAL，而是从名称reservation切到SQLite VFS连接的exclusive ownership。现有外部fixture子进程以独立只读SQLite连接执行真实`SELECT`并得到blocked，静态接线确为跨进程probe，不是同进程布尔模拟。
- ownership取得后只把当时固定三条完整路径中实际存在的VFS sidecar记录为`path → SHA256`；`RejectLateSidecars`比较完整集合数、完整路径及内容hash，因此新增、删除、等长改写都会改变集合或SHA而拒绝。`frozen-source-vfs-owned`在capture之后、reject之前；注入rollback journal会在任何caller DDL/DML前留下原文件并拒绝。main也在ownership后再次按共享读取hash与preopen基线比较，然后才在同一连接读取完整migration/结构/数据fingerprint。
- 生产App只在PID/start authorization握手成功后调用takeover，并把该连接直接传给`DatabaseInitializer.InitializeOpened`。`UseSqlite(connection)`使用外部已开连接，`Migrate`及`journal_mode=WAL`都发生在takeover callback内，直到返回前没有另开迁移连接。fixture同样把takeover连接直接传给`FixtureMigrations.Apply`，真实DDL、BLOB DML、migration history及transaction commit均在该连接内；此前“Verify后另开连接Apply”的缺口已关闭。
- callback返回后连接才dispose。生产WPF Shell/core health及fixture `VerifyCoreRead`之后另开只读连接属于迁移完成后的受控health阶段，不破坏“首次writable open到migration commit同连接”的证明。

### 边界与剩余证据缺口

- 在已经约定的产品边界内，本轮**没有发现新的takeover数据安全P0**：所有本软件writer已由maintenance排空、默认context `Pooling=false`、data-root事务mutex仍覆盖，且release reservation到exclusive的短直线代码没有hook或返回普通调用链。标准.NET/SQLite没有把文件名reservation原子转换为VFS锁的API；不要求自造VFS，也不把可精准抢占这几条指令并直接写原始SQLite文件的同用户非协作程序扩成全能防御目标。
- 仍必须限定声明：如果非协作原始文件写者恰在reservation释放后、exclusive取得前植入可用WAL，标准锁无法提供数学上的零窗口；若该WAL只改变逻辑视图而main SHA不变，单靠后续main SHA和migration列表不能证明其来源。当前接受来自“无可插入停顿的直接接管 + 协作writer mutex + ownership后复核”的产品契约，而不是声称操作系统原子无窗。任何未来在两者之间加入await、IPC、marker或普通回调都会重新成为P0。
- **P1测试证据补强**：源码集合/hash判断足以静态覆盖owned sidecar删除和等长改写，但当前可见focused测试只在`vfs-owned`新增rollback journal，没有分别执行“已有owned WAL等长改字节”和“删除已有owned WAL/SHM”。最终fresh专项应各加一个反例：若Windows/SQLite锁直接阻断变更，记录sharing/locked即通过；若原始变更成功，必须由集合/SHA门禁在caller marker前拒绝且main不变。不能只断言length。
- **P1 probe精度**：外部lock fixture把任意`SqliteException`都写成`blocked`，没有保存SQLite error/extended code。最终实际证据应记录异常码并确认失败发生在`Open/SELECT`的busy/locked ownership冲突，而不是路径、格式或fixture自身错误；同时记录probe PID/start/exe和目标main SHA。现有测试路径是由刚创建的真实库传入，降低了误绿风险，但46/46收据仍不是Sol fresh执行。
- 最终测试还应在`InitializeOpened`回调内于EF context dispose后再用传入connection执行一次命令并断言仍为Open/exclusive，直接固定“EF未关闭/替换外部connection”这一运行时事实；这是证据补强，不是当前静态P0。

本结论只覆盖本次可信maintenance边界建立后的未知/late sidecar与受控SQLite接管，不追溯此前已固化main的历史污染，也不宣称防御能同步重写DB、snapshot、journal、authorization及全部证据的同用户行为。S9-T07整体仍为`NOT_ACCEPTED`，等待strict JSON/phase、terminal launch和最终fresh全矩阵闭合。

## 2026-09-05 接续 Sol：terminal 到普通 App 启动的可重入边界

- 状态仍为`NOT_ACCEPTED`。本轮只基于已冻结的Updater路径静态设计，不读取Terra正在修改的takeover代码、不改生产、不build。审阅的`Updater/Program.cs`身份仍为`2D8BC490C63ECEDDBB773F38FFE03F0D24039AD92230D4333DEB3FC30B8201E1`。
- 当前两个分支都是`StartNormalApplication(journal)`成功返回后才`Advance`：candidate为`CandidateCommitted → StartNormal → Completed`，rollback为`OldCandidateHealthVerified → StartNormalOld → RolledBack`。`Process.Start`只证明Windows接受创建请求，没有普通App PID/start持久证据或WPF/core loaded ACK。
- 这形成可复现竞态：普通App先启动时看到journal仍非terminal，`PendingUpdateRecovery`会再拉起Updater并自行Shutdown；第二Updater可能因第一Updater仍持有operation mutex而退出。第一Updater随后写terminal，但唯一普通App已经退出，事务呈terminal却没有正常运行实例。若第一Updater在StartNormal后被kill，普通App拉起的新Updater还可能重复同一循环。single-instance mutex只能防两个普通App同时存活，不能让非terminal普通App绕过Pending，也不能证明某次launch已成功加载。

### 保持“自动启动”契约时的最小方案

不需要service或常驻watchdog；复用Updater、App启动和single-instance mutex，增加本operation一个严格、durable的`normal-launch.json`及短握手：

1. `CandidateCommitted`或`OldCandidateHealthVerified`已durable后，Updater重验相应提交/回滚证据，再durable写launch intent：`operationId, launchToken, role(candidate|old), expectedTreeHash, expectedOuterPhase, expectedSchemaPhase, state(Pending|Identified|Loaded), pid, startedUtc, updatedUtc`。字段集合、enum、PID/start和phase组合严格校验；token每operation/role固定，重入不随意换代。
2. Updater用专用`--s9-t07-normal-launch <operationId> <launchToken>`启动普通App。App只在严格验证intent、journal phase、当前exe/tree角色后允许此模式；先取得现有single-instance mutex，再durable写自身PID/start identity，然后绕过这一次`PendingUpdateRecovery`。普通无token启动仍走现有Pending逻辑，不能泛化旁路。
3. App完成正常数据库初始化、核心Shell读取和真实WPF loaded后durable写loaded ACK；启动期间若journal仍在CandidateCommitted/OldCandidateHealthVerified，不得开始新的update recovery。Updater验证token、role、PID/start、进程仍存活、tree hash和loaded ACK后，才写`Completed`或`RolledBack`。
4. Updater重入按intent判定：无intent则创建；Pending且无可验证活进程则启动；Identified且PID/start仍活则等待loaded；Loaded且PID/start仍活则写terminal；PID不存在或start不符则保留旧attempt诊断并重新进入Pending/启动。并发重入仍由data-root+operation mutex串行；App mutex保证最多一个正常实例写有效identity。identity必须在拿到App mutex后写，避免退出的竞争实例覆盖赢家。
5. terminal持久后Updater只返回成功，不再重复launch；此时已存在一个经验证loaded且仍活的普通App。launch intent/ACK保留为terminal证据，不能先删除再写terminal。candidate分支在创建intent前必须重验candidate tree、完整target migration、ACK/authorization绑定；rollback分支必须重验old tree、source DB、old ACK绑定。

这是一份很小的operation文件状态机，而不是新更新框架。也可把等价字段加进journal，但必须保留旧journal字段/数值兼容，并纳入上一节outer/schema严格组合；独立文件更容易避免改变S9-T05 wire record。

### 仅允许用户手动重新打开时的更小边界

另一种安全但功能较弱的顺序是先durable写`Completed/RolledBack`，再best-effort `Process.Start`。kill-before-launch后，下次用户普通启动看到terminal便直接运行，不会rollback；这能满足卡Q的“实际重启不得误回滚”，但不能证明Updater自动恢复后已启动普通App，也不满足本卡rollback文字顺序“正常启动old → RolledBack”。只有用户/任务明确把契约改为“terminal后允许等待用户手动启动”时才能采用，并须在验收中明确记录`autoLaunch=false/manualLaunchRequired=true`；不能把terminal写入等同于正常App已启动。

### 真实 hard-kill 断言

- candidate与rollback各覆盖：terminal前证据已成/intent前、intent durable后/Process.Start前、Start返回后/identity前、identity后/loaded前、loaded后/terminal前、terminal durable后。marker只在相应durable文件落盘后写；按marker中的Updater或App PID+start执行`Kill(entireProcessTree:true)`，随后从同journal重启外部Updater。
- 另在identity前、identity后分别kill普通App而不killUpdater，证明Updater能识别死亡PID并重启；同时启动两个带同token的普通App，证明只有取得single-instance mutex者能写identity/loaded ACK，最终恰好一个live normal App。
- 每例断言：candidate terminal绝不回滚source9；rollback terminal始终是old tree+source9；terminal前普通无tokenApp不得进入业务初始化；最终terminal journal与intent/loaded PID/start、exe路径/tree hash一致；Updater全部退出后仍恰好一个普通App存活。若采用手动契约，则kill-before-launch后必须断言零自动App、journal terminal、一次普通用户启动成功且不产生Updater递归，并把它与自动启动证据明确分开。

现有fixture的`normal-loaded.marker`只能证明某进程的隐藏Window触发Loaded；它没有与Updater持久intent、PID/start和terminal写入顺序绑定，不能单独关闭此竞态。

## 2026-09-05 接续 Sol：严格 JSON / phase 门禁最小规格

- 状态仍为 `NOT_ACCEPTED`；本轮只读、不改生产、不 build。读取源码身份：`Updater/Program.cs=2D8BC490C63ECEDDBB773F38FFE03F0D24039AD92230D4333DEB3FC30B8201E1`、`PendingUpdateRecovery.cs=8EDBAB0AB595E0AB2E5BE8770E3AAF40950FFED3090A2FC7FCFCCC880AB3528E`、`UpgradeHealthAck.cs=288D37338FDD4010B090011AF31F97404DE076B6C56EA42528A6FC366220CD3E`、`SchemaUpgradeSnapshot.cs=3A9BF5EC75377F0DC1333FEBDF4A19EFF94E9ECCBEB448D13BA81A5A5DAA18FD`、`SchemaUpdateJournal.cs=B514789557DACEB316CAD2522863EBD2637D81DE4700AC86F836A6283CB435E3`。Terra 后续变更后必须重读并更新hash。

### 当前读取面与已存在的可复用门禁

- Updater `Read` 已对outer journal做精确属性集合，并对`OldTree/CandidateTree`做嵌套精确集合：区分大小写，拒绝duplicate、unknown、missing。这段`RequireProperties(JsonElement, expected...)`可下沉到`UpdateSafety`复用，无需引入schema framework。
- 尚未同等检查：journal内`Schema`和`Schema.Snapshot`、candidate authorization、health ACK、`schema-restore.json`。candidate identity在Updater侧已有精确camelCase五字段检查，但App读取authorization只有case-insensitive deserialize；ACK只逐项`TryGetProperty`；restore state只deserialize后做部分值校验。
- `PendingUpdateRecovery`是启动前独立读取入口，只取outer `Phase`及`Schema`是否为object；它不拒绝duplicate/unknown，也不验证outer/schema组合。`schema-preparation.json`当前只有durable写、没有恢复读取入口；因此不应为它另造通用反序列化层，后续若开始读取再按其固定六字段验证。

### P0 / P1

1. **P0：outer/schema非法组合可跳过整条安全链。** `SchemaUpdateJournal.Validate`没有检查组合，`ResumeCoreAsync`只要outer `>= CandidateActivated`且非terminal便直接按schema phase分派。例如把合法结构journal改成`outer=CandidateActivated, schema=CandidateCommitted`，并给一个存活PID/start，即会直接走CandidateCommitted分支启动普通candidate并写Completed；不要求authorization、migration、ACK或SchemaHealthVerified。另一个启动旁路是`outer=Completed, schema=SnapshotVerified`：`PendingUpdateRecovery`把任何Schema object加outer Completed视作terminal，让普通App直接启动。最小修复是一个显式、穷举的`IsValidPhasePair`，在Updater完整Validate和Pending启动分类前共同应用；未知组合一律fail closed。
2. **P0：Pending可被duplicate Phase误分类为terminal。** 当前`JsonDocument.TryGetProperty`不拒绝duplicate。构造同一对象同时含`"Phase":8`和后置`"Phase":10`，Pending可按Completed跳过Updater，而Updater自己的严格Read若被调用本会拒绝。对跨Schema未完成journal，这可让普通App启动。Pending至少必须先执行outer精确属性/duplicate检查、严格enum读取和schema phase/组合检查，再判断terminal。
3. **P1：schema与restore numeric enum未限制到定义值。** `JsonStringEnumConverter`默认允许整数，`SchemaUpdateJournal.Validate`也不做`Enum.IsDefined`；`schema.Phase=0`会命中`< MigrationAuthorized`并被推进，`999`会落到非预期分支。restore已有`Enum.IsDefined`，但仍接受missing `Stage`反序列化成默认`None`。所有enum必须字段存在，允许历史数字或精确区分大小写的正式名称，数字必须`TryGetInt32 + Enum.IsDefined`；拒绝小数、数字字符串、大小写变体、未知名和越界值。
4. **P1：authorization/ACK/restore state接受歧义对象。** unknown、duplicate及大小写变体目前可被忽略或last-wins；这不等于可伪造所有可信文件的全能攻击结论，但会让损坏/歧义协议输入进入安全决策。最小负例：authorization重复`sourceSha256`、schema ACK重复`migrations`、restore state重复`Stage`或缺失`Stage`，均须在SQLite open、phase推进或restore文件移动前拒绝并保留现场。

### 精确属性与legacy兼容

- outer journal legacy固定20字段：`OperationId, ProductId, InstallRoot, DataRoot, AppPath, StagingPath, OldPath, PackageSha256, SourceVersion, TargetVersion, ParentPid, ParentStartedUtc, Phase, OldTree, CandidateTree, CreatedUtc, UpdatedUtc, CandidatePid, CandidateStartedUtc, LastError`。这些字段均必须出现；只有值可为空的`CandidateStartedUtc/LastError`可为JSON null。唯一允许缺省的新增字段是`Schema`：历史journal可完全没有它；当前同Schema serializer写出的`Schema:null`也允许。`Schema` absent/null只走S9-T05 legacy规则。若operation目录已有`schema-source.db`、`schema-restore.json`、`candidate-identity.json`或`candidate-authorization.json`等schema事务证据却Schema absent/null，应fail closed；这样保留真实legacy兼容，同时捕获非全能的字段丢失/混合状态。
- `OldTree`、`CandidateTree`均只允许`Files, Hash`；`Files`必须字符串数组，`Hash`必须64位hex。schema对象只允许且必须出现`Phase, Snapshot, SourceMigrations, TargetMigrations, LaunchToken, CandidatePid, CandidateStartedUtc, LastError`；完整journal只接受`Phase>=SnapshotVerified`且`Snapshot`为object，拒绝`SourceVerified/SnapshotPreparing`进入正式journal。snapshot只允许且必须出现`OperationId, SourceVersion, DataRootIdentity, SnapshotPath, SourceSha256, SnapshotSha256, LogicalFingerprint, SourceMigrations, CreatedUtc`。
- identity固定camelCase五字段：`operationId, launchToken, pid, startedUtc, migrations`；authorization固定camelCase七字段：再加`sourceSha256, sourceMigrations`。schema ACK固定12字段：`operationId, launchToken, version, pid, startedUtc, migrations, migrationCount, lastMigration, integrity, foreignKeys, coreRead, uiLoaded`；并继续核对count/last与完整migrations自洽。legacy ACK固定10字段（无launchToken/migrations），只在Schema absent/null使用。restore state固定PascalCase五字段：`OperationId, SnapshotSha256, Stage, Quarantined, MainSha256`；`Quarantined`只允许三个sidecar suffix键且每键唯一、值64位hex。
- 所有协议object统一：root必须object；property名称ordinal/case-sensitive；枚举一遍用ordinal HashSet拒绝重复，同时拒绝expected集合外和缺失；随后再deserialize并跑现有语义Validate。不要启用全局case-insensitive，也不要做“忽略未来字段”的协议演进；需要新字段时显式升协议/更新允许集合。

### outer/schema唯一允许组合

schema absent/null时保留现有S9-T05 outer `Prepared(0)..FailedNeedsManualRecovery(16)`兼容与terminal分类。schema为object时只允许下列组合；表外组合拒绝：

| outer phase | schema phase |
|---|---|
| `Prepared, MainExitRequested, MainExited, CandidateStaged, OldAppPreserved, SwitchStarted` | `SnapshotVerified` |
| `CandidateActivated` | `SnapshotVerified` 或 `MigrationAuthorized` |
| `CandidateStarted` | `MigrationStarted` |
| `WaitingForHealthAck` | `MigrationApplied` 或 `SchemaHealthVerified` |
| `Committed` | `CandidateCommitted` |
| `Completed` | `CandidateCommitted` |
| `RollbackRequired` | `RollbackRequired` |
| `RollbackStarted` | `CandidateStopped` |
| `OldAppRestored` | `OldAppRestored, SnapshotRestoreStarted, SnapshotRestored, OldSchemaVerified, OldCandidateHealthVerified` |
| `RolledBack` | `RolledBack` |
| `FailedNeedsManualRecovery` | `FailedNeedsManualRecovery` |

`RollbackVerified`是legacy-only，schema事务不得使用。PID/start必须成对：SnapshotVerified/MigrationAuthorized必须`0/null`；MigrationStarted至进入rollback前必须正PID/非null；OldSchemaVerified允许`0/null`（等待/重启old identity）或正PID/非null（identity已持久），不得半组。其余rollback阶段按实际状态机保留前一candidate身份，最终实现若选择清零必须把该选择写入组合验证和测试，不能靠record默认值。

### 最小修复与高价值反例

1. 只提取一个共享`RequireExactObject`和一个接受“定义内数字或精确名称”的enum reader；Updater、Pending、authorization、ACK、restore分别列常量字段集合后复用，保留现有语义validator。
2. 在任何phase dispatch、Pending terminal判断、candidate等待authorization、ACK决定commit、restore移动文件之前完成严格结构验证。Malformed Pending必须阻止普通App启动；不能仅启动Updater后再失败。
3. 最小反例集：`CandidateActivated/CandidateCommitted`不得启动普通App；`Completed/SnapshotVerified`不得被Pending视作terminal；Pending duplicate `Phase:8/10`不得跳过恢复；schema `Phase=0/999/"candidatecommitted"`拒绝；Schema未知/duplicate字段、Snapshot缺`CreatedUtc`、authorization duplicate SHA、ACK unknown/duplicate migrations、restore missing/duplicate Stage均拒绝。再跑一份真实旧S9-T05无Schema journal和一份当前`Schema:null` journal，证明legacy阶段/terminal行为不退化。

本规格只要求协议文件在既定可信升级边界内对损坏、歧义、混合phase fail closed；不宣称能抵抗同用户同时重写所有可信文件，也不追溯边界前已固化主库历史内容。

## 2026-09-05 接续 Sol：source schema、DurableFile 与 Restore intent 复审

- 状态仍为 `NOT_ACCEPTED`；本轮只读，不改生产、不 build。读取源码身份：`SchemaUpgradeSnapshot.cs=778D54C22A7D6061DEA0D0D668776ED14622602B14EFD568C15AA7D0A584AF92`、`DurableFile.cs=BA28EA0D3A70090933FB74CD6D49CB3F0F1C93419C11C4CB2DE36F22E90D9810`、`S9T07SchemaUpgradeSnapshotTests.cs=480C4D222AE77B98FD3A02E24B3908605D0741C61FBC0CC1CD746B6EF51B4C1F`。Terra 的 34/34、28/28 是实施收据，不是本轮 fresh Sol 证据；Terra 后续正在改 takeover，相关全文件 hash 变化后须重读受影响区域。

### migration9 source schema 契约

- `Source9Tables` 与 Stage8 `RequiredTables` 一致，并只额外允许临时 `__EFMigrationsLock`；migration 全序固定9、integrity/FK、`page_count>0` 与 Stage8 相同。Stage8 `ReadSchema` 只要求每表可读且至少一列；当前 `Source9CoreColumns` 进一步要求每张表的 identity/core列。source↔snapshot 的完整 `sqlite_master`、全部 table_info、全部行/类型/长度/BLOB fingerprint 又保证复制前后结构与内容一致。这里证明的是当前 source 结构可用和快照一致，不证明历史业务内容来源正确。
- **P0：显式 index 集合只拒绝额外项，不拒绝缺失项。** 当前条件是 `indexes.Any(name => !Source9Indexes.Contains(name))`；执行 `DROP INDEX IX_products_product_code` 或删除任一列出的必需 index 后，index 集合仍是 allowlist 子集，表/core column/migration/page/integrity/FK 均可继续通过。至少部分 index 是唯一约束，缺失时不能把该库称为完整 migration9 source。最小修复是对 `sql IS NOT NULL` 的显式 index 名做集合完全相等，允许 SQLite 自动 index 保持排除；增加“删除一个唯一必需 index”和“删除一个普通必需 index”两个负例，均须在 snapshot/migration 前拒绝。
- 非 migration9 列表当前跳过版本专属结构检查，在**当前生产链**可以作为明确范围边界：Preparer 先要求 actual migrations 精确等于当前 App compiled migrations，而当前 compiled 只有9，所以 source10 会在 Create 前被拒绝。验收表述必须限定为“当前发布验证 migration9 source 并建立未来机制”，不能称任意 future source schema 已验证。
- 将来真正发布 compiled migration10 并允许 10→11 时，该版本只需同时交付它当时支持的一个（或短期兼容的少数）source结构契约；未知 source contract fail closed。无需现在创建长期 migration catalog，也不能让 generic integrity/FK/fingerprint 自描述替代版本结构有效性。

### DurableFile 与 Restore intent

- `DurableFile.Replace` 的 committed authority 始终是 target：temp 使用 `WriteThrough` + `Flush(true)`，随后 Windows 同目录 overwrite move；中断 temp 下次只移为诊断文件，不会解析或提升。注释也正确限定了存储/掉电能力，没有把 rename 返回夸大为任意介质耐久。其调用路径已经先验证受控普通目录/文件；在既定“非全能防同用户同步改写全部可信文件”边界内，本轮没有发现新的数据安全 P0。
- 上轮三个 Restore P0 在当前读取版本均有对应静态闭环：`Copying` 的 missing/partial staging 可保留残片后从已验证 snapshot 重建；sidecar hash intent 在 Move 前 durable，重入验证 source/target唯一组合；main SHA 与 `Replacing` intent 在 Replace 前 durable，replace后/state前会验证 snapshot main和quarantine main。snapshot lease/reservations及目标 sidecar reservations仍连续。新增 checkpoint 是受控异常，最终仍须真实进程 kill。
- 剩余严格性门禁沿用前文：restore state JSON 仍需纳入统一 nested strict JSON；`Replaced` terminal 重入目前只重验 restored main，不再核对 state 中已绑定的 quarantine hashes。后者不改变已恢复 DB 的安全性，但若证据完整性属于 terminal contract，应补一个 quarantine tamper 负例并在 terminal 重入验证，不应冒称隔离证据仍完整。

### release 到 SQLite VFS 的可实现接管边界（修正前文过强测试口径）

- 不要求实现一个 Windows/.NET 不提供的“关闭普通文件 reservation 与 SQLite VFS open 原子合并”原语，也不要求自造 VFS。最小可证明链是：data-root 全局 mutex 覆盖整个事务；所有本软件 writer 已停且 `Pooling=false` context 已 Dispose；candidate 在同一 takeover 方法内持有 main lease+三 sidecar reservation完成最终 hash/migrations/fingerprint；不返回普通调用链、不插入可外部等待点，立即释放并打开一个 `Pooling=false` 的受控 SQLite connection，取得 SQLite exclusive locking ownership，并在**同一已开连接**上再次核对完整 source逻辑身份后把该连接交给 EF migration，直到 migration/health完成前不关闭或换连接。
- 可执行 marker 只放在“reservation仍持有且最终检查完成”和“同一 SQLite connection 已取得 exclusive ownership且复核 source完成”两端。前一 marker 注入 sidecar必须被 reservation阻断；后一 marker 的普通 SQLite writer必须被 SQLite锁阻断。不要故意在 release 与 VFS open 两条语句之间加暂停 hook，再把能任意写原始文件的同用户进程定义成产品必须绝对防住的攻击者。
- 如果 open 后同一连接看到 source migrations/fingerprint 与 snapshot 不同，必须在任何 DDL/DML前 fail closed并保留现场。这个方案约束本软件协作 writer和本次可信边界内可观测 late WAL；它不宣称抵抗能同时改 journal、snapshot、authorization、DB和原始 sidecar 的同用户恶意协调者，也不重开历史污染裁决。当前 Terra takeover 实现尚未冻结，本节只是可验收设计，不是实现通过。

## 2026-09-05 接续 Sol：maintenance 连接生命周期最小建议

- 状态仍为 `NOT_ACCEPTED`；本轮仅静态读取，不改生产、不运行 build/test。读取身份：`DatabaseInitializer.cs=B4F0787878FA8AE462FDD309D5E1361D6E5607A58C7FB462FE204C650D569031`、`DatabaseRuntimeGate.cs=ACAE50E48CBB05CA6D5EBD3B7095AD5CD3F77F47C2BEB65E429CA743DC2AAE4C`、`App.xaml.cs=544DEF940C1C388EBFFEA8912C4BA220B966A871D244A54815A1122BD61A7B3E`。

### 静态结论

- 可以采用一行运行时连接选择：在 `DatabaseInitializer.CreateContext` 的 `SqliteConnectionStringBuilder` 增加 `Pooling = false`。EF 由 connection string 创建并拥有连接；现有生产调用均以 `using`/`using var` 释放 context，没有发现长活 `StoreDbContext`、生产 `IDbContextFactory` 或保存到 ViewModel 字段的 context。`ShellViewModel` 各 loader、Import、startup、settings、reminder 都是一次 operation 一个短 context；应用层接收 `StoreDbContext` 的 use case 只在调用栈内使用。
- 其余生产 raw SQLite 路径已经显式 `Pooling=false`：`PreImportSnapshotService`、`ImportUndoEligibilityService`、`DatabaseRestoreUseCase`、`InstallerPreflight`、`SchemaUpgradeSnapshots`、`UpdateInstallationPreparer`、`UpgradeHealthAck` 和 App verification。设计时 `StoreDbContextFactory` 只使用 `:memory:`，不接触运行时 DB。
- 因此这项修改后不应在 update maintenance 中调用 `SqliteConnection.ClearAllPools()`。盲清全局池可能关闭来源未判定的旧 pooled handle并触发 SQLite 收尾；禁用默认工厂池后，每个已知受控 operation 在自身 `using` 结束时关闭物理连接，maintenance 只需阻止新操作、等待已进入 gate 的操作完成、停止 scheduler，并在排空后立即拒绝任何残留 sidecar、取得 main 独占 lease和 sidecar reservation。

### 边界与一个必须固定的调用链条件

- 这个方案足以支持“正常 WPF 会话可升级”，无需新增 WAL provenance ledger，前提是可信边界定义在 maintenance 已阻止新操作、所有已知 using context 正常 Dispose、scheduler 停止之后。排空后出现或残留的任何 `-wal/-shm/-journal` 一律未知并 fail closed；不得为通过而 checkpoint、删除或普通 SQLite open。
- `DatabaseRuntimeGate` 只追踪合作 operation，但当前权威异步读写入口均经 gate；Detail autosave 在进入 maintenance 前由 `WaitForStableSaveAsync` 等待。MainWindow settings 两处 context 没包 gate，不过它们是 UI 线程同步 `using`，更新入口不能与其同时执行；最终外部 WPF 测试仍要固定这个事实。若以后新增后台/长活 context，必须接入 gate或另行关闭证明。
- 这不追溯可信边界前已经由正常 SQLite 会话固化进 main 的内容，也不宣称识别所有历史污染。若非协作同用户进程在边界建立后直接写 DB/sidecar，最终 sidecar/独占/identity检查应 fail closed；能同步改写全部可信文件的同用户攻击者不属于全能防御声明。

### 最小实际回归

1. 正常 WPF 长会话依次执行 dashboard、草稿 autosave、Import/History、Reminder读写，确认所有 operation 完成后进入 maintenance；断言无 pooled handle、无 sidecar，main `FileShare.None` 与三个 sidecar reservation可取得，9→10 fixture继续成功。
2. 在一个 gate operation 持有默认 context时请求 maintenance；断言 maintenance等待，Dispose后才进入冻结。冻结完成后新业务 operation被拒绝。
3. 在 maintenance排空后的最终检查前分别预置合法非空 WAL、SHM、rollback journal；断言没有 `ClearAllPools`、checkpoint或普通 SQLite open，snapshot/migration/DDL marker均未发生，main与sidecar SHA保持原样。
4. 用现有性能基线比较 `Pooling=false` 前后 dashboard、分页、草稿保存和批量导入。只在测得不可接受回归时再设计受控专用池；不能为性能恢复盲 `ClearAllPools`。

## 2026-09-05 接续 Sol：Restore 冻结后有界静态复审

- 状态仍为 `NOT_ACCEPTED`。本轮只读 `SchemaUpgradeSnapshots.Restore` 与新增受控异常测试；没有 build/test，没有真实 hard kill。Terra/协调者记录的 focused 23/23 TRX SHA256 `32BE3E25CB1C4200D3E9E104C8A7E2A3EA4EFF8A685B9721CF585AA2F3120827` 仅作为实施收据，不是本轮 Sol fresh 复跑。
- 当前源码身份：`SchemaUpgradeSnapshot.cs=94670B1D8966D3E03C459E089078C7E0ADCD96C603EB258E9488AEC28C7DE78F`；`S9T07SchemaUpgradeSnapshotTests.cs=2D91A8C09AE2DF4816F7C7E2D6FB6BF8BD802466D538D6AE84A5A07B8089A773`。

### Restore 已静态确认的进展

- snapshot 主文件 lease 与 snapshot sidecar reservation 从首次重验贯穿恢复结束；目标 DB 的三个 sidecar reservation 覆盖 `File.Replace`/`File.Move` 和最终 source 验证。该实现没有把主文件 `FileShare.None` 错说成兄弟 WAL 锁。
- `main` 缺失时可从已验证 staging 落位；`File.Replace`/`File.Move` 已完成但 `Replaced` state 尚未写时，`StagingVerified + staging missing + main == snapshot` 可识别并完成 state。`Replaced` 重入会重新验证 main，而不是重复 replace。
- quarantine 只允许固定四个普通文件名，未知目录/文件 fail closed；snapshot、staging、replaced main 的 SHA、完整 migrations 与逻辑 fingerprint 均有复核。现有受控异常覆盖 staging verified 后、单个 sidecar state 已写后、replace 后 state 前、main missing、staging/snapshot/unknown residue tamper；这些仍不能替代进程 hard kill。

### Restore 当前仍可复现的 P0

1. **`Copying` 的持久状态不足以恢复 partial/missing staging。** `WriteRestoreState(Copying)` 发生在创建 staging 前；若在 state 落盘后、`FileStream(staging)` 创建前硬杀，重入看到 `Stage=Copying` 且 staging 不存在，直接按“暂存状态缺失或篡改”进入 manual recovery。若在 copy/flush 中硬杀，staging 存在但不完整，重入直接 `VerifyStaging` 并 fail closed，同样无法按任务要求幂等继续。当前 `RestoreReentersAfterControlledInterruption` 的最早 checkpoint 已在 staging 完整验证并写 `StagingVerified` 之后，没有覆盖这两个窗口。最小修复：仅在 durable state 精确为 `Copying` 时，把不存在或经 SHA/结构验证失败的 operation-owned普通 staging 视为未完成产物，安全删除并从仍被 lease 保护且已重验的 snapshot 重新复制；其他 phase 的缺失/损坏仍 fail closed。真实测试在 Copying state 后/创建前、首块写后、flush 前分别硬杀，第二次启动必须恢复 source，且 snapshot/main/quarantine 不被误删。

2. **sidecar 隔离记录晚于破坏性 Move。** 每个 sidecar 当前执行 `hash = Hash(source); File.Move(source,target); recovery.Quarantined[suffix]=hash; WriteRestoreState(...)`。若在 Move 成功后、state 写入前硬杀，重入时 source 已不存在且字典没有该 suffix，会直接跳过；quarantine 中固定名字虽然获准存在，但其 hash 没有绑定到 durable state，之后仍会完成恢复。当前“partial sidecar quarantine”测试的 checkpoint 在 state 已写之后，未覆盖该窗口。最小修复：在 Move 前 durable 写入该 suffix 的 expected hash/intent；重入只接受 `(source存在且hash匹配,target不存在)` 并继续 Move，或 `(source不存在,target存在且hash匹配)` 并确认完成；两者同时存在、同时缺失或 hash 不符均 fail closed。实际测试在 intent 后/Move 前、Move 后/state 确认前硬杀，并单独篡改 target。

3. **restore state 自身的 `.tmp` 硬杀残留不能重入。** durable writer 在 temp 写入/flush 后再 Move；`ReadRestoreState` 发现任何 `schema-restore.json.tmp` 都直接拒绝。进程在 temp 创建、部分写、flush 后/Move 前被杀，会永久转 manual recovery，即使旧 state 和文件系统状态足以安全判断。最小修复可使用 generation/单调 phase 的双槽状态，或严格解析 current+temp 并结合 staging/source/quarantine 实体选择唯一可证明状态；不得盲删 temp。测试要在 temp 部分写与完整 flush 后分别硬杀。

### Windows replace 与身份边界

- 当前同卷 `File.Replace(staging, main, quarantineMain)` 成功后/state 前已有恢复分支；但 `migrated-app.db` 没有记录原 main SHA，只有固定文件名。建议在 replace 前把 main SHA 作为 durable intent 写入 restore state，重入时验证 quarantine main；这与 sidecar 使用同一最小 intent 模型即可。
- `File.Replace` 抛错后的实际结果不能一律假设“未替换”。重入应只接受三种可证明组合：staging=verified且main仍为迁移库；staging缺失且main=snapshot且quarantine main匹配intent；state=Replaced且main=snapshot。其余组合进入 manual recovery。至少用锁定目标/备份、AccessDenied、SharingViolation 和 replace 后受控异常覆盖；真实 hard kill仍需冻结后执行。
- data-root mutex、普通文件/reparse检查和 reservation 可约束协作进程与已知路径；不能宣称抵抗可同时重写 journal、snapshot、authorization、state 和数据库的同用户恶意协调者。本结论只覆盖本次已建立可信边界后的未知残留/late sidecar与崩溃重入，不追溯历史已固化污染。

### authorization 到首次 open 的最小可行收口

- 不再让 App 分两次调用“`VerifyFrozenSource` 返回 → `DatabaseInitializer.Initialize`”。增加一个共享的 candidate 接管入口：在 data-root 全局事务互斥仍有效时，取得 main 独占 lease和三个 sidecar reservation，完成 source SHA、完整 migration、逻辑 fingerprint；写独立 marker 后再次检查这些身份；随后由同一入口释放 reservation并立即完成 candidate 的首次受控 SQLite open/migration，期间不得返回普通 App 调用链、不得启动 shell/background writer。
- Windows 普通主文件 lease不锁兄弟 sidecar，释放 reservation到 SQLite VFS 接管之间也不能声称对任意同用户恶意写入绝对无窗。可验收边界应明确为：所有本软件协作 writer 共用 data-root mutex；受控 late writer 在最终检查 marker 前后注入 WAL/SHM/journal时，candidate 必须在任何 DDL/DML marker前拒绝；非协作同用户若能同步改写全部可信证据不属于“全能防御”声明。
- 最小反例：分别在 authorization 落盘后、candidate 最终 lease/reservation 内、reservation 释放/首次 open 接管点注入合法非空 WAL、SHM、rollback journal和主文件换名；记录 writer exit、候选首次 open/DDL marker、源 main/sidecar SHA。任何 sidecar被 SQLite读取/回放后才报错都算失败。

## 2026-09-05 接续 Sol 第三增量静态复审（Restore 冻结期间）

- 状态仍为 `NOT_ACCEPTED`。本增量只读审查 App maintenance/SQLite 连接生命周期、冻结源到首次可写 open、authorization/journal/snapshot 绑定、严格 JSON/phase、`CandidateCommitted`/terminal normal launch；没有审查 Terra 正在修改的 Restore 实现，没有运行 build/test，没有访问正式安装、正式 DB 或私钥。
- 下列 SHA256 是本次实际读取的源码身份；之后任一文件变化都必须重新审查受影响结论。

| 文件 | SHA256 |
|---|---|
| `src/StoreExpiryInspector/App.xaml.cs` | `544DEF940C1C388EBFFEA8912C4BA220B966A871D244A54815A1122BD61A7B3E` |
| `src/StoreExpiryInspector/UI/DatabaseRuntimeGate.cs` | `ACAE50E48CBB05CA6D5EBD3B7095AD5CD3F77F47C2BEB65E429CA743DC2AAE4C` |
| `src/StoreExpiryInspector/Infrastructure/DatabaseInitializer.cs` | `B4F0787878FA8AE462FDD309D5E1361D6E5607A58C7FB462FE204C650D569031` |
| `src/StoreExpiryInspector/Application/Updates/UpdateInstallationPreparer.cs` | `A4DADA49AC91B140C5F0BB5E5F7B0EF35F6EC33E83C3EEF2EEB6F7F2F3339AB0` |
| `src/StoreExpiryInspector/Application/Updates/SignedUpdatePackageDownloader.cs` | `A669CEFC0E598780FBED3098C9DD7C170720315EEF3A256DA590560238163743` |
| `src/StoreExpiryInspector/Application/Updates/UpgradeHealthAck.cs` | `AB567DEDB5EE3F04BEB395EC6E2F1D65A6CC3D49DFD0A23F192E5EF42B88AC7D` |
| `src/StoreExpiryInspector/Application/Updates/PendingUpdateRecovery.cs` | `8EDBAB0AB595E0AB2E5BE8770E3AAF40950FFED3090A2FC7FCFCCC880AB3528E` |
| `src/StoreExpiryInspector.UpdateSafety/SchemaUpdateJournal.cs` | `B514789557DACEB316CAD2522863EBD2637D81DE4700AC86F836A6283CB435E3` |
| `src/StoreExpiryInspector.Updater/Program.cs` | `38DCAECD72EBAB0A8648615A4EE603B77579A47DA66FF4B811C1AFF8C8E5F0E2` |
| `src/StoreExpiryInspector/Application/Updates/SchemaUpgradeSnapshot.cs` | `27CACE9A7B114C903318D2CBE676C6942F6DAF54A63C6D12F5603A55B95CEE3B` |

### 当前仍可触发的 P0

1. **冻结验证结束到 candidate 首次 SQLite open 仍可接纳本次边界内的 late WAL。** `VerifyFrozenSource` 两个 overload 都按“拒绝 sidecar → 路径 hash → immutable SQLite Verify”分步执行，没有覆盖整段的 main lease/sidecar reservation，也没有结束复核；App 在其返回后才调用 `DatabaseInitializer.Initialize()`。确定性触发是在 `VerifyFrozenSource` 最后一次检查后、`Initialize` 首次 open 前写入一份合法非空 `app.db-wal`：当前路径会让 SQLite 在首次普通 open 时读取它，而不是在 migration 前 fail closed。最小修复应把最终 main SHA/完整 source migrations/逻辑 fingerprint/sidecar 检查与 candidate 的首次受控 SQLite open 收敛到一个连接接管入口，并由 data-root mutex 约束全部协作写入者；用 marker 在“最终检查后/首次 open 前”注入 WAL/SHM/journal，断言 open/DDL marker 均未发生。`FileShare.None` 主库 lease 只锁主文件，不能宣称锁住兄弟 WAL。标准用户态文件与 mutex 也不构成对能同时改 journal、snapshot、authorization 和 DB 的同用户恶意进程的全能防御；本项只要求本次可信边界内 unknown/late sidecar fail closed。

2. **`CandidateCommitted` 重入不验证提交证据，并与普通 App 启动存在竞态。** Updater 在 schema phase `CandidateCommitted` 时直接删包、`StartNormalApplication`，之后才持久化 outer `Completed`。新 App 若先读到 schema+outer `Committed`，会启动第二 Updater并自行退出；第二 Updater又会因原 operation mutex 退出，原 Updater随后写 Completed，最终可能没有普通 App 存活。硬杀后重入同一分支还没有先重验 candidate tree、target 完整 migrations/integrity/FK/核心只读和 ACK 身份。最小修复是先重验 committed evidence，再持久化一个 terminal/launch-safe 状态，最后启动普通 App；rollback terminal 分支采用同一顺序。实际测试必须在 `CandidateCommittedBeforeNormalLaunch`、normal process started、terminal journal persisted 三点分别硬杀，断言始终保持 target DB、只留下一个正常 candidate，且不会再触发 rollback Updater。

3. **关键 journal/authorization/ACK 仍没有 durable 写入契约。** Preparer `WritePreparationAtomically`/`WriteJournalAtomically`、Updater `Advance`/`WriteAuthorization`、App `WriteAtomically` 仍是 `WriteAllText` + `File.Move`，没有 `WriteThrough`/`Flush(true)`；`CandidateCommitted` 因而不能作为断电后的 durable commit 证据。最小修复应复用 Restore 已采用的 durable 原子写法形成一个共享的最小 helper，并固定“证据 flush → phase flush → 后续副作用”的顺序。进程 hard kill 可验重入；物理掉电能力只能按明确文件系统契约表述，不能把 Move 返回当作落盘证明。

### 当前 P1 / 最终门禁

1. **maintenance 没有关闭受控 SQLite pool。** `DatabaseRuntimeGate` 排空的是合作业务 operation；`DatabaseInitializer.CreateContext` 的普通 EF SQLite connection 默认 pooling，`BeginDatabaseMaintenanceAsync` 停 scheduler/等待 gate 后没有 `SqliteConnection.ClearAllPools()`。正常长期 WPF 会话可能因此让 `FileShare.None` 快照 lease失败，或在冻结边界附近延迟处理受控 WAL。最小修复是在 maintenance 排空后、任何冻结检查前清理当前进程的受控池；unknown 非空 WAL 必须在任何可能 checkpoint/普通 open 前拒绝。实际测试需要“正常业务会话产生并正常关闭连接后跨 Schema 成功”和“预置外来合法 WAL 时在 Clear/open 前拒绝”两个外部进程反例。

2. **nested JSON/phase 契约仍不严格。** Updater 只精确枚举顶层 journal、OldTree/CandidateTree 与 identity；nested `Schema`/`Snapshot`、App authorization、Updater ACK 仍接受未知/重复字段或大小写变体。`SchemaUpdateJournal.Validate` 没有 `Enum.IsDefined(schema.Phase)`，也没有校验 outer phase 与 schema phase 的允许组合；`JsonStringEnumConverter` 默认仍可读数字枚举。最小修复是复用一个递归精确属性检查和显式 phase transition/组合表，同时保留无 Schema 的 S9-T05 数字兼容。实际负例覆盖每个 nested object 的 duplicate/unknown/missing/case mismatch、numeric out-of-range 和不一致 outer/schema phase，全部不得推进或启动进程。

3. **authorization 的正常协作链已基本绑定，但读取方仍需严格反例。** 正常 writer 使用 journal snapshot 的 source SHA/list、candidate identity 的 operation/token/PID/start/static target；Updater 写入前也精确比较 static target 与 signed target。`RevalidateForInstall` 当前已绑定 signed source 四字段，Preparer 已用真实 source version/actual last migration执行许可，旧 P0-4 对本 SHA 已关闭。剩余要求是 App 对 authorization 做严格 JSON，并显式验证 `sourceMigrations` 是 identity target 的严格前缀；这用于拒绝损坏/错配证据，不应表述成能防御可协调改写全部可信文件的同用户攻击者。

4. **phase 证据没有表达 `MigrationApplied`，外部成功仍非 fresh Sol 证据。** 当前 journal 从 `MigrationStarted` 在 ACK+退出后直接写 `SchemaHealthVerified`，没有 durable `MigrationApplied`；现有两个 9→10/11 外部 fixture 成功与两个负例是 Terra 已实施结果，本次 Sol 未重跑。最终需要可观测的 migration applied/ACK persisted/health verified 分界和对应 hard-kill marker，再按 SOL-PLAN 串行 fresh 执行。

### 本增量明确不关闭

- Restore P0 继续保留：Terra 正在实现/测试幂等 restore，本次按协作边界没有读取并裁定该区域，也没有真实 hard kill。
- 前 Sol P0-1 旧 candidate 身份复用继续维持“已撤销”，本次没有发现反向证据。
- 当前没有 fresh build/test/EF/publish/kill/full-suite 证据；即使上述静态项返修完成，也只能进入冻结后独立验收，不能提前接受 S9-T07。

- 审查时间：2026-09-05T20:50:20+08:00
- 基线：`origin/main=c4f7618c0dbdc0996ddfc183b9cb8e2cbf9d3803`
- 状态：`NOT_ACCEPTED`
- 性质：Terra 实施期间的未提交盘面静态审查；本轮没有运行 build/test，没有访问正式安装、正式数据库或私钥。本报告不代表实现冻结或技术验收通过。
- 独立公开发行冻结检查另见 `S9-T07-PUBLIC-RELEASE-CHECK.json`。

## 本次读取快照

下列 SHA256 只标识本次静态审查所见内容；Terra 后续修改后必须重新读完整 diff：

| 文件 | SHA256 |
|---|---|
| `src/StoreExpiryInspector.UpdateSafety/SchemaUpdateJournal.cs` | `ED81A1FB49A5C0E71A527AFD43F163B0E0136BA2D2A2820BFB61A54D89D0AF1B` |
| `src/StoreExpiryInspector/Application/Updates/SchemaUpgradeSnapshot.cs` | `51C4364645B8BDE2DAF9024897E43EC8044603A50FF887120C5C5DE1F5168B53` |
| `src/StoreExpiryInspector/Application/Updates/UpdateInstallationPreparer.cs` | `14F55E193A09119DA905A2DA980E5AF36823A3384E5C09070F5BC0CEBEB8CCB5` |
| `src/StoreExpiryInspector/Application/Updates/SignedUpdatePackageDownloader.cs` | `A669CEFC0E598780FBED3098C9DD7C170720315EEF3A256DA590560238163743` |
| `src/StoreExpiryInspector.Updater/Program.cs` | `E0D0A14CD22BA1C06ECAA58351DFA61A296A3D7182221F85F656A527D1B9D515` |
| `src/StoreExpiryInspector/App.xaml.cs` | `63889F541480C55747D9CEB8A14D921C9FF00B0E1B218CC7C39A95A153C654DC` |
| `src/StoreExpiryInspector/Application/Updates/PendingUpdateRecovery.cs` | `8EDBAB0AB595E0AB2E5BE8770E3AAF40950FFED3090A2FC7FCFCCC880AB3528E` |
| `src/StoreExpiryInspector/Application/Updates/UpgradeHealthAck.cs` | `4940A82B33BED29BCAC0BD5D42985D6308027B9C8E1EDF57827CE2E24512E901` |

### 第二增量定点复审快照

下列 SHA256 对应 identity/source authorization/frozen-source/WAL health 第二增量。本节结论只表示静态接线存在，13/13 底层测试是 Terra 开发证据；本轮 Sol 没有运行 build/test，也没有外部进程、WPF 或硬 Kill 证明。

| 文件 | SHA256 |
|---|---|
| `src/StoreExpiryInspector.UpdateSafety/SchemaUpdateJournal.cs` | `B514789557DACEB316CAD2522863EBD2637D81DE4700AC86F836A6283CB435E3` |
| `src/StoreExpiryInspector/Application/Updates/SchemaUpgradeSnapshot.cs` | `360AD5D5C80D43B5AC48D7B7D5685BC733A180D670773CBD0FDBAEA5B5B3B449` |
| `src/StoreExpiryInspector.Updater/Program.cs` | `417137A0ED24BF02903878DDEBEEB5141173FE1A51BCF273BBE566AB60C8003C` |
| `src/StoreExpiryInspector/App.xaml.cs` | `544DEF940C1C388EBFFEA8912C4BA220B966A871D244A54815A1122BD61A7B3E` |
| `src/StoreExpiryInspector/Application/Updates/UpgradeHealthAck.cs` | `603167405129EFEA67D79F9D5F57984F34AF5F2D76D5235348258C977F770096` |

## P0：提交前必须关闭

### 1. 已撤销：当前盘面没有复用已退出 candidate 身份

- 历史审查曾把 `OldSchemaVerified` 分支的 `schema.CandidatePid > 0` 误认为仍是已退出 candidate 的身份；该断言对本报告所列 `Program.cs` SHA 不成立，不能据此要求实施返修。
- 当前实际状态链：`SnapshotRestored` 删除 candidate identity/authorization/ACK，并把 outer/schema CandidatePid 与 start time 清零；`OldSchemaVerified` 首次进入时生成新的 launch token，启动 old verifier并持久化其新 PID/start/token，重入后才复用这份 old-verifier 身份。
- 该纠正只撤销原 P0；它不是运行通过证据。仍须在冻结后对 old launch 前、identity 写后、authorization 写后、old ACK 前逐点硬 Kill，证明重入使用同一 old-verifier 身份，并且旧程序只在 DB 恢复和授权之后打开 SQLite。

### 2. migration 授权校验已补，但没有连续绑定最终快照与冻结源实体

- 位置：`SchemaUpgradeSnapshot.Create` 本次约 38～50 行；Updater `SnapshotVerified` 到 `MigrationAuthorized` 转换。
- 第二增量已有保护：Updater 在 candidate identity 到达后、写 authorization 前同时调用 `Verify(snapshot)` 与 `VerifyFrozenSource(snapshot)`；authorization 携带 snapshot 的 SourceSha256/source migrations；App 收到后再调用 `VerifyFrozenSource(hash,list)`，然后才 `DatabaseInitializer.Initialize()`。因此旧版“只验证 journal 元数据”和“candidate 不复核冻结源”的表述均已撤销。
- 当前触发一：`VerifyFrozenSource` 先 RejectSidecars，再分别按路径 Hash 和 immutable Verify，没有一个覆盖全段的主文件 lease/reservation，结束时也不再次拒绝 sidecar。数据库可在 hash 与逻辑读取之间被替换；尤其 App overload 的第二次读取只比较 migration list，替换成同 migration、不同业务数据的数据库可与第一次取得的原 source hash 拼接成一次错误通过。
- 当前触发二：Updater 两次 Verify 返回到 authorization 原子发布之间、App VerifyFrozenSource 返回到 `DatabaseInitializer.Initialize()` 首次可写 open 之间仍有窗口。late WAL/SHM/journal 或主文件换名可在校验后出现并被 candidate 消费；这不满足“late sidecar 在首次 SQLite 写打开前 fail closed”。
- 当前触发三：authorization 的 SourceSha256/source migrations 不与 candidate identity 中既有可信值绑定；shared Validate 只要求 source list 格式合法，没有要求它是 identity migrations 的严格前缀。若 authorization 被替换为另一份匹配当前数据库 hash/list、但不属于 signed source→target 三方链的内容，App 会自行接受。Updater 正常生成值是正确的，但读取方没有封闭篡改反例。
- 必要修复：Create 对 temp 到 final 发布使用连续阻写/删除契约并保护最终 sidecar 名；authorization 读取方必须能把 source hash/list绑定到不可被同目录文件替换同时改写的已验证操作身份，并至少验证 source list 是 candidate target 的严格前缀；冻结源的 hash、逻辑、sidecar 和实体身份必须在同一保护契约下完成，并消除 App 校验结束到首次授权 SQLite open 的 late-sidecar 窗口。
- 必要检查：temp/final 主文件写入、删除、换名及 late sidecar 注入；所有失败必须发生在 candidate 首次 SQLite open 之前。

### 3. candidate 静态 migration 声明已静态接线，尚未外进程实测

- 第二增量中 `SchemaCandidateIdentity.Migrations` 已加入；App 在等待 authorization 前通过 EF migration assembly 取得静态全序，Updater 在 `MigrationStarted` 写 authorization 前用 `SequenceEqual` 精确比较 `identity.Migrations` 与 signed journal target。10A、跳号、删除、重排、额外 migration、candidate/manifest 分叉已有静态拒绝点。
- 从代码路径看，`StaticMigrations()` 只构造 context 并调用 `Database.GetMigrations()`；其后才等待 authorization，再由 `VerifyFrozenSource` 首次只读打开数据库，最后才 `DatabaseInitializer.Initialize()`。仍需 marker/hook 证明实际 provider 没有在静态声明阶段打开 SQLite，并用真实外部 candidate 证明所有分叉在 DDL/DML marker 前拒绝。

### 4. signed source 许可没有在安装冻结点完整复核

- 位置：`SignedUpdatePackageDownloader.RevalidateForInstall` 本次约 158～170 行；`UpdateInstallationPreparer.PrepareCore` 本次约 61～85 行。
- 触发：重验只绑定 minimum protocol 和 target migrations，没有把 `SourceMinVersion`、`SourceMaxVersion`、`SourceMinMigration`、`SourceMaxMigration` 与 signed manifest 逐字段绑定；Preparer 也没有用刚取得的真实 source version 与实际最后 migration 再执行 signed source 许可判断。公开 record 字段可与 signed bytes 分叉。
- 必要修复：重验时绑定所有 signed source 字段；在 maintenance/freeze 后用真实 source version、实际完整 source migrations 和最后 migration 检查许可。
- 已确认进展：本次盘面已经要求 cross-schema 使用 protocol 2、实际 DB migrations 等于旧程序 EF 声明、source 是 signed target 的严格前缀；这些旧缺口不再重复报告。

### 5. snapshot 的 fingerprint 是身份值，不是 source schema 有效性证明

- 位置：`SchemaUpgradeSnapshot.Verify/Fingerprint` 本次约 118～157 行。
- 触发：只创建 `__EFMigrationsHistory` 并插入 9 个合法 ID 的空业务数据库，仍可能通过 integrity、FK、migration 格式和自描述 fingerprint，进而生成“已验证”保护快照。缺少 Stage 8 的 RequiredTables、只允许表集合、核心 columns、page count 等有效性要求。
- 必要修复：复用或等价实现 Stage 8 的已验收 schema 契约；完整 `sqlite_master` 与逐字段长度编码用于一致性，RequiredTables/core columns/page count/固定 source migrations 用于有效性。
- 必要检查：缺表、额外表、缺核心列、改 index/trigger/view、仅 migration history 空库均拒绝。

### 6. Restore 尚不能从中途硬 Kill 重入，也不能恢复丢失的 main DB

- 位置：`SchemaUpgradeSnapshot.Restore` 本次约 62～94 行。
- 触发一：固定 `quarantine` 或 `schema-restore.tmp` 已存在就抛错。Kill 在建目录、复制 staging、移动部分 sidecar 后，会永久转人工恢复，不能完成 Task 指定的 restore 前/中/后恢复。
- 触发二：`ValidatePaths` 在 Restore 开始即要求 `app.db` 为现存普通文件。迁移故障导致 main DB 丢失时，保护快照无法恢复。
- 触发三：sidecar 全量预检后逐个 Move，仍未用 journal 子阶段记录每个已移动实体；移动完成到 Replace 之间也没有三个目标 sidecar 名的连续 reservation。
- 必要修复：使用 operation/journal 绑定的确定性 staging/quarantine 和可识别的幂等子阶段；验证已有残留后继续；支持 main 缺失的安全恢复分支；在置换窗口证明 candidate/连接已退出并保护 sidecar 名。
- 必要检查：每个文件动作前后稳定 marker 硬 Kill；恢复结果必须是 snapshot 精确 bytes/source fingerprint，故障 DB/sidecar 保留可诊断，旧程序此前不得启动。

### 7. candidate 健康检查已改为包含 WAL，尚未真实 migration 实测

- 第二增量把 schema candidate 的 verification shell 和最终 health connection 改为普通 path + `Mode=ReadOnly` + `Pooling=false`，不再使用 immutable；同 Schema 的既有验证仍保持 immutable。静态上 candidate health 能读取其受控 migration WAL。
- 仍未证明：13/13 底层测试不是实际外部 WPF candidate。真实 WAL fixture 必须证明 DDL/DML/BLOB 明确仍在 WAL 时，Shell core read、integrity/FK 和完整 target list均来自包含 WAL 的一致视图；ACK 后 candidate 完全退出，Updater 还须按最终协议验证合法 WAL 的收口与任何退出后 late sidecar 的拒绝。

### 8. 新 journal 和 CandidateCommitted 尚无落盘耐久证明

- 位置：Preparer `WritePreparationAtomically`/`WriteJournalAtomically`、Updater `Advance` 均为 `WriteAllText` 后 `File.Move`，未见 `Flush(true)`/WriteThrough 或等价持久化契约。
- 触发：tree 已切换或 DB 已迁移后发生断电，journal 可能丢失、回退或截断；普通旧程序随后可能找不到 pending recovery。`CandidateCommitted` 也不能仅因 Move 返回就宣称已耐久提交。
- 必要修复：定义并实现 journal 文件内容及原子替换的持久化顺序；任何 migration authorization 前必须已有可恢复的 durable journal/snapshot 身份，CandidateCommitted durable 后才允许正常 candidate 启动。
- 必要检查：进程硬 Kill覆盖所有阶段；物理断电耐久性若无法自动实证，必须给出文件系统契约与能力边界，不能把普通 `File.Move` 当 fsync 证明。

### 9. authorization 前失败可能在无 identity 时递归回滚

- 位置：Updater `ResumeSchema` 的 `MigrationAuthorized`、`RollbackRequired` 与 `StopSchemaCandidate`。
- 触发：最终 snapshot 在 candidate 启动前验证失败、`Process.Start` 失败，或 candidate 在写 identity 前退出时，catch 会把 journal推进到 `RollbackRequired`。该分支无条件 `ReadIdentity`；文件不存在会再次进入 catch，再次写同一 `RollbackRequired` 并递归 `ResumeCoreAsync`，既不能推进 `CandidateStopped`，也不能可靠等待已启动但尚未写 identity 的进程退出。
- 新字段影响：candidate 正常写出 identity 后，target migrations 比较和 PID/start 验证方向正确；但 identity 尚不存在的授权前窗口没有可持久恢复的 launched PID/start，也没有“从未启动／已确认退出”的独立状态。
- 必要修复：把 process launch 身份先持久化，或为未产生 identity 的路径提供确定性的未启动/已退出证明；rollback 必须把“没有 identity 且没有活 candidate”作为可推进状态，不能靠异常递归。用 snapshot prelaunch tamper、Process.Start 后 identity 前硬 Kill、identity 文件缺失三项外进程测试固定行为。

## P1：必须在最终验收中关闭或明确证明

### A. maintenance 与连接池关闭顺序

`DatabaseRuntimeGate` 只追踪合作业务操作；EF SQLite connection 默认启用 pooling。当前 maintenance 停 scheduler 并等待 tracked workers，但未见 `SqliteConnection.ClearAllPools()` 或其他“全部 SQLite 连接已正常关闭”的证明。可能结果是 `FileShare.None` 快照 lease 永远失败，正常 cross-schema 更新不可用；也可能 pool 在边界期间延迟关闭并改变 WAL 状态。

应在可信当前会话、maintenance 已排空后显式关闭池，再立即拒绝残留 sidecar并取得主库 lease。只允许关闭已由当前受控会话拥有的连接；若已有来源未知的非空 WAL，不得用 ClearAllPools/checkpoint 将其吸收后继续。需有正常 WPF 业务会话产生 WAL 的成功路径，以及外来合法 WAL 在任何 checkpoint/open 前拒绝的反例。

### B. CandidateCommitted 后启动普通 App 的竞态

Updater 当前先 `StartNormalApplication`，再把 top phase 写为 Completed。新 App 会先看到 schema Committed journal，启动另一个 Updater并自行退出；原 Updater随后完成，可能没有正常 App 留存。RolledBack 前的普通旧程序启动存在同类竞态。提交后 launch 失败当前还会写 FailedNeedsManualRecovery，虽不回滚已提交 schema，但会阻塞后续普通启动。

需要持久的 post-commit/post-rollback launch 子状态或握手，使重入只重试允许的正常启动，且绝不回滚 CandidateCommitted。

### C. schema nested JSON/handshake/ACK 尚未保持严格 JSON 契约

Updater 顶层 journal 和 identity 使用精确属性枚举，但 nested `Schema`/`Snapshot`、App 读取 authorization、Updater 读取 ACK 未统一拒绝未知/重复/缺失属性；`SchemaPhase` 也未见 `Enum.IsDefined` 与 outer phase/schema phase 一致性检查。当前多数异常会 fail closed，但会把可恢复 journal 变成人工恢复，并留下解析器差异空间。需加入严格正反例，同时保持旧 S9-T05 无 Schema journal 的数字兼容。

### D. PID 身份还应绑定实际 executable/tree

PID + start time 可降低 PID 重用，但 Updater 授权前没有把运行中进程可执行路径重新绑定到 `journal.AppPath/StoreExpiryInspector.exe` 和已验证 candidate tree。应在 Windows 上验证进程路径；失败应在 authorization 前拒绝。

## 已确认的正确方向

- shared production project 是 App/Updater 两个真实消费者的单实现复用，App 已排除本地重复编译；该工程变化本身可接受，最终仍需验证 app/updater self-contained publish 都带齐共享程序集且没有测试 fixture。
- App 的 cross-schema 路径静态顺序目前是：解析安全参数、pending 分流、single-instance、logger、写 identity/等待 authorization，然后才 `DatabaseInitializer.Initialize()`。未发现 authorization 前的业务数据库 open；需通过 hook/marker 实测固定此顺序。
- `PendingUpdateRecovery` 已使带 Schema 的 legacy Committed(9) 保持 pending，不再直接绕过 post-commit 恢复；旧无 Schema journal 的既有数字语义仍保留。
- shared journal 已把 candidate PID 必填限制在 forward migration phases，因此 prelaunch failure 转 RollbackRequired 不会仅因枚举数值顺序被 Validate 拒绝；`StopSchemaCandidate` 对 identity 缺失的运行时恢复缺口仍见 P0-9。
- Preparer 已写 tree-switch 禁止的 `schema-preparation.json`，并在正式 journal 后删除；snapshot 前硬 Kill不会授权 tree switch/migration。其残留清理与实测仍待门禁。

## 后续 Sol 门禁

Terra 提交并停止后，Sol 必须从冻结提交重新读取完整 diff，再串行执行计划中的 focused、真实 WPF、Updater、稳定 marker 硬 Kill、S9-T05/S9-T06/Stage 8 回归、无 filter Release 全量、Release build 0/0、EF model/migration9、App/Updater self-contained publish、fixture/secret/Git 检查。当前所有结论均为静态发现，不能替代这些 fresh 证据。

## Terra 必须交付的最小 test-only fixture

fixture 必须是默认生产 build/publish 不包含的测试项目或显式 test-only 条件输入；不能新增正式 migration10、不能改变生产 ModelSnapshot。Sol 不代写生产接线，冻结后只消费下列入口做独立验收。

### 建议文件与职责

1. `tests/StoreExpiryInspector.S9T07Fixture/StoreExpiryInspector.S9T07Fixture.csproj`
   - 独立 `net10.0-windows` test-only candidate，可显示真实 WPF Window 并执行与正式 App 相同的 identity → authorization → SQLite open → migration → health ACK 协议。
   - 引用 shared UpdateSafety；输出不得进入 App/Updater 的普通 publish。
   - 若必须通过条件编译把 migration fixture 注入真实 App，属性只能由测试命令显式传入，普通 build/publish 必须有反向扫描证明 fixture 类型、migration ID、marker 字符串均不存在。
2. `tests/StoreExpiryInspector.S9T07Fixture/FixtureMigrations.cs`
   - source 完整列表精确等于现有 9 条。
   - target10：`20260905120000_S9T07Fixture10`；执行真实 DDL 建普通表，执行真实 DML，从 source 代表性数据派生一行并写入至少 131073-byte BLOB。
   - target11：`20260905121000_S9T07Fixture11`；再做可核对的 DDL（例如新增非空默认列或 index）和 DML 更新。
   - failure 模式应在事务内 DDL 后、DML 后各提供 marker/fault；不得把 migration history 手工写成成功来冒充迁移。
3. `tests/StoreExpiryInspector.Tests/S9T07CrossSchemaContractTests.cs`
   - 覆盖 actual source == old compiled declaration、actual strict-prefix signed target、candidate compiled declaration == signed target 三方全序。
   - 覆盖 protocol1 同 Schema、protocol2 跨 Schema、source version/migration signed range、10A、跳号10→11、删除、重排、分叉、重复、未知 source、candidate/manifest 分叉。
   - 覆盖 nested journal/snapshot/identity/authorization/ACK 的未知、重复、缺失、numeric enum 越界及旧 S9-T05 journal 兼容。
4. `tests/StoreExpiryInspector.Tests/S9T07CrossSchemaTransactionTests.cs`
   - 用真实 App tree、Updater tree、fixture candidate tree 和 TEMP/GUID data root 驱动完整事务。
   - 成功时核对 target10/11 DDL、DML、BLOB、原业务 fingerprint、完整 migrations、ACK PID/start/token/UI loaded，然后确认 CandidateCommitted 后不回滚。
   - 失败时核对 old tree、snapshot 原始 SHA、source migration9、source fingerprint、故障 DB/sidecar quarantine、真实 old WPF ACK。
5. `tests/S9T07-RunHardKillMatrix.ps1`
   - Sol 可独立调用的串行 runner；每个 checkpoint 使用全新 TEMP/GUID install/data/operation，不能复用上个绿色样本。
   - 参数至少为 `-AppPublish`、`-UpdaterPublish`、`-FixturePublish`、`-ResultsRoot`、`-Checkpoint`；拒绝非 TEMP/GUID data root。
   - 输出每场原始 JSON 和总表，退出码非零即停止，不自动循环挑选成功样本。

### marker 协议

- marker 是 operation 目录外或 operation 下独立的原子 JSON 文件，不得打开或锁住 `journal.json`、snapshot、DB 或 sidecar。
- 必需字段：`operationId`、`checkpoint`、`actor`、`pid`、`startedUtc`、`journalPhase`、`schemaPhase`、`timestampUtc`；适用时加 `databaseSha256`、`migrations`、`snapshotSha256`、`treeSha256`。
- writer 使用 temp + atomic move 并完成 flush；runner 轮询读取后必须核对 operation、checkpoint、PID/start time 和进程仍存活，再执行 `Process.Kill(entireProcessTree:true)`。
- runner 保存 marker bytes/SHA、kill actor/PID/start、恢复入口、所有 exit code 和最终 journal；固定超时必须计为失败，不能按普通 skipped 处理。

### 至少 12 个确定性硬 Kill checkpoint

| checkpoint | kill 对象 | 重启后必须成立 |
|---|---|---|
| `SourceVerified` | Preparer | 未授权 tree switch/migration；普通 source 可安全启动 |
| `SnapshotPreparing` | Preparer | 未授权 migration；残留被识别或安全清理 |
| `SnapshotVerifiedBeforeJournal` | Preparer | snapshot 可留证，但无 journal 就绝不迁移 |
| `MigrationAuthorizedBeforeCandidateOpen` | candidate 或 Updater | candidate 尚未打开 SQLite；恢复可继续或回滚 source |
| `MigrationTransactionAfterDdl` | candidate | 未提交事务不冒充 target；自动恢复 snapshot/source |
| `MigrationTransactionAfterDml` | candidate | 同上，BLOB/业务源指纹恢复 |
| `MigrationAppliedBeforeAck` | candidate | journal 未 committed，必须恢复 old tree + source DB |
| `AckPersistedBeforeCandidateCommitted` | Updater | ACK 不等于 durable commit；按未提交策略恢复或确定性继续提交 |
| `CandidateCommittedBeforeNormalLaunch` | Updater | 永不回滚 target；重入只完成正常 candidate 启动 |
| `OldAppRestoredBeforeSnapshotRestore` | Updater | old 尚未启动；先恢复 DB |
| `SnapshotRestoreStaging` | Updater | 识别已有 staging/quarantine 并幂等继续 |
| `SnapshotRestoreReplacedBeforeJournal` | Updater | 识别 DB 已等于 snapshot，继续 source 验证，不二次破坏证据 |
| `OldVerifierIdentityBeforeAuthorization` | old verifier 或 Updater | old 尚未打开 SQLite；重入使用独立 old 身份 |
| `OldAckBeforeRolledBack` | Updater | source DB/old tree 保持，重入完成 RolledBack 与普通 old 启动 |

### A～R 的可执行映射

- A、F、G、K、L、M、N：`S9T07CrossSchemaContractTests` 的无进程单项，配合真实文件锁/sidecar/路径实体测试。
- B、C、D、E、H、I、J、O、P、Q、R：`S9T07CrossSchemaTransactionTests`，每项启动真实外部进程；B/R 还必须看到真实 WPF loaded，不接受仅写布尔 fixture。
- 硬 Kill 表覆盖 snapshot、migration、ACK/commit、restore、old ACK；其中 DDL/DML 两点使用 target10/11 的真实事务 marker。
- 每项最终统一读取：integrity、FK、完整 migrations、required schema、代表性业务+BLOB fingerprint、sidecars、app/old/candidate tree fingerprint、journal terminal phase。

### Sol 最终运行顺序

1. 冻结提交后审查普通 App/Updater publish 不含 fixture。
2. 单独构建 Release App、Updater、fixture；固定各树 SHA。
3. 先跑无进程 contract/primitive，再跑一次正常 9→10、一次 9→10→11。
4. 串行跑每个硬 Kill checkpoint；任一失败立即保留原始目录并停止。
5. 再跑真实 WPF 成功与回滚、S9-T05/S9-T06/Stage 8 回归、无 filter 全量、EF migration9 与正式 publish 门禁。
