# S9-T06 生产发布审查

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

2026-09-05 `PAUSED_PRODUCT_REVIEW / NOT_ACCEPTED`；前置环境暂停，未批准发行门禁通过。

- 权威同步基线 fd541b88f071badd6a692373e82deaf6146c10ee，clean/0/0；指定T05事务分析文件缺失，实际事务依据T05 Task/Acceptance/JSON及代码审查。
- 正式私钥尚未生成；须先验证仓库外、非TEMP、持久、当前用户独占保管能力。这里永不写私钥或其本机绝对路径；只登记公钥SPKI SHA256和非秘密保管描述。
- 正式安装身份固定，禁止开发用户正式数据根访问；真实Setup/升级须有可证明隔离的干净用户/OS，不能以更换AppId/测试构建冒充正式成功链。
- T04 source字段是version/migration范围，targetMigrations是完整列表；必须把两版完整migration9清单及同Schema比较记录在发行证据，不暗改协议。
- 预发布门禁先于公开Release；已发布字节不可悄悄替换，失败保留事实并交用户决定。发布说明和资产不可包含本地证据/路径/secret。
- 尚无本卡密钥指纹、source release commit、asset SHA、匿名下载、安装、更新或clean OS结果。

## 只读审查与恢复条件

正式新Terra完成只读代码链审查，治理角色核对关键源码；无生产代码变更。
MainWindow默认构造SignedUpdatePackageDownloader，无TrustedPublicKey，当前返回SigningNotConfigured；恢复后须嵌入仅production公钥，客户端不接触私钥。现有版本在csproj和installer ISS均为1.0.0；后续1.0.1必须同步版本并从明确源提交重建。
现有T02测试安装器脚本不是完整production发行自动化，缺少最终源码→版本化publish/Setup/严格ZIP/manifest签名/清单hash/secret scan的完整正式链证据。不要以旧TEMP二进制填补。
正式Setup会用固定数据根进行preflight，故当前开发用户现场不能用于本卡正式安装；测试AppId不能冒充正式发行身份。当前仅可证明开发机Win11 Pro23H2 22631.6199，用户确认未安装过clean Windows虚拟环境，本机无可用Sandbox/VM入口；暂停，不推断硬件不支持。
匿名GitHub本轮repo200/public、list200且0条、latest404；未使用Authorization，未验证Release写权限或生产资产下载。持久秘密位置尚未建立/验证，没有私钥内容或绝对路径进入治理。
恢复需要可访问clean OS及安全合成数据安排；Win10/Win11覆盖各自记录，随后核实持久密钥保管和发布权限。不得通过省略clean OS、使用开发机silent install或开启生产目录访问来解锁。

## Production signing identity（2026-09-05，本轮新鲜）

RSA3072；manifest raw bytes RSA-PSS/SHA256。公钥SPKI DER SHA256：`565956021399C88A8B13DD0873D2A801F6675EAB44BEB4FC8EBE53C71FEFBADC`，公钥文件 `ACCEPTANCE/S9-T06-PUBLIC-KEY.pem`。
私钥仓库外非TEMP持久保存为Windows DPAPI CurrentUser加密PKCS8，目录关闭ACL继承且仅当前用户FullControl，私钥文件继承此独占ACL；实际读回解密及RSA-PSS签验通过。未输出私钥/密文字节，私钥绝对路径只保留本机，不写Git治理/Release body。尚无独立恢复备份，依赖当前Windows用户配置文件；若机器或DPAPI用户材料丢失，可能失去继续以此身份签名能力，需用户后续安排受控备份。
密钥保管已建立不等于生产客户端或发行包通过；Terra仅接收公钥，正式签名由发布端执行。未购买Authenticode证书的既有边界不变。

## 最终补充复核与报告更正（2026-09-05）

- 真实1.0.0→1.0.1受控失败新增通过：从GitHub由production客户端重新下载/验签，在全新TEMP/GUID注入test-only CandidateActivated故障；Phase15 RolledBack，恢复原611文件，旧1.0.0实际WPF健康ACK、正常窗口和托盘退出通过，全部字段/BLOB指纹仍相同。公开资产未修改。见 `1.0.1-real-version-rollback.json`。
- 撤回前文将25C145…/B250EA…标为Updater权威hash的独立验收计算标注。以与Updater源码完全相同的Windows路径/OrdinalIgnoreCase排序、relative-path|bytes|uppercase-SHA、UTF8无尾换行计算并逐文件重核：1.0.0 `BE93548A81FCBF61DB2737C8BBEC9F9CE84DF2D226CB0456282469DF9835E0D8`；1.0.1 `E7E0692B0A998D5901A0998240B6D06A817BE9500F8DEF64D7596F3814A9C0E7`。后者与事务journal CandidateTree、实际active tree、fresh publish三方一致，611文件全部相同。先前错误值保留在tree-verification.json的previousIncorrectCanonicalLabel供审计。这是内部证据计算/标注更正，没有生产代码或已公开字节变化。
- 升级后SQLite原始文件SHA为160f0c0f565e58f86400f18e05728d8fbb6f91a81f9f4f643def3485518f4613（升级前cabccaa1233ed54fa858b636c4f0fd9f3e772607f1c02bc20042f6c001dc0262）；全表/字段/BLOB/schema指纹保持，不虚报原始文件字节相等。
- `released-exe-versions.json`记录真实FileVersion/ProductVersion：App1.0.0.0→1.0.1.0，含各自source commit；Updater自身组件Version仍1.0.0.0、构建来源随source commit更新。两个Setup文件版本分别1.0.0/1.0.1，均未做Authenticode签名。
- 证据收集工具只服务指定独立合成Win11。最终最小人工包4文件约10KB，不含Setup/DB/日志/秘密；Setup必须用户从真实Release下载。不得将本机隔离自动化标成独立PC GUI回执。