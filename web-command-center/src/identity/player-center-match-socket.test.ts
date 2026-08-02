import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { GamePhase, WsEvents } from '../types';
import { attachMembershipToSession } from './session-integration';
import { createTestLoginAccount } from './test-account-helper';

class FakeSocket {
    readonly data: Record<string, any> = {};
    readonly handshake = { address: '127.0.0.1', secure: false, headers: {} as Record<string, string> };
    readonly emitted: Array<{ event: string; payload: any }> = [];
    readonly rooms = new Set<string>();
    private readonly handlers = new Map<string, (payload: any) => any>();

    constructor(readonly id: string) {}
    on(event: string, handler: (payload: any) => any) { this.handlers.set(event, handler); }
    emit(event: string, payload: any) { this.emitted.push({ event, payload }); }
    join(room: string) { this.rooms.add(room); }
    leave(room: string) { this.rooms.delete(room); }
    async trigger(event: string, payload: any) {
        const handler = this.handlers.get(event);
        if (!handler) throw new Error(`missing handler: ${event}`);
        await handler(payload);
    }
    last(event: string) { return this.emitted.filter((entry) => entry.event === event).at(-1)?.payload; }
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

const runtimeDir = path.resolve(__dirname, '..', '..', 'runtime', `player-center-match-socket-${process.pid}-${Date.now()}`);
fs.mkdirSync(runtimeDir, { recursive: true });
process.env.IDENTITY_STORE_PATH = path.join(runtimeDir, 'identity-store.json');
process.env.PLAYER_CENTER_SESSION_STORE_PATH = path.join(runtimeDir, 'player-center-sessions.json');

test.after(() => fs.rmSync(runtimeDir, { recursive: true, force: true }));

test('match socket ticket binds playerId only to the same authenticated identity and current session', async () => {
    const [socketModule, identityRuntime, sessionManager] = await Promise.all([
        import('../socket-handlers.js'),
        import('./identity-runtime.js'),
        import('../session-manager.js'),
    ]);
    await identityRuntime.initializeIdentityRuntime();
    const session = sessionManager.createInitialSession();
    sessionManager.setSession(session);
    const created = await createTestLoginAccount(identityRuntime.lobbyIdentityService, {
        steamId: '76561198000010411', nickname: 'Ticket Player', password: 'current-pass',
    });
    const joined = await identityRuntime.lobbyIdentityService.joinPlayerCenterMatch(created.identity.identityId, session.sessionId);
    if (!joined.ok) throw new Error('join should succeed');
    const player = attachMembershipToSession(session, joined.membership);
    const issue = () => identityRuntime.playerCenterMatchSocketTickets.issue({
        identityId: created.identity.identityId,
        sessionId: session.sessionId,
        membershipId: joined.membership.membershipId,
    }, 30_000).ticket;
    const io = new FakeIo();
    socketModule.registerSocketHandlers(io as any, { broadcastState() {}, notifyMessage() {} });

    const unauthenticated = new FakeSocket('unauthenticated');
    io.connect(unauthenticated);
    await unauthenticated.trigger(WsEvents.PLAYER_CENTER_MATCH_LOGIN, { ticket: issue() });
    assert.equal(unauthenticated.data.playerId, null);
    assert.equal(unauthenticated.last(WsEvents.LOGIN_RESPONSE)?.success, false);

    const validTicket = issue();
    const valid = new FakeSocket('valid');
    valid.data.identityId = created.identity.identityId;
    io.connect(valid);
    await valid.trigger(WsEvents.PLAYER_CENTER_MATCH_LOGIN, { ticket: validTicket });
    assert.equal(valid.data.identityId, created.identity.identityId);
    assert.equal(valid.data.playerId, player.playerId);
    assert.equal(valid.last(WsEvents.LOGIN_RESPONSE)?.success, true);

    const replay = new FakeSocket('replay');
    replay.data.identityId = created.identity.identityId;
    io.connect(replay);
    await replay.trigger(WsEvents.PLAYER_CENTER_MATCH_LOGIN, { ticket: validTicket });
    assert.equal(replay.data.playerId, null);
    assert.equal(replay.last(WsEvents.LOGIN_RESPONSE)?.success, false);

    const staleTicket = issue();
    sessionManager.setSession(sessionManager.createInitialSession());
    const stale = new FakeSocket('stale');
    stale.data.identityId = created.identity.identityId;
    io.connect(stale);
    await stale.trigger(WsEvents.PLAYER_CENTER_MATCH_LOGIN, { ticket: staleTicket });
    assert.equal(stale.data.playerId, null);
    assert.equal(stale.last(WsEvents.LOGIN_RESPONSE)?.success, false);
});

test('disabled account cannot consume a match ticket', async () => {
    const [socketModule, identityRuntime, sessionManager] = await Promise.all([
        import('../socket-handlers.js'),
        import('./identity-runtime.js'),
        import('../session-manager.js'),
    ]);
    const session = sessionManager.createInitialSession();
    sessionManager.setSession(session);
    const created = await createTestLoginAccount(identityRuntime.lobbyIdentityService, {
        steamId: '76561198000010412', nickname: 'Disabled Ticket', password: 'current-pass',
    });
    const joined = await identityRuntime.lobbyIdentityService.joinPlayerCenterMatch(created.identity.identityId, session.sessionId);
    if (!joined.ok) throw new Error('join should succeed');
    attachMembershipToSession(session, joined.membership);
    const ticket = identityRuntime.playerCenterMatchSocketTickets.issue({
        identityId: created.identity.identityId,
        sessionId: session.sessionId,
        membershipId: joined.membership.membershipId,
    }, 30_000).ticket;
    await identityRuntime.lobbyIdentityService.setLoginAccountEnabled(created.identity.identityId, false);
    const io = new FakeIo();
    socketModule.registerSocketHandlers(io as any, { broadcastState() {}, notifyMessage() {} });

    const disabled = new FakeSocket('disabled');
    disabled.data.identityId = created.identity.identityId;
    io.connect(disabled);
    await disabled.trigger(WsEvents.PLAYER_CENTER_MATCH_LOGIN, { ticket });
    assert.equal(disabled.data.playerId, null);
    assert.equal(disabled.last(WsEvents.LOGIN_RESPONSE)?.success, false);

});

test('scoreboard new-round action clears all players and leaves every old-session membership', async () => {
    const [socketModule, identityRuntime, sessionManager] = await Promise.all([
        import('../socket-handlers.js'),
        import('./identity-runtime.js'),
        import('../session-manager.js'),
    ]);
    const session = sessionManager.createInitialSession();
    session.phase = GamePhase.Scoreboard;
    session.players.admin = { playerId: 'admin', name: 'Admin', role: 'Admin', isReady: false, isOnline: true };
    session.playerOrder.push('admin');
    sessionManager.setSession(session);
    const created = await createTestLoginAccount(identityRuntime.lobbyIdentityService, {
        steamId: '76561198000010414', nickname: 'Old Round Player', password: 'current-pass',
    });
    const joined = await identityRuntime.lobbyIdentityService.joinPlayerCenterMatch(created.identity.identityId, session.sessionId);
    if (!joined.ok) throw new Error('join should succeed');
    const player = attachMembershipToSession(session, joined.membership);
    const io = new FakeIo();
    socketModule.registerSocketHandlers(io as any, { broadcastState() {}, notifyMessage() {}, persistSessionNow() {} });
    const admin = new FakeSocket('admin-socket');
    io.connect(admin);
    admin.data.playerId = 'admin';
    const participant = new FakeSocket('participant-socket');
    participant.data.identityId = created.identity.identityId;
    io.connect(participant);
    participant.data.playerId = player.playerId;
    participant.rooms.add(player.playerId);

    await admin.trigger(WsEvents.ADMIN_ACTION, { playerId: 'admin', action: 'ADVANCE_PHASE' });

    const current = sessionManager.getSession();
    assert.notEqual(current.sessionId, session.sessionId);
    assert.equal(current.phase, GamePhase.Lobby);
    assert.deepEqual(Object.keys(current.players), ['admin']);
    assert.deepEqual(current.playerOrder, ['admin']);
    assert.equal(current.players.admin.role, 'Admin');
    assert.equal(admin.data.playerId, 'admin');
    assert.equal(identityRuntime.lobbyIdentityService.listMemberships(session.sessionId).length, 0);
    assert.equal(participant.data.identityId, created.identity.identityId);
    assert.equal(participant.data.playerId, null);
});

test('administrator login never restores a normal match player with the same name', async () => {
    const [socketModule, sessionManager, constants] = await Promise.all([
        import('../socket-handlers.js'),
        import('../session-manager.js'),
        import('../game-constants.js'),
    ]);
    const session = sessionManager.createInitialSession();
    session.players.player = { playerId: 'player', name: 'Same Name', role: 'Player', isReady: false };
    session.playerOrder.push('player');
    sessionManager.setSession(session);
    const io = new FakeIo();
    socketModule.registerSocketHandlers(io as any, { broadcastState() {}, notifyMessage() {} });
    const admin = new FakeSocket('isolated-admin');
    io.connect(admin);

    await admin.trigger(WsEvents.LOGIN, { name: 'Same Name', extraParam: constants.ADMIN_PASSWORD });

    const response = admin.last(WsEvents.LOGIN_RESPONSE);
    assert.equal(response?.success, true);
    assert.notEqual(admin.data.playerId, 'player');
    assert.equal(session.players[admin.data.playerId]?.role, 'Admin');
    assert.equal(session.players.player.role, 'Player');
});

test('disconnecting one tab keeps the shared player online while another authenticated tab remains', async () => {
    const [socketModule, sessionManager] = await Promise.all([
        import('../socket-handlers.js'),
        import('../session-manager.js'),
    ]);
    const session = sessionManager.createInitialSession();
    session.players.player = { playerId: 'player', name: 'Multi Tab', role: 'Player', isReady: false, isOnline: true };
    session.playerOrder.push('player');
    sessionManager.setSession(session);
    const io = new FakeIo();
    socketModule.registerSocketHandlers(io as any, { broadcastState() {}, notifyMessage() {} });
    const first = new FakeSocket('first-tab');
    const second = new FakeSocket('second-tab');
    io.connect(first);
    io.connect(second);
    first.data.playerId = 'player';
    second.data.playerId = 'player';

    await first.trigger('disconnect', undefined);
    assert.equal(session.players.player.isOnline, true);

    io.sockets.sockets.delete('first-tab');
    io.sockets.sockets.delete('second-tab');
    await second.trigger('disconnect', undefined);
    assert.equal(session.players.player.isOnline, false);
});
