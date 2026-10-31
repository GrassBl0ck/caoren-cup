import assert from 'node:assert/strict';
import fs from 'node:fs';
import http from 'node:http';
import path from 'node:path';
import test from 'node:test';
import express from 'express';
import { AddressInfo } from 'node:net';
import { registerIdentityAuthRoutes } from './auth-routes';
import { IdentityStore } from './identity-store';
import { LobbyIdentityService } from './identity-service';
import { PlayerCenterSessionStore } from './player-center-session-store';
import { EmailCodeService, VerificationEmail } from './email-codes';
import { createTestLoginAccount } from './test-account-helper';
import { registerBoardPages } from '../portal-routes';

async function fixture() {
    const dir = fs.mkdtempSync(path.resolve(__dirname, '../../runtime/email-http-'));
    const store = new IdentityStore(path.join(dir, 'identity.json'));
    const sessions = new PlayerCenterSessionStore(path.join(dir, 'sessions.json'));
    await Promise.all([store.load(), sessions.load()]);
    const service = new LobbyIdentityService(store);
    const sent: VerificationEmail[] = [];
    const now = { value: Date.now() };
    const codes = new EmailCodeService({ now: () => now.value, deliver: async (message) => { sent.push(message); } });
    const gameTickets = new Map<string, { steamId: string; name: string }>();
    const invalidations: string[] = [];
    const app = express();
    app.use(express.json());
    registerIdentityAuthRoutes(app, {
        service, playerCenterSessionStore: sessions, emailCodes: codes,
        getSession: () => ({ sessionId: 'fixture-match', phase: 'Lobby' } as any),
        onPlayerCenterSessionsRevoked: (event) => { invalidations.push(event.identityId); },
        consumeGameLoginTicket: (raw) => {
            const key = String(raw || '');
            const ticket = gameTickets.get(key);
            gameTickets.delete(key);
            return ticket;
        },
    });
    fs.writeFileSync(path.join(dir, 'cs.html'), '<h1>Protected CS board</h1>', 'utf8');
    fs.writeFileSync(path.join(dir, 'mc.html'), '<h1>Protected MC board</h1>', 'utf8');
    registerBoardPages(app, { service, sessions, pagesDirectory: dir });
    app.use(express.static(path.resolve(__dirname, '../../public')));
    const server = http.createServer(app);
    await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
    const base = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
    async function request(url: string, body?: unknown, cookie?: string, origin?: string) {
        const response = await fetch(base + url, {
            method: body === undefined ? 'GET' : 'POST', redirect: 'manual',
            headers: {
                ...(body === undefined ? {} : { 'content-type': 'application/json' }),
                ...(cookie ? { cookie } : {}), ...(origin ? { origin } : {}),
            },
            body: body === undefined ? undefined : JSON.stringify(body),
        });
        const text = await response.text();
        return {
            status: response.status, headers: response.headers, text,
            data: response.headers.get('content-type')?.includes('application/json') ? JSON.parse(text) : {},
            cookie: (response.headers.get('set-cookie') || '').split(';')[0],
        };
    }
    async function proof(email: string, purpose: string, cookie?: string) {
        const response = await request('/api/account-email/code', { email, purpose }, cookie);
        assert.equal(response.status, 200);
        return { email, challengeId: response.data.challengeId, code: sent.at(-1)!.code };
    }
    return { dir, store, sessions, service, sent, now, gameTickets, invalidations, request, proof,
        close: () => new Promise<void>((resolve, reject) => server.close((error) => error ? reject(error) : resolve())),
    };
}

