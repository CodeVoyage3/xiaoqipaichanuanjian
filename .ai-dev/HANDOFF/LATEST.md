# 最新交接

## S8-T06正式开工（2026-09-04，覆盖下方历史停止点）

- 用户批准Stage8最后一张卡S8-T06；IN_PROGRESS / S8-T06_CURRENT / NOT_ACCEPTED。已重新fetch，main=origin/main=f9812110d8616801852c56aca40b802f3c578438，clean、0/0；T01～T05 CLOSED。
- Task/Acceptance已建立；派发全新Terra medium/priority复用最终回归设施，默认不改生产；Sol独立验收。仅TEMP/GUID，不访问正式库。保留WAL来源与坏库无法保护的能力限制，不创建Stage9/升级/重置/Undo。
- 全部门禁通过才关闭Stage8；当前尚未取得本卡新鲜结果，不沿用旧数字预报通过。

## S8-T05正式关闭（2026-09-04，覆盖下方历史状态）

- S8-T05 TECHNICALLY_ACCEPTED / CLOSED；最终代码/测试e0e5a0dc6eaba2d347246daa571a35cbbbcf3004。Stage8仍IN_PROGRESS，S8-T01～T05关闭，等待后续授权；不创建S8-T06。
- Sol最终无filter Release984/984、0failure/skip、exit0，23m36s；独立专项100/100；新鲜48/48硬Kill（排查15/库存9/10k导入18/100k导入6），36提交前回滚、12提交后保留，全部integrity ok/FK0/migration9，48原worker均退出。
- build0warning/0error、EF无漂移、migration9末条20260901155124_AddPolicyAndBaselineFoundation；完整diff和diff --check通过，无Schema/ModelSnapshot/index/生产PRAGMA/dependency/csproj/slnx变化。源码最终仅已有库预检、启动失败终止和Restore测试接缝；最后两个治理前修复为旧换行断言及marker共享冲突测试同步。
- 安全边界：坏备份拒绝；严重坏当前无法保护时Restore安全阻断、不覆盖。合法外来WAL可在结构健康时改变业务状态，来源防护未实现，作为用户接受的known limitation保留；不新增provenance/sidecar删除/自动恢复模式。
- 真实100k Batch/300k History，DB和backup225427456bytes。原backup7258ms/restore23089ms保留；最终新样本6946ms/22552ms，额外integrity/FK2275ms，完整指纹一致。均单样本非SLA。
- 旧982/983全量、WAL原4/5及marker失败均保留，不改写为通过。详见S8-T05 Acceptance与S8-T05-CORRUPTION-RESULT.json（原始大DB只留TEMP）。未访问正式数据库，不声称物理硬盘/SSD/文件系统/真实断电安全，不冒称真实GUI验证。
- 此治理提交后按授权普通push main并核对clean/0/0；最终main SHA以提交/推送回执与最终回复为准，验收后不改生产/测试。停止，不启动S8-T06/Stage9/在线升级/重置/Undo。

## 本轮恢复验收过程（历史）

- 用户正式接受结构合法外来WAL来源不可识别为已知能力限制，不新增防护；旧失败及业务漂移事实保留，不算防护成功。S8-T05 IN_PROGRESS / NOT_ACCEPTED，待最终HEAD新鲜无filter Release全量0failure后关闭。
- 当前最终候选e0e5a0dc6eaba2d347246daa571a35cbbbcf3004；ad7ec3b全量982/983因旧marker共享冲突失败，已仅修复测试同步并新增无数据库专项。Sol最终专项100/100；新的无filter全量启用既有100k硬杀，正在运行，不能预报通过。
- 已重新fetch，origin/main仍80b2c57；本地已知aa27b18及治理/测试返修均保留。接续本卡Terra仅调整测试契约并提交，Sol独立验收，不代写生产代码。禁止正式库访问及新Task/Schema/PRAGMA/防护模式。

## 历史停止点（裁决前）

- S8-T05 = PAUSED_PRODUCT_REVIEW / NOT_ACCEPTED。真实非空外来WAL导致商品指纹漂移，但SQLite integrity ok/FK0/migration9且Initialize接受。Sol独立WAL4+UI1：4/5，mismatched失败，证据见S8-T05 Acceptance；不得改断言冒充安全。
- 用户header坏库无法保护时安全阻断裁决已落实；不解决本次结构健康的外来WAL来源错配。需独立明确风险边界/防护契约，不自行删sidecar、拒绝正常WAL、改Schema或PRAGMA。
- 无filter Release983总计982通过1失败（旧UI换行断言）；仅输入换行正规化后的专项通过，但随后真实WAL失败，未再声称全量通过。build0/0、EF无漂移、migration9、禁止项diff无变化、diff --check通过。
- HEAD aa27b1838759a39a18c0d2147f2fec78a12eb602；已知origin/main80b2c57，9/0，未push。治理及S8T05/Stage4测试返修未提交且保留；无reset，无生产数据库访问。两名实施者已停止，Sol不代写生产代码。不创建S8-T06。

