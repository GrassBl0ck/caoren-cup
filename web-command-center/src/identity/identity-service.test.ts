import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { IdentityStore } from './identity-store';
import { LobbyIdentityService } from './identity-service';

const DAY = 24 * 60 * 60 * 1000;

const makeRuntimeDir = (name: string) => {
    const dir = path.resolve(__dirname, '..', '..', 'runtime', `identity-test-${name}-${process.pid}-${Date.now()}`);
    fs.mkdirSync(dir, { recursive: true });
    return dir;
};

const makeService = async (name: string, nowRef = { value: 1_000 }) => {
    const dir = makeRuntimeDir(name);
    const store = new IdentityStore(path.join(dir, 'identity-store.json'));
    await store.load();
    let counter = 1;
    const service = new LobbyIdentityService(store, {
        now: () => nowRef.value,
        randomBytes: (size) => Buffer.alloc(size, counter++),
    });
    return { dir, store, service, nowRef };
};

test('temporary identity is persisted with a pending Steam claim', async (t) => {
    const { dir, service } = await makeService('temporary');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));

    const membership = await service.createTemporaryMembership({
        sessionId: 'session-1',
        nickname: 'New Player',
        steamClaim: { steamId: '76561198000000001', personaName: 'Public Name' },
    });

    assert.equal(membership.identityLevel, 'temporary');
    assert.equal(membership.confirmationState, 'pending');
    assert.equal(membership.claimedSteamId, '76561198000000001');
});

test('temporary nickname rejects HTML and control characters', async (t) => {
    const { dir, service } = await makeService('nickname-safety');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));

    await assert.rejects(
        service.createTemporaryMembership({ sessionId: 'session-safe-name', nickname: '<img src=x onerror=alert(1)>' }),
        /nickname_invalid/,
    );
});

test('trusted challenge promotes only the matching SteamID', async (t) => {
    const { dir, service } = await makeService('challenge');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const membership = await service.createTemporaryMembership({
        sessionId: 'session-2',
        nickname: 'Pending Player',
        steamClaim: { steamId: '76561198000000002', personaName: 'Pending' },
    });
    const [challenge] = await service.getConfirmationChallenges('session-2', [
        { steamId: '76561198000000002', name: 'Trusted Player' },
    ]);

    assert.ok(challenge);
    const mismatch = await service.confirmChallenge(membership.membershipId, challenge.code, '76561198000000999');
    assert.equal(mismatch.ok, false);
    assert.equal(mismatch.reason, 'steam_mismatch');

    const confirmed = await service.confirmChallenge(membership.membershipId, challenge.code, '76561198000000002');
    assert.equal(confirmed.ok, true);
    assert.equal(confirmed.membership?.identityLevel, 'longTerm');
    assert.equal(confirmed.membership?.confirmationState, 'confirmed');
    assert.equal(confirmed.identity?.steamId, '76561198000000002');
});

test('device token auto login, expiry and revocation are enforced', async (t) => {
    const nowRef = { value: 5_000 };
    const { dir, service } = await makeService('tokens', nowRef);
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const membership = await service.createTemporaryMembership({
        sessionId: 'session-3',
        nickname: 'Token Player',
        steamClaim: { steamId: '76561198000000003', personaName: 'Token Player' },
    });
    const promoted = await service.confirmTrustedIdentity(membership.membershipId, '76561198000000003', 'Token Player');
    assert.equal(promoted.ok, true);
    const issued = await service.issueDeviceToken(promoted.identity!.identityId, 'device-1');

    const authenticated = await service.authenticateDeviceToken(issued.rawToken, {
        sessionId: 'session-4',
        steamClaim: { steamId: '76561198000000003', personaName: 'Token Player' },
    });
    assert.equal(authenticated.ok, true);
    assert.equal(authenticated.membership?.identityLevel, 'longTerm');

    await service.revokeDeviceToken(issued.rawToken);
    assert.equal((await service.authenticateDeviceToken(issued.rawToken, { sessionId: 'session-4' })).reason, 'revoked');

    const expiring = await service.issueDeviceToken(promoted.identity!.identityId, 'device-2');
    nowRef.value += 91 * DAY;
    assert.equal((await service.authenticateDeviceToken(expiring.rawToken, { sessionId: 'session-5' })).reason, 'expired');
});

