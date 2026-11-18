import assert from 'node:assert/strict';
import fs from 'node:fs';
import http from 'node:http';
import path from 'node:path';
import test from 'node:test';
import express from 'express';
import { AddressInfo } from 'node:net';
import { EphemeralTicketService } from './auth-core';
import { registerIdentityAuthRoutes } from './auth-routes';
import { IdentityStore } from './identity-store';
import { LobbyIdentityService } from './identity-service';
import { createTestLoginAccount } from './test-account-helper';
import { AccountLoginGuard } from './password-auth';
import { PlayerCenterSessionStore } from './player-center-session-store';

const makeService = async (name: string) => {
    const dir = path.resolve(__dirname, '..', '..', 'runtime', `account-auth-${name}-${process.pid}-${Date.now()}`);
    fs.mkdirSync(dir, { recursive: true });
    const store = new IdentityStore(path.join(dir, 'identity-store.json'));
    await store.load();
    return { dir, store, service: new LobbyIdentityService(store) };
};

const postJson = async (url: string, body: unknown) => {
    const response = await fetch(url, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(body),
    });
    return { status: response.status, body: await response.json() as Record<string, any> };
};

const startAuthServer = async (
    name: string,
    consumeGameLoginTicket?: (code: unknown) => { steamId: string; name: string } | undefined,
    accountLoginGuard: AccountLoginGuard = new AccountLoginGuard(),
) => {
    const runtime = await makeService(name);
    const bootstrapTickets = new EphemeralTicketService<{ identityId: string; accountUpdatedAt: number }>();
    const recoveryTickets = new EphemeralTicketService<{ identityId: string }>();
    const playerCenterSessionStore = new PlayerCenterSessionStore(path.join(runtime.dir, 'player-center-sessions.json'));
    await playerCenterSessionStore.load();
    const app = express();
    app.use(express.json());
    registerIdentityAuthRoutes(app, {
        service: runtime.service,
        accountLoginGuard,
        playerCenterBootstrapTickets: bootstrapTickets,
        accountRecoveryTickets: recoveryTickets,
        playerCenterSessionStore,
        consumeGameLoginTicket,
    });
    const server = http.createServer(app);
    await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
    const address = server.address() as AddressInfo;
    return {
        ...runtime,
        bootstrapTickets,
        recoveryTickets,
        baseUrl: `http://127.0.0.1:${address.port}`,
        close: async () => {
            await new Promise<void>((resolve, reject) => server.close((error) => error ? reject(error) : resolve()));
            fs.rmSync(runtime.dir, { recursive: true, force: true });
        },
    };
};

test('account login is case-sensitive and unifies missing-account and wrong-password failures', async (t) => {
    const { dir, service } = await makeService('login');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const provisioned = await createTestLoginAccount(service, {
        steamId: '76561198000000101', nickname: 'Login Player', password: 'correct-pass',
    });
    const account = service.getLoginAccount(provisioned.identity.identityId)!;

    const success = await service.authenticateLoginAccount({
        loginName: account.loginName,
        password: 'correct-pass',
    });
    assert.equal(success.ok, true);
    if (!success.ok) throw new Error('account login should succeed');
    assert.equal(success.identity.identityId, provisioned.identity.identityId);

    assert.deepEqual(await service.authenticateLoginAccount({
        loginName: account.loginName.toLowerCase(),
        password: 'correct-pass',
    }), { ok: false, reason: 'invalid_credentials' });
    assert.deepEqual(await service.authenticateLoginAccount({
        loginName: account.loginName,
        password: 'wrong-pass',
    }), { ok: false, reason: 'invalid_credentials' });
    assert.deepEqual(await service.authenticateLoginAccount({
        loginName: 'Missing_Account',
        password: 'correct-pass',
    }), { ok: false, reason: 'invalid_credentials' });
});

