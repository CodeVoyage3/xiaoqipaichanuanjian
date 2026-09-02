# 最新交接

## 当前任务与状态

`V1-F03-I01｜今日排查计划查询与 Excel 导出` 已由全新 GPT-5.6 Terra（medium）实施，并由 Sol 独立技术验收通过。

当前状态：`V1-F03-I01_TECHNICALLY_ACCEPTED / WAITING_I02_APPROVAL`

本轮没有启动 WPF、访问生产数据库、修改 Schema 或进入 I02～I04、Stage 8、Stage 9。

## 当前 Git

- 分支：`master`，普通推送到 `origin/main`。
- 产品经理批准前基线：`96d8e228d0cc25578d187c8b509ccd51e9ec5326`。
- I01 开工治理提交：`1aebc33`。
- Terra 实现：`db8be19`；UTC/打印/测试返修：`1ac63a2`。
- 本交接及验收文档所在提交以 `git rev-parse HEAD` 为准。

## I01 已交付

- 从当前合法 open Managed ProductTask 生成独立“今日排查计划 Excel”，支持全部任务或精确选择唯一正数 TaskId。
- 选中集合含重复、非正数、不存在、completed 或其他非法 Task 时整体拒绝，不静默导出错误的部分集合。
- 一批次一行；固定排序 `ProductCode → TaskId → BatchId → TaskItemId`。
- A～L 为固定可见业务列，只有“本次排查数量”为空白待填；M～Y 隐藏保存格式版本、Task/TaskItem/Product/Batch 身份和必要陈旧判断快照。
- 复用 `DocumentFormat.OpenXml 3.5.1` 与 `ProductCategoryScopes`；包含筛选、冻结首行、列宽、横向、单页宽度和重复标题行。
- 同目录临时写入成功后再不覆盖移动；失败不返回伪成功，不留下目标残片。
- 查询 `AsNoTracking`，无数据库业务写入；无 ExportRecord、migration、ModelSnapshot、依赖、WPF 或 I02 逻辑。

## Sol 独立新鲜验收

- I01 专项：3/3。
- ProductTask/query/lifecycle 相关回归：142/142。
- Release 全量：767/767，失败 0，跳过 0。
- Release build：0 warning / 0 error。
- EF 无模型漂移；`--no-connect` migration 列表 9 条，最后一条为 `20260901155124_AddPolicyAndBaselineFoundation`。
- 相对开工治理提交，实现仅 2 个生产文件和 1 个测试文件；无 migration、ModelSnapshot、`.csproj`、`.slnx` 或依赖变化；`git diff --check` 通过。
- `StoreExpiryInspector` 进程 0；未启动 WPF、未访问生产数据库，测试只使用隔离临时数据库与临时 Excel。
- 本轮使用现有 restore 产物运行 `--no-restore`，不冒充在线 NuGet 漏洞审计。

完整证据：`.ai-dev/ACCEPTANCE/V1-F03-I01.md`；冻结契约：`.ai-dev/TASKS/V1-F03-I01.md`；决策：`.ai-dev/DECISIONS.md` D-032。

## Schema 停机门禁

I01 已证明现有 Schema 足够，migration 保持 9。后续若必须持久化导出批次/文件记录或跨重启待确认预览，必须立即停止并提交 Schema 决策报告；未经产品经理批准不得修改 Schema、ModelSnapshot 或借用 Import 表。

## 下一唯一审批点

等待产品经理单独批准：`V1-F03-I02｜排查结果读取、陈旧校验与 Draft 应用`。

批准前不得：

- 创建或执行 I02；
- 开始 I03、I04；
- 进入 Stage 8、Stage 9 或其他功能；
- 把 I01 的隐藏快照写出扩展为回导、Draft、陈旧应用或正式提交。