test('administrator can list and revoke one device without revoking another', async (t) => {
    const { dir, service } = await makeService('single-revoke');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const membership = await service.createTemporaryMembership({ sessionId: 'single-revoke-session', nickname: 'Devices' });
    const promoted = await service.confirmTrustedIdentity(membership.membershipId, '76561198000000012', 'Devices');
    const first = await service.issueDeviceToken(promoted.identity!.identityId, 'device-one');
    const second = await service.issueDeviceToken(promoted.identity!.identityId, 'device-two');

    const devices = service.listDeviceTokens(promoted.identity!.identityId);
    assert.equal(devices.length, 2);
    assert.equal(await service.revokeDeviceTokenById(promoted.identity!.identityId, first.tokenId), true);
    assert.equal((await service.authenticateDeviceToken(first.rawToken, { sessionId: 'after-revoke' })).reason, 'revoked');
    assert.equal((await service.authenticateDeviceToken(second.rawToken, { sessionId: 'after-revoke' })).ok, true);
});

test('device token rotates in two phases and supports revoking every device', async (t) => {
    const nowRef = { value: 8_000 };
    const { dir, service } = await makeService('rotation', nowRef);
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const membership = await service.createTemporaryMembership({
        sessionId: 'session-rotation',
        nickname: 'Rotation Player',
        steamClaim: { steamId: '76561198000000008' },
    });
    const promoted = await service.confirmTrustedIdentity(membership.membershipId, '76561198000000008', 'Rotation Player');
    const first = await service.issueDeviceToken(promoted.identity!.identityId, 'device-a');
    const second = await service.issueDeviceToken(promoted.identity!.identityId, 'device-b');

    const rotation = await service.beginDeviceTokenRotation(first.rawToken);
    assert.equal(rotation.ok, true);
    assert.equal(await service.confirmDeviceTokenRotation(rotation.rawToken), true);
    assert.equal((await service.authenticateDeviceToken(rotation.rawToken, { sessionId: 'session-rotation-2' })).ok, true);
    assert.equal((await service.authenticateDeviceToken(first.rawToken, { sessionId: 'session-rotation-2' })).reason, 'revoked');

    const interruptedRotation = await service.beginDeviceTokenRotation(rotation.rawToken);
    assert.equal(interruptedRotation.ok, true);
    assert.equal((await service.authenticateDeviceToken(interruptedRotation.rawToken, { sessionId: 'session-rotation-3' })).ok, true);
    assert.equal((await service.authenticateDeviceToken(rotation.rawToken, { sessionId: 'session-rotation-3' })).reason, 'revoked');

    assert.equal(await service.revokeAllDeviceTokens(promoted.identity!.identityId), 2);
    assert.equal((await service.authenticateDeviceToken(interruptedRotation.rawToken, { sessionId: 'session-rotation-2' })).reason, 'revoked');
    assert.equal((await service.authenticateDeviceToken(second.rawToken, { sessionId: 'session-rotation-2' })).reason, 'revoked');
});

test('confirmation challenge is invalidated after five wrong codes', async (t) => {
    const { dir, service } = await makeService('challenge-attempts');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const membership = await service.createTemporaryMembership({
        sessionId: 'session-attempts',
        nickname: 'Attempts Player',
        steamClaim: { steamId: '76561198000000009' },
    });
    const [challenge] = await service.getConfirmationChallenges('session-attempts', [{ steamId: '76561198000000009' }]);
    for (let attempt = 0; attempt < 5; attempt++) {
        assert.equal((await service.confirmChallenge(membership.membershipId, 'WRONG1', '76561198000000009')).reason, 'challenge_invalid');
    }
    assert.equal(
        (await service.confirmChallenge(membership.membershipId, challenge.code, '76561198000000009')).reason,
        'challenge_expired',
    );
});

test('left and blocked temporary memberships never receive confirmation challenges', async (t) => {
    const { dir, service } = await makeService('inactive-challenges');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const left = await service.createTemporaryMembership({
        sessionId: 'inactive-session',
        nickname: 'Left Player',
        steamClaim: { steamId: '76561198000000032' },
    });
    const blocked = await service.createTemporaryMembership({
        sessionId: 'inactive-session',
        nickname: 'Blocked Player',
        steamClaim: { steamId: '76561198000000033' },
    });
    await service.leaveMembership(left.membershipId);
    await service.blockMembership(blocked.membershipId);

    assert.deepEqual(await service.getConfirmationChallenges('inactive-session', [
        { steamId: '76561198000000032' },
        { steamId: '76561198000000033' },
    ]), []);
});

