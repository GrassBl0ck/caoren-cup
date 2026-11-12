import assert from 'node:assert/strict';
import test from 'node:test';
import { AbilityId } from './types';
import {
    buildAbilityDraftBatches,
    calculateSafeBanLimit,
    confirmAbilityBan,
    confirmAbilityChoice,
    createAbilityBanState,
    createAbilityDraftState,
    finishCurrentAbilityBatch,
    resolveAbilityBans,
    updateAbilityBanSelection,
    updateAbilityChoice,
} from './ability-draft-service';

test('BP 状态只保存可序列化数据和 timeoutAt', () => {
    const state = createAbilityBanState({
        orderedA: ['A1'],
        orderedB: ['B1'],
        banCountPerTeam: 1,
        timeoutAt: 123456,
    });

    assert.equal(state.timeoutAt, 123456);
    assert.doesNotThrow(() => JSON.stringify(state));
    assert.equal('timer' in state, false);
});

test('5v5、A 首选时按 1-2-2 蛇形生成批次', () => {
    const batches = buildAbilityDraftBatches(
        'A',
        ['A1', 'A2', 'A3', 'A4', 'A5'],
        ['B1', 'B2', 'B3', 'B4', 'B5'],
    );

    assert.deepEqual(batches, [
        { team: 'A', playerIds: ['A1'] },
        { team: 'B', playerIds: ['B1', 'B2'] },
        { team: 'A', playerIds: ['A2', 'A3'] },
        { team: 'B', playerIds: ['B3', 'B4'] },
        { team: 'A', playerIds: ['A4', 'A5'] },
        { team: 'B', playerIds: ['B5'] },
    ]);
});

test('4v4、B 首选与不等人数都会跳过空批次且不遗漏玩家', () => {
    assert.deepEqual(
        buildAbilityDraftBatches('B', ['A1', 'A2', 'A3', 'A4'], ['B1', 'B2', 'B3', 'B4']),
        [
            { team: 'B', playerIds: ['B1'] },
            { team: 'A', playerIds: ['A1', 'A2'] },
            { team: 'B', playerIds: ['B2', 'B3'] },
            { team: 'A', playerIds: ['A3', 'A4'] },
            { team: 'B', playerIds: ['B4'] },
        ],
    );

    const uneven = buildAbilityDraftBatches(
        'A',
        ['A1', 'A2', 'A3', 'A4', 'A5'],
        ['B1', 'B2', 'B3'],
    );
    assert.deepEqual(uneven.flatMap((batch) => batch.playerIds).sort(),
        ['A1', 'A2', 'A3', 'A4', 'A5', 'B1', 'B2', 'B3'].sort());
    assert.ok(uneven.every((batch) => batch.playerIds.length > 0));
    assert.deepEqual(buildAbilityDraftBatches('A', ['A1'], ['B1']), [
        { team: 'A', playerIds: ['A1'] },
        { team: 'B', playerIds: ['B1'] },
    ]);
});

test('安全 Ban 上限保证任意最终不同 Ban 集仍可合法分配', () => {
    assert.equal(calculateSafeBanLimit({ teamSizeA: 1, teamSizeB: 1 }), 5);
    assert.equal(calculateSafeBanLimit({ teamSizeA: 3, teamSizeB: 3 }), 4);
    assert.equal(calculateSafeBanLimit({ teamSizeA: 5, teamSizeB: 5 }), 3);
    assert.equal(calculateSafeBanLimit({ teamSizeA: 5, teamSizeB: 3 }), 3);
});

test('安全 Ban 上限按实际有人的投票队伍数计算最大值', () => {
    assert.equal(calculateSafeBanLimit({ teamSizeA: 5, teamSizeB: 0 }), 8);
    assert.equal(calculateSafeBanLimit({ teamSizeA: 1, teamSizeB: 0 }), 12);
});

