# S14-T01 简化在线更新设计冻结

日期：2026-09-08（Asia/Shanghai）

状态：`DESIGN_FROZEN / IMPLEMENTATION_NOT_AUTHORIZED`

## 1. v1.0.4 事实与最短路径

v1.0.4 已有可复用的 `UpdateCheckRuntime`、`GitHubReleaseUpdateChecker`、签名包下载器、安装准备器、Updater、journal/rollback/candidate ACK/pending recovery 和更新提示。检查运行时采用 fire-and-forget `RunAsync`；GitHub 检查使用 linked cancellation token 和默认 5 秒超时。无需新增更新策略状态机、调度框架或持久状态。

当前最小缺口只有五处：

1. `InitializeTrayAndReminderScheduler` 把更新检查、托盘和提醒绑在同一方法；提醒失败会 dispose 已创建托盘。
2. 更新检查在托盘创建调用之前启动，不满足“主界面和托盘优先”的确定顺序。
3. `MainWindow.StopUpdatePreparation` 会在 WPF 退出路径调用 `_updateWorker?.GetAwaiter().GetResult()`。
4. “稍后提醒”关闭首窗后没有固定二次告知。
5. 安装器把 `DisplayVersion` 与实际 EXE 版本不相等当成损坏。另有两个必须分开的升级责任：v1.0.4 -> 新 v1.0.5 由旧 v1.0.4 Updater 执行，新 App 必须兼容其普通启动；新 v1.0.5 自带 Updater 的 same-schema Loaded/ACK/Completed 收口只服务 v1.0.5 -> 后续版本。

## 2. 固定启动顺序

普通启动保持现有 Stage9 pending recovery / 特殊验证角色优先级不变。进入普通 WPF 后固定为：

1. 创建并 `Show()` 主界面。
2. Dispatcher 空闲回调中独立尝试创建托盘；成功即保存托盘并采用显式退出语义，失败只记录。
3. 托盘尝试完成后启动现有 `UpdateCheckRuntime`；它等待现有 `StartupLoadTask`，网络工作不在 Dispatcher 上运行，完成后只把“有新版”提示投递回 Dispatcher。
4. 每日提醒在独立 try/catch 中读取设置、创建并启动 scheduler。它的失败不得撤销托盘；托盘失败也不跳过提醒初始化。

不要求联网检查成功才显示主窗、创建托盘或开放业务。检查结果除 `UpdateAvailable` 外均只记录/结束；不得 `Shutdown`、禁用导航或持久化业务门禁。

## 3. UI 与异步边界

- 初始“发现新版本”只显示“稍后提醒 / 立即更新”。
- “稍后提醒”关闭首窗后，用现有 `WpfDialogService.Show` 显示固定文案，单按钮“知道了”，然后返回普通使用。
- “立即更新”继续使用现有异步下载、签名验证、安装准备与进度 UI。下载已开始后可保留“取消更新”，它不是初始第三选择。
- 窗口关闭/应用退出只发出 cancellation。不得在 WPF UI 线程等待 `_updateWorker`；任务自身保留现有 try/catch 和 cancellation 收口。
- 不新增“本次忽略”“永不提醒”、持久 snooze、倒计时或强制 modal。

## 4. 托盘与提醒隔离

实现可拆成两个私有初始化方法，不新增接口或管理器：

- `InitializeTray` 只负责 tray、Closing 接线和 `ShutdownMode`。
- `InitializeReminderScheduler` 只负责数据库只读设置、channel、scheduler 和启动。

任一 catch 只清理本域部分构造的对象并写日志。提醒 catch 禁止 dispose `_trayIcon` 或把成功托盘的 `ShutdownMode` 改回 `OnLastWindowClose`；托盘 catch 禁止 return 掉提醒路径。

## 5. 安装器与 Updater ownership

安装树身份继续要求合法 AppId、固定安装根、可信卸载命令、正确 DisplayName/InstallLocation、普通非重解析目录以及可读取的主 EXE 文件版本。只删除“DisplayVersion 必须与 EXE 文件版本完全相同”的等值假设；防降级仍分别参考可解析的注册表版本和实际 EXE 版本，不引入版本修复/回写状态。

### 5.1 v1.0.4 -> 新 v1.0.5

正式切换事务由 v1.0.4 安装目录中已经存在的旧 Updater 执行。包内的新 v1.0.5 Updater 不会接管已开始的事务，也无法反向改变旧 Updater 在 `Committed` 后启动普通应用并结束事务的行为。

因此本路径不以“修复新版 Updater”为成功保障。真正的兼容点在新 v1.0.5 App：它必须接受旧 Updater 不携带新版 normal-launch token 的普通启动方式，并在旧 Updater 尚处于切换收尾窗口时可靠完成启动。App 必须避免先取得进程/Mutex 却在主界面和托盘创建前停住；主界面与托盘优先，后台更新检查完全不阻塞。具体生产改法由后续实施在 v1.0.4 基线上用最小复现确定，本轮不预写新状态机或额外门禁。

### 5.2 v1.0.5 -> 后续版本

新 v1.0.5 包内 Updater 的 same-schema 成功路径可做独立最小修正：不增加新 phase，在现有 `Committed` 分支复用现成 `NormalLaunchHandshake`，绑定 operation/token、candidate tree 和进程身份，等到普通主窗 Loaded/ACK 后再写 `Completed`；失败沿既有安全语义收口。它只约束由 v1.0.5 自带 Updater 发起的后续升级，不是 v1.0.4 -> v1.0.5 的反向补丁。实现必须从 v1.0.4 当前代码重新写最小 diff，不 cherry-pick 废弃 v1.0.5/v1.0.6。

## 6. 验收分层

发布前技术候选验收只证明源码与隔离运行时行为，不伪装正式在线升级：

- 新专项证明启动顺序、无 UI 同步等待、网络超时/异常不影响主窗/托盘/业务、两步稍后提示、托盘/提醒双向失败隔离及安装版本不一致兼容。
- ownership 分两套证据：`v1.0.4 -> v1.0.5` 使用旧 v1.0.4 Updater 或其精确启动契约，验证新 App 能显示真实主界面/托盘且无卡死进程/Mutex；`v1.0.5 -> 后续版本` 才验证新版 Updater 的 Loaded/ACK/Completed 收口。
- 运行直接相关的现有更新/托盘/提醒/安装器 focused 回归；Release App/Updater build、EF no drift、migration 9、Updater GUI subsystem、签名/包/tree/secret/diff scope 等候选门禁由后续实施与 Sol 卡精确列出。
- 默认不跑 Full。用户 GUI 只看自动化无法替代的真实显示/交互：主窗与托盘先出现且可用；发现新版后两按钮；点击稍后提醒看到准确文案并能继续使用。安装器、超时、异常注入、版本比对和握手不要求用户重复操作。
- 候选技术接受后仍须独立发布授权。发布后由正式 v1.0.4 的旧 Updater 发起一次真实 v1.0.5 在线升级，用户只确认立即更新链、旧 Updater 启动的新 App 能可靠出现主窗/托盘、版本与原数据正常；通过后才能记录真实在线升级通过。新版 Updater 的未来 Loaded/ACK 能力不得代替这项回执。
