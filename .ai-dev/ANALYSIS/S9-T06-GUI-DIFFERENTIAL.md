# S9-T06 实机 Verified 后的 GUI 差异审查

状态：IN_PROGRESS / NOT_ACCEPTED / USER_GUI_BLOCKED。普通Win11原始JSONL已读取并逐项核对；独立探针成功不是GUI升级成功，但足以撤回将基础网络/资产或开发机代理环境差异当作既定根因的方向。此次不要求用户重复探针或做After。

## 实机证据

原JSONL SHA256 `345AC20CB228D01455CEDEAEEB56A820B9EFE199F7BB15C6F572C1F25936538D`；OS内核build26200/x64、.NET10.0.10；正式1.0.0+7044a98 DLL SHA `09E1E4B7DF08D48602D76684AC9F6D39BA0B6F02066973AD5B7A8E25090CF444`。Check/Refresh200、三个资产302→CDN200；manifest852、sig384、ZIP107875191字节，Prepare Verified且无Updater启动。所有代理环境变量不存在。日志同时记录DefaultProxy.IsBypassed=false，因此不能把“无代理环境变量”写成“没有系统代理”；无论是否存在系统代理，该次实机核心成功都不支持代理环境变量根因。

## 十项逐链比较（冻结1.0.0源码）

|范围|GUI行为|独立探针行为|审查结论|
|---|---|---|---|
|真实调用|按钮→MainWindow.PrepareUpdateAsync→Task.Run→PrepareAsync；result来自启动检查|async Main先CheckAsync再PrepareAsync|核心相同，调用宿主/线程/时机不同|
|取消创建|每MainWindow一个_updatePackageCancellation；每次按钮创建linked，传给Task.Run和Prepare|CancellationToken.None|需真实日志，但静态正常点击不主动取消|
|弹窗关闭/Dismiss|Dismiss回调为空；dialog.Closed只移除PropertyChanged订阅|无弹窗|关闭提示本身不取消CTS，不能声称“Dismiss提前取消”|
|主窗/运行时|MainWindow.Closed与App.OnExit调用StopUpdatePreparation；正常关窗转托盘走Hide，不触发Closed；UpdateCheckRuntime持有独立CTS|无WPF运行时|未发现检查CTS误传到下载的路径|
|handler实例|checker和MainWindow downloader各创建独立HttpClientHandler；downloader在窗口构造时创建|checker和downloader共用TraceHandler/底层HttpClientHandler，先检查后准备|确有实例复用、连接预热、创建线程/时机差异；不是完全等价宿主，尚非根因|
|options/timeout|ProductionUpdateTrustAnchor.Options；MetadataTimeout/PackageTimeout未覆盖，默认30s/10min；HttpClient无限整体timeout|同Options，仅CacheRoot改TEMP/GUID/cache|未发现GUI额外10秒timeout|
|Dispose|SignedUpdatePackageDownloader没有IDisposable路径，MainWindow不会提前Dispose客户端；linked直到await结束才离开using|handler在整次诊断结束后Dispose|静态不支持HttpClient提前释放|
|single-flight|每downloader按目标版本合并，finally移除；窗口同版本提示抑制，IsBusy阻止重复启动|单次调用|需operation/flight关联日志；没有证据显示重复触发是当前根因|
|progress/Dispatcher|worker投递BeginInvoke；关闭/Shutdown时跳过；完成回UI写StatusText|同步JSONL记录|确有宿主差异；一般回调异常不对应当前网络文案，需现场轨迹|
|cache/后继安装|默认TEMP/StoreExpiryInspector/updates；Verified以后才调用App安装delegate|独立TEMP/GUID/cache；Verified后不安装|当前文案发生在准备内，安装/Updater不是已证实失败阶段；缓存IO有不同文案|

原版取消正常映射为“已取消更新包准备”；Refresh超时映射“读取发行超时”，ReadSmall超时走UpdatePackageException通用文案；主窗外层一般异常显示“更新包准备失败”。用户“无法连接更新服务器”是PrepareCoreInnerAsync捕获HttpRequestException的文案。上述静态分支不支持直接把CTS当根因，但不能用期望异常类型替代真实运行日志。

## 下一步授权与验收边界

