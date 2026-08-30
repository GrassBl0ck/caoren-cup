import assert from 'node:assert/strict';
import fs from 'node:fs';
import http from 'node:http';
import path from 'node:path';
import test from 'node:test';
import express from 'express';
import { AddressInfo } from 'node:net';
import { GamePhase } from '../types';
import { createInitialSession } from '../session-manager';
import {
    EphemeralTicketService,
    PlayerCenterBootstrapTicket,
    PlayerCenterMatchSocketTicket,
} from './auth-core';
import { registerIdentityAuthRoutes } from './auth-routes';
import { IdentityStore } from './identity-store';
import { LobbyIdentityService } from './identity-service';
import { createTestLoginAccount } from './test-account-helper';
import { PlayerCenterSessionStore } from './player-center-session-store';
import { attachMembershipToSession, detachMatchMembershipsForScoreboard, removeIdentityFromSession } from './session-integration';

const requestJson = async (url: string, method: string, body?: unknown, cookie?: string) => {
    const response = await fetch(url, {
        method,
        headers: {
            ...(body === undefined ? {} : { 'content-type': 'application/json' }),
            ...(cookie ? { cookie } : {}),
        },
        body: body === undefined ? undefined : JSON.stringify(body),
    });
    return {
        status: response.status,
        body: await response.json() as Record<string, any>,
        setCookie: response.headers.get('set-cookie') || '',
    };
};

const startRuntime = async (name: string) => {
    const dir = path.resolve(__dirname, '..', '..', 'runtime', `player-center-match-${name}-${process.pid}-${Date.now()}`);
    fs.mkdirSync(dir, { recursive: true });
    const identityStore = new IdentityStore(path.join(dir, 'identity-store.json'));
    const sessionStore = new PlayerCenterSessionStore(path.join(dir, 'player-center-sessions.json'));
    await Promise.all([identityStore.load(), sessionStore.load()]);
    const service = new LobbyIdentityService(identityStore);
    const bootstrapTickets = new EphemeralTicketService<PlayerCenterBootstrapTicket>();
    const matchTickets = new EphemeralTicketService<PlayerCenterMatchSocketTicket>();
    const session = createInitialSession();
    let joinedCalls = 0;
    let leftCalls = 0;
    const app = express();
    app.use(express.json());
    registerIdentityAuthRoutes(app, {
        service,
        playerCenterSessionStore: sessionStore,
        playerCenterBootstrapTickets: bootstrapTickets,
        playerCenterMatchSocketTickets: matchTickets,
        getSession: () => session,
        onPlayerCenterMatchJoined: (membership) => {
            joinedCalls += 1;
            attachMembershipToSession(session, membership);
        },
        onPlayerCenterMatchLeft: (identityId) => {
            leftCalls += 1;
            removeIdentityFromSession(session, identityId);
        },
    });
    const server = http.createServer(app);
    await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
    const address = server.address() as AddressInfo;
    return {
        dir, service, sessionStore, bootstrapTickets, matchTickets, session,
        get joinedCalls() { return joinedCalls; },
        get leftCalls() { return leftCalls; },
        baseUrl: `http://127.0.0.1:${address.port}`,
        close: async () => {
            await new Promise<void>((resolve, reject) => server.close((error) => error ? reject(error) : resolve()));
            fs.rmSync(dir, { recursive: true, force: true });
        },
    };
};

const openPlayerCenter = async (runtime: Awaited<ReturnType<typeof startRuntime>>, suffix: string) => {
    const created = await createTestLoginAccount(runtime.service, {
        steamId: `7656119800001${suffix.padStart(4, '0')}`,
        nickname: `Steam Match ${suffix}`,
        password: 'current-pass',
    });
    const account = runtime.service.getLoginAccount(created.identity.identityId)!;
    const ticket = runtime.bootstrapTickets.issue({
        identityId: created.identity.identityId,
        accountUpdatedAt: account.updatedAt,
    }, 30_000).ticket;
    const opened = await requestJson(`${runtime.baseUrl}/api/player-center/session`, 'POST', {
        sessionBootstrapTicket: ticket,
    });
    return {
        identity: created.identity,
        cookie: opened.setCookie.split(';')[0],
    };
};

