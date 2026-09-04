# S9-T02 Sol 独立验收证据

本目录保存 Sol 独立验收使用的原始脚本和 JSON 证据；它们不是业务生产实现，也不会被产品 publish 打包。

- 将 `verify.ps1`、`probe.py`、`negative.ps1` 和 `negative.py` 连同所需 publish 内容复制到一个全新的 `%TEMP%\\<GUID>` 目录后再运行。脚本会拒绝在仓库目录或其他路径直接运行。
- `verify.ps1` 需要传入真实的 publish 根目录和 Inno Setup 编译器路径。
- 脚本中的仓库路径固定为本项目 `D:\\wendang\\ChatGPT\\门店效期排查软件`。
- 负例脚本需要同一输出根中的 publish 内容；如另行放置，先把 `negative.ps1` 的应用路径更新为明确的 publish 路径。

JSON 文件记录该次独立 A-I 验收的输入、检查结果和文件指纹。