test('email registration signs into both protected boards but cannot join CS until a trusted game binding', async (t) => {
    const f = await fixture(); t.after(f.close);
    assert.equal((await f.request('/cs')).status, 303);
    assert.equal((await f.request('/mc.html')).status, 303);
    assert.equal((await f.request('/api/mc/info')).status, 401);
    const proof = await f.proof('new@example.com', 'register');
    const fields = { ...proof, loginName: 'New_Player', password: 'website-password', confirmPassword: 'website-password' };
    const created = await f.request('/api/account-auth/register', fields);
    assert.equal(created.status, 201);
    assert.match(created.headers.get('set-cookie')!, /HttpOnly/i);
    assert.equal(created.data.profile.hasSteamBinding, false);
    const desktop = await f.request('/api/desktop-auth/account-login', { loginName: fields.loginName, password: fields.password, deviceId: 'website-only-device' });
    assert.equal(desktop.status, 200);
    assert.equal((await f.service.authenticatePlayerCenterDeviceToken(desktop.data.deviceToken)).ok, true);
    assert.equal((await f.request('/cs', undefined, created.cookie)).status, 200);
    assert.equal((await f.request('/mc', undefined, created.cookie)).status, 200);
    assert.equal((await f.request('/api/mc/info', undefined, created.cookie)).data.premiumOnly, true);
    assert.notEqual((await f.request('/pages/cs.html')).status, 200);
    assert.notEqual((await f.request('/cs%2ehtml')).status, 200);
    const denied = await f.request('/api/player-center/match/join', {}, created.cookie);
    assert.equal(denied.data.error, 'steam_binding_required');
    assert.equal(f.service.listMemberships('fixture-match').length, 0);
    assert.equal((await f.request('/api/account-auth/register', fields)).data.error, 'email_code_invalid');
    f.gameTickets.set('BINDONCE', { steamId: '76561198000019901', name: 'Game Player' });
    const bound = await f.request('/api/player-center/account/steam', { code: 'BINDONCE', steamId: '76561198000019999' }, created.cookie);
    assert.equal(bound.status, 200);
    assert.equal(bound.data.profile.hasSteamBinding, true);
    assert.equal(f.service.findIdentityBySteamId('76561198000019999'), undefined);
    assert.equal((await f.request('/api/player-center/account/steam', { code: 'BINDONCE' }, created.cookie)).status, 400);
    assert.equal((await f.request('/api/player-center/match/join', {}, created.cookie)).status, 200);
    assert.equal(f.service.listMemberships('fixture-match').length, 1);
    await f.request('/mc', undefined, created.cookie);
    assert.equal(f.service.listMemberships('fixture-match').length, 1);
});