## S8-T05正式开工（2026-09-04）

- 当前S8-T05：IN_PROGRESS / NOT_ACCEPTED；Task/Acceptance已建。开工重新fetch main=origin/main=80b2c57f599fec736a0e191b13e8ae923810a633，clean、0/0。全新Terra `/root/s8_t05_fresh_terra` medium/priority实施中；中间提交6b5a726/2568037，未push；Sol独立Release新测试17/17仅局部覆盖，不是整卡验收。详见Acceptance中间记录，继续完成剩余矩阵。
- 用户裁决：严重坏当前库无法生成合格保护快照时必须安全阻断，不staging/replace、不删改坏文件、不损坏健康备份、不伪造成功记录；属于预期，不要求现有Restore救援无法保护的坏库。不得绕过保护或新增Restore模式。
- 其余隔离损坏/备份拒绝/健康恢复/失败回退/大库/Release门禁按S8-T05 Task。仅TEMP/GUID合成数据，严禁正式库访问。S8-T01～T04仍关闭；下方旧停止点为历史回执。
- 实施交接：旧`s8_t05_fresh_terra`因未完成矩阵及WAL证据缺陷已停止；全新`s8_t05_completion_terra` medium/priority接续同卡，保留旧commit/diff，未push。详见Acceptance开发记录。
- 不创建S8-T06，不实施Undo/重置数据/Stage9/在线升级。完成独立验收后才关闭、普通push main并停。

## S8-T04正式关闭（2026-09-04）

- S8-T04：`TECHNICALLY_ACCEPTED / CLOSED`。Stage8仍IN_PROGRESS，S8-T01～T04关闭，WAITING_NEXT_AUTHORIZATION；S8-T05未创建。
- 本轮恢复核对：origin/main2142cf7、本地治理bfa87d9、clean、ahead3/behind0；实现9eff272，代码/测试均未变化。旧本地main61a057e仅落后、无分叉，归档只作正常fast-forward。
- 用户仅为Release全量特别放行4类既有TEMP/GUID SQLite损坏回归。Sol无filter全量944/944、0failure/skip、exit0，12.1949分钟；PreImportSnapshotServiceTests 6/6、S7T02DatabaseRestoreTests 9/9、S7T03DatabaseBackupRestoreViewModelTests 30/30、ImportUndoEligibilityTests 42/42，全部87项真实通过。不是旧838/838过滤回执。
- 既有48/48 Process.Kill矩阵及TRX哈希保留不变：36次提交前完整回滚、12次提交后完整保留，全部integrity ok/FK0/migration9/可重开可写。本轮没有额外重跑21分钟的100k专项；标准全量中的常规用例按原实现执行。
- Sol新鲜Release build0warning/0error，EF无漂移；migration --no-connect仍9，末条20260901155124_AddPolicyAndBaselineFoundation。生产、Schema/ModelSnapshot/index/PRAGMA策略/dependency/csproj/slnx不变，diff --check通过；未做在线NuGet漏洞审计。
- 未访问、复制、哈希、损坏、恢复或调查正式库；损坏仅发生在获准既有测试的TEMP/GUID对象。不新增损坏场景、不恢复Undo规划。未确认生产一致性bug；开发I/O及其他失败历史继续保留，不以本轮通过改写其原因。
- 不证明真实断电、磁盘/SSD、文件系统或介质损坏安全。不实施重置数据、Stage9、在线升级或S8-T05。
- 代码9eff272；本文件所在提交为最终治理收口。发布方式为正常fast-forward本地main及普通push origin main，不force；发布后核对HEAD=origin/main、clean、0/0，最终SHA以Git回执为准。
- Acceptance：`.ai-dev/ACCEPTANCE/S8-T04.md`；48次逐项摘要：`S8-T04-CRASH-RESULT.json`。全量TRX位于TEMP/StoreExpiryInspectorS8T04Sol/bc9bacb459a9413abaf455c5d3dc2811，SHA256 8097D52F940C1CEEC09494707AA2E551DF4FC4D5796E338DEC61BB58084F59BE。
- 停止。S8-T05仅为建议方向，等待用户新授权；下方S8-T03为历史回执。

## S8-T03历史状态与停止点