test('5v3 安全上限保证短队先选伊斯塔露后双方仍能完成选角', () => {
    const state = createAbilityDraftState({
        firstTeam: 'B',
        orderedA: ['A1', 'A2', 'A3', 'A4', 'A5'],
        orderedB: ['B1', 'B2', 'B3'],
        bannedAbilityIds: ['medic', 'berserker', 'assassin', 'tank', 'capitalist', 'balance'],
        timeoutAt: 10,
    });
    updateAbilityChoice(state, 'B1', 'istaru');
    confirmAbilityChoice(state, 'B1');

    while (state.currentBatchIndex < state.batches.length) {
        assert.deepEqual(finishCurrentAbilityBatch(state, () => 0), { ok: true });
    }

    assert.equal(state.assignments.length, 8);
    assert.equal(new Set(state.assignments.filter(({ team }) => team === 'A').map(({ abilityId }) => abilityId)).size, 5);
    assert.equal(new Set(state.assignments.filter(({ team }) => team === 'B').map(({ abilityId }) => abilityId)).size, 3);
});

test('5v3 使用安全上限加一时存在短队拿伊斯塔露导致长队无解的 Ban 集', () => {
    const state = createAbilityDraftState({
        firstTeam: 'B',
        orderedA: ['A1', 'A2', 'A3', 'A4', 'A5'],
        orderedB: ['B1', 'B2', 'B3'],
        bannedAbilityIds: [
            'medic', 'berserker', 'assassin', 'tank',
            'capitalist', 'balance', 'glass_cannon', 'utility_specialist',
        ],
        timeoutAt: 10,
    });
    updateAbilityChoice(state, 'B1', 'istaru');
    confirmAbilityChoice(state, 'B1');

    let result = finishCurrentAbilityBatch(state, () => 0);
    while (result.ok && state.currentBatchIndex < state.batches.length) {
        result = finishCurrentAbilityBatch(state, () => 0);
    }

    assert.equal(result.code, 'NO_LEGAL_ABILITY');
});

test('0 Ban 可直接确认并结算为空结果', () => {
    const state = createAbilityBanState({
        orderedA: ['A1'], orderedB: ['B1'], banCountPerTeam: 0, timeoutAt: 10,
    });

    assert.deepEqual(updateAbilityBanSelection(state, 'A1', []), { ok: true });
    assert.deepEqual(confirmAbilityBan(state, 'A1'), { ok: true });
    assert.deepEqual(confirmAbilityBan(state, 'B1'), { ok: true });
    assert.deepEqual(resolveAbilityBans(state, () => 0), {
        teamBans: { A: [], B: [] },
        bannedAbilityIds: [],
        votes: { A: {}, B: {} },
    });
});

test('每人最多选择 N 个不同职业且确认后不能修改', () => {
    const state = createAbilityBanState({
        orderedA: ['A1'], orderedB: ['B1'], banCountPerTeam: 2, timeoutAt: 10,
    });

    assert.deepEqual(updateAbilityBanSelection(state, 'A1', ['medic', 'tank']), { ok: true });
    assert.equal(updateAbilityBanSelection(state, 'A1', ['medic', 'tank', 'witch']).code, 'BAN_LIMIT_EXCEEDED');
    assert.equal(updateAbilityBanSelection(state, 'A1', ['medic', 'medic']).code, 'DUPLICATE_ABILITY');
    assert.deepEqual(confirmAbilityBan(state, 'A1'), { ok: true });
    const locked = updateAbilityBanSelection(state, 'A1', ['witch']);
    assert.equal(locked.code, 'BAN_ALREADY_CONFIRMED');
    assert.match(locked.message ?? '', /确认/);
    assert.deepEqual(state.selections.A1, ['medic', 'tank']);
});

test('Ban 结算只统计已确认票，候选不足时不补齐', () => {
    const state = createAbilityBanState({
        orderedA: ['A1', 'A2'], orderedB: ['B1'], banCountPerTeam: 2, timeoutAt: 10,
    });
    updateAbilityBanSelection(state, 'A1', ['medic']);
    confirmAbilityBan(state, 'A1');
    updateAbilityBanSelection(state, 'A2', ['tank', 'witch']);
    updateAbilityBanSelection(state, 'B1', []);
    confirmAbilityBan(state, 'B1');

    const resolution = resolveAbilityBans(state, () => 0);

    assert.deepEqual(resolution.teamBans, { A: ['medic'], B: [] });
    assert.deepEqual(resolution.votes, { A: { medic: 1 }, B: {} });
    assert.deepEqual(resolution.bannedAbilityIds, ['medic']);
});

