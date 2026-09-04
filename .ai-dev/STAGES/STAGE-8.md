# Stage 8｜性能与稳定性

启动日期：2026-09-03。状态：`IN_PROGRESS / S8-T06_CURRENT / NOT_ACCEPTED`；T01～T05关闭。2026-09-04用户批准最后卡T06，fetch main=f981211、clean/0/0；Task/Acceptance已建立，阶段整体待最终新鲜验收。

本次授权覆盖下方历史“不创建S8-T06”停止点。全新Terra medium/priority，仅复用及必要最小测试；Sol独立验收、默认零生产改动，全部合成TEMP/GUID。WAL来源不可证明、坏当前不能保护时阻断及物理风险继续保留。完成后创建STAGE-8-CLOSEOUT.md并关闭阶段；不启动Stage9或任何新Task。

S8-T05已TECHNICALLY_ACCEPTED / CLOSED：代码/测试e0e5a0d；Sol新鲜无filter984/984、专项100/100、新鲜48/48硬杀、build0/0、EF无漂移、migration9、禁止项与diff检查通过。用户接受合法外来WAL业务漂移为residual risk，不新增来源防护；旧失败保留，不冒充识别成功。严重坏当前不能保护时Restore仍阻断。治理普通push后停止，不创建S8-T06；未来阶段收口须继续列入上述两项能力限制和未证明的物理介质风险。

## 开工事实

- 已重新 fetch GitHub `main`；开工时 `master == origin/main == 1e41876bfa9c203a88cf53955867f0c3dd639e84`，ahead/behind `0/0`，工作区干净。
- V1-F01、V1-F02、V1-F03、V1-F03-I04、V1-UI-01 均已 `CLOSED`；V1-UI-01 为 `GUI_ACCEPTANCE_PASSED`。
- 最近已归档技术基线为 Release 894/894、Release build 0 warning / 0 error、EF 无模型漂移、migration=9，最后一条为 `20260901155124_AddPolicyAndBaselineFoundation`。这些是接任依据，不冒充 Stage 8 新鲜复跑结果。
- Stage 8 已获用户明确批准；Stage 9 与在线升级仍未开始。

## 阶段目标

在完全隔离的 SQLite 环境中，以 100,000 Batch 和 300,000 条正式 Inspection 历史为最低规模，先建立可重复的真实基线，再按证据处理查询、导入、事务中断、损坏、备份恢复和最终稳定性。

本阶段不预设 500ms / 1s 等 SLA。S8-T01 的实测结果是后续性能目标的输入，不是为了让现有实现先通过而调整数据或口径。

## 当前正式任务

- S8-T05已于2026-09-04授权建档，基线80b2c57。全新Terra medium/priority实施隔离损坏与恢复矩阵，Sol独立验收；严重坏库无法合格保护时必须安全阻断，不要求现有Restore直接救援、不绕过保护。详见S8-T05 Task/Acceptance，历史停止点不覆盖本次授权。

- `S8-T01｜高规模数据基线与性能压测基座`：已由全新 GPT-5.6 Terra 实施，Sol 独立技术验收通过并关闭。
- S8-T01 已真实生成并核对 100,000 Batch / 300,000 Inspection；只建立隔离数据、测量工具和基线证据，没有优化生产代码或新增索引、migration、Schema、依赖。
- S8-T02｜查询、分页与高规模读性能优化：Sol 独立技术验收通过并关闭。隔离9/9、正确性150/150、100k/300k专项1/1、Release912/912、build0/0、EF无漂移、migration9；未新增索引/migration。当前停止，详见 S8-T02 Acceptance。

- S8-T03｜大Excel导入性能、事务与回滚压力：Sol独立验收关闭。最终代码31ace5b；三档真实导入3/3、回滚10/10含100k及跨250组、隔离9/9、Release925/925、build0/0、EF无漂移、migration9。10k/50k/100k单次4467.94/23510.82/49210.72ms；详见S8-T03 Acceptance，保留N+1等局限。

S8-T03已于2026-09-04获单卡授权并正式建档，基线e4c628c；只做大Excel权威导入性能及受控异常原子回滚。用户已永久取消Import Undo，删除全部execution/eligibility验证要求，不以Restore替代、不再规划。设置页重置数据仅记录未来独立需求，不建Task或实施。

## 最后正式任务

S8-T06已获单卡授权并正式建立Task/Acceptance：最终高规模回归与稳定性收口。无后续自动任务。

## 冻结边界

S8-T04于2026-09-04正式TECHNICALLY_ACCEPTED / CLOSED。开工main2142cf7，全新Terra测试实现9eff272；Sol独立48/48硬Kill证据保留。用户限定放行既有隔离损坏回归后，Release无filter全量944/944、0failure，4类87项全部通过；build0/0、EF无漂移、migration9，生产/Schema/index/依赖/工程零改动，未访问正式库。普通push main后停止；不证明真实断电、磁盘/文件系统损坏安全。S8-T05仍仅候选，不创建。

- blank / 0 / positive、ProductTask 生命周期、Stage 算法、Reminder、Excel 导入/导出/回导、History/Revision、Backup/Restore 语义全部冻结。
- “应季搭配 / 赠品小样”继续正常导入，但不参与效期管理。
- 先测量再优化；未经后续单卡批准，不新增索引、migration、Schema，不改业务算法或全局 UI。
- 所有压测只允许使用带本轮唯一标记的临时 SQLite；不得读取、复制、迁移、备份、恢复或修改默认正式数据库。
- 不启动 Stage 9，不实施在线升级。

## 协作治理

- Sol 定义任务与 Acceptance、维护治理文档，并在实现后独立审查完整 diff、复跑专项/Release/build/EF/migration/Git 门禁；Sol 不直接写生产代码。
- 每张 Stage 8 正式 Task 使用一个全新 GPT-5.6 Terra（reasoning medium）；用户已允许后续 priority 服务档，不再重复询问速度。不得复用旧阶段或旧卡 Terra。
- Terra 只实现当前 Task，提交后停止，不 push，不创建后续 Task。
- 用户只承担确有必要的真实 GUI 验收；Stage 8 优先使用自动化、终端、SQLite、日志和压测证据。
