# Stage20｜Release Builder

日期：2026-09-13（Asia/Shanghai）

Stage20 = `CLOSED / ACCEPTED`

## 最终交付

1. S20-T01｜Core Release Builder：`CLOSED / ACCEPTED`。未来用户只提供 `Version + CandidateSha`，Builder 只生成本地候选并固定停止于 `RELEASE_CANDIDATE_READY / PUBLISH_NOT_AUTHORIZED`。
2. S20-T02｜变更影响判定与历史证据复用：`CLOSED / ACCEPTED`。自动判定 Candidate 相对上一正式 product source 改变的职责、必须重新取得的 evidence、可复用的历史 frozen evidence，并对未知变化 fail closed。

Stage20 不负责自动执行测试、GUI、FULL、rollback 或发布；Stage20 到此结束，不创建 S20-T03。

## 永久边界

- 正常业务输入只有 `Version + CandidateSha`；Version 只校验 Candidate 自身 App/Updater/程序集身份，不覆盖 MSBuild Version。
- Candidate 从精确 40 位 commit 建立独立 clean worktree；正式 dirty 工作区不作为构建输入。
- Release Contract 只保存历史 source compatibility；target migrations 只取 Candidate 的 `CurrentSchemaIdentity.Migrations`。
- 流程固定为 publish → ZIP → archive audit → manifest → production RSA-PSS → reverse verify → production RevalidateForInstall → ISCC → Asset Freeze → receipt。
- 禁止 push、tag、Release、上传、Quark、Gitee、FULL、GUI、fault rollback 或重新执行 migration9→10 正式 E2E。

## S20-T02 边界

- 只执行 `previous official product source → CandidateSha` 的 Git diff、声明式 path ownership 分类、evidence impact 和 receipt schema v2 写入；不运行受影响测试。
- previous product source 只由 Release Contract 的 `previousRelease` 定位本地 annotated tag 并 peel 为完整 commit；必须为 Candidate ancestor。
- `UNKNOWN`、tag 缺失/lightweight/不可 peel/非 ancestor 均在 `CHANGE_IMPACT` fail closed。
- `requiredEvidence` 只表示旧证据失效；`reusableEvidence` 只表示历史 frozen evidence 具备复用资格，均不代表 PASS。
- `FAULT_INJECTION_ROLLBACK=NOT_FULLY_VERIFIED / PRODUCT_RISK_ACCEPTED` 保持，不包装为 reusable PASS，也不自动重跑。

## S20-T01 最终裁决

- 首次 dry run `2de255bb-3f64-41a7-98fa-5e73a8eaeae8` 在 `PRODUCTION_REVALIDATION` 停止；未进入 ISCC、未生成 Setup，失败 receipt 保留且 `publishAuthorized=false`。
- 根因是专项误用会绑定 testhost EntryAssembly 版本的 `PrepareEmbedded`；修复 `80952753e7ff266f9866cba5713a6c500869ddc9` 已改为直接调用生产 `RevalidateForInstall`，对原失败 ZIP/manifest/signature 定向复验=`Verified`。
- 第二次 dry run `441570f8-a606-4da0-aaec-868995948fc5` 在 `SCHEMA_IDENTITY` fail closed；其 `FAILED` receipt 原样保留，未进入 ZIP、签名、RevalidateForInstall 或 ISCC。
- 第二次根因是 Builder 错误要求历史 Candidate 包含未来的 S20-T01 probe。返修改为 Builder HEAD 执行 probe，并通过隔离 AssemblyLoadContext 读取 Candidate assembly 的 `CurrentSchemaIdentity`。
- 最终 Builder=`108f43234d8e767198a2bab10d28d4772593f3a2`；第三次 dry run `ba98a10b-81c9-4024-a769-6e924766a1c0` 使用 Candidate `18230c3e6013a098874426575e6a14c202fa7f7c`，结果=`RELEASE_CANDIDATE_READY / PUBLISH_NOT_AUTHORIZED`、mode=`NOT_FOR_PUBLICATION`、production revalidation=`Verified`、archive/signature=`PASS`、ISCC exit=`0`。
- `S20-T01=CLOSED / ACCEPTED`；production diff=`0`，`FULL/GUI/rollback/migration E2E=NOT_RUN`。两个失败 run 与最终成功 run 的 disposition 均永久保留。

## S20-T02 最终裁决

- implementation=`7cfc67ec3266b2431f4b13faf4edb7ed4b1662d8`；治理基线 `origin/main=870eb4cb0d38cb196d404bce6b0e70cff8376202`；仅 Release Builder、policy、receipt schema、一个专项文件和治理发生变化，production diff=`0`。
- receipt 升级为 `schemaVersion=2`，保留既有 Schema Gate 字段并增加可审计 `changedFiles`、分类、UNKNOWN、required/reusable evidence。
- 实际 `v1.0.9` annotated tag peel=`9bf4ee71f1579097d816032d867041c0519cf789`，到 Candidate `18230c3e6013a098874426575e6a14c202fa7f7c` 为 ancestor，得到 61 files、0 UNKNOWN。
- Sol 最终技术验收=`PASS`；S20-T02=`CLOSED / ACCEPTED`。S20-T02/Builder 直接专项=`3/3 PASS`；`FULL/GUI/rollback/migration E2E/Installer E2E/Updater transaction E2E/完整 Builder dry run=NOT_RUN`。
