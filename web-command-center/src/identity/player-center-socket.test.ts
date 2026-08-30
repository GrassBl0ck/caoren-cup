import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { IdentityStore } from './identity-store';
import { LobbyIdentityService } from './identity-service';
import { createTestLoginAccount } from './test-account-helper';
import { PlayerCenterSessionStore } from './player-center-session-store';
import { bindPlayerCenterSocketIdentity } from './player-center-socket';

test('valid player-center cookie binds only identityId and never creates a lobby membership or playerId', async (t) => {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'caoren-player-center-socket-'));
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const identityStore = new IdentityStore(path.join(dir, 'identity-store.json'));
    const sessionStore = new PlayerCenterSessionStore(path.join(dir, 'player-center-sessions.json'));
    await Promise.all([identityStore.load(), sessionStore.load()]);
    const service = new LobbyIdentityService(identityStore);
    const created = await createTestLoginAccount(service, {
        steamId: '76561198000000301', nickname: 'Socket Steam Name', password: 'current-pass',
    });
    const account = service.getLoginAccount(created.identity.identityId)!;
    const session = await sessionStore.create(created.identity.identityId, account.updatedAt);
    const socketData: Record<string, unknown> = {};

    const result = await bindPlayerCenterSocketIdentity({
        cookieHeader: `other=value; caoren_player_center=${encodeURIComponent(session.rawToken)}`,
        socketData,
        sessionStore,
        service,
    });

    assert.equal(result, 'authenticated');
    assert.equal(socketData.identityId, created.identity.identityId);
    assert.equal(socketData.playerCenterSessionId, session.session.sessionId);
    assert.equal(socketData.playerId, undefined);
    assert.equal(Object.keys(identityStore.snapshot().memberships).length, 0);
});

test('disabled or version-stale account does not bind identityId and revokes the web session', async (t) => {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'caoren-player-center-socket-invalid-'));
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const identityStore = new IdentityStore(path.join(dir, 'identity-store.json'));
    const sessionStore = new PlayerCenterSessionStore(path.join(dir, 'player-center-sessions.json'));
    await Promise.all([identityStore.load(), sessionStore.load()]);
    const service = new LobbyIdentityService(identityStore);
    const created = await createTestLoginAccount(service, {
        steamId: '76561198000000302', nickname: 'Disabled Socket', password: 'current-pass',
    });
    const account = service.getLoginAccount(created.identity.identityId)!;
    const session = await sessionStore.create(created.identity.identityId, account.updatedAt);
    await service.setLoginAccountEnabled(created.identity.identityId, false);
    const socketData: Record<string, unknown> = {};

    const result = await bindPlayerCenterSocketIdentity({
        cookieHeader: `caoren_player_center=${session.rawToken}`,
        socketData,
        sessionStore,
        service,
    });

    assert.equal(result, 'invalid');
    assert.equal(socketData.identityId, undefined);
    assert.equal(await sessionStore.use(session.rawToken), undefined);
});

test('connection without a player-center cookie remains available to the administrator flow', async (t) => {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'caoren-player-center-socket-missing-'));
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const identityStore = new IdentityStore(path.join(dir, 'identity-store.json'));
    const sessionStore = new PlayerCenterSessionStore(path.join(dir, 'player-center-sessions.json'));
    await Promise.all([identityStore.load(), sessionStore.load()]);
    const socketData: Record<string, unknown> = { playerId: null };
    const result = await bindPlayerCenterSocketIdentity({
        cookieHeader: undefined,
        socketData,
        sessionStore,
        service: new LobbyIdentityService(identityStore),
    });
    assert.equal(result, 'missing');
    assert.deepEqual(socketData, { playerId: null });
});
