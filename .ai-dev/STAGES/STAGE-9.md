# Stage 9｜安装、发布与在线升级交付

## 当前停止点：GUI 1.0.2 诊断候选已验证，等待原失败实机日志

S9-T06 `IN_PROGRESS / NOT_ACCEPTED / USER_GUI_BLOCKED`；Stage9仍 `IN_PROGRESS / S9-T06_CURRENT`。根因未确认，未宣布修复；Win10 NOT_VERIFIED，不创建S9-T07。

- 全新Terra实施并停止，Sol完整diff/独立复验；候选源 `e5ecc65762671f3a29cbbee589aead57714c7e63`，实际产品/FileVersion=1.0.2/1.0.2.0，未创建1.0.2 tag/Release/Setup。公开产品仍1.0.1，既有两版tag/资产不改写。
- 真实完整App启动→更新提示→实际“立即更新”→GitHub metadata/manifest/sig/ZIP→production验证Verified；ZIP107875191字节/SHA689F7A872ECE50F2177A6349EF6DEE9637A8AB655798899EFD0E3C76BDDDD169。仅TEMP/GUID合成数据，模拟source100→101，安装delegate硬关闭；用户包移除Updater目录。这是开发机诊断准备成功，不是原实机正式升级成功。
- GUI取消返回Cancelled并正常退出；关闭提示、Hide、Closed、CTS/check CTS和App退出事件可读。独立阶段/超时/取消/重定向/脱敏/reparse边界5/5；默认诊断关闭smoke exit0。合成DB integrity=ok/FK0/migration9，不是升级After证据。
- 最终fresh无filter Release1055/1055，failure/error/timeout/aborted/skipped0；build0/0，EF无漂移，migration9末条固定。第一轮1054/1055的启动源码静态契约失败保留；Terra显式分支修正后全量重跑通过。
- 诊断ZIP 71011157字节，SHA256 `529936669cf06ee274497e60dad359940dda8dad873517c7890c822b4d689f07`；423项扫描0已知secret；私钥头字面量仅为既有包验证器拦截规则，已独立辨明。无私钥读取、正式数据访问或Windows安全设置改变；EXE仍无Authenticode。
- 用户下一步：原失败标准Win11解压诊断ZIP，运行Start-GuiDiagnostic.cmd，确认实际102/source100/只准备横幅，点击一次立即更新，托盘退出后回传JSONL与结果。无需重复独立网络探针，不做After。具体清单与全部证据在S9-T06-GUI-SOL-VERIFY。

以下内容保留为历史阶段记录。

## 当前：失败实机核心 Verified，转真实 GUI 生命周期诊断

- S9-T06仍为 `IN_PROGRESS / NOT_ACCEPTED / USER_GUI_BLOCKED`。已独立读取用户原Win11 JSONL：正式100程序集/runtime实际完成Check/Refresh、manifest/signature及107875191字节ZIP，Prepare Verified，无Updater。不得继续将GitHub/DNS/TLS/CDN/冻结redirect/签名/资产/开发机代理环境变量差异当作既定根因。
- GUI与探针调用比较及Sol原100 GUI隔离动态复验见 `S9-T06-GUI-DIFFERENTIAL.md`；静态未证实CTS或handler根因。下一步仅全新Terra实施默认关闭的真实GUI安全诊断候选，模拟source100→公开101，只准备不安装；候选不是正式修复发行。无需用户重复独立网络探针或提交After。
- 两版公开资产/tag继续不可变；Win10 NOT_VERIFIED，Win11 USER_GUI_BLOCKED；不关闭Stage9/T06，不创建T07。以下既往网络排查、停止点及发布记录保留为历史。

## 当前：正式在线升级人工阻塞，诊断中

