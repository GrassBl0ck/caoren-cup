import assert from 'node:assert/strict';
import { afterEach, test } from 'node:test';
import { getAbilityCatalog } from './ability-catalog';
import { confirmAbilityBan, confirmAbilityChoice, updateAbilityChoice } from './ability-draft-service';
import * as flow from './game-flow-manager';
import { clearAllFlowTimers, getAbilityDraftTimer } from './game-timers';
import { createInitialSession, getSession, setSession } from './session-manager';
import { canTransition } from './state-machine';
import { AbilityId, GamePhase, GameSession, RosterTeam } from './types';

const abilityBanPhase = 'AbilityBan' as GamePhase;
const abilityDraftPhase = 'AbilityDraft' as GamePhase;

const createSidePickSession = (options: {
    abilityModeEnabled: boolean;
    abilityBanCountPerTeam?: number;
    matchMode?: 'competitive' | 'duel';
    sidePickTeam?: RosterTeam;
}): GameSession => {
    const session = createInitialSession();
    session.phase = GamePhase.SidePick;
    session.matchOptions.matchMode = options.matchMode ?? 'competitive';
    session.matchOptions.abilityModeEnabled = options.abilityModeEnabled;
    session.matchOptions.abilityBanCountPerTeam = options.abilityBanCountPerTeam ?? 1;
    session.matchOptions.abilityBanSeconds = 45;
    session.matchOptions.abilityDraftBatchSeconds = 30;
    session.sidePickTeam = options.sidePickTeam ?? 'A';
    session.selectedSide = 'CT';
    session.captains = { A: 'a1', B: 'b1' };
    session.teams.A.players = ['a2', 'a1', 'a3', 'a2'];
    session.teams.B.players = ['b2', 'b1'];
    for (const [playerId, rosterTeam, isOnline] of [
        ['a1', 'A', true],
        ['a2', 'A', true],
        ['a3', 'A', false],
        ['b1', 'B', true],
        ['b2', 'B', true],
    ] as const) {
        session.players[playerId] = {
            playerId,
            name: playerId,
            role: 'Player',
            rosterTeam,
            isReady: false,
            isOnline,
        };
        session.playerOrder.push(playerId);
    }
    setSession(session);
    return session;
};

const advancePastSidePick = () => {
    flow.advancePhase(GamePhase.SidePick, GamePhase.PreGameSetup);
    return getSession();
};

afterEach(() => {
    clearAllFlowTimers();
    flow.injectNotify(() => {});
    flow.injectFlowBroadcast(() => {});
});

test('异能模式关闭时保持 SidePick → PreGameSetup 旧路径', () => {
    createSidePickSession({ abilityModeEnabled: false });

    const session = advancePastSidePick();

    assert.equal(session.phase, GamePhase.PreGameSetup);
    assert.equal(session.abilityBanState, undefined);
    assert.equal(session.abilityDraftState, undefined);
});

test('单挑模式固定跳过异能 BP', () => {
    createSidePickSession({ abilityModeEnabled: true, matchMode: 'duel' });

    assert.equal(advancePastSidePick().phase, GamePhase.PreGameSetup);
});

test('异能模式 Ban=0 时从 SidePick 直接进入 AbilityDraft', () => {
    const startedAt = Date.now();
    createSidePickSession({ abilityModeEnabled: true, abilityBanCountPerTeam: 0 });

    const session = advancePastSidePick();

    assert.equal(session.phase, abilityDraftPhase);
    assert.ok(session.abilityDraftState);
    assert.equal(session.timerPhase, abilityDraftPhase);
    assert.equal(session.timerEndAt, session.abilityDraftState.timeoutAt);
    assert.ok(session.abilityDraftState.timeoutAt >= startedAt + 29_000);
});