test('trusted game proof creates credentials once and later begins recovery without revealing or changing the password', async (t) => {
    const { dir, store, service } = await makeService('game-proof');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));

    const opened = await service.openOrBeginAccountRecovery({
        steamId: '76561198000000102',
        steamNickname: 'Current Steam Name',
    });
    assert.equal(opened.kind, 'created');
    if (opened.kind !== 'created') throw new Error('first trusted proof should create an account');
    assert.match(opened.loginName, /^cc_[A-HJ-NP-Za-hj-km-np-z2-9]{8}$/);
    assert.match(opened.initialPassword, /^[A-HJ-NP-Za-hj-km-np-z2-9]{14}$/);
    assert.equal(JSON.stringify(store.snapshot()).includes(opened.initialPassword), false);
    assert.equal(store.snapshot().accounts[opened.identityId].passwordState, 'active');

    const passwordBeforeRecovery = store.snapshot().accounts[opened.identityId].password;
    const recovery = await service.openOrBeginAccountRecovery({
        steamId: '76561198000000102',
        steamNickname: 'Latest Steam Name',
    });
    assert.deepEqual(recovery, {
        kind: 'recovery_required',
        identityId: opened.identityId,
        loginName: opened.loginName,
    });
    assert.deepEqual(store.snapshot().accounts[opened.identityId].password, passwordBeforeRecovery);
    assert.equal(service.findIdentityBySteamId('76561198000000102')?.steamNickname, 'Latest Steam Name');
});

test('an existing account begins recovery without generating throwaway credentials', async (t) => {
    const dir = path.resolve(__dirname, '..', '..', 'runtime', `account-auth-no-throwaway-${process.pid}-${Date.now()}`);
    fs.mkdirSync(dir, { recursive: true });
    const store = new IdentityStore(path.join(dir, 'identity-store.json'));
    await store.load();
    let randomByteCalls = 0;
    const service = new LobbyIdentityService(store, {
        randomBytes: (size) => {
            randomByteCalls += 1;
            return Buffer.alloc(size, 7);
        },
    });
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    await createTestLoginAccount(service, {
        steamId: '76561198000000110', nickname: 'Existing Player', password: 'current-pass',
    });
    randomByteCalls = 0;

    const result = await service.openOrBeginAccountRecovery({
        steamId: '76561198000000110', steamNickname: 'Existing Steam Name',
    });
    assert.equal(result.kind, 'recovery_required');
    assert.equal(randomByteCalls, 0);
});

test('account recovery activates migrated credentials, invalidates the old password, and revokes existing devices', async (t) => {
    const { dir, store, service } = await makeService('recovery-complete');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const created = await createTestLoginAccount(service, {
        steamId: '76561198000000103', nickname: 'Migrated Player', password: 'old-password',
    });
    const recovery = await service.openOrBeginAccountRecovery({
        steamId: '76561198000000103', steamNickname: 'Migrated Steam Name',
    });
    assert.equal(recovery.kind, 'recovery_required');
    const accountBefore = store.snapshot().accounts[created.identity.identityId];
    const firstDevice = await service.issueDeviceToken(created.identity.identityId, 'desktop-one');
    const secondDevice = await service.issueDeviceToken(created.identity.identityId, 'desktop-two');

    const result = await service.completeAccountRecovery({
        identityId: created.identity.identityId,
        newPassword: 'replacement-pass',
    });
    assert.deepEqual(result.revocation, {
        identityId: created.identity.identityId,
        preserveSessionId: undefined,
        revokeOtherPlayerCenterSessions: true,
        revokedDeviceTokenIds: [firstDevice.tokenId, secondDevice.tokenId],
    });
    assert.equal(store.snapshot().accounts[created.identity.identityId].passwordState, 'active');
    assert.equal((await service.authenticateLoginAccount({
        loginName: accountBefore.loginName,
        password: 'replacement-pass',
    })).ok, true);
    assert.deepEqual(await service.authenticateLoginAccount({
        loginName: accountBefore.loginName,
        password: 'old-password',
    }), { ok: false, reason: 'invalid_credentials' });
    assert.equal((await service.authenticatePlayerCenterDeviceToken(firstDevice.rawToken)).reason, 'revoked');
    assert.equal((await service.authenticatePlayerCenterDeviceToken(secondDevice.rawToken)).reason, 'revoked');
});

