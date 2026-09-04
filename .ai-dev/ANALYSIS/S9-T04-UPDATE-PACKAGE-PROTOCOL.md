# S9-T04 更新包协议 v1

2026-09-05 Sol最终核实协议；对应生产/测试4dbf088ff024a9818418e33bc67868dd0447b604。与S9-T01兼容：签名只提供可信发布声明，不是安装/迁移许可。

## Manifest与签名

`update-manifest.json` 为UTF-8 JSON对象，schemaVersion整数1；拒绝缺失、类型错误、重复属性和不支持协议。字段精确布局由本卡实现与本文件一致冻结：

- `version`：严格无v前缀三段SemVer；`releaseTag`：精确v+version；`repository`：CodeVoyage3/xiaoqipaichanuanjian。
- `channel`：stable；`rid`：win-x64；`minimumProtocolVersion`：整数1。当前尚无Updater，不伪造最低Updater已满足的结论；后续引入Updater必须另定兼容协议。
- `package`：`fileName`、`bytes`（正整数）、`sha256`（64位十六进制）。名字精确StoreExpiryInspector-<version>-win-x64.zip。
- `targetMigrations`：完整有序唯一migration ID列表；格式14位时间戳_非空标识，必须与候选主程序集的静态migration元数据一致。
- `source`：`minVersion`/`maxVersion`（含边界数值比较）、`minMigration`/`maxMigration`（合法ID、按时间顺序范围）；min<=max。当前产品代码声明的版本及migration必须包含在内；不打开实际DB，不执行Migrate，不声称实际旧DB已兼容或保护完成。

Manifest上限64KiB，签名上限1KiB。`.sig`为RSA-PSS/SHA256原始二进制签名，验签输入为下载的原始JSON字节，包括空白/字段顺序。可信RSA公钥至少2048位；测试key仅内存/TEMP，禁止私钥及测试key落入产品。生产trust anchor当前未配置，fail-closed并明确反馈；不得从网络/用户输入信任key，不能用测试公钥填生产常量。

## GitHub资产身份与重定向

