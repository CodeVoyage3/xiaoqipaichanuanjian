发布后说明（2026-09-07）：本报告保留发布前事实；其中未发布/未接受状态已由 S12-T01-RELEASE.md 的正式发布与 CLOSED 结论取代。四项冻结原资产已原样公开，未重新生成。

# S12-T01 v1.0.4 最终本地候选（Sol）

日期：2026-09-07（Asia/Shanghai）
结论：**冻结源码的最终本地候选构建、EF、打包、生产公钥验签、ZIP 树、Windows subsystem 与隔离启动门禁通过。候选未发布；S12-T01 / Stage12 继续保持 `IN_PROGRESS / NOT_ACCEPTED`。**

## 1. 来源与边界

- clean detached checkout：`C:\Users\39037\AppData\Local\Temp\S12FinalSource-cc3340b3d3244fd4aef7df809ccea891`
- source commit：`c2b3f699408f422b3aeaf5b96321a6df08a038c5`
- 门禁前后 `git status --short` 为空。
- 源项目版本仍为 `1.0.3`；本候选统一以 `-p:Version=1.0.4` 覆盖。未来重建同一候选必须保留该参数。
- 没有运行 tests、178、full 或旧矩阵；没有读取/探测正式安装根、正式数据库或正式数据根；没有安装 Setup、创建 tag、GitHub Release 或上传资产。
- 复用 `tests/S9T06-BuildRelease.ps1` 协议的既有 TEMP 参数化副本；clean、签名身份 ACL/DPAPI、生产公钥指纹、RSA-PSS、allowlist、secret scan 与严格四资产门禁均保留。签名私钥路径及内容不写入本报告。

## 2. 构建与 EF

首次在受限网络沙箱中 restore 出现 `NU1301`；授权使用官方 NuGet 源后，App 与 Updater 的 `win-x64` restore 均成功。该失败属于环境网络，不属于源码或候选失败。

```powershell
dotnet build src\StoreExpiryInspector\StoreExpiryInspector.csproj `
  -c Release --no-restore -p:Version=1.0.4 -p:NuGetAudit=false

dotnet ef migrations has-pending-model-changes `
  --project src\StoreExpiryInspector\StoreExpiryInspector.csproj `
  --configuration Release --no-build

dotnet ef migrations list `
  --project src\StoreExpiryInspector\StoreExpiryInspector.csproj `
  --configuration Release --no-build --no-connect