- 用户报告标准Win11实机与Windows Sandbox均可运行正式1.0.0、发现GitHub1.0.1，但“立即更新”显示“正在准备更新包”约十几秒后报“无法连接更新服务器”。S9-T06现为 `IN_PROGRESS / NOT_ACCEPTED / USER_GUI_BLOCKED`；Stage9仍 `IN_PROGRESS / S9-T06_CURRENT`。此前本机自动化成功仅保留其隔离证据范围，不能覆盖此次人工失败。不要继续After，不关闭本卡，不创建S9-T07。
- 实机人工回执：正式Setup可安装、无需以管理员身份运行、桌面快捷方式及中文应用名正常、1.0.0正常启动。向导通用英文文本是未本地化，不是乱码，本轮非阻塞。Sandbox中文方框仅记录环境差异判断，不将实机升级失败归因于Sandbox。
- 用户保留Init/Before及运行、发现更新、连接失败、实机Setup截图；当前仅收到文字回执，未伪称已独立读取附件。完整正式GUI升级未成功，clean Win11为USER_GUI_BLOCKED，Win10仍NOT_VERIFIED。
- 本次fetch main=origin/main=b4a793fc79f4222ee75a2b73964fbfcb0744a725，clean/0/0。先只读审查生产下载链，增加仓库外/验收侧安全诊断，分辨RefreshRelease/Manifest/Signature/Redirect/Package及DNS/TLS/连接/timeout，不先猜根因。
- v1.0.0/v1.0.1及所有公开字节/tag保持不变；不删除失败证据，不先创建1.0.2、不先改生产代码。若证据确认需修复，另建全新Terra medium/priority实施；Sol仅治理/独立复验。修复版本仍同Schema9、不改业务/AppId/安装根/data root。正式新版本实机GUI成功后才恢复本卡验收。

## 当前停止点：两版已正式发布，等待独立 Win11 GUI 回执（2026-09-05）

- S9-T06 `IN_PROGRESS / NOT_ACCEPTED / USER_GUI_PENDING`；Stage9 `IN_PROGRESS / S9-T06_CURRENT`。此前无 clean OS 暂停已由用户独立 Win11 方案解除。Win10 `NOT_VERIFIED`，Win11 `USER_GUI_PENDING`；不关闭本卡/Stage9，不创建 S9-T07。以下早期暂停与预发布段落仅为历史，不代表当前状态。
- v1.0.0：https://github.com/CodeVoyage3/xiaoqipaichanuanjian/releases/tag/v1.0.0 ，tag/source `7044a984ddca757d8ae9350fbc523800bd769796`。v1.0.1：https://github.com/CodeVoyage3/xiaoqipaichanuanjian/releases/tag/v1.0.1 ，tag/source `99eb7510b3b1288e680551f92367e0ccc6c25755`。均 stable、非 draft/非 prerelease、4 项资产。代码版本提交 `81bc6703415c1186bc133432445f41371537628b`；当前产品1.0.1。不得改写 tag/替换同版本公开字节。
- Sol 独立匿名 repo/list/latest/两版 release/tag 接口 HTTP200，latest=v1.0.1；两版四资产实际匿名下载 size/SHA 匹配。production 公钥对两版原始 manifest 验签、完整 ZIP 重验、错误签名和 test-key 拒绝通过。所有 SHA/bytes/source 在 `S9-T06-RELEASE-RESULT.json` 与 `S9-T06-SOL-VERIFY/*release-evidence.json`。
- 真实 GitHub → 1.0.0 production checker/downloader → production 验签/完整包验证 → 独立 Updater → 真实1.0.1 candidate WPF ACK → Completed → 实际1.0.1 WPF重启及托盘退出通过。数据/程序均 TEMP/GUID；旧父进程是 Sol 验收宿主，Updater 启用隔离测试路径。此证据不替代用户正式 Setup 首装及点击“立即更新”的 GUI 链。
- 升级前后完整字段/BLOB fingerprint 均 `9f2e9ac0942675cff2e5f1b4fe5390a239f0f3c2919d7d3be9aa8d6aa3c581ce`，integrity=ok/FK0/migration9；原 Excel BLOB、设置、所有业务表、两份备份与创建 SHA 保持，Dashboard/Pending/Today/History/Reminder 权威读取通过。正常启动使 DB 文件原始 SHA 改变，完整逻辑字段/BLOB未变，未把文件字节相等冒充数据证据。
- 两版各自 fresh 无 filter Release 1050/1050，failure/error/timeout/aborted/skipped=0；两版 build0warning/0error，EF无漂移，9条migration，末条 `20260901155124_AddPolicyAndBaselineFoundation`。T04新鲜核心128/128、WPF12/12、退出1/1；真实版本受控切换失败回滚1/1（恢复1.0.0实际WPF和完整指纹）、Updater受控硬杀成功27/27、ACK回滚9/9、坏候选EXE回滚1/1；两版隔离TestMode安装器各5/5。它们不冒充干净OS正式GUI证据。
- RSA3072 公钥 SPKI SHA256：`565956021399C88A8B13DD0873D2A801F6675EAB44BEB4FC8EBE53C71FEFBADC`。私钥仓库外、非TEMP持久保存，DPAPI CurrentUser加密PKCS8+当前用户独占受保护ACL；绝对路径只在本机，不写治理/Release；没有独立恢复备份，依赖当前Windows用户DPAPI资料。客户端只有公钥。两版独立扫描各1227项，0已知secret命中。
- Setup/App/Updater Authenticode均NotSigned，未伪造自签Publisher；manifest RSA签名与Windows代码签名明确分开，SmartScreen行为待用户现场回执。
- 用户执行清单与采集器：`S9-T06-SOL-VERIFY/CLEAN-WIN11-README.md`、`Collect-CleanWin11.cmd/.ps1`。仅指定独立电脑、指定三行合成Excel；采集Before/After/Uninstalled关闭态数据副本，回传当前话题后由Sol独立核对。正式开发机数据/DB/backup未访问。
- 后续仅接收本卡人工回执、独立复核并判断是否可关闭。跨Schema migration保护与Stage9最终closeout仍待后续明确授权。

