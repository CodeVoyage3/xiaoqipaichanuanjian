# 2026-09-17 v1.1.3 RELEASED / Stage24 CLOSED / ACCEPTED

S24-T01 = CLOSED / ACCEPTED / REAL_USER_RELEASE_GUI_PASS。
S24-T02 = CLOSED / ACCEPTED / PUBLIC_VERIFICATION_PASS。
Stage24 = CLOSED / ACCEPTED；Stage25 = NOT_STARTED / NOT_AUTHORIZED。
真实用户回执：v1.1.3 Release Candidate GUI 验收通过；允许启动正式发布与公网验证。
PRODUCT_SOURCE_SHA = 280c86f2f30686092f9e603a9bbb8ac593355a27。
Sol RC acceptance HEAD = df32158da763111faa54cc42cab1495c23282267。
冻结候选 f3a75491-7eae-4520-b25d-915c3407586f 四资产原样发布；未重建。
Annotated v1.1.3 tag object = c2c55cc33ab33cecf866869ee3abd709cb36bd78。
Tag peel = 280c86f2f30686092f9e603a9bbb8ac593355a27（唯一产品源）。
GitHub Release ID = 390497014；Latest = v1.1.3；draft=false / prerelease=false。
Release URL: https://github.com/CodeVoyage3/xiaoqipaichanuanjian/releases/tag/v1.1.3
Draft 下载与正式公网匿名下载四资产均 size / SHA256 PASS；公网 HTTP200，无认证头/凭据。
公开 manifest：RSA-PSS/SHA256 PASS；source1.1.2..1.1.2 / target1.1.3 / protocol2 /
migration10；ZIP618 entries / PDB0 / non-Windows runtime0。不宣称 Authenticode。
App / Updater = 1.1.3（assembly/file1.1.3.0）；migration10；migration11 NOT_CREATED。
未修改生产代码，未 build / rerun tests / FULL；复用已验收1301/1301 FULL。
未引入待排查导出/回导 backlog；Stage25未启动；原dirty工作区未触碰。
main 普通fast-forward保留产品、测试、治理完整提交链；无rebase/squash/force push。
最终 governance/main SHA 为包含本收口记录的提交，推送后另 fresh fetch/API 核对并回报。
完整机器回执：.ai-dev/ACCEPTANCE/S24-T02.json。
本发布范围不含 Gitee / Quark 渠道写入或旧 Release/tag 清理。

## 正式资产（公网匿名重新下载验证）

| 文件 | bytes | SHA256 |
| --- | ---: | --- |
| StoreExpiryInspector-1.1.3-win-x64.zip | 109479872 | 4FC04F243CF50FCACD87FBE92080FF1EF81932322A31FCE9F08597D18099AB07 |
| StoreExpiryInspector-Setup-1.1.3.exe | 75371245 | 3584D097C614DD8539793EC702C8AA13613F4CD39454B1AA1B195C8FBAD3A333 |
| update-manifest.json | 914 | D75D30CCCCABB3D8D16FC37E25900BC149D6EB5FA2F21B3CF0A4159E989E2792 |
| update-manifest.sig | 384 | 90C3A8522B9E1480581BEF47B079F2495EA140477C4971F9493133794CDFD767 |

以下旧状态仅为历史过程记录，不覆盖本节最终状态。

# Stage24 | v1.1.3 Same-Schema Release

Status: IN_PROGRESS

Stage24: v1.1.3 Same-Schema Release. Baseline aff932484adf62aed35222f08ec0eb0f4b7fb9aa. Stage23 CLOSED / ACCEPTED with real GUI PASS retained. migration=10; migration11=NOT_CREATED; no schema/protocol/product feature changes. Original dirty workspace untouched. Formal v1.1.3 tag/Release/upload/Latest forbidden until user RC GUI PASS.

S24-T01: CLOSED / ACCEPTED / USER_RELEASE_GUI_PASS — version freeze and Release Candidate preparation.
S24-T02: AUTHORIZED / IN_PROGRESS — publication and anonymous public verification only after explicit RC GUI PASS.

Required: production Release build 0 warning / 0 error; existing FULL gate; EF identity/no drift; integrity/FK; v1.1.2 -> v1.1.3 same-schema update and ACK/data fingerprint; new install; downgrade protection; fixed signed candidate assets; minimum Stage23 GUI smoke. Isolated TEMP/GUID automated evidence does not replace formal installed-version user acceptance.
