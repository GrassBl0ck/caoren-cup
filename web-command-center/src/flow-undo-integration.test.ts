import assert from 'node:assert/strict';
import test from 'node:test';
import {
    clearFlowUndoHistory,
    exportFlowUndoState,
    getFlowUndoStatus,
    pushFlowUndoCheckpoint,
} from './flow-undo-manager';
import { clearAllFlowTimers } from './game-timers';
import { createInitialSession, getSession, setSession } from './session-manager';
import { registerSocketHandlers } from './socket-handlers';
import { GamePhase, Player, WsEvents } from './types';

class FakeSocket {
    readonly data: Record<string, any> = {};
    readonly handshake = { address: '127.0.0.1', secure: false, headers: {} as Record<string, string> };
    readonly emitted: Array<{ event: string; payload: any }> = [];
    private readonly handlers = new Map<string, (payload: any) => any>();
    constructor(readonly id: string) {}
    on(event: string, handler: (payload: any) => any) { this.handlers.set(event, handler); }
    emit(event: string, payload: any) { this.emitted.push({ event, payload }); }
    join() {}
    async trigger(event: string, payload: any) {
        const handler = this.handlers.get(event);
        if (!handler) throw new Error(`missing handler: ${event}`);
        await handler(payload);
    }
}

class FakeIo {
    readonly sockets = { sockets: new Map<string, FakeSocket>() };
    readonly broadcasts: Array<{ room?: string; event: string; payload: any }> = [];
    private connectionHandler?: (socket: FakeSocket) => void;
    on(event: string, handler: (socket: FakeSocket) => void) {
        if (event === 'connection') this.connectionHandler = handler;
    }
    connect(socket: FakeSocket) {
        this.sockets.sockets.set(socket.id, socket);
        this.connectionHandler?.(socket);
    }
    to(room: string) {
        return { emit: (event: string, payload: any) => this.broadcasts.push({ room, event, payload }) };
    }
    emit(event: string, payload: any) { this.broadcasts.push({ event, payload }); }
}

const player = (playerId: string, name: string, role: Player['role'] = 'Player'): Player => ({
    playerId, name, role, isReady: false, isOnline: true,
});

const undoPayload = (session: ReturnType<typeof createInitialSession>, actor: Player) => {
    const status = getFlowUndoStatus(session, actor);
    return {
        expectedPhase: session.phase,
        expectedHistoryDepth: status.historyDepth,
        expectedEntryId: status.latest?.id,
    };
};

test.afterEach(() => {
    clearAllFlowTimers();
    if (getSession().rollTimeout) clearTimeout(getSession().rollTimeout);
    getSession().rollTimeout = undefined;
    clearFlowUndoHistory();
});

test('admin can undo phase advance, forced draft assignment, and forced map ban in LIFO order', async () => {
    const session = createInitialSession();
    const admin = player('admin', 'Admin', 'Admin');
    const captainA = player('captain-a', 'Captain A');
    const captainB = player('captain-b', 'Captain B');
    const target = player('target', 'Target');
    for (const item of [admin, captainA, captainB, target]) {
        session.players[item.playerId] = item;
        session.playerOrder.push(item.playerId);
    }
    setSession(session);
    const io = new FakeIo();
    let persisted = 0;
    registerSocketHandlers(io as any, {
        broadcastState() {},
        notifyMessage() {},
        persistSessionNow() { persisted += 1; },
    });
    const socket = new FakeSocket('admin-socket');
    io.connect(socket);
    socket.data.playerId = admin.playerId;

    await socket.trigger(WsEvents.ADMIN_ACTION, { playerId: admin.playerId, action: 'ADVANCE_PHASE' });
    assert.equal(session.phase, GamePhase.CaptainSelection);
    assert.equal(exportFlowUndoState().entries.length, 1);
    await socket.trigger(WsEvents.ADMIN_ACTION, {
        playerId: admin.playerId, action: 'UNDO_FLOW_ACTION', payload: undoPayload(session, admin),
    });
    assert.equal(session.phase, GamePhase.Lobby);
    assert.deepEqual(session.captains, { A: null, B: null });

    session.phase = GamePhase.PlayerDraft;
    session.captains = { A: captainA.playerId, B: captainB.playerId };
    session.teams.A.players = [captainA.playerId];
    session.teams.B.players = [captainB.playerId];
    captainA.rosterTeam = 'A';
    captainB.rosterTeam = 'B';
    target.rosterTeam = undefined;
    session.draftOrder = ['A'];
    session.draftOriginalOrder = ['A'];
    session.draftIndex = 0;
    await socket.trigger(WsEvents.ADMIN_ACTION, {
        playerId: admin.playerId,
        action: 'ASSIGN_ROSTER_TEAM',
        payload: { playerId: target.playerId, team: 'A' },
    });
    assert.equal(target.rosterTeam, 'A');
    await socket.trigger(WsEvents.ADMIN_ACTION, {
        playerId: admin.playerId, action: 'UNDO_FLOW_ACTION', payload: undoPayload(session, admin),
    });
    assert.equal(session.phase, GamePhase.PlayerDraft);
    assert.equal(target.rosterTeam, undefined);
    assert.equal(session.draftIndex, 0);

    session.phase = GamePhase.MapBan;
    session.mapPool = ['Dust II', 'Inferno'];
    session.bannedMaps = [];
    session.banSequence = ['A'];
    session.currentBanTeam = 'A';
    session.mapVote = { team: 'A', votes: { [captainA.playerId]: 'Dust II' }, timeoutAt: Date.now() + 10_000, banCount: 1 };
    await socket.trigger(WsEvents.ADMIN_ACTION, {
        playerId: admin.playerId,
        action: 'ADMIN_BAN_MAP',
        payload: { map: 'Dust II' },
    });
    assert.equal(session.phase, GamePhase.SidePick);
    assert.deepEqual(session.bannedMaps, ['Dust II']);
    await socket.trigger(WsEvents.ADMIN_ACTION, {
        playerId: admin.playerId, action: 'UNDO_FLOW_ACTION', payload: undoPayload(session, admin),
    });
    assert.equal(session.phase, GamePhase.MapBan);
    assert.deepEqual(session.bannedMaps, []);
    assert.deepEqual(session.mapVote?.votes, { [captainA.playerId]: 'Dust II' });
    assert.ok(Number(session.mapVote?.timeoutAt) > Date.now());
    assert.ok(persisted >= 6);
});

