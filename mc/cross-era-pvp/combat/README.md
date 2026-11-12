# 跨时代战斗原型 0.1.0-dev

状态：实验，待真实客户端及原版对照验收。目标为现代客户端上的 1.8.9 / 26.3 两套近战规则。
本插件管理OCM的命名规则与观察日志；OCM v2.8.0负责战斗。插件本身不实现正式小游戏、旧客户端接入、地图切换或自研伤害重算。2026-11-01另以部署增量接入外层Esc单实例切图，真人往返仍NOT_RUN。

**2026-11-05夜间当前状态：** 已完成离线清单、两项只读工具、管理/日志/生命周期/OCM适配修复，并构建本地候选。Java76通过；证据工具52通过/1跳过；迁移工具38通过。候选SHA-256为 `72a72c8db0513e7d0546b233cbbdbe3a7c2ac1b1fb53212d0e5724562a25d0c8`，来自27aa248基线加未提交修改，未部署、未验收。构建与旧包区分见04-007报告（历史工作记录，未随包分发），全过程见夜间记录（历史工作记录，未随包分发）。下文追加记录按各任务当时状态保留。

## 构建

需要 JDK 25。将官方 OCM v2.8.0 独立 JAR 放入 `libs/OldCombatMechanics.jar`，或传 `-PocmJar=绝对路径`。
可从本仓库 `mc/third-party/old-combat-mechanics/` 的固定源码构建；不要拿其他版本替代。

```powershell
.\gradlew.bat test jar
# 可选：.\gradlew.bat test jar -PocmJar=D:/path/OldCombatMechanics.jar
```

Linux 首先赋予 `gradlew` 执行权限，然后使用 `./gradlew test jar`。
产物为 `build/libs/CrossEraCombat-0.1.0-dev.jar`。依赖缓存、libs、世界、日志与构建产物不提交。
第三方边界见 [THIRD-PARTY.md](THIRD-PARTY.md)。测试需要同一仓库中的固定 OCM 配置原件作分类对照；本工程不会修改它。

## 单实例运行入口、配置与管理

默认 `experimental-enabled: false`、`allowed-worlds: []`，没有启用世界或战斗接管。
用户已改为使用正式服唯一实例，三个独立实验服已删除，不按旧说明重建。现有接入来源与2026-11-01历史证据见[PRODUCTION.md](../docs/stage04/PRODUCTION.md)，本文不代表已授权新的安装、切图或重启。

1. 按获准维护方案先备份目标配置、核对实际单实例及当前地图。cross-era两项独立JAR历史上放在 `maps/cross-era/plugins`，由启动参数按地图加载；不要直接放入供所有玩法共享加载的根plugins目录。
2. 历史Purpur加载器仍将**数据/配置**写入根 `plugins/<插件名>/`，与地图的JAR目录不同。地图ID为 `cross-era`，Bukkit世界名为 `world`；以操作前实际核对结果为准，不凭目录名推断世界名。
3. `examples/ocm-config.yml`、`examples/combat-config.yml` 保留通用示例名 `cross-era-test`；不能原样覆盖正式配置。两份配置必须映射到同一实际允许世界，并依赖按地图加载边界；不全局启用old。
4. 获准操作时完整启动，先检查无配置/API错误，再以管理员命令核对；不使用 `/reload`。本轮没有服务器操作，已有自动加载不代替真人切图/战斗验收。

所有下列命令都要求 `cross-era.combat.admin`（默认 OP），只接受在线玩家；反馈仅执行者可见，绿色 `[草人杯]`。

```text
/cecombat status
/cecombat status <在线玩家>
/cecombat profile <在线玩家> <old|modern>
```

选择通过 OCM 正常 API 按 UUID、世界保存，重连后保留。没有另一份自研玩家规则表。
新玩家遵守 OCM 的现代默认。任意模块 overrides 会导致切换拒绝；本插件不会清空其他插件的覆盖。
模块审计检查旧预设仅启用本轮七项、现代全部关闭；这只是配置一致性，不是原版公式验收。
如果 OCM 已保存选择但模块不符，命令会明确报告不一致，不能将其当作成功的战斗验证。后端调用失败时用 status 核对实际保存值，避免假定外部 API 是可回滚事务。

配置字段在完整重启时读取：实验开关为布尔值；世界为无重复的精确名称列表；后端固定 `ocm`；预设为互不相同的小写标识；日志队列为 16—65536 的整数，默认 2048。
缺依赖、配置非法、默认顺序错误或 API 不符时停止本插件；不替服务器安装、停用 OCM，也不退回另一种结算实现。

## 日志与接口

`plugins/CrossEraCombat/events.jsonl` 为 UTF-8 JSONL，schemaVersion=1。
事件包括 startup、damage_event、health_followup、velocity_event、ocm_modeset_change、player_state、shutdown。
UUID、两方规则、模块状态、装备耐久、位置速度、冷却及受伤状态在主线程采样；后台只接收字符串，250ms 轮询、最长约500ms批量刷出，无游戏对象异步访问。

