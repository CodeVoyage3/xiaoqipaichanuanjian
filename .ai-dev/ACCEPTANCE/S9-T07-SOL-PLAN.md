# S9-T07 Sol 独立技术验收计划

## 2026-09-06 当前裁决：收口模式

本节覆盖下方早期重复运行设计。全部确定性状态机/fault-injection覆盖保留；14节点×3真实硬杀耐久矩阵转入 `../BACKLOG.md`，不阻塞S9-T07/Stage9 CLOSED。开发只跑S9-T07专项与必要S8/S9-T05/S9-T06回归；冻结代码后，Sol最终只执行一次fresh无filter Release全量、build、EF/migration、secret scan、git门禁，不机械重跑。

当前真实子进程硬杀清单：

| 节点 | 实际停止位置 | 初始次数 |
|---|---|---:|
| 1 | SchemaMigrationStarted后 | 1 |
| 2 | SchemaMigrationApplied后、candidate ACK前 | 1 |
| 3 | ACK后、CandidateCommitted前 | 1 |
| 4 | SchemaSnapshotRestore过程中 | 1 |
| 5 | old app恢复后、old health ACK前 | 1 |

必须等待实际actor持久marker，按PID/start/exe精确硬杀并有界清理；恢复后验证程序树与DB版本配对、完整migration/integrity/FK/保护指纹和ACK。首次失败或不稳定才追加针对性重复，稳定通过不重复3次。最终门禁失败保留真实NOT_ACCEPTED，不无限重跑挑绿色。

状态：`READY_FOR_POST-TERRA_REVIEW / NOT_EXECUTED`。本计划只规定 Terra 提交后的串行独立验收，不是通过记录。全部数据库、程序树、Updater、snapshot、journal、结果目录均使用本轮新建的 TEMP/GUID 合成根；禁止读取、复制、哈希或探测正式安装、正式数据库、正式 Backup、真实 Excel 和私钥。

## 1. 接收与冻结

- 取得 Terra 明确提交 SHA 后停止其实施进程；确认没有并发 `dotnet` build/test/publish、Updater 或测试 WPF 残留。
- 记录 `git status --short --branch`、`git rev-parse HEAD`、`git rev-parse origin/main`、`git diff <baseline>..HEAD --stat` 和完整 changed-file 清单。生产/测试 SHA 在独立验收期间冻结；后续若改变，受影响门禁全部重跑。
- 只审查 `c4f7618c0dbdc0996ddfc183b9cb8e2cbf9d3803..HEAD` 的完整 diff，同时区分用户/根代理治理文件与 Terra 生产/测试提交。先执行 `git diff --check <baseline>..HEAD`，任何错误停止。
- 核对没有生产 migration10、`StoreDbContextModelSnapshot`、Domain、业务算法、索引、installer、公开 Release/bridge 或 Stage10 范围变化。项目引用或协议字段若为独立Updater读取SQLite和本卡安全接线所必需，可以接受，但必须最小化、优先复用仓库已有 `Microsoft.Data.Sqlite`/框架能力，不引入无关业务依赖，并验证self-contained publish完整。当前未发布1.0.2版本可保持不变；公开v1.0.0/v1.0.1字节与资产绝对不变。

## 2. 完整 diff 必审链

### 2.1 migration 许可

- 实际数据库 source 完整列表必须与旧程序声明逐项相等，无未知、重复、缺失或分叉。
- source 必须是候选静态 EF migration 完整列表的严格前缀；候选静态列表必须与签名 manifest `targetMigrations` 逐项完全相等。source version 和完整 source migration 身份必须满足 manifest 许可。
- 9→9走原同 Schema路径；9→10允许；删除、重排、9→11跳过候选已声明10、9→10A错配、9,10→9,11、重复、manifest/candidate不一致均在任何数据库写入前拒绝。
- strict prefix 只负责序列关系。候选+manifest由可信发布签名同时授权的新序列属于既有发布信任边界，不另造 migration catalog。

