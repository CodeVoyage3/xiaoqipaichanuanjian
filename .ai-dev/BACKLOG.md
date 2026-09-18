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
## 2026-09-17 Stage25 授权更新

S25-T01 = AUTHORIZED / IN_PROGRESS，Scope 见 TASKS/S25-T01.md；覆盖此前延期状态。
S25-T02 = NOT_STARTED：Same-Schema｜跨版本直升策略与兼容基线治理。
同一兼容世代内，Schema、Updater、manifest/signature、持久化格式和必要转换兼容时，默认旧版本直接升级最新版，不要求逐版本安装；仅明确 breaking change 才提高最低兼容版本或要求桥接。最低兼容版本待单独审计。本轮只登记，不启动 T02。

## 2026-09-17 产品决策｜Stage25 候选首项（延期，不进入 v1.1.3）

需求：待排查任务｜选中任务导出 + 正式排查结果回导。
状态：DEFERRED / PRODUCT_DIRECTION_ONLY；Stage25 = NOT_STARTED / NOT_AUTHORIZED。
本轮只记录，不建立 Stage25 Task，不创建 Terra，不审计或修改生产实现。

冻结原则：
1. 今日排查由系统计算今天应处理的 Task 集合，保留现有正式导出 / 回导。
2. 待排查展示全部未完成待办，由门店人工勾选本次处理的 Task；未来提供“导出已选任务”和“导入排查结果”。
3. 两个入口必须复用同一套正式 Excel 模板、导出服务、导入校验、排查记录提交、库存校验、重复提交保护；不得新造第二套业务逻辑。
4. 待排查不增加今日 / 明天 / 后天计划能力。
5. 不提供一键导出全部，导出必须基于用户勾选项。
6. 导入不依赖当前页面勾选状态；根据 Excel 中既有任务身份，重新核验实时任务状态。
7. 未来实施前先审计今日排查导出是否已接受可复用的 TaskId 集合；本轮不预判该实现。

发布边界：
- v1.1.3 不包含本需求；不改生产代码、不重建 RC、不重跑 FULL、不扩大测试。
- PRODUCT_SOURCE_SHA = 280c86f2f30686092f9e603a9bbb8ac593355a27。
- 既有 Sol RC acceptance HEAD = df32158da763111faa54cc42cab1495c23282267。
- RC identity = f3a75491-7eae-4520-b25d-915c3407586f，继续有效，资产与 SHA256 不变。
- S24-T01 = USER_RELEASE_GUI_PENDING；Stage24 = IN_PROGRESS；S24-T02 = NOT_STARTED。
- 等用户明确 RC GUI PASS 后才能启动正式发布；v1.1.3 发布完成前 Stage25 保持 NOT_STARTED。
## Stage13 Release 桥梁保留规则（2026-09-07）

- stable Release 只要仍承担任一支持源版本到 latest 的合法升级桥梁，就不得删除。
- 删除旧 Release 前必须用全部支持源版本重新证明仍存在逐跳签名授权、protocol/source/migration 合法的路径；无路径则 fail-closed，不以 latest 或 Setup 强装替代。
- v1.0.5 发布前，`1.0.2 / 1.0.3 / 1.0.4 -> 1.0.5` 直接兼容证明是 RELEASE BLOCKER；详见 `TASKS/S13-T01.md` 与 `ACCEPTANCE/S13-T01.md`。

## 后续验证：首次由v1.0.4发起真实在线升级
人工确认升级通知modal、取消/关闭恢复主界面、全过程无黑色控制台、自动进入新版本、原数据正常。通过后可追加REAL_V104_TO_NEXT_UPDATE_UX_VERIFIED。此项不阻塞S12/Stage12关闭，不据此创建新版本或Stage13。

# 后续能力与耐久验证 backlog

## S9-T07：14节点×3真实硬杀耐久矩阵

- 状态：DEFERRED / NON_BLOCKING，2026-09-06用户明确移入backlog。
- 范围：原 `ACCEPTANCE/S9-T07-SOL-PLAN.md` 10.5节的14个实际actor marker节点，每节点3次fresh独立真实硬杀与恢复验证。
- 不阻塞S9-T07或V1 Stage9 CLOSED；不在本轮继续执行，不自动创建Stage10或后继任务。
- 本轮保留全部确定性状态机/fault-injection覆盖，真实硬杀改为TASKS/S9-T07.md顶部规定的5个最高风险边界，各先1次，首失败或不稳才追加针对性验证。
- 未来单独安排耐久验证时保留准确marker、PID/start/exe、完整tree/DB/ACK/sidecar证据，不以固定sleep猜测位置；不宣称可证明物理断电、介质可靠性或历史污染来源。