test('末位并列使用注入随机，双方撞 Ban 后不补 Ban', () => {
    const state = createAbilityBanState({
        orderedA: ['A1', 'A2'], orderedB: ['B1'], banCountPerTeam: 1, timeoutAt: 10,
    });
    updateAbilityBanSelection(state, 'A1', ['medic']);
    updateAbilityBanSelection(state, 'A2', ['tank']);
    updateAbilityBanSelection(state, 'B1', ['tank']);
    confirmAbilityBan(state, 'A1');
    confirmAbilityBan(state, 'A2');
    confirmAbilityBan(state, 'B1');

    const resolution = resolveAbilityBans(state, () => 0.999999);

    assert.deepEqual(resolution.teamBans, { A: ['tank'], B: ['tank'] });
    assert.deepEqual(resolution.bannedAbilityIds, ['tank']);
});

test('玩家输入错误返回稳定 code 和中文 message 而不抛异常', () => {
    const state = createAbilityBanState({
        orderedA: ['A1'], orderedB: ['B1'], banCountPerTeam: 1, timeoutAt: 10,
    });

    assert.doesNotThrow(() => updateAbilityBanSelection(state, '陌生玩家', ['medic']));
    const unknownPlayer = updateAbilityBanSelection(state, '陌生玩家', ['medic']);
    assert.equal(unknownPlayer.code, 'UNKNOWN_PLAYER');
    assert.match(unknownPlayer.message ?? '', /玩家/);
    const invalidAbility = updateAbilityBanSelection(state, 'A1', ['not-an-ability' as never]);
    assert.equal(invalidAbility.code, 'INVALID_ABILITY');
    assert.match(invalidAbility.message ?? '', /职业/);
});

test('选角状态可序列化并保存当前批次 timeoutAt', () => {
    const state = createAbilityDraftState({
        firstTeam: 'A',
        orderedA: ['A1'],
        orderedB: ['B1'],
        bannedAbilityIds: ['witch'],
        timeoutAt: 987654,
    });

    assert.equal(state.timeoutAt, 987654);
    assert.deepEqual(state.batches, [
        { team: 'A', playerIds: ['A1'] },
        { team: 'B', playerIds: ['B1'] },
    ]);
    assert.doesNotThrow(() => JSON.stringify(state));
    assert.equal('timer' in state, false);
});

test('只有当前批次玩家可选择，且被 Ban 或不存在的职业会返回稳定错误', () => {
    const state = createAbilityDraftState({
        firstTeam: 'A', orderedA: ['A1'], orderedB: ['B1'], bannedAbilityIds: ['witch'], timeoutAt: 10,
    });

    assert.equal(updateAbilityChoice(state, 'B1', 'medic').code, 'DRAFT_NOT_ACTIVE_PLAYER');
    assert.equal(updateAbilityChoice(state, 'A1', 'witch').code, 'ABILITY_BANNED');
    assert.equal(updateAbilityChoice(state, 'A1', 'bad-id' as never).code, 'INVALID_ABILITY');
    assert.doesNotThrow(() => updateAbilityChoice(state, '陌生玩家', 'medic'));
    assert.match(updateAbilityChoice(state, '陌生玩家', 'medic').message ?? '', /批次|玩家/);
});

test('同批同队冲突由先确认者成功，失败者可继续改选', () => {
    const state = createAbilityDraftState({
        firstTeam: 'A', orderedA: ['A1'], orderedB: ['B1', 'B2'], bannedAbilityIds: [], timeoutAt: 10,
    });
    updateAbilityChoice(state, 'A1', 'medic');
    confirmAbilityChoice(state, 'A1');
    finishCurrentAbilityBatch(state, () => 0);

    assert.deepEqual(updateAbilityChoice(state, 'B1', 'tank'), { ok: true });
    assert.deepEqual(updateAbilityChoice(state, 'B2', 'tank'), { ok: true });
    assert.deepEqual(confirmAbilityChoice(state, 'B1'), { ok: true });
    const conflict = confirmAbilityChoice(state, 'B2');
    assert.equal(conflict.code, 'ABILITY_TAKEN_BY_TEAM');
    assert.match(conflict.message ?? '', /同队/);
    assert.deepEqual(updateAbilityChoice(state, 'B2', 'witch'), { ok: true });
    assert.deepEqual(confirmAbilityChoice(state, 'B2'), { ok: true });
});