test('rejoining with the same SteamID issues a challenge only for the active membership', async (t) => {
    const { dir, service } = await makeService('rejoin-challenge');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const oldMembership = await service.createTemporaryMembership({
        sessionId: 'rejoin-session',
        nickname: 'Old Join',
        steamClaim: { steamId: '76561198000000034' },
    });
    await service.leaveMembership(oldMembership.membershipId);
    const activeMembership = await service.createTemporaryMembership({
        sessionId: 'rejoin-session',
        nickname: 'New Join',
        steamClaim: { steamId: '76561198000000034' },
    });

    const challenges = await service.getConfirmationChallenges('rejoin-session', [{ steamId: '76561198000000034' }]);
    assert.deepEqual(challenges.map((challenge) => challenge.membershipId), [activeMembership.membershipId]);
});

test('long-term identities survive store reload', async (t) => {
    const { dir, store, service } = await makeService('restart');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const membership = await service.createTemporaryMembership({
        sessionId: 'session-6',
        nickname: 'Persistent Player',
        steamClaim: { steamId: '76561198000000004', personaName: 'Persistent' },
    });
    await service.confirmTrustedIdentity(membership.membershipId, '76561198000000004', 'Persistent');
    await store.flush();

    const reloadedStore = new IdentityStore(path.join(dir, 'identity-store.json'));
    await reloadedStore.load();
    const reloaded = new LobbyIdentityService(reloadedStore);

    assert.equal(reloaded.findIdentityBySteamId('76561198000000004')?.displayName, 'Persistent Player');
});

test('identity store restores the previous snapshot when the primary file is corrupt', async (t) => {
    const { dir, store, service } = await makeService('previous');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    await service.createTemporaryMembership({ sessionId: 'session-a', nickname: 'First Snapshot' });
    await service.createTemporaryMembership({ sessionId: 'session-b', nickname: 'Second Snapshot' });
    await store.flush();
    fs.writeFileSync(path.join(dir, 'identity-store.json'), '{broken', 'utf8');

    const recovered = new IdentityStore(path.join(dir, 'identity-store.json'));
    await recovered.load();

    assert.equal(Object.keys(recovered.snapshot().memberships).length >= 1, true);
});

test('legacy trusted game-code path confirms an existing temporary membership', async (t) => {
    const { dir, service } = await makeService('legacy');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const membership = await service.createTemporaryMembership({
        sessionId: 'session-7',
        nickname: 'Legacy Player',
    });

    const result = await service.confirmTrustedIdentity(membership.membershipId, '76561198000000005', 'Legacy Steam Name');

    assert.equal(result.ok, true);
    assert.equal(result.membership?.confirmationState, 'confirmed');
    assert.equal(result.identity?.steamId, '76561198000000005');
});

test('legacy trusted flow refuses to replace an existing permanent Steam binding', async (t) => {
    const { dir, service } = await makeService('legacy-rebind');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const membership = await service.createTemporaryMembership({ sessionId: 'legacy-rebind-session', nickname: 'Bound Player' });
    const first = await service.confirmTrustedIdentity(membership.membershipId, '76561198000000021', 'Bound Player');
    assert.equal(first.ok, true);

    const rebound = await service.confirmTrustedIdentity(membership.membershipId, '76561198000000022', 'Different Steam');

    assert.equal(rebound.ok, false);
    assert.equal(rebound.reason, 'steam_mismatch');
    assert.equal(service.findIdentityBySteamId('76561198000000021')?.identityId, first.identity?.identityId);
    assert.equal(service.findIdentityBySteamId('76561198000000022'), undefined);
    assert.equal(service.getMembership(membership.membershipId)?.confirmationState, 'mismatch');
});