已创建全新GPT-5.6 Terra medium实施GUI诊断。重点在原GUI/原下载器内部加默认关闭的安全事件，不先更改handler共享、代理/TLS、超时、取消策略；记录handler创建线程/实例、operation/CTS身份及取消来源、弹窗/主窗事件、single-flight、阶段/status/hop及异常错误码。禁止原始URL query、异常原文、私钥、数据字段和本机敏感路径。诊断sink自身失败不得改变业务行为。

未公开1.0.2候选仅用于真实GUI诊断。当前公开latest仍1.0.1，因此若要复现原100→101准备，必须显式启用隔离诊断入口并清楚显示“候选1.0.2，模拟source100，只准备不安装”；不篡改实际产品版本，不放宽正式版本/安装规则，硬禁止Updater。运行仅TEMP/GUID合成环境，不访问开发机正式数据根，也不改固定安装身份/自启动注册。

根因未确认，不称为修复。既有公开1.0.0/1.0.1资产/tag保持不可变；不发布新正式版本，不创建S9-T07。真实GUI现场轨迹回来后才按证据确定修复；后续正式补丁仍需完整独立技术门禁与普通Win11真实升级成功。

## Sol新鲜原GUI路径动态复验

使用正式100发布DLL与10.0.10 runtime，System.Windows.Application隔离宿主、syntheticShell查询loader（不创建DB），反射原MainWindow(shell)构造获得原默认downloader；checker另外new默认实例，未注入handler、未预热/共享连接、未覆盖timeout/cache。调用原ShowUpdateAvailable并触发真实“立即更新”按钮，执行原PrepareUpdateAsync/Task.Run/linked CTS/Dispatcher；GitHub实际107875191字节完整，Prepared Verified，UI100%。关闭提示和Hide不取消sourceCTS，MainWindow.Close才取消。无安装delegate或Updater配置。

这条证据进一步反驳把handler分离或CTS本身当已证实根因；它不是完整App启动/退出生命周期、更不是失败实机的GUI成功，不能关闭本卡。详见S9-T06-GUI-SOL-VERIFY/frozen100-gui-result.json。
## 候选独立复验完成

## 当前停止点：GUI 1.0.2 诊断候选已验证，等待原失败实机日志

S9-T06 `IN_PROGRESS / NOT_ACCEPTED / USER_GUI_BLOCKED`；Stage9仍 `IN_PROGRESS / S9-T06_CURRENT`。根因未确认，未宣布修复；Win10 NOT_VERIFIED，不创建S9-T07。

- 全新Terra实施并停止，Sol完整diff/独立复验；候选源 `e5ecc65762671f3a29cbbee589aead57714c7e63`，实际产品/FileVersion=1.0.2/1.0.2.0，未创建1.0.2 tag/Release/Setup。公开产品仍1.0.1，既有两版tag/资产不改写。
- 真实完整App启动→更新提示→实际“立即更新”→GitHub metadata/manifest/sig/ZIP→production验证Verified；ZIP107875191字节/SHA689F7A872ECE50F2177A6349EF6DEE9637A8AB655798899EFD0E3C76BDDDD169。仅TEMP/GUID合成数据，模拟source100→101，安装delegate硬关闭；用户包移除Updater目录。这是开发机诊断准备成功，不是原实机正式升级成功。
- GUI取消返回Cancelled并正常退出；关闭提示、Hide、Closed、CTS/check CTS和App退出事件可读。独立阶段/超时/取消/重定向/脱敏/reparse边界5/5；默认诊断关闭smoke exit0。合成DB integrity=ok/FK0/migration9，不是升级After证据。
- 最终fresh无filter Release1055/1055，failure/error/timeout/aborted/skipped0；build0/0，EF无漂移，migration9末条固定。第一轮1054/1055的启动源码静态契约失败保留；Terra显式分支修正后全量重跑通过。
- 诊断ZIP 71011157字节，SHA256 `529936669cf06ee274497e60dad359940dda8dad873517c7890c822b4d689f07`；423项扫描0已知secret；私钥头字面量仅为既有包验证器拦截规则，已独立辨明。无私钥读取、正式数据访问或Windows安全设置改变；EXE仍无Authenticode。
- 用户下一步：原失败标准Win11解压诊断ZIP，运行Start-GuiDiagnostic.cmd，确认实际102/source100/只准备横幅，点击一次立即更新，托盘退出后回传JSONL与结果。无需重复独立网络探针，不做After。具体清单与全部证据在S9-T06-GUI-SOL-VERIFY。

以下内容保留为历史阶段记录。
