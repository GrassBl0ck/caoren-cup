import assert from 'node:assert/strict';
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import express from 'express';
import { AddressInfo } from 'node:net';
import { createInitialSession } from '../session-manager';
import { EphemeralTicketService, FixedAccountAdminTicket, SocketLoginTicket } from './auth-core';
import { registerIdentityAuthRoutes } from './auth-routes';
import { LobbyIdentityService } from './identity-service';
import { IdentityStore } from './identity-store';
import { FixedMemberLoginGuard } from './password-auth';

const startTestServer = async (name: string) => {
    const dir = path.resolve(__dirname, '..', '..', 'runtime', `fixed-auth-${name}-${process.pid}-${Date.now()}`);
    fs.mkdirSync(dir, { recursive: true });
    const store = new IdentityStore(path.join(dir, 'identity-store.json'));
    await store.load();
    const service = new LobbyIdentityService(store);
    const session = createInitialSession();
    const socketTickets = new EphemeralTicketService<SocketLoginTicket>();
    const adminTickets = new EphemeralTicketService<FixedAccountAdminTicket>();
    const loginGuard = new FixedMemberLoginGuard();
    const app = express();
    app.use(express.json());
    registerIdentityAuthRoutes(app, {
        service,
        getSession: () => session,
        fixedMemberSocketTickets: socketTickets,
        fixedAccountAdminTickets: adminTickets,
        fixedMemberLoginGuard: loginGuard,
    });
    const server = http.createServer(app);
    await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
    const address = server.address() as AddressInfo;
    return {
        dir,
        store,
        service,
        session,
        socketTickets,
        adminTickets,
        loginGuard,
        baseUrl: `http://127.0.0.1:${address.port}`,
        close: async () => {
            await new Promise<void>((resolve, reject) => server.close((error) => error ? reject(error) : resolve()));
            fs.rmSync(dir, { recursive: true, force: true });
        },
    };
};

const requestJson = async (method: string, url: string, body: unknown, ticket?: string) => {
    const response = await fetch(url, {
        method,
        headers: {
            'content-type': 'application/json',
            ...(ticket ? { authorization: `Bearer ${ticket}` } : {}),
        },
        body: JSON.stringify(body),
    });
    return { status: response.status, body: await response.json() as Record<string, any> };
};

const postJson = (url: string, body: unknown, ticket?: string) => requestJson('POST', url, body, ticket);
const patchJson = (url: string, body: unknown, ticket?: string) => requestJson('PATCH', url, body, ticket);

test('fixed member HTTP login distinguishes errors and returns only a one-time socket ticket', async (t) => {
    const runtime = await startTestServer('login');
    t.after(runtime.close);
    await runtime.service.createOrUpdateFixedAccount({
        steamId: '76561198000000080', nickname: 'HTTP Member', password: 'correct-pass',
    });

    const missing = await postJson(`${runtime.baseUrl}/api/fixed-member-auth/login`, {
        steamId: '76561198000000081', password: 'correct-pass',
    });
    assert.equal(missing.status, 404);
    assert.equal(missing.body.error, 'account_not_found');

    const wrong = await postJson(`${runtime.baseUrl}/api/fixed-member-auth/login`, {
        steamId: '76561198000000080', password: 'wrong-pass',
    });
    assert.equal(wrong.status, 401);
    assert.equal(wrong.body.error, 'password_incorrect');

    const success = await postJson(`${runtime.baseUrl}/api/fixed-member-auth/login`, {
        steamId: '76561198000000080', password: 'correct-pass',
    });
    assert.equal(success.status, 200);
    assert.deepEqual(Object.keys(success.body).sort(), ['socketTicket', 'socketTicketExpiresAt', 'success']);
    assert.equal(JSON.stringify(success.body).match(/password|hash|salt|deviceToken|rotation/i), null);
    assert.ok(runtime.socketTickets.consume(success.body.socketTicket));
    assert.equal(runtime.socketTickets.consume(success.body.socketTicket), undefined);
});

test('fixed login becomes rate limited on the tenth failed attempt for a SteamID', async (t) => {
    const runtime = await startTestServer('rate-limit');
    t.after(runtime.close);
    let latest: Awaited<ReturnType<typeof postJson>> | undefined;
    for (let attempt = 1; attempt <= 10; attempt += 1) {
        latest = await postJson(`${runtime.baseUrl}/api/fixed-member-auth/login`, {
            steamId: '76561198000000082', password: 'wrong-pass',
        });
    }
    assert.equal(latest?.status, 429);
    assert.equal(latest?.body.error, 'rate_limited');
    assert.equal(typeof latest?.body.retryAt, 'number');
});

test('fixed login rejects a nickname already used by a legacy session player', async (t) => {
    const runtime = await startTestServer('legacy-name-collision');
    t.after(runtime.close);
    await runtime.service.createOrUpdateFixedAccount({
        steamId: '76561198000000086', nickname: 'Legacy Name', password: 'correct-pass',
    });
    runtime.session.players.legacy = {
        playerId: 'legacy', name: 'Legacy Name', role: 'Player', isReady: false, isOnline: true,
    };
    runtime.session.playerOrder.push('legacy');

    const result = await postJson(`${runtime.baseUrl}/api/fixed-member-auth/login`, {
        steamId: '76561198000000086', password: 'correct-pass',
    });

    assert.equal(result.status, 409);
    assert.equal(result.body.error, 'nickname_in_use');
    assert.equal(runtime.service.listMemberships(runtime.session.sessionId).length, 0);
});

