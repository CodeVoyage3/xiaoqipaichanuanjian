# Stage 8 收口｜性能与稳定性

状态：TECHNICALLY_ACCEPTED / CLOSED（2026-09-04）；S8-T01～T06全部CLOSED，Stage9 NOT_STARTED。最终验收候选17ebb6cc5e03462892ff6f7f4a50484e86598ecb，生产/测试与开工f981211一致；随后仅治理归档，普通push并重新fetch核对clean/0/0，最终main SHA以发布回执为准。

## 逐卡总结

| Task | 目标 | 实测规模 | 核心结果 | 生产修复/优化 | 剩余风险 | 状态 |
|---|---|---|---|---|---|---|
| S8-T01 | 建立真实基线 |100k Batch/300k Inspection|发现Dashboard/待排查/Today/Reminder超大IN变量上限；Stage约6.63秒|无，仅压测|基线不是性能通过；读指纹为采样|CLOSED|
| S8-T02 | 查询/分页 |同规模|数据库筛选/count/分页；原变量上限路径可完成；Stage约174.68ms|查询与最小分页接线、测试注入fail-closed|SCAN/排序、Reminder权威全评估仍存在；100k Today实际Excel导出未证明|CLOSED|
| S8-T03 | 大Excel及回滚 |10k/50k/100k行|4.47/23.51/49.21秒；10/10真实故障回滚|Import分批读取/字典/有界后置分组与清tracker，保留外层单事务|逐商品SQL/SaveChanges，seed已建scope；非峰值内存|CLOSED|
| S8-T04 | 进程硬中断 |48次，含100k导入|36pre完整回滚、12post完整保留，重开可读写、integrity/FK正常|无生产变化|不等于断电/物理介质安全；旧开发I/O根因不由后续通过证明|CLOSED|
| S8-T05 | 损坏检测与安全恢复 |100k/300k、225.43MB|明显损坏/坏备份拒绝，坏当前不能保护则阻断；健康恢复/final失败回退|Initializer已有库检查、启动失败停止；Restore测试接缝|合法外来WAL来源不可验证；不可保护坏主库不直接救援|CLOSED|
| S8-T06 | 最终集成回归 |100k/300k、100k Excel、代表18次Kill|专项全过、无filter Release984/984、全量内另42/42 Kill|无生产/测试改动，仅复用运行说明与治理|见下方能力边界，不扩大承诺|CLOSED|

最终Sol全量984/984、0failure/error/aborted/skip（既有10个显式空return不是压力证据），build0warning/0error，EF无漂移，migration9、末条20260901155124_AddPolicyAndBaselineFoundation；Schema/ModelSnapshot/index/dependency/csproj/slnx/生产PRAGMA无变化，diff --check通过。本卡没有发现新的生产一致性bug或数量级性能退化。

## 新鲜最终基线

S8-T06独立读：Dashboard260.82、首屏193.33、深页367.69、搜索67.62、Stage107.56、大类245.83、三条件146.24、Today186.63、History344.05、Reminder全评估784.25ms（1热身+3样本median）。20路径无变量上限/OOM/timeout/lock/crash，没有数量级退化；无额外优化，不把环境波动宣称性能收益。

100k真实导入50191.79ms，约为T03参考+2%，n=1；完整业务断言、integrity ok/FK0。Product/Batch写入中、跨250后置操作异常两项完整指纹回滚。新大库225427456bytes，Backup5831ms/Restore20546ms/额外integrity-FK2225ms，完整恢复指纹一致。详细SQL、max、阶段边界、路径/哈希见S8-T06 Acceptance。

S8-T04后Import/Inspection/Inventory/Revision事务源无变化；新鲜代表18/18（每链pre/post各3次）：9pre全回滚、9post完整提交，均可重开/权威读/合法写、integrity ok/FK0/migration9。全量另42/42：33pre/9post，全部原worker退出。历史48/48不改写成此处重新运行48次。Revision编辑全量16/16。

全量内第二大库样本Backup5816ms/Restore20852ms/额外integrity-FK2220ms，首样本保留；全量亦再次观察合法外来WAL业务漂移，来源防护仍false。原始TRX/JSON路径和SHA索引：`.ai-dev/ACCEPTANCE/S8-T06-RESULT.json`，详细门禁及空返回：`S8-T06.md`。

## Known limitation / Residual risk

Current validation cannot prove provenance of a structurally valid foreign WAL accepted by SQLite. Such a WAL may alter business state while integrity/FK/migration checks remain valid.

SQLite接受的结构合法外来WAL可能改变业务状态，而integrity/FK/migration仍正常。结构健康不等于业务来源身份；当前没有身份绑定或来源防护，历史真实复现保留，不说“所有WAL/SHM错配都能识别”。本轮不新增provenance/quarantine、不删sidecar、不忽略WAL、不拒绝一切WAL、不自动恢复。

现有Restore是安全恢复健康当前库，不是损坏主库灾难救援工具。当前库严重损坏、无法生成并验证保护快照时，必须fail-closed，保留原文件，不允许用健康备份强制覆盖。损坏文件保全/救援须未来独立契约，当前不建Task。

已证明范围仅实际执行的进程Kill、合成文件损坏、备份恢复逻辑、高规模读/导入及原子性；真实断电、SSD controller、磁盘/文件系统/物理介质损坏、OS不可读介质和bitrot均未证明。原子File.Replace内部介质故障未注入，不把安全接缝冒称硬件试验。

所有实验仅合成TEMP/GUID，未访问/哈希/复制/调查/恢复正式库，不调查S8-T02旧疑似事件或把未知改写为未发生。没有本卡真实GUI/门店实机验收。关闭Stage8不等于Stage9安装部署/在线升级就绪。

Import Undo永久取消，既有代码/历史测试不是产品承诺，不恢复规划；设置页重置数据仅未来独立需求，未创建Task。Stage9、在线升级、安装器与新优化/防护任务均未开始。
