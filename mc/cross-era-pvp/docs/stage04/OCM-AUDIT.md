# 阶段04：OCM v2.8.0 最小模块边界

依据固定源码及公开 API 审计；下表是源码和配置事实，不是原版 PvP 还原验收。
不复制上游实现，自研仅调用 `kernitus.plugin.OldCombatMechanics.api`。

| 模块 | 跟随对象 / 主要作用 | 本原型 |
|---|---|---|
| disable-attack-cooldown | 玩家攻击速度属性；OCM 负责模式改变和退出恢复 | 仅 old |
| old-tool-damage | 攻击者武器伤害；钻石剑配置8HP；关闭工具提示修改 | 仅 old |
| old-critical-hits | 攻击者条件；当前源码默认允许疾跑、倍率1.5；配置键名差异见下文，不自行复刻公式 | 仅 old，需真人与原版对照 |
| disable-sword-sweep | 攻击者；去除旧身份的横扫 | 仅 old |
| old-armour-strength | 受击者；旧护甲与附魔等减伤链，测试仅无附魔钻石甲 | 仅 old |
| old-armour-durability | 受击装备耐久；上游 reduction=1 | 仅 old |
| old-player-knockback | 近战跟随攻击者；环境与部分非玩家路径跟随受击者；第三方可改变速度及抗性修饰 | 仅 old；仅钻石装备场景待验 |
| attack-frequency | 主要修改受击实体 maximumNoDamageTicks；上游全局默认18/16会改变现代行为 | 两套均禁用，保留原生20；不声称已解决所有无敌帧差异 |
| old-golden-apples | 上游 new 预设也启用它 | 两套均禁用，消耗品不在本轮范围 |
| 其余可配置模块 | 范围、格挡、药水、恢复、鱼竿、投射物、制作、冷却及更新器等 | 全部禁用；非近战内容未验收 |

没有 always_enabled_modules。示例将固定版本全部30个可配置模块分类一次，测试校验与上游来源的集合一致。
内部 modeset-listener、entity-damage-listener 由 OCM 管理，不冒充可配置模块。内部伤害管线会参与旧规则，不能仅凭现代预设列表为空就宣称现代完全未受影响，仍需原版对照。

OCM 的模块改动可能覆盖攻击速度、耐久、击退与抗性修饰；本管理插件不额外复制这些操作。
API modeset 选择持久化，读取以 OCM 玩家/世界数据为准；共享 overrides 优先级高于配置，因此存在覆盖时管理切换拒绝，不清空它们。
示例世界默认只允许modern，只有 `cross-era-test` 允许modern/old且modern排第一。正式接入历史配置使用实际Bukkit世界名 `world`，JAR按cross-era地图加载；不能把示例世界名当成生产值。Esc加载增量已经接入，但真人往返及其他地图实际隔离仍NOT_RUN，见[接入记录](PRODUCTION.md)。

已知差距：阶段02的鱼竿方向失败和3项上游禁用用例保持原记录；本轮不修改上游或断言。
原生受伤间隔不等于整套1.8.9无敌期间比较/叠加语义已匹配，B05—B07需原版输入对照。

## 2026-11-05离线复核与限制

- 自研 `CombatService.routing` 只返回攻击者/受击者各自身份，16项桩后端测试验证路由与管理状态；它没有决定OCM伤害管线。实际模块归属必须以固定源码及真人数据分别核对，不能由接口测试推出公式正确。
- 当前管理/配置测试可核对模块集合、世界边界、默认顺序和覆盖冲突，不能证明30项模块的所有配置键都被上游读取；自动覆盖表见[离线核对](OFFLINE-REVIEW-20261105.md)。
- 发现一项固定源码内的不一致：示例和上游YAML使用 `old-critical-hits.allow-sprinting`，而[ModuleOldCriticalHits.reload](../../../third-party/old-combat-mechanics/src/main/java/kernitus/plugin/OldCombatMechanics/module/ModuleOldCriticalHits.java)读取 `allowSprinting`，默认true；`OCMModule.module()`直接返回配置段。当前示例true与源码默认相同，所以这不是本轮已证实的数值差异；但不能宣称该连字符配置键已被验证生效，未来改false也不能据此保证禁用疾跑暴击。真实候选行为保持待测。
- 本轮不修改上游、示例或生产配置，不新增疾跑暴击规则，也不把上游8HP/1.5倍/20tick字段写成原版采样值。S03/S04/S05分别观测未疾跑下落、地面疾跑、疾跑下落，保留输入与真实伤害证据。
- 无甲/钻石甲范围内检查两种身份的耐久与受击行为；不将钻石装备的结果扩展到下界合金抗性、附魔、药水或投射物。OCM击退模块会接触受击方抗性修饰，现代回归与状态恢复仍需实际核验。
- 同版原版基准、混版组合判据、B05—B07输入方法和击退/时序容差尚缺。判据待确认时不得调预期消除差异，详见[验收清单](ACCEPTANCE.md)。
