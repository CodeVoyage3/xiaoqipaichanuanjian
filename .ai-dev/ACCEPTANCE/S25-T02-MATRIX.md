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
# S25-T02｜真实旧版本升级矩阵

状态：IN_PROGRESS / WAITING_USER_MANUAL_ENV / BLOCKED_BY_TEST_ENV / NOT_ACCEPTED。
TEST_CANDIDATE_SIGNING=PASS（用户已接受，独立于矩阵）。FULL=NOT_RUN / NO_FULL；S25-T03=NOT_STARTED；S25-T01原GUI PASS/CLOSED保留。

证据根：`C:/Users/39037/.codex/visualizations/2026/09/18/01a0b27a-1980-7df2-93cb-55b5614a7c45/s25-t02-evidence/sandbox-matrix`。主结论`final-matrix.json`；来源哈希`input/expected-source-binaries.json`；原始输出`output-r4`。实现dc1005298ac0fbf3d0ee4a9380d82912deff0427，专项证据80805377a5d1f0b8974174ccca42cd0ce823a239，前序5647c58f77c3cdfc9848639d1386c171ae1ad6ac。Fresh Terra实施，Sol独立验证；未修改生产App/Updater/Installer/schema/正式Version。

| 真实来源 | Setup直接覆盖 | Online正常直升 | Online失败恢复 | Online完整门禁 |
|---|---|---|---|---|
| v1.1.0 | PASS | PASS | PASS：候选验证进程ACK前终止后RolledBack(15) | PASS |
| v1.1.1 | PASS | PASS | BLOCKED：AccessDenied，FailedNeedsManualRecovery(16)，预设故障尚未注入 | BLOCKED / WAITING_USER_MANUAL_ENV |
| v1.1.2 | PASS | PASS | PASS：实际AccessDenied后RolledBack(15)，旧App健康及指纹保留；预设进程故障未注入单独BLOCKED | PASS（实际自然故障恢复证据） |
| v1.1.3 | PASS | PASS | PASS：候选验证进程ACK前终止后RolledBack(15) | PASS |

有效Setup：`output-r4/{version}-Setup`，四个真实正式旧安装器。有效Online：1.1.0/1.1.3使用`{version}-Online-r8`；1.1.1/1.1.2使用`{version}-Online-r9`。旧App/Updater安装后哈希逐一匹配已验证正式ZIP，操作目录复制Updater哈希及实际PID/路径/命令行均保留。没有重编译、当前或TEMP Updater替代。早期TEMP/TestAppId Setup仅为支持证据，不替代本矩阵。

独立Windows Sandbox账户WDAGUtilityAccount；DataRoot=`C:/Users/WDAGUtilityAccount/AppData/Local/StoreExpiryInspector`；正式AppId只在VM中使用。只映射只读测试输入及可写证据输出，不映射宿主机正式安装、LocalAppData或manifest私钥。合成最小商品数据；每条路径独立安装旧版本、建立指纹后直升，不先逐版本升级。

四条Setup全部证明：来源版本识别、minimumDirectVersion=1.1.0、安装事务完成、启动正常、升级后业务指纹一致、integrity=ok/FK=0/完整有序10条migration一致；目标首次启动前数据库字节不变；旧Setup降级被拒绝(exit1)，数据库及安装树不变。

四条正常Online全部Completed(10)，目标99.25.2、journal包SHA与冻结ZIP一致；健康ACK migration10/coreRead/uiLoaded；正常窗口；指纹保持、integrityok/FK0/完整10migration。`output-r4/normal-loaded-observations/{operationId}.json`捕获Loaded state2，PID匹配正常启动记录。Same-Schema的Schema=null，外层Committed(9)->Completed(10)；没有伪造独立SchemaPhase.CandidateCommitted或migration11/跨schema快照。

v1.1.2自然回滚operation=`ade417fb-b727-4207-a641-c0760aa1a0f1`，Phase15，LastError=Access to the path is denied；旧健康ACK version1.1.2/migration10/coreRead/uiLoaded；正常旧窗口。回滚前后指纹均`a3e77e413f406921e72b054438588240c02b21fc09e93a2b14bb83568b5586ed`，integrityok/FK0/完整10migration。预设kill未注入的测试断言保持BLOCKED；实际自然故障恢复单独PASS，没有改成“受控kill PASS”。

## v1.1.1真实阻断

恢复operation=`1371514f-a1a9-4315-a66d-626d8592f619`；真实Updater PID5664，DLLSHA=`076520E9707EAEE5F0B9A89DEEE49CF848EB746F15561870EB262520D998CA75`；观测Phase0->16、CandidatePid0、Schema=null。LastError=`2026-09-18T04:55:39.5057272+00:00 UnauthorizedAccessException: Access to the path is denied.`。尚未进入候选验证进程故障注入。

分类E_TEST_ENVIRONMENT_IO_ACCESS_DENIED_UNRESOLVED：具体拒绝文件/占用者尚未定位，旧Updater恢复缺陷亦未排除。登记的是恢复测试阻断，不是已证明的协议breaking、目标兼容修复、必须桥接或整个版本“不兼容”。v1.1.1正常直升独立PASS；恢复未通过，不降低证据标准。