test('device login recomputes an existing membership claim and trusted presence preserves mismatch', async (t) => {
    const { dir, service } = await makeService('existing-membership-mismatch');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const initial = await service.createTemporaryMembership({ sessionId: 'old-device-session', nickname: 'Device Player' });
    const promoted = await service.confirmTrustedIdentity(initial.membershipId, '76561198000000023', 'Device Player');
    const token = await service.issueDeviceToken(promoted.identity!.identityId, 'device-mismatch');
    const firstLogin = await service.authenticateDeviceToken(token.rawToken, {
        sessionId: 'current-device-session',
        steamClaim: { steamId: '76561198000000023' },
    });
    assert.equal(firstLogin.ok, true);
    if (!firstLogin.ok) throw new Error('first device login should succeed');
    assert.equal(firstLogin.membership?.confirmationState, 'pending');

    const changedAccount = await service.authenticateDeviceToken(token.rawToken, {
        sessionId: 'current-device-session',
        steamClaim: { steamId: '76561198000000024' },
    });
    assert.equal(changedAccount.ok, true);
    if (!changedAccount.ok) throw new Error('changed-account device login should succeed');

    assert.equal(changedAccount.membership.confirmationState, 'mismatch');
    assert.equal(changedAccount.membership.confirmationReason, 'steam_mismatch');
    assert.equal(changedAccount.membership.claimedSteamId, '76561198000000024');
    assert.deepEqual(await service.confirmLongTermPresence('current-device-session', ['76561198000000023']), []);
    assert.equal(service.getMembership(changedAccount.membership.membershipId)?.confirmationState, 'mismatch');
});

test('trusted plugin presence confirms a long-term member for the current session', async (t) => {
    const { dir, service } = await makeService('longterm-presence');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const initial = await service.createTemporaryMembership({ sessionId: 'old-session', nickname: 'Returning' });
    const promoted = await service.confirmTrustedIdentity(initial.membershipId, '76561198000000006', 'Returning');
    const token = await service.issueDeviceToken(promoted.identity!.identityId, 'returning-device');
    const login = await service.authenticateDeviceToken(token.rawToken, {
        sessionId: 'new-session',
        steamClaim: { steamId: '76561198000000006' },
    });
    assert.equal(login.ok, true);
    if (!login.ok) throw new Error('device login should succeed');
    assert.equal(login.membership?.confirmationState, 'pending');

    const updated = await service.confirmLongTermPresence('new-session', ['76561198000000006']);

    assert.equal(updated[0]?.confirmationState, 'confirmed');
});

test('blocked membership remains marked for the current session', async (t) => {
    const { dir, service } = await makeService('blocked');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const membership = await service.createTemporaryMembership({ sessionId: 'blocked-session', nickname: 'Blocked' });

    assert.equal(await service.blockMembership(membership.membershipId), true);
    assert.equal(service.getMembership(membership.membershipId)?.blockedAt !== undefined, true);
});

test('carrying a member into a new session creates a fresh pending confirmation', async (t) => {
    const { dir, service } = await makeService('carry');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const oldMembership = await service.createTemporaryMembership({
        sessionId: 'old-session',
        nickname: 'Carry Player',
        steamClaim: { steamId: '76561198000000010' },
    });
    await service.confirmTrustedIdentity(oldMembership.membershipId, '76561198000000010', 'Carry Player');

    const carried = await service.carryMembershipToSession(oldMembership.membershipId, 'new-session');

    assert.notEqual(carried?.membershipId, oldMembership.membershipId);
    assert.equal(carried?.sessionId, 'new-session');
    assert.equal(carried?.identityLevel, 'longTerm');
    assert.equal(carried?.confirmationState, 'pending');
});

test('temporary identities older than thirty days are pruned without deleting long-term identities', async (t) => {
    const nowRef = { value: 1_000 };
    const { dir, store, service } = await makeService('prune', nowRef);
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    await service.createTemporaryMembership({ sessionId: 'old-temp', nickname: 'Old Temp' });
    const permanent = await service.createTemporaryMembership({ sessionId: 'old-longterm', nickname: 'Permanent' });
    await service.confirmTrustedIdentity(permanent.membershipId, '76561198000000011', 'Permanent');
    nowRef.value += 31 * DAY;

    const result = await service.pruneTemporaryRecords();

    assert.deepEqual(result, { memberships: 1, identities: 1 });
    assert.equal(Object.values(store.snapshot().identities).some((identity) => identity.steamId === '76561198000000011'), true);
});

