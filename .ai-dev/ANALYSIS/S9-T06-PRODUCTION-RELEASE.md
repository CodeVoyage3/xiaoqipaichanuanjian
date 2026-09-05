# S9-T06 生产发布审查

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
