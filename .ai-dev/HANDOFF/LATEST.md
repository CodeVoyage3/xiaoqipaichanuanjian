# 最新交接

## S9-T06前置环境暂停（2026-09-05，当前权威停止点）

- S9-T06 `PAUSED_PRODUCT_REVIEW / NOT_ACCEPTED`；Stage9 `IN_PROGRESS / S9-T06_CURRENT / PAUSED_PRODUCT_REVIEW`。本卡已创建但未实施/未验收；Stage8及T01～T05保持CLOSED，不创建S9-T07。
- 精确阻塞：当前没有可执行的独立clean Windows环境。开发机为Windows11专业版23H2，10.0.22631.6199，x64；WindowsSandbox.exe不存在，未发现Get-VM/vmms或常见VM工具入口，用户明确回复“未安装过”。HypervisorPresent=true不证明可用clean OS；DISM功能状态查询需提升，未据此断言硬件不支持。未安装/启用VM、未重启系统、未关闭安全功能。
- 按用户第36节硬停止，在任何production key/版本变更/公开Release前暂停。正式身份安装器会预检固定数据根，不能在开发用户正式数据环境试装；未探测/访问正式安装或数据/DB/backup。
- 正式新Terra medium为`/root/s9_t06_fresh_terra_medium`，工具提供priority服务；只读审查后停止，零代码修改/零提交/零push。首次误用全历史fork的实例立即中断，不计正式实施者。治理角色独立核对了公钥未配置、安装器固定身份及硬编码1.0.0、migration源码9条。
- 新鲜匿名GitHub：repo200/public、releases200/0条、latest404；无Authorization、未下载生产资产。GitHub发布写权限和持久私钥保管尚未验证，不能将git fetch/push当Release权限证据。
- 产品仍1.0.0；无生产公钥fingerprint/私钥、无v1.0.0/v1.0.1 Release及asset SHA、无真实升级/DB指纹/回滚/clean Win10或Win11验收。Release1040/1040、build0/0、EF无漂移仍仅T05历史，本轮未重跑；文档diff检查通过。
- 已建立Task、Acceptance、Production Release Analysis和RELEASE-RESULT.json暂停证据。仅治理普通commit/push main，fetch确认clean/HEAD=origin/main/0/0后停止；最终SHA见Git回执。恢复前需用户提供或明确授权建立可用clean OS，再核实密钥与发布权限，不降低原门禁。

## S9-T06正式启动（2026-09-05，覆盖下方历史停止点）

- 用户正式授权首次production manifest签名、v1.0.0/v1.0.1正式GitHub Release及同Schema真实在线升级验收。Stage9 `IN_PROGRESS / S9-T06_CURRENT`；S9-T06 `IN_PROGRESS / NOT_ACCEPTED`。Stage8及T01～T05保持CLOSED，不创建T07。
- 新鲜fetch确认main=origin/main=fd541b88f071badd6a692373e82deaf6146c10ee、clean/0/0，原无T06，现已建立Task/Acceptance/Production Release Analysis。指定T05事务Analysis文件实际缺失，保留事实，不伪称已读。
- 治理角色只治理/完整diff/独立复验，新的Terra medium/priority实施并提交后停、不push。先核实持久私钥保管、发布权限、可用clean Windows与正式身份合成隔离；硬阻塞则PAUSED_PRODUCT_REVIEW / NOT_ACCEPTED，不创建公开资产。
- 产品当前仍1.0.0；未生成production key，未发布tag/Release/assets，未做真实升级，未访问正式数据。本卡新鲜Release/build/EF门禁尚未执行，1040/1040属于T05历史。
- 严禁Schema/migration10/Domain业务变化、正式门店数据、Reset/Undo、上报、强制静默升级、改AppId/安装根/数据根/lowest。详见S9-T06 Task与Acceptance；完成或暂停后记录真实证据，不自动关闭Stage9。

## S9-T05正式关闭（2026-09-05，覆盖下方历史停止点）

- S9-T05 `TECHNICALLY_ACCEPTED / CLOSED`；Stage9 `IN_PROGRESS / WAITING_NEXT_AUTHORIZATION`。开工 main=origin/main=`a8e1414030864c67e5cc82ee81c6062dc20c724d`、clean/0/0；fresh Terra 实施并提交、不 push，Sol 治理、完整 diff 与独立复验。产品仍 1.0.0，未创建 S9-T06。
- 已有独立 self-contained Updater、持久 journal、精确 operation 路径、完整树 staging/switch/rollback、父进程身份、mutex、候选只读 UI/DB ACK 与 manual recovery。正常流程只请求主程序优雅退出；ACK 前不执行 Initialize/Migrate/补算/reminder 写入。
- Sol 新鲜隔离证据：WPF self-contained smoke/verification exit0；preparer3/3、成功硬杀27/27、回滚硬杀9/9、失败矩阵8/8、live-parent及双Updater通过；migration9 DB SHA不变、无WAL/SHM、锁/ACL/junction无混树。
- 最终无过滤 Release 1040/1040、0 failure/skip、10m56s；build0/0；EF无漂移、migration9末条固定；diff/secret/禁止项通过。详见 `../ACCEPTANCE/S9-T05.md`、`../ACCEPTANCE/S9-T05-HARD-KILL-RESULT.json`、`../ACCEPTANCE/S9-T05-UPDATER-RESULT.json`。
- 未访问正式安装/数据/数据库/备份；未创建 GitHub Release/tag/asset，未配置生产 trust anchor/private key，未发布真实更新包，未执行 migration。开发机合成通过不代表门店投产。
- 下一卡仅建议 S9-T06 首次签名正式 Release 与干净 Win10/11 同 Schema 1.0.0→1.0.1 端到端升级验收；所有正式发布身份和版本变化须新授权，不含 Schema/migration、正式业务数据、重置或 Undo。

## S9-T04正式关闭（2026-09-05，当前权威状态）

- S9-T04 TECHNICALLY_ACCEPTED / CLOSED；Stage9 IN_PROGRESS / WAITING_NEXT_AUTHORIZATION。Stage8、S9-T01/T02/T03继续CLOSED，S9-T05未创建。开工c6200ca8507c7f8d99f7f40047b6be291d6ff70b与origin一致、clean/0/0。全新Terra medium/priority完成生产/测试4dbf088ff024a9818418e33bc67868dd0447b604并停止不push；Sol只治理、完整diff和独立复验。最终main以普通push/fetch回执为准。
- schemaVersion1、stable/win-x64、严格三段数值SemVer；raw manifest bytes RSA-PSS/SHA256，固定repo/Release/tag/asset身份与有限批准CDN跳转。流式TEMP/GUID ZIP下载，256MiB包/512MiB展开/4096条目等硬上限，size/hash/受限ZIP/EXE及DLL版本/目标migration声明验证后只返回VerifiedUpdatePackage。
- 生产trust anchor仍未配置并在网络前fail-closed；测试RSA只内存，无私钥/PAT/secret进入repo/publish。产品仍1.0.0。立即更新显示准备/下载/校验及进度、可取消，成功明确后续版本才启用安装；重复点击single-flight，正常退出等待取消清理。没有程序替换、Updater事务、候选执行、更新migration、正式Release/tag/asset、重置或Undo，正式数据根未访问。
- Sol最终Release核心128/128（真实合成HTTP含完整publish ZIP）、实际WPF12/12、退出子进程1/1、专项27/27；fresh无filter Release1023/1023，0failure/error/timeout/aborted/skip，10m45s；build0warning/0error，EF无漂移，migration9末条20260901155124_AddPolicyAndBaselineFoundation，diffcheck通过。生产代码与Schema/依赖/installer身份边界审查通过。
- 最终self-contained publish420文件164297044字节；合成旧客户端0.9.9→原版1.0.0完整ZIP88096081字节Verified，未执行候选。真实匿名repo/public、Release0/latest404，01:34:39生产客户端NoPublishedRelease；成功不是实际GitHub Release下载证据。证据见ACCEPTANCE/S9-T04.md、S9-T04-DOWNLOAD-RESULT.json、S9-T04-SOL-VERIFY及ANALYSIS/S9-T04-UPDATE-PACKAGE-PROTOCOL.md；失败历史保留并区分harness错误与生产缺口。
- 限制：正式发行需配置生产签名公钥/离线私钥保管，严格ZIP及CDN策略变化需复审；Verified TEMP未来消费前须重验。自动化不替代用户GUI/干净机器；全量空return不算高规模/真实Excel，离线restore不算在线漏洞审计；T02旧安装器历史产物，最终发行重建。既有Stage8物理介质/恢复边界不变。
- 下一步仅建议S9-T05独立Updater身份/journal/隔离staging程序切换与回滚，真实数据与跨版本动作必须先满足S9-T01保护/握手及新授权；没有创建Task或开始实施。普通push main、fetch核对clean/HEAD=origin/0/0后停止，等待用户明确授权。

## S9-T03历史关闭（2026-09-05，由上方T04覆盖）