test('敌方可重复普通职业，但伊斯塔露全场唯一', () => {
    const normal = createAbilityDraftState({
        firstTeam: 'A', orderedA: ['A1'], orderedB: ['B1'], bannedAbilityIds: [], timeoutAt: 10,
    });
    updateAbilityChoice(normal, 'A1', 'medic');
    confirmAbilityChoice(normal, 'A1');
    finishCurrentAbilityBatch(normal, () => 0);
    assert.deepEqual(updateAbilityChoice(normal, 'B1', 'medic'), { ok: true });
    assert.deepEqual(confirmAbilityChoice(normal, 'B1'), { ok: true });

    const unique = createAbilityDraftState({
        firstTeam: 'A', orderedA: ['A1'], orderedB: ['B1'], bannedAbilityIds: [], timeoutAt: 10,
    });
    updateAbilityChoice(unique, 'A1', 'istaru');
    confirmAbilityChoice(unique, 'A1');
    finishCurrentAbilityBatch(unique, () => 0);
    assert.equal(updateAbilityChoice(unique, 'B1', 'istaru').code, 'GLOBAL_ABILITY_TAKEN');
});

test('确认后不能修改，未选择职业时不能确认', () => {
    const state = createAbilityDraftState({
        firstTeam: 'A', orderedA: ['A1'], orderedB: [], bannedAbilityIds: [], timeoutAt: 10,
    });

    assert.equal(confirmAbilityChoice(state, 'A1').code, 'ABILITY_NOT_SELECTED');
    updateAbilityChoice(state, 'A1', 'medic');
    confirmAbilityChoice(state, 'A1');
    assert.equal(updateAbilityChoice(state, 'A1', 'tank').code, 'ABILITY_ALREADY_CONFIRMED');
    assert.equal(confirmAbilityChoice(state, 'A1').code, 'ABILITY_ALREADY_CONFIRMED');
});

test('结束批次仅随机补未确认玩家并保留已确认选择', () => {
    const state = createAbilityDraftState({
        firstTeam: 'A', orderedA: ['A1'], orderedB: ['B1', 'B2'], bannedAbilityIds: [], timeoutAt: 10,
    });
    updateAbilityChoice(state, 'A1', 'medic');
    confirmAbilityChoice(state, 'A1');
    let randomCalls = 0;
    finishCurrentAbilityBatch(state, () => { randomCalls += 1; return 0.5; });
    assert.equal(randomCalls, 0);

    updateAbilityChoice(state, 'B1', 'tank');
    confirmAbilityChoice(state, 'B1');
    updateAbilityChoice(state, 'B2', 'witch');
    finishCurrentAbilityBatch(state, () => { randomCalls += 1; return 0; });

    assert.equal(randomCalls, 1);
    assert.deepEqual(
        state.assignments.map(({ playerId, abilityId }) => ({ playerId, abilityId })),
        [
            { playerId: 'A1', abilityId: 'medic' },
            { playerId: 'B1', abilityId: 'tank' },
            { playerId: 'B2', abilityId: 'medic' },
        ],
    );
    assert.equal(state.currentBatchIndex, state.batches.length);
});

test('超时随机池排除已 Ban、同队已占和敌方已占的伊斯塔露', () => {
    const bannedAbilityIds: AbilityId[] = ['medic'];
    const state = createAbilityDraftState({
        firstTeam: 'A', orderedA: ['A1'], orderedB: ['B1', 'B2'], bannedAbilityIds, timeoutAt: 10,
    });
    updateAbilityChoice(state, 'A1', 'istaru');
    confirmAbilityChoice(state, 'A1');
    finishCurrentAbilityBatch(state, () => 0);

    assert.deepEqual(finishCurrentAbilityBatch(state, () => 0), { ok: true });
    assert.deepEqual(
        state.assignments.filter(({ team }) => team === 'B').map(({ abilityId }) => abilityId),
        ['berserker', 'assassin'],
    );
});
