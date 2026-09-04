# S9-T03 公开稳定Release元数据契约

2026-09-04 Sol治理。只定义检查/提示边界，不代表下载、签名链或Updater已经实现。

## 固定公开来源

GET https://api.github.com/repos/CodeVoyage3/xiaoqipaichanuanjian/releases/latest，匿名HTTPS、固定User-Agent，无Authorization/PAT/OAuth/secret；不跟随任意重定向，不使用body/assets/URL启动进程、打开网站或下载二进制。每正常进程自动检查最多一次，无长期Timer、持久snooze或自动重试。

[GitHub官方latest文档](https://docs.github.com/en/rest/releases/releases#get-the-latest-release)允许对公开资源匿名读取，latest排除draft/prerelease。客户端仍自行验证字段与版本，不能把服务器过滤当作安全验证。latest是GitHub选定的发布版本；客户端比较它与本机版本，不扫描历史寻找另一个“最大tag”。远端低于本机时只返回RemoteOlder，不降级。

[GitHub官方限流文档](https://docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api)说明403/429与限流头语义；本卡不重试，请求结束即止，避免持续消耗匿名配额。共享公网IP仍可能触发限流，核心业务不受影响。

## 数据与结果

严格稳定tag：vMAJOR.MINOR.PATCH，三段非负数字、无前导零（单独0合法）、无前后空白、pre-release或build后缀；超出受支持数值范围拒绝。当前版本来自正式产品程序集，不新增第二套1.0.0常量。解析失败不提示新版。

成功响应必须是JSON对象，tag_name合法，draft/prerelease明确Boolean false。仅安全显示body纯文本摘要，空说明合法；有长度和响应总字节上限。禁止HTML/WebView/Markdown执行、URL导航或远程图片加载。

| 条件 | Application结果 | 默认UI |
|---|---|---|
| 远端高于当前 | UpdateAvailable，携带当前/最新版本和纯文本说明 | 轻量提示 |
| 版本相等 | UpToDate | 不打扰 |
| latest404 | NoPublishedRelease | 不打扰 |
| 远端低于当前 | RemoteOlder | 不提示降级 |
| DNS/TLS/断网/超时/5xx | NetworkUnavailable | 不阻断业务 |
| 403/429 | RateLimited | 不阻断业务，不重试 |
| JSON/字段/tag/稳定标记不合法或响应过大 | InvalidRemoteMetadata | 不提示假新版 |
| 用户退出/主动取消 | Cancelled | 不再操作UI |

latest404本身不能鉴别仓库未来变private/移除和无Release。当前无Release结论由Sol同次匿名repo200/public、列表200/0、latest404共同验证；未来不可用时客户端仍安全静默、不索要token，不声称完成鉴权或私有分发支持。

## 生命周期及下一卡边界

主窗与核心初次读取完成后发起异步检查；网络任务不成为主窗/SQLite/业务可用的前置条件。请求头与正文读取共享短超时和取消；结果任务异常被观察，进程退出取消、丢弃晚到结果；重复触发不再发多个请求或显示多个提示。

新版提示显示中文标题和当前/最新v版本。稍后提醒只在本进程抑制该版本，下次正常启动可再查。UpdateRequested仅传递明确的更新意愿；下载能力未接入时展示未启用说明，不假装成功、不退出应用、不写程序文件。本卡不落地update-manifest.json、签名、公钥链、SHA包校验、解压、staging、升级保护或回滚。

S9-T02安装器身份/路径/权限/数据契约与Version1.0.0保持；旧安装器为历史验收产物，Stage9最终交付需重新构建。真正首个v1.0.0 Release与更新包仍待独立授权。
