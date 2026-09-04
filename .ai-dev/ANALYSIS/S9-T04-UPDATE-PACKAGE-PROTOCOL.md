# S9-T04 更新包协议 v1

2026-09-05 Sol开工契约；实施细节与独立证据收口时核实，不代表已验收。与S9-T01兼容：签名只提供可信发布声明，不是安装/迁移许可。

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

生产下载入口固定 `https://github.com/CodeVoyage3/xiaoqipaichanuanjian/releases/download/<tag>/<name>`。先以原已检测版本/tag（以及可用的Release id）读取固定repo的该Release元数据，验证稳定、tag一致、资产名称唯一且uploaded、URL与构造入口完全相符；不能改查新的latest替换用户选择。Manifest和签名资产也必须属于此Release；读取已验签Manifest后定位唯一ZIP。

关闭HttpClient自动重定向，手动最多3跳；初始路径必须匹配上述固定仓库/标签/资产，最终只接受精确主机 `release-assets.githubusercontent.com`、HTTPS443、路径 `/github-production-release-asset/<数字仓库ID>/<GUID>`。禁止userinfo、fragment、http、IP/localhost、file、UNC、用户自选host、其他githubusercontent子域及后缀欺骗；只跟随这次可信响应的Location，不缓存/日志其签名query。CDN路径/域名变化时安全拒绝，不能自动信任任何HTTPS或依赖泛通配。未来如官方变化需重新证据审查，不等于本卡无法建立安全规则。

## 有界缓存与ZIP

下载只用当前TEMP中新GUID独占目录；验证普通本地祖先，文件CreateNew，无已存在路径覆写。metadata限额可1MiB，Manifest64KiB、signature1KiB，单次package256MiB；流式读取、增量SHA256、实际bytes核对。metadata/Manifest各30秒以内，package总10分钟以内（可测试注入短超时），取消贯穿网络、IO和展开审计。进度最多每100ms更新，并保证最终状态送达。

当前发布420文件、164235092字节（约157MiB），256MiB压缩包/512MiB展开/256MiB单文件/4096条目/200:1压缩比为有余量的硬上限；超限拒绝。扫描实际解压流并核对entry长度、CRC或等效完整性，不能只信声明。受限ZIP只普通文件/目录和已支持压缩方法；未知链接extra/加密/格式拒绝。路径、DOS保留名、尾点/空格、大小写/父文件冲突、traversal/ADS/UNC、symlink/reparse等按Task拒绝。

按现有publish清单允许应用文件类型，拒绝任何业务data/backups/logs、app.db/sidecar、Excel、脚本、私钥/PFX/PAT/token；允许清单不是“文件名安全就能执行”的证明。本卡从不运行候选。静态核验根EXE的AMD64/FileVersion与主DLL程序集/产品版本，以及DLL migration声明。允许将必要文件流式写到本次独立scratch读版本，其余只展开读；没有真实app安装写入。

VerifiedUpdatePackage只表示该时刻的候选身份/hash/签名/审计全部通过，不是程序已经更新，也不授权后续消费者盲信可写TEMP文件。失败/取消仅删除本次未验证缓存；IO清理不成功明确失败，不能标Verified。未来Updater必须消费前重验并建立受控事务，不在本卡落地。

## UI与生产状态

立即更新接下载/验证编排；准备、下载、校验与成功可见、可取消、同版本single-flight。成功文本明确“更新包已准备完成，安装更新功能将在后续版本启用”。没有生产锚点时显示发行验签未配置并安全拒绝，不提供用户输入key旁路。核心业务/DB/Restore maintenance独立，退出取消且无晚到弹窗。

完整成功只能合成key/资产与隔离HTTP测试；真实仓库2026-09-05新鲜匿名repo200/public、列表200/0、latest404。本卡不创建真实Release/tag/资产，不生成生产私钥，不替换程序，不迁移DB。
