import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { afterEach, test } from 'node:test';
import { getAbilityCatalog } from './ability-catalog';
import { confirmAbilityBan, confirmAbilityChoice, updateAbilityChoice } from './ability-draft-service';
import * as flow from './game-flow-manager';
import { clearAllFlowTimers, getAbilityDraftTimer } from './game-timers';
import { createInitialSession, getSession, setSession } from './session-manager';
import { registerSocketHandlers } from './socket-handlers';
import { canTransition } from './state-machine';
import { AbilityId, GamePhase, GameSession, RosterTeam, WsEvents } from './types';

const abilityBanPhase = 'AbilityBan' as GamePhase;
const abilityDraftPhase = 'AbilityDraft' as GamePhase;

class FakeSocket {
    readonly data: Record<string, unknown> = {};
    readonly handshake = { address: '127.0.0.1', secure: false, headers: {} };
    readonly emitted: Array<{ event: string; payload: unknown }> = [];
    private readonly handlers = new Map<string, (payload?: unknown) => unknown>();

    constructor(readonly id: string) {}

    on(event: string, handler: (payload?: unknown) => unknown) { this.handlers.set(event, handler); }
    emit(event: string, payload: unknown) { this.emitted.push({ event, payload }); }
    join(_room: string) {}
    trigger(event: string, payload?: unknown) {
        const handler = this.handlers.get(event);
        if (!handler) throw new Error(`missing handler: ${event}`);
        return handler(payload);
    }
}

class FakeIo {
    readonly sockets = { sockets: new Map<string, FakeSocket>() };
    private connectionHandler?: (socket: FakeSocket) => void;

    on(event: string, handler: (socket: FakeSocket) => void) {
        if (event === 'connection') this.connectionHandler = handler;
    }
    emit(_event: string, _payload: unknown) {}
    connect(socket: FakeSocket) {
        this.sockets.sockets.set(socket.id, socket);
        this.connectionHandler?.(socket);
    }
}

const createAbilityConfigSocketContext = (options: {
    phase?: GamePhase;
    actorRole?: 'Admin' | 'Player';
    participantCount?: number;
} = {}) => {
    const session = createInitialSession();
    session.phase = options.phase ?? GamePhase.Lobby;
    session.players.admin = {
        playerId: 'admin',
        name: '管理员',
        role: 'Admin',
        isReady: false,
        isOnline: true,
    };
    session.playerOrder.push('admin');
    const participantCount = options.participantCount ?? 5;
    for (let index = 1; index <= participantCount; index += 1) {
        const playerId = `p${index}`;
        session.players[playerId] = {
            playerId,
            name: `玩家${index}`,
            role: 'Player',
            isReady: false,
            isOnline: true,
        };
        session.playerOrder.push(playerId);
    }
    setSession(session);

    let broadcastCount = 0;
    const io = new FakeIo();
    registerSocketHandlers(io as never, {
        broadcastState() { broadcastCount += 1; },
        notifyMessage() {},
    });
    const actorId = options.actorRole === 'Player' ? 'p1' : 'admin';
    const socket = new FakeSocket(`${actorId}-socket`);
    io.connect(socket);
    socket.data.playerId = actorId;
    return { session, socket, actorId, getBroadcastCount: () => broadcastCount };
};

const validAbilityConfigPayload = {
    abilityModeEnabled: true,
    abilityBanCountPerTeam: 4,
    abilityBanSeconds: 45,
    abilityDraftBatchSeconds: 30,
};

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

