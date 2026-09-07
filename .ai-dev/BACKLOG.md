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
