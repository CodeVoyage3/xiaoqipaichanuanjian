# Stage25｜门店排查工作流与跨版本升级能力优化

状态：IN_PROGRESS。fresh baseline：588ea7c9417b32074214f268435638c30da995ed。
S25-T01：AUTHORIZED / IN_PROGRESS；Scope 已获用户确认。
S25-T02：NOT_STARTED；仅登记，须 T01 用户真实 GUI PASS、CLOSED / ACCEPTED 后再获用户明确授权。
Sol 仅治理与独立验收；每张实施卡使用全新 Terra；原 dirty main 不触碰。
FULL = NOT_RUN / NO_FULL；正式 DB / 安装根 NO_ACCESS。
Version App/Updater=1.1.3；migration=10；migration11=NOT_CREATED。
不自动集成 main、push、tag、Release 或改变已发布资产。

S25-T02：Same-Schema｜跨版本直升策略与兼容基线治理。
同一兼容世代内，Schema、Updater、manifest/signature、持久化格式和必要转换均兼容时，旧版本默认直接升级当前最新版，无需逐版本升级。只有明确 breaking change 才提高最低兼容版本或要求桥接；未验证不等于技术不兼容。最低兼容版本由未来 T02 审计确定，不预先写死。已发布资产不可偷换。本轮不审计或实施 T02。