test('administrator create ticket is target-bound and single-use without exposing credentials', async (t) => {
    const runtime = await startTestServer('admin-ticket');
    t.after(runtime.close);
    const issued = runtime.adminTickets.issue({
        sessionId: runtime.session.sessionId,
        adminPlayerId: 'admin-player',
        operation: 'create',
        steamId: '76561198000000083',
    }, 30_000);

    const wrongTarget = await postJson(`${runtime.baseUrl}/api/admin/fixed-members`, {
        steamId: '76561198000000084', nickname: 'Wrong Target', password: 'initial-pass',
    }, issued.ticket);
    assert.equal(wrongTarget.status, 403);
    assert.equal(wrongTarget.body.error, 'admin_ticket_invalid');

    const validTicket = runtime.adminTickets.issue({
        sessionId: runtime.session.sessionId,
        adminPlayerId: 'admin-player',
        operation: 'create',
        steamId: '76561198000000083',
    }, 30_000);
    const created = await postJson(`${runtime.baseUrl}/api/admin/fixed-members`, {
        steamId: '76561198000000083', nickname: 'Admin Created', password: 'initial-pass',
    }, validTicket.ticket);
    assert.equal(created.status, 200);
    assert.deepEqual(Object.keys(created.body.account).sort(), [
        'enabled', 'identityId', 'nickname', 'passwordUpdatedAt', 'steamId',
    ]);
    assert.equal(JSON.stringify(created.body).match(/initial-pass|hash|salt|params/i), null);

    const replay = await postJson(`${runtime.baseUrl}/api/admin/fixed-members`, {
        steamId: '76561198000000083', nickname: 'Replay', password: 'initial-pass',
    }, validTicket.ticket);
    assert.equal(replay.status, 403);
    assert.equal(replay.body.error, 'admin_ticket_invalid');
});

test('administrator tickets rename reset and disable an existing fixed account', async (t) => {
    const runtime = await startTestServer('admin-operations');
    t.after(runtime.close);
    const account = await runtime.service.createOrUpdateFixedAccount({
        steamId: '76561198000000085', nickname: 'Before Rename', password: 'initial-pass',
    });
    const login = await runtime.service.authenticateFixedAccount({
        sessionId: runtime.session.sessionId, steamId: '76561198000000085', password: 'initial-pass',
    });
    if (!login.ok) throw new Error('initial fixed login should succeed');

    const renameTicket = runtime.adminTickets.issue({
        sessionId: runtime.session.sessionId,
        adminPlayerId: 'admin-player',
        operation: 'rename',
        identityId: account.identity.identityId,
    }, 30_000);
    const renamed = await patchJson(
        `${runtime.baseUrl}/api/admin/fixed-members/${account.identity.identityId}/nickname`,
        { nickname: 'After Rename' },
        renameTicket.ticket,
    );
    assert.equal(renamed.status, 200);
    assert.equal(renamed.body.account.nickname, 'After Rename');

    const resetTicket = runtime.adminTickets.issue({
        sessionId: runtime.session.sessionId,
        adminPlayerId: 'admin-player',
        operation: 'reset_password',
        identityId: account.identity.identityId,
    }, 30_000);
    for (let attempt = 0; attempt < 10; attempt += 1) {
        runtime.loginGuard.recordFailure(['steam:76561198000000085']);
    }
    assert.equal(runtime.loginGuard.check(['steam:76561198000000085']).blocked, true);
    const reset = await postJson(
        `${runtime.baseUrl}/api/admin/fixed-members/${account.identity.identityId}/password`,
        { password: 'replacement-pass' },
        resetTicket.ticket,
    );
    assert.equal(reset.status, 200);
    assert.equal(runtime.loginGuard.check(['steam:76561198000000085']).blocked, false);
    assert.equal((await runtime.service.authenticateFixedAccount({
        sessionId: 'password-check', steamId: '76561198000000085', password: 'initial-pass',
    })).reason, 'password_incorrect');

    const disableTicket = runtime.adminTickets.issue({
        sessionId: runtime.session.sessionId,
        adminPlayerId: 'admin-player',
        operation: 'set_enabled',
        identityId: account.identity.identityId,
    }, 30_000);
    const disabled = await patchJson(
        `${runtime.baseUrl}/api/admin/fixed-members/${account.identity.identityId}/enabled`,
        { enabled: false },
        disableTicket.ticket,
    );
    assert.equal(disabled.status, 200);
    assert.equal(disabled.body.account.enabled, false);
    assert.equal(runtime.service.getMembership(login.membership.membershipId)?.blockedAt !== undefined, true);
    assert.deepEqual(await runtime.service.authenticateFixedAccount({
        sessionId: runtime.session.sessionId, steamId: '76561198000000085', password: 'replacement-pass',
    }), { ok: false, reason: 'account_disabled' });
});