test('administrator provisions a fixed account without storing the plaintext password', async (t) => {
    const { dir, store, service } = await makeService('fixed-provision');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));

    const result = await service.createOrUpdateFixedAccount({
        steamId: '76561198000000040',
        nickname: '固定成员甲',
        password: 'initial-pass',
    });

    assert.equal(result.created, true);
    assert.equal(result.identity.steamId, '76561198000000040');
    assert.equal(result.identity.displayName, '固定成员甲');
    assert.equal(result.identity.fixedAccount?.enabled, true);
    assert.equal(JSON.stringify(store.snapshot()).includes('initial-pass'), false);
});

test('provisioning an existing long-term Steam identity sets its password without duplication', async (t) => {
    const { dir, store, service } = await makeService('fixed-existing');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const draft = await service.createTemporaryMembership({ sessionId: 'existing-session', nickname: 'Old Name' });
    const promoted = await service.confirmTrustedIdentity(draft.membershipId, '76561198000000041', 'Old Name');
    const identityCount = Object.keys(store.snapshot().identities).length;

    const result = await service.createOrUpdateFixedAccount({
        steamId: '76561198000000041',
        nickname: '管理员昵称',
        password: 'another-pass',
        sessionId: 'existing-session',
    });

    assert.equal(result.created, false);
    assert.equal(result.identity.identityId, promoted.identity?.identityId);
    assert.equal(result.identity.displayName, '管理员昵称');
    assert.equal(Object.keys(store.snapshot().identities).length, identityCount);
    assert.equal(service.getMembership(draft.membershipId)?.nickname, '管理员昵称');
});

test('fixed account provisioning validates SteamID64 and nickname strictly', async (t) => {
    const { dir, service } = await makeService('fixed-validation');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));

    await assert.rejects(service.createOrUpdateFixedAccount({
        steamId: '7656119-8000000042', nickname: 'Bad Steam', password: 'initial-pass',
    }), /steam_id_invalid/);
    await assert.rejects(service.createOrUpdateFixedAccount({
        steamId: '76561198000000042', nickname: '<b>Bad</b>', password: 'initial-pass',
    }), /nickname_invalid/);
});

test('correct fixed password creates and reuses a pending membership for the current session', async (t) => {
    const { dir, service } = await makeService('fixed-login');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    await service.createOrUpdateFixedAccount({
        steamId: '76561198000000043', nickname: 'Login Member', password: 'correct-pass',
    });

    const wrong = await service.authenticateFixedAccount({
        sessionId: 'fixed-session', steamId: '76561198000000043', password: 'wrong-pass',
    });
    assert.deepEqual(wrong, { ok: false, reason: 'password_incorrect' });

    const first = await service.authenticateFixedAccount({
        sessionId: 'fixed-session', steamId: '76561198000000043', password: 'correct-pass',
    });
    const second = await service.authenticateFixedAccount({
        sessionId: 'fixed-session', steamId: '76561198000000043', password: 'correct-pass',
    });
    assert.equal(first.ok, true);
    assert.equal(second.ok, true);
    if (!first.ok || !second.ok) throw new Error('fixed login should succeed');
    assert.equal(first.membership.membershipId, second.membership.membershipId);
    assert.equal(first.membership.identityLevel, 'longTerm');
    assert.equal(first.membership.confirmationState, 'pending');
    assert.equal(first.membership.claimedSteamId, '76561198000000043');
});

test('blocked fixed membership cannot be bypassed but a new session gets a fresh membership', async (t) => {
    const { dir, service } = await makeService('fixed-blocked');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    await service.createOrUpdateFixedAccount({
        steamId: '76561198000000044', nickname: 'Blocked Member', password: 'correct-pass',
    });
    const first = await service.authenticateFixedAccount({
        sessionId: 'blocked-session', steamId: '76561198000000044', password: 'correct-pass',
    });
    if (!first.ok) throw new Error('first login should succeed');
    await service.blockMembership(first.membership.membershipId);

    assert.deepEqual(await service.authenticateFixedAccount({
        sessionId: 'blocked-session', steamId: '76561198000000044', password: 'correct-pass',
    }), { ok: false, reason: 'blocked_for_session' });

    const next = await service.authenticateFixedAccount({
        sessionId: 'next-session', steamId: '76561198000000044', password: 'correct-pass',
    });
    assert.equal(next.ok, true);
    if (!next.ok) throw new Error('next-session login should succeed');
    assert.notEqual(next.membership.membershipId, first.membership.membershipId);
    assert.equal(next.membership.confirmationState, 'pending');
});

