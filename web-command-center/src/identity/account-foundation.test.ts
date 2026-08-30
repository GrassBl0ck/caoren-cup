import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { IdentityStore } from './identity-store';
import { LobbyIdentityService } from './identity-service';
import { hashAccountPassword } from './password-auth';
import { createTestLoginAccount } from './test-account-helper';

const makeDir = (name: string) => {
    const dir = path.resolve(__dirname, '..', '..', 'runtime', `account-foundation-${name}-${process.pid}-${Date.now()}`);
    fs.mkdirSync(dir, { recursive: true });
    return dir;
};

const loadAccountFoundation = () => require('./account-foundation') as {
    validateLoginName(value: unknown): string;
    generateRandomLoginName(existing: ReadonlySet<string>, randomBytes?: (size: number) => Buffer): string;
    generateRandomInitialPassword(randomBytes?: (size: number) => Buffer): string;
};

test('login names are case-sensitive, strictly validated, and random names use the readable cc_ format', () => {
    const foundation = loadAccountFoundation();
    assert.equal(foundation.validateLoginName('Member_A1'), 'Member_A1');
    assert.throws(() => foundation.validateLoginName('member-a1'), /login_name_invalid/);
    assert.throws(() => foundation.validateLoginName('short'), /login_name_invalid/);
    assert.throws(() => foundation.validateLoginName('a'.repeat(21)), /login_name_invalid/);

    const existing = new Set(['cc_AAAAAAAA']);
    let calls = 0;
    const generated = foundation.generateRandomLoginName(existing, (size) => {
        calls += 1;
        return Buffer.alloc(size, calls === 1 ? 0 : 1);
    });
    assert.match(generated, /^cc_[A-HJ-NP-Za-hj-km-np-z2-9]{8}$/);
    assert.notEqual(generated, 'cc_AAAAAAAA');
    assert.equal(new Set(['CaseName', 'casename']).size, 2);
});

test('random initial passwords have fourteen readable characters with uppercase lowercase and digits', () => {
    const { generateRandomInitialPassword } = loadAccountFoundation();
    const password = generateRandomInitialPassword((size) => Buffer.from(Array.from({ length: size }, (_, index) => index)));
    assert.equal(password.length, 14);
    assert.match(password, /^[A-HJ-NP-Za-hj-km-np-z2-9]{14}$/);
    assert.match(password, /[A-HJ-NP-Z]/);
    assert.match(password, /[a-hj-km-np-z]/);
    assert.match(password, /[2-9]/);
    assert.doesNotMatch(password, /[0O1Il]/);
});

