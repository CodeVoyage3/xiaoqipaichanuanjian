# Stage23｜门店端使用体验与近期产品需求收口

日期：2026-09-16（Asia/Shanghai）
基线：origin/main@16b93188557dd08c5e5fc1e5505040c85f71ba9c
状态：IN_PROGRESS

## Task 顺序

- S23-T01：AUTHORIZED / IN_PROGRESS；今日三日自动计划与首页明日提示。
- S23-T02：NOT_STARTED；左侧导航 SVG 统一替换。
- S23-T03：NOT_STARTED；商品详情交互优化。

## 共同边界

正式 dirty 工作区 1 modified + 4 untracked 不修改、不清理、不覆盖。治理与开发在独立 clean worktree；Sol 不写生产代码，每张生产修改 Task 使用全新 Terra，禁止复用。

Version=1.1.2；migrationCount=10；migration11=NOT_CREATED。禁止 Schema、CurrentSchemaIdentity、Version、Installer、Updater、Release Builder、在线升级与备份恢复协议变化。

FULL=NOT_RUN / NO_FULL。只跑直接专项与必要 Release build；公共核心影响或跨模块失败须由 Sol 有证据决定扩大。技术验收不能代替用户真实 WPF GUI PASS；用户确认前不得 CLOSED / ACCEPTED。