test('disabled account rejects login and re-enabling does not clear the current block', async (t) => {
    const { dir, service } = await makeService('fixed-disabled');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const provisioned = await service.createOrUpdateFixedAccount({
        steamId: '76561198000000045', nickname: 'Disabled Member', password: 'correct-pass',
    });
    const login = await service.authenticateFixedAccount({
        sessionId: 'disable-session', steamId: '76561198000000045', password: 'correct-pass',
    });
    if (!login.ok) throw new Error('initial login should succeed');

    const disabled = await service.setFixedAccountEnabled(provisioned.identity.identityId, false, 'disable-session');
    assert.equal(disabled?.membership?.blockedAt !== undefined, true);
    assert.deepEqual(await service.authenticateFixedAccount({
        sessionId: 'disable-session', steamId: '76561198000000045', password: 'correct-pass',
    }), { ok: false, reason: 'account_disabled' });

    await service.createOrUpdateFixedAccount({
        steamId: '76561198000000045', nickname: 'Disabled Member Renamed', password: 'replacement-pass',
        sessionId: 'disable-session',
    });
    assert.deepEqual(await service.authenticateFixedAccount({
        sessionId: 'disable-session', steamId: '76561198000000045', password: 'replacement-pass',
    }), { ok: false, reason: 'account_disabled' });

    await service.setFixedAccountEnabled(provisioned.identity.identityId, true, 'disable-session');
    assert.deepEqual(await service.authenticateFixedAccount({
        sessionId: 'disable-session', steamId: '76561198000000045', password: 'replacement-pass',
    }), { ok: false, reason: 'blocked_for_session' });
});

test('password reset invalidates the old password and survives store reload', async (t) => {
    const { dir, store, service } = await makeService('fixed-reset');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const account = await service.createOrUpdateFixedAccount({
        steamId: '76561198000000046', nickname: 'Reset Member', password: 'old-password',
    });
    await service.resetFixedAccountPassword(account.identity.identityId, 'new-password');
    await store.flush();

    const reloadedStore = new IdentityStore(path.join(dir, 'identity-store.json'));
    await reloadedStore.load();
    const reloaded = new LobbyIdentityService(reloadedStore);

    assert.deepEqual(await reloaded.authenticateFixedAccount({
        sessionId: 'reset-session', steamId: '76561198000000046', password: 'old-password',
    }), { ok: false, reason: 'password_incorrect' });
    assert.equal((await reloaded.authenticateFixedAccount({
        sessionId: 'reset-session', steamId: '76561198000000046', password: 'new-password',
    })).ok, true);
});

test('same-session nickname collision rejects fixed login and rename', async (t) => {
    const { dir, service } = await makeService('fixed-nickname-collision');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    await service.createTemporaryMembership({ sessionId: 'collision-session', nickname: 'Taken Name' });
    const account = await service.createOrUpdateFixedAccount({
        steamId: '76561198000000047', nickname: 'Taken Name', password: 'correct-pass',
    });

    assert.deepEqual(await service.authenticateFixedAccount({
        sessionId: 'collision-session', steamId: '76561198000000047', password: 'correct-pass',
    }), { ok: false, reason: 'nickname_in_use' });
    await assert.rejects(
        service.renameFixedAccount(account.identity.identityId, 'Taken Name', 'collision-session'),
        /nickname_in_use/,
    );
});

test('lobby SteamID list includes active claims and excludes left or blocked memberships', async (t) => {
    const { dir, service } = await makeService('lobby-steamids');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const active = await service.createTemporaryMembership({
        sessionId: 'list-session', nickname: 'Active', steamClaim: { steamId: '76561198000000048' },
    });
    const blocked = await service.createTemporaryMembership({
        sessionId: 'list-session', nickname: 'Blocked Claim', steamClaim: { steamId: '76561198000000049' },
    });
    const left = await service.createTemporaryMembership({
        sessionId: 'list-session', nickname: 'Left Claim', steamClaim: { steamId: '76561198000000050' },
    });
    await service.blockMembership(blocked.membershipId);
    await service.leaveMembership(left.membershipId);

    assert.deepEqual(service.listLobbySteamIds('list-session'), [active.claimedSteamId]);
});
