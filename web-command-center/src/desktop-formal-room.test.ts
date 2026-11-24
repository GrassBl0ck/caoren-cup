import assert from 'node:assert/strict';
import { once } from 'node:events';
import { tmpdir } from 'node:os';
import { mkdtemp, rm, writeFile } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import express from 'express';
import { canStartDesktopRoom, ForceRoomReady, loadDesktopRoomAdmins } from './desktop-formal-room';
import { registerDesktopRoomRoutes } from './identity/desktop-room-routes';
import { LobbyIdentityService } from './identity/identity-service';
import { IdentityStore } from './identity/identity-store';
import { attachMembershipToSession } from './identity/session-integration';
import { createTestLoginAccount } from './identity/test-account-helper';
import { createInitialSession } from './session-manager';
import { GamePhase } from './types';

test('admin email file grants only an enabled, verified account and an empty list keeps web hosting', async (t) => {
    const dir = await mkdtemp(path.join(tmpdir(), 'caoren-admins-'));
    t.after(() => rm(dir, { recursive: true, force: true }));
    const file = path.join(dir, 'admins.json');
    await writeFile(file, JSON.stringify({ adminEmails: [] }));
    assert.equal(loadDesktopRoomAdmins(file).configured(), false);
    await writeFile(file, JSON.stringify({ adminEmails: [' Host@Example.com '] }));
    const policy = loadDesktopRoomAdmins(file);
    const account = { enabled: true, emailVerifiedAt: 1, email: 'host@example.com' };
    assert.equal(policy.allows('id', { getLoginAccount: () => account }), true);
    for (const changes of [{ enabled: false }, { emailVerifiedAt: 0 }, { email: 'other@example.com' }]) {
        assert.equal(policy.allows('id', { getLoginAccount: () => ({ ...account, ...changes }) }), false);
    }
});

test('force-ready requires a second confirmation for an unchanged lobby and keeps disconnected seats', () => {
    const session = createInitialSession();
    session.players.a = { playerId: 'a', role: 'Player', name: 'A', isReady: false, steamId: '76561198000000901' };
    session.players.b = { playerId: 'b', role: 'Player', name: 'B', isReady: false, steamId: '76561198000000902', isOnline: false };
    session.players.admin = { playerId: 'admin', role: 'Admin', name: 'B', isReady: false };
    const confirmation = new ForceRoomReady(() => 5000);
    assert.equal(canStartDesktopRoom(session), false);
    const preview = confirmation.preview(session, 'host')!;
    assert.deepEqual(preview.players, [{ id: 'a', name: 'A' }, { id: 'b', name: 'B' }]);
    assert.equal(canStartDesktopRoom(session), false);
    assert.equal(confirmation.confirm(session, 'other', preview.token), false);
    session.players.a.lobbyReady = 'self';
    assert.equal(confirmation.confirm(session, 'host', preview.token), false);
    const again = confirmation.preview(session, 'host')!;
    assert.equal(confirmation.confirm(session, 'host', again.token), true);
    assert.equal(session.players.a.lobbyReady, 'self');
    assert.equal(session.players.b.lobbyReady, 'forced');
    assert.equal(session.players.b.isReady, false);
    assert.equal(canStartDesktopRoom(session), true);
    assert.equal(confirmation.confirm(session, 'host', again.token), false);
    session.phase = GamePhase.CaptainSelection;
    assert.equal(confirmation.preview(session, 'host'), undefined);
});