- S9-T03 TECHNICALLY_ACCEPTED / CLOSED；Stage9 IN_PROGRESS / WAITING_NEXT_AUTHORIZATION。Stage8、T01/T02保持CLOSED；T04未创建。开工main=origin/main=82c3fd16423c9772e4c2f4f41a8b56cbbf67c669、clean/0/0。全新GPT-5.6 Terra medium/priority实现523918b及可读性修复15434e53a6809fd654337fee0332c851c238a922，已提交停止；Sol只治理、完整diff、独立复验。最终main见普通push/fetch回执。
- Sol独立协议40/40、最终实际WPF16/16、相关回归32/32；fresh无filter Release1017/1017、0failure/error/timeout/aborted/skip，约11m01s；build0warning/0error、EF无漂移、migration9末条20260901155124_AddPolicyAndBaselineFoundation、diffcheck通过。空return不当高规模/真实Excel证据；初次37/40和修复过程保留。
- 当前版本从正式程序集读取，仍1.0.0；匿名固定HTTPS latest元数据，严格稳定三段tag和数值比较，8种结果，5秒总超时/256KiB响应/1000字符纯文本。核心初次读取完成后每进程一次非阻塞检查；退出取消/晚到保护，只有新版提示，无轮询或持久snooze。稍后本进程不再提示，下次启动可再查；立即更新明确显示尚未启用，不下载、不退出、不替换。
- 真实匿名HTTP：2026-09-04 23:27:41～44 +08:00，repo200/public、list200/0、latest404；无Authorization。生产客户端23:44:06实际NoPublishedRelease。新版由合成协议/WPF验证，没有创建Release/tag/资产。private与无Release可能同为404，仍静默安全、不索要token。
- 最终fresh self-contained发布420文件/164235092字节；显式TEMP/GUID实际WPF核心启动exit0/ready1/程序树SHA256不变；合成DB副本integrity ok/FK0/migration9。smoke-exit分支跳过更新检查，更新链由独立协议、真实客户端网络、实际WPF及源码链分别证明。正式数据根未探测/访问/哈希/复制，无Schema/依赖/installer契约变化。
- 证据：ACCEPTANCE/S9-T03.md、S9-T03-UPDATE-CHECK-RESULT.json、S9-T03-GITHUB-SMOKE.json、S9-T03-SOL-VERIFY；契约见ANALYSIS/S9-T03-PUBLIC-RELEASE-CONTRACT.md。TEMP产物可能被清理。T02旧49MB安装器仅历史产物，不是本卡最新可交付版本，Stage9最终须重新构建。
- 剩余边界：当前没有真实新版Release/资产下载/Updater/程序替换/跨版本保护；开发机WPF不替代干净Win10/11门店GUI，未签名/SmartScreen及Stage8既有风险不变。无重置/Undo/secret。
- 下一步仅建议更新包下载与校验：先冻结manifest/原始字节RSA-PSS签名及公钥信任，再以合成资产验证隔离下载、大小/版本/平台/签名/SHA256及失败清理；不做Updater/替换/迁移/正式Release。未创建T04，等待用户新授权。普通push main、fetch确认clean/HEAD=origin/0/0后停止。

## Stage9 / S9-T02正式关闭（2026-09-04，历史回执）

