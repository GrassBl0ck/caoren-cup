# TASK-MC-CROSSERA-04-001 本地交付与审查记录

执行者：Codex；日期：2026-11-01。用户明确指定直接编码，本轮未调用 DSH、未委派实际开发。

## 结论与状态

已完成可构建的 CrossEraCombat 0.1.0-dev 本地实验原型：OCM 命名规则适配、管理员切换、配置审计、攻防身份接口及异步观察日志。
36项本地测试通过；4247个原导入文件保持一致；JAR范围与许可检查通过。
**最新进展：用户改为直接使用正式服测试。原型已接入原有MC服务的cross-era地图，外网原端口可连接；三组独立测试目录已按要求直接删除、不备份。真人战斗和原版PvP对照仍待验收。最新部署记录见 `mc/cross-era-pvp/docs/stage04/PRODUCTION.md`。**

| 分项 | 实际状态 |
|---|---|
| 代码 | 本轮新增工程和相关文档完成 |
| 本地测试 | 36通过，0失败、0错误、0跳过；来源与产物检查通过 |
| 服务端自动加载 / 配置 | 6项通过；详见本报告追加验证 |
| 真实客户端 | NOT_RUN；用户选择稍后安排 |
| 原版伤害 / 时序 / 击退对照 | NOT_RUN |
| 用户验收 | 待完成 |
| 提交 / 合并 main / 推送 | 均未执行 |
| 发行 / 正式部署 / 正式重启 | 未发行；正式服已切至cross-era并保持运行，等待真人测试 |

## 实际工作区与变更

- 工作树：`D:/OpenSourcework/02-worktrees/caoren-cup/feat-mc-cross-era-melee`。
- 分支：`feat/mc-cross-era-melee`；基线 HEAD：`5c27ddfc017452b992d791ac7fbca7e0287b02f8`。
- Owner：`TASK-MC-CROSSERA-04-001-Codex`。最初的全局兼容锁正常释放后才取得本工作树写入占用；没有改锁或停止另一项任务。
- 主工作树及坐垫战争B改动未接手；不在旧独立工程继续维护源码。
- 新增 `mc/cross-era-pvp/combat/`：独立 Gradle 工程、源码、5组测试、两份实验配置、说明与许可。
- 新增 `mc/cross-era-pvp/docs/stage04/`：OCM模块审计、原版验收矩阵。
- 新增本任务与本报告，未修改旧stage02/stage03、OCM、原导入清单、源校验器、根索引或发行包脚本。
- 备份、局部缓存和证据保存在本工作树 Git忽略目录 `runtime/cross-era-stage04/`；凭据不进入本报告、源码或产物。

## 已实现的行为

1. 自有 `CombatService` 经 Bukkit Services 提供身份查询、规则切换和攻击/防御独立路由；OCM后端只调用公开 API，不复制底层实现或重复结算伤害。
2. `/cecombat profile <在线玩家> <old|modern>` 与 `/cecombat status [在线玩家]`；所有执行和补全检查 `cross-era.combat.admin`，默认OP；响应统一私有绿色 `[草人杯]`。
3. 规则以 OCM 的 UUID/世界选择为唯一来源，重读实际状态，不维护另一份缓存。存在共享 overrides 时拒绝切换，不清空其他插件覆盖。API失败后不会伪称成功；外部写入后失败也可从权威状态读到实际结果。
4. 默认关闭实验、世界允许列表为空；启用时检查依赖版本、所需模块、命名预设、加载世界及现代默认顺序。非法配置/缺依赖停止本插件，不切换另一套后端或停用OCM。
5. 专用OCM配置只让old启用七项近战模块；modern无旧模块，无 always_enabled_modules。禁用上游全局18/16受伤间隔、旧金苹果、非近战模块及自动更新。
6. 记录伤害事件、两方状态与规则、装备耐久、位置/速度、下次采样净生命变化、速度事件、OCM选择变更及启停；后台只接收序列化字符串，队列有界，溢出拒绝、I/O失败可见、关闭限时收尾。

## 真实验证

构建环境：JDK25，固定Gradle9.8.2 Wrapper；Paper API `26.3.build.223-stable`；Gson2.14.0；本地 compileOnly OCM v2.8.0。
编译依赖JAR SHA-256：`3c8c9fa33124e15c0b4bb6e2807b6e1b3535273867782965d00aa8981d558a60`。

实际执行本工程 `gradlew.bat --project-dir <combat> test jar --no-daemon --console=plain`，附带本任务局部Java/缓存/临时目录与进程级网络属性；未修改全局工具或网络配置。
本地测试使用常规JUnit，不启动Minecraft服务器。

| 测试组 | 通过数 | 范围 |
|---|---:|---|
| CombatSettingsTest | 7 | 默认关闭、世界边界、非法类型/后端/预设/队列 |
| CombatServiceTest | 16 | 默认不写、权威保存/重读、四向身份、UUID/世界边界、覆盖冲突、缺依赖、后端失败 |
| CombatCommandsTest | 5 | 未授权前置拒绝、离线/非法输入、切换状态、模块不一致反馈 |
| ExampleConfigTest | 4 | YAML解析、30模块完整且唯一分类、现代无旧模块、实验世界默认 |
| JsonJournalTest | 4 | UTF-8排空与重复关闭、有界溢出、磁盘失败、非法数值拒绝 |

- 测试先行记录：19个初始契约测试在未实现的核心入口上失败；实现后通过，随后新增命令、日志、示例及边界测试，最终36项通过。
- 首次Bukkit桥接编译发现 Plugin/Listener 双重身份导致 `unregisterAll` 重载不明确，明确转为 Plugin 后构建通过。
- `python -B mc/verify_sources.py`：4247个导入文件及组件许可证通过；1项既有、有记录的集成变更，未增加历史字节变化。
- `git diff --check`：通过；另检查所有新增路径仅在批准范围，UTF-8可解码、没有秘密、日志、依赖缓存或产物进入待提交清单。
- JAR：30个条目，仅自研类、配置/插件描述与MIT许可；没有OCM、Paper、Gson、JUnit类、日志、备份或玩家数据。默认配置仍关闭实验。

