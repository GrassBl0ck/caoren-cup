import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import test from 'node:test';

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
    async trigger(event: string, payload: any) {
        const handler = this.handlers.get(event);
        if (!handler) throw new Error(`missing handler: ${event}`);
        await handler(payload);
    }
    last(event: string) { return this.emitted.filter((entry) => entry.event === event).at(-1)?.payload; }
    count(event: string) { return this.emitted.filter((entry) => entry.event === event).length; }
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

test('fixed member socket ticket logs in once without device enrollment and rejects blocked membership', async () => {
    const runtimeDir = path.resolve(__dirname, '..', '..', 'runtime', `fixed-socket-${process.pid}-${Date.now()}`);
    fs.mkdirSync(runtimeDir, { recursive: true });
    process.env.IDENTITY_STORE_PATH = path.join(runtimeDir, 'identity-store.json');

    const [socketModule, identityRuntime, sessionManager] = await Promise.all([
        import('../socket-handlers.js'),
        import('./identity-runtime.js'),
        import('../session-manager.js'),
    ]);
    await identityRuntime.initializeIdentityRuntime();
    const session = sessionManager.createInitialSession();
    sessionManager.setSession(session);
    const io = new FakeIo();
    socketModule.registerSocketHandlers(io as any, { broadcastState() {}, notifyMessage() {} });
    await identityRuntime.lobbyIdentityService.createOrUpdateFixedAccount({
        steamId: '76561198000000090', nickname: 'Socket Member', password: 'correct-pass',
    });
    const authenticated = await identityRuntime.lobbyIdentityService.authenticateFixedAccount({
        sessionId: session.sessionId, steamId: '76561198000000090', password: 'correct-pass',
    });
    if (!authenticated.ok) throw new Error('fixed authentication should succeed');
    const issued = identityRuntime.fixedMemberSocketTickets.issue({
        membershipId: authenticated.membership.membershipId,
        sessionId: session.sessionId,
    }, 30_000);

    const first = new FakeSocket('fixed-first');
    io.connect(first);
    await first.trigger('FIXED_MEMBER_SOCKET_LOGIN', { ticket: issued.ticket });
    assert.equal(first.last('LOGIN_RESPONSE')?.success, true);
    assert.equal(first.last('LOGIN_RESPONSE')?.identityLevel, 'longTerm');
    assert.equal(first.count('DEVICE_ENROLLMENT_READY'), 0);

    const replay = new FakeSocket('fixed-replay');
    io.connect(replay);
    await replay.trigger('FIXED_MEMBER_SOCKET_LOGIN', { ticket: issued.ticket });
    assert.equal(replay.last('LOGIN_RESPONSE')?.success, false);

    await identityRuntime.lobbyIdentityService.blockMembership(authenticated.membership.membershipId);
    const blockedTicket = identityRuntime.fixedMemberSocketTickets.issue({
        membershipId: authenticated.membership.membershipId,
        sessionId: session.sessionId,
    }, 30_000);
    const blocked = new FakeSocket('fixed-blocked');
    io.connect(blocked);
    await blocked.trigger('FIXED_MEMBER_SOCKET_LOGIN', { ticket: blockedTicket.ticket });
    assert.equal(blocked.last('LOGIN_RESPONSE')?.success, false);
});

test('only an authenticated admin receives a target-bound fixed account action ticket', async () => {
    const [socketModule, identityRuntime, sessionManager] = await Promise.all([
        import('../socket-handlers.js'),
        import('./identity-runtime.js'),
        import('../session-manager.js'),
    ]);
    const session = sessionManager.createInitialSession();
    session.players.admin = { playerId: 'admin', name: 'Admin', role: 'Admin', isReady: false, isOnline: true };
    session.playerOrder.push('admin');
    sessionManager.setSession(session);
    const io = new FakeIo();
    socketModule.registerSocketHandlers(io as any, { broadcastState() {}, notifyMessage() {} });

    const outsider = new FakeSocket('outsider');
    io.connect(outsider);
    await outsider.trigger('IDENTITY_ADMIN_ACTION', {
        action: 'ISSUE_FIXED_ACCOUNT_TICKET', operation: 'create', steamId: '76561198000000091', requestId: 'outside',
    });
    assert.equal(outsider.last('IDENTITY_ADMIN_ACTION')?.error, 'admin_required');

    const admin = new FakeSocket('admin-socket');
    io.connect(admin);
    admin.data.playerId = 'admin';
    await admin.trigger('IDENTITY_ADMIN_ACTION', {
        action: 'ISSUE_FIXED_ACCOUNT_TICKET', operation: 'create', steamId: '76561198000000091', requestId: 'admin-request',
    });
    const response = admin.last('IDENTITY_ADMIN_ACTION');
    assert.equal(response?.success, true);
    assert.equal(response?.requestId, 'admin-request');
    const ticket = identityRuntime.fixedAccountAdminTickets.consume(response.adminTicket);
    assert.equal(ticket?.operation, 'create');
    assert.equal(ticket?.steamId, '76561198000000091');
    assert.equal(ticket?.adminPlayerId, 'admin');
});

test('temporary invitation login requires a strict claimed SteamID and keeps it pending', async (t) => {
    const [socketModule, identityRuntime, sessionManager] = await Promise.all([
        import('../socket-handlers.js'),
        import('./identity-runtime.js'),
        import('../session-manager.js'),
    ]);
    const session = sessionManager.createInitialSession();
    t.after(() => fs.rmSync(path.dirname(identityRuntime.identityStore.filePath), { recursive: true, force: true }));
    sessionManager.setSession(session);
    const io = new FakeIo();
    socketModule.registerSocketHandlers(io as any, { broadcastState() {}, notifyMessage() {} });

    const invalid = new FakeSocket('temp-invalid');
    io.connect(invalid);
    await invalid.trigger('LOBBY_INVITE_LOGIN', {
        inviteCode: session.lobbyAccess.inviteCode,
        nickname: 'Invalid Claim',
        claimedSteamId: '7656119-8000000092',
    });
    assert.equal(invalid.last('LOGIN_RESPONSE')?.success, false);

    const valid = new FakeSocket('temp-valid');
    io.connect(valid);
    await valid.trigger('LOBBY_INVITE_LOGIN', {
        inviteCode: session.lobbyAccess.inviteCode,
        nickname: 'Temporary Claim',
        claimedSteamId: '76561198000000092',
    });
    const response = valid.last('LOGIN_RESPONSE');
    assert.equal(response?.success, true);
    const membership = identityRuntime.lobbyIdentityService.getMembership(response.player.membershipId);
    assert.equal(membership?.identityLevel, 'temporary');
    assert.equal(membership?.confirmationState, 'pending');
    assert.equal(membership?.claimedSteamId, '76561198000000092');
});
