import assert from 'node:assert/strict';
import test from 'node:test';
import {
    confirmAbilityBan,
    confirmAbilityChoice,
    createAbilityBanState,
    createAbilityDraftState,
    updateAbilityBanSelection,
    updateAbilityChoice,
} from './ability-draft-service';
import {
    authorizeAbilitySocketAction,
    shouldFinishAbilityBanEarly,
    shouldFinishAbilityDraftBatchEarly,
} from './ability-socket-policy';
import { GamePhase, PlayerRole, RosterTeam } from './types';

const banState = () => createAbilityBanState({
    orderedA: ['a1', 'a2'],
    orderedB: ['b1'],
    banCountPerTeam: 1,
    timeoutAt: Date.now() + 45_000,
});

const draftState = () => createAbilityDraftState({
    firstTeam: 'A',
    orderedA: ['a1', 'a2'],
    orderedB: ['b1'],
    bannedAbilityIds: [],
    timeoutAt: Date.now() + 30_000,
});

const actor = (
    playerId = 'a1',
    role: PlayerRole = 'Player',
    rosterTeam: RosterTeam | undefined = 'A',
) => ({ playerId, role, rosterTeam });

test('未登录连接和已登录玩家冒用其他 playerId 均被拒绝', () => {
    const unauthenticated = authorizeAbilitySocketAction({
        event: 'ABILITY_BAN_CONFIRM',
        actor: null,
        phase: GamePhase.AbilityBan,
        banState: banState(),
        payload: { playerId: 'a1' },
    });
    assert.deepEqual(unauthenticated, {
        allowed: false,
        code: 'ACTOR_NOT_AUTHENTICATED',
        message: '当前连接尚未登录，无法执行异能 BP 操作。',
    });

    const impersonation = authorizeAbilitySocketAction({
        event: 'ABILITY_PICK_CONFIRM',
        actor: actor('a1'),
        phase: GamePhase.AbilityDraft,
        draftState: draftState(),
        payload: { playerId: 'a2' },
    });
    assert.equal(impersonation.allowed, false);
    assert.equal(impersonation.code, 'ACTOR_MISMATCH');
    assert.match(impersonation.message, /不能替其他玩家/);
});

test('管理员、观众和没有参赛队伍的账号不能参与异能 BP', () => {
    for (const currentActor of [
        { playerId: 'admin', role: 'Admin' as const },
        { playerId: 'spectator', role: 'Spectator' as const },
        { playerId: 'waiting', role: 'Player' as const },
    ]) {
        const result = authorizeAbilitySocketAction({
            event: 'ABILITY_BAN_CONFIRM',
            actor: currentActor,
            phase: GamePhase.AbilityBan,
            banState: banState(),
            payload: { playerId: currentActor.playerId },
        });
        assert.equal(result.allowed, false);
        assert.equal(result.code, 'ACTOR_NOT_PARTICIPANT');
        assert.match(result.message, /参赛玩家/);
    }
});

test('Ban 操作重新校验阶段、名单队伍和 payload 结构', () => {
    const wrongPhase = authorizeAbilitySocketAction({
        event: 'ABILITY_BAN_UPDATE',
        actor: actor(),
        phase: GamePhase.AbilityDraft,
        banState: banState(),
        payload: { playerId: 'a1', selectedAbilityIds: ['medic'] },
    });
    assert.equal(wrongPhase.code, 'ABILITY_BAN_PHASE_REQUIRED');

    const wrongTeam = authorizeAbilitySocketAction({
        event: 'ABILITY_BAN_UPDATE',
        actor: actor('a1', 'Player', 'B'),
        phase: GamePhase.AbilityBan,
        banState: banState(),
        payload: { playerId: 'a1', selectedAbilityIds: ['medic'] },
    });
    assert.equal(wrongTeam.code, 'ABILITY_BAN_TEAM_MISMATCH');

    const malformed = authorizeAbilitySocketAction({
        event: 'ABILITY_BAN_UPDATE',
        actor: actor(),
        phase: GamePhase.AbilityBan,
        banState: banState(),
        payload: { playerId: 'a1', selectedAbilityIds: 'medic' },
    });
    assert.equal(malformed.code, 'INVALID_ABILITY_PAYLOAD');
});

test('选角操作只允许当前批次本人，不能替队友确认且 AbilityId 必须是字符串', () => {
    const notCurrent = authorizeAbilitySocketAction({
        event: 'ABILITY_PICK_UPDATE',
        actor: actor('b1', 'Player', 'B'),
        phase: GamePhase.AbilityDraft,
        draftState: draftState(),
        payload: { playerId: 'b1', abilityId: 'medic' },
    });
    assert.equal(notCurrent.code, 'DRAFT_NOT_ACTIVE_PLAYER');

    const teammate = authorizeAbilitySocketAction({
        event: 'ABILITY_PICK_CONFIRM',
        actor: actor('a1'),
        phase: GamePhase.AbilityDraft,
        draftState: draftState(),
        payload: { playerId: 'a2' },
    });
    assert.equal(teammate.code, 'ACTOR_MISMATCH');

    const malformed = authorizeAbilitySocketAction({
        event: 'ABILITY_PICK_UPDATE',
        actor: actor(),
        phase: GamePhase.AbilityDraft,
        draftState: draftState(),
        payload: { playerId: 'a1', abilityId: 123 },
    });
    assert.equal(malformed.code, 'INVALID_ABILITY_PAYLOAD');
});

test('服务端规则拒绝超出 Ban 数量、非法职业 ID 和重复确认', () => {
    const bans = banState();
    assert.equal(updateAbilityBanSelection(bans, 'a1', ['medic', 'tank']).code, 'BAN_LIMIT_EXCEEDED');
    assert.equal(updateAbilityBanSelection(bans, 'a1', ['not-an-ability' as never]).code, 'INVALID_ABILITY');
    assert.equal(confirmAbilityBan(bans, 'a1').ok, true);
    assert.equal(confirmAbilityBan(bans, 'a1').code, 'BAN_ALREADY_CONFIRMED');

    const draft = draftState();
    assert.equal(updateAbilityChoice(draft, 'a1', 'not-an-ability' as never).code, 'INVALID_ABILITY');
    assert.equal(updateAbilityChoice(draft, 'a1', 'medic').ok, true);
    assert.equal(confirmAbilityChoice(draft, 'a1').ok, true);
    assert.equal(confirmAbilityChoice(draft, 'a1').code, 'ABILITY_ALREADY_CONFIRMED');
});

test('Ban 仅等待在线参赛者，选角必须等待当前批次每个席位确认', () => {
    const bans = banState();
    bans.confirmedPlayerIds = ['a1', 'a2'];
    assert.equal(shouldFinishAbilityBanEarly(bans, new Set(['a1', 'a2'])), true);
    assert.equal(shouldFinishAbilityBanEarly(bans, new Set(['a1', 'a2', 'b1'])), false);

    const draft = draftState();
    draft.batches[0] = { team: 'A', playerIds: ['a1', 'a2'] };
    draft.confirmedPlayerIds = ['a1'];
    assert.equal(shouldFinishAbilityDraftBatchEarly(draft), false);
    draft.confirmedPlayerIds.push('a2');
    assert.equal(shouldFinishAbilityDraftBatchEarly(draft), true);
});
