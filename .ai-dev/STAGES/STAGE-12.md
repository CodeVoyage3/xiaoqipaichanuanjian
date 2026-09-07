## 2026-09-07 最终Candidate准备（最新裁决）
已批准热修source冻结：即时刷新/布局USER_GUI_ACCEPTED、取消更新文案、更新通知modal及导出Owner、Updater WinExe。仅4个生产文件，无安全状态机/业务/模型/依赖扩大。modal1/1与console2/2 Sol证据继续有效，本轮不重跑。
当前仅Release build、EF NO_MODEL_DRIFT/migration9、全新App/Updater publish/Setup/ZIP/manifest/signature、PE2、签名/tree/secret与隔离启动。禁止full/178/旧GUI重验/公开发布。完成后停止待最终GUI与发布后真实公开升级。HISTORICAL_TEST_ISOLATION_UNCERTAINTY保持，禁止探测正式数据。
S12-T01=IN_PROGRESS/NOT_ACCEPTED，Stage12=IN_PROGRESS，v1.0.4=NOT_RELEASED。旧candidate保持SUPERSEDED_BY_GLOBAL_MODAL_FIX。
# Stage12｜v1.0.4 局部热修复

Stage12 = IN_PROGRESS
S12-T01 = IN_PROGRESS / NOT_ACCEPTED

基线：HEAD=origin/main=0a5775ce4f56b649758f5b60a915febbe4b61f5d，ahead/behind 0/0；Stage11 CLOSED；公开 stable/latest v1.0.3，source 651bd1074a6a95f9cfdad70a9e6e7df6ed0d6df7。

唯一任务见 ../TASKS/S12-T01.md。仅首次导入当前会话今日排查刷新、排查确认布局、取消更新文案。默认不跑 full，专项先行。不得创建 Stage13。

PRE-RELEASE 技术及用户候选 GUI 验收与 POST-RELEASE 正式在线升级分别记录；全部通过前不关闭。
