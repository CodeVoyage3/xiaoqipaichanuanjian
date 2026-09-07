# S12-T01 v1.0.4 正式发布验收

日期：2026-09-07（Asia/Shanghai）
结论：**`PUBLIC_RELEASE_ASSET_EQUIVALENCE = PASS`。S12-T01 可记录为 `RELEASED_AND_ACCEPTED / CLOSED`，Stage12 可记录为 `CLOSED`。**

## 公开 Release

- Release：[v1.0.4](https://github.com/CodeVoyage3/xiaoqipaichanuanjian/releases/tag/v1.0.4)
- Release ID：`383891004`
- 发布时间：`2026-09-07T07:11:42Z`
- `draft=false`、`prerelease=false`、stable，`releases/latest=v1.0.4`
- annotated tag：`d66c32f4361571c1b16138e554ffebb24e4be187`
- tag 解引用后的产品 source：`c2b3f699408f422b3aeaf5b96321a6df08a038c5`

## 匿名 fresh 公开资产验收

四项资产均从公开 Release URL 匿名下载到新的 `TEMP/GUID`：

`C:\Users\39037\AppData\Local\Temp\122e0af2-5d67-4c44-a3f4-14a2531a6628`

| 资产 | 公开下载字节 | 公开下载 SHA-256 | 冻结候选等价 |
|---|---:|---|---|
| `StoreExpiryInspector-1.0.4-win-x64.zip` | 109410517 | `4598C9A608B4B048A1E97BACD4D22C86E7C8DC4DB2637B527631DBB88EFA079D` | PASS |
| `StoreExpiryInspector-Setup-1.0.4.exe` | 75334652 | `AEE5229B94D7048846CA6CEEA37BDB7049E1C7882734B471D3EF4A8BFCF6DAC3` | PASS |
| `update-manifest.json` | 852 | `A86C73692CC54128A486BF59156A7C0EAA0BBC07175BC47988E07C643D1B80D0` | PASS |
| `update-manifest.sig` | 384 | `25A302A014AFB3A2C9164E69D273D8B1D497C820982685D5EDE587606DC704D1` | PASS |

公开下载不是 GitHub API digest 的替代：四个文件均实际下载并在本机重新计算 SHA-256；结果与最终冻结候选完全一致。

## manifest、签名与 ZIP

- manifest：target/release tag `1.0.4 / v1.0.4`；source `minVersion=maxVersion=1.0.3`；ZIP bytes/hash 正确；migrationCount `9`。
- 使用冻结源码仓库内 production public key 独立执行 RSA-PSS/SHA256 验签：PASS。
- production public key SPKI SHA-256：`565956021399C88A8B13DD0873D2A801F6675EAB44BEB4FC8EBE53C71FEFBADC`。
- 公开 ZIP 共 618 项，与冻结候选 package tree 逐项差异 `0`。
- ZIP 内 App FileVersion `1.0.4.0`；Updater FileVersion `1.0.4.0`。
- App PE subsystem `2`；Updater PE subsystem `2 (Windows GUI)`。
- ZIP 内程序集检出 9 个冻结 migration ID，首项 `20260826123739_InitialCreate`，末项 `20260901155124_AddPolicyAndBaselineFoundation`。
- v1.0.4 继续 unsigned；没有把 manifest 应用层签名写成 Authenticode/SmartScreen 结论。

独立机器可复核收据：

- `C:\Users\39037\AppData\Local\Temp\122e0af2-5d67-4c44-a3f4-14a2531a6628\public-release-equivalence.json`
- SHA-256：`55186658150D32D144D8D2FDB66D1AE62D3DE82AF5286CB1B5B72FFFAE190658`

核验脚本第一次仅因 PowerShell `New-Item` 参数名错误而在解 ZIP 前停止；没有改动任何公开下载资产。修正验收脚本后最终门禁一次 PASS，没有重建或替换 Candidate。

## v1.0.3 保持不变

匿名 GitHub API fresh 核对 v1.0.3 Release ID `383669847`，仍为 stable、四资产均存在。四项公开 asset digest/size 与冻结历史基线全部一致：

- ZIP：109409826 bytes，`725DBA97029DC9FC1B66CF4B8019144A66174FABC695439172B5E651F7F3D910`
- Setup：75323323 bytes，`B9FE900FE5A36166D11475C7C6333E4AA8F8DB4198D241A7C13F67438B5E6E14`
- manifest：852 bytes，`72DE8AC9E2062C1353A801C7071267EB735EF24E2E414C362CD07588588ACCD3`
- signature：384 bytes，`83D2B6DB71FCE793FC19DE8E3BA81E6601975D646D5C8F4099CC248C37DDA50F`

本轮没有重复下载 v1.0.3 大资产；该结论是公开 API identity 核对。

## 架构口径与边界

v1.0.3 → v1.0.4 使用 v1.0.3 主程序和 v1.0.3 自带的旧 Updater。因此这一跳不能自然验证 v1.0.4 的 modal 通知或 WinExe Updater 行为，也不再作为 S12 关闭阻断。

第一次由 v1.0.4 发起后续真实在线升级时，再人工核验并记录 `REAL_V104_TO_NEXT_UPDATE_UX_VERIFIED`：通知 modal、关闭后主窗恢复、更新全程无控制台窗口、自动重启至新版本、原数据正常。

本轮没有执行正式数据库升级或访问正式安装/数据根；`PUBLIC_RELEASE_ASSET_EQUIVALENCE` 不依赖该操作。`HISTORICAL_TEST_ISOLATION_UNCERTAINTY` 继续保留，不作已解决表述。

最终结果：`v1.0.4 RELEASED`；`S12-T01 RELEASED_AND_ACCEPTED / CLOSED`；`Stage12 CLOSED`。没有创建 Stage13。
