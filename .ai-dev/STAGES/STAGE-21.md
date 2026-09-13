# Stage21｜v1.1.1 Setup 体积治理

日期：2026-09-13（Asia/Shanghai）

Stage21 = `CLOSED / ACCEPTED`

## 最终状态

- S21-T01｜v1.1.1 Same-Schema Slim Setup = `CLOSED / ACCEPTED`。
- S21-T02 = `NOT_CREATED / NOT_STARTED`；不得创建。
- 最终 implementation=`e4573ff06b1a6d7f0823be95c7297fcc3a0abdbb`，治理基线=`origin/main@c8b8a7db3e4f5740eb7aa22858cc404d84b9f9fa`。

## 冻结产品合同

- v1.1.1 Setup 模式为 `SAME_SCHEMA_SLIM`，`minimumDirectVersion=1.1.0`，`crossSchemaAllowed=false`。
- fresh install、v1.1.0/migration10 直升、v1.1.1 repair 属于目标能力；v1.0.9 必须先升级 v1.1.0。
- Slim 仍含完整安装 payload 和临时 preflight payload，但不嵌入 ZIP、manifest、signature；线上更新仍生成并使用这三项资产。
- 继续只维护单一 `installer/StoreExpiryInspector.iss`，以明确编译参数选择 `SAME_SCHEMA_SLIM` 或历史 `CROSS_SCHEMA_FULL`。
- `schemaChanged` 只验证模式合理性；Setup 模式必须由 Release Contract 的 `setupCompatibility` 明确授权。
- receipt 升级为 schemaVersion 3，历史 v1/v2 receipt 和 v1.1.0 正式资产保持不变。

## 最终验收

- S21 relevant direct gate=`12/12 PASS`；Harness=`5/5 PASS`；Installer A–G 全部符合预期；`git diff --check=PASS`。
- A fresh 产品链已真实完成，后续只读 evidence replay PASS 且没有重新运行 Setup；B、C PASS；D、E、F、G 均 `EXPECTED BLOCK / PASS`。A/B/C migrationCount=10、integrity=ok、FK=0；全部场景 updaterTransactions=0。
- production Slim Setup=`StoreExpiryInspector-Setup-1.1.1.exe`，75,345,382 bytes，SHA256=`F5E746D7A01A58DA738C0B19189E1B4CC47FACC0C603EAB99EAEC93129280E27`；较 v1.1.0 Full 149,778,454 bytes 减少 74,433,072 bytes（49.69%）。TestMode Setup 大小不作 production 门禁。
- 精确排除 `S16T02InstallerLaunchTests.Candidate_versions_are_1_0_8`：`PRE_EXISTING_STALE_VERSION_ASSERTION / NOT_S21_BLOCKER`，源文件 unchanged，记入 `BACKLOG / FUTURE TEST CLEANUP`。
- Harness 仅在 `tests/S21T01-RunInstallerSlim.ps1` 分离 machine-readable JSON/console transcript，并在 Setup 完成边界取 DB SHA；均为 test-only 修复。
- 历史 V110 两脚本只显式补齐 `CROSS_SCHEMA_FULL` 和 `minimumDirectVersion=1.0.9`。v1.1.0 source identity、migration9→10 合同、fault rollback disposition、历史断言和历史测试语义均未改变。
- 未修改 App/Updater C# production code、migration、CurrentSchemaIdentity、在线更新协议或 ZIP layout。`FULL=NOT_RUN / NO_FULL`；GUI、fault rollback、migration9→10 正式 E2E、online Updater E2E、backup/restore 均 `NOT_RUN`。

## 成果与边界

- 单一 Installer 源文件形成显式 `SAME_SCHEMA_SLIM` / `CROSS_SCHEMA_FULL` 双模式。普通 same-schema Release 可使用不嵌入完整 update ZIP/manifest/signature 的 Slim Setup；Cross-schema Release 仅在兼容合同明确批准时允许 Full。
- 本 Stage 未泛化任意未来 Schema upgrade、未缩小在线 ZIP、未修改 self-contained、未合并 App/Updater runtime、未修改在线更新协议。
- App/Updater runtime 约 83.7 MB 重复问题=`OUT_OF_SCOPE / FUTURE REVIEW`。
