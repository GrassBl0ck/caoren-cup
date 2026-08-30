import assert from 'node:assert/strict';
import fs from 'node:fs';
import http from 'node:http';
import path from 'node:path';
import test from 'node:test';
import express from 'express';
import { AddressInfo } from 'node:net';
import { EphemeralTicketService, PlayerCenterBootstrapTicket } from './auth-core';
import { registerIdentityAuthRoutes } from './auth-routes';
import { IdentityStore } from './identity-store';
import { LobbyIdentityService } from './identity-service';
import { createTestLoginAccount } from './test-account-helper';
import { PlayerCenterSessionStore } from './player-center-session-store';

const startServer = async (name: string) => {
    const dir = path.resolve(__dirname, '..', '..', 'runtime', `player-center-routes-${name}-${process.pid}-${Date.now()}`);
    fs.mkdirSync(dir, { recursive: true });
    const identityStore = new IdentityStore(path.join(dir, 'identity-store.json'));
    const sessionStore = new PlayerCenterSessionStore(path.join(dir, 'player-center-sessions.json'));
    await Promise.all([identityStore.load(), sessionStore.load()]);
    const service = new LobbyIdentityService(identityStore);
    const tickets = new EphemeralTicketService<PlayerCenterBootstrapTicket>();
    const socketInvalidations: Array<{ identityId: string; preserveSessionId?: string }> = [];
    const app = express();
    app.use(express.json());
    registerIdentityAuthRoutes(app, {
        service,
        playerCenterSessionStore: sessionStore,
        playerCenterBootstrapTickets: tickets,
        getPlayerCenterMatchStatus: () => 'started',
        onPlayerCenterSessionsRevoked: (event) => { socketInvalidations.push(event); },
    });
    const server = http.createServer(app);
    await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
    const address = server.address() as AddressInfo;
    return {
        dir, identityStore, sessionStore, service, tickets, socketInvalidations,
        baseUrl: `http://127.0.0.1:${address.port}`,
        close: async () => {
            await new Promise<void>((resolve, reject) => server.close((error) => error ? reject(error) : resolve()));
            fs.rmSync(dir, { recursive: true, force: true });
        },
    };
};

const requestJson = async (
    url: string,
    method: string,
    body?: unknown,
    cookie?: string,
    authorization?: string,
) => {
    const response = await fetch(url, {
        method,
        headers: {
            ...(body === undefined ? {} : { 'content-type': 'application/json' }),
            ...(cookie ? { cookie } : {}),
            ...(authorization ? { authorization } : {}),
        },
        body: body === undefined ? undefined : JSON.stringify(body),
    });
    return {
        status: response.status,
        body: await response.json() as Record<string, any>,
        setCookie: response.headers.get('set-cookie') || '',
    };
};

test('desktop account login can remember a device without exposing a match identity', async (t) => {
    const runtime = await startServer('desktop-remember');
    t.after(runtime.close);
    const { identity, account } = await createAccount(runtime, '206');

    const login = await requestJson(`${runtime.baseUrl}/api/desktop-auth/account-login`, 'POST', {
        loginName: account.loginName,
        password: 'current-pass',
        deviceId: 'desktop-device-206',
    });

    assert.equal(login.status, 200);
    assert.equal(typeof login.body.deviceToken, 'string');
    assert.equal(typeof login.body.sessionBootstrapTicket, 'string');
    assert.equal(login.body.socketTicket, undefined);
    assert.equal(runtime.service.listMemberships('unused').length, 0);

    const opened = await requestJson(`${runtime.baseUrl}/api/player-center/session`, 'POST', {
        sessionBootstrapTicket: login.body.sessionBootstrapTicket,
    });
    assert.equal(opened.status, 200);
    assert.match(opened.setCookie, /^caoren_player_center=/);
    const storedSession = Object.values(runtime.sessionStore.snapshot().sessions)[0];
    const storedDevice = runtime.service.listDeviceTokens(identity.identityId)[0];
    assert.equal(storedSession.currentDeviceTokenId, storedDevice.tokenId);
    assert.equal(opened.body.joined, false);
});