test('desktop participants and hosts must use verified account identities and occupy separate seats', async (t) => {
    const dir = await mkdtemp(path.resolve(__dirname, '..', 'test-desktop-room-'));
    t.after(() => rm(dir, { recursive: true, force: true }));
    const store = new IdentityStore(path.join(dir, 'identity.json'));
    await store.load();
    const service = new LobbyIdentityService(store);
    const primary = '76561198000000951';
    const alternate = '76561198000000952';
    const player = '76561198000000953';
    const hostAccount = await createTestLoginAccount(service, { steamId: primary, nickname: 'Host', password: 'current-pass' });
    const playerAccount = await createTestLoginAccount(service, { steamId: player, nickname: 'Player', password: 'current-pass' });
    await service.linkVerifiedSteamIdentity(hostAccount.identity.identityId, alternate);
    const previous = process.env.CAOREN_ROOM_ADMIN_IDENTITY_IDS;
    process.env.CAOREN_ROOM_ADMIN_IDENTITY_IDS = hostAccount.identity.identityId;
    t.after(() => {
        if (previous === undefined) delete process.env.CAOREN_ROOM_ADMIN_IDENTITY_IDS;
        else process.env.CAOREN_ROOM_ADMIN_IDENTITY_IDS = previous;
    });
    const room = createInitialSession();
    const app = express();
    app.use(express.json());
    registerDesktopRoomRoutes(app, {
        service,
        authenticate: async (req, res) => {
            const identity = req.headers['x-test-view'] === 'host' ? hostAccount.identity
                : req.headers['x-test-view'] === 'player' ? playerAccount.identity : undefined;
            if (identity) return { identityId: identity.identityId, playerOnly: req.header('x-test-player-only') === '1' ? true : undefined };
            res.status(401).end();
        },
        sameOrigin: (_req, _res, next) => next(),
        getSession: () => room,
        onPlayerJoined: membership => { attachMembershipToSession(room, membership); },
        issuePlayerSocketTicket: () => ({}),
        publish: () => {},
        start: () => { room.phase = GamePhase.CaptainSelection; return true; },
        changeMode: () => {},
    });
    const server = app.listen(0, '127.0.0.1');
    t.after(() => new Promise<void>((resolve, reject) => server.close(err => err ? reject(err) : resolve())));
    await once(server, 'listening');
    const address = server.address();
    assert.ok(address && typeof address !== 'string');
    const request = (viewer: string, route = '', body?: object, channel = 'desktop-room', playerOnly = false) => fetch(
        `http://127.0.0.1:${address.port}/api/player-center/${channel}${route}`, {
            method: body ? 'POST' : 'GET',
            headers: { 'content-type': 'application/json', 'x-test-view': viewer, 'x-test-player-only': playerOnly ? '1' : '0' },
            body: body ? JSON.stringify({ sessionId: room.sessionId, ...body }) : undefined,
        },
    );

    assert.equal((await request('guest')).status, 401);
    assert.equal((await (await request('player')).json()).state.sessionId, room.sessionId);
    assert.equal((await request('player', '/join', { as: 'admin', steamId: player })).status, 403);
    assert.equal((await request('host', '/join', { as: 'admin', steamId: player })).status, 409);
    // Simulate the host starting while a new player's membership is being saved.
    const join = service.joinPlayerCenterMatch.bind(service);
    let lateMembership = '';
    const racingJoin = t.mock.method(service, 'joinPlayerCenterMatch', async (...args: Parameters<typeof join>) => {
        const result = await join(...args);
        if (result.ok) lateMembership = result.membership.membershipId;
        room.phase = GamePhase.CaptainSelection;
        return result;
    });
    assert.equal((await request('player', '/join', { as: 'player', steamId: player }, 'room')).status, 409);
    assert.ok(service.getMembership(lateMembership)?.leftAt);
    assert.equal(Object.keys(room.players).length, 0);
    racingJoin.mock.restore();
    room.phase = GamePhase.Lobby;
    const configured = process.env.CAOREN_ROOM_ADMIN_IDENTITY_IDS;
    delete process.env.CAOREN_ROOM_ADMIN_IDENTITY_IDS;
    try {
        const simultaneous = await Promise.all(['room', 'desktop-room'].map(channel =>
            request('player', '/join', { as: 'player', steamId: player }, channel)));
        assert.deepEqual(simultaneous.map(response => response.status), [200, 200]);
        assert.equal(Object.keys(room.players).length, 1);
        assert.ok(!room.desktopFormalRoom);
        assert.equal((await request('player', '/action', { action: 'ready', steamId: player, ready: true })).status, 200);
        assert.equal((await request('player', '/action', { action: 'ready', steamId: player, ready: false })).status, 200);
    } finally {
        if (configured !== undefined) process.env.CAOREN_ROOM_ADMIN_IDENTITY_IDS = configured;
    }
    assert.equal((await request('host', '/join', { as: 'admin', steamId: alternate })).status, 200);
    const viewerBeforeJoining = await (await request('player')).json();
    assert.equal(Object.values(viewerBeforeJoining.state.players).length, 2);
    assert.equal(((Object.values(viewerBeforeJoining.state.players) as Array<{ role: string; steamId?: string }>).find(p => p.role === 'Admin')!).steamId, undefined);
    assert.equal((await request('host', '/join', { as: 'player', steamId: alternate })).status, 200);
    assert.equal((await request('host', '/join', { as: 'player', steamId: primary })).status, 409);
    assert.equal((await request('player', '/join', { as: 'player', steamId: player })).status, 200);
    const before = await (await request('player')).json();
    assert.equal((await request('player', '/join', { as: 'player', steamId: player }, 'room')).status, 200);
    const webView = await (await request('player', '', undefined, 'room')).json();
    assert.equal(webView.playerId, before.playerId);
    assert.deepEqual(Object.keys(webView.state.players).sort(), Object.keys(before.state.players).sort());
    const webReady = { action: 'ready', ready: true, steamId: player, phase: GamePhase.Lobby };
    assert.equal((await request('player', '/action', webReady, 'room')).status, 200);
    assert.equal((await (await request('player')).json()).state.players[before.playerId].lobbyReady, 'self');
    assert.equal((await request('player', '/action', { ...webReady, phase: GamePhase.Scoreboard }, 'room')).status, 409);
    assert.equal((await request('host', '/join', { as: 'admin', steamId: alternate }, 'room')).status, 403);
    assert.equal((await request('host', '/action', { ...webReady, action: 'start', steamId: alternate }, 'room')).status, 403);
    assert.equal((await request('player', '/action', { ...webReady, ready: false }, 'room')).status, 200);
    const browserHost = await (await request('host', '', undefined, 'desktop-room', true)).json();
    assert.equal(browserHost.canHost, false);
    assert.equal(browserHost.hosting, false);
    assert.equal((await request('host', '/join', { as: 'admin', steamId: alternate }, 'desktop-room', true)).status, 403);
    assert.equal((await request('host', '/action', { action: 'start', steamId: alternate }, 'desktop-room', true)).status, 403);
    assert.equal((await request('player', '/profile', { name: 'Web Player' }, 'room')).status, 200);
    await request('player', '/join', { as: 'player', steamId: player });
    assert.equal(room.players[before.playerId].name, 'Web Player');
    const hostView = await (await request('host')).json();
    const seats = Object.values(hostView.state.players) as Array<{ role: string }>;
    assert.equal(hostView.hosting, true);
    assert.equal(seats.filter(seat => seat.role === 'Admin').length, 1);
    assert.equal(seats.filter(seat => seat.role === 'Player').length, 2);
    assert.equal((await (await request('player')).json()).canHost, false);
    assert.equal((await request('player', '/action', { action: 'force-ready-preview', steamId: player })).status, 403);
    assert.equal((await request('host', '/action', { action: 'start', steamId: alternate })).status, 409);
    const preview = await (await request('host', '/action', { action: 'force-ready-preview', steamId: alternate })).json();
    assert.equal(preview.players.length, 2);
    assert.equal((await request('host', '/action', { action: 'force-ready-confirm', steamId: alternate, token: preview.token })).status, 200);
    assert.equal((await request('host', '/action', { action: 'start', steamId: alternate })).status, 200);
    assert.equal(room.phase, GamePhase.CaptainSelection);
});
