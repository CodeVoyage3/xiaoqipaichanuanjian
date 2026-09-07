# S12-T01 实施证据（尚未独立接受）

状态 IN_PROGRESS / NOT_ACCEPTED。当前生产diff仅3个UI文件；未改任务算法、导入事务、Domain、EF、Updater。

可信Release对照证据目录：`%TEMP%/S12T01-3ec639a4c22b4d80b7c7e31b6820fdc5`。
- `S12T01-Case1-ReleaseOldChain.trx`：旧刷新接线0/1，真实首次导入commit后DB已有任务，今日UI仍空，查询计数[0,0,1]。
- `S12T01-Case1-ReleaseFixed.trx`：新接线1/1，查询计数[0,0,1,1]，今日和首页均为1。
- 先前Debug运行受项目CopyProductionAppToTestOutput固定输出覆盖旧DLL影响，不作为可信生产前后对照；带手动Today.Load的诊断通过也不计入修复证据。
- 相关生命周期56/56为实施收据，不替代Case2-8真实UI会话覆盖。
- UI组合先65/66、后64/65失败记录均保留；Sol定位为分类切换异步测试未等待加载完成，已授权仅修改等待条件，禁止生产扩修。

初始Terra已停止，唯一测试写者切换为全新s12_terra_completion（Terra medium）；独立Sol仍只读，不并发build/test。

尚欠完整Case2-8、最终直接回归、Sol独立验收/build/EF/secret scan。DPI及真实视觉/点击为USER_GUI_REQUIRED。未publish、未发布、未宣称REAL_GITHUB升级。

## 后续补齐（Terra实施收据，独立Sol仍待完成）
Case2-8真实TEMP/GUID数据库+Shell当前会话覆盖已完成。S4T06=25/25；负向场景同时验证导入成功、商品分类/库存入库，再断言无任务；重复刷新检查任务总数1；二次库存0导入清空旧open任务/今日页并同步首页。
最终断言收据：`S12T01-Case2-8-Release-Assertions.trx`，位于上方证据根。补强前直接相关组合85/85（收据目录GUID录入差异为S12T01-3ec639a4c22b4d80b7c31b6820fdc5，文件S12T01-DirectRegression-Release.trx）；不以该旧收据替代最终独立复验。
两个分类切换测试仅修正等待异步加载结束的条件，未改变生产筛选语义。重复接线源码字符串测试已移除，保留真实行为用例。生产仍仅3个UI文件，Terra已停止写入，独立Sol开始最终门禁。

## 独立复验已完成
Sol最终178/178及build/EF/migration/diff/限定secret门禁已通过，见S12-T01-SOL.md。本文件上方待验状态为过程历史。仍欠候选资产、用户GUI与发布后升级，不关闭任务。
