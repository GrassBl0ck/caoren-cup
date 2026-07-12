import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { IdentityStore } from './identity-store';

const makeStoreDir = (name: string) => {
    const dir = path.resolve(__dirname, '..', '..', 'runtime', `identity-store-${name}-${process.pid}-${Date.now()}`);
    fs.mkdirSync(dir, { recursive: true });
    return dir;
};

const schemaV1Fixture = () => ({
    schemaVersion: 1,
    identities: {
        identity: {
            identityId: 'identity',
            displayName: 'Existing Member',
            steamId: '76561198000000071',
            createdAt: 10,
            updatedAt: 20,
        },
    },
    memberships: {
        membership: {
            membershipId: 'membership',
            sessionId: 'old-session',
            identityId: 'identity',
            nickname: 'Existing Member',
            identityLevel: 'longTerm',
            confirmationState: 'confirmed',
            claimedSteamId: '76561198000000071',
            trustedSteamId: '76561198000000071',
            joinedAt: 10,
            updatedAt: 20,
        },
    },
    deviceTokens: {
        token: {
            tokenId: 'token',
            identityId: 'identity',
            deviceId: 'desktop',
            tokenHash: '00'.repeat(32),
            familyId: 'family',
            status: 'active',
            createdAt: 10,
            lastUsedAt: 20,
            idleExpiresAt: 30,
            absoluteExpiresAt: 40,
            rotateAfter: 25,
        },
    },
});

test('failed persistence keeps the previous in-memory state and the write queue can recover', async (t) => {
    const dir = path.resolve(__dirname, '..', '..', 'runtime', `identity-store-failure-${process.pid}-${Date.now()}`);
    fs.mkdirSync(dir, { recursive: true });
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    let failRename = false;
    const controlledFs = new Proxy(fs, {
        get(target, property, receiver) {
            if (property === 'renameSync') {
                return (...args: Parameters<typeof fs.renameSync>) => {
                    if (failRename) throw new Error('simulated rename failure');
                    return fs.renameSync(...args);
                };
            }
            return Reflect.get(target, property, receiver);
        },
    });
    const store = new IdentityStore(path.join(dir, 'identity-store.json'), { fs: controlledFs });
    await store.load();
    const before = store.snapshot();

    failRename = true;
    await assert.rejects(store.mutate((data) => {
        data.identities.failed = { identityId: 'failed', displayName: 'Failed', createdAt: 1, updatedAt: 1 };
    }), /simulated rename failure/);
    assert.deepEqual(store.snapshot(), before);

    failRename = false;
    await store.mutate((data) => {
        data.identities.recovered = { identityId: 'recovered', displayName: 'Recovered', createdAt: 2, updatedAt: 2 };
    });
    assert.equal(store.snapshot().identities.recovered?.displayName, 'Recovered');
});

test('schema v1 migrates to v2 without losing identity membership or device token data', async (t) => {
    const dir = makeStoreDir('migration');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const file = path.join(dir, 'identity-store.json');
    const fixture = schemaV1Fixture();
    fs.writeFileSync(file, JSON.stringify(fixture), 'utf8');

    const store = new IdentityStore(file);
    await store.load();
    const migrated = store.snapshot();

    assert.equal(migrated.schemaVersion, 2);
    assert.deepEqual(migrated.identities, fixture.identities);
    assert.deepEqual(migrated.memberships, fixture.memberships);
    assert.deepEqual(migrated.deviceTokens, fixture.deviceTokens);
});

test('a valid schema v1 previous file restores a corrupt primary as schema v2', async (t) => {
    const dir = makeStoreDir('previous-migration');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const file = path.join(dir, 'identity-store.json');
    fs.writeFileSync(file, '{broken', 'utf8');
    fs.writeFileSync(path.join(dir, 'identity-store.previous.json'), JSON.stringify(schemaV1Fixture()), 'utf8');

    const store = new IdentityStore(file);
    await store.load();

    assert.equal(store.snapshot().schemaVersion, 2);
    assert.equal(store.snapshot().identities.identity?.steamId, '76561198000000071');
});

test('unknown schema fails instead of silently creating an empty identity store', async (t) => {
    const dir = makeStoreDir('unknown-schema');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const file = path.join(dir, 'identity-store.json');
    fs.writeFileSync(file, JSON.stringify({ schemaVersion: 99, identities: {}, memberships: {}, deviceTokens: {} }), 'utf8');

    await assert.rejects(new IdentityStore(file).load(), /identity store schema is unsupported/);
    assert.equal(fs.readFileSync(file, 'utf8').includes('"schemaVersion":99'), true);
});

test('schema v2 rejects a malformed fixed password credential without overwriting the file', async (t) => {
    const dir = makeStoreDir('malformed-fixed-account');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const file = path.join(dir, 'identity-store.json');
    const malformed = {
        schemaVersion: 2,
        identities: {
            fixed: {
                identityId: 'fixed',
                displayName: 'Malformed',
                steamId: '76561198000000072',
                fixedAccount: { enabled: true, password: { algorithm: 'plain', hash: 'secret' } },
                createdAt: 1,
                updatedAt: 1,
            },
        },
        memberships: {},
        deviceTokens: {},
    };
    fs.writeFileSync(file, JSON.stringify(malformed), 'utf8');

    await assert.rejects(new IdentityStore(file).load(), /fixed account credential is invalid/);
    assert.equal(JSON.parse(fs.readFileSync(file, 'utf8')).identities.fixed.fixedAccount.password.algorithm, 'plain');
});

test('schema v2 rejects duplicate permanent SteamID bindings', async (t) => {
    const dir = makeStoreDir('duplicate-steam');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const file = path.join(dir, 'identity-store.json');
    const duplicate = {
        schemaVersion: 2,
        identities: {
            first: { identityId: 'first', displayName: 'First', steamId: '76561198000000073', createdAt: 1, updatedAt: 1 },
            second: { identityId: 'second', displayName: 'Second', steamId: '76561198000000073', createdAt: 1, updatedAt: 1 },
        },
        memberships: {},
        deviceTokens: {},
    };
    fs.writeFileSync(file, JSON.stringify(duplicate), 'utf8');

    await assert.rejects(new IdentityStore(file).load(), /identity SteamID is duplicated/);
});
