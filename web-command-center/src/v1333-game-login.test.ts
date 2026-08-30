import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import test from 'node:test';

class FakeSocket {
    readonly data: Record<string, any> = {};
    readonly handshake = { address: '127.0.0.1', secure: false, headers: {} as Record<string, string> };
    readonly emitted: Array<{ event: string; payload: any }> = [];
    private readonly handlers = new Map<string, (payload: any) => any>();

    constructor(readonly id: string) {}

    on(event: string, handler: (payload: any) => any) { this.handlers.set(event, handler); }
    emit(event: string, payload: any) { this.emitted.push({ event, payload }); }
    join(_room: string) {}
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
    private connectionHandler?: (socket: FakeSocket) => void;

    on(event: string, handler: (socket: FakeSocket) => void) {
        if (event === 'connection') this.connectionHandler = handler;
    }
    connect(socket: FakeSocket) {
        this.sockets.sockets.set(socket.id, socket);
        this.connectionHandler?.(socket);
    }
}

test('GAME_CODE_LOGIN cannot consume player recovery codes while administrator login remains isolated', async (t) => {
    const runtimeDir = path.resolve(__dirname, '..', 'runtime', `legacy-socket-test-${process.pid}-${Date.now()}`);
    fs.mkdirSync(runtimeDir, { recursive: true });
    t.after(() => fs.rmSync(runtimeDir, { recursive: true, force: true }));
    process.env.IDENTITY_STORE_PATH = path.join(runtimeDir, 'identity-store.json');

    const [{ registerGameCodeLogin, v1333ConsumeGameLoginTicket, v1333IssueGameLoginCode }, identityRuntime, sessionManager, constants] = await Promise.all([
        import('./v1333-game-login.js'),
        import('./identity/identity-runtime.js'),
        import('./session-manager.js'),
        import('./game-constants.js'),
    ]);
    await identityRuntime.initializeIdentityRuntime();
    const session = sessionManager.createInitialSession();
    sessionManager.setSession(session);
    const io = new FakeIo();
    registerGameCodeLogin({ get() {}, post() {} } as any, io as any, { broadcastState() {} });

    const firstCode = v1333IssueGameLoginCode('76561198000000035', 'Legacy Socket Player');
    const firstSocket = new FakeSocket('first');
    io.connect(firstSocket);
    await firstSocket.trigger('GAME_CODE_LOGIN', { credential: firstCode.code });
    const firstLogin = firstSocket.last('LOGIN_RESPONSE');
    assert.equal(firstLogin?.success, false);
    assert.deepEqual(session.players, {});
    assert.equal(identityRuntime.lobbyIdentityService.findIdentityBySteamId('76561198000000035'), undefined);
    assert.equal(v1333ConsumeGameLoginTicket(firstCode.code)?.steamId, '76561198000000035');

    const adminSocket = new FakeSocket('admin');
    io.connect(adminSocket);
    await adminSocket.trigger('GAME_CODE_LOGIN', { credential: constants.ADMIN_PASSWORD });
    assert.equal(adminSocket.last('LOGIN_RESPONSE')?.success, true);
    assert.equal(session.players[adminSocket.data.playerId]?.role, 'Admin');
    assert.equal(Object.values(session.players).filter((player) => player.role !== 'Admin').length, 0);
});