- Stage8：`IN_PROGRESS / S8-T03_CLOSED / WAITING_NEXT_AUTHORIZATION`。S8-T01、S8-T02、S8-T03技术验收关闭；S8-T04～T06仅候选，未创建Task。Stage9、在线升级未开始。
- V1-F01、V1-F02、V1-F03、V1-F03-I04、V1-UI-01继续CLOSED；V1-UI-01保持GUI_ACCEPTANCE_PASSED。本卡没有启动真实GUI，不冒充新的GUI人工验收。
- 用户永久取消“撤销上一次Excel导入”。不新增Undo executor/UI，不验证或规划Undo eligibility，不用Restore冒充Undo。既有代码未顺带删除。
- 设置页“重置数据”仅为未来独立需求，未建Task/实施；以后另定清理范围、自动备份、二次确认与设置保留。
- 正式数据库禁止调查/读取/哈希/修复。本卡全部显式TEMP/GUID，未访问正式库；S8-T02旧轮疑似访问仍未核实，不借本卡通过改写历史事件。

## Git / 角色

- 开工重新fetch：HEAD=origin/main=`e4c628c0e2df261c2da5761b8398c2ecd919452c`，clean、0/0。推送前重新fetch仍同SHA，无远端新提交。
- 治理`8b59746`；基座`01efa1b`、测量`2149d63`；生产优化`617e3dd`、zero跟踪修正`20fc8df`；真实矩阵`6ed428d`；强断言/计量最终`31ace5b70ee03969c11213c4aeb623de7bd196fc`。
- 本卡全新Terra medium/priority实施；中间交付不完整曾退回及更换全新Terra，详细链见Acceptance。未复用旧卡Terra，全部已停止。Sol只写治理并独立审查/运行验收，未写生产代码。
- 工作分支`codex/s8-t03-import-stability`，按用户原授权普通`git push origin HEAD:main`归档，不force。最新归档SHA以包含本记录的提交/推送回执为准，代码验收基线为31ace5b。

## Sol独立验收（2026-09-04）

- 隔离9/9，默认factory=0；相关Import/隔离/本卡回归158/158（其中4高规模门禁空返回不算压测）。
- 实际10k/50k/100k压测3/3；最新回滚专项10/10，含真实100k Stage2完成后失败和跨250商品后置分组失败。
- Release全量925/925，0failure；Release build0warning/0error；EF无漂移；migration代码清单9，末条20260901155124_AddPolicyAndBaselineFoundation。设计时`:memory:`，migration list用--no-connect，不连接正式库；未做在线NuGet漏洞审计。
- 成功/失败integrity_check=ok、FK=0。失败前后完整业务/BLOB指纹一致；snapshot失败阻断写入，已生成快照可依旧契约残留但业务全回滚。
- 完整diff、Schema/ModelSnapshot/index/dependency/csproj/slnx与git diff --check通过。生产仅4个Import链文件，无业务算法或S8-T02读取改变。

## 性能与局限

- 单样本n=1，10k4467.94ms、50k23510.82ms、100k49210.72ms；100k Excel7255524bytes，DB逻辑45010944bytes，物理main+wal+shm91611720bytes。
- 10k Release Before313705.59ms；100k Before在planner发生too many SQL variables。优化后均成功。热身/运行顺序不同，不宣称固定倍率或SLA；未测50k Before。
- 最慢100k post20760.58ms，Batch Save11150.99ms；仍有逐商品查询/SaveChanges。100k execute-context SQL321895、SaveChanges15146，最大参数500；没有宣称N+1全部消除。结束时working-set约1.07GB，不是峰值。未观测OOM/timeout/lock/crash。
- 数据分布为每商品2批、10大类、最多1000既有商品、含库存0；scope先由seed建立，不宣称覆盖100k首次scope冷启动或所有倾斜分布。真实强杀/断电/损坏未做，未来仍需单卡授权。

## 证据与后续边界

- Task：`.ai-dev/TASKS/S8-T03.md`；详细命令、JSON路径/SHA、Before/After、失败矩阵与统计局限：`.ai-dev/ACCEPTANCE/S8-T03.md`；阶段：`.ai-dev/STAGES/STAGE-8.md`。
- 原始产物只在本机TEMP下`StoreExpiryInspectorS8T03/<GUID>`与`StoreExpiryInspectorS8T03Sol/<GUID>`，不包含正式数据，不上传大SQLite/XLSX。S8-T02归档事实仍见其Acceptance，不冒充本卡新鲜压测。
- 完成本卡普通push后停止；不得创建S8-T04或重置数据Task，不做强杀/断电/人工损坏、不启动Stage9或在线升级。下一卡需用户明确批准。
