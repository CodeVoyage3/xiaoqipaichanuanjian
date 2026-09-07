状态：SUPERSEDED_BY_GLOBAL_MODAL_FIX。用户已通过即时刷新与确认窗布局/提交；新增modal finding待修。以下候选证据保留，资产禁止发布。

# S12-T01 v1.0.4 预发布候选准备（Sol）

日期：2026-09-07（Asia/Shanghai）  
结论：**候选资产门禁通过，可进入用户隔离 GUI 验收；未发布、未打 tag、未上传、未安装 Setup。**

## 1. 冻结来源与边界

- 干净候选源码：`C:\Users\39037\AppData\Local\Temp\S12CandidateSource-ec1c3c73a28f42d6b307b3e20663e435`
- detached HEAD：`94d3efcfbd31ca1a9ce397b336c8c444e80f05ba`
- 构建前后 `git status --short` 为空。
- 源 `StoreExpiryInspector.csproj` 仍为 `1.0.3`；本候选通过 `dotnet publish ... -p:Version=1.0.4` 生成 `1.0.4`，未修改版本文件。未来正式构建必须继续显式传相同版本参数。
- 复用 `tests/S9T06-BuildRelease.ps1` 协议的 TEMP 参数化副本，仅增加显式仓库路径、将 source 约束改为 `1.0.3`、target/发布版本改为 `1.0.4`；clean、私钥 ACL/DPAPI/生产公钥身份、RSA-PSS、ZIP allowlist、tree hash、secret scan、四资产门禁均保留。
- 临时脚本：`C:\Users\39037\AppData\Local\Temp\d16d5284-c8c7-4355-a404-40b9b34236e3\S12-BuildCandidate.ps1`
- 脚本 SHA-256：`D7E776BAFC6CE16E7F145052B1684E8689310F8B57EFDA7443B8DFCF3BE7177B`
- 未读取或修改正式安装目录、正式数据库或正式数据根。

## 2. 构建命令与失败保留

先分别为 App、Updater 执行 `dotnet restore <project> -r win-x64 -p:NuGetAudit=false`。App 首次在受限网络环境中出现 `NU1301`，同一命令在授权网络环境通过。

候选调用形式：

```powershell
& <TEMP-S12-BuildCandidate.ps1> `
  -Compiler 'C:\Users\39037\AppData\Local\Programs\Inno Setup 6\ISCC.exe' `
  -OutputRoot <NEW-TEMP-GUID> `
  -SigningKeyFile <configured-production-signing-identity> `
  -RepositoryRoot 'C:\Users\39037\AppData\Local\Temp\S12CandidateSource-ec1c3c73a28f42d6b307b3e20663e435'
```

- 首次输出根：`C:\Users\39037\AppData\Local\Temp\41c5102f-a538-44d7-9645-161f18e955c9`
- 首次失败：App publish 已完成，Updater 因缺少自身 `obj\project.assets.json` 报 `NETSDK1004`；未把该部分输出当作候选。随后仅补 Updater restore，并使用新输出根重跑资产脚本。
- 首次失败日志：`C:\Users\39037\AppData\Local\Temp\41c5102f-a538-44d7-9645-161f18e955c9.log`
- 首次失败日志 SHA-256：`FD55C120409BFB6160BAFC021946EBE6AFE7CC1FD9882FB3348EAEB3826C8B66`
- 成功输出根：`C:\Users\39037\AppData\Local\Temp\7f1607db-db80-4979-a150-21405e45f298`
- 成功日志：`C:\Users\39037\AppData\Local\Temp\7f1607db-db80-4979-a150-21405e45f298.log`
- 成功日志 SHA-256：`18FB9246466FFBEE2A640A3FB64823F40036D9A22E1FFBBAC7A1E688967C76C6`
- 最终 exit code：`0`

没有重跑 178 项回归或 full。

## 3. 候选资产

| 资产 | 字节 | SHA-256 |
|---|---:|---|
| `StoreExpiryInspector-1.0.4-win-x64.zip` | 109410215 | `DA5252AE5C4D38EC7B334EE93B03E2B9AF82B863AD6A8B4538D37D7EE04C371F` |
| `StoreExpiryInspector-Setup-1.0.4.exe` | 75330832 | `DC2C9D4154F14FA0B886115ACF2F1C2343E012688A395D9A11FFB998BB19F248` |
| `update-manifest.json` | 852 | `A9A3BCCCD87932F397741F615BD71E4207B610B128195715D4743EE1A1F23250` |
| `update-manifest.sig` | 384 | `2D6FB3DE8C8346F84705DCAAB3497AD05B9C03D2AB74445EEB17D4A3E8D1E850` |

资产目录：`C:\Users\39037\AppData\Local\Temp\7f1607db-db80-4979-a150-21405e45f298\assets`

## 4. 独立资产核验

