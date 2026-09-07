# S13-T01 强制升级与合法路径设计冻结

日期：2026-09-07（Asia/Shanghai）
状态：DESIGN_FROZEN / IMPLEMENTATION_AUTHORIZED

## 1. 当前链路与最小改动方向

当前 `App.OnStartup` 已把 `PendingUpdateRecovery.TryResume` 放在普通数据库初始化之前；upgrade verification/candidate ACK 也有独立早期分支。普通版本检查则在主窗与业务读取完成后由一次性 `UpdateCheckRuntime` 发起，只读 `releases/latest`，仅 `UpdateAvailable` 弹窗；现窗仍有“稍后提醒/取消更新”，主窗关闭后可进托盘。

S13 不重写 Stage9。复用现有 GitHub 检查、签名 manifest/包校验、`DurableFile.Replace`、Updater、journal、maintenance、rollback、candidate ACK 与 pending recovery；只增加一个更新策略状态机、一个合法路径解析器、启动/周期调度和强制 UI 门禁，并在共同入口集中执行业务阻断。

## 2. 启动优先级

固定顺序：

1. 解析并验证 RuntimeDataRoot/受控启动参数。
2. Stage9 pending recovery / updater recovery；有未完成事务则先恢复并退出或继续其既有流程。
3. upgrade verification、schema candidate、candidate ACK、old-app recovery、normal-launch handshake 等既有特殊角色，完全按 Stage9 协议完成；普通强制门禁不得抢先。
4. 获取单实例身份并加载 update-policy durable state。
5. 执行启动可信检查/门禁判定。只有 `BUSINESS_ALLOWED` 才进入普通 DatabaseInitializer、补算、主 Shell、Reminder 与托盘业务路径。
6. 业务运行后用单一 6 小时调度器复检；不并发重复检查。进程退出取消并观察任务。

不得为 Stage13 改写 Stage9 journal phase、ACK、rollback、maintenance 或 schema 事务；若实际实现必须改动其中任一核心，Terra 停止并交 Sol 扩围/full 裁决。

## 3. Durable 状态

建议路径：`RuntimeDataRoot/updates/update-policy-state.json` 与最小 companion `update-policy-anchor.json`，均为普通本地文件，使用 `DurableFile.Replace`；不得写入业务 SQLite。

状态最少包含：`SchemaVersion`、固定 `ProductId`、随机 `StateId`、`FirstObservedUtc`、`LastObservedUtc`、`LastSuccessfulCheckUtc`、`RequiredVersion`、`ForcedUpdateRequired`、`AutoContinue`、`LastBlockingReason`。anchor 只含 SchemaVersion、ProductId、StateId、CreatedUtc；两者必须一致。

- 两文件都不存在：仅真正新环境或受 Stage9 可信升级握手保护的首次 Stage13 初始化可创建一次基准。普通既有数据根无合法升级来源时不得静默当新装；先要求可信联网检查。
- 仅一个存在、身份不一致、JSON/schema/字段/时间关系错误：`STATE_INVALID`，fail-closed，联网可信检查可修复；网络失败不能给新宽限。
- 两文件都被管理员级同时删除/篡改不在本阶段防护承诺内。单文件删除、普通重启和网络反复失败不能续期。
- 每次判定把 `LastObservedUtc=max(旧值,currentUtc)` 原子保存。若 `currentUtc < LastObservedUtc`，进入 `CLOCK_ROLLBACK_RECHECK_REQUIRED`，只允许联网重验/退出；不得用负 elapsed 增加宽限。
- 可信完整检查成功才写 `LastSuccessfulCheckUtc=currentUtc`。失败永不刷新它，也不清除 Forced/Security/NoPath 状态。

状态写失败时不得先开放业务或显示可绕过弹窗；持久化成功是进入强制 GUI 的前置条件。

## 4. 判定表

| 持久状态 / 本次结果 | 判定 |
|---|---|
| 当前版本可信等于 required/latest，且完整检查成功 | 清 Forced/AutoContinue/RequiredVersion，开放业务 |
| 完整可信检查证明更高 stable 且存在合法下一跳 | 先持久化 `FORCED_UPDATE_REQUIRED` 与 RequiredVersion，再强制 modal |
| 已 Forced，随后任意网络失败 | 仍强制阻断；24 小时不适用 |
| 安全验证失败或无合法路径 | 持久化独立安全/路径阻断；重试或退出，不吃网络宽限 |
| 无阻断，临时网络失败，LastSuccessfulCheckUtc 距有效当前时间 <=24h | 开放业务，轻量提示稍后自动重试 |
| 无阻断，临时网络失败，成功检查已 >24h | `VERSION_VERIFICATION_EXPIRED`，重试或退出 |
| 从未成功检查，合法首次基准 <=24h | 一次性首次联网宽限 |
| 从未成功检查，合法首次基准 >24h | 阻断，要求联网确认 |
| 状态损坏/矛盾/版本不支持/时间回拨 | fail-closed，联网重验或退出 |