- S9-T02 TECHNICALLY_ACCEPTED / CLOSED；Stage9 IN_PROGRESS / WAITING_NEXT_AUTHORIZATION；Stage8、T01保持CLOSED。T03未创建。开工main=origin/main=34c336a3f03e823048e5987d102001911527e5b2、clean/0/0；最终生产/测试5a8fe57d53697afcec066c8e70d63a346d45c5df，之后仅治理收口。最终main以本次普通push/fetch回执为准。
- 全新GPT-5.6 Terra medium/priority实施并已提交停止；Sol仅治理、完整diff、独立复验。Sol两轮真实独立身份A-I各9/9、preflight12/12；最终无filter Release996/996，0failure/error/timeout/aborted/skip、10m35s；build0warning/0error；EF无漂移，migration9末条20260901155124_AddPolicyAndBaselineFoundation。门禁空return不当高规模或真实Excel证据。
- 正式产物StoreExpiryInspector-Setup-1.0.0.exe，49,294,439bytes，SHA256 AE1608F57CA66BCA08FC5545DD7674E8F4057BD4F46420EE6390E3B227F8F258；本地TEMP/4b26880c-ba60-471c-8bde-2afd6401e5ee/production-final。Inno6.7.3，未签名；AppId={8F90E64E-5B0D-4FA8-A854-EEA2F4D1EC14}，后续1.x不变。lowest/current-user，固定%LOCALAPPDATA%/Programs/StoreExpiryInspector/app/StoreExpiryInspector.exe；完整420文件/164218708bytes self-contained payload，包审计无DB/secret/开发路径命中，二进制不入Git。
- 首装双快捷方式/Run on、同版本修复且尊重Run off、卸载全部数据保留、重装读取原合成BLOB/设置、数字降级阻断及旧/未知/坏Schema写入前阻断均实测通过。源DB/sidecar只读复制，SQLite仅检查临时副本，不调用Migrate/修复/业务写入。正式身份EXE从未执行，正式数据根从未探测/访问/哈希/复制；测试安装/Run/双快捷方式/卸载项清理，合成数据可恢复保留。
- 证据见ACCEPTANCE/S9-T02.md、S9-T02-INSTALLER-RESULT.json与S9-T02-SOL-VERIFY。历史返修/沙箱安装失败/NuGet缓存问题保留；后续已独立复验，不冒充原始候选通过。最终源码无业务算法/Schema/ModelSnapshot/index/生产PRAGMA/依赖变化。
- 开发机Win11静默矩阵不替代干净Win10/11或门店GUI；未解决SmartScreen信誉。Stage8合法外来WAL来源、严重坏当前无法保护时Restore阻断、真实物理介质安全限制不变。TEMP产物可能被系统清理。
- 下一步仅建议S9-T03公开版本元数据及离线友好检查/提示，明确无Release/无更新/网络失败；不自动建卡、不下载/替换/实现Updater，不创建正式Release。重置数据另需授权，Undo永久取消。按授权普通push main核对clean/0/0后停止。

## Stage9 / S9-T02正式启动（历史，已被上方关闭状态覆盖）

- 用户仅授权S9-T02当前用户首次安装器与数据保留安全门禁；Stage9 IN_PROGRESS / S9-T02_CURRENT，T02 IN_PROGRESS / NOT_ACCEPTED。开工fetch main=origin/main=34c336a3f03e823048e5987d102001911527e5b2、clean/0/0，无既有T02，现已建立Task/Acceptance。
- Sol仅治理/完整diff/独立复验；全新Terra medium/priority实施并提交后停、不push。仅官方Inno6、lowest当前用户、稳定AppId/Programs/app入口、首装Run on与重装尊重off、卸载保留全部数据、降级及非健康migration9只读阻断。全部测试严格TEMP/GUID独立身份，禁止探测正式库。
- 待实际安装器A-I矩阵、包安全、fresh Release/build/EF/migration门禁；尚未验收。Stage8及T01仍CLOSED。不创建T03、不实现Updater/Release/重置/Undo。验收后普通push main并停止。
- 实施中回执：治理a389cd4；Terra候选c4acd2f及返修b6b60d8/62ef4d3均未获验收，尚未完整执行A-I；Sol已退回同一Terra补齐。已有11/11仅实施者局部单测，不是安装矩阵或Sol全量证据。无push、无正式身份安装。官方Inno6.7.3已以签名Valid/Pyrsys B.V.的portable模式准备，详见Acceptance。

## Stage9 / S9-T01正式关闭（2026-09-04，覆盖下方开工状态）