test('异能模式 Ban>0 时从 SidePick 依次进入 AbilityBan 和 AbilityDraft', () => {
    createSidePickSession({ abilityModeEnabled: true, abilityBanCountPerTeam: 1 });

    const session = advancePastSidePick();

    assert.equal(session.phase, abilityBanPhase);
    assert.ok(session.abilityBanState);
    assert.equal(session.abilityBanState.banCountPerTeam, 1);
    assert.equal(session.timerEndAt, session.abilityBanState.timeoutAt);
    assert.equal(session.timerPhase, abilityBanPhase);
});

test('SidePick 的显式目标不能绕过当前异能模式和 Ban 配置', () => {
    const route = (
        options: Parameters<typeof createSidePickSession>[0],
        requestedTo: GamePhase,
    ): GamePhase => {
        clearAllFlowTimers();
        createSidePickSession(options);
        flow.advancePhase(GamePhase.SidePick, requestedTo);
        return getSession().phase;
    };

    assert.equal(
        route({ abilityModeEnabled: true, abilityBanCountPerTeam: 1 }, abilityDraftPhase),
        abilityBanPhase,
    );
    assert.equal(
        route({ abilityModeEnabled: false }, abilityBanPhase),
        GamePhase.PreGameSetup,
    );
    assert.equal(
        route({ abilityModeEnabled: true, matchMode: 'duel' }, abilityDraftPhase),
        GamePhase.PreGameSetup,
    );
    assert.equal(
        route({ abilityModeEnabled: true, abilityBanCountPerTeam: 0 }, abilityBanPhase),
        abilityDraftPhase,
    );
});

test('没有选边权的一方首选职业，队长第一且其余保持原选人顺序并去重', () => {
    createSidePickSession({ abilityModeEnabled: true, abilityBanCountPerTeam: 0, sidePickTeam: 'B' });

    let session = advancePastSidePick();
    assert.deepEqual(session.abilityDraftState?.batches[0], { team: 'A', playerIds: ['a1'] });
    assert.deepEqual(
        session.abilityDraftState?.batches.flatMap((batch) => batch.playerIds),
        ['a1', 'b1', 'b2', 'a2', 'a3'],
    );

    createSidePickSession({ abilityModeEnabled: true, abilityBanCountPerTeam: 0, sidePickTeam: 'A' });
    session = advancePastSidePick();
    assert.equal(session.abilityDraftState?.batches[0].team, 'B');
});

test('全部在线参赛者确认 Ban 后提前结束，离线玩家不阻塞', () => {
    const finishAbilityBanIfReady = (flow as any).finishAbilityBanIfReady as (() => boolean) | undefined;
    assert.equal(typeof finishAbilityBanIfReady, 'function');
    createSidePickSession({ abilityModeEnabled: true, abilityBanCountPerTeam: 1 });
    const state = advancePastSidePick().abilityBanState!;

    for (const playerId of ['a1', 'a2', 'b1']) confirmAbilityBan(state, playerId);
    assert.equal(finishAbilityBanIfReady!(), false);

    confirmAbilityBan(state, 'b2');
    assert.equal(finishAbilityBanIfReady!(), true);
    assert.equal(getSession().phase, abilityDraftPhase);
});

test('最后一批职业确认后完成 BP 并进入 PreGameSetup', () => {
    const finishAbilityDraftBatch = (flow as any).finishAbilityDraftBatch as ((reason: 'manual') => boolean) | undefined;
    assert.equal(typeof finishAbilityDraftBatch, 'function');
    const session = createSidePickSession({ abilityModeEnabled: true, abilityBanCountPerTeam: 0, sidePickTeam: 'A' });
    session.teams.A.players = ['a1'];
    session.teams.B.players = ['b1'];
    delete session.players.a2;
    delete session.players.a3;
    delete session.players.b2;
    advancePastSidePick();

    const choices: AbilityId[] = ['medic', 'berserker'];
    for (const abilityId of choices) {
        const current = getSession().abilityDraftState!;
        const playerId = current.batches[current.currentBatchIndex].playerIds[0];
        assert.equal(updateAbilityChoice(current, playerId, abilityId).ok, true);
        assert.equal(confirmAbilityChoice(current, playerId).ok, true);
        assert.equal(finishAbilityDraftBatch!('manual'), true);
    }

    assert.equal(getSession().phase, GamePhase.PreGameSetup);
    assert.deepEqual(getSession().abilityAssignments?.map((item) => item.playerId), ['b1', 'a1']);
});