test('schema v2 migration separates accounts, invalidates old passwords, preserves trusted identity data, and clears tokens', async (t) => {
    const dir = makeDir('migration');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const file = path.join(dir, 'identity-store.json');
    const oldPassword = await hashAccountPassword('old-password', {
        now: () => 20,
        randomBytes: (size) => Buffer.alloc(size, 7),
    });
    const legacy = {
        schemaVersion: 2,
        identities: {
            trusted: {
                identityId: 'trusted', displayName: 'Web Display', steamId: '76561198000000071',
                fixedAccount: { enabled: false, password: oldPassword }, createdAt: 10, updatedAt: 20,
            },
            partial: { identityId: 'partial', displayName: 'Partial', createdAt: 11, updatedAt: 21 },
        },
        memberships: {
            older: {
                membershipId: 'older', sessionId: 'old', identityId: 'trusted', nickname: 'Old', identityLevel: 'longTerm',
                confirmationState: 'confirmed', claimedSteamId: '76561198000000071', claimPersonaName: 'Trusted Old',
                trustedSteamId: '76561198000000071', confirmedAt: 100, joinedAt: 10, updatedAt: 100,
            },
            newer: {
                membershipId: 'newer', sessionId: 'new', identityId: 'trusted', nickname: 'New', identityLevel: 'longTerm',
                confirmationState: 'confirmed', claimedSteamId: '76561198000000071', claimPersonaName: 'Trusted Latest',
                trustedSteamId: '76561198000000071', confirmedAt: 200, joinedAt: 20, updatedAt: 200,
            },
        },
        deviceTokens: {
            old: {
                tokenId: 'old', identityId: 'trusted', deviceId: 'desktop', tokenHash: '00'.repeat(32), familyId: 'family',
                status: 'active', createdAt: 10, lastUsedAt: 20, idleExpiresAt: 30, absoluteExpiresAt: 40, rotateAfter: 25,
            },
        },
    };
    fs.writeFileSync(file, JSON.stringify(legacy), 'utf8');

    const StoreWithOptions = IdentityStore as unknown as new (
        filePath: string,
        options: { randomBytes: (size: number) => Buffer },
    ) => IdentityStore;
    const store = new StoreWithOptions(file, { randomBytes: (size) => Buffer.alloc(size, 2) });
    await store.load();
    const migrated = store.snapshot() as unknown as {
        schemaVersion: number;
        identities: Record<string, { steamId?: string; steamNickname?: string; fixedAccount?: unknown }>;
        accounts: Record<string, { identityId: string; loginName: string; enabled: boolean; passwordState: string; password?: unknown }>;
        memberships: Record<string, unknown>;
        deviceTokens: Record<string, unknown>;
    };

    assert.equal(migrated.schemaVersion, 3);
    assert.equal(migrated.identities.trusted.steamId, '76561198000000071');
    assert.equal(migrated.identities.trusted.steamNickname, undefined);
    assert.equal(migrated.identities.trusted.fixedAccount, undefined);
    assert.deepEqual(migrated.accounts.partial, undefined);
    assert.match(migrated.accounts.trusted.loginName, /^cc_[A-HJ-NP-Za-hj-km-np-z2-9]{8}$/);
    assert.equal(migrated.accounts.trusted.identityId, 'trusted');
    assert.equal(migrated.accounts.trusted.enabled, false);
    assert.equal(migrated.accounts.trusted.passwordState, 'recovery_required');
    assert.equal(migrated.accounts.trusted.password, undefined);
    assert.equal(Object.keys(migrated.memberships).length, 2);
    assert.deepEqual(migrated.deviceTokens, {});
    assert.equal(JSON.parse(fs.readFileSync(file, 'utf8')).schemaVersion, 3);
});

test('migration write failure preserves the original file and the previous in-memory state', async (t) => {
    const dir = makeDir('atomic');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const file = path.join(dir, 'identity-store.json');
    let failedRenamesRemaining = 0;
    const controlledFs = new Proxy(fs, {
        get(target, property, receiver) {
            if (property === 'renameSync') {
                return (...args: Parameters<typeof fs.renameSync>) => {
                    if (failedRenamesRemaining > 0) {
                        failedRenamesRemaining -= 1;
                        throw new Error('simulated migration write failure');
                    }
                    return fs.renameSync(...args);
                };
            }
            return Reflect.get(target, property, receiver);
        },
    });
    const store = new IdentityStore(file, { fs: controlledFs });
    await store.load();
    const before = store.snapshot();
    fs.writeFileSync(path.join(dir, 'identity-store.previous.json'), JSON.stringify(before), 'utf8');
    const legacyText = JSON.stringify({
        schemaVersion: 2,
        identities: { trusted: { identityId: 'trusted', displayName: 'Trusted', steamId: '76561198000000072', createdAt: 1, updatedAt: 1 } },
        memberships: {},
        deviceTokens: {},
    });
    fs.writeFileSync(file, legacyText, 'utf8');

    failedRenamesRemaining = 1;
    await assert.rejects(store.load(), /simulated migration write failure/);
    assert.deepEqual(store.snapshot(), before);
    assert.equal(fs.readFileSync(file, 'utf8'), legacyText);
});

test('migration validation failure does not fall back over the original file when a previous snapshot exists', async (t) => {
    const dir = makeDir('validation-atomic');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const file = path.join(dir, 'identity-store.json');
    const store = new IdentityStore(file);
    await store.load();
    const before = store.snapshot();
    fs.writeFileSync(path.join(dir, 'identity-store.previous.json'), JSON.stringify(before), 'utf8');
    const invalidLegacyText = JSON.stringify({
        schemaVersion: 2,
        identities: {
            first: { identityId: 'first', displayName: 'First', steamId: '76561198000000074', createdAt: 1, updatedAt: 1 },
            second: { identityId: 'second', displayName: 'Second', steamId: '76561198000000074', createdAt: 1, updatedAt: 1 },
        },
        memberships: {},
        deviceTokens: {},
    });
    fs.writeFileSync(file, invalidLegacyText, 'utf8');

    await assert.rejects(store.load(), /identity SteamID is duplicated/);
    assert.deepEqual(store.snapshot(), before);
    assert.equal(fs.readFileSync(file, 'utf8'), invalidLegacyText);
});

