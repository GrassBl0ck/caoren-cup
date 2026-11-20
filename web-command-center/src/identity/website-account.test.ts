import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { IdentityStore } from './identity-store';
import { LobbyIdentityService } from './identity-service';
import { createTestLoginAccount } from './test-account-helper';

async function websiteAccount() {
    const root = path.resolve(__dirname, '../../runtime');
    const dir = fs.mkdtempSync(path.join(root, 'website-account-'));
    const store = new IdentityStore(path.join(dir, 'identity-store.json'));
    await store.load();
    const service = new LobbyIdentityService(store);
    const created = await createTestLoginAccount(service, {
        steamId: '76561198000009931', nickname: 'Website Player', password: 'test-password-123',
    });
    await store.mutate((data) => {
        delete data.identities[created.identity.identityId].steamId;
        delete data.identities[created.identity.identityId].steamNickname;
        delete data.identities[created.identity.identityId].gameBoundAt;
        Object.assign(data.accounts[created.identity.identityId], {
            email: 'player@example.com', emailVerifiedAt: Date.now(),
        });
    });
    return { root, dir, store, service, ...created };
}

test('a verified website account can log in without entering either game and survives reload', async () => {
    const runtime = await websiteAccount();
    const result = await runtime.service.authenticateLoginAccount({
        email: 'player@example.com', password: runtime.password,
    });
    assert.equal(result.ok, true);
    const reloaded = new IdentityStore(runtime.store.filePath);
    await reloaded.load();
    assert.equal(reloaded.snapshot().accounts[runtime.identity.identityId].identityId, runtime.identity.identityId);
    assert.equal(reloaded.snapshot().identities[runtime.identity.identityId].steamId, undefined);
});

test('a formal room admits pending binding and verifies the same membership after binding', async () => {
    const runtime = await websiteAccount();
    const identityId = runtime.identity.identityId;
    const sessionId = 'formal-cs-match';
    const steamId = '76561198000009931';
    const joined = await runtime.service.joinPlayerCenterMatch(identityId, sessionId, true);
    assert.equal(joined.ok, true);
    if (!joined.ok) return;
    assert.equal(joined.membership.trustedSteamId, undefined);
    assert.deepEqual(runtime.service.checkCsBindings([steamId]), { [steamId]: false });

    await runtime.service.bindVerifiedGameIdentity(identityId, steamId, 'Website Player', sessionId);
    const [bound] = runtime.service.listMemberships(sessionId);
    assert.equal(bound.membershipId, joined.membership.membershipId);
    assert.equal(bound.trustedSteamId, steamId);
    assert.equal(bound.confirmationState, 'confirmed');
    assert.deepEqual(runtime.service.checkCsBindings([steamId, 'junk']), { [steamId]: true });
});

test('a website account without a trusted Steam binding cannot join the CS match', async () => {
    const runtime = await websiteAccount();
    const result = await runtime.service.joinPlayerCenterMatch(runtime.identity.identityId, 'test-cs-match');
    assert.equal(result.ok, false);
    assert.equal(result.reason, 'game_binding_required');
    assert.equal(runtime.service.listMemberships('test-cs-match').length, 0);
});
