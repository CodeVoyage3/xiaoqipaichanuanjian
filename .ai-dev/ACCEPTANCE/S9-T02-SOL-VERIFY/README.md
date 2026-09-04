# S9-T02 Sol 独立验收证据

本目录保存 Sol 独立验收使用的原始脚本和 JSON 证据；它们不是业务生产实现，也不会被产品 publish 打包。

- 将 `verify.ps1`、`probe.py`、`negative.ps1` 和 `negative.py` 连同所需 publish 内容复制到一个全新的 `%TEMP%\\<GUID>` 目录后再运行。脚本会拒绝在仓库目录或其他路径直接运行。
- `verify.ps1` 需要传入真实的 publish 根目录和 Inno Setup 编译器路径。
- 脚本中的仓库路径固定为本项目 `D:\\wendang\\ChatGPT\\门店效期排查软件`。
- 负例脚本需要同一输出根中的 publish 内容；如另行放置，先把 `negative.ps1` 的应用路径更新为明确的 publish 路径。

JSON 文件记录该次独立 A-I 验收的输入、检查结果和文件指纹。

`matrix-final.json`/`identity.json`为第一轮；`final-matrix.json`/`final-identity.json`为第二轮最终核心复验，均9/9 PASS。`terra-identity-cleanup.json`是Sol对两组已知Terra测试身份的只读清理核对。最终正式EXE信息及全局技术门禁见上级`S9-T02-INSTALLER-RESULT.json`。

`negative-results.json`的`sourceUnchanged`沿用脚本原始字段：true表示做了源文件字节比较；busy_writer和linked_data的false表示该项没有使用通用源树比较，并非检测到修改。busy_writer只验阻断，释放句柄后再验源；linked_data另有链接目标不变断言。负例脚本应在同一证据根先完成A-I并保留`preserved-AF-data`后运行。

大安装器、完整publish、合成DB和原始运行日志仅保存在验收记录中的TEMP输出根，可能被系统清理；Git内不含二进制或正式用户数据。
