# Stage 10｜v1.0.2 正式发行与安装器体验完善

## 当前状态

`Stage10 = IN_PROGRESS / S10-T01_CURRENT`。

2026-09-06 用户明确授权：正式发布 v1.0.2，并在发布前完成安装器简体中文化与“首次安装可选择程序安装路径”。Stage9 保持 CLOSED，不重开；v1.0.0 / v1.0.1 公开 tag 与资产保持不可变。

## 阶段目标

1. 将当前已完成 Stage9 安全能力的 main 正式收口为 v1.0.2。
2. Inno Setup 安装向导完整简体中文化，包括通用页面、按钮、目录选择、确认、安装、完成、取消等文本。
3. 新用户首次安装时可选择程序安装路径；默认仍为 `%LOCALAPPDATA%\Programs\StoreExpiryInspector`。
4. 已安装用户升级/修复必须沿用原安装目录，不允许升级过程中静默搬家。
5. 数据根继续固定为 `%LOCALAPPDATA%\StoreExpiryInspector`，不因程序安装盘变化而迁移。
6. 自定义安装目录下，快捷方式、开机启动、卸载、在线更新、Updater rollback、跨 Schema 保护均必须继续按实际安装根工作。
7. 正式发布 stable v1.0.2，并把 GitHub latest 更新到 v1.0.2。

## 产品边界

- “选择安装路径”仅指程序文件安装根；不开放数据目录选择。
- 首次安装允许选择本地、普通、当前用户可写目录，例如 `D:\门店效期排查软件`。
- 拒绝 UNC/网络路径、reparse/junction/symlink、路径穿越、ADS、不可写/需管理员权限目录及其他无法安全验证的安装根。
- 保持 `PrivilegesRequired=lowest`，不因用户选择目录自动提权。
- 已安装实例升级时保留原安装目录；若用户未来要搬家，采用卸载后重新安装且保留数据，不在本阶段实现“升级时迁移程序目录”。
- AppId 不变；数据根不变；生产 migrationCount 保持 9，无真实 migration10、无业务 Schema/Domain 变化。
- 不在本阶段实现差分更新、Win10专项、Authenticode证书、A链继续深挖。

## 旧公开版本到 v1.0.2 的首次跨越

公开 v1.0.0/v1.0.1 自身携带旧 Updater，不能假定它们已具备当前 main 的最终 Updater 修复。

因此 v1.0.2 的正式用户策略为：

- 新用户：直接下载安装 `StoreExpiryInspector-Setup-1.0.2.exe`。
- 已安装 v1.0.0/v1.0.1：第一次跨到 v1.0.2 使用 v1.0.2 安装器覆盖升级，保留原数据根及原安装身份；不得要求用户卸载数据。
- 从 v1.0.2 起：后续版本恢复正常应用内在线升级。

除非通过独立证据证明旧公开版本能够无桥接可靠自升级，否则 Release Notes 不得宣称 v1.0.0/v1.0.1 可直接通过旧“立即更新”完成首次跨越。

## 最终发行门禁

S10-T01 通过前不得创建 v1.0.2 tag/Release。至少需要：

- fresh install：默认路径通过；自定义本地路径通过；中文路径/空格路径通过。
- unsafe path：UNC、reparse/junction/symlink、不可写/需提权路径安全拒绝。
- existing install：固定旧默认路径的 v1.0.1 → v1.0.2 Setup 覆盖升级，原数据正常。
- selected-root install：v1.0.2 在自定义路径下启动、快捷方式、自启动、卸载正常。
- updater：自定义安装根下的测试版未来升级事务/rollback 正常，不依赖固定 `%LOCALAPPDATA%\Programs\StoreExpiryInspector`。
- Release build 0 warning / 0 error；fresh full 0 failure/error/timeout/aborted/skipped；EF 无漂移，migration=9。
- App/Updater self-contained publish、secret scan、manifest RSA-PSS/SHA256 签名与完整 ZIP hash 重验通过。
- 安装器仅简体中文，不出现此前通用英文 `Ready to Install / Install / Cancel` 等未本地化页面。
- 实机只需一次最精简 Win11 安装验收：目录选择页为中文、选择自定义路径能安装、启动和原/新数据逻辑正常。

发布后核对 GitHub latest=v1.0.2、4项正式资产身份/hash、tag/source、工作区 clean、HEAD=origin/main、ahead/behind 0/0。

完成后 `Stage10 = CLOSED`。不自动创建 Stage11。