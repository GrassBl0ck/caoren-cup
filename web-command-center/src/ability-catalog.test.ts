import assert from 'node:assert/strict';
import test from 'node:test';
import { getAbilityCatalog } from './ability-catalog';
import { createInitialSession } from './session-manager';

const expectedAbilityIds = [
    'medic',
    'berserker',
    'assassin',
    'tank',
    'istaru',
    'capitalist',
    'balance',
    'glass_cannon',
    'utility_specialist',
    'commander',
    'sky_courier',
    'snow_golem',
    'witch',
];

test('职业目录包含全部稳定职业 ID 和公开元数据', () => {
    const catalog = getAbilityCatalog();

    assert.deepEqual(catalog.map((ability) => ability.id), expectedAbilityIds);
    for (const ability of catalog) {
        assert.match(ability.name, /[\u4e00-\u9fff]/);
        assert.ok(ability.passiveDescription.length > 0);
        assert.ok(ability.activeDescription.length > 0);
        assert.ok(['A', 'B', 'C'].includes(ability.chargeModel));
        assert.ok(ability.catalogVersion.length > 0);
    }
});

test('仅伊斯塔露为全场唯一职业', () => {
    const globalUniqueIds = getAbilityCatalog()
        .filter((ability) => ability.globalUnique)
        .map((ability) => ability.id);

    assert.deepEqual(globalUniqueIds, ['istaru']);
});

test('返回的职业目录副本不会修改内部元数据', () => {
    const firstCatalog = getAbilityCatalog();
    firstCatalog[0].name = '被修改的名称';
    firstCatalog.push(firstCatalog[0]);

    const secondCatalog = getAbilityCatalog();
    assert.equal(secondCatalog.length, expectedAbilityIds.length);
    assert.equal(secondCatalog[0].name, '医师');
});

test('新建大厅默认关闭异能模式并使用规定的 BP 时限', () => {
    const { matchOptions } = createInitialSession();

    assert.equal(matchOptions.abilityModeEnabled, false);
    assert.equal(matchOptions.abilityBanCountPerTeam, 1);
    assert.equal(matchOptions.abilityBanSeconds, 45);
    assert.equal(matchOptions.abilityDraftBatchSeconds, 30);
});