### 2.2 maintenance 与快照信任边界

- 调用顺序必须是：阻止新业务操作 → 等待 `DatabaseRuntimeGate` 权威写归零 → 停止 Reminder/background → 正常释放所有 SQLite connection/context/pool → 建立可信冻结边界 → 创建并验证快照 → journal持久记录 → 才可授权 migration。
- 建立边界时发现任何未知非空或残留 `-wal`、`-shm`、`-journal`，立即 fail-closed。禁止先 checkpoint、打开 source 或让 SQLite 回放未知 WAL 后继续；SQLite 正常关闭自动 checkpoint 不能替代此门禁。
- 从首次 sidecar 检查到 source 身份/hash、快照完成和最终复核之间必须防 TOCTOU：证明排他访问或用等价机制阻止普通受控写，并在 SQLite 打开前后核对 source hash、完整 migration、逻辑指纹和 sidecar。任何 late sidecar/source 漂移拒绝。
- Windows文件门禁至少证明：取得main DB `FileShare.None` lease后再次检查sidecar；对原本不存在的`-wal/-shm/-journal`名称用`CreateNew`和`FileShare.None`持有本operation reservation；main lease与reservation贯穿raw copy、`Flush(true)`、immutable只读验证和前后hash。reservation创建冲突或清理失败均拒绝。main lease本身不锁兄弟sidecar，不能据此宣称已关闭late-WAL窗口。
- lease释放到candidate打开SQLite之间，以data-root全局互斥和candidate打开前的main hash/sidecar复核约束普通受控写者；不宣称标准文件锁能绝对阻止同用户非协作进程在该瞬间直接写sidecar。
- 快照验证不得弱于 Stage8：source 与 snapshot 的完整 schema/table/column、完整 migration、integrity、FK、page/size、全业务字段及 BLOB 逻辑 fingerprint、文件 SHA 均可核对。
- 逻辑fingerprint必须流式增量hash，并编码table/row/column、SQLite值类型、长度和原始bytes边界；禁止`StringBuilder + Convert.ToString`造成类型/分隔碰撞和大BLOB内存放大。
- snapshot 路径必须固定在受控 `dataRoot/updates/<operationId>` 恢复区域，并绑定 operationId、source version、完整 source migrations、source/snapshot fingerprint、data-root身份、创建时间。database、data root、operation、snapshot及所有祖先拒绝 reparse/junction/symlink、UNC、path traversal、ADS。
- 本卡只保证已确认的升级前信任边界及其后的 WAL/rollback 安全，不声称识别此前已固化到 main DB 的历史污染。

### 2.3 journal、迁移、ACK 与恢复

- 阶段单向、原子持久化且重入可判定；每步绑定 operationId、程序树和数据库身份。严格 JSON拒绝重复、未知、大小写错、缺字段和伪造路径。
- 保留旧 S9-T05 journal 数字值与读取兼容；新增阶段不能让旧 `Committed(9)`、`RollbackVerified(14)` 或新 `CandidateCommitted` 被 `PendingUpdateRecovery` 误判为无需恢复/无需核验。
- `SchemaMigrationStarted` 后到 `CandidateCommitted` 前，任何不能证明安全续跑的状态默认rollback。ACK后、commit持久化前硬杀必须有唯一可证明决策；`CandidateCommitted`后、Completed前重启重验提交证据后收尾，不能误回滚。
- candidate必须在普通业务写、Reminder、Import、Task、History、Backup/Restore启动前进入受控 migration/health 模式。ACK包含完整 target migration列表、version、PID/start time、integrity/FK、核心只读和UI loaded；禁止只验count+last。
- PID/start-time必须在子进程可能写ACK前形成无空窗的可校验身份；不能接受旧ACK、伪造ACK或PID复用。
- rollback顺序强制为停止candidate并确认退出 → 恢复old完整程序树 → 恢复本operation快照 → 验source完整migration/integrity/FK/core fingerprint → 启动old验证模式并取得真实health ACK → 正常启动old → `RolledBack`。数据库恢复前绝不启动old。
- app树恢复成功但DB失败、DB成功但old树失败、old ACK失败均为 `FailedNeedsManualRecovery`；保留old/candidate/snapshot/journal/诊断，不删除journal、不无限重试、不启动不安全组合。
- restore先把已迁移main及sidecar移入本operation quarantine保留，再从已重验snapshot生成同卷staging并`Flush(true)`/hash，使用原子replace切换；最终按source完整migration/逻辑fingerprint/integrity/FK/no-sidecar验证。任一步不确定即manual recovery，禁止删除故障sidecar或用Down migration。
- 数据根级双Updater/双operation必须互斥。现有mutex按operationId命名，只能防同operation；若实现未增加同data-root全局互斥则不接受。

## 3. 构建与执行顺序

所有步骤串行；每步使用新的 TEMP/GUID结果根并保留命令、起止时间、exit code、原始TRX/JSON/log和SHA256。任一步非零或计数不符立即停止，不重复挑绿色样本。

1. 如现有 `obj/project.assets.json` 足够，先执行 `dotnet build StoreExpiryInspector.slnx -c Release --no-restore -p:NuGetAudit=false`。缺包时另行记录并按批准方式restore；离线 `NuGetAudit=false` 不冒充在线漏洞审计。
2. Release build必须0 warning/0 error。锁定新生成的测试/产品/Updater DLL SHA和嵌入source revision，后续全部 `--no-build --no-restore` 使用这一批产物。
3. 先跑S9-T07 isolation/path/permit/snapshot/journal/restore/ACK专项；确认filter实际发现数，不接受环境门禁early-return冒充执行。
4. 再跑S9-T07真实SQLite 9→10/11 test-only fixture与实际WPF/Updater隔离事务。
5. 再跑S9-T05/S9-T06/Stage8回归。
6. 最后跑一次fresh无filter Release全量；只从TRX读取 `total/executed/passed/failed/error/timeout/aborted/notExecuted`，要求全部执行且 failed/error/timeout/aborted/notExecuted/skipped均为0。
7. 全量完成后再做EF、publish、secret、diff/Git门禁；若这些发现源码问题并返修，重新build和受影响测试/full suite。

## 4. S9-T07专项矩阵

- 同Schema 9→9完整旧路径不退化；9→10成功后完整数据/BLOB fingerprint保持、目标DDL/DML事实成立、ACK与commit成立。
- migration中途异常；migration完成但ACK前崩溃；integrity、FK、core read、UI health分别失败；全部恢复old树+migration9 DB+真实old health ACK。
- snapshot创建失败、验证失败、hash/fingerprint/metadata篡改、source变化、source/target/manifest不许可，全部在migration前或安全rollback节点阻断。
- 冻结前非空WAL、SHM、rollback journal；初查后late WAL/SHM；错配合法WAL；snapshot自身sidecar；均不得被SQLite打开/回放后当作安全source。断言migration未开始、source main和sidecar保留、诊断准确。
- app树成功而DB恢复失败；DB成功而old树失败；old ACK失败；断言manual recovery且old普通启动为0。
- IOException、AccessDenied、SharingViolation；短时和永久锁；无固定Sleep掩盖、无无限重试。
- reparse/junction/symlink、UNC、相对路径、`..`、ADS，覆盖database/dataRoot/updates/operation/snapshot/journal/app/staging/old/ACK路径。
- 双Updater同operation与双operation同dataRoot；第二实例不改journal、树、DB或snapshot。
- candidate committed后Completed前硬杀；重启核验证据后保持新程序+新Schema，不恢复旧Schema。

## 5. 硬 Kill 证据（以下早期广覆盖设计已转backlog）

