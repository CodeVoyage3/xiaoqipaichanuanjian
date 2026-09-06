# Stage 10｜v1.0.2 首次正式发行与安装器体验完善

## 当前状态

`Stage10 = IN_PROGRESS / S10-T01_RELEASE_CURRENT`。S10-T01 技术门禁与用户最简 Win11 GUI 门禁均已通过，现进入获授权的正式发布、匿名核验及旧开发期 Release/tag 顺序清理；完成前不关闭。

2026-09-06 用户明确授权：正式发布 v1.0.2，并在发布前完成安装器简体中文化与“首次安装可选择程序安装路径”。Stage9 保持 CLOSED，不重开。

同日用户进一步裁决：**v1.0.2 作为本软件首个正式对外发行版本**。现有 v1.0.0 / v1.0.1 仅作为开发阶段 GitHub Release/在线升级验证产物；在 v1.0.2 正式发布且全部发布后核验完成之前不得删除或改写，核验成功后删除这两个旧 GitHub Release 及对应 tag。历史 commit、Task、Acceptance、Analysis 和发布验证证据必须保留，不改写历史。

## 阶段目标

1. 将当前已完成 Stage9 安全能力的 main 正式收口为 v1.0.2，并把它作为首个正式面向用户分发的版本。
2. Inno Setup 安装向导完整简体中文化，包括通用页面、按钮、目录选择、确认、安装、完成、取消等文本。
3. 新用户首次安装时可选择程序安装路径；默认仍为 `%LOCALAPPDATA%\Programs\StoreExpiryInspector`。
4. 已安装用户升级/修复必须沿用原安装目录，不允许升级过程中静默搬家。
5. 数据根继续固定为 `%LOCALAPPDATA%\StoreExpiryInspector`，不因程序安装盘变化而迁移。
6. 自定义安装目录下，快捷方式、开机启动、卸载、在线更新、Updater rollback、跨 Schema 保护均必须继续按实际安装根工作。
7. 正式发布 stable v1.0.2，并把 GitHub latest 更新到 v1.0.2。
8. v1.0.2 发布后完成匿名下载、hash、签名、latest、Setup 的最终核验；全部通过后，再删除开发期 v1.0.0/v1.0.1 Release 与 tag，使 GitHub 对外只保留 v1.0.2 作为首个正式发行版本。

## 产品边界

- “选择安装路径”仅指程序文件安装根；不开放数据目录选择。
- 首次安装允许选择本地、普通、当前用户可写目录，例如 `D:\门店效期排查软件`。
- 拒绝 UNC/网络路径、reparse/junction/symlink、路径穿越、ADS、不可写/需管理员权限目录及其他无法安全验证的安装根。
- 保持 `PrivilegesRequired=lowest`，不因用户选择目录自动提权。
- 已安装实例升级时保留原安装目录；若用户未来要搬家，采用卸载后重新安装且保留数据，不在本阶段实现“升级时迁移程序目录”。
- AppId 不变；数据根不变；生产 migrationCount 保持 9，无真实 migration10、无业务 Schema/Domain 变化。
- 不在本阶段实现差分更新、Win10专项、Authenticode证书、A链继续深挖。

## 开发期 v1.0.0/v1.0.1 的定位

v1.0.0/v1.0.1 是 Stage9 期间用于验证真实 GitHub Release、manifest 签名、下载、Updater 与 rollback 的开发期公开测试版本，并未作为正式产品分发给真实用户。

因此正式产品口径为：

- v1.0.2：首个正式对外版本；新用户直接安装 v1.0.2。
- 不再设计或承诺真实用户的 v1.0.0/v1.0.1 → v1.0.2 迁移支持作为正式发行前置条件。
- 仍需保留必要的旧版本覆盖安装自动化，证明同 AppId、固定数据根和安装兼容没有被本阶段改坏；该测试属于兼容回归，不代表旧测试版本继续对外维护。
- 在 v1.0.2 发布并核验成功之前，v1.0.0/v1.0.1 Release/tag 不得提前删除，因为它们仍是 Stage9 历史证据链的一部分。
- v1.0.2 全部发布后门禁通过后，先删除 GitHub Release v1.0.0/v1.0.1，再删除对应 tag；不得删除其历史 commits，也不得删改 `.ai-dev` 中既有验收和分析记录。

## 最终发行门禁

S10-T01 通过前不得创建 v1.0.2 tag/Release。至少需要：

- fresh install：默认路径通过；自定义本地路径通过；中文路径/空格路径通过。
- unsafe path：UNC、reparse/junction/symlink、不可写/需提权路径安全拒绝。
- existing install：同 AppId 旧默认路径安装 → v1.0.2 Setup 覆盖升级，原数据正常，作为兼容回归。
- selected-root install：v1.0.2 在自定义路径下启动、快捷方式、自启动、卸载正常。
- updater：自定义安装根下的测试版未来升级事务/rollback 正常，不依赖固定 `%LOCALAPPDATA%\Programs\StoreExpiryInspector`。
- Release build 0 warning / 0 error；fresh full 0 failure/error/timeout/aborted/skipped；EF 无漂移，migration=9。
- App/Updater self-contained publish、secret scan、manifest RSA-PSS/SHA256 签名与完整 ZIP hash 重验通过。
- 安装器仅简体中文，不出现此前通用英文 `Ready to Install / Install / Cancel` 等未本地化页面。
- 实机只需一次最精简 Win11 安装验收：目录选择页为中文、选择自定义路径能安装、启动和数据逻辑正常。

## 正式发布与旧测试版本清理顺序

必须严格按以下顺序执行，不得提前删除旧版本：

1. 冻结 v1.0.2 source/tag 目标与四项资产。
2. 发布 stable、非 draft、非 prerelease 的 v1.0.2。
3. 独立匿名核对 `latest=v1.0.2`、tag/source、4项资产 size/SHA256、manifest production 公钥验签、ZIP hash/bytes/version、Setup 中文与可选目录能力。
4. 确认 main/tag/Release source 关系和 Git 工作区状态正常。
5. 只有 1～4 全部通过，才删除 GitHub Release `v1.0.0`、`v1.0.1`。
6. Release 删除确认成功后，再删除 git tag `v1.0.0`、`v1.0.1`。
7. 最终再次核对 GitHub 对外 Release/latest 仅以 v1.0.2 作为首个正式发行版本；历史 commit 与 `.ai-dev` 验收证据仍存在。

任何 v1.0.2 发布后核验失败，都必须保留旧 v1.0.0/v1.0.1，不执行清理，先修复/裁决 v1.0.2。

完成后 `Stage10 = CLOSED`。不自动创建 Stage11。