test('timeoutAt 过期轮询可结算 Ban，未过期时不推进', () => {
    const pollAbilityFlowTimeouts = (flow as any).pollAbilityFlowTimeouts as ((now: number) => boolean) | undefined;
    assert.equal(typeof pollAbilityFlowTimeouts, 'function');
    createSidePickSession({ abilityModeEnabled: true, abilityBanCountPerTeam: 1 });
    const state = advancePastSidePick().abilityBanState!;

    assert.equal(pollAbilityFlowTimeouts!(state.timeoutAt - 1), false);
    assert.equal(getSession().phase, abilityBanPhase);
    assert.equal(pollAbilityFlowTimeouts!(state.timeoutAt), true);
    assert.equal(getSession().phase, abilityDraftPhase);
});

test('Draft 自动结算失败会停止同一截止时间的 timer 和轮询重试并公开错误', () => {
    const finishAbilityDraftBatch = (flow as any).finishAbilityDraftBatch as ((reason: 'timeout') => boolean);
    const pollAbilityFlowTimeouts = (flow as any).pollAbilityFlowTimeouts as ((now: number) => boolean);
    const notifications: string[] = [];
    let broadcastCount = 0;
    flow.injectNotify((message) => notifications.push(message));
    flow.injectFlowBroadcast(() => { broadcastCount += 1; });
    const session = createSidePickSession({
        abilityModeEnabled: true,
        abilityBanCountPerTeam: 0,
        sidePickTeam: 'B',
    });
    session.teams.A.players = ['a1'];
    session.teams.B.players = [];
    delete session.players.a2;
    delete session.players.a3;
    delete session.players.b1;
    delete session.players.b2;
    advancePastSidePick();
    const state = getSession().abilityDraftState!;
    state.bannedAbilityIds = getAbilityCatalog().map((ability) => ability.id);
    state.timeoutAt = Date.now() - 1;
    getSession().timerEndAt = state.timeoutAt;
    const broadcastsBeforeFailure = broadcastCount;

    assert.equal(finishAbilityDraftBatch('timeout'), false);
    assert.equal(getAbilityDraftTimer(), null);
    assert.equal(getSession().timerEndAt, null);
    assert.equal(getSession().timerPhase, null);
    assert.equal((state as any).failure?.code, 'NO_LEGAL_ABILITY');
    assert.match(notifications.at(-1) ?? '', /职业自动分配失败.*没有可供该玩家分配的合法职业/);
    assert.equal(broadcastCount, broadcastsBeforeFailure + 1);

    assert.equal(pollAbilityFlowTimeouts(Date.now() + 10_000), false);
    assert.equal(state.currentBatchIndex, 0);
    assert.equal(getAbilityDraftTimer(), null);
    assert.equal(broadcastCount, broadcastsBeforeFailure + 1);
});

test('管理员不能从未完成的 BP 阶段直接跳到 PreGameSetup', () => {
    createSidePickSession({ abilityModeEnabled: true, abilityBanCountPerTeam: 1 });
    advancePastSidePick();

    assert.equal(canTransition(abilityBanPhase, GamePhase.PreGameSetup), false);
    flow.advancePhase(abilityBanPhase, GamePhase.PreGameSetup);
    assert.equal(getSession().phase, abilityBanPhase);

    (flow as any).finishAbilityBan?.('admin');
    assert.equal(getSession().phase, abilityDraftPhase);
    assert.equal(canTransition(abilityDraftPhase, GamePhase.PreGameSetup), true);
    flow.advancePhase(abilityDraftPhase, GamePhase.PreGameSetup);
    assert.equal(getSession().phase, abilityDraftPhase);
});