人工复核入口`manual-v111-recovery.wsb`：新Sandbox自动准备真实旧1.1.1、合成数据/指纹、私有HTTPS源及已核对身份的候选进程故障观察；用户只点击真实旧App的“立即更新”。独立输出`output-user-v111/manual-result.json`；PASS仍需真实旧Updater、终态15、旧窗口/ACK、完整10migration、integrity/FK和一致指纹。该入口未自动启动，不修改签名或生产验证。

## 历史失败/无效尝试均保留

所有probe/r1-r9文件保留，以下均分类E，不算兼容性PASS：

- VM继承不可达127.0.0.1:7890代理，hosts未被DNS采用；只在VM采用回环HTTPS CONNECT源，严格404预检通过才执行。DNS CIM无效类改用原生ipconfig。原客户端TLS、签名、包和身份检查保持启用。
- WindowsPowerShell5.1对无BOM UTF8的中文按钮匹配读取错误；修正BOM，见`automation-encoding-root-cause.json`。
- 卸载遗留app.old/app.staging及故障注入DENY_EXECUTE；`Control-11-InspectIsolationAndStop.log`证明目录和ACL残留。只在独立场景之间对已校验的WDAG合成测试根回收，不在真实升级过程清目录。
- 旧协议观察器未允许FileShare.Delete，干扰journal原子替换；已停旧进程，全部活动协议读改ReadWrite|Delete、复制已读缓冲。`observer-share-delete-check.json`证明读取期间replace/delete成功。r6/r7相关错误不是生产结论。
- r8未完成注入的v1.1.1故障进程跨场景终止v1.1.2候选：v111故障标记operation=`fdf34db0-bf5d-4abf-ad6b-c73dd144bd5c`与v112正常升级journal一致。停残留进程，新增reset/finally回收，仅补跑缺口行。受影响r8行保持无效。

## 门禁与minimum结论

A Generation/Builder=PASS（Sol50条，历史条目不变、minimum继承、maximum仍previousRelease、无证据收窄拒绝、新generation及正式验证证据门禁）；B真实Setup4源=PASS；C正常Online4源=PASS/失败恢复3源PASS+1源BLOCKED；D边界=PASS（保留6条签名拒绝、Sol46项安全专项、production trust1/1、四个真实旧App篡改manifest拒绝无ZIP/安装事务且指纹保持、四源Setup降级保护）。合成边界不冒充真实旧二进制矩阵。

E完整generation已验证minimum=NOT_VERIFIED_PENDING_RECOVERY_GATE。Setup已验证minimum=1.1.0；Online数值候选minimum=1.1.0，完整恢复门禁未闭环。两者计算数值一致，验证状态不一致。v1.1.0暂不能升级为完整已验证generation minimum；契约minimumStatus仍CANDIDATE_NOT_VERIFIED，无提高minimum、新generation或桥接。A-E尚未全部闭环，不提前USER_GOVERNANCE_PENDING/CLOSED/ACCEPTED。

## Sidecar / WAL / journal最终风险

四源有效轨迹均实际观察app.db、app.db-wal、app.db-shm。采样未见app.db-journal，不证明永不出现。成功直升及已验证恢复均完整10/integrityok/FK0/稳定指纹。旧v1.1.2正式源码e758563b51d0222ac7a3518ca83e40954be5001e（保留`released-v1.1.2-Updater-Program.cs`）枚举后直接File.GetAttributes，无sidecar消失容错；正式v1.1.3源码280c86f2f30686092f9e603a9bbb8ac593355a27增加容错。老Updater潜在时序风险仍保留，单次矩阵不证明所有竞态消除；本次AccessDenied不是FileNotFound，未归因WAL。目标代码不会回溯修补执行中的旧Updater。

v1.1.1失败journal保留，自动恢复尚未验证；未手改journal、绕签名、放宽manifest或替换生产Updater。

## 99.25.2候选身份/处置

PRIVATE TEST CANDIDATE，正式manifest签名RSA3072/PSS/SHA256/tamper/install-revalidate已由用户接受。正式DPAPI私钥未导出/复制/修改/映射Sandbox；VM仅临时TLS证书，不是替代manifest签名身份。

| 文件 | 冻结及最终SHA256 |
|---|---|
| ZIP | 1e3e610630ba0ad3d90fb9f0b849507e57f53748c549a2679029c754ef3f2c63 |
| Setup | 049e95c09b4ba12f8b2e859f88806ce3ef94a5f30e91e19a850e8ea75f688bbd |
| manifest | d231ae0b9472fd6cc6a0d7cc952af2a04b1044dd3235a1bd10c9c76dead16559 |
| sig | b773b082954f06fec47a628da50b5a1d5a624df781ba9ce9ecfbafe0bd20d8fc |

处置PRIVATE_RETAINED_FOR_EVIDENCE_AND_V111_MANUAL_REVIEW，final hashes与frozen一致。未发布/上传、无正式tag/Latest/公开地址/门店交付/正式资产替换，正式v1.1.3及原dirty工作区未动。

Online真实性范围REAL_RELEASED_APP_AND_UPDATER_PRIVATE_SANDBOX_FEED：回环TLS代理提供原客户端既有URL形状、夹具metadata和冻结签名原始字节；真实旧App/Updater及协议原样执行。这不是公共GitHub下载线路验证，metadata夹具不是新正式Release。
