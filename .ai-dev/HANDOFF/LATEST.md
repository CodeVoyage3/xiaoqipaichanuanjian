# 最新交接

## 当前状态与停止点

- Stage8：`IN_PROGRESS / WAITING_NEXT_AUTHORIZATION`；S8-T01、S8-T02均技术验收关闭。停止，不创建/实施S8-T03；T03～T06仅候选，Stage9与在线升级未开始。
- V1-F01、V1-F02、V1-F03、V1-F03-I04、V1-UI-01继续CLOSED；V1-UI-01保持GUI_ACCEPTANCE_PASSED。本轮未启动真实GUI，不冒充人工GUI验收。
- 正式数据库仍禁止调查/读取/哈希/修复。本轮测试显式隔离TEMP；旧轮疑似默认库访问未核实，不能改写为未发生。

## Git / 角色 / 实现

- S8-T02初始main：`0ede8b901fb5e6cbc1c2f2824d6f8a6c7a54f901`；隔离恢复main：`0d1d42a0667909e52264c5cd091297a827338788`。推送前fetch仍为后者，无远端新增。
- 旧diff已保存在外部 `S8-T02-recovery/old-terra-uncommitted.patch`，hash及原回执见Acceptance；恢复clean main后才派发新Terra，旧代理不复用。
- 新 `/root/s8_t02_isolation_new_terra`：GPT-5.6 Terra / medium / priority（用户已允许后续该速度，不重复询问）。隔离 `e294a6b`、查询 `50539c2`、修复 `88d1896`、最终测试 `81a0c1c`，提交后停止。Sol只写治理并独立验收，未写生产代码。
- Dashboard汇总、待排查/Today数据库分页筛选、Reminder轻量候选、历史分页/排序/页内聚合已接入；保留Today跨页选择，只新增必要原样式分页控件。无索引/Schema/migration/ModelSnapshot/PRAGMA/依赖/csproj/slnx变更。

## Sol独立验收（2026-09-04）

- 最终隔离9/9，默认factory调用0，TEMP路径门禁通过；正确性150/150；Release912/912、0失败/跳过；build0 warning/0 error。
- EF无漂移；migration代码清单9（--no-connect），末条 `20260901155124_AddPolicyAndBaselineFoundation`；设计时factory用内存SQLite，不连接正式库。
- 高规模After1/1，20条路径无blocker；真实100,000 Batch / 300,000 Inspection及Item。SQLite及snapshot225,427,456bytes、integrity=ok、FK=0、migration9；前后计数及采样指纹一致，不是全库逐字段证明。
- Sol独立TEMP旧main工作树只重跑安全压测fixture得到新鲜Before，未跑旧Shell测试。After运行于88d1896，最终81a0c1c只补测试，生产/基座diff为空。
- 完整diff、禁止文件及diff check通过。归档普通 `git push origin HEAD:main`，不force或改写历史；归档SHA以本文件所在提交为准，避免自引用SHA。

## 性能与残留

- Stage median7241.50→174.68ms；待排查首页387.64ms、深页752.40ms、Dashboard389.02ms、Today355.55ms、历史分页628.39ms、提醒全评估1657.34ms。旧100k变量数阻断消失。
- 搜索39.65→128.18ms；snapshot单次6436.42→12815.98ms，原因未证明。历史Before全量300k与After50条分页不同形状；测量是查询调用，不是GUI渲染。
- 最慢5条：snapshot、提醒全评估、提醒候选、深页、历史分页。仍有索引扫描、相关MIN、TEMP B-TREE；历史submitted_at_utc/id排序索引仅待验证候选，未实施。
- 未验证100k全选Excel实际导出；52项跨页选择转交有证据，既有导出超大IN潜在限制未宣称修复。导入/中断/损坏/大库灾难恢复稳定性仍待后续授权。

## 证据

- 完整命令、20路径Before/After、SQL口径、局限及旧隔离事件：`.ai-dev/ACCEPTANCE/S8-T02.md`；Task `.ai-dev/TASKS/S8-T02.md`；阶段 `.ai-dev/STAGES/STAGE-8.md`。
- 本机JSON/SQL归档：`C:\Users\39037\.codex\visualizations\2026\09\03\01a06754-503b-72c0-b8f1-9e10cd9cad3f\S8-T02-evidence`，before/after目录，不含数据库文件，未宣称上传GitHub。
- Before SHA256 `2C2205DD35F7BF95F41BD8C36E934E977A056F622A3211A65D215E27B3D26BD4`；After `8FCF92343B77F926A79BD00D8D8F88D13769F260D9618B25D4A503ECA70E3F5F`。

等待下一卡明确授权；本轮“后续都允许”不扩大为新卡或正式库访问权限。