test('changing the login name requires the current password and preserves only the current device', async (t) => {
    const { dir, service } = await makeService('change-login-name');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const provisioned = await createTestLoginAccount(service, {
        steamId: '76561198000000104', nickname: 'Rename Player', password: 'current-pass',
    });
    const identityId = provisioned.identity.identityId;
    const oldLoginName = service.getLoginAccount(identityId)!.loginName;
    const currentDevice = await service.issueDeviceToken(identityId, 'current-device');
    const otherDevice = await service.issueDeviceToken(identityId, 'other-device');

    await assert.rejects(service.changeLoginName({
        identityId,
        currentPassword: 'wrong-pass',
        newLoginName: 'Renamed_Account',
        currentSessionId: 'current-web-session',
        currentDeviceTokenId: currentDevice.tokenId,
    }), /current_password_incorrect/);
    assert.equal(service.getLoginAccount(identityId)?.loginName, oldLoginName);

    const changed = await service.changeLoginName({
        identityId,
        currentPassword: 'current-pass',
        newLoginName: 'Renamed_Account',
        currentSessionId: 'current-web-session',
        currentDeviceTokenId: currentDevice.tokenId,
    });
    assert.equal(changed.account.loginName, 'Renamed_Account');
    assert.deepEqual(changed.revocation, {
        identityId,
        preserveSessionId: 'current-web-session',
        revokeOtherPlayerCenterSessions: true,
        revokedDeviceTokenIds: [otherDevice.tokenId],
    });
    assert.deepEqual(await service.authenticateLoginAccount({
        loginName: oldLoginName,
        password: 'current-pass',
    }), { ok: false, reason: 'invalid_credentials' });
    assert.equal((await service.authenticateLoginAccount({
        loginName: 'Renamed_Account',
        password: 'current-pass',
    })).ok, true);
    assert.equal((await service.authenticatePlayerCenterDeviceToken(currentDevice.rawToken)).ok, true);
    assert.equal((await service.authenticatePlayerCenterDeviceToken(otherDevice.rawToken)).reason, 'revoked');
});

test('changing the password invalidates the old password and returns the same preserve-current revocation boundary', async (t) => {
    const { dir, service } = await makeService('change-password');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const provisioned = await createTestLoginAccount(service, {
        steamId: '76561198000000105', nickname: 'Password Player', password: 'current-pass',
    });
    const identityId = provisioned.identity.identityId;
    const loginName = service.getLoginAccount(identityId)!.loginName;
    const currentDevice = await service.issueDeviceToken(identityId, 'current-device');
    const otherDevice = await service.issueDeviceToken(identityId, 'other-device');

    await assert.rejects(service.changeAccountPassword({
        identityId,
        currentPassword: 'wrong-pass',
        newPassword: 'replacement-pass',
        currentSessionId: 'current-web-session',
        currentDeviceTokenId: currentDevice.tokenId,
    }), /current_password_incorrect/);
    assert.equal((await service.authenticateLoginAccount({ loginName, password: 'current-pass' })).ok, true);
    assert.equal((await service.authenticatePlayerCenterDeviceToken(otherDevice.rawToken)).ok, true);

    const changed = await service.changeAccountPassword({
        identityId,
        currentPassword: 'current-pass',
        newPassword: 'replacement-pass',
        currentSessionId: 'current-web-session',
        currentDeviceTokenId: currentDevice.tokenId,
    });
    assert.deepEqual(changed.revocation, {
        identityId,
        preserveSessionId: 'current-web-session',
        revokeOtherPlayerCenterSessions: true,
        revokedDeviceTokenIds: [otherDevice.tokenId],
    });
    assert.deepEqual(await service.authenticateLoginAccount({
        loginName,
        password: 'current-pass',
    }), { ok: false, reason: 'invalid_credentials' });
    assert.equal((await service.authenticateLoginAccount({ loginName, password: 'replacement-pass' })).ok, true);
    assert.equal((await service.authenticatePlayerCenterDeviceToken(currentDevice.rawToken)).ok, true);
    assert.equal((await service.authenticatePlayerCenterDeviceToken(otherDevice.rawToken)).reason, 'revoked');
});

