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
    getOnlineAbilityBanPlayerIds,
    shouldFinishAbilityBanEarly,
    shouldFinishAbilityDraftBatchEarly,
} from './ability-socket-policy';
import { clearAllFlowTimers } from './game-timers';
import { createInitialSession, setSession } from './session-manager';
import { registerSocketHandlers } from './socket-handlers';
import { GamePhase, PlayerRole, RosterTeam } from './types';

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
    to(_room: string) { return { emit(_event: string, _payload: unknown) {} }; }
    connect(socket: FakeSocket) {
        this.sockets.sockets.set(socket.id, socket);
        this.connectionHandler?.(socket);
    }
}

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

test('Ban 在线名单排除缺失记录和明确离线玩家，兼容未设置 isOnline 的旧玩家', () => {
    const bans = banState();
    bans.orderedPlayers.B.push('missing-player');
    const onlinePlayerIds = getOnlineAbilityBanPlayerIds(bans, {
        a1: { isOnline: true },
        a2: {},
        b1: { isOnline: false },
    });

    assert.deepEqual([...onlinePlayerIds], ['a1', 'a2']);
});

test('未确认玩家断线后重新评估 Ban 并立即触发提前结算', (t) => {
    t.after(clearAllFlowTimers);
    const session = createInitialSession();
    session.phase = GamePhase.AbilityBan;
    session.matchOptions.abilityModeEnabled = true;
    session.players = {
        a1: { playerId: 'a1', name: 'A1', role: 'Player', rosterTeam: 'A', isOnline: true } as never,
        b1: { playerId: 'b1', name: 'B1', role: 'Player', rosterTeam: 'B', isOnline: true } as never,
    };
    session.playerOrder = ['a1', 'b1'];
    session.teams.A.players = ['a1'];
    session.teams.B.players = ['b1'];
    session.captains = { A: 'a1', B: 'b1' };
    session.abilityBanState = createAbilityBanState({
        orderedA: ['a1'],
        orderedB: ['b1'],
        banCountPerTeam: 1,
        timeoutAt: Date.now() + 45_000,
    });
    session.abilityBanState.confirmedPlayerIds = ['a1'];
    setSession(session);

    const io = new FakeIo();
    registerSocketHandlers(io as never, { broadcastState() {}, notifyMessage() {} });
    const socket = new FakeSocket('b1-socket');
    io.connect(socket);
    socket.data.playerId = 'b1';

    assert.equal(shouldFinishAbilityBanEarly(session.abilityBanState, new Set(['a1', 'b1'])), false);
    socket.trigger('disconnect');
    assert.equal(session.players.b1.isOnline, false);
    assert.equal(session.phase, GamePhase.AbilityDraft);
});

