import { AbilityId, ChargeModel } from './types';

export const ABILITY_CATALOG_VERSION = 'ability-catalog-v1';

export interface AbilityCatalogEntry {
    id: AbilityId;
    name: string;
    passiveDescription: string;
    activeDescription: string;
    chargeModel: ChargeModel;
    catalogVersion: string;
    globalUnique?: boolean;
}

const ABILITY_CATALOG: ReadonlyArray<Readonly<AbilityCatalogEntry>> = Object.freeze([
    Object.freeze({ id: 'medic', name: '医师', passiveDescription: '枪械命中队友时治疗并提供短暂加速。', activeDescription: '充满后恢复全部存活队友的生命。', chargeModel: 'A', catalogVersion: ABILITY_CATALOG_VERSION }),
    Object.freeze({ id: 'berserker', name: '狂战士', passiveDescription: '有效击杀可回满生命并叠加本回合普通免伤。', activeDescription: '消耗全部充能，在本回合获得无限弹药和射速提升。', chargeModel: 'C', catalogVersion: ABILITY_CATALOG_VERSION }),
    Object.freeze({ id: 'assassin', name: '刺客', passiveDescription: '背后枪械命中造成双倍伤害，静止时可隐身。', activeDescription: '充满后获得 10 秒完全隐身。', chargeModel: 'A', catalogVersion: ABILITY_CATALOG_VERSION }),
    Object.freeze({ id: 'tank', name: '坦克', passiveDescription: '拥有更高生命和减伤，但移动与跳跃较慢。', activeDescription: '消耗充能暂时移除自身惩罚并强化正面减伤。', chargeModel: 'B', catalogVersion: ABILITY_CATALOG_VERSION }),
    Object.freeze({ id: 'istaru', name: '伊斯塔露', passiveDescription: '全场唯一，按阵营调整回合与 C4 时间。', activeDescription: '充满后将全体玩家回溯到 15 秒前的状态。', chargeModel: 'A', catalogVersion: ABILITY_CATALOG_VERSION, globalUnique: true }),
    Object.freeze({ id: 'capitalist', name: '资本家', passiveDescription: '正常比赛奖励翻倍。', activeDescription: '消耗全部充能，按当前充能比例掠夺敌方金钱。', chargeModel: 'C', catalogVersion: ABILITY_CATALOG_VERSION }),
    Object.freeze({ id: 'balance', name: '天平', passiveDescription: '在符合条件的回合开始时获得固定金钱。', activeDescription: '充满后将所有存活敌人的基础生命调为自身当前生命。', chargeModel: 'A', catalogVersion: ABILITY_CATALOG_VERSION }),
    Object.freeze({ id: 'glass_cannon', name: '玻璃大炮', passiveDescription: '生命固定较低，普通弹药枪械伤害大幅提升。', activeDescription: '消耗充能进入死亡爆炸准备状态。', chargeModel: 'B', catalogVersion: ABILITY_CATALOG_VERSION }),
    Object.freeze({ id: 'utility_specialist', name: '道具手', passiveDescription: '可携带更多道具，并强化道具效果。', activeDescription: '充满后在持续时间内返还成功投出的道具。', chargeModel: 'A', catalogVersion: ABILITY_CATALOG_VERSION }),
    Object.freeze({ id: 'commander', name: '指挥官', passiveDescription: '持续显示最近存活敌人的距离。', activeDescription: '消耗充能显示所有存活敌人的姓名与距离。', chargeModel: 'B', catalogVersion: ABILITY_CATALOG_VERSION }),
    Object.freeze({ id: 'sky_courier', name: '超级飞侠', passiveDescription: '可无限空中跳跃并免疫摔落伤害。', activeDescription: '充满后准备传送至自己诱饵弹的落点。', chargeModel: 'A', catalogVersion: ABILITY_CATALOG_VERSION }),
    Object.freeze({ id: 'snow_golem', name: '雪傀儡', passiveDescription: '枪械不造成生命伤害，改为击飞敌人。', activeDescription: '消耗全部充能，暂时恢复枪械伤害并保留击飞。', chargeModel: 'C', catalogVersion: ABILITY_CATALOG_VERSION }),
    Object.freeze({ id: 'witch', name: '女巫', passiveDescription: '造成实际生命伤害时施加持续中毒。', activeDescription: '充满后对附近中毒敌人造成真实伤害并加深中毒。', chargeModel: 'A', catalogVersion: ABILITY_CATALOG_VERSION }),
]);

export const getAbilityCatalog = (): AbilityCatalogEntry[] => ABILITY_CATALOG.map((ability) => ({ ...ability }));