test('waiting account session joins idempotently and receives a single-use identity/session/membership ticket', async (t) => {
    const runtime = await startRuntime('join');
    t.after(runtime.close);
    const account = await openPlayerCenter(runtime, '401');

    const before = await requestJson(`${runtime.baseUrl}/api/player-center/me`, 'GET', undefined, account.cookie);
    assert.deepEqual({ matchStatus: before.body.matchStatus, joinAvailable: before.body.joinAvailable, joined: before.body.joined }, {
        matchStatus: 'waiting', joinAvailable: true, joined: false,
    });

    const first = await requestJson(`${runtime.baseUrl}/api/player-center/match/join`, 'POST', {}, account.cookie);
    assert.equal(first.status, 200);
    assert.equal(first.body.success, true);
    assert.equal(typeof first.body.socketTicket, 'string');
    assert.equal(Object.keys(runtime.session.players).length, 1);
    assert.equal(runtime.service.listMemberships(runtime.session.sessionId).length, 1);
    const firstClaim = runtime.matchTickets.consume(first.body.socketTicket);
    const player = Object.values(runtime.session.players)[0];
    assert.deepEqual(firstClaim, {
        identityId: account.identity.identityId,
        sessionId: runtime.session.sessionId,
        membershipId: player.membershipId,
    });
    assert.equal(runtime.matchTickets.consume(first.body.socketTicket), undefined);

    const second = await requestJson(`${runtime.baseUrl}/api/player-center/match/join`, 'POST', {}, account.cookie);
    assert.equal(second.status, 200);
    assert.equal(Object.keys(runtime.session.players).length, 1);
    assert.equal(runtime.service.listMemberships(runtime.session.sessionId).length, 1);
    assert.equal(runtime.joinedCalls, 2);
});

test('started match rejects joining and leaving without changing current membership', async (t) => {
    const runtime = await startRuntime('phase');
    t.after(runtime.close);
    const account = await openPlayerCenter(runtime, '402');
    runtime.session.phase = GamePhase.CaptainSelection;

    const join = await requestJson(`${runtime.baseUrl}/api/player-center/match/join`, 'POST', {}, account.cookie);
    assert.deepEqual({ status: join.status, error: join.body.error }, { status: 409, error: 'match_not_waiting' });
    assert.equal(runtime.service.listMemberships(runtime.session.sessionId).length, 0);

    runtime.session.phase = GamePhase.Lobby;
    assert.equal((await requestJson(`${runtime.baseUrl}/api/player-center/match/join`, 'POST', {}, account.cookie)).status, 200);
    runtime.session.phase = GamePhase.LiveGame;
    const leave = await requestJson(`${runtime.baseUrl}/api/player-center/match/leave`, 'POST', {}, account.cookie);
    assert.deepEqual({ status: leave.status, error: leave.body.error }, { status: 409, error: 'match_not_waiting' });
    assert.equal(runtime.service.listMemberships(runtime.session.sessionId).length, 1);
    assert.equal(Object.keys(runtime.session.players).length, 1);
});

test('an existing member can request a fresh socket ticket after reconnect even after the match started', async (t) => {
    const runtime = await startRuntime('reconnect');
    t.after(runtime.close);
    const account = await openPlayerCenter(runtime, '407');
    const notJoined = await requestJson(`${runtime.baseUrl}/api/player-center/match/socket-ticket`, 'POST', {}, account.cookie);
    assert.deepEqual({ status: notJoined.status, error: notJoined.body.error }, { status: 403, error: 'match_membership_required' });

    assert.equal((await requestJson(`${runtime.baseUrl}/api/player-center/match/join`, 'POST', {}, account.cookie)).status, 200);
    runtime.session.phase = GamePhase.LiveGame;
    const resumed = await requestJson(`${runtime.baseUrl}/api/player-center/match/socket-ticket`, 'POST', {}, account.cookie);
    assert.equal(resumed.status, 200);
    assert.equal(typeof resumed.body.socketTicket, 'string');
    assert.deepEqual(runtime.matchTickets.consume(resumed.body.socketTicket), {
        identityId: account.identity.identityId,
        sessionId: runtime.session.sessionId,
        membershipId: Object.values(runtime.session.players)[0].membershipId,
    });
});

