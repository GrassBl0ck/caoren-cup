import assert from 'node:assert/strict';
import test from 'node:test';
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
    private connectionHandler?: (socket: FakeSocket) => void;
    on(event: string, handler: (socket: FakeSocket) => void) { if (event === 'connection') this.connectionHandler = handler; }
    connect(socket: FakeSocket) { this.sockets.sockets.set(socket.id, socket); this.connectionHandler?.(socket); }
    to() { return { emit() {} }; }
    emit() {}
}

const makePlayer = (id: string, team: 'A' | 'B', options: Partial<Player> = {}): Player => ({
    playerId: id,
    name: id,
    role: 'Player',
    rosterTeam: team,
    isReady: false,
    isOnline: true,
    ...options,
});

const connect = (session: ReturnType<typeof createInitialSession>, playerId: string) => {
    setSession(session);
    const io = new FakeIo();
    registerSocketHandlers(io as any, { broadcastState() {}, notifyMessage() {}, persistSessionNow() {} });
    const socket = new FakeSocket(`${playerId}-socket`);
    io.connect(socket);
    socket.data.playerId = playerId;
    return socket;
};

test.afterEach(() => {
    clearAllFlowTimers();
    if (getSession().rollTimeout) clearTimeout(getSession().rollTimeout);
    getSession().rollTimeout = undefined;
});

test('accusations enforce team, self, type and lock rules while offline players do not block completion', async () => {
    const session = createInitialSession();
    session.phase = GamePhase.PostGameAccusation;
    const accuser = makePlayer('accuser', 'A', { gameRole: 'Soldier' });
    const ally = makePlayer('ally', 'A', { gameRole: 'Undercover' });
    const ally2 = makePlayer('ally2', 'A', { gameRole: 'Soldier' });
    const enemy = makePlayer('enemy', 'B', { gameRole: 'Undercover' });
    const offline = makePlayer('offline', 'B', { gameRole: 'Soldier', isOnline: false });
    for (const player of [accuser, ally, ally2, enemy, offline]) session.players[player.playerId] = player;
    session.accusations = {
        accuser: { own: null, enemy: null },
        ally: { own: 'ally2', enemy: 'enemy' },
        ally2: { own: 'ally', enemy: 'enemy' },
        enemy: { own: 'offline', enemy: 'ally' },
        offline: { own: null, enemy: null },
    };
    const socket = connect(session, accuser.playerId);

    await socket.trigger('ACCUSE', { playerId: accuser.playerId, targetId: accuser.playerId, type: 'own' });
    await socket.trigger('ACCUSE', { playerId: accuser.playerId, targetId: enemy.playerId, type: 'own' });
    await socket.trigger('ACCUSE', { playerId: accuser.playerId, targetId: ally.playerId, type: 'invalid' });
    assert.deepEqual(session.accusations.accuser, { own: null, enemy: null });

    await socket.trigger('ACCUSE', { playerId: accuser.playerId, targetId: ally.playerId, type: 'own' });
    await socket.trigger('ACCUSE', { playerId: accuser.playerId, targetId: ally2.playerId, type: 'own' });
    assert.equal(session.accusations.accuser.own, ally.playerId);

    await socket.trigger('ACCUSE', { playerId: accuser.playerId, targetId: enemy.playerId, type: 'enemy' });
    assert.equal(session.phase, GamePhase.Scoreboard);
});

test('repeated role release preserves acknowledgements and editing a released role requires re-release', async () => {
    const session = createInitialSession();
    session.phase = GamePhase.PreGameSetup;
    const admin: Player = { playerId: 'admin', name: 'Admin', role: 'Admin', isReady: true, isOnline: true };
    const undercover = makePlayer('undercover', 'A', { gameRole: 'Undercover' });
    const secondUndercover = makePlayer('undercover-b', 'B', { gameRole: 'Undercover' });
    const soldier = makePlayer('soldier', 'B', { gameRole: 'Soldier' });
    session.players = { admin, undercover, secondUndercover, soldier };
    const socket = connect(session, admin.playerId);

    await socket.trigger(WsEvents.ADMIN_ACTION, { playerId: admin.playerId, action: 'RELEASE_ROLES' });
    undercover.undercoverTaskAckStage = 'read';
    undercover.isReady = true;
    await socket.trigger(WsEvents.ADMIN_ACTION, { playerId: admin.playerId, action: 'RELEASE_ROLES' });
    assert.equal(undercover.undercoverTaskAckStage, 'read');
    assert.equal(undercover.isReady, true);

    await socket.trigger(WsEvents.ADMIN_ACTION, {
        playerId: admin.playerId,
        action: 'SET_PLAYER_ROLE',
        payload: { playerId: soldier.playerId, gameRole: 'Detective' },
    });
    assert.equal(session.rolesReleased, false);
    assert.equal(undercover.undercoverTaskAckStage, 'none');
});

