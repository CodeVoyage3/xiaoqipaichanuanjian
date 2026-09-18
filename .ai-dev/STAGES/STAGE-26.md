# 2026-09-18 v1.1.4 正式发布完成
Stage26=CLOSED / ACCEPTED。
S26-T01=CLOSED / ACCEPTED；真实用户 Release RC GUI=PASS（回执已单独登记）。
S26-T02=CLOSED / ACCEPTED；Sol正式发布与公网验证=PASS。
PRODUCT_SOURCE_SHA=96cb01a843eaed80791762416dd87329ec802449
正式tag v1.1.4 peel等于PRODUCT_SOURCE_SHA；Release ID=391379910；Latest=v1.1.4。
Release：https://github.com/CodeVoyage3/xiaoqipaichanuanjian/releases/tag/v1.1.4
四项资产：独立草稿下载与公网匿名HTTP 200下载，size/SHA256全部等于冻结RC，GitHub digest一致；生产公钥RSA-PSS/SHA256 manifest验签PASS。
App / Updater=1.1.4（公网ZIP内Assembly/FileVersion=1.1.4.0）。
Compatibility Generation=G1-m10-protocol2；source.minVersion=1.1.0，source.maxVersion=1.1.3。
minimumDirectVersion=1.1.0；minimumProtocolVersion=2；setupMode=SAME_SCHEMA_SLIM；crossSchemaAllowed=false。
migration=10；migration11=NOT_CREATED；CurrentSchemaIdentity未改变。
正式发布只使用冻结原资产；无生产修改、无重建、无新增测试；FULL=NOT_RUN / NO_FULL。
原dirty工作区HEAD/status/五项文件哈希保持；main仅普通ff-only集成、普通push。
下一Stage=NOT_STARTED。
详见ACCEPTANCE/S26-T02.json与其evidenceRoot。

| 正式资产 | bytes | SHA256 |
|---|---:|---|
| StoreExpiryInspector-1.1.4-win-x64.zip | 109485799 | 32c3d42d67668669ae421166a8f3bf4985a80d32dcef77d689a4d60fbea05ca9 |
| StoreExpiryInspector-Setup-1.1.4.exe | 75385824 | bf40231ed5c4996dd653ac2763afa0de4f6fbad01a24a977ade2f11880452f01 |
| update-manifest.json | 914 | 9e7563ba259733b6ae2dc0d5e3d5b516cc550ca983c0ec1c9c82db5e3648a23e |
| update-manifest.sig | 384 | ae6001060e49e1fc2249649b737711422fa95828330b8c69c72b4b2182302542 |

以下为历史记录，当前状态以本次收口为准。
# 2026-09-18 真实用户 Release GUI PASS / 正式发布授权
S26-T01=CLOSED / ACCEPTED；RC GUI=PASS（真实用户回执）。
S26-T02=AUTHORIZED / IN_PROGRESS；Stage26=AUTHORIZED / IN_PROGRESS。
PRODUCT_SOURCE_SHA=96cb01a843eaed80791762416dd87329ec802449；冻结资产沿用，不重建、不修改生产代码。
用户原文：v1.1.4 RC GUI 验收通过。确认：S26-T01 RC GUI = PASS；S26-T01 = CLOSED / ACCEPTED；登记真实用户 Release GUI PASS。现在允许启动 S26-T02｜v1.1.4 正式发布与公网验证。
FULL=NOT_RUN / NO_FULL；不启动下一 Stage。

以下为历史记录，当前状态以上述登记为准。
# 2026-09-18 Stage26 RC技术通过｜等待真实用户
Stage26=AUTHORIZED / IN_PROGRESS。
S26-T01=TECHNICAL_PASS / USER_GUI_PENDING / NOT_ACCEPTED；不CLOSED/ACCEPTED。
S26-T02=NOT_STARTED / NOT_AUTHORIZED。
PRODUCT_SOURCE_SHA=96cb01a843eaed80791762416dd87329ec802449
v1.1.4 G1-m10-protocol2，source1.1.0..1.1.3，minimumDirect1.1.0/protocol2/SAME_SCHEMA_SLIM/crossSchemaAllowed=false。
Sol106/106回归+50assertions、生产候选build/签名/identity、110和113真实Setup/Online、数据健康、新安装、防降级全部PASS。
FULL=NOT_RUN / NO_FULL；m10/migration11 NOT_CREATED；未正式发布或push main。
RC入口、冻结资产与独立证据见ACCEPTANCE/S26-T01.json；用户仅六项新增功能GUI确认。
原dirty保持，正式DB未访问；当前停止，等待RC GUI PASS，不启动S26-T02。

以下为历史记录。
# Stage26｜v1.1.4 Same-Schema Release
Status: AUTHORIZED / IN_PROGRESS
Baseline: c096c15364a7904c7bb306d146f705c727f881a0
Stage25: CLOSED / ACCEPTED（真实用户 GUI PASS）
S26-T01: AUTHORIZED / IN_PROGRESS
S26-T02: NOT_STARTED / NOT_AUTHORIZED
FULL = NOT_RUN / NO_FULL
独立 clean Release 环境；原 dirty 工作区不得触碰。
目标：Stage25 已验收功能、产品版本 1.1.4、G1-m10-protocol2。
migration=10；migration11=NOT_CREATED；禁止修改 Schema / CurrentSchemaIdentity。
合同：source=1.1.0..1.1.3；minimumDirectVersion=1.1.0；minimumProtocolVersion=2；SAME_SCHEMA_SLIM；crossSchemaAllowed=false。
真实用户 RC GUI PASS 前不得正式 tag / Release / Latest / 上传公开资产。

