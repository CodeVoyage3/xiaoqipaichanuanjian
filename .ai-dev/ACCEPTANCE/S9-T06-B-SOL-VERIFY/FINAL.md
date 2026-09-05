# S9-T06 B 最终独立技术验收

## 最终收尾：CLOSED

2026-09-05用户真实Win11人工验收通过：正式100经bridge成功自动升级公开101，自动重开显示101，再次关闭重开仍101，原数据正常。B=ROOT_CAUSE_TECHNICALLY_CONFIRMED + REAL_WIN11_END_TO_END_VERIFIED。A为历史偶发准备阶段连接失败，本次未复现，根因未独立确认，不宣称修复，不再阻塞本卡。

重新核验下列技术证据及其对应源码未变，所有门禁仍有效，S9-T06=CLOSED。Stage9仍IN_PROGRESS / S9-T06_CLOSED / WAITING_NEXT_AUTHORIZATION；未发布102、不建T07，无需用户再次GUI测试。人工回执与技术证据hash见 `../S9-T06-WIN11-CLOSEOUT.json`。下方待人工表述保留为历史，不代表当前状态。

## 历史技术验收记录

日期2026-09-05。Sol：TECHNICAL PASS；S9-T06仍 IN_PROGRESS / NOT_ACCEPTED / USER_GUI_BLOCKED。用户实机验收未回执。无S9-T07、无102 tag/Release/Setup。

Git归档授权：上一轮自动审批因缺少明确远端授权而拒绝提交/push，命令未执行。用户本轮已明确批准将本卡修复与脱敏治理证据普通push至指定仓库main。推送基线重新fetch确认仍为45072b965809ecc0f3e576eacb991055e5cb2fc8；不改历史或公开资产。生产源仍为8c6ebc0cba615cd4844231fda8990a271587f389，后续提交仅作治理与脱敏归档。

公开证据说明：此目录是脱敏副本。原始日志及单次本地验收/打包脚本已保留在仓库外，不上传；所有本机绝对根目录、账号和主机身份已删除或改为路径别名，TEMP/GUID与相对组件保留以区分app内外CWD。TRX保留全部1056项测试结果及标准命名空间。JSON/JSONL保留PID、phase、HRESULT、hash与验收结果；公开副本不是原始字节。ZIP只记录名称、大小和hash，不提交二进制。范围见PUSH-FILES.md，脱敏清单见PUBLICATION-REDACTION.json，最终敏感扫描见PUBLICATION-SCAN.json。

## 根因与首次跨越

B=`ROOT_CAUSE_TECHNICALLY_CONFIRMED`。三组冻结事务各3次达到用户规定的收口门槛：父CWD=app、原始WorkingDirectory空时，Updater虽已复制在app外，仍继承app CWD；父退出后CandidateStaged Move(app,old)仍3/3抛IOException/0x80070020。显式外部WorkingDirectory和父外部CWD各3/3成功。失败实际100 ACK/完整树/正常核心启动恢复，全部migration9健康。

详见 `frozen-transaction-result.json`、九份trace和 `../../ANALYSIS/S9-T06-B-ROOT-CAUSE.md`。原实机journal缺少原始CWD/phase/PID，未补造实机持锁PID。技术对照、失败类型、candidate未启动、健康RolledBack与实机记录一致，不再追加B根因猜测。

冻结100的Preparer从自己 app/Updater复制至 data/updates/operation/updater。目标包中的102 Updater不能倒过来改变此旧事务，现有manifest/参数也没有修改启动CWD的能力。因此**仅目标包修复不可能覆盖不可变100的首次事务**。

本次桥接在核验固定安装入口、公开100版本及完整611文件树后，从全新app外工作目录启动原100。无正在运行的同名进程才启动；不改公开字节、不直接处理journal/切换、不得以管理员身份为前提。旧100原GUI继续负责原下载/验签/Preparer/旧Updater。当前公开目标仍101。桥接仅允许100；不能冒称已解决不可变101后续通过普通快捷方式启动时的相同问题；未来公开新版本的跨越另按授权处理，本轮不发布102。

## 生产diff独立审查

全新 GPT-5.6 Terra medium/priority 实施，提交50b5e9d与8c6ebc0；Sol未写生产代码。最终生产源8c6ebc0cba615cd4844231fda8990a271587f389。