- 至少覆盖 snapshot创建前、写出后/验证前、验证后、migration授权前、migration DDL/DML代表中点、migration完成ACK前、ACK后commit前、CandidateCommitted后Completed前、old树恢复后DB restore前、DB restore中、DB restore后验证前、old ACK前。
- 每节点按任务要求稳定重复；必须等待产品在原子持久化checkpoint之后写独立marker，再按PID+start time硬杀。禁止仅 `Start-Sleep 500ms` 后猜测已到节点。
- 观测器只读独立marker，不持续打开/锁journal。每次核对worker已退出、journal phase、app/old/candidate完整tree fingerprint、DB完整migration/逻辑fingerprint/integrity/FK、sidecar、是否启动old/candidate及最终状态。
- 旧 `tests/S9T05-RunHardKill.ps1` 可参考循环和断言，不能直接当S9-T07阶段证据；旧脚本固定sleep后kill，且旧journal夹具可能不满足新增严格字段。

## 6. 既有回归入口

- S9-T05：`S9T05UpdaterTests`、`S9T05JournalContractTests`、`tests/S9T05-PreparerHardKill.ps1`、`S9T05-FailureMatrix.ps1`、`S9T05-DoubleUpdater.ps1`、`S9T05-LiveParent.ps1`、`S9T05-RunHardKill.ps1`、`S9T05-RunRollbackHardKill.ps1`。执行前逐个更新/核对其TEMP/GUID夹具包含新增journal字段且断言仍检查真实阶段，不能靠反序列化默认值误绿。
- S9-T06：`S9T06PendingUpdateRecoveryTests` 必跑，特别是两个Updater启动入口的外部 `WorkingDirectory`、pending多journal阻断和terminal phase分类；完整树fingerprint、旧/候选真实WPF ACK与working-directory bridge采用S9-T06-B既有隔离观测方法重新生成本轮证据。
- Stage8：先跑 `S8T02IsolationTests` 默认factory/loader零调用门禁，再按 `docs/S8-T06-REGRESSION.md` 跑S7/shared snapshot、S8-T05 corruption/restore/authoritative reads及外来WAL回归。S8-T05 mismatched WAL的pass仍只表示限制被观察，不能算S9-T07新门禁通过。
- 若S9-T07未改变普通Import/inspection/inventory事务，按S8-T06 run map跑六组代表pre/postcommit硬Kill；若diff触及共享事务/DB初始化链，扩大到相关完整crash矩阵。
- 高规模门禁按最终任务要求决定是否启用；任何启用项必须显式环境变量真实执行并保留JSON，early return不算性能或稳定性证据。

## 7. EF、生产migration与publish

- 在Release build产物上执行设计时 `dotnet ef migrations has-pending-model-changes`，必须输出模型无变化、exit0；设计时factory仍为`:memory:`。
- 执行 `dotnet ef migrations list --no-connect`，必须恰好9条，末条 `20260901155124_AddPolicyAndBaselineFoundation`；`--no-connect`不声明正式库已应用状态。
- `rg --files src/StoreExpiryInspector/Migrations`与baseline diff确认无migration10、无ModelSnapshot变化。
- 用新TEMP/GUID输出直接执行 `dotnet publish src/StoreExpiryInspector/StoreExpiryInspector.csproj -c Release --no-restore -p:PublishProfile=WinX64 -p:DebugType=None -p:DebugSymbols=false -p:NuGetAudit=false -o <TEMP/GUID>/app-publish`。核对App与`Updater/StoreExpiryInspector.Updater.exe`存在、win-x64 self-contained、多文件、无需被替换app树运行时。
- 另直接publish Updater至独立TEMP/GUID并从app外CWD运行安全usage/synthetic transaction smoke，证明外部Updater不依赖app树。
- 不运行需要`SigningKeyFile`的 `tests/S9T06-BuildRelease.ps1`，不探测生产私钥。本卡publish门禁不生成或发布签名v1.0.2资产；签名协议通过既有测试钥匙/公开公钥负门禁回归验证。

## 8. 安全扫描与最终证据

