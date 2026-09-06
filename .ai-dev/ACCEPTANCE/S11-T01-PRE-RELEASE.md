# S11-T01 PRE_RELEASE 独立技术状态

记录时间：2026-09-07。当前结论：`S11-T01 = IN_PROGRESS / NOT_ACCEPTED`；`Stage11 = IN_PROGRESS / S11-T01_CURRENT / NOT_ACCEPTED`。本记录不是最终技术通过、不是用户 GUI 全项通过、不是 `REAL_GITHUB` 更新验收，也不授权发布、push、tag 或关闭任务/阶段。

## 冻结边界与适用性

- 当前 `HEAD=c36463774fe396025cf2c06a468a788863a500ce`，相对 `origin/main=034b0f9a366275a9c3dd4f770c8c68da78cf837f` 为 ahead 10、behind 0。
- PRE_RELEASE C-K 在生产实现冻结后独立完成；其后 `d148043..HEAD` 只修改 S7/S9 测试及 test-only fixture，`src/` 与 `installer/` diff 为 0，故该 C-K 结果仍适用于当前生产实现。
- C-K 使用 TEMP/GUID 根，1/1 通过；TRX SHA256 `053E5A2FE6B4B4DA729B6B80FF8603C84ACCEDFC158641DB5CD92E5F3D4EB7BA`。结果精确标记 `PRE_RELEASE_PRODUCTION_EQUIVALENT`，并披露 `officialGitHubBytes=false`：source v1.0.2 host 是 test-only current-production-source，Updater 使用 same-production-source TEMP-root adapter，并非正式发布二进制，绝不声称 `REAL_GITHUB`。

## 已知 focused 收敛

- 最新一次获授权 full 的历史结果永久保留：1158 total / 1156 passed / 2 failed；TRX SHA256 `B611F7E4CFFA80E89B99BF7F31916FCD546E1E7B60FD1487CCEAC6BF7E14BD77`。失败分别属于 S7 草稿保存导航同步与 S9 锁探针 fixture；不得以后续 focused 结果改写为全量通过。
- S7 修复 `ec4357320fb9f1e1c8745e78e96c2c2be9ef3093` 经目标项及直接相邻 6 项独立复核为 7/7，TRX SHA256 `E636673A25D6990154220B11023EFFE1876434775DFDB5FDD67B84AA3E965446`；断言继续覆盖保存期间导航阻塞、页面未加载及释放后完成。
- S9 最终 focused `c36463774fe396025cf2c06a468a788863a500ce` 经目标、缺 native 负例及直接相邻项独立复核为 4/4，TRX SHA256 `731E1520AB47891FB11030EB8C5C6A841F288949B67C7781DDE785BDD2999B8D`；Windows Application 日志无新 fixture crash，未遗留 fixture/testhost 进程。
- 用户已明确：不得再次运行 full，除非后续另行明确批准。因此最终 fresh、无 filter、Release full 仍是未完成门禁。

## 本轮非 full 独立门禁

- 标准 Release build：0 warning / 0 error。
- EF：`NO_MODEL_DRIFT`；生产 migrationCount=9，末条仍为 `20260901155124_AddPolicyAndBaselineFoundation`。
- fresh self-contained win-x64 publish：App 618 文件、247,447,353 bytes，`StoreExpiryInspector.exe` SHA256 `0542282981BDE0D9884034BB1A4D8CB3D0907093F84500F3E35C6317F0E1562E`，FileVersion `1.0.3.0`，ProductVersion `1.0.3+c36463774fe396025cf2c06a468a788863a500ce`；独立 Updater 197 文件、82,744,280 bytes，其 EXE 与 App 内嵌 Updater SHA256 均为 `A4E6C68D67DD7754C74EB2B65858B788BBEBA1B204F5E0D9B6F4F158E477E01C`。
- Inno Setup 6.7.3 候选编译成功：`StoreExpiryInspector-Setup-1.0.3.exe` 75,318,634 bytes，ProductVersion `1.0.3`，SHA256 `F3AF21D09FA191C1033EF8630357F5BC37300432EB2FF075FDED22A64A5BB7B7`。
- 生产源与候选 secret 实质扫描 0；App publish 测试 hook/fixture/`.trx`/`.db` 命中 0。源码中唯一宽模式 private-key 文本命中是下载器自身的 fail-closed 检测字面量，不是密钥；收紧为实际 PEM 内容后命中 0。
- 匿名、无 Authorization 的 GitHub 复核：latest 仍为 v1.0.2，stable、非 draft、非 prerelease，source `02ab6f291c7a52f9de05b19aa29ae9356dc9c676`；公开 v1.0.3 Release/tag 均为 HTTP 404。v1.0.2 四资产重新下载后 bytes/SHA256 与 S10 冻结记录逐项一致。
- `git diff --check` 通过；App/Updater/testhost/dotnet/ISCC 匹配残留进程为 0。所有本轮 publish、installer 与匿名资产下载均落在 fresh TEMP/GUID 根；命令未以正式安装、数据、数据库或备份路径为目标，且未探测这些正式路径。

本轮保留命令前置失败，不以纠正后的通过覆盖：首次 `--no-restore` publish 因先前 focused build 未生成 win-x64 assets 而 `NETSDK1047`；首次 sandbox restore 因隔离网络不可达出现 `NU1801/NU1101`。随后在同一源码边界完成明确 win-x64 restore，publish 与 installer 才成功。secret scanner 的前两次启动还分别因 Git 中文路径 quote 解析和 PowerShell 变量插值语法而在扫描前失败；修正 scanner 本身后才取得上述 0 实质命中结果。

## 仍未满足的完成条件

- 最终 fresh、无 filter、Release full 尚未运行；必须由用户后续明确批准，且需达到 failure/error/timeout/aborted/skipped/notExecuted 全部为 0。
- 尚未存在公开 v1.0.3，因此“官方匿名 public v1.0.2 → 正式公开 v1.0.3”的 `REAL_GITHUB` 门禁客观尚未完成；不得由 PRE_RELEASE 链替代。
- 用户 GUI 回执仅限“重置生效：首页暂无导入数据且显示 v1.0.3”，不扩展为图标、首页有数据对齐、完整更新链或其他 GUI 项通过。

在上述缺口完成并另获收口授权前，必须持续保持 `IN_PROGRESS / NOT_ACCEPTED`，不得写技术全通过、accepted 或 closed。