- 正常安装及pending恢复两调用点使用同一个UpdaterLaunch，WorkingDirectory明确为已复制的外部Updater目录，UseShellExecute=false，ArgumentList保留journal路径边界。
- Updater可执行入口在读取事务前将CWD设为AppContext.BaseDirectory，失败返回非零；不在可复用ResumeAsync中更改调用宿主全局CWD。支持路径仍为经过原Preparer/recovery验证的外部副本。
- 不增加生产Sleep/延迟/重试，不改变事务状态机、RSA锚、包hash、指纹、old保留、candidate ACK、rollback与fail-closed。migration9/Schema/业务/AppId/安装根/data root/权限要求均无变动。
- 桥接使用Windows内置PowerShell5及.NET标准库；无新依赖。拒绝错路径、篡改树、reparse、并行运行、混合参数。仅验收隔离模式传全新TEMP/GUID data-root；正式模式不读取数据库。

## 新鲜独立证据

|检查|结果|证据|
|---|---|---|
|冻结100三组事务|9/9|frozen-transaction-result.json|
|旧100真实签名101包、负验签拒绝、原Preparer、旧Updater与实际101 WPF|PASS；Completed|signed-bridge-result.json|
|Windows PowerShell5桥接真实100隔离smoke及五项拒绝|6/6|bridge-independent-result.json|
|最终未筛选Release全量|1056/1056，失败/跳过0|full-release/S9-T06-B-Sol-Full.trx|
|独立Release build|0警告/0错误|build-release.log|
|EF内存设计时工厂|模型无漂移；9条migration末条固定|ef-model.log、ef-migrations.log|
|修复后各阶段硬杀恢复3轮、永久锁、坏ACK|40/40|patched-recovery-result.json|
|fresh完整102 WPF三种启动上下文、真实100故障回滚|4/4；成功10/回滚15|patched/wpf/frozen-transaction-result.json|
|fresh self-contained win-x64候选|611文件；实际1.0.2+8c6ebc0|candidate-publish.log、candidate-version.json|
|桥接/候选ZIP完整性和已知secret扫描|3/612项，0命中|bridge-scan.json、unpublished102-scan.json|

全部事务只使用TEMP/GUID合成DB。实际WPF检查ACK/核心正常启动/integrity ok/FK0/migration9；正常启动会更新运行状态，保留SHA差异，未谎报业务DB字节不变。40项夹具矩阵另验证合成DB字节不变，但其ACK是夹具，不能替代实际WPF的4项与冻结9项。

冻结/修复WPF矩阵从已核对完整staging开始，candidate.zip为保留标记；原100→101的签名包与真实Preparer覆盖由独立signed-bridge补足。该项使用已完整校验的本地公开资产，不冒称本轮网络或GUI点击已成功。102 ZIP是未公开技术候选，不是已签发manifest的正式在线更新资产。

观测副本启用既有S9T05_TEST根映射，仅加trace、现有隔离smoke退出，以及硬杀checkpoint已持久化后的独立观测标记。完整diff已保存；checkpoint原有无限等待仅为硬杀注入，未用人工延迟制造切换成功。生产源码没有该新增观测代码。

## 失败历史与范围

第一次全量因独立构建重叠主动取消，保留full-release-cancelled-overlap.log；仅随后串行完整1056通过算最终全量。Terra初次并行构建告警与后续串行通过不作为Sol最终构建证据。

恢复矩阵前两次直接轮询活动journal时出现原子替换UnauthorizedAccessException（0x80070005），分别保留observer-lock和shared-reader失败日志；第一次已通过18项的部分结果也保留。验收观测改为读取独立checkpoint标记后完整40/40，不改生产重试或状态机。该观测干扰不并入已确认的CandidateStaged/0x80070020根因，也不扩展新生产分析分支。

桥接在审查中修正隔离data-root参数、树根范围、PS5兼容以及Get-FileHash模块依赖；独立PS5最终6/6。早期工具失败保留，不覆盖最后结果。gh未安装时改用公共GitHub API只读确认，公开版本仍100/101各4资产。

## 交付与唯一人工门禁

`delivery.json`记录两份ZIP路径/大小/SHA256。用户只使用小型 `S9-T06-100-Bridge.zip`，依 `WIN11-ONCE.md` 操作一次：托盘退出100→桥接启动→点击一次更新→确认101正常打开且原数据正常。出错即停回传，不要求重复点击、旧网络诊断或额外数据库脚本。

A保持 `FROZEN_HISTORICAL_INTERMITTENT`，不是已修复。仅修复后新版本真实Win11再现或自动化出现稳定可复现Prepare失败才重开；否则不阻塞本卡关闭。Win10 NOT_VERIFIED。实机回执前不关闭S9-T06。
