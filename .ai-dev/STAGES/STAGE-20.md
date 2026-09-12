# Stage20｜Release Builder

日期：2026-09-13（Asia/Shanghai）

Stage20 = `IN_PROGRESS / S20-T01_IMPLEMENTATION_AUTHORIZED`

## 唯一当前任务

- S20-T01｜Core Release Builder：只生成本地候选，最终边界固定为 `RELEASE_CANDIDATE_READY / PUBLISH_NOT_AUTHORIZED`。
- S20-T02：`NOT_CREATED / NOT_DESIGNED / NOT_IMPLEMENTED`。

## 永久边界

- 正常业务输入只有 `Version + CandidateSha`；Version 只校验 Candidate 自身 App/Updater/程序集身份，不覆盖 MSBuild Version。
- Candidate 从精确 40 位 commit 建立独立 clean worktree；正式 dirty 工作区不作为构建输入。
- Release Contract 只保存历史 source compatibility；target migrations 只取 Candidate 的 `CurrentSchemaIdentity.Migrations`。
- 流程固定为 publish → ZIP → archive audit → manifest → production RSA-PSS → reverse verify → production RevalidateForInstall → ISCC → Asset Freeze → receipt。
- 禁止 push、tag、Release、上传、Quark、Gitee、FULL、GUI、fault rollback 或重新执行 migration9→10 正式 E2E。
