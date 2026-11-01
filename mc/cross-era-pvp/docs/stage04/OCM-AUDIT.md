# 阶段04：OCM v2.8.0 最小模块边界

依据固定源码及公开 API 审计；下表是源码和配置事实，不是原版 PvP 还原验收。
不复制上游实现，自研仅调用 `kernitus.plugin.OldCombatMechanics.api`。

| 模块 | 跟随对象 / 主要作用 | 本原型 |
|---|---|---|
| disable-attack-cooldown | 玩家攻击速度属性；OCM 负责模式改变和退出恢复 | 仅 old |
| old-tool-damage | 攻击者武器伤害；钻石剑配置8HP；关闭工具提示修改 | 仅 old |
| old-critical-hits | 攻击者条件；上游1.5倍和允许疾跑配置，不自行复刻公式 | 仅 old |
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
世界默认只允许 modern，只有专用实验世界允许 modern/old 且 modern 排第一。此配置不能替代正式 Esc 切图的插件加载隔离。

已知差距：阶段02的鱼竿方向失败和3项上游禁用用例保持原记录；本轮不修改上游或断言。
原生受伤间隔不等于整套1.8.9无敌期间比较/叠加语义已匹配，B05—B07需原版输入对照。