test('entering LiveGame clears all pregame undo history', async () => {
    const session = createInitialSession();
    const admin = player('admin', 'Admin', 'Admin');
    session.players.admin = admin;
    session.playerOrder = ['admin'];
    session.phase = GamePhase.PreGameSetup;
    session.matchOptions.undercoverModeEnabled = false;
    pushFlowUndoCheckpoint(session, {
        actionType: 'ADVANCE_PHASE', actorId: admin.playerId, actorName: admin.name, summary: '赛前历史',
    });
    setSession(session);
    const io = new FakeIo();
    registerSocketHandlers(io as any, { broadcastState() {}, notifyMessage() {}, persistSessionNow() {} });
    const socket = new FakeSocket('admin-live');
    io.connect(socket);
    socket.data.playerId = admin.playerId;

    await socket.trigger(WsEvents.ADMIN_ACTION, { playerId: admin.playerId, action: 'ADVANCE_PHASE' });

    assert.equal(session.phase, GamePhase.LiveGame);
    assert.equal(exportFlowUndoState().entries.length, 0);
});

test('no-op assignment and duel LiveGame assignment do not create undo history', async () => {
    const session = createInitialSession();
    const admin = player('admin', 'Admin', 'Admin');
    const target = player('target', 'Target');
    session.players = { admin, target };
    session.playerOrder = ['admin', 'target'];
    session.phase = GamePhase.PlayerDraft;
    target.rosterTeam = 'A';
    session.teams.A.players = ['target'];
    session.draftOrder = ['A'];
    session.draftOriginalOrder = ['A'];
    setSession(session);
    const io = new FakeIo();
    registerSocketHandlers(io as any, { broadcastState() {}, notifyMessage() {}, persistSessionNow() {} });
    const socket = new FakeSocket('admin-noop');
    io.connect(socket);
    socket.data.playerId = admin.playerId;

    await socket.trigger(WsEvents.ADMIN_ACTION, {
        playerId: admin.playerId,
        action: 'ASSIGN_ROSTER_TEAM',
        payload: { playerId: target.playerId, team: 'A' },
    });
    assert.equal(exportFlowUndoState().entries.length, 0);

    session.phase = GamePhase.LiveGame;
    session.matchOptions.matchMode = 'duel';
    session.liveGameData = {
        scoreCT: 0, scoreT: 0, scoreA: 0, scoreB: 0, currentRound: 0,
        pluginConnected: false, winnerTeam: null, matchFinished: false,
        winTarget: 1, lastScoredRound: 0, duelWaitingForPlayers: true,
    };
    target.rosterTeam = undefined;
    session.teams.A.players = [];
    await socket.trigger(WsEvents.ADMIN_ACTION, {
        playerId: admin.playerId,
        action: 'ASSIGN_ROSTER_TEAM',
        payload: { playerId: target.playerId, team: 'A' },
    });
    assert.equal(target.rosterTeam, 'A');
    assert.equal(exportFlowUndoState().entries.length, 0);
});