test('waiting leave keeps the player-center session and allows a fresh membership on rejoin', async (t) => {
    const runtime = await startRuntime('leave-rejoin');
    t.after(runtime.close);
    const account = await openPlayerCenter(runtime, '403');
    const joined = await requestJson(`${runtime.baseUrl}/api/player-center/match/join`, 'POST', {}, account.cookie);
    assert.equal(joined.status, 200);
    const oldMembershipId = Object.values(runtime.session.players)[0].membershipId;

    const left = await requestJson(`${runtime.baseUrl}/api/player-center/match/leave`, 'POST', {}, account.cookie);
    assert.deepEqual({ status: left.status, success: left.body.success }, { status: 200, success: true });
    assert.equal(Object.keys(runtime.session.players).length, 0);
    assert.equal(runtime.service.getMembership(oldMembershipId!)?.leftAt !== undefined, true);
    assert.equal(runtime.leftCalls, 1);
    const current = await requestJson(`${runtime.baseUrl}/api/player-center/me`, 'GET', undefined, account.cookie);
    assert.deepEqual({ status: current.status, joined: current.body.joined, joinAvailable: current.body.joinAvailable }, {
        status: 200, joined: false, joinAvailable: true,
    });

    const rejoined = await requestJson(`${runtime.baseUrl}/api/player-center/match/join`, 'POST', {}, account.cookie);
    assert.equal(rejoined.status, 200);
    assert.notEqual(Object.values(runtime.session.players)[0].membershipId, oldMembershipId);
});

test('disabled account invalidates the web session before it can join', async (t) => {
    const runtime = await startRuntime('disabled');
    t.after(runtime.close);
    const account = await openPlayerCenter(runtime, '404');
    await runtime.service.setLoginAccountEnabled(account.identity.identityId, false);

    const joined = await requestJson(`${runtime.baseUrl}/api/player-center/match/join`, 'POST', {}, account.cookie);
    assert.deepEqual({ status: joined.status, error: joined.body.error }, {
        status: 401, error: 'player_center_session_invalid',
    });
    assert.equal(runtime.service.listMemberships(runtime.session.sessionId).length, 0);
});

test('session-wide lifecycle cleanup marks every active membership left', async (t) => {
    const runtime = await startRuntime('cleanup');
    t.after(runtime.close);
    const first = await openPlayerCenter(runtime, '405');
    const second = await openPlayerCenter(runtime, '406');
    await requestJson(`${runtime.baseUrl}/api/player-center/match/join`, 'POST', {}, first.cookie);
    await requestJson(`${runtime.baseUrl}/api/player-center/match/join`, 'POST', {}, second.cookie);

    const cleared = await runtime.service.leaveSessionMemberships(runtime.session.sessionId);

    assert.equal(cleared, 2);
    assert.equal(runtime.service.listMemberships(runtime.session.sessionId).length, 0);
});

test('scoreboard cleanup detaches participant credentials but keeps result rows and administrator access', () => {
    const session = createInitialSession();
    session.phase = GamePhase.Scoreboard;
    session.players.player = {
        playerId: 'player', name: 'Result Player', role: 'Player', isReady: false,
        identityId: 'identity', membershipId: 'membership', identityLevel: 'longTerm',
        finalScore: 12,
    };
    session.players.admin = { playerId: 'admin', name: 'Admin', role: 'Admin', isReady: false };
    session.playerOrder.push('player', 'admin');

    assert.deepEqual(detachMatchMembershipsForScoreboard(session), ['player']);
    assert.equal(session.players.player.finalScore, 12);
    assert.equal(session.players.player.identityId, undefined);
    assert.equal(session.players.player.membershipId, undefined);
    assert.equal(session.players.player.identityLevel, undefined);
    assert.equal(session.players.admin.role, 'Admin');
});