证据（私有、被Git忽略）：

- `runtime/cross-era-stage04/build-red.log/json`
- `runtime/cross-era-stage04/build-core-green.log/json`
- `runtime/cross-era-stage04/build-plugin-green.log/json`（初次编译失败）与 `build-plugin-green2.log/json`
- `runtime/cross-era-stage04/build-examples-green.log/json`、`build-final.log/json`、`build-reviewed.log/json`
- `runtime/cross-era-stage04/verification.json`
- `mc/cross-era-pvp/combat/build/test-results/test/TEST-*.xml` 与 HTML 测试报告

## 产物

- `mc/cross-era-pvp/combat/build/libs/CrossEraCombat-0.1.0-dev.jar`
- 大小：42607字节。
- SHA-256：`0c38bcbf547278f0d99349f630bbf2b92c3e643695f6bb338c0cf555a3f2dbdf`。
- 未上传、未进入正式发行包。构建 JAR 受MIT许可；OCM是独立第三方依赖，不包含在该JAR。

## 直接审查与未验证项

对本轮新增实现直接检查了权限前置、配置范围、OCM公开API边界、无重复结算、后台线程不访问Bukkit、故障可见、停用语义与产物范围，未发现本轮本地交付范围内的阻断问题。没有启动另一名开发代理或冒充独立审查。

- 核心测试使用桩后端，只证明管理与路由契约。追加服务端测试验证了API服务启动及实验世界配置；OCM真实玩家持久化、游戏事件顺序和原版公式仍未对照。
- `eventFinalDamage` 是事件数值；后续净生命变化和速度事件并不保证只来自一次攻击。冷却为采样时公开API值，也不保证是攻击前状态。
- OCM实际模块表不匹配时命令会报告未通过。源码审计/预设表不能替代完整原版对照；modern空预设也不能排除内部管线影响。
- 受伤间隔保持原生20 ticks；1.8.9无敌期间同tick、较高伤害、零伤害历史的差异仍未实测解决。
- 阶段02的145通过、1个鱼竿方向失败、3个上游预先禁用保持历史结论；没有修改上游或通过放宽断言消除问题。
- OCM可能改变属性、耐久和击退；停用管理插件不撤销已保存的第三方规则。正式Esc切图加载隔离仍待后续实现。
- 真实1.8.9客户端、现代独有物品、消耗品和正式小游戏不在本轮范围。

## 追加服务端自动验证（2026-11-01）

用户选择先补齐阶段04验收，并明确“先完成自动验证，真人测试稍后安排”。沿用获准的临时隔离方式，正版验证开启、仅回环监听，每次一台、内存上限2GiB，完成后全部停止。
没有改动正式服务的程序、配置、地图选择、玩家存档或已安装插件；原小游戏合集的进程与关键文件校验保持一致。

| 用例 | 实际结果 |
|---|---|
| 默认关闭，未安装OCM | 插件可加载、状态命令可用，实验保持关闭，无研究日志文件 |
| 有效配置，OCM v2.8.0 | 两个插件启用；公开API、实验世界及现代默认检查通过；控制台管理状态/用法/非法规则/离线目标响应符合预期 |
| 实验启用，但未安装OCM | 明确拒绝启用本插件；Paper继续运行 |
| 允许列表包含未加载世界 | 明确拒绝启用本插件；Paper继续运行 |
| 实验开关被写为字符串 | 明确拒绝非法类型；Paper继续运行 |
| 实验世界以old为默认 | 明确拒绝错误顺序；Paper继续运行 |

- 六项最终全部PASS；没有真人连接，不能把控制台命令的分支验证当成真实玩家模式切换。
- 正常启用环境记录startup/shutdown，errors=0、rejected=0，退出时完成日志收尾；尚未触发真实玩家damage_event/health_followup/velocity_event。
- 每台均正常停止，MainPID=0、ExecMainStatus=0，无临时Java进程和监听端口残留。峰值内存1124589568字节，低于2147483648字节限制。
- 候选自研JAR与OCM JAR的实际部署前后哈希均一致；本轮未修改生产源码、原配置样例或重新构建候选。
- 首次默认关闭用例被测试脚本中止：脚本未识别`::ffff:127.0.0.1`形式的回环地址，实际监听仍为本机。修正脚本后通过6项地址边界检查，在独立重试目录通过；原失败目录、日志及结果保留，未篡改为通过。
- 反向用例中的ERROR是预期拒绝证据；正常启用和默认关闭日志未见ERROR/异常。

私有证据：`runtime/cross-era-stage04/validation-20261101/evidence/remote/runtime-verification.json` 与同级各用例结果、日志。
归档：`runtime/cross-era-stage04/validation-20261101/evidence/stage04-runtime-evidence.zip`，SHA-256：`07ef35fb0c11ca787d65b766971026b588ee8c3ccd6deceac8d81ab44735e6f8`。服务器配置、秘密和玩家存档未收入该归档。

后续用户明确取消独立测试服并要求不备份删除：三组目录已清理，原正式服按单实例切图接入。当前接入事实、启动增量、菜单及出生场地见 `mc/cross-era-pvp/docs/stage04/PRODUCTION.md`，本节前文保留历史自动验证事实。

下一步：用户使用现代客户端进入正式服当前cross-era地图，验证真实切换、重连与伤害日志；原版对照方法按新的单实例约束另行安排，不自动重建测试服。整个阶段04仍待用户验收。