[GitHub官方Release资产API](https://docs.github.com/en/rest/releases/assets#get-a-release-asset)说明公开资产可匿名读取，二进制既可能直接200，也可能302；browser_download_url是浏览器下载入口。官方文档不保证某个CDN域名永不变化。

2026-09-05 00:23:45 +08:00，Sol仅GET响应头实测GitHub官方cli/cli v2.100.0的gh_2.100.0_checksums.txt：固定github.com仓库/tag/资产路径302至HTTPS `release-assets.githubusercontent.com/github-production-release-asset/212613049/<GUID>`，无Authorization、未读正文。仅研究分发行为，不是本产品成功下载或资产证据；短期签名query不记录。原始安全字段见Acceptance证据。

生产下载入口固定 `https://github.com/CodeVoyage3/xiaoqipaichanuanjian/releases/download/<tag>/<name>`。先以原已检测版本/tag及原Release id读取固定repo的该Release元数据，验证稳定、tag一致、资产名称唯一且uploaded、URL与构造入口完全相符；不能改查新的latest替换用户选择。Manifest和签名资产也必须属于此Release；读取已验签Manifest后定位唯一ZIP。

关闭HttpClient自动重定向，手动最多3跳；初始路径必须匹配上述固定仓库/标签/资产，最终只接受精确主机 `release-assets.githubusercontent.com`、HTTPS443、路径 `/github-production-release-asset/<数字仓库ID>/<GUID>`。禁止userinfo、fragment、http、IP/localhost、file、UNC、用户自选host、其他githubusercontent子域及后缀欺骗；只跟随这次可信响应的Location，不缓存/日志其签名query。CDN路径/域名变化时安全拒绝，不能自动信任任何HTTPS或依赖泛通配。未来如官方变化需重新证据审查，不等于本卡无法建立安全规则。

## 有界缓存与ZIP

下载只用当前TEMP中新GUID独占目录；验证普通本地祖先，文件CreateNew，无已存在路径覆写。metadata限额可1MiB，Manifest64KiB、signature1KiB，单次package256MiB；流式读取、增量SHA256、实际bytes核对。metadata/Manifest各30秒以内，package总10分钟以内（可测试注入短超时），取消贯穿网络、IO和展开审计。进度最多每100ms更新，并保证最终状态送达。

当前发布420文件、164297044字节（约157MiB），256MiB压缩包/512MiB展开/256MiB单文件/4096条目/200:1压缩比为有余量的硬上限；超限拒绝。扫描实际解压流并核对entry长度、CRC或等效完整性，不能只信声明。受限ZIP只ASCII正斜杠普通文件条目，目录通过文件路径隐含；拒绝显式目录条目、反斜杠、ZIP64、多盘、加密、data descriptor、任意extra/entry或EOCD comment、未知flags及非store/deflate方法。单段最长255、全路径最长1024；local/central名称、flags、method、CRC、size及连续物理偏移必须一致，拒绝SFX/尾随字节。路径、DOS保留名、尾点/空格、大小写/父文件冲突、traversal/ADS/UNC、symlink/reparse等按Task拒绝。

依据[PKWARE APPNOTE 6.3.10](https://pkware.cachefly.net/webdocs/casestudies/APPNOTE.TXT)：本卡审查central/local记录一致性、压缩方法、flags、CRC及extra；UNIX extra可携链接目标，不能只检查文件后缀或ExternalAttributes。具体支持的受限ZIP特征以实现和正反例收口冻结，不声称兼容所有ZIP。

按现有publish清单允许应用文件类型，拒绝任何业务data/backups/logs、app.db/sidecar、Excel、脚本、私钥/PFX/PAT/token；允许清单不是“文件名安全就能执行”的证明。本卡从不运行候选。静态核验根EXE的AMD64/FileVersion与主DLL程序集/产品版本，以及DLL migration声明。允许将必要文件流式写到本次独立scratch读版本，其余只展开读；没有真实app安装写入。

现有完整payload为416 DLL、主EXE与.NET自带createdump.exe、deps/runtimeconfig两个JSON；本地化资源有子目录。Sol独立ZIP样本压缩88096081字节、展开164297044、最大entry压缩比46.22，处于本卡限额内。createdump为明确运行时白名单项，不能因此放开任意EXE。完整包正例用于避免仅两个主文件通过而真实payload被误拒绝。

签名使用[.NET RSA.VerifyData](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.rsa.verifydata?view=net-10.0)的PSS/SHA256。按[PEReader安全说明](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.portableexecutable.pereader?view=net-10.0)，必须先完成可信发布者签名及ZIP整体hash核验，再静态读取候选PE；不能在认证前解析任意网络PE，也不能执行候选程序集。

VerifiedUpdatePackage只表示该时刻的候选身份/hash/签名/审计全部通过，不是程序已经更新，也不授权后续消费者盲信可写TEMP文件。失败/取消仅删除本次未验证缓存；IO清理不成功明确失败，不能标Verified。未来Updater必须消费前重验并建立受控事务，不在本卡落地。

## UI与生产状态

立即更新接下载/验证编排；准备、下载、校验与成功可见、可取消、同版本single-flight。成功文本明确“更新包已准备完成，安装更新功能将在后续版本启用”。没有生产锚点时显示发行验签未配置并安全拒绝，不提供用户输入key旁路。核心业务/DB/Restore maintenance独立，退出取消且无晚到弹窗。

完整成功只能合成key/资产与隔离HTTP测试；真实仓库2026-09-05新鲜匿名repo200/public、列表200/0、latest404。本卡不创建真实Release/tag/资产，不生成生产私钥，不替换程序，不迁移DB。

## 最终证据与保留边界

Sol独立Release核心128/128、实际WPF12/12和真实退出子进程1/1。成功HTTP使用测试专用transport路由到真实TcpListener，不放宽生产URL规则。为验证本卡最终完整publish，模拟旧客户端0.9.9→候选1.0.0，产品本身仍1.0.0；不能冒称已发布1.0.1或真实GitHub更新成功。真实生产客户端01:34:39 +08:00仍NoPublishedRelease。

ZIP内容扫描是已知PEM/PAT标记防护，不是任意编码secret的完备检测。完整源码与publish另做已知私钥块、PAT、证书密码和密钥容器扫描；测试RSA只内存生成，无测试私钥文件进入repo/publish。可信发布者仍须对全部payload负责。当前生产trust anchor为空，真实生产验签仍待正式发行阶段配置。

用户正常退出时Closed先取消并等待线程池下载worker清理，App.OnExit保留兜底；不是更新触发退出。取消可重复重试，失败不影响业务。没有对正式数据库实际schema/健康做推断，未运行更新migration。
