# S9-T07 升级保护快照信任边界

2026-09-05 用户已确认本文件边界并授权继续实施，解除 PAUSED_PRODUCT_REVIEW。`S9-T07 = IN_PROGRESS / NOT_ACCEPTED`，`Stage9 = IN_PROGRESS / S9-T07_CURRENT`。恢复时重新 fetch，origin/main=c4f7618c0dbdc0996ddfc183b9cb8e2cbf9d3803。以下是产品契约，不是已经实现或通过验收的声明。

## 准确能力表述

> S9-T07 guarantees upgrade/rollback safety from a verified pre-upgrade trust boundary; it does not provide forensic detection of historical contamination already committed into the main database.

S9-T07 从本次升级事务开始前已经建立并验证的可信状态承担升级与回滚保护责任。不追溯在该边界之前已由 SQLite 合法回放并固化到主库的历史污染；这不意味着当前业务数据一定正确，也不意味着可以识别所有历史外部篡改。

未来如需可信基线、数据来源证明或审计链，作为独立能力处理；不阻塞 Stage9，不在本卡创建后继任务。

## 建立可信状态的硬门禁

1. 已进入 maintenance，阻止新的业务操作；等待本软件所有权威业务写完成，Reminder/background 等受控写入者停止。
2. SQLite 连接正常关闭。关闭过程中不能通过自动或显式 checkpoint 将未知 WAL 静默吸收后冒称干净。
3. 快照前不得存在非空或来源不可证明的 WAL。WAL/SHM 状态不满足冻结安全规则即 fail-closed；禁止直接 checkpoint 未知 WAL 后继续，禁止删除或忽略未知 sidecar 以制造通过。
4. 本次可信边界建立期间残留或新出现、来源不可证明的 WAL 必须拒绝，不能静默吸收；文件身份或路径异常同样阻断。
5. 应尽可能取得数据库独占访问证明；maintenance 计数本身不是跨进程文件独占证据。独占、连接关闭及迟到文件的检查应有真实自动化验证。
6. 保护快照验证完整 source migration、integrity、FK、核心逻辑 fingerprint 与 operation 身份，绑定 operationId/source version/data root 身份/创建时间/hash。快照创建或验证失败禁止 migration；篡改快照禁止恢复。

从可信冻结点到快照、migration 授权、候选健康、提交或失败恢复完成，持续使用受控身份与持久 journal。不得将某一次文件存在检查视为整个事务的来源证明。

## 恢复与既有能力复用

`PreImportSnapshotService` 的 SQLite `BackupDatabase` 和结构验证可作为复用基础，但它自身使用普通只读连接，不能在未知 WAL 存在时直接调用后补 metadata；事后 fingerprint 只能绑定已读到的状态，不能证明 WAL 来源。

普通 `DatabaseRestoreUseCase` 按当前应用 migration 验备份，并先要求保护健康的当前库。它不能直接承担 migration 中途损坏或已变为新 Schema 时的升级回退。升级专属恢复应复用适用的底层验证/安全文件操作，保留故障 DB/sidecar 和 journal，在 source Schema、integrity/FK/核心数据、完整 old tree 验证完成后才能启动 old，再取得真实 old health ACK 才记 RolledBack。普通 Restore 业务规则不变，不使用 Down migration。

## migration 许可的信任边界

实际 source 完整历史必须匹配旧程序声明；source 是候选静态完整 migration 序列的严格前缀；候选序列与已签名 manifest 的 targetMigrations 全序列相等，同时满足 source version/migration 范围。重复、删除、重排、未知 source、manifest/candidate 分叉或缺失全部拒绝。保留同 Schema 安全路径。

仅 strict prefix 无法推断从未见过的未来 migration 应叫什么名称。未来目标权威来自可信签名发布及候选实际静态序列；测试中的跳号、10A、缺失10等拒绝须绑定独立 fixture 预期序列。不能靠字符串前缀单项冒充三方全序校验，也不另发明长期 migration catalog。

## 既有局限与本轮证据

Stage8 S8-T05/S8-T06 已保留结构合法外来 WAL 来源无法验证的历史限制，不改写旧断言和失败记录。独立 Sol 本轮用现有 Release 产物在 TEMP/GUID 执行精确单项，1/1 复现限制：12,392-byte 合法外来 WAL 被接受，integrity/FK/migration 健康，业务指纹改变，provenanceProtected=false。

TRX 与原始 JSON hash 已由协调者再次核对，详见 `../ACCEPTANCE/S9-T07-WAL-PREFLIGHT.json`。这不是 fresh build 或 S9-T07 防护验收通过。用户已裁决该历史污染不属于本卡追溯范围，不能再以缺少历史审计能力阻塞实施。

## 实施与验收约束

Terra 负责生产与实施测试、提交后停止不 push；Sol 独立完整 diff、全部 Task A～R/硬 Kill/WPF/Updater/同 Schema 回归、fresh 无 filter Release、build、EF/migration9、publish、secret/Git 门禁。全部使用 TEMP/GUID 合成数据，不访问正式安装/DB，不引入生产 migration10，不发布 v1.0.2，不创建 Stage10。未经完整技术验收不关闭 Stage9。