test('duel temporary admin can undo only their own lobby-to-pregame advance', async () => {
    const session = createInitialSession();
    const temporary = player('temporary', 'Temporary');
    const target = player('target', 'Target');
    session.players = { temporary, target };
    session.playerOrder = ['temporary', 'target'];
    session.phase = GamePhase.PreGameSetup;
    session.matchOptions.matchMode = 'duel';
    session.duelTempAdminId = temporary.playerId;
    pushFlowUndoCheckpoint(session, {
        actionType: 'ASSIGN_ROSTER_TEAM',
        actorId: temporary.playerId,
        actorName: temporary.name,
        summary: '临时管理员分队',
    });
    target.rosterTeam = 'A';
    session.teams.A.players = [target.playerId];
    setSession(session);
    const io = new FakeIo();
    registerSocketHandlers(io as any, { broadcastState() {}, notifyMessage() {}, persistSessionNow() {} });
    const socket = new FakeSocket('temporary-undo');
    io.connect(socket);
    socket.data.playerId = temporary.playerId;

    await socket.trigger(WsEvents.ADMIN_ACTION, {
        playerId: temporary.playerId, action: 'UNDO_FLOW_ACTION', payload: undoPayload(session, temporary),
    });

    assert.equal(target.rosterTeam, 'A');
    assert.equal(exportFlowUndoState().entries.length, 1);
    clearFlowUndoHistory();

    session.phase = GamePhase.Lobby;
    session.matchOptions.matchMode = 'competitive';
    session.matchOptions.matchController = 'matchzy';
    session.matchOptions.undercoverModeEnabled = true;
    temporary.rosterTeam = undefined;
    target.rosterTeam = undefined;
    session.teams.A.players = [];
    session.teams.B.players = [];
    await socket.trigger(WsEvents.ADMIN_ACTION, { playerId: temporary.playerId, action: 'ADVANCE_PHASE' });
    assert.equal(session.phase, GamePhase.PreGameSetup);
    assert.equal(session.matchOptions.matchMode, 'duel');
    await socket.trigger(WsEvents.ADMIN_ACTION, {
        playerId: temporary.playerId, action: 'UNDO_FLOW_ACTION', payload: undoPayload(session, temporary),
    });
    assert.equal(session.phase, GamePhase.Lobby);
    assert.equal(session.matchOptions.matchMode, 'competitive');
    assert.equal(session.matchOptions.undercoverModeEnabled, true);
});

test('official admin can undo the temporary admin latest action while delegation is active', async () => {
    const session = createInitialSession();
    const admin = player('admin', 'Admin', 'Admin');
    const temporary = player('temporary', 'Temporary');
    const target = player('target', 'Target');
    session.players = { admin, temporary, target };
    session.playerOrder = ['admin', 'temporary', 'target'];
    session.phase = GamePhase.PreGameSetup;
    session.matchOptions.matchMode = 'duel';
    session.duelTempAdminId = temporary.playerId;
    pushFlowUndoCheckpoint(session, {
        actionType: 'ASSIGN_ROSTER_TEAM',
        actorId: temporary.playerId,
        actorName: temporary.name,
        summary: '临时管理员分队',
    });
    target.rosterTeam = 'A';
    session.teams.A.players = [target.playerId];
    setSession(session);
    const io = new FakeIo();
    registerSocketHandlers(io as any, { broadcastState() {}, notifyMessage() {}, persistSessionNow() {} });
    const socket = new FakeSocket('official-override');
    io.connect(socket);
    socket.data.playerId = admin.playerId;

    await socket.trigger(WsEvents.ADMIN_ACTION, {
        playerId: admin.playerId, action: 'UNDO_FLOW_ACTION', payload: undoPayload(session, admin),
    });

    assert.equal(target.rosterTeam, undefined);
    assert.equal(exportFlowUndoState().entries.length, 0);
});

test('duplicate socket undo request cannot pop two checkpoints', async () => {
    const session = createInitialSession();
    const admin = player('admin', 'Admin', 'Admin');
    session.players = { admin };
    session.playerOrder = ['admin'];
    session.phase = GamePhase.Lobby;
    pushFlowUndoCheckpoint(session, {
        actionType: 'ADVANCE_PHASE', actorId: admin.playerId, actorName: admin.name, summary: 'First',
    });
    session.phase = GamePhase.CaptainSelection;
    pushFlowUndoCheckpoint(session, {
        actionType: 'ADVANCE_PHASE', actorId: admin.playerId, actorName: admin.name, summary: 'Second',
    });
    session.phase = GamePhase.Roll;
    setSession(session);
    const io = new FakeIo();
    registerSocketHandlers(io as any, { broadcastState() {}, notifyMessage() {}, persistSessionNow() {} });
    const socket = new FakeSocket('duplicate-undo');
    io.connect(socket);
    socket.data.playerId = admin.playerId;
    const payload = undoPayload(session, admin);

    await socket.trigger(WsEvents.ADMIN_ACTION, { playerId: admin.playerId, action: 'UNDO_FLOW_ACTION', payload });
    await socket.trigger(WsEvents.ADMIN_ACTION, { playerId: admin.playerId, action: 'UNDO_FLOW_ACTION', payload });

    assert.equal(session.phase, GamePhase.CaptainSelection);
    assert.equal(exportFlowUndoState().entries.length, 1);
});
