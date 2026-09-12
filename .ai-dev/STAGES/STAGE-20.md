# Stage20｜Release Builder

日期：2026-09-13（Asia/Shanghai）

Stage20 = `IN_PROGRESS / S20-T01_REPAIR_IMPLEMENTED / SOL_REVIEW_PENDING`

## 唯一当前任务

- S20-T01｜Core Release Builder：只生成本地候选，最终边界固定为 `RELEASE_CANDIDATE_READY / PUBLISH_NOT_AUTHORIZED`。
- S20-T02：`NOT_CREATED / NOT_DESIGNED / NOT_IMPLEMENTED`。

## 永久边界

- 正常业务输入只有 `Version + CandidateSha`；Version 只校验 Candidate 自身 App/Updater/程序集身份，不覆盖 MSBuild Version。
- Candidate 从精确 40 位 commit 建立独立 clean worktree；正式 dirty 工作区不作为构建输入。
- Release Contract 只保存历史 source compatibility；target migrations 只取 Candidate 的 `CurrentSchemaIdentity.Migrations`。
- 流程固定为 publish → ZIP → archive audit → manifest → production RSA-PSS → reverse verify → production RevalidateForInstall → ISCC → Asset Freeze → receipt。
- 禁止 push、tag、Release、上传、Quark、Gitee、FULL、GUI、fault rollback 或重新执行 migration9→10 正式 E2E。

## 当前门禁

- 首次且唯一获准 dry run `2de255bb-3f64-41a7-98fa-5e73a8eaeae8` 在 `PRODUCTION_REVALIDATION` 停止；未进入 ISCC、未生成 Setup，失败 receipt 保留且 `publishAuthorized=false`。
- 根因是专项误用会绑定 testhost EntryAssembly 版本的 `PrepareEmbedded`；修复 `80952753e7ff266f9866cba5713a6c500869ddc9` 已改为直接调用生产 `RevalidateForInstall`，对原失败 ZIP/manifest/signature 定向复验=`Verified`。
- 第二次 dry run `441570f8-a606-4da0-aaec-868995948fc5` 在 `SCHEMA_IDENTITY` fail closed；其 `FAILED` receipt 原样保留，未进入 ZIP、签名、RevalidateForInstall 或 ISCC。
- 第二次根因是 Builder 错误要求历史 Candidate 包含未来的 S20-T01 probe。返修改为 Builder HEAD 执行 probe，并通过隔离 AssemblyLoadContext 读取 Candidate assembly 的 `CurrentSchemaIdentity`。
- 历史 Candidate `18230c3e6013a098874426575e6a14c202fa7f7c` 专项读取 migration count=`10`、latest=`20260912083448_AdjustCatchupWindowConstraint`；完整第三次 dry run=`NOT_RUN`。S20-T01 继续 `NOT_ACCEPTED`，等待 Sol review。