- 扫描baseline diff、repo tracked source、完整app/updater publish、测试fixture和治理证据：真实private-key/PFX、PAT/token/credential赋值、DB/Excel/Backup、用户数据、主机/账号和敏感绝对路径均0命中。仅字符串`PRIVATE KEY`测试常量不能误报为真实秘密，需记录规则。
- 发布清单逐文件记录相对路径、bytes、SHA256；确认test-only migration10/11 fixture、fault injection、S9T05_TEST代码、TRX/JSON、DB/snapshot/journal均未进入正式publish。
- 核对公开v1.0.0/v1.0.1 tag/四资产/latest仍不可变，只做匿名只读验证；不创建/修改Release，不发布v1.0.2。
- `git diff --check`通过；最终治理提交前后分别记录status、HEAD、origin/main和ahead/behind。只有生产/测试冻结、全部门禁通过后才能写技术接受与Stage9 closeout；普通push后再核对clean/0/0。

## 9. 立即停止条件

出现以下任一项即 `NOT_ACCEPTED` 并保留失败现场：未知sidecar先被checkpoint/打开/回放；old可能在DB恢复验证前启动；snapshot验证弱于Stage8或无法绑定operation/source；journal无法区分未提交与已提交；程序树/DB可能形成混合版本；硬Kill只靠时间猜测；需要真实migration10、正式DB、私钥、提权或降低既有S9-T05/S9-T06/Stage8门禁。

## 10. 最终实际 WPF / Updater / 旧版 ACK / hard-kill 可复用接线（2026-09-05 静态设计，尚未执行）

本节是 Terra 冻结后的独立执行设计，不是测试通过记录。静态审阅快照：`App.xaml.cs=2D6B380E3C2DCC5E96F643ACC81537B9DAE9360CA53546B26F8F630E6AFD8AEE`，`UpdateInstallationPreparer.cs=45B5BD7962BF47BB63B78AE589C2671A18C9F79F7A245644E661380C42F836B1`，`DatabaseInitializer.cs=F52B9653423981B0A780BB791D479F52767152EAD22CF2BE07391BF209C9BC8C`，`Updater/Program.cs=2D8BC490C63ECEDDBB773F38FFE03F0D24039AD92230D4333DEB3FC30B8201E1`，`S9T07Fixture/Program.cs=C52FA064884B36C6B2655BF84C35A6374A78AC7A11003F83C7E44237E334A53C`，`S9T07SchemaUpgradeSnapshotTests.cs=3F514F65EC9A71537D762D12B512B91585711AA3E036C8DFCE337E55AE3B8237`。Terra 后续变更这些文件时，最终 Sol 必须重读并重记 hash。

### 10.1 现有入口及其证据上限

- `tests/S9T01-PublishSmoke.ps1`（`4B8754DCCD3C9832AD61BB19976F49191EA57594240BF2E81E495CF19206704F`）已给出生产 App publish 后的隔离 WPF 启动模板：新 TEMP/GUID data root、app 外 working directory、`--data-root <root> --s9-t01-smoke-exit`、真实 `MainWindow/Shell` 初始化、程序树前后 hash 不变。它适合作为最终生产 App 1.0.2 的构建产物/壳层 smoke，不进入更新 maintenance，也不证明 Updater 事务。
- `.ai-dev/ACCEPTANCE/S9-T06-GUI-SOL-VERIFY/Invoke-GuiCandidate.ps1`（`EEEF1B18720235C4472D40011B4F07FF4DE0F5B4FFC64EE3D2D7DFA4589A9B33`）能在真实生产 WPF 上通过 UI Automation 点击“立即更新”，从而走 `InstallPreparedUpdateAsync` 之前的真实下载/准备 UI，并使用 `--s9-t06-prepare-only` 禁止安装。其结果明确 `installationConfigured=false/updaterStarted=false`，只能复用 GUI 和 maintenance 入口模式，不能冒充 S9-T07 外部 Updater 事务。
- `tools/Start-S9T06-100-Bridge.ps1`（`B9EC928F00D19100A0B4088519D895A750D527E925F6A451C3D634863EF92AF1`）只接受已冻结 public v1.0.0 树及固定 hash/version。它用于 S9-T06 历史 bridge 回归；不能作为本卡“当前生产 old9”或新跨 Schema ACK 证据。
- 现有 `ExternalUpdaterCompletesSchemaFixtureMigration` 是真实外部 test-updater + WPF fixture + SQLite DDL/DML，但 active old tree只有 `source.txt`，journal写 `SourceVersion=1.0.0/TargetVersion=1.0.2`，candidate fixture ACK也写1.0.2。它证明协议 fixture，不证明当前真实 1.0.2 生产树回滚或真实版本升级。
- `tests/S9T05-RunHardKill.ps1`（`6EDAD5919D2B1231712209BFF71DC80D502F2BB45C05035E662FBB2C07A6E69A`）及同组脚本用伪树、旧journal和`Start-Sleep 500ms`后猜测kill点。只借用矩阵循环和最终断言结构；不得计入本卡 hard-kill 证据。

### 10.2 三种树和版本必须分账

1. `old-prod9`：从最终冻结 SHA fresh Release publish 的真实 `StoreExpiryInspector` 1.0.2 完整树，生产 migration恰好9。它是跨 Schema fixture 事务的 active old tree，也是rollback后真实旧版 WPF/schema health ACK执行体；记录exe Product/File/AssemblyVersion、完整tree fingerprint、publish manifest和源SHA。这里的“old”指schema9/切换前树，不虚构为历史1.0.0。
2. `candidate-fixture10` / `candidate-fixture11`：仅测试工程产出的WPF候选树，追加真实测试DDL/DML且不进入生产publish。其migration身份可作为target10/11，但当前程序集没有独立版本且ACK硬编码1.0.2。最终证据必须明确“test-only schema target”，不能称作真实生产1.0.2升级。若legacy journal要求版本递增，fixture应在测试工程声明独立可核对版本（例如1.0.3-test产物对应规范化1.0.3）并从实际exe读取写journal/ACK；不能继续手写`source=1.0.0,target=1.0.2`。
3. public v1.0.0/v1.0.1冻结树：仅做S9-T06既有bridge/rollback回归。旧二进制没有本卡schema authorization/完整migration ACK协议，不承担本卡old-prod9验证。

### 10.3 当前阻塞真实 old9 ACK 的可触发缺口与最小修复

- **P0（真实rollback验收接线不可达）**：test-updater的`SchemaVerificationArguments`无条件按journal原始target与source的差值附加`--s9-t07-fixture-target 10/11`。candidate阶段可用；rollback启动已恢复的old tree时仍传同一fixture参数。真实生产App的`RuntimeDataRoot`拒绝该未知参数；若old tree也放fixture，它又会按10/11重做迁移，不能验证source9。因此当前没有“fixture迁移失败后由真实old-prod9产生新协议ACK”的可执行路径。最小修复只在`S9T05_TEST`接线显式区分candidate verifier与old verifier：candidate fixture传target10/11；old-prod9只传`--data-root <TEMP/GUID> --allow-existing-isolated-data-root --s9-t07-verify <operation> <token>`，不得从migration数量猜执行体。增加外部失败事务，active old tree复制完整old-prod9 publish，staging为fixture10，migration确实落到10后让ACK失败，断言tree恢复、snapshot恢复9、真实1.0.2 App identity/authorization/loaded/core-read/完整9 migrations ACK、再正常启动old。
- **P1（测试隔离路径副作用）**：`UpdateInstallationPreparer.DeclaredSourceMigrations()`调用无path的`DatabaseInitializer.CreateContext()`；该函数先取`RuntimeDataRoot`默认路径并`Directory.CreateDirectory`。`PrepareForTest`虽显式传入TEMP/GUID dataRoot，却没有把全局`RuntimeDataRoot`绑定到它，因此测试进程可能在默认用户data目录创建目录。这里虽未调用`Open/Migrate`，仍违反“测试不探测/创建正式根”的边界。最小修复用现有`StoreDbContextFactory`同型的`:memory:` options仅读取EF metadata，或提供纯`DeclaredMigrations()` helper；不得从默认database path派生。增加隔离测试，在fresh子进程中把可观察默认路径设为不存在/禁止位置，调用`PrepareForTest`仍成功且该路径保持不存在。`App.StaticMigrations()`也应复用同一纯metadata helper，以便“candidate身份写入前不接触SQLite”能由代码结构直接证明；当前App路径已先配置隔离root，风险低于Preparer测试，但没有必要保留目录副作用。