test('old account email binding preserves identity and password; email recovery revokes sessions and devices', async (t) => {
    const f = await fixture(); t.after(f.close);
    const old = await createTestLoginAccount(f.service, {
        steamId: '76561198000019902', nickname: 'Legacy Player', password: 'legacy-password',
    });
    const first = await f.sessions.create(old.identity.identityId, old.account.updatedAt);
    const other = await f.sessions.create(old.identity.identityId, old.account.updatedAt);
    const cookie = `caoren_player_center=${first.rawToken}`;
    const device = await f.service.issueDeviceToken(old.identity.identityId, 'old-device');
    f.gameTickets.set('ISSUEDBEFOREBIND', { steamId: '76561198000019902', name: 'Legacy Player' });
    const oldRecovery = await f.request('/api/account-recovery/game-code', { gameCode: 'ISSUEDBEFOREBIND' });
    assert.equal(oldRecovery.status, 200);
    assert.equal((await f.request('/api/account-email/code', { email: 'old@example.com', purpose: 'bind-email' })).status, 401);
    const proof = await f.proof('old@example.com', 'bind-email', cookie);
    const bound = await f.request('/api/player-center/account/email', { ...proof, currentPassword: old.password }, cookie);
    assert.equal(bound.status, 200);
    assert.equal(bound.data.profile.email, 'old@example.com');
    const staleRecovery = await f.request('/api/account-recovery/complete', {
        recoveryTicket: oldRecovery.data.recoveryTicket, newPassword: 'should-not-work', confirmPassword: 'should-not-work',
    });
    assert.equal(staleRecovery.data.error, 'use_email_recovery');
    await assert.rejects(f.service.completeAccountRecovery({ identityId: old.identity.identityId, newPassword: 'should-not-work' }), /use_email_recovery/);
    assert.equal(f.service.getLoginAccount(old.identity.identityId)!.loginName, old.account.loginName);
    assert.equal(f.service.findIdentityBySteamId('76561198000019902')!.identityId, old.identity.identityId);
    assert.equal((await f.service.authenticateLoginAccount({ loginName: old.account.loginName, password: old.password })).ok, true);
    assert.equal((await f.request('/api/player-center/me', undefined, cookie)).status, 200);
    assert.equal((await f.request('/api/player-center/me', undefined, `caoren_player_center=${other.rawToken}`)).status, 401);
    f.gameTickets.set('NOEMAILBYPASS', { steamId: '76561198000019902', name: 'Legacy Player' });
    assert.equal((await f.request('/api/account-recovery/game-code', { gameCode: 'NOEMAILBYPASS' })).data.error, 'use_email_recovery');
    f.now.value += 60_001;
    const reset = await f.proof('old@example.com', 'reset-password');
    const result = await f.request('/api/account-recovery/email', { ...reset, newPassword: 'new-password-123', confirmPassword: 'new-password-123' });
    assert.equal(result.status, 200);
    assert.equal((await f.request('/api/player-center/me', undefined, cookie)).status, 401);
    assert.equal((await f.service.authenticatePlayerCenterDeviceToken(device.rawToken)).reason, 'revoked');
    assert.equal((await f.service.authenticateLoginAccount({ loginName: old.account.loginName, password: old.password })).ok, false);
    assert.equal((await f.service.authenticateLoginAccount({ loginName: old.account.loginName, password: 'new-password-123' })).ok, true);
    assert.equal(Object.keys(f.store.snapshot().identities).length, 1);
    assert.ok(f.invalidations.length >= 2);
});

test('proof purposes, CS ownership conflicts, duplicate signup and cross-site writes are rejected', async (t) => {
    const f = await fixture(); t.after(f.close);
    const first = await f.service.registerVerifiedEmailAccount({ email: 'owner@example.com', loginName: 'Owner_Account', password: 'owner-password' });
    const second = await f.service.registerVerifiedEmailAccount({ email: 'other@example.com', loginName: 'Other_Account', password: 'other-password' });
    await f.service.bindVerifiedGameIdentity(first.identity.identityId, '76561198000019903', 'Owner');
    const session = await f.sessions.create(second.identity.identityId, second.account.updatedAt);
    const cookie = `caoren_player_center=${session.rawToken}`;
    f.gameTickets.set('CONFLICT', { steamId: '76561198000019903', name: 'Owner' });
    assert.equal((await f.request('/api/player-center/account/steam', { code: 'CONFLICT' }, cookie)).data.error, 'steam_identity_in_use');
    assert.equal(f.service.findIdentityBySteamId('76561198000019903')!.identityId, first.identity.identityId);
    const reset = await f.proof('owner@example.com', 'reset-password');
    const wrongPurpose = await f.request('/api/account-auth/register', { ...reset, loginName: 'Attack_Player', password: 'attack-password', confirmPassword: 'attack-password' });
    assert.equal(wrongPurpose.data.error, 'email_code_invalid');
    assert.equal((await f.request('/api/account-email/code', { email: 'site@example.com', purpose: 'register' }, undefined, 'https://other.example')).status, 403);
    await assert.rejects(f.service.registerVerifiedEmailAccount({ email: 'owner@example.com', loginName: 'Unused_Name', password: 'test-password' }), /registration_unavailable/);
    const attempts = await Promise.allSettled([
        f.service.registerVerifiedEmailAccount({ email: 'race@example.com', loginName: 'Race_First', password: 'test-password' }),
        f.service.registerVerifiedEmailAccount({ email: 'race@example.com', loginName: 'Race_Second', password: 'test-password' }),
    ]);
    assert.equal(attempts.filter((item) => item.status === 'fulfilled').length, 1);
    assert.equal((await f.request('/api/steam-auth/web/start')).status, 404);
});