```

- Release build：`0 warning / 0 error`，9.83 秒。
- EF：`No changes have been made to the model since the last migration.`
- migration list：9 项，首项 `20260826123739_InitialCreate`，末项 `20260901155124_AddPolicyAndBaselineFoundation`。
- gate log：`C:\Users\39037\AppData\Local\Temp\S12-Final-Gates-2a749b1011124d34aa1600a202033d52.log`
- gate log SHA-256：`07BE7662D14B31D103E885BB2ED7D53174AB7E8E75785782406F6C4B451CB7B7`

## 3. 最终候选资产

- output root：`C:\Users\39037\AppData\Local\Temp\dbe9da86-4b5e-4944-a511-5113521b9ca0`
- assets：上述根下 `assets`
- publish：上述根下 `publish`
- 打包脚本：`C:\Users\39037\AppData\Local\Temp\d16d5284-c8c7-4355-a404-40b9b34236e3\S12-BuildCandidate.ps1`
- 脚本 SHA-256：`D7E776BAFC6CE16E7F145052B1684E8689310F8B57EFDA7443B8DFCF3BE7177B`
- Inno Setup：`C:\Users\39037\AppData\Local\Programs\Inno Setup 6\ISCC.exe`
- package exit code：`0`
- package log：`C:\Users\39037\AppData\Local\Temp\dbe9da86-4b5e-4944-a511-5113521b9ca0.log`
- package log SHA-256：`D0CEA95D68AF38A8C6FE8FBB7F97EC622C91B63C769BA39F2C1C1FFF4CDC9F36`

| 资产 | 字节 | SHA-256 |
|---|---:|---|
| `StoreExpiryInspector-1.0.4-win-x64.zip` | 109410517 | `4598C9A608B4B048A1E97BACD4D22C86E7C8DC4DB2637B527631DBB88EFA079D` |
| `StoreExpiryInspector-Setup-1.0.4.exe` | 75334652 | `AEE5229B94D7048846CA6CEEA37BDB7049E1C7882734B471D3EF4A8BFCF6DAC3` |
| `update-manifest.json` | 852 | `A86C73692CC54128A486BF59156A7C0EAA0BBC07175BC47988E07C643D1B80D0` |
| `update-manifest.sig` | 384 | `25A302A014AFB3A2C9164E69D273D8B1D497C820982685D5EDE587606DC704D1` |

- `release-evidence.json` SHA-256：`A4F59E06DB25C8EA3DC315EDF0EF4145B16F529FFCAE6DB4D8D19FBEB24FF5D8`
- package tree SHA-256：`1510527ECADEDD4F0F62A104AD585D6E6AD1A1B0A4212E21C6E248D4F249D119`
- manifest：target `1.0.4`，source `minVersion=maxVersion=1.0.3`，9 migrations。

## 4. 独立资产核验

- 使用仓库内 production public key 独立验签：RSA-PSS/SHA-256 `True`。
- production public key fingerprint SHA-256：`565956021399C88A8B13DD0873D2A801F6675EAB44BEB4FC8EBE53C71FEFBADC`，与冻结身份一致。
- ZIP：618/618 项逐文件长度和 SHA-256 与 evidence 一致；difference `0`；数据库、SQLite、日志、备份、TRX、PDB、fixture 等禁入项 `0`。
- App、Updater 均含 `coreclr.dll` 与 `hostfxr.dll`，为 self-contained `win-x64`。
- FileVersion：App `1.0.4.0`；Updater `1.0.4.0`；Setup `1.0.4`。
- PE subsystem：App `2 (Windows GUI)`；Updater `2 (Windows GUI)`。这确认正式 publish 中的 Updater 不再是 Console subsystem。
- Windows Authenticode：App、Updater、Setup 均为 `NotSigned`。此限制与已通过的 manifest 应用层生产身份签名分开记录。
- receipt：`C:\Users\39037\AppData\Local\Temp\dbe9da86-4b5e-4944-a511-5113521b9ca0\independent-asset-validation.json`
- receipt SHA-256：`BD42FAA4DC8E469B8F7728EB1668AFB3B51A698F82E6B6B081F0A3F71ADAF032`

独立核验脚本首次因 `[Convert]::ToHexString` 调用写法错误而早退；随后两次仅修正 Setup FileVersion 的尾随空格与预期格式。资产从未因这些核验器错误而重建或修改，最终 receipt 为上列 PASS 收据。

## 5. 隔离启动

使用最终 `publish\StoreExpiryInspector.exe` 与生产内置 smoke 入口：

```text
--data-root <TEMP/GUID> --s9-t01-smoke-exit
```

- data root：`C:\Users\39037\AppData\Local\Temp\ab68e592-0d09-4cef-bab9-f379aaec6b17`
- 60 秒内退出：`True`；exit code：`0`
- 隔离数据库创建：`True`
- `s9_t01_smoke_ready`（WPF Shell 初始化与首轮读取完成）日志标记：`True`
- receipt：`C:\Users\39037\AppData\Local\Temp\dbe9da86-4b5e-4944-a511-5113521b9ca0\isolated-app-smoke.json`
- receipt SHA-256：`22391F8CBAAF291E669B89F13F53D94413878338E0D8A02B3FF40F2C752D2F72`

首次 smoke 命令曾误用 `TEMP\S12FinalSmoke-<hex>`，不符合生产代码要求的 `TEMP` 直属 GUID 目录，应用按约束拒绝且未创建数据；该结果不计为有效 smoke。随后只按上述既有协议完成一次有效启动。

## 6. GitHub 稳定版身份核对

根任务 fresh API 核对结果：`origin/main=0a5775c...`；GitHub latest 为稳定 `v1.0.3`，source/tag commit `651bd107...`；tag 列表中没有 `v1.0.4`。候选 source commit `c2b3f699...` 是尚未 push/tag 的本地提交，不能与远端 main 混称。

v1.0.3 四资产的 GitHub API digest/size 与旧 HANDOFF 基线一致：ZIP `725DBA...` / 109409826 bytes；Setup `B9FE...` / 75323323 bytes；manifest `72DE...` / 852 bytes；signature `83D2...` / 384 bytes。该项是 API identity 核对，没有重新下载四资产全量字节。

## 7. 人工升级入口与停止点

`PRE_RELEASE_UPDATE_ENTRY_UNAVAILABLE`：正常生产更新检查只读取 GitHub latest；当前 latest 是 `1.0.3`，而本地候选为 `1.0.4`，因此不会出现“发现新版本”入口。仓库已有离线 pre-release transport 固定用于旧的 `1.0.2 → 1.0.3`，在禁止修改生产代码或新增 test adapter 的本轮边界内不能把它冒充为 `1.0.3 → 1.0.4`。

因此用户剩余四项——更新窗 modal、关闭后主窗恢复、真实更新过程无黑色控制台、自动启动 1.0.4——本轮预发布阶段没有真实可操作入口。这里记录的是预发布入口可用性限制，不是新的产品缺陷，也不把静态/自动化证据冒充人工升级收据。

`HISTORICAL_TEST_ISOLATION_UNCERTAINTY` 继续保留：此前旧 `App.Run()` 测试宿主可能访问默认数据根；本轮没有探测正式根，也没有把后续安全宿主结果追溯改写成“历史未访问”。

最终状态：本地 candidate 技术门禁 PASS；`v1.0.4 NOT_RELEASED`；`S12-T01 IN_PROGRESS / NOT_ACCEPTED`；`Stage12 IN_PROGRESS`。停止在候选资产准备完成处，等待后续经明确授权的真实发布/升级验收步骤。
