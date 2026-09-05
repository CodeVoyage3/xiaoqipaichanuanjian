# S9-T07 Sol 独立技术验收计划

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
- 快照验证不得弱于 Stage8：source 与 snapshot 的完整 schema/table/column、完整 migration、integrity、FK、page/size、全业务字段及 BLOB 逻辑 fingerprint、文件 SHA 均可核对。
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

## 5. 硬 Kill 证据

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