- S9-T01 TECHNICALLY_ACCEPTED / CLOSED；Stage9 IN_PROGRESS / WAITING_NEXT_AUTHORIZATION；S9-T02未创建。开工main=origin/main=7c1fa2d4b0178314816e79663765f952c66d3095、clean/0/0；最终生产/测试0c6d0e4e37b3d18f3fdefa65c7c80f0108d41b11。最终治理提交后普通push main、fetch核对clean/0/0，实际SHA以发布回执为准。
- 全新GPT-5.6 Terra medium/priority实施e86d91c/f1eb6c5/abb31bb/0c6d0e4并已停止；Sol治理、完整diff审查和独立复验，未代写生产代码。Sol最终无filter Release991/991、0failure/error/timeout/aborted/skip、10m38s；build0warning/0error、EF无漂移、migration9末条20260901155124_AddPolicyAndBaselineFoundation；无业务/Schema/index/包变动。
- 总纲第2/3/37/38节和D-007明确2026-09-04决策覆盖旧在线升级禁令；首次当前用户安装EXE、后续GitHub在线检查+用户确认后自动升级，核心业务离线，无静默强制升级。Version=1.0.0，Assembly/File=1.0.0.0，UI读程序集；发布为win-x64 self-contained多文件，不trim。
- Sol最终发布164508896bytes（156.8879MiB）；移目录前后真实WPF双跑，两个独立TEMP/GUID库与程序树hash门禁通过。另关闭全局.NET搜索、指向不存在DOTNET_ROOT直接启动成功，hostfxr/CoreCLR实际从发布目录加载；三份合成DB各339968bytes、integrity ok/FK0/migration9。不是干净机器或用户GUI验收。
- 数据逻辑默认根仍%LOCALAPPDATA%\StoreExpiryInspector（data/app.db、backups/pre-import、logs；settings/runtime/原始Excel BLOB在库内），与拟安装Programs\StoreExpiryInspector\app解耦。正式根/库未探测/访问/哈希/复制。隔离参数仅接受本次全新TEMP/GUID，拒绝已有根、未知/缺失参数和ReparsePoint祖先；隔离自启动读写拒绝。
- GitHub匿名仓库200/public、Release列表200/0、latest404，无private token blocker，但未有实际Release资产下载。客户端严禁PAT/secret。Inno Setup lowest当前用户首装方案冻结，保留用户接受未签名EXE的历史决策；完整安装器/Updater/签名协议/跨版本回滚尚未实现。
- 现有Restore不能直接当跨版本回滚器；后续升级专用保护+staging迁移+独立Updater/journal/健康ACK+旧程序/旧DB回退契约见ANALYSIS/S9-T01-UPDATE-ARCHITECTURE.md。Stage8继续CLOSED：合法外来WAL来源未证明，严重坏当前无法保护时Restore阻断，真实断电/SSD/磁盘/文件系统/bit rot/不可读介质安全未证明。
- 首次NuGet TLS失败、两条旧静态门禁返修、一次S7T03异步超时和原样单项1/1后新鲜全量通过的过程均保留于Acceptance。10个显式空return不算高规模/真实Excel验证。证据索引S9-T01-RESULT.json；TEMP原件保留但可能被系统清理。
- 下一步仅建议S9-T02：当前用户Inno首装、稳定路径/AppId、快捷方式、自启动偏好、同版本重装/卸载保留合成数据；旧Schema无保护升级/降级必须阻断。未创建，等待用户新授权。Undo永久取消、重置数据另立需求。现在停止。

## Stage9 / S9-T01正式启动（2026-09-04，覆盖下方阶段停止点）

- 用户授权Stage9及仅S9-T01：产品基线修订、win-x64 self-contained发布/版本/数据路径底座、安装与在线升级架构。状态IN_PROGRESS / NOT_ACCEPTED，未创建S9-T02。Sol先fetch：main=origin/main=7c1fa2d4b0178314816e79663765f952c66d3095，clean、0/0；治理目录实查无既有Stage9文件。
- 总纲保留旧规则并注明被2026-09-04新决策覆盖：首次当前用户安装EXE；后续GitHub在线检查→提示→用户立即更新/稍后提醒→自动下载校验升级重启；核心业务完全离线，无后台静默强制升级。禁止PAT/secret客户端分发、总部管理、Undo、重置。
- 正式实施者为全新GPT-5.6 Terra medium `/root/s9_t01_terra_medium`，提交后停、不push；Sol不写生产代码，待完整diff和独立门禁后验收。首次误用全历史fork的agent已立即中断，未作为正式实施者。
- 所有发布/运行验证仅TEMP/GUID合成SQLite，不访问正式库、不用旧Junction脚本。原数据路径已在用户LocalAppData；需集中路径及隔离启动验证，默认路径保持不变。
- GitHub匿名HTTPS实际返回仓库200/private=false/public，Release列表200/0条、latest404；当前无private token blocker，未证明资产下载。后续外部发布不在本卡范围。
- Stage8继续CLOSED，不重开。合法外来WAL来源不可证明、严重坏当前无法保护时Restore阻断及物理介质风险未证明，全部保留。最终验收后普通push main并停止，下一卡另需用户授权。

## Stage8 / S8-T06正式关闭（2026-09-04，覆盖下方历史状态）

- Stage8及S8-T06 TECHNICALLY_ACCEPTED / CLOSED，T01～T06 CLOSED；Stage9 NOT_STARTED。开工fetch main=f981211、clean/0/0；最终测试候选17ebb6c，之后只有治理归档。全新Terra medium/priority文档31d3522/b8644ce，已停止；Sol完整审查、独立复验，无生产/测试代码变化。
- 新鲜100k/300k读20路径无异常，首屏193.33ms/深页367.69/History344.05/Reminder784.25（median）；100k Excel50191.79ms，完整业务断言/integrity ok/FK0；无数量级回退。写中及跨250post故障回滚2/2，恢复代表6/6，大库225.43MB/Backup5.83s/Restore20.55s。
- 独立18/18 Kill（9pre/9post）；最终无filter Release984/984、0failure/skip/error/aborted，内含42/42常规Kill（33pre/9post）、Revision16/16、S8T05 39/39；历史48/48独立保留。10个空门禁不当压力或真实Excel证据。build0/0、EF无漂移、migration9末条固定；禁止项及diff check通过。
- 未发现本卡生产一致性bug。合法外来WAL可改变业务而结构/FK/migration仍健康，来源防护未实现；严重坏当前不能保护时Restore阻断，不直接救援；物理断电/磁盘/SSD/文件系统/介质安全未证明。所有实验合成TEMP/GUID，正式库未访问，旧隔离事件不调查、不改写。
- 已建立STAGE-8-CLOSEOUT.md及S8-T06-RESULT.json。发布前fetch远端仍f981211；本治理提交后普通push main、再fetch核对clean/0/0，最终SHA见发布回执/回复。停止，不创建Stage9/升级/安装器/重置/Undo/新防护任务。

