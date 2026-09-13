# Stage21｜v1.1.1 Setup 体积治理

日期：2026-09-13（Asia/Shanghai）

Stage21 = `IN_PROGRESS / S21-T01_IMPLEMENTED / WAITING_SOL_REVIEW`

## 当前唯一任务

S21-T01｜v1.1.1 Same-Schema Slim Setup 已完成代码实施与实施期直接检查，状态为 `IMPLEMENTED / DIRECT_CHECKS_PASS / NOT_ACCEPTED`。不得创建 S21-T02，不得自动执行最终 Installer E2E。

## 冻结产品合同

- v1.1.1 Setup 模式为 `SAME_SCHEMA_SLIM`，`minimumDirectVersion=1.1.0`，`crossSchemaAllowed=false`。
- fresh install、v1.1.0/migration10 直升、v1.1.1 repair 属于目标能力；v1.0.9 必须先升级 v1.1.0。
- Slim 仍含完整安装 payload 和临时 preflight payload，但不嵌入 ZIP、manifest、signature；线上更新仍生成并使用这三项资产。
- 继续只维护单一 `installer/StoreExpiryInspector.iss`，以明确编译参数选择 `SAME_SCHEMA_SLIM` 或历史 `CROSS_SCHEMA_FULL`。
- `schemaChanged` 只验证模式合理性；Setup 模式必须由 Release Contract 的 `setupCompatibility` 明确授权。
- receipt 升级为 schemaVersion 3，历史 v1/v2 receipt 和 v1.1.0 正式资产保持不变。

## 当前边界

- implementation=`a5bd7388d8d4c701a187ab20775275f518a62166`，基线=`origin/main@c8b8a7db3e4f5740eb7aa22858cc404d84b9f9fa`。
- 未修改 App/Updater C# production code、migration、CurrentSchemaIdentity、在线更新协议或 ZIP layout。
- 当前只完成直接专项、静态合同、Slim/Full 编译、PowerShell/JSON 解析和 diff 检查；最终 Installer E2E、FULL、GUI、rollback、migration E2E 均 `NOT_RUN`。
- 下一步只等待 Sol 独立审查；PASS 前不关闭 S21-T01，不 push main。
