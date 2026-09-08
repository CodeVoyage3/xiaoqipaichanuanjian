# S13-T01 回退至 v1.0.4 收口

日期：2026-09-08（Asia/Shanghai）

状态：`S13-T01 = IMPLEMENTATION_ABANDONED / PRODUCT_TREE_ROLLED_BACK_TO_V104 / CLOSED`；`Stage13 = CLOSED / SUPERSEDED_BY_PRODUCT_DECISION`。

## 产品裁决

- v1.0.4 重新认定为最后可信稳定基线。
- v1.0.5、v1.0.6 本轮实现废弃，不再修补；其历史提交与既有验收记录保留作审计，不再代表当前产品状态。
- 产品树恢复提交为 `8a0d7368d26a2dc35b50e842e0f1dfb32b119f13`。排除 `.ai-dev/**` 后，该提交树与 annotated tag `v1.0.4` 解引用的 `c2b3f699408f422b3aeaf5b96321a6df08a038c5` 无差异。
- GitHub Release `v1.0.5`（ID `384564898`）和 `v1.0.6`（ID `384770577`）已删除；对应远端 tags 已删除。fresh 复核 Release 仅剩 v1.0.2、v1.0.3、v1.0.4，latest 为 v1.0.4（ID `383891004`）；远端 v1.0.4 tag 保留。
- 本轮使用基于 fresh `origin/main` 的普通后继提交恢复产品树，没有 force push、reset、clean 或历史改写。

## 后续 v1.0.5 产品方向（未开工）

- 取消强制升级；软件必须先正常启动主界面和托盘，再在后台异步检查更新，WPF 主线程不得同步等待网络。
- 有新版时提供“立即更新 / 稍后提醒”；选择稍后提醒后提示门店尽快升级及旧版后续可能停止支持，并允许继续正常使用。
- 不再实现 `ForcedUpdateRequired`、24 小时强制联网宽限、`AutoContinue` 或启动业务锁死。
- 托盘与每日提醒解耦，任一失败不得拖死另一个。
- 安装器兼容在线升级后注册表 `DisplayVersion` 与实际 EXE 版本不一致。
- 发布前只做候选技术验收；正式在线升级验收在发布后执行，不再以未发布版本的正式在线升级作为发布门禁。

这些规则仅作为下一版设计输入；本轮不创建新任务、不开发新版 v1.0.5。

## 执行边界

- `NO_FULL`；未运行测试或 build。
- 未访问正式 SQLite、正式安装根、正式数据根或备份。
- 当前主工作树原有 `Stage4ViewModelTests.cs` 换行状态、两个操作手册和两个测试宿主重置脚本均未触碰。