- `eventBaseDamage` / `eventFinalDamage` 是 MONITOR 事件数值，不等于实际扣血。
- `health_followup.netHealthDelta` 是到下一次采样的净生命变化，可能混入其他攻击、治疗或复活，不自动归因于单次命中。
- `attackCooldownObserved` 是采样时公开 API 返回值，不保证是攻击前冷却；暴击相关字段记录条件，不伪称原生暴击判定。
- 速度事件不自动归因于某次命中；合成 Bukkit 事件也无法仅凭观察判断为真实输入。
- 队列满会拒绝记录并限频告警，不阻塞攻击。status 显示 accepted/rejected/errors；关闭最多等待5秒收尾。出现丢记录、I/O错误或收尾超时，本轮证据不能视为完整。
- 记录为私有研究数据，不进 Git 或发行包；不采集账号凭据、昵称、聊天、物品自定义文本或网站会话。

启用成功时通过 Bukkit Services 注册 `com.caoren.crossera.core.CombatService`；其公开 `profile`、`select`、`routing` 均须在服务端主线程使用。
`routing` 分别返回攻击者与受击者身份，不直接计算伤害。调用方须检查 `verifiedModules`、overrides 和异常，不能从规则名称推断还原已通过。
后续目标是自研替换后端，未来小游戏不依赖OCM内部包；当前接口仍含modeset、模块审计和overrides等OCM语义，独立化接口/数据迁移方案尚需确认，不能称为已经完全解耦。

## 停用和验收

停用前在允许世界把实验玩家切回 modern，并核对 status；停服、备份 OCM 玩家数据后移除测试组件。
只移除管理插件不会撤销 OCM 已保存的模式。未上线的实验玩家仍可能保留 old，恢复使用前逐个核对；不按空资料清玩家数据。
插件本身不发物品、不传送、不创建方块，也不重算伤害或施加速度。OCM 自己的属性和耐久行为见 [模块审计](../docs/stage04/OCM-AUDIT.md)。

原版对照步骤和未验证项见[阶段04验收清单](../docs/stage04/ACCEPTANCE.md)，逐轮使用[结果模板](../docs/stage04/RESULT-TEMPLATE.md)。[离线核对](../docs/stage04/OFFLINE-REVIEW-20261105.md)区分历史36项/6项与本轮36项/4项结果，并列出管理、配置、日志接入缺口。

[独立化依赖与下一步提案](../docs/stage04/REPLACEMENT-PLAN.md)包括已确认的原规则选择保留原则。2026-11-05首次离线收尾仅改文档，后续04-002新增工具，04-004至04-007继续修改原型并构建候选；Git HEAD仍为27aa248，新增源码尚未提交，没有重新部署。构建/单测通过不能将整个阶段04标记完成或替代用户验收。

## 2026-11-05追加：离线证据工具

用户批准后，TASK-MC-CROSSERA-04-002已新增[离线证据完整性校验器](tools/README.md)与自动测试，可对私有清单和现有JSONL做只读核对。该任务本身不改变本插件战斗源码/配置，不写玩家数据；交付当时没有重建或部署。后续新候选及旧包备份见本文顶部的当前状态和04-007报告。

工具报告只判断所列证据的结构，不生成战斗PASS；真人攻击、生命周期、原版对照仍NOT_RUN。上一段“仅文档整理”描述此前离线收尾，本次追加的Python源码及验证单独见04-002交付（历史工作记录，未随包分发）。

夜间继续完成R02：[OCM选择只读迁移清单](tools/MIGRATION.md)解析明确提供的BSON副本，保留玩家/世界/原预设并列出未知项。38项合成测试和官方编码器交叉读取通过；未读取真实玩家副本或执行迁移。详见04-003交付（历史工作记录，未随包分发）。

## 2026-11-05夜间：管理与日志候选修复

04-004已修改本地Java源码：空规则/玩家在写前拒绝；切换须读回实际保存值；后端状态异常返回管理错误；日志捕获普通Writer运行时异常、回调失败计数，非法参数在打开文件前拒绝。54项JUnit通过，详见交付记录（历史工作记录，未随包分发）。这些源码尚未构建为新JAR或部署，不改变上文历史候选的部署时间/哈希。

保存选择的API读回不等于磁盘刷盘；上游读取未知玩家也可能创建空玩家文档。自研不把缺失选择补成old/modern。真实重连、重启和生命周期仍待测。

04-005本地候选进一步加入[生命周期观察保护](tools/FOLLOWUP-PROTOCOL.md)：死亡/复活/退出/切图等通知使旧采样窗口失效，抑制跨窗口HP差，startup显式声明能力。最终63项Java通过、Python48通过/1跳过，Java实际序列化输出与Python读取已交叉验证；均为合成/本地证据，未部署，真人仍NOT_RUN。

04-006进一步核对OCM API的注册owner、依赖实例和provider，防止接受其他插件的服务或继续使用被替换的服务；新增13项接口替身测试后，Java总计76项通过。详细边界见04-006交付（历史工作记录，未随包分发）；未运行真实服务端停用/重载测试。
