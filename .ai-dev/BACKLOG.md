# 后续能力与耐久验证 backlog

## S9-T07：14节点×3真实硬杀耐久矩阵

- 状态：DEFERRED / NON_BLOCKING，2026-09-06用户明确移入backlog。
- 范围：原 `ACCEPTANCE/S9-T07-SOL-PLAN.md` 10.5节的14个实际actor marker节点，每节点3次fresh独立真实硬杀与恢复验证。
- 不阻塞S9-T07或V1 Stage9 CLOSED；不在本轮继续执行，不自动创建Stage10或后继任务。
- 本轮保留全部确定性状态机/fault-injection覆盖，真实硬杀改为TASKS/S9-T07.md顶部规定的5个最高风险边界，各先1次，首失败或不稳才追加针对性验证。
- 未来单独安排耐久验证时保留准确marker、PID/start/exe、完整tree/DB/ACK/sidecar证据，不以固定sleep猜测位置；不宣称可证明物理断电、介质可靠性或历史污染来源。
