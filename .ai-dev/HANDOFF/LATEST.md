# 最新交接

## S8-T04当前暂停点（2026-09-04）

- 用户已单卡授权S8-T04。重新fetch HEAD=origin/main=2142cf72588fb727cca6efd58509c113646de1e2，clean、0/0，无冲突；工作分支codex/s8-t04-crash-consistency。
- 状态`PAUSED_RELEASE_SCOPE_CONFIRMATION / NOT_ACCEPTED`。全新Terra medium/priority测试实现`9eff2724a9bb5dcb857d8bec6129d3ef3b713b46`已提交并停止；Sol只治理和独立验收，生产代码零修改。
- Sol最终48/48真实强杀通过（Import10k六点×3、100k前后两点×3；Inspection五点×3；Inventory三点×3），36次提交前完整回滚、12次提交后完整保留。全部integrity ok/FK0/migration9/可重开可写；100k提交后50k Product/100k Batch/5001 Task。19/19 test rows，21.5144分钟、exit0；其中2个空返回facts不另算crash。逐次JSON/指纹/原文件SHA见`.ai-dev/ACCEPTANCE/S8-T04-CRASH-RESULT.json`。
- 隔离10项通过（另2项仅100k关闭gate），过滤Release838/838；独立Release build0warning/0error，EF无漂移，migration --no-connect仍9末条AddPolicyAndBaselineFoundation。完整diff及diff --check通过，无Schema/index/PRAGMA策略/依赖/工程变化。
- 唯一未完成门禁：既有Release有Stage2/7人工损坏隔离SQLite的用例，与本卡禁令冲突。已请求用户澄清，仅保守排除4类共87项，未运行它们；不能称全量Release通过。尚无答复，故不TECH_ACCEPTED/CLOSED、不push main。下次只需先确认旧隔离回归是否放行；如放行，再跑真正全量及收口，不额外重复48次高规模专项，也不复用旧卡Terra。
- 开发阶段握手/reader/比较/I/O错误及一次缺最终JSON的Import试跑全部保留，不混入最终48次。旧I/O根因未独立确定；最终通过不改写旧失败或旧正式库疑似访问。48个最终worker实例都已退出，复用PID未被操作。
- 仅TEMP/GUID真实子进程硬终止，权威大Import/InspectionSubmission/InventoryAdjustment；不访问正式库，不新增工程/依赖/Schema/index/migration/PRAGMA，不创建S8-T05，不启动Stage9/升级/重置数据。Undo永久取消。
- 最新复fetch origin/main仍2142cf7；本地代码及治理未push。只有补齐全量Release且全部门禁完成后，才按授权普通push main并停止。以下是S8-T03历史归档回执，不再是当前停止点。

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
