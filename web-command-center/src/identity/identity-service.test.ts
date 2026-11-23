import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import jpeg from 'jpeg-js';
import { PNG } from 'pngjs';
import { GameAvatars, validateAvatarJpeg } from '../game-avatars';
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

test('verified additional Steam IDs persist, belong to one account and do not replace the selected match identity', async (t) => {
    const { dir, service } = await makeService('multiple-steam-ids');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const primary = '76561198000000701';
    const secondary = '76561198000000702';
    const other = '76561198000000703';
    const { identity } = await createTestLoginAccount(service, {
        steamId: primary, nickname: 'Cup player', password: 'current-pass',
    });
    const otherAccount = await createTestLoginAccount(service, {
        steamId: other, nickname: 'Other player', password: 'current-pass',
    });
    await service.linkVerifiedSteamIdentity(identity.identityId, secondary);
    await service.linkVerifiedSteamIdentity(identity.identityId, secondary);
    await assert.rejects(service.linkVerifiedSteamIdentity(otherAccount.identity.identityId, secondary), /steam_identity_in_use/);
    const joined = await service.joinPlayerCenterMatch(identity.identityId, 'desktop-room', false, secondary);
    assert.equal(joined.ok, true);
    if (!joined.ok) return;
    assert.equal(joined.membership.trustedSteamId, secondary);
    assert.deepEqual(await service.joinPlayerCenterMatch(identity.identityId, 'desktop-room', false, primary),
        { ok: false, reason: 'match_steam_locked' });
    assert.deepEqual(await service.joinPlayerCenterMatch(identity.identityId, 'desktop-room', false, other),
        { ok: false, reason: 'game_binding_required' });
    await service.confirmLongTermPresence('desktop-room', [{steamId: primary, name: 'Primary Steam'}]);
    assert.deepEqual(service.listLobbySteamIds('desktop-room'), [secondary]);
    const store = new IdentityStore(path.join(dir, 'identity-store.json'));
    await store.load();
    const reloaded = new LobbyIdentityService(store);
    assert.equal(reloaded.findIdentityBySteamId(secondary)?.identityId, identity.identityId);
    assert.equal(reloaded.getIdentity(identity.identityId)?.steamId, primary);
    assert.deepEqual(reloaded.getIdentity(identity.identityId)?.additionalSteamIds, [secondary]);
    assert.deepEqual(reloaded.checkCsBindings([primary, secondary]), { [primary]: true, [secondary]: true });
});

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


test('game registration binds Steam, hashes the password, and permits password login', async (t) => {
    const { dir, service } = await makeService('game-registration');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const registered = await service.registerAccountFromGame({
        steamId: '76561198000000906',
        steamNickname: 'In Game Player',
        loginName: 'player_906',
        password: 'current-pass',
    });
    assert.equal(registered.identity.gameBoundAt, 1_000);
    assert.equal(registered.account.password?.hash.includes('current-pass'), false);
    assert.equal((await service.authenticateLoginAccount({ loginName: 'player_906', password: 'current-pass' })).ok, true);
    await assert.rejects(
        service.registerAccountFromGame({
            steamId: '76561198000000906',
            steamNickname: 'In Game Player',
            loginName: 'player_906b',
            password: 'current-pass',
        }),
        /account_exists/,
    );
});

test('avatar preferences survive reload and export the selected image for each verified Steam ID', async t => {
    const { dir, service } = await makeService('desktop-avatars');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const primary = '76561198000000951', secondary = '76561198000000952';
    const { identity } = await createTestLoginAccount(service, { steamId: primary, nickname: 'Avatar' });
    const id = identity.identityId;
    await service.linkVerifiedSteamIdentity(id, secondary);
    const red = jpeg.encode({ width: 4, height: 2, data: Buffer.from(Array.from({ length: 8 }, () => [245, 4, 6, 255]).flat()) }).data.toString('base64');
    const green = jpeg.encode({ width: 4, height: 2, data: Buffer.from(Array.from({ length: 8 }, () => [4, 240, 5, 255]).flat()) }).data.toString('base64');
    const gameDir = path.join(dir, 'avatars');
    const game = new GameAvatars(gameDir);
    const read = (key: string) => fs.readFileSync(path.join(gameDir, key + '.png'));
    await game.initialize(service.avatarIdentities());
    const logo = read('default');
    assert.deepEqual(read(primary), logo);
    assert.deepEqual(read(secondary), logo);
    await assert.rejects(service.rememberSteamAvatar(id, '76561198000000953', red), /game_binding_required/);
    assert.throws(() => validateAvatarJpeg(Buffer.from([255, 216, 255, 0, 255, 217]).toString('base64')), /avatar_invalid/);
    await game.sync(await service.rememberSteamAvatar(id, primary, red));
    const steam = read(primary);
    assert.notDeepEqual(steam, logo);
    assert.ok(steam.length <= 16384);
    const image = PNG.sync.read(steam);
    assert.equal(image.width, 64);
    assert.equal(image.height, 64);
    assert.ok(image.data[0] > 220 && image.data[1] < 20);
    assert.deepEqual(read(secondary), logo);
    await game.sync(await service.updateDesktopProfile(id, 'Logo', undefined, 'logo'));
    assert.deepEqual(read(primary), logo);
    assert.equal((await service.updateDesktopProfile(id, 'Nickname only')).avatarLogo, true);
    await game.sync(await service.rememberSteamAvatar(id, secondary, green));
    assert.deepEqual(read(secondary), logo);
    await game.sync(await service.updateDesktopProfile(id, 'Custom', green));
    assert.deepEqual(read(primary), read(secondary));
    assert.notDeepEqual(read(primary), steam);
    assert.equal((await service.updateDesktopProfile(id, 'Legacy upload')).avatarJpeg, green);
    await game.sync(await service.updateDesktopProfile(id, 'Steam', undefined, 'steam'));
    assert.deepEqual(read(primary), steam);
    assert.notDeepEqual(read(secondary), steam);
    await game.sync(await service.rememberSteamAvatar(id, primary, null));
    assert.deepEqual(read(primary), logo);
    await game.sync(await service.rememberSteamAvatar(id, primary, red));
    assert.deepEqual(read(primary), steam);
    const store = new IdentityStore(path.join(dir, 'identity-store.json'));
    await store.load();
    const restored = store.snapshot().identities[id];
    assert.equal(restored.steamAvatarJpegs?.[primary], red);
    assert.equal(restored.steamAvatarJpegs?.[secondary], green);
    assert.equal(restored.avatarLogo, undefined);
    assert.equal(restored.avatarJpeg, undefined);
    await game.sync({ ...restored, avatarJpeg: '/9j/AP/Z' });
    assert.deepEqual(read(primary), logo);
});