test('account HTTP login uses verified email and unifies credential errors', async (t) => {
    const runtime = await startAuthServer('http-login');
    t.after(runtime.close);
    const provisioned = await createTestLoginAccount(runtime.service, {
        steamId: '76561198000000106', nickname: 'HTTP Account', password: 'correct-pass',
    });
    const account = await runtime.service.bindVerifiedEmail({identityId: provisioned.identity.identityId, email:'http@example.com', currentPassword:'correct-pass'});

    const missing = await postJson(`${runtime.baseUrl}/api/account-auth/login`, {
        email: 'missing@example.com', password: 'correct-pass',
    });
    const wrong = await postJson(`${runtime.baseUrl}/api/account-auth/login`, {
        email: account.email, password: 'wrong-pass',
    });
    assert.deepEqual({ status: missing.status, error: missing.body.error }, { status: 401, error: 'invalid_credentials' });
    assert.deepEqual({ status: wrong.status, error: wrong.body.error }, { status: 401, error: 'invalid_credentials' });

    const success = await postJson(`${runtime.baseUrl}/api/account-auth/login`, {
        email: account.email, password: 'correct-pass',
    });
    assert.equal(success.status, 200);
    assert.deepEqual(Object.keys(success.body).sort(), [
        'sessionBootstrapExpiresAt', 'sessionBootstrapTicket', 'success',
    ]);
    assert.equal(JSON.stringify(success.body).match(/password|steamId|membership|socket/i), null);
    assert.deepEqual(runtime.bootstrapTickets.consume(success.body.sessionBootstrapTicket), {
        identityId: provisioned.identity.identityId,
        accountUpdatedAt: account.updatedAt,
    });
    assert.equal(runtime.bootstrapTickets.consume(success.body.sessionBootstrapTicket), undefined);

});

test('account HTTP failures are limited by IP and normalized email', async (t) => {
    class RecordingGuard extends AccountLoginGuard {
        readonly recordedKeys: string[][] = [];

        override recordFailure(keys: string[]) {
            this.recordedKeys.push([...keys]);
            return super.recordFailure(keys);
        }
    }
    const guard = new RecordingGuard();
    const runtime = await startAuthServer('http-rate-limit', undefined, guard);
    t.after(runtime.close);

    let latest: Awaited<ReturnType<typeof postJson>> | undefined;
    for (let attempt = 1; attempt <= 10; attempt += 1) {
        latest = await postJson(`${runtime.baseUrl}/api/account-auth/login`, {
            email: 'CaseSensitive@example.com', password: 'wrong-pass',
        });
    }
    assert.equal(latest?.status, 429);
    assert.equal(latest?.body.error, 'rate_limited');
    assert.equal(typeof latest?.body.retryAt, 'number');
    assert.equal(guard.recordedKeys[0].some((key) => key.startsWith('ip:')), true);
    assert.equal(guard.recordedKeys[0].includes('account:casesensitive@example.com'), true);
    assert.equal(guard.recordedKeys[0].includes('account:CaseSensitive@example.com'), false);
});

test('retired game-code recovery endpoints cannot consume proof or create an account', async (t) => {
    let consumed = false;
    const runtime = await startAuthServer('retired-recovery', () => { consumed = true; return {steamId:'76561198000000107',name:'Player'}; });
    t.after(runtime.close);
    for (const endpoint of ['game-code', 'complete']) {
        const result = await postJson(`${runtime.baseUrl}/api/account-recovery/${endpoint}`, {gameCode:'CODE',newPassword:'replacement-pass',confirmPassword:'replacement-pass'});
        assert.equal(result.status, 410);
        assert.equal(result.body.error, 'endpoint_retired');
    }
    assert.equal(consumed, false);
    assert.equal(runtime.service.findIdentityBySteamId('76561198000000107'), undefined);
});
