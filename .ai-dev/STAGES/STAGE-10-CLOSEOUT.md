# Stage 10 收口｜v1.0.2 首次正式发行

2026-09-06：`Stage10 = CLOSED`；`S10-T01 = CLOSED`。不创建 Stage11。

## 正式发行

- Release：<https://github.com/CodeVoyage3/xiaoqipaichanuanjian/releases/tag/v1.0.2>
- tag/source：`v1.0.2` → `02ab6f291c7a52f9de05b19aa29ae9356dc9c676`
- 状态：stable、非 draft、非 prerelease、GitHub latest
- 四资产及完整 SHA256：见 `../ACCEPTANCE/S10-T01-RELEASE-RESULT.json`
- production manifest RSA-PSS/SHA256 原始验签、错误签名/测试 key 拒绝、ZIP hash/bytes/version/migration 完整重验全部通过。

## 验收闭环

- Sol fresh full 1150/1150，failure/error/timeout/aborted/skipped/notExecuted均0；build 0 warning/0 error；EF无漂移，生产migration=9。
- Setup A-K 11/11，Updater/S9-T06/S9-T07 L-M-N 6/6；用户最简 Win11 GUI 明确回复“通过”。
- 正式 source 仅在发布前治理提交后增加验收器字段绑定修复，未修改或放宽生产校验；首轮陈旧验收器失败完整保留。
- 正式资产从修复已 push 且同步的 source 全新生成，未复用失败轮资产。匿名下载、签名和生产客户端完整复验通过。

## 旧开发期版本清理

只有 v1.0.2 全部发布后门禁通过后，才先后删除 v1.0.0、v1.0.1 GitHub Release；确认两者不存在后，再删除对应远端与本地 tags。最终匿名核对 Release、tag、latest 均只剩 v1.0.2。历史 commits 与全部 `.ai-dev` 证据继续保留。

## 边界

- v1.0.2 是首个正式对外版本；v1.0.0/v1.0.1 仅为 Stage9 开发期公开验证产物。
- Windows 可执行文件没有 Authenticode；manifest RSA 签名不冒充 Windows Publisher 签名。
- 所有自动化均在 TEMP/GUID；未访问正式安装根、正式数据根或正式数据库。
- S9-T07 的既有可信边界表述不变；Stage9保持CLOSED。
