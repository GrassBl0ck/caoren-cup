import assert from 'node:assert/strict';
import { EventEmitter } from 'node:events';
import test from 'node:test';
import type { Request, Response, Express } from 'express';
import type { Server } from 'socket.io';
import { configuredSecret } from '../game-constants';
import { createInitialSession } from '../session-manager';
import { registerGameModeRoutes } from './game-mode-routes';
import { registerMatchOptionsRoutes } from './match-options-routes';
import { registerMatchAdminSessions } from './match-admin-session';

function fixture() {
    const room = createInitialSession();
    room.players.host = { playerId: 'host', role: 'Admin', name: 'Host', isReady: true };
    room.players.member = { playerId: 'member', role: 'Player', name: 'Player', isReady: true };
    const connections = new Map<string, EventEmitter>();
    const io = Object.assign(new EventEmitter(), { sockets: { sockets: connections } });
    const authorize = registerMatchAdminSessions(io as unknown as Server, () => room,
        (identity, session) => identity === 'host-account' && session === 'current-session');
    function connect(id: string, playerId: string) {
        const socket = Object.assign(new EventEmitter(), {
            id, data: { playerId, identityId: '', playerCenterSessionId: '' },
        });
        connections.set(id, socket);
        io.emit('connection', socket);
        return socket;
    }
    function proof(socket: EventEmitter) {
        let value: string | null = null;
        socket.emit('MATCH_ADMIN_SESSION', (token: string | null) => { value = token; });
        return value;
    }
    return { room, connections, connect, proof, authorize };
}

const request = (proof: string | null, body = {}) => ({
    get: (name: string) => name === 'Authorization' && proof ? 'Bearer ' + proof : '',
    body,
} as Request);

test('match changes use the live admin connection and reject unauthenticated or player requests before side effects', () => {
    const f = fixture();
    const host = f.connect('host-socket', 'host');
    const member = f.connect('member-socket', 'member');
    assert.equal(f.proof(member), null);
    const handlers = new Map<string, (req: Request, res: Response) => void>();
    const app = { get() {}, post(path: string, handle: (req: Request, res: Response) => void) { handlers.set(path, handle); } };
    const commands: string[] = [];
    let effects = 0;
    const deps = {
        isMatchAdmin: f.authorize, getPhase: () => f.room.phase, ensureMatchOptions: () => f.room.matchOptions,
        applyMatchOptions: (options: unknown) => { effects++; return Object.assign(f.room.matchOptions, options); },
        enqueuePluginCommand: (kind: string) => commands.push(kind),
        notify: () => { effects++; }, broadcastState: () => { effects++; },
    };
    registerGameModeRoutes(app as unknown as Express, deps);
    registerMatchOptionsRoutes(app as unknown as Express, deps);
    const initial = structuredClone(f.room.matchOptions);
    let status = 200;
    const res = { status(code: number) { status = code; return this; }, json() {} } as Response;
    assert.equal(handlers.size, 4);
    for (const handle of handlers.values()) {
        status = 200;
        handle(request(null, { playerId: 'host' }), res);
        assert.equal(status, 403);
    }
    assert.equal(effects, 0);
    assert.deepEqual(f.room.matchOptions, initial);
    assert.deepEqual(commands, []);

    for (const handle of handlers.values()) {
        status = 200;
        handle(request(f.proof(host), { gameModeId: 'competitive' }), res);
        assert.equal(status, 200);
    }
    assert.deepEqual(commands, ['GAME_MODE_SELECT', 'GAME_MODE_START', 'GAME_MODE_STOP']);
    assert.ok(effects > 0);
});

test('match authorization follows room, role, host account session and connection lifetime', () => {
    const f = fixture();
    const socket = f.connect('host-socket', 'host');
    const initial = request(f.proof(socket));
    assert.equal(f.authorize(initial), true);
    socket.data.playerId = 'member';
    assert.equal(f.authorize(initial), false);
    socket.data.playerId = 'host';
    f.room.sessionId = 'next-room';
    assert.equal(f.authorize(initial), false);

    f.room.desktopFormalRoom = true;
    assert.equal(f.proof(socket), null);
    f.room.players.host.identityId = 'host-account';
    socket.data.identityId = 'host-account';
    socket.data.playerCenterSessionId = 'current-session';
    const hostRequest = request(f.proof(socket));
    assert.equal(f.authorize(hostRequest), true);
    socket.data.playerCenterSessionId = 'logged-out';
    assert.equal(f.authorize(hostRequest), false);
    socket.data.playerCenterSessionId = 'current-session';
    f.connections.delete(socket.id);
    assert.equal(f.authorize(hostRequest), false);
});

test('missing secrets and unchanged deployment examples disable password and plugin authentication', () => {
    for (const example of [undefined, '', '  ', 'CHANGE_ME_ADMIN_PASSWORD', 'CHANGE_ME_PLUGIN_TOKEN',
        'CHANGE_ME_TO_A_LONG_RANDOM_SECRET', 'CHANGE_ME_TO_THE_SAME_VALUE_AS_BACKEND_PLUGIN_TOKEN', 'REPLACE_ADMIN_PASSWORD', 'REPLACE_BRIDGE_TOKEN']) {
        assert.equal(configuredSecret(example), '');
    }
    assert.equal(configuredSecret('local-test-value'), 'local-test-value');
});