- App 与 Updater 均为 self-contained `win-x64`；两者 `FileVersion=1.0.4.0`。
- `update-manifest.json`：`version=1.0.4`、`releaseTag=v1.0.4`、package 文件/长度/SHA 与 ZIP 一致。
- source 约束：`minVersion=maxVersion=1.0.3`。
- 清单列出 9 个迁移，首个 `20260826123739_InitialCreate`，末个 `20260901155124_AddPolicyAndBaselineFoundation`。
- 使用仓库内 production public key 独立执行 RSA-PSS/SHA-256 验签：`True`；公钥指纹 `565956021399C88A8B13DD0873D2A801F6675EAB44BEB4FC8EBE53C71FEFBADC`。
- ZIP 共 618 个文件；逐项重新计算长度与 SHA-256，与 `release-evidence.json` 的 618 项 tree 完全一致，difference `0`；tree SHA-256 `1A491AB79CDCA047BB037806EE4301EB93665EF7161C83E7C6E2519E059036FD`。
- ZIP 中数据库、SQLite、TRX、测试程序集/Fixture 禁入项：`0`。
- 原协议已完成 source/release secret scan、allowlist 和严格四资产检查。
- `release-evidence.json` SHA-256：`F681F4CD095F175FF584F263C11866FA5CB9840B0C05638A15B0F11B7187BC27`。
- 独立复核收据：`C:\Users\39037\AppData\Local\Temp\7f1607db-db80-4979-a150-21405e45f298\independent-asset-validation.json`
- 独立复核收据 SHA-256：`C56AE21BE4B65FEC89F4C9F6C4C925B23AD1771F9313AE681CC9E5205AF7D520`

EF 结论沿用本轮用户已接受的冻结源码 `NO_MODEL_DRIFT` 与 9 migrations fresh 门禁；S12-T01 没有 model/schema 变化。本候选报告对资产中的清单/程序集迁移列表作核对，没有把此前源码 EF 结果改写成新的资产 EF full。

### 签名限制

`manifest.sig` 的应用层生产身份签名有效。App、Updater 与 Setup 的 Windows Authenticode 状态均为 `NotSigned`；这项限制与 manifest 签名结论分开记录。

## 5. 隔离启动 smoke

候选 `publish\StoreExpiryInspector.exe` 使用普通生产入口启动：

```text
--data-root C:\Users\39037\AppData\Local\Temp\8291348c-96fb-455c-ad66-aa0ef2095be3 --allow-existing-isolated-data-root
```

- 观察到主窗口：`True`
- 新建隔离数据库：`True`，339968 bytes
- `CloseMainWindow` 接受：`True`
- 强制 kill：`False`
- 退出码：`0`
- 收据：`C:\Users\39037\AppData\Local\Temp\7f1607db-db80-4979-a150-21405e45f298\isolated-startup-smoke.json`
- 收据 SHA-256：`3AB6CA5E6F82F1AB98A4F016FF8C0EFB05801FC52F052558DE7B0191A7752EF1`

该根与用户 GUI 预留根不同；未访问正式数据。

## 6. 用户 GUI 入口

- 双击入口：`C:\Users\39037\AppData\Local\Temp\7f1607db-db80-4979-a150-21405e45f298\gui-review\启动-v1.0.4-隔离GUI.cmd`
- 入口 SHA-256：`C15B77020AFF9E74C4A7F77870A845EE028C84E1F06D42A9C08A2B8288B29595`
- 人工步骤：同目录 `人工GUI验收说明.txt`
- 人工步骤 SHA-256：`3A2DF0D4E3771925C49796D3324EC4D07B3171BD93E8D8E10A4AE3CBE3E3B5A0`
- 合成样例：同目录 `S12-v1.0.4-GUI-当天到期样例.xlsx`
- 样例 SHA-256：`7D39247CDB246BFC89351F034B2B599782D3CF2BF23E5C6B7C958D37D3B139D3`
- 样例内容：食品、当天 `2026-09-07` 到期、批次到货 5、库存 5；已 export/reopen inspect 并渲染目视，11 个模板字段完整。
- 用户预留数据根：`%TEMP%\c856061d-a358-4459-b24a-12b860d86773`；首次启动前未被 smoke 使用。

人工步骤要求先观察空的“今日排查”，再导入样例并确认待办/今日排查即时刷新；随后按现有“导出排查计划 → 填写本次排查数量 → 导入排查结果”流程进入确认窗，先取消一次，再重新导入并实际提交一次。

## 7. 更新弹窗与停止点

2026-09-07 本次候选核验通过 GitHub 官方 `releases/latest` API fresh 查询到 `v1.0.3`（非 draft、非 prerelease，发布时间 `2026-09-06T17:58:25Z`）。本地候选为 `1.0.4`，正常生产更新检查不会提供更高版本，因此“发现新版本”弹窗及“取消更新”文案在本候选正常路径不可达；用户步骤明确不要求寻找该窗口。

当前停止在本地预发布候选准备：没有安装 Setup、没有候选升级事务、没有发布 GitHub Release、没有 tag、没有上传、没有更新 latest。