const createCompletedAbilityPreGameSession = () => {
    const session = createInitialSession();
    session.phase = GamePhase.PreGameSetup;
    session.matchOptions.matchMode = 'competitive';
    session.matchOptions.abilityModeEnabled = true;
    session.matchOptions.undercoverModeEnabled = false;
    session.players = {
        admin: { playerId: 'admin', name: '管理员', role: 'Admin', isReady: true, isOnline: true },
        a1: { playerId: 'a1', name: 'A1', role: 'Player', rosterTeam: 'A', isReady: true, isOnline: true },
        b1: { playerId: 'b1', name: 'B1', role: 'Player', rosterTeam: 'B', isReady: true, isOnline: true },
    };
    session.playerOrder = ['admin', 'a1', 'b1'];
    session.teams.A.players = ['a1'];
    session.teams.B.players = ['b1'];
    session.abilityAssignments = [
        { playerId: 'a1', team: 'A', abilityId: 'medic' },
        { playerId: 'b1', team: 'B', abilityId: 'witch' },
    ];
    setSession(session);
    return session;
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

test('只有管理员可通过专用动作在 Lobby 更新四项异能配置，额外键不能覆盖比赛设置', async () => {
    const { session, socket, actorId, getBroadcastCount } = createAbilityConfigSocketContext();
    session.matchOptions.undercoverModeEnabled = true;
    session.matchOptions.matchMode = 'competitive';

    await socket.trigger(WsEvents.ADMIN_ACTION, {
        playerId: actorId,
        action: 'SET_ABILITY_MODE_CONFIG',
        payload: {
            ...validAbilityConfigPayload,
            undercoverModeEnabled: false,
            matchMode: 'duel',
        },
    });

    assert.equal(session.matchOptions.abilityModeEnabled, true);
    assert.equal(session.matchOptions.abilityBanCountPerTeam, 4);
    assert.equal(session.matchOptions.abilityBanSeconds, 45);
    assert.equal(session.matchOptions.abilityDraftBatchSeconds, 30);
    assert.equal(session.matchOptions.undercoverModeEnabled, true);
    assert.equal(session.matchOptions.matchMode, 'competitive');
    assert.equal(getBroadcastCount(), 1);
});

test('通用本局设置入口不能绕过专用动作覆盖异能配置', () => {
    const session = createInitialSession();
    session.matchOptions.abilityModeEnabled = true;
    session.matchOptions.abilityBanCountPerTeam = 3;
    session.matchOptions.abilityBanSeconds = 60;
    session.matchOptions.abilityDraftBatchSeconds = 90;
    setSession(session);

    (flow as any).applyMatchOptions({
        matchMode: 'competitive',
        undercoverModeEnabled: false,
        abilityModeEnabled: false,
        abilityBanCountPerTeam: 0,
        abilityBanSeconds: 1,
        abilityDraftBatchSeconds: 1,
    });

    assert.equal(session.matchOptions.abilityModeEnabled, true);
    assert.equal(session.matchOptions.abilityBanCountPerTeam, 3);
    assert.equal(session.matchOptions.abilityBanSeconds, 60);
    assert.equal(session.matchOptions.abilityDraftBatchSeconds, 90);
    assert.equal(session.matchOptions.undercoverModeEnabled, false);
});

test('普通玩家不能修改异能配置', async () => {
    const { session, socket, actorId } = createAbilityConfigSocketContext({ actorRole: 'Player' });

    await socket.trigger(WsEvents.ADMIN_ACTION, {
        playerId: actorId,
        action: 'SET_ABILITY_MODE_CONFIG',
        payload: validAbilityConfigPayload,
    });

    assert.equal(session.matchOptions.abilityModeEnabled, false);
    assert.match(String((socket.emitted.at(-1)?.payload as any)?.message || ''), /只有管理员/);
});

test('离开 Lobby 后异能配置锁定，包括已进入 AbilityBan 的对局', async () => {
    for (const phase of [GamePhase.CaptainSelection, abilityBanPhase]) {
        const { session, socket, actorId } = createAbilityConfigSocketContext({ phase });

        await socket.trigger(WsEvents.ADMIN_ACTION, {
            playerId: actorId,
            action: 'SET_ABILITY_MODE_CONFIG',
            payload: validAbilityConfigPayload,
        });

        assert.equal(session.matchOptions.abilityModeEnabled, false);
        assert.match(String((socket.emitted.at(-1)?.payload as any)?.message || ''), /只能在大厅阶段/);
    }
});

test('Ban 数必须是安全整数且不超过按当前参赛人数计算的保守上限', async () => {
    for (const invalidBanCount of [-1, 1.5, Number.MAX_SAFE_INTEGER + 1, 5]) {
        const { session, socket, actorId } = createAbilityConfigSocketContext({ participantCount: 5 });

        await socket.trigger(WsEvents.ADMIN_ACTION, {
            playerId: actorId,
            action: 'SET_ABILITY_MODE_CONFIG',
            payload: { ...validAbilityConfigPayload, abilityBanCountPerTeam: invalidBanCount },
        });

        assert.equal(session.matchOptions.abilityModeEnabled, false, `invalid Ban count: ${invalidBanCount}`);
        assert.match(String((socket.emitted.at(-1)?.payload as any)?.message || ''), /当前最多可 Ban 4 个|安全整数/);
    }
});

test('保存后玩家加入导致上限下降时，实际 ADVANCE_PHASE 入口阻止离开 Lobby 且仍可修改配置', async () => {
    const notifications: string[] = [];
    const { session, socket, actorId } = createAbilityConfigSocketContext({ participantCount: 2 });
    flow.injectNotify((message) => notifications.push(message));

    await socket.trigger(WsEvents.ADMIN_ACTION, {
        playerId: actorId,
        action: 'SET_ABILITY_MODE_CONFIG',
        payload: { ...validAbilityConfigPayload, abilityBanCountPerTeam: 5 },
    });
    assert.equal(session.matchOptions.abilityBanCountPerTeam, 5);

    for (let index = 3; index <= 5; index += 1) {
        const playerId = `p${index}`;
        session.players[playerId] = {
            playerId,
            name: `玩家${index}`,
            role: 'Player',
            isReady: false,
            isOnline: true,
        };
        session.playerOrder.push(playerId);
    }

    await socket.trigger(WsEvents.ADMIN_ACTION, {
        playerId: actorId,
        action: 'ADVANCE_PHASE',
    });

    assert.equal(session.phase, GamePhase.Lobby);
    assert.equal(session.matchOptions.abilityBanCountPerTeam, 5);
    assert.match(notifications.at(-1) ?? '', /当前最多可 Ban 4 个/);

    await socket.trigger(WsEvents.ADMIN_ACTION, {
        playerId: actorId,
        action: 'SET_ABILITY_MODE_CONFIG',
        payload: { ...validAbilityConfigPayload, abilityBanCountPerTeam: 4 },
    });
    assert.equal(session.phase, GamePhase.Lobby);
    assert.equal(session.matchOptions.abilityBanCountPerTeam, 4);

    await socket.trigger(WsEvents.ADMIN_ACTION, {
        playerId: actorId,
        action: 'ADVANCE_PHASE',
    });
    assert.equal(session.phase, GamePhase.CaptainSelection);
});

test('Ban 秒数和选角批次秒数必须是正的安全整数，不虚构最大秒数', async () => {
    for (const [field, invalidValue] of [
        ['abilityBanSeconds', 0],
        ['abilityBanSeconds', 1.5],
        ['abilityBanSeconds', Number.MAX_SAFE_INTEGER + 1],
        ['abilityDraftBatchSeconds', 0],
        ['abilityDraftBatchSeconds', 1.5],
        ['abilityDraftBatchSeconds', Number.MAX_SAFE_INTEGER + 1],
    ] as const) {
        const { session, socket, actorId } = createAbilityConfigSocketContext();

        await socket.trigger(WsEvents.ADMIN_ACTION, {
            playerId: actorId,
            action: 'SET_ABILITY_MODE_CONFIG',
            payload: { ...validAbilityConfigPayload, [field]: invalidValue },
        });

        assert.equal(session.matchOptions.abilityModeEnabled, false, `${field}: ${invalidValue}`);
        assert.match(String((socket.emitted.at(-1)?.payload as any)?.message || ''), /正整数/);
    }

    const { session, socket, actorId } = createAbilityConfigSocketContext();
    await socket.trigger(WsEvents.ADMIN_ACTION, {
        playerId: actorId,
        action: 'SET_ABILITY_MODE_CONFIG',
        payload: {
            ...validAbilityConfigPayload,
            abilityBanSeconds: Number.MAX_SAFE_INTEGER,
            abilityDraftBatchSeconds: Number.MAX_SAFE_INTEGER,
        },
    });
    assert.equal(session.matchOptions.abilityBanSeconds, Number.MAX_SAFE_INTEGER);
    assert.equal(session.matchOptions.abilityDraftBatchSeconds, Number.MAX_SAFE_INTEGER);
});

test('SidePick 进入 BP 前按实际 A/B 有序名单重算上限，超限时保持阶段并提示管理员', () => {
    const notifications: string[] = [];
    const session = createSidePickSession({ abilityModeEnabled: true, abilityBanCountPerTeam: 4 });
    session.players.a4 = {
        playerId: 'a4',
        name: 'a4',
        role: 'Player',
        rosterTeam: 'B',
        isReady: false,
        isOnline: true,
    };
    session.playerOrder.push('a4');
    for (const playerId of ['a1', 'a2', 'a3', 'b1', 'b2']) session.players[playerId].rosterTeam = 'A';
    session.teams.A.players = ['a1', 'a2', 'a3', 'b1', 'b2'];
    session.teams.B.players = ['a4'];
    session.captains = { A: 'a1', B: 'a4' };
    flow.injectNotify((message) => notifications.push(message));

    const result = advancePastSidePick();

    assert.equal(result.phase, GamePhase.SidePick);
    assert.equal(result.abilityBanState, undefined);
    assert.equal(result.abilityDraftState, undefined);
    assert.match(notifications.at(-1) ?? '', /当前最多可 Ban 3 个/);
});

test('finishSideVote 门禁失败不消费选边状态，修正配置后可用原投票重试进入 AbilityBan', () => {
    const notifications: string[] = [];
    const session = createSidePickSession({ abilityModeEnabled: true, abilityBanCountPerTeam: 4 });
    session.players.a4 = {
        playerId: 'a4',
        name: 'a4',
        role: 'Player',
        rosterTeam: 'B',
        isReady: false,
        isOnline: true,
    };
    session.playerOrder.push('a4');
    for (const playerId of ['a1', 'a2', 'a3', 'b1', 'b2']) session.players[playerId].rosterTeam = 'A';
    session.teams.A.players = ['a1', 'a2', 'a3', 'b1', 'b2'];
    session.teams.B.players = ['a4'];
    session.captains = { A: 'a1', B: 'a4' };
    flow.injectNotify((message) => notifications.push(message));
    flow.startSideVote('A');
    session.sideVote!.votes.a1 = 'T';
    const originalSideVote = session.sideVote;
    const originalTimerEndAt = session.timerEndAt;

    flow.finishSideVote('manual');

    assert.equal(session.phase, GamePhase.SidePick);
    assert.equal(session.sideVote, originalSideVote);
    assert.equal(session.selectedSide, null);
    assert.equal(session.timerEndAt, originalTimerEndAt);
    assert.equal(session.timerPhase, GamePhase.SidePick);
    assert.match(notifications.at(-1) ?? '', /当前最多可 Ban 3 个/);

    session.matchOptions.abilityBanCountPerTeam = 3;
    flow.finishSideVote('manual');

    assert.equal(session.phase, abilityBanPhase);
    assert.equal(session.sideVote, undefined);
    assert.equal(session.selectedSide, 'T');
    assert.equal(session.abilityBanState?.banCountPerTeam, 3);
});

test('Lobby 管理区显示异能配置与最新安全上限，保存按钮使用专用 ADMIN_ACTION', () => {
    const lobbySource = readFileSync('public/js/lobby-app.js', 'utf8');

    for (const token of [
        'ability-mode-config-panel',
        'match-option-ability-enabled',
        'match-option-ability-ban-count',
        'match-option-ability-ban-seconds',
        'match-option-ability-draft-seconds',
        'ability-ban-safe-limit',
    ]) {
        assert.ok(lobbySource.includes(token), `Lobby 缺少异能配置 UI 标记：${token}`);
    }
    assert.match(
        lobbySource,
        /function saveAbilityModeConfig\b[\s\S]{0,1800}ws\.emit\('ADMIN_ACTION',[\s\S]{0,300}action:\s*'SET_ABILITY_MODE_CONFIG'/,
    );
    assert.match(lobbySource, /function calculateLobbyAbilityBanSafeLimit\b/);
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

test('阶段 1 异能配置完成后 MatchZy round_start 不能把 PreGameSetup 提升为 LiveGame', () => {
    const notifications: string[] = [];
    flow.injectNotify((message) => notifications.push(message));
    createCompletedAbilityPreGameSession();

    assert.equal(flow.markStandardMatchLiveFromMatchZy(), false);
    assert.equal(getSession().phase, GamePhase.PreGameSetup);
    assert.match(notifications.at(-1) ?? '', /插件尚未同步.*不能正式开赛/);
    assert.doesNotMatch(notifications.at(-1) ?? '', /\.start/);

    const incomplete = createCompletedAbilityPreGameSession();
    incomplete.abilityAssignments = incomplete.abilityAssignments?.slice(0, 1);
    assert.equal(flow.markStandardMatchLiveFromMatchZy(), true);
    assert.equal(getSession().phase, GamePhase.LiveGame);
});

test('管理员 ADVANCE_PHASE 不能绕过阶段 1 异能正式开赛门禁', async () => {
    createCompletedAbilityPreGameSession();
    const io = new FakeIo();
    registerSocketHandlers(io as never, { broadcastState() {}, notifyMessage() {} });
    const socket = new FakeSocket('admin-socket');
    io.connect(socket);
    socket.data.playerId = 'admin';

    await socket.trigger(WsEvents.ADMIN_ACTION, { playerId: 'admin', action: 'ADVANCE_PHASE' });

    assert.equal(getSession().phase, GamePhase.PreGameSetup);
    const notification = socket.emitted.find((item) => item.event === WsEvents.NOTIFICATION);
    assert.match(String((notification?.payload as any)?.message || ''), /插件尚未同步.*不能正式开赛/);
    assert.doesNotMatch(String((notification?.payload as any)?.message || ''), /\.start/);
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