## S8-T05正式关闭（2026-09-04，覆盖下方历史状态）

- S8-T05 TECHNICALLY_ACCEPTED / CLOSED；最终代码/测试e0e5a0dc6eaba2d347246daa571a35cbbbcf3004。Stage8仍IN_PROGRESS，S8-T01～T05关闭，等待后续授权；不创建S8-T06。
- Sol最终无filter Release984/984、0failure/skip、exit0，23m36s；独立专项100/100；新鲜48/48硬Kill（排查15/库存9/10k导入18/100k导入6），36提交前回滚、12提交后保留，全部integrity ok/FK0/migration9，48原worker均退出。
- build0warning/0error、EF无漂移、migration9末条20260901155124_AddPolicyAndBaselineFoundation；完整diff和diff --check通过，无Schema/ModelSnapshot/index/生产PRAGMA/dependency/csproj/slnx变化。源码最终仅已有库预检、启动失败终止和Restore测试接缝；最后两个治理前修复为旧换行断言及marker共享冲突测试同步。
- 安全边界：坏备份拒绝；严重坏当前无法保护时Restore安全阻断、不覆盖。合法外来WAL可在结构健康时改变业务状态，来源防护未实现，作为用户接受的known limitation保留；不新增provenance/sidecar删除/自动恢复模式。
- 真实100k Batch/300k History，DB和backup225427456bytes。原backup7258ms/restore23089ms保留；最终新样本6946ms/22552ms，额外integrity/FK2275ms，完整指纹一致。均单样本非SLA。
- 旧982/983全量、WAL原4/5及marker失败均保留，不改写为通过。详见S8-T05 Acceptance与S8-T05-CORRUPTION-RESULT.json（原始大DB只留TEMP）。未访问正式数据库，不声称物理硬盘/SSD/文件系统/真实断电安全，不冒称真实GUI验证。
- 此治理提交后按授权普通push main并核对clean/0/0；最终main SHA以提交/推送回执与最终回复为准，验收后不改生产/测试。停止，不启动S8-T06/Stage9/在线升级/重置/Undo。

## 本轮恢复验收过程（历史）

- 用户正式接受结构合法外来WAL来源不可识别为已知能力限制，不新增防护；旧失败及业务漂移事实保留，不算防护成功。S8-T05 IN_PROGRESS / NOT_ACCEPTED，待最终HEAD新鲜无filter Release全量0failure后关闭。
- 当前最终候选e0e5a0dc6eaba2d347246daa571a35cbbbcf3004；ad7ec3b全量982/983因旧marker共享冲突失败，已仅修复测试同步并新增无数据库专项。Sol最终专项100/100；新的无filter全量启用既有100k硬杀，正在运行，不能预报通过。
- 已重新fetch，origin/main仍80b2c57；本地已知aa27b18及治理/测试返修均保留。接续本卡Terra仅调整测试契约并提交，Sol独立验收，不代写生产代码。禁止正式库访问及新Task/Schema/PRAGMA/防护模式。

## 历史停止点（裁决前）

- S8-T05 = PAUSED_PRODUCT_REVIEW / NOT_ACCEPTED。真实非空外来WAL导致商品指纹漂移，但SQLite integrity ok/FK0/migration9且Initialize接受。Sol独立WAL4+UI1：4/5，mismatched失败，证据见S8-T05 Acceptance；不得改断言冒充安全。
- 用户header坏库无法保护时安全阻断裁决已落实；不解决本次结构健康的外来WAL来源错配。需独立明确风险边界/防护契约，不自行删sidecar、拒绝正常WAL、改Schema或PRAGMA。
- 无filter Release983总计982通过1失败（旧UI换行断言）；仅输入换行正规化后的专项通过，但随后真实WAL失败，未再声称全量通过。build0/0、EF无漂移、migration9、禁止项diff无变化、diff --check通过。
- HEAD aa27b1838759a39a18c0d2147f2fec78a12eb602；已知origin/main80b2c57，9/0，未push。治理及S8T05/Stage4测试返修未提交且保留；无reset，无生产数据库访问。两名实施者已停止，Sol不代写生产代码。不创建S8-T06。