### 10.4 最小实际事务接线

- 每个case创建彼此独立的直接TEMP/GUID `runRoot`、`installRoot`、`dataRoot`、`resultRoot`；禁止沿用测试进程的默认LocalAppData。先将old-prod9完整publish复制为`install/app`，用old-prod9在该dataRoot执行一次隔离初始化/壳层加载后正常退出，得到真实schema9合成库；随后写入BLOB、历史、设置等代表数据并记录文件SHA、完整逻辑fingerprint、migration列表、integrity/FK。正常关闭后确认无sidecar。
- 候选包只包含fixture10/11树；manifest、source许可、target完整migration和tree fingerprint均从本case产物生成并绑定。调用必须经过真实App的`BeginDatabaseMaintenanceAsync → UpdateInstallationPreparer.Prepare → 外部Updater`。没有用户GUI时，最小新增test-only入口应只复用同一个`InstallPreparedUpdateAsync`委托，并接收已在TEMP/GUID内生成且已签名测试钥验证的`VerifiedUpdatePackage`；它不得跳过maintenance、Preparer、父进程退出和Updater复制。S9-T06的prepare-only入口不能替代这一段。
- `old-prod9`普通App/验证App、fixture candidate、独立Updater均从app外working directory启动。runner记录exe绝对路径、PID、process start time、参数、tree hash和退出码。成功case要求candidate真实WPF `Window.Loaded`、完整target migration ACK、CandidateCommitted持久后普通candidate WPF loaded；rollback case要求candidate停止、old tree恢复、snapshot恢复与验证后才启动真实old-prod9 verify，再取得完整source9 ACK和普通old WPF loaded。
- 当前fixture自己`VerifyFrozenSource`后又在`FixtureMigrations.Apply`另开SQLite连接。最终冻结版本若要求连续接管，fixture必须与生产candidate一样使用takeover返回的同一已开连接并在该连接事务中执行DDL/DML；否则fixture不能证明release→first writable open窗口已闭合。

### 10.5 14节点marker / kill / 恢复矩阵（DEFERRED / NON_BLOCKING backlog，非本轮门禁）

接续执行限定：下面旧草案中的 `Kill(entireProcessTree:true)` 不作为默认清理方式；只终止按PID/start/exe核验的实际actor，fixture/probe等子进程逐一按记录清理。无kill的单次成功事务应只Start一次；涉及Start与身份落盘之间硬杀的重入，验收要求最多一个具备业务写授权的正常实例，不能将OS进程创建与文件写入视为原子操作而宣称绝对只创建一次进程。所有退出/清理失败保留诊断；marker必须证明实际actor已观察对应状态，测试端只看到另一进程写出的identity不等于Updater已观察它。

每个节点至少3次独立fresh case；任一次非预期即保留该case并停止该节点，不追加重跑挑绿色。marker必须由到达该节点的实际actor在前置状态已通过`DurableFile.Replace`并`Flush(true)`之后写入独立`checkpoint-<name>.json`，然后仅在test build等待；observer不得长期打开journal/DB。marker至少含`operationId, checkpoint, actor, pid, startedUtc, journalPhase, schemaPhase, createdUtc`，并按节点附source/snapshot/tree/authorization/ACK hash。observer严格解析并核对operation、checkpoint、PID/start、actor exe路径和进程仍存活，再对这一精确PID执行`Process.Kill(entireProcessTree:true)`、等待退出并验证PID/start对应进程消失。随后清除单个checkpoint注入，以同一operation journal从app外CWD启动独立Updater恢复。