test('异能 BP 与完成后的 PreGameSetup 阶段拒绝管理员踢人并保留普通大厅旧行为', async (t) => {
    t.after(clearAllFlowTimers);
    const createKickContext = (phase: GamePhase, completeAssignments = false) => {
        const session = createInitialSession();
        session.phase = phase;
        session.matchOptions.abilityModeEnabled = true;
        session.matchOptions.matchMode = 'competitive';
        session.players = {
            admin: { playerId: 'admin', name: 'Admin', role: 'Admin', isReady: true },
            a1: { playerId: 'a1', name: 'A1', role: 'Player', rosterTeam: 'A', isReady: true },
        };
        session.playerOrder = ['admin', 'a1'];
        session.teams.A.players = ['a1'];
        session.abilityAssignments = completeAssignments
            ? [{ playerId: 'a1', team: 'A', abilityId: 'medic' }]
            : [];
        setSession(session);
        const io = new FakeIo();
        registerSocketHandlers(io as never, { broadcastState() {}, notifyMessage() {} });
        const socket = new FakeSocket(`admin-${phase}`);
        io.connect(socket);
        socket.data.playerId = 'admin';
        return { session, socket };
    };

    for (const [phase, completeAssignments] of [
        [GamePhase.AbilityBan, false],
        [GamePhase.AbilityDraft, false],
        [GamePhase.PreGameSetup, true],
    ] as const) {
        const { session, socket } = createKickContext(phase, completeAssignments);
        await socket.trigger('ADMIN_ACTION', { playerId: 'admin', action: 'KICK_PLAYER', payload: { playerId: 'a1' } });
        assert.ok(session.players.a1, `${phase} must retain the protected roster player`);
        const message = String((socket.emitted.find((item) => item.event === 'NOTIFICATION')?.payload as any)?.message || '');
        assert.match(message, /终止本局.*返回大厅/);
    }

    const lobby = createKickContext(GamePhase.Lobby);
    await lobby.socket.trigger('ADMIN_ACTION', { playerId: 'admin', action: 'KICK_PLAYER', payload: { playerId: 'a1' } });
    assert.equal(lobby.session.players.a1, undefined);

    const abilityBan = createKickContext(GamePhase.AbilityBan);
    abilityBan.session.players.spectator = {
        playerId: 'spectator', name: 'Spectator', role: 'Spectator', isReady: true,
    };
    abilityBan.session.playerOrder.push('spectator');
    await abilityBan.socket.trigger('ADMIN_ACTION', { playerId: 'admin', action: 'KICK_PLAYER', payload: { playerId: 'spectator' } });
    assert.equal(abilityBan.session.players.spectator, undefined, '非参赛观众保持原有可踢出路径');
});

test('SidePick 踢人请求在异步封禁前原子移除阵容，不会跨阶段留下幽灵席位', async (t) => {
    t.after(clearAllFlowTimers);
    const session = createInitialSession();
    session.phase = GamePhase.SidePick;
    session.matchOptions.abilityModeEnabled = true;
    session.matchOptions.matchMode = 'competitive';
    session.players = {
        admin: { playerId: 'admin', name: 'Admin', role: 'Admin', isReady: true },
        a1: { playerId: 'a1', name: 'A1', role: 'Player', rosterTeam: 'A', membershipId: 'membership-a1', isReady: true },
        b1: { playerId: 'b1', name: 'B1', role: 'Player', rosterTeam: 'B', isReady: true },
    };
    session.playerOrder = ['admin', 'a1', 'b1'];
    session.teams.A.players = ['a1'];
    session.teams.B.players = ['b1'];
    setSession(session);

    let releaseBlock!: () => void;
    let markBlockStarted!: () => void;
    const blockStarted = new Promise<void>((resolve) => { markBlockStarted = resolve; });
    const blockGate = new Promise<void>((resolve) => { releaseBlock = resolve; });
    const io = new FakeIo();
    registerSocketHandlers(io as never, {
        broadcastState() {},
        notifyMessage() {},
        blockMembership: async () => {
            markBlockStarted();
            await blockGate;
        },
    });
    const socket = new FakeSocket('admin-race');
    io.connect(socket);
    socket.data.playerId = 'admin';

    const kickPromise = socket.trigger('ADMIN_ACTION', {
        playerId: 'admin', action: 'KICK_PLAYER', payload: { playerId: 'a1' },
    });
    const injectedBlockStarted = await Promise.race([
        blockStarted.then(() => true),
        new Promise<boolean>((resolve) => setTimeout(() => resolve(false), 100)),
    ]);
    assert.equal(injectedBlockStarted, true, '测试注入的异步封禁入口必须被调用');

    assert.equal(session.players.a1, undefined, '第一次 await 前必须移除玩家');
    assert.deepEqual(session.teams.A.players, [], '第一次 await 前必须移除队伍席位');
    session.phase = GamePhase.AbilityBan;
    session.abilityBanState = createAbilityBanState({
        orderedA: [...session.teams.A.players],
        orderedB: [...session.teams.B.players],
        banCountPerTeam: 1,
        timeoutAt: Date.now() + 45_000,
    });
    releaseBlock();
    await kickPromise;

    assert.equal(session.abilityBanState.orderedPlayers.A.includes('a1'), false);
});