## S9-T06恢复执行（2026-09-05，覆盖下方历史暂停）

- 用户提供另一台独立Windows11 x64电脑并本人负责人工GUI验收；不要求Codex远控或Windows Sandbox。解除“完全无clean Windows环境”暂停。S9-T06 `IN_PROGRESS / NOT_ACCEPTED`；Stage9 `IN_PROGRESS / S9-T06_CURRENT`。clean Win11为USER_GUI_PENDING，Win10保持NOT_VERIFIED，不创建S9-T07。
- 恢复fetch：main=origin/main=f4e1fb62c293e1a228a28707c3536f911a02a33c，clean/0/0。生产签名、正式v1.0.0/v1.0.1 Release、真实匿名下载及自动化门禁继续执行。现有Git凭据仅在受控发布端内存调用GitHub，已只读确认目标repo public及admin/maintain/push=true，未输出/保存凭据。
- 用户独立Win11执行正式Setup首次当前用户安装、双快捷方式、自启动/重开、1.0.0显示、指定合成数据、发现1.0.1、立即更新/下载/验签/Updater/退出重启、1.0.1显示、数据设置历史保持、卸载保数据及Windows/SmartScreen实际行为；现场OS/build、无预装.NET状态仍需真实回执。
- Sol独立负责SHA/资产/manifest签名正负例/DB完整字段BLOB指纹/EF与migration/全量Release/build及失败回滚。用户GUI回执未返回前不关闭本卡；不能把自动化隔离证据冒充独立Win11真实GUI。最终给用户最精简清单和取证工具，禁止访问开发机正式数据库。
- 其余原Task安全边界、不可变发行、同Schema9、密钥保管和发布前门禁继续有效；此更新不授权Win10完成、Schema变化或S9-T07。

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

- S9-T05 `TECHNICALLY_ACCEPTED / CLOSED`；Stage9 `IN_PROGRESS / WAITING_NEXT_AUTHORIZATION`。独立 self-contained Updater、持久 journal、完整树 staging/switch/rollback、候选只读健康 ACK、失败重入与人工恢复边界已完成。产品仍 1.0.0；Stage8、S9-T01～T04 保持 CLOSED，未创建 S9-T06。
- Sol 新鲜 TEMP/GUID migration9 证据：self-contained WPF smoke/verification exit0；preparer3/3、成功硬杀27/27、回滚硬杀9/9、失败矩阵8/8、live-parent与双Updater通过；DB SHA不变、无WAL/SHM、锁/ACL/junction无混树。
- 最终无过滤 Release 1040/1040、build0/0、EF无漂移、migration清单9且末条固定、diff/secret/禁止项通过。正式安装/数据/数据库/备份未访问；无Release/tag/asset、生产trust anchor/private key、真实更新包或migration执行。
- 下一卡仅建议 S9-T06 首次签名正式 Release 与干净 Win10/11 同 Schema 1.0.0→1.0.1 端到端升级验收；须新授权，不自动建卡。

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

## 2026-09-04 S9-T02历史关闭状态

