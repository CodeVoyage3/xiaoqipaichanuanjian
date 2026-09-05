# S9-T06 正式在线更新阻塞诊断

状态：IN_PROGRESS / NOT_ACCEPTED / USER_GUI_BLOCKED。根因尚未在失败实机建立，不创建1.0.2，不修改生产代码或公开Release。

## 已确认事实

1. 用户文字回执：普通Win11与Sandbox都能发现1.0.1，正式1.0.0准备更新十几秒后报“无法连接更新服务器”。实机Setup无需以管理员身份运行、中文应用名和桌面快捷方式正常。未收到证据文件本体，不伪称已经独立读取截图/Init/Before。
2. UI的UpdateNotificationViewModel.Complete直接使用result.Message。冻结源码RefreshReleaseAsync把HttpRequestException捕获为“无法读取指定发行”；外层PrepareCoreInnerAsync把未被内部处理的HttpRequestException映射成用户看到的“无法连接更新服务器”。ReadSmallAsync对非成功HTTP也抛无status的HttpRequestException；SendFollowingRedirects对不安全host/重定向也抛相同异常。因此原文不能分辨DNS/TLS/连接、非2xx与redirect拒绝。
3. “正在下载更新包”进度在Manifest和Signature成功且manifest检查通过后、DownloadAsync之前发出。如果用户全程仅看到“正在准备更新包”，优先定位ReadSmallAsync的Manifest/Signature和重定向；仅凭截图不能严密排除一闪而过的Package阶段，更不能指定是Manifest还是Signature。
4. 版本检查仅访问api.github.com/latest；准备链额外refresh tags endpoint，再访问github.com与release-assets.githubusercontent.com。两者默认均HttpClientHandler AllowAutoRedirect=false、同UserAgent、系统默认TLS/代理，不存在源码中专为安装版切换的HTTP handler。安装路径不能单凭相关性归为根因。

## 新鲜本机分阶段证据

直接加载正式v1.0.0发布DLL（SHA256 09E1E4B7DF08D48602D76684AC9F6D39BA0B6F02066973AD5B7A8E25090CF444），使用其正式自包含.NET10.0.10 runtime，在TEMP/GUID中复用GitHubReleaseUpdateChecker及SignedUpdatePackageDownloader。只在HttpMessageHandler外层记录安全事件及body读取异常；原始重定向安全函数通过反射读取，未改规则。

- CheckLatest200、RefreshRelease200。
- Manifest：github.com302 → release-assets.githubusercontent.com200，852bytes。
- Signature：github.com302 → 同CDN200，384bytes。
- Package：github.com302 → 同CDN200，107875191bytes；实际原版production验签和完整包校验Verified。
- 三条Location都通过原版IsCdnUri：HTTPS/443、指定host、/github-production-release-asset/数字repoId/36位GUID形态。当前所见样本没有打破T04冻结规则；不代表用户失败时得到完全相同响应。
- 本机三个DNS查询只返回IPv4（1、1、4条），系统支持IPv6；这是本机本地解析结果，不是失败实机证据，也不等于代理服务端解析。
- 本机进程HTTP_PROXY/HTTPS_PROXY/ALL_PROXY/NO_PROXY均存在，三个目标DefaultProxy.IsBypassed均false。开发验收进程使用代理是已确认事实，实机是否相同未知；它是下一步需对照的环境变量，不能先称为根因。

完整脱敏事件见development-host-trace.jsonl。没有记录Location/query/代理地址/原始异常文本/私钥/数据库。

## 诊断工具及安全边界

NetworkProbe为Sol验收侧工具，不是生产修复。小包借用已安装1.0.0二进制/runtime，先固定SHA校验，再复制顶层程序文件到新TEMP/GUID，运行Probe而不是App。无数据库/backup读取、无Updater启动、无系统配置修改、无管理员或新.NET安装要求。仍使用默认代理和TLS认证，不自动退回不安全路由。

记录RefreshRelease/Manifest/Signature/Package/Redirect、host/路径类别、HTTP状态、hop、exception type、HttpRequestError、递归inner type/HResult/socket错误码；异常原文全部移除，采用安全分类代替，以免签名query/token/用户名路径流入日志。DNS/TLS/Connection/ProxyTunnel/TimeoutOrCancellation/HttpStatus/RedirectRejected有明确标记，未知错误保留Other不猜测。

专项自检通过：模拟非法redirect确实标记拒绝，嵌套TLS异常及DNS/取消分类正确，原始消息中植入的测试query/password均不出现在日志；Windows PowerShell5启动器在指定TEMP发布目录上运行通过。诊断项目build0warning/0error。首次诊断项目restore在受限执行环境遇NuGet TLS认证失败，固定原版RuntimeFrameworkVersion10.0.10并用获准开发环境构建后通过；此工具构建异常不是用户产品根因。

尚无生产diff，不重跑整个产品测试来冒充修复完成。旧1050/1050与自动化网络成功保留历史范围。若需要改生产错误分类或传输策略，必须先据失败实机日志定位，再创建全新Terra medium/priority，使用新补丁版本及完整技术门禁；不可替换1.0.0/1.0.1。

## 当前需要的唯一实机动作

在原失败标准Win11、同网络条件下，退出App、解压诊断包，普通双击Run-Network-Diagnostic.cmd，回传生成的network-diagnostic-….jsonl。无需After、不重复安装、不卸载、不清数据。没有该失败机分阶段证据，精确故障阶段和根因仍NOT_ESTABLISHED_ON_AFFECTED_HOST。

安装器英文通用文本单独记录为未本地化，本轮非阻塞；标准实机中文应用名正常。Sandbox方框不并入本次网络根因。