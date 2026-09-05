# S9-T06 B 根因确认与修复门禁

2026-09-05。用户本轮明确要求三组冻结事务验证满足条件即收口；不创建 S9-T07，不发布 v1.0.2。

## 权威结论

B = `ROOT_CAUSE_TECHNICALLY_CONFIRMED`。正式 v1.0.0 启动 Updater 时未设置 WorkingDirectory；从安装器快捷方式启动的旧 App 使用 app 作为 CWD，独立复制到 app 外的 Updater 仍继承 app CWD，自身持有目录锁。在父进程已经退出后，CandidateStaged 的 Directory.Move(app, old) 抛 IOException / 0x80070020，随后正常回滚。不是未复制 Updater，也不是已证实的 Defender 瞬时扫描。

这是一条通过源码和受控事务对照确立的技术根因。原实机 journal 没有原始 phase/CWD/PID，不能补造该次实机持锁 PID；但错误文字、候选 PID=0、最终 RolledBack 与旧版 ACK 均一致，达到用户规定的技术确认标准，不再追加 B 根因猜测。

## 本轮新鲜冻结验证

开工 fetch 成功：HEAD=origin/main=45072b965809ecc0f3e576eacb991055e5cb2fc8，clean、0/0。

冻结源 v1.0.0 = 7044a984ddca757d8ae9350fbc523800bd769796。使用正式两版完整 611 文件程序树，独立逐文件读取、排序、SHA 后与历史权威树匹配：100=BE93548A81FCBF61DB2737C8BBEC9F9CE84DF2D226CB0456282469DF9835E0D8；101=E7E0692B0A998D5901A0998240B6D06A817BE9500F8DEF64D7596F3814A9C0E7。

| 对照 | 次数 | 实际结果 |
|---|---:|---|
| 父 CWD=app，原始 UseShellExecute=false / WorkingDirectory空 | 3/3 | Updater CWD=app；父已退出；CandidateStaged app→old 抛 IOException 0x80070020；RolledBack；旧完整树、实际100 WPF ACK和正常核心启动通过 |
| 父 CWD=app，显式 Updater WorkingDirectory=外部 operation/updater | 3/3 | app→old成功；Completed；完整101树、实际101 ACK和正常核心启动通过 |
| 父 CWD=app外，原始 UseShellExecute=false / WorkingDirectory空 | 3/3 | Updater继承外部CWD；Completed；完整101树、实际101 ACK和正常核心启动通过 |

三组均合成 TEMP/GUID migration9，integrity ok/FK0/固定末条。父进程 PID、Updater PID、两者 CWD、WorkingDirectory、UseShellExecute、phase、move开始/结果/HResult均见 `../ACCEPTANCE/S9-T06-B-SOL-VERIFY/frozen-transaction-result.json` 与九份trace。

验收边界：父进程是 version100 的最小启动宿主，采用冻结启动配置，不冒充正式 App GUI。Updater 是冻结源码的观测副本，启用既有 S9T05_TEST 根映射；仅增加观测日志和在正常 WPF 重启后使用既有隔离 smoke-exit自动退出，不改变目录操作、状态机、ACK、rollback或等待策略。完整差异在 frozen-observation.diff。状态机从已准备的完整已核对 staging 开始，candidate.zip是合成保留标记，不冒称本矩阵执行了网络下载/验签/Preparer；该项由修复验收另行覆盖。

无人工延迟使 Move 成功，无新增 Sleep/重试；只等待进程真实退出及既有状态机本来的有界等待。正式两版 WPF 都实际启动了候选/旧版只读 ACK 与正常核心 smoke。正常启动会写运行状态，因此 DB 文件 SHA 前后不同被如实保留，不虚报字节相同。此次仅检查migration及健康，不冒充业务字段全量对比。

验收工具失败历史：初次 Python 读取 dotnet 输出使用系统编码造成解码错误；之后受限构建无法使用运行时包，获准使用开发环境恢复成功；两次 seed 初始化误预建隔离数据目录触发产品既有拒绝，超时停止该专用子进程。修正为让产品创建全新 GUID 目录后，正式九组一次连续运行全部通过。上述不是产品事务失败，也不删除/掩盖失败现场。

## 修复与首次跨越

全新 Terra `/root/s9_t06_b_fresh_terra`（GPT-5.6 Terra，medium，priority服务）负责生产修复。Sol 只治理、diff与独立复验。

修复必须覆盖正常安装与pending recovery的启动入口。旧100的Preparer使用自身 app/Updater，复制到 data/updates/<operation>/updater；目标102包中的新Updater不能改变旧100已经启动的事务。当前manifest和命令行也没有“指定新Updater工作目录”的能力。

三组第三组证明无需改旧公开字节即可从外部CWD启动旧App，再由原始完整GUI/验签/安装流程完成首次跨越。应准备受限且可独立测试的一次启动辅助入口，固定身份、核验正式旧版本、不触碰DB、不直接执行目录切换或修改journal；不能复用已经以错误CWD运行的旧App。最终用户只做一次最小Win11测试。公开latest仍101；不把未公开102伪报为可发现Release。

保留migration9、Schema、业务、AppId/安装根/data root/current-user权限；保留RSA、包hash、journal、树fingerprint、old、candidate ACK、fail-closed及rollback。自身CWD锁应通过正确启动上下文消除，不以固定延迟或重试掩盖。短时/永久外部锁与恢复继续做回归，不扩展无关优化。

## A 链新停止规则

A = `FROZEN_HISTORICAL_INTERMITTENT`，不是已修复。用户最新授权覆盖此前“A未定位阻塞关闭”的要求。只在修复后新版本真实Win11升级再次出现该错误，或自动化出现稳定可复现的准备阶段失败时重开；其余不扩大诊断、不阻塞本卡关闭，不要求用户重复正式更新或旧网络诊断。

本卡现为 CLOSED。用户最终真实Win11回执确认：正式100经bridge自动升级到公开101，自动重开、版本显示和再次退出重开均为101，原数据正常。B追加 REAL_WIN11_END_TO_END_VERIFIED；A本次未复现、根因未独立确认、未宣称修复，不再阻塞关闭。既有技术门禁与代码未变已重新核对，详见 `../ACCEPTANCE/S9-T06-WIN11-CLOSEOUT.json`。Stage9保持IN_PROGRESS并等待后续授权；不发布102、不建T07，用户无需再测试。
