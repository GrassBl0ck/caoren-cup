import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { IdentityStore } from './identity-store';
import { LobbyIdentityService } from './identity-service';
import { createTestLoginAccount } from './test-account-helper';

const DAY = 24 * 60 * 60 * 1000;

const makeService = async (name: string, nowRef = { value: 1_000 }) => {
    const dir = path.resolve(__dirname, '..', '..', 'runtime', `identity-test-${name}-${process.pid}-${Date.now()}`);
    fs.mkdirSync(dir, { recursive: true });
    const store = new IdentityStore(path.join(dir, 'identity-store.json'));
    await store.load();
    let counter = 1;
    const service = new LobbyIdentityService(store, {
        now: () => nowRef.value,
        randomBytes: (size) => Buffer.alloc(size, counter++),
    });
    return { dir, store, service, nowRef };
};

test('player-center device authentication never creates match membership and enforces expiry', async (t) => {
    const nowRef = { value: 10_000 };
    const { dir, service } = await makeService('player-center-token', nowRef);
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const created = await createTestLoginAccount(service, {
        steamId: '76561198000000901', nickname: 'Desktop Player', password: 'current-pass',
    });
    const issued = await service.issueDeviceToken(created.identity.identityId, 'desktop-device');

    const authenticated = await service.authenticatePlayerCenterDeviceToken(issued.rawToken);
    assert.equal(authenticated.ok, true);
    assert.equal(service.listMemberships('desktop-auto-login').length, 0);

    nowRef.value += 30 * DAY;
    assert.equal((await service.authenticatePlayerCenterDeviceToken(issued.rawToken)).reason, 'expired');
});

test('administrator can list and revoke individual devices', async (t) => {
    const { dir, service } = await makeService('single-revoke');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const created = await createTestLoginAccount(service, {
        steamId: '76561198000000902', nickname: 'Devices', password: 'current-pass',
    });
    const first = await service.issueDeviceToken(created.identity.identityId, 'device-one');
    const second = await service.issueDeviceToken(created.identity.identityId, 'device-two');

    assert.equal(service.listDeviceTokens(created.identity.identityId).length, 2);
    assert.equal(await service.revokeDeviceTokenById(created.identity.identityId, first.tokenId), true);
    assert.equal((await service.authenticatePlayerCenterDeviceToken(first.rawToken)).reason, 'revoked');
    assert.equal((await service.authenticatePlayerCenterDeviceToken(second.rawToken)).ok, true);
});

test('device token rotation is two-phase and supports revoking every device', async (t) => {
    const { dir, service } = await makeService('rotation');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const created = await createTestLoginAccount(service, {
        steamId: '76561198000000903', nickname: 'Rotation Player', password: 'current-pass',
    });
    const first = await service.issueDeviceToken(created.identity.identityId, 'device-a');
    const second = await service.issueDeviceToken(created.identity.identityId, 'device-b');

    const rotation = await service.beginDeviceTokenRotation(first.rawToken);
    assert.equal(rotation.ok, true);
    if (!rotation.ok) throw new Error('rotation should succeed');
    assert.equal(await service.confirmDeviceTokenRotation(rotation.rawToken), true);
    assert.equal((await service.authenticatePlayerCenterDeviceToken(rotation.rawToken)).ok, true);
    assert.equal((await service.authenticatePlayerCenterDeviceToken(first.rawToken)).reason, 'revoked');
    assert.equal(await service.revokeAllDeviceTokens(created.identity.identityId), 2);
    assert.equal((await service.authenticatePlayerCenterDeviceToken(rotation.rawToken)).reason, 'revoked');
    assert.equal((await service.authenticatePlayerCenterDeviceToken(second.rawToken)).reason, 'revoked');
});

test('trusted Steam identity survives reload and plugin presence refreshes explicit membership identity', async (t) => {
    const { dir, store, service } = await makeService('trusted-presence');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const created = await createTestLoginAccount(service, {
        steamId: '76561198000000904', nickname: 'Trusted Player', password: 'current-pass',
    });
    const joined = await service.joinPlayerCenterMatch(created.identity.identityId, 'match-session');
    assert.equal(joined.ok, true);
    if (!joined.ok) throw new Error('join should succeed');
    assert.equal(joined.membership.confirmationState, 'confirmed');
    assert.deepEqual(service.listLobbySteamIds('match-session'), ['76561198000000904']);
    await service.confirmLongTermPresence('match-session', [
        { steamId: '76561198000000904', name: 'Latest Trusted Player' },
    ]);
    assert.equal(service.findIdentityBySteamId('76561198000000904')?.steamNickname, 'Latest Trusted Player');

    await store.flush();
    const reloadedStore = new IdentityStore(path.join(dir, 'identity-store.json'));
    await reloadedStore.load();
    const reloaded = new LobbyIdentityService(reloadedStore);
    assert.equal(reloaded.findIdentityBySteamId('76561198000000904')?.identityId, created.identity.identityId);
});

test('blocked membership remains blocked in its current match', async (t) => {
    const { dir, service } = await makeService('blocked');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const created = await createTestLoginAccount(service, {
        steamId: '76561198000000905', nickname: 'Blocked', password: 'current-pass',
    });
    const joined = await service.joinPlayerCenterMatch(created.identity.identityId, 'blocked-session');
    if (!joined.ok) throw new Error('join should succeed');
    assert.equal(await service.blockMembership(joined.membership.membershipId), true);
    assert.equal(service.getMembership(joined.membership.membershipId)?.blockedAt !== undefined, true);
});