| # | marker（实际actor） | marker前必须已经持久成立 | kill后核心观测 |
|---:|---|---|---|
| 1 | `SourceVerified`（Preparer/父App） | source version、完整source migration、许可与actual DB绑定 | 无candidate schema写；old-prod9/tree/DB/fingerprint不变 |
| 2 | `SnapshotPreparing`（Preparer） | preparation journal为SnapshotPreparing | 重入只重建/验证本operation snapshot；未授权candidate |
| 3 | `SnapshotVerifiedBeforeJournal`（Preparer） | snapshot文件+metadata SHA/fingerprint已验证，正式journal尚未提交 | old不变；残留snapshot可识别，不能出现可运行半journal |
| 4 | `MigrationAuthorizedBeforeCandidateOpen`（candidate） | identity、updater PID/start绑定authorization均durable；SQLite尚未首次open | kill后rollback，不得出现migration10/未知WAL被接受 |
| 5 | `MigrationTransactionAfterDdl`（candidate） | 同一SQLite transaction内真实DDL已执行、commit未发生 | 进程kill造成事务回滚或Updater走snapshot rollback；最终source9 |
| 6 | `MigrationTransactionAfterDml`（candidate） | 同一transaction内代表DML/BLOB事实已执行、commit未发生 | 同上，并核对原BLOB/历史/设置fingerprint |
| 7 | `MigrationAppliedBeforeAck`（candidate） | migration transaction已commit、target10/11可读，ACK尚不存在 | 必须rollback snapshot9，不能启动old直读target DB |
| 8 | `AckPersistedBeforeCandidateCommitted`（Updater或candidate协议点） | 严格完整ACK已durable且candidate退出，CandidateCommitted尚未持久 | 恢复决策唯一且可解释；按最终状态机要求完成提交或安全rollback，不得混合 |
| 9 | `CandidateCommittedBeforeNormalLaunch`（Updater） | journal已durable CandidateCommitted，普通App尚未启动 | 重启重验提交证据并保持target schema；不得回滚old9 |
| 10 | `OldAppRestoredBeforeSnapshotRestore`（Updater） | candidate停止，old完整tree已恢复/hash通过；DB仍可能target | kill后绝不启动old；重入先restore snapshot |
| 11 | `SnapshotRestoreStaging`（Updater/Restore） | durable restore copying intent，staging只写入部分或尚未验证 | 重入隔离partial staging、从已验证snapshot重建；保留quarantine |
| 12 | `SnapshotRestoreReplacedBeforeJournal`（Updater/Restore） | 原子replace已完成、main为source snapshot，SchemaPhase尚未SnapshotRestored | 重入验证main/source身份后推进；不能重复破坏或遗失故障证据 |
| 13 | `OldVerifierIdentityBeforeAuthorization`（真实old-prod9） | 真实1.0.2 exe已写identity，SQLite尚未open，authorization尚未写 | kill后不接受旧identity/ACK；新PID/token重启验证，DB保持source9 |
| 14 | `OldAckBeforeRolledBack`（Updater或old ACK协议点） | 真实old WPF loaded/core-read/integrity/FK/完整9 migrations ACK durable，RolledBack尚未持久 | 重入严格重验ACK/PID与source DB后只启动old；不得再启动candidate |

所有case的最终JSON同时记录：journal和schema终态、每次durable phase序列、old/candidate/current tree逐文件fingerprint、snapshot/restore-state/quarantine SHA、DB文件SHA与逻辑fingerprint、完整migration、integrity/FK、sidecar集合、authorization/identity/ACK原始SHA、启动进程清单和normal-loaded marker。成功节点9要求正常candidate恰好一次；rollback节点10–14要求DB恢复验证前old启动计数为0、真实old verify ACK恰好一次、之后普通old启动恰好一次。marker只是确定kill位置，不能代替状态、数据、进程和树的最终断言。