test('desktop automatic login rotates every successful token and returns a one-time player-center bootstrap only', async (t) => {
    const runtime = await startServer('desktop-auto');
    t.after(runtime.close);
    const { identity } = await createAccount(runtime, '207');
    const issued = await runtime.service.issueDeviceToken(identity.identityId, 'desktop-device-207');

    const login = await requestJson(
        `${runtime.baseUrl}/api/desktop-auth/login`,
        'POST',
        {},
        undefined,
        `Bearer ${issued.rawToken}`,
    );

    assert.equal(login.status, 200);
    assert.equal(typeof login.body.sessionBootstrapTicket, 'string');
    assert.equal(typeof login.body.rotation?.rawToken, 'string');
    assert.equal(login.body.socketTicket, undefined);
    assert.equal(runtime.service.listMemberships('unused').length, 0);
    const confirmed = await requestJson(
        `${runtime.baseUrl}/api/desktop-auth/rotation/confirm`,
        'POST',
        {},
        undefined,
        `Bearer ${login.body.rotation.rawToken}`,
    );
    assert.equal(confirmed.status, 200);
    assert.equal((await runtime.service.authenticatePlayerCenterDeviceToken(issued.rawToken)).reason, 'revoked');

    const opened = await requestJson(`${runtime.baseUrl}/api/player-center/session`, 'POST', {
        sessionBootstrapTicket: login.body.sessionBootstrapTicket,
    });
    assert.equal(opened.status, 200);
    assert.equal(opened.body.joined, false);
    const replay = await requestJson(`${runtime.baseUrl}/api/player-center/session`, 'POST', {
        sessionBootstrapTicket: login.body.sessionBootstrapTicket,
    });
    assert.deepEqual({ status: replay.status, error: replay.body.error }, { status: 401, error: 'session_bootstrap_invalid' });
});

test('desktop-backed account change preserves its current device and revokes other devices', async (t) => {
    const runtime = await startServer('desktop-account-change');
    t.after(runtime.close);
    const { identity, account } = await createAccount(runtime, '208');
    const currentDevice = await runtime.service.issueDeviceToken(identity.identityId, 'current-device');
    const otherDevice = await runtime.service.issueDeviceToken(identity.identityId, 'other-device');
    const bootstrap = runtime.tickets.issue({
        identityId: identity.identityId,
        accountUpdatedAt: account.updatedAt,
        currentDeviceTokenId: currentDevice.tokenId,
    }, 30_000).ticket;
    const opened = await requestJson(`${runtime.baseUrl}/api/player-center/session`, 'POST', { sessionBootstrapTicket: bootstrap });
    const cookie = opened.setCookie.split(';')[0];

    const changed = await requestJson(`${runtime.baseUrl}/api/player-center/account/password`, 'POST', {
        currentPassword: 'current-pass', newPassword: 'replacement-pass', confirmPassword: 'replacement-pass',
    }, cookie);

    assert.equal(changed.status, 200);
    assert.equal((await runtime.service.authenticatePlayerCenterDeviceToken(currentDevice.rawToken)).ok, true);
    assert.equal((await runtime.service.authenticatePlayerCenterDeviceToken(otherDevice.rawToken)).reason, 'revoked');
});

const createAccount = async (runtime: Awaited<ReturnType<typeof startServer>>, suffix: string) => {
    const created = await createTestLoginAccount(runtime.service, {
        steamId: `7656119800000${suffix.padStart(4, '0')}`,
        nickname: `Steam Player ${suffix}`,
        password: 'current-pass',
    });
    const account = runtime.service.getLoginAccount(created.identity.identityId)!;
    return { identity: created.identity, account };
};

const establish = async (runtime: Awaited<ReturnType<typeof startServer>>, identityId: string, accountUpdatedAt: number) => {
    const ticket = runtime.tickets.issue({ identityId, accountUpdatedAt }, 30_000).ticket;
    const response = await requestJson(`${runtime.baseUrl}/api/player-center/session`, 'POST', { sessionBootstrapTicket: ticket });
    return { ...response, cookie: response.setCookie.split(';')[0] };
};