Stage9 IN_PROGRESS / WAITING_NEXT_AUTHORIZATION；S9-T02 TECHNICALLY_ACCEPTED / CLOSED。开工main=origin/main=34c336a3f03e823048e5987d102001911527e5b2、clean/0/0；全新Terra medium/priority实施提交后停，Sol治理/diff/独立两轮A-I各9/9、preflight12/12、Release996/996、build0/0、EF无漂移/migration9通过。真实Inno6.7.3当前用户安装器已生成；固定AppId、lowest、稳定路径、双快捷方式、首装Run on/重装off、卸载保全部数据/重装复用、降级和非健康migration9写前阻断已验收。正式数据及正式安装身份未访问/执行；未签名与干净机器GUI边界保留。详见S9-T02 Acceptance/INSTALLER-RESULT.json。T03未创建，Updater/正式Release/重置/Undo未实施；T01与Stage8仍CLOSED。

历史开工：2026-09-04 用户正式授权Stage9启动，当时状态IN_PROGRESS / S9-T01_CLOSED / WAITING_NEXT_AUTHORIZATION；仅S9-T01已创建，S9-T02未创建。现已由上方T03关闭状态覆盖。
开工重新 fetch：HEAD=origin/main=7c1fa2d4b0178314816e79663765f952c66d3095，clean、0/0；STAGES/TASKS/ACCEPTANCE 实际检查无既有 Stage 9 / S9 文件。

## 产品冻结

首次版本1.0.0，Windows 10/11 x64 当前用户安装EXE；后续在线检查GitHub→提示→用户立即更新→下载→校验→自动升级→重启，或稍后提醒。无提示后台强制升级不做。核心业务完全离线可用，只有版本检查、更新说明和更新包下载联网。
程序与业务数据分离；更新不得覆盖业务数据，卸载默认保留数据。禁止客户端PAT、长期secret、仓库写权限token。无总部管理、设备注册/上报、在线状态、远程强制更新。Import Undo永久取消，重置数据另行授权。

## 逐卡路线（仅方向，不代表已建Task或实施授权）

- S9-T01：TECHNICALLY_ACCEPTED / CLOSED；需求冲突治理、发布/版本/数据路径基座及安装升级架构完成。最终生产/测试0c6d0e4，Sol独立Release991/991、build0/0、EF无漂移/migration9、self-contained发布164508896bytes/移目录WPF双跑/隔离DB验证与本地runtime加载通过。详见Acceptance及RESULT.json。
- S9-T02：TECHNICALLY_ACCEPTED / CLOSED；Inno Setup当前用户首次安装器及固定Programs/app路径、AppId、双快捷方式、首装HKCU Run与重装关闭偏好、同版本修复、卸载保全部数据、重装复用、降级及旧/未知/坏Schema阻断完成。AppId={8F90E64E-5B0D-4FA8-A854-EEA2F4D1EC14}为后续1.x长期身份。
- S9-T03：TECHNICALLY_ACCEPTED / CLOSED；匿名版本元数据检查、离线友好提示与生命周期门禁完成，详见上方当前状态。
- 后续逐卡考虑下载/校验；独立Updater与文件回滚；升级前保护/migration恢复；首次正式Release、最终安装升级卸载矩阵与阶段closeout。不得一次创建全部卡。

## 治理与停止点

Sol仅治理/审查/独立复验，不写生产代码；T03全新GPT-5.6 Terra medium/priority实施后已提交停止、不push。验收后Sol普通push main并停，不建T04，等待用户下一步授权。
所有测试显式TEMP/GUID合成SQLite，严禁正式库读取、哈希、复制、恢复或其他访问。Schema/index/migration/业务规则不变，migration仍9，末条20260901155124_AddPolicyAndBaselineFoundation。

Stage8保持TECHNICALLY_ACCEPTED / CLOSED，原历史文件不重开。合法外来WAL来源不可证明，结构/FK/migration健康仍可能业务漂移；严重坏当前不能生成健康保护快照时Restore fail-closed，不强制覆盖救援；真实物理断电、SSD controller、磁盘/文件系统损坏、bit rot及不可读介质均未证明。

GitHub匿名API确认public，无private token blocker；Release=0，真实资产下载未验证。当前用户安装器已完成；独立Updater、签名/回滚/跨版本保护及干净机器矩阵仍是后续交付门禁。S9-T01未制作安装器，S9-T02已实际产出但未创建正式Release；版本1.0.0不代表公开发行已完成。