test('role release requires exact configured counts on both teams', async () => {
    const session = createInitialSession();
    session.phase = GamePhase.PreGameSetup;
    session.undercoverCount = 1;
    session.detectiveCount = 1;
    const admin: Player = { playerId: 'admin', name: 'Admin', role: 'Admin', isReady: true, isOnline: true };
    session.players = {
        admin,
        a1: makePlayer('a1', 'A', { gameRole: 'Undercover' }),
        a2: makePlayer('a2', 'A', { gameRole: 'Soldier' }),
        b1: makePlayer('b1', 'B', { gameRole: 'Undercover' }),
        b2: makePlayer('b2', 'B', { gameRole: 'Detective' }),
    };
    const socket = connect(session, admin.playerId);

    await socket.trigger(WsEvents.ADMIN_ACTION, { playerId: admin.playerId, action: 'RELEASE_ROLES' });
    assert.equal(session.rolesReleased, false);
});

test('role release rejects participants without an A or B roster team', async () => {
    const session = createInitialSession();
    session.phase = GamePhase.PreGameSetup;
    session.undercoverCount = 0;
    session.detectiveCount = 0;
    const admin: Player = { playerId: 'admin', name: 'Admin', role: 'Admin', isReady: true, isOnline: true };
    const orphan: Player = { playerId: 'orphan', name: 'orphan', role: 'Player', gameRole: 'Soldier', isReady: false, isOnline: true };
    session.players = { admin, orphan };
    const socket = connect(session, admin.playerId);

    await socket.trigger(WsEvents.ADMIN_ACTION, { playerId: admin.playerId, action: 'RELEASE_ROLES' });
    assert.equal(session.rolesReleased, false);
});

test('task templates can change only before roles are released in lobby or pregame', async () => {
    const session = createInitialSession();
    const admin: Player = { playerId: 'admin', name: 'Admin', role: 'Admin', isReady: true, isOnline: true };
    const undercover = makePlayer('undercover', 'A', { gameRole: 'Undercover' });
    session.players = { admin, undercover };
    const original = session.taskTemplate;
    const replacement = { ...original, replacementTask: { level: 4, description: 'new' } };
    const socket = connect(session, admin.playerId);

    session.phase = GamePhase.LiveGame;
    await socket.trigger(WsEvents.ADMIN_ACTION, { playerId: admin.playerId, action: 'UPDATE_TASK_TEMPLATE', payload: { taskTemplate: replacement } });
    assert.equal(session.taskTemplate, original);

    session.phase = GamePhase.PreGameSetup;
    session.rolesReleased = true;
    await socket.trigger(WsEvents.ADMIN_ACTION, { playerId: admin.playerId, action: 'UPDATE_TASK_TEMPLATE', payload: { taskTemplate: replacement } });
    assert.equal(session.taskTemplate, original);

    session.rolesReleased = false;
    await socket.trigger(WsEvents.ADMIN_ACTION, { playerId: admin.playerId, action: 'UPDATE_TASK_TEMPLATE', payload: { taskTemplate: replacement } });
    assert.equal(session.taskTemplate, replacement);
});

test('task hints can be revealed only when the selected task has non-empty hint text', async () => {
    const session = createInitialSession();
    const undercover = makePlayer('undercover', 'A', {
        gameRole: 'Undercover',
        taskGrid: {
            A1: {
                cellId: 'A1', description: 'task', hint: '', level: 1, type: 'custom',
                status: 'Incomplete', nType: 'none', isHintUsed: false,
            } as any,
        },
    });
    session.players = { undercover };
    const socket = connect(session, undercover.playerId);

    await socket.trigger(WsEvents.TASK_ACTION, { playerId: undercover.playerId, action: 'REQUEST_HINT', cellId: 'A1' });
    assert.equal(undercover.taskGrid!.A1.isHintUsed, false);

    undercover.taskGrid!.A1.hint = 'look at the round timing';
    await socket.trigger(WsEvents.TASK_ACTION, { playerId: undercover.playerId, action: 'REQUEST_HINT', cellId: 'A1' });
    assert.equal(undercover.taskGrid!.A1.isHintUsed, true);
});