test('bootstrap ticket creates an HttpOnly browser session and current identity exposes only safe player-center fields', async (t) => {
    const runtime = await startServer('bootstrap');
    t.after(runtime.close);
    const { identity, account } = await createAccount(runtime, '201');

    const opened = await establish(runtime, identity.identityId, account.updatedAt);
    assert.equal(opened.status, 200);
    assert.match(opened.setCookie, /^caoren_player_center=/);
    assert.match(opened.setCookie, /HttpOnly/i);
    assert.match(opened.setCookie, /SameSite=Lax/i);
    assert.match(opened.setCookie, /Path=\//i);
    assert.doesNotMatch(opened.setCookie, /;\s*Secure/i);

    const current = await requestJson(`${runtime.baseUrl}/api/player-center/me`, 'GET', undefined, opened.cookie);
    assert.deepEqual(current.body, {
        success: true,
        profile: { steamNickname: identity.steamNickname || identity.displayName, loginName: account.loginName },
        matchStatus: 'started',
        joinAvailable: false,
        leaveAvailable: false,
        joined: false,
    });
    assert.equal(JSON.stringify(current.body).match(/steamId|identityId|password|token|membership|players|score/i), null);
    assert.equal(Object.keys(runtime.sessionStore.snapshot().sessions).length, 1);
});

test('bootstrap ticket is consumed once and rechecks disabled, password state, and account version', async (t) => {
    const runtime = await startServer('bootstrap-recheck');
    t.after(runtime.close);
    const { identity, account } = await createAccount(runtime, '202');
    const issued = runtime.tickets.issue({ identityId: identity.identityId, accountUpdatedAt: account.updatedAt }, 30_000).ticket;
    await runtime.service.setLoginAccountEnabled(identity.identityId, false);

    const disabled = await requestJson(`${runtime.baseUrl}/api/player-center/session`, 'POST', { sessionBootstrapTicket: issued });
    assert.deepEqual({ status: disabled.status, error: disabled.body.error }, { status: 401, error: 'session_bootstrap_invalid' });
    const replay = await requestJson(`${runtime.baseUrl}/api/player-center/session`, 'POST', { sessionBootstrapTicket: issued });
    assert.equal(replay.status, 401);
    assert.equal(Object.keys(runtime.sessionStore.snapshot().sessions).length, 0);
});

test('account version mismatch invalidates the session and clears its cookie', async (t) => {
    const runtime = await startServer('version');
    t.after(runtime.close);
    const { identity, account } = await createAccount(runtime, '203');
    const opened = await establish(runtime, identity.identityId, account.updatedAt);
    await runtime.service.setLoginAccountEnabled(identity.identityId, false);

    const current = await requestJson(`${runtime.baseUrl}/api/player-center/me`, 'GET', undefined, opened.cookie);
    assert.deepEqual({ status: current.status, error: current.body.error }, { status: 401, error: 'player_center_session_invalid' });
    assert.match(current.setCookie, /^caoren_player_center=;/);
    assert.equal(Object.keys(runtime.sessionStore.snapshot().sessions).length, 0);
});

test('login-name change verifies the current password, keeps this session, and revokes other web sessions without leaking ids', async (t) => {
    const runtime = await startServer('rename');
    t.after(runtime.close);
    const { identity, account } = await createAccount(runtime, '204');
    const current = await establish(runtime, identity.identityId, account.updatedAt);
    const other = await establish(runtime, identity.identityId, account.updatedAt);

    const wrong = await requestJson(`${runtime.baseUrl}/api/player-center/account/login-name`, 'PATCH', {
        currentPassword: 'wrong-pass', newLoginName: 'Renamed_Player',
    }, current.cookie);
    assert.deepEqual({ status: wrong.status, error: wrong.body.error }, { status: 401, error: 'current_password_incorrect' });

    const changed = await requestJson(`${runtime.baseUrl}/api/player-center/account/login-name`, 'PATCH', {
        currentPassword: 'current-pass', newLoginName: 'Renamed_Player',
    }, current.cookie);
    assert.equal(changed.status, 200);
    assert.deepEqual(changed.body, {
        success: true,
        profile: { steamNickname: identity.steamNickname || identity.displayName, loginName: 'Renamed_Player' },
        revocation: {
            revokeOtherPlayerCenterSessions: true,
            otherWebSessionsRevoked: 1,
            otherDevicesRevoked: 0,
        },
    });
    assert.equal(JSON.stringify(changed.body).match(/sessionId|tokenId|identityId|steamId/i), null);
    assert.equal((await requestJson(`${runtime.baseUrl}/api/player-center/me`, 'GET', undefined, current.cookie)).status, 200);
    assert.equal((await requestJson(`${runtime.baseUrl}/api/player-center/me`, 'GET', undefined, other.cookie)).status, 401);
    assert.deepEqual(runtime.socketInvalidations, [{
        identityId: identity.identityId,
        preserveSessionId: Object.values(runtime.sessionStore.snapshot().sessions)[0].sessionId,
    }]);
});

test('password change preserves the current session, rejects confirmation mismatch, and logout revokes only the current token', async (t) => {
    const runtime = await startServer('password-logout');
    t.after(runtime.close);
    const { identity, account } = await createAccount(runtime, '205');
    const current = await establish(runtime, identity.identityId, account.updatedAt);

    const mismatch = await requestJson(`${runtime.baseUrl}/api/player-center/account/password`, 'POST', {
        currentPassword: 'current-pass', newPassword: 'replacement-pass', confirmPassword: 'different-pass',
    }, current.cookie);
    assert.deepEqual({ status: mismatch.status, error: mismatch.body.error }, { status: 400, error: 'password_confirmation_mismatch' });

    const changed = await requestJson(`${runtime.baseUrl}/api/player-center/account/password`, 'POST', {
        currentPassword: 'current-pass', newPassword: 'replacement-pass', confirmPassword: 'replacement-pass',
    }, current.cookie);
    assert.equal(changed.status, 200);
    assert.equal((await requestJson(`${runtime.baseUrl}/api/player-center/me`, 'GET', undefined, current.cookie)).status, 200);

    const logout = await requestJson(`${runtime.baseUrl}/api/player-center/logout`, 'POST', {}, current.cookie);
    assert.deepEqual({ status: logout.status, success: logout.body.success }, { status: 200, success: true });
    assert.match(logout.setCookie, /^caoren_player_center=;/);
    assert.equal((await requestJson(`${runtime.baseUrl}/api/player-center/me`, 'GET', undefined, current.cookie)).status, 401);
});