临时网络仅包括 timeout、DNS/网络不可达、合理的 GitHub 5xx/暂时服务失败和可归类的限流。404/缺 manifest、metadata/manifest/签名/锚点/identity/hash/size/protocol/source/migration/archive 不合法均不是临时网络。

## 5. 可信确认与路径图

`releases/latest` 只确定目标候选，不足以解除或建立完整信任。对 latest 及可能的中间稳定 Release，必须从固定仓库读取元数据并验证 non-draft/non-prerelease、严格版本/tag、唯一 manifest/signature/package 资产、production RSA-PSS/SHA256 原始 manifest、repository/channel/rid/protocol、source version/migration、target migration 和 package 元数据身份。实际 ZIP bytes/hash/archive 审计只对当前将执行的下一跳下载，不能一次下载全路径。

图节点是完整通过上述轻量元数据与签名验证的 stable Release；边 `A -> B` 仅当 B 的已签名 source version/migration 允许 A。当前节点使用当前程序集静态 migration 身份；后继节点使用前一已签名 manifest 的 target migrations。任何非法 Release 不入图。

确定性规则：按目标版本升序建立图，以标准 BFS 找到到 latest 的最少跳路径；同层候选按版本降序遍历，使并列时稳定选择更高的下一跳。BFS 是这里足够且可测试的现成算法，不增加策略/插件抽象。无路径即 `NO_LEGAL_UPGRADE_PATH`，明确要求官方升级方式，禁止 latest 硬装或 Setup fallback。

只持久化 RequiredVersion/AutoContinue 和必要阻断身份，不缓存可绕过重新验证的“授权路径”。每次重启先完成 Stage9 恢复/ACK，再重新可信检查并从当前版本重算路径；网络不可用时保持强制阻断。用户只在第一次点击时令 `AutoContinue=true`，中间版本无需再次确认；仍未到目标时自动准备下一跳，到达经可信确认的 required/latest 后才清状态并开放业务。

## 6. v1.0.5 引导兼容

v1.0.2/v1.0.3/v1.0.4 均不会拥有上述图算法，只会读取 latest。因此 v1.0.5 必须自身成为旧客户端可直接消费的收口节点，不能依赖新代码寻找旧桥梁。

当前 fresh tag 检查仅证明三者各有相同 9 条 migration，且 v1.0.2/v1.0.3 Updater 为 Console、v1.0.4 Updater 为 WinExe；它不证明直接升级安全。发布前分别以原始公开客户端/Updater、正式 schema/protocol、签名候选和 TEMP/GUID 合成数据验证 source identity/migration、下载与 ZIP identity、maintenance、journal、candidate ACK、pending recovery、normal success、数据保持，并选代表性失败点验证 rollback。

全部通过后才可把 v1.0.5 manifest source 冻结为 version `1.0.2..1.0.4`、migration 严格为当前 9 条范围；任一失败则保留准确原因并设计可验证桥梁。承担桥梁的旧 Release 不得删除；每次删除前以支持源版本集合重新证明仍存在到 latest 的合法路径。

## 7. GUI 与运行期门禁

- 强制状态必须有真正 modal 窗，只含“立即更新/重试”（按状态）和“退出软件”；无稍后/忽略/取消继续。
- 关闭按钮不能解除状态或返回业务；可转为同一选择或退出。更新准备失败仍停留强制态。
- 共同业务门禁必须覆盖主窗口、导航、导入、今日/待排查/历史/设置、数据修改命令及托盘显示/业务入口；退出始终允许。
- 运行期发现阻断时，先 durable 落盘，再冻结业务命令/Reminder 等写入口，最后显示 modal。不得只依赖 WPF Owner 的暂时禁用。
- 6 小时复检使用进程内单一调度，以上一次检查完成为基准，无分钟级轮询；强制/安全阻断期间由用户重试或 AutoContinue 驱动，不开放业务。

## 8. 非目标

不新增账户、防管理员篡改、服务、计划任务、Setup 自动重装、业务 migration/Schema、发布 v1.0.5、修改既有公开资产或重做 Stage9 状态机。若真实证据要求越过这些边界，停止并由 Sol/用户另行裁决。