## S8-T05正式开工（2026-09-04）

- 当前S8-T05：IN_PROGRESS / NOT_ACCEPTED；Task/Acceptance已建。开工重新fetch main=origin/main=80b2c57f599fec736a0e191b13e8ae923810a633，clean、0/0。全新Terra `/root/s8_t05_fresh_terra` medium/priority实施中；中间提交6b5a726/2568037，未push；Sol独立Release新测试17/17仅局部覆盖，不是整卡验收。详见Acceptance中间记录，继续完成剩余矩阵。
- 用户裁决：严重坏当前库无法生成合格保护快照时必须安全阻断，不staging/replace、不删改坏文件、不损坏健康备份、不伪造成功记录；属于预期，不要求现有Restore救援无法保护的坏库。不得绕过保护或新增Restore模式。
- 其余隔离损坏/备份拒绝/健康恢复/失败回退/大库/Release门禁按S8-T05 Task。仅TEMP/GUID合成数据，严禁正式库访问。S8-T01～T04仍关闭；下方旧停止点为历史回执。
- 实施交接：旧`s8_t05_fresh_terra`因未完成矩阵及WAL证据缺陷已停止；全新`s8_t05_completion_terra` medium/priority接续同卡，保留旧commit/diff，未push。详见Acceptance开发记录。
- 不创建S8-T06，不实施Undo/重置数据/Stage9/在线升级。完成独立验收后才关闭、普通push main并停。

## S8-T04正式关闭（2026-09-04）

- S8-T04：`TECHNICALLY_ACCEPTED / CLOSED`。Stage8仍IN_PROGRESS，S8-T01～T04关闭，WAITING_NEXT_AUTHORIZATION；S8-T05未创建。
- 本轮恢复核对：origin/main2142cf7、本地治理bfa87d9、clean、ahead3/behind0；实现9eff272，代码/测试均未变化。旧本地main61a057e仅落后、无分叉，归档只作正常fast-forward。
- 用户仅为Release全量特别放行4类既有TEMP/GUID SQLite损坏回归。Sol无filter全量944/944、0failure/skip、exit0，12.1949分钟；PreImportSnapshotServiceTests 6/6、S7T02DatabaseRestoreTests 9/9、S7T03DatabaseBackupRestoreViewModelTests 30/30、ImportUndoEligibilityTests 42/42，全部87项真实通过。不是旧838/838过滤回执。
- 既有48/48 Process.Kill矩阵及TRX哈希保留不变：36次提交前完整回滚、12次提交后完整保留，全部integrity ok/FK0/migration9/可重开可写。本轮没有额外重跑21分钟的100k专项；标准全量中的常规用例按原实现执行。
- Sol新鲜Release build0warning/0error，EF无漂移；migration --no-connect仍9，末条20260901155124_AddPolicyAndBaselineFoundation。生产、Schema/ModelSnapshot/index/PRAGMA策略/dependency/csproj/slnx不变，diff --check通过；未做在线NuGet漏洞审计。
- 未访问、复制、哈希、损坏、恢复或调查正式库；损坏仅发生在获准既有测试的TEMP/GUID对象。不新增损坏场景、不恢复Undo规划。未确认生产一致性bug；开发I/O及其他失败历史继续保留，不以本轮通过改写其原因。
- 不证明真实断电、磁盘/SSD、文件系统或介质损坏安全。不实施重置数据、Stage9、在线升级或S8-T05。
- 代码9eff272；本文件所在提交为最终治理收口。发布方式为正常fast-forward本地main及普通push origin main，不force；发布后核对HEAD=origin/main、clean、0/0，最终SHA以Git回执为准。
- Acceptance：`.ai-dev/ACCEPTANCE/S8-T04.md`；48次逐项摘要：`S8-T04-CRASH-RESULT.json`。全量TRX位于TEMP/StoreExpiryInspectorS8T04Sol/bc9bacb459a9413abaf455c5d3dc2811，SHA256 8097D52F940C1CEEC09494707AA2E551DF4FC4D5796E338DEC61BB58084F59BE。
- 停止。S8-T05仅为建议方向，等待用户新授权；下方S8-T03为历史回执。

## S8-T03历史状态与停止点

- Stage8：`IN_PROGRESS / S8-T03_CLOSED / WAITING_NEXT_AUTHORIZATION`。S8-T01、S8-T02、S8-T03技术验收关闭；S8-T04～T06仅候选，未创建Task。Stage9、在线升级未开始。
- V1-F01、V1-F02、V1-F03、V1-F03-I04、V1-UI-01继续CLOSED；V1-UI-01保持GUI_ACCEPTANCE_PASSED。本卡没有启动真实GUI，不冒充新的GUI人工验收。
- 用户永久取消“撤销上一次Excel导入”。不新增Undo executor/UI，不验证或规划Undo eligibility，不用Restore冒充Undo。既有代码未顺带删除。
- 设置页“重置数据”仅为未来独立需求，未建Task/实施；以后另定清理范围、自动备份、二次确认与设置保留。
- 正式数据库禁止调查/读取/哈希/修复。本卡全部显式TEMP/GUID，未访问正式库；S8-T02旧轮疑似访问仍未核实，不借本卡通过改写历史事件。

## Git / 角色

- 开工重新fetch：HEAD=origin/main=`e4c628c0e2df261c2da5761b8398c2ecd919452c`，clean、0/0。推送前重新fetch仍同SHA，无远端新提交。
- 治理`8b59746`；基座`01efa1b`、测量`2149d63`；生产优化`617e3dd`、zero跟踪修正`20fc8df`；真实矩阵`6ed428d`；强断言/计量最终`31ace5b70ee03969c11213c4aeb623de7bd196fc`。
- 本卡全新Terra medium/priority实施；中间交付不完整曾退回及更换全新Terra，详细链见Acceptance。未复用旧卡Terra，全部已停止。Sol只写治理并独立审查/运行验收，未写生产代码。
- 工作分支`codex/s8-t03-import-stability`，按用户原授权普通`git push origin HEAD:main`归档，不force。最新归档SHA以包含本记录的提交/推送回执为准，代码验收基线为31ace5b。

## Sol独立验收（2026-09-04）

- 隔离9/9，默认factory=0；相关Import/隔离/本卡回归158/158（其中4高规模门禁空返回不算压测）。
- 实际10k/50k/100k压测3/3；最新回滚专项10/10，含真实100k Stage2完成后失败和跨250商品后置分组失败。
- Release全量925/925，0failure；Release build0warning/0error；EF无漂移；migration代码清单9，末条20260901155124_AddPolicyAndBaselineFoundation。设计时`:memory:`，migration list用--no-connect，不连接正式库；未做在线NuGet漏洞审计。
- 成功/失败integrity_check=ok、FK=0。失败前后完整业务/BLOB指纹一致；snapshot失败阻断写入，已生成快照可依旧契约残留但业务全回滚。
- 完整diff、Schema/ModelSnapshot/index/dependency/csproj/slnx与git diff --check通过。生产仅4个Import链文件，无业务算法或S8-T02读取改变。

## 性能与局限

- 单样本n=1，10k4467.94ms、50k23510.82ms、100k49210.72ms；100k Excel7255524bytes，DB逻辑45010944bytes，物理main+wal+shm91611720bytes。
- 10k Release Before313705.59ms；100k Before在planner发生too many SQL variables。优化后均成功。热身/运行顺序不同，不宣称固定倍率或SLA；未测50k Before。
- 最慢100k post20760.58ms，Batch Save11150.99ms；仍有逐商品查询/SaveChanges。100k execute-context SQL321895、SaveChanges15146，最大参数500；没有宣称N+1全部消除。结束时working-set约1.07GB，不是峰值。未观测OOM/timeout/lock/crash。
- 数据分布为每商品2批、10大类、最多1000既有商品、含库存0；scope先由seed建立，不宣称覆盖100k首次scope冷启动或所有倾斜分布。真实强杀/断电/损坏未做，未来仍需单卡授权。

## 证据与后续边界

- Task：`.ai-dev/TASKS/S8-T03.md`；详细命令、JSON路径/SHA、Before/After、失败矩阵与统计局限：`.ai-dev/ACCEPTANCE/S8-T03.md`；阶段：`.ai-dev/STAGES/STAGE-8.md`。
- 原始产物只在本机TEMP下`StoreExpiryInspectorS8T03/<GUID>`与`StoreExpiryInspectorS8T03Sol/<GUID>`，不包含正式数据，不上传大SQLite/XLSX。S8-T02归档事实仍见其Acceptance，不冒充本卡新鲜压测。
- 完成本卡普通push后停止；不得创建S8-T04或重置数据Task，不做强杀/断电/人工损坏、不启动Stage9或在线升级。下一卡需用户明确批准。