test('schema v3 rejects a formal login account whose identity has no trusted SteamID', async (t) => {
    const dir = makeDir('account-without-steam');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const file = path.join(dir, 'identity-store.json');
    const invalid = {
        schemaVersion: 3,
        identities: { partial: { identityId: 'partial', displayName: 'Partial', createdAt: 1, updatedAt: 1 } },
        accounts: {
            partial: {
                identityId: 'partial', loginName: 'Partial_1', enabled: true,
                passwordState: 'recovery_required', createdAt: 1, updatedAt: 1,
            },
        },
        memberships: {},
        deviceTokens: {},
    };
    fs.writeFileSync(file, JSON.stringify(invalid), 'utf8');

    await assert.rejects(new IdentityStore(file).load(), /trusted SteamID/);
});

test('trusted game account opening stores only the trusted Steam nickname', async (t) => {
    const dir = makeDir('trusted-name');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const store = new IdentityStore(path.join(dir, 'identity-store.json'));
    await store.load();
    const service = new LobbyIdentityService(store, { now: () => 500, randomBytes: (size) => Buffer.alloc(size, 3) });
    const opened = await service.openOrBeginAccountRecovery({
        steamId: '76561198000000073', steamNickname: 'Trusted Steam Name',
    });
    const identity = service.findIdentityBySteamId('76561198000000073') as unknown as {
        identityId: string; displayName: string; steamNickname?: string;
    };
    assert.equal(opened.kind, 'created');
    assert.equal(identity.displayName, 'Trusted Steam Name');
    assert.equal(identity.steamNickname, 'Trusted Steam Name');
    const account = (store.snapshot() as unknown as { accounts: Record<string, { loginName: string; passwordState: string; password?: unknown }> })
        .accounts[identity.identityId];
    assert.match(account.loginName, /^cc_[A-HJ-NP-Za-hj-km-np-z2-9]{8}$/);
    assert.equal(account.passwordState, 'active');
    assert.ok(account.password);
});

test('trusted bridge presence refreshes the Steam nickname of an existing permanent identity', async (t) => {
    const dir = makeDir('bridge-name-refresh');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const store = new IdentityStore(path.join(dir, 'identity-store.json'));
    await store.load();
    const service = new LobbyIdentityService(store, { now: () => 700, randomBytes: (size) => Buffer.alloc(size, 5) });
    const created = await createTestLoginAccount(service, {
        steamId: '76561198000000076', nickname: 'Old Trusted Name', password: 'current-pass',
    });
    const joined = await service.joinPlayerCenterMatch(created.identity.identityId, 'session');
    assert.equal(joined.ok, true);

    const presenceService = service as unknown as {
        confirmLongTermPresence(sessionId: string, players: Array<{ steamId: string; name: string }>): Promise<unknown>;
    };
    await presenceService.confirmLongTermPresence('session', [
        { steamId: '76561198000000076', name: 'Latest Bridge Name' },
    ]);

    const identity = service.findIdentityBySteamId('76561198000000076') as unknown as { steamNickname?: string };
    assert.equal(identity.steamNickname, 'Latest Bridge Name');
});

test('account lookup is exact and case-sensitive for the future player-center boundary', async (t) => {
    const dir = makeDir('case-sensitive-lookup');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const store = new IdentityStore(path.join(dir, 'identity-store.json'));
    await store.load();
    const service = new LobbyIdentityService(store, { now: () => 800, randomBytes: (size) => Buffer.alloc(size, 6) });
    const created = await createTestLoginAccount(service, {
        steamId: '76561198000000077', nickname: 'Trusted', password: 'current-pass',
    });
    const account = store.snapshot().accounts[created.identity.identityId];
    const lookupService = service as unknown as {
        findAccountByLoginName(loginName: unknown): { identityId: string; loginName: string } | undefined;
    };

    assert.equal(lookupService.findAccountByLoginName(account.loginName)?.identityId, created.identity.identityId);
    assert.equal(lookupService.findAccountByLoginName(account.loginName.toLowerCase()), undefined);
});
