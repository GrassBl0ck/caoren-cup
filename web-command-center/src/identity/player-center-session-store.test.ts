import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import {
    PLAYER_CENTER_ABSOLUTE_TTL_MS,
    PLAYER_CENTER_IDLE_TTL_MS,
    PlayerCenterSessionStore,
} from './player-center-session-store';

const makeStore = (name: string, now: { value: number }) => {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), `caoren-player-center-${name}-`));
    const file = path.join(dir, 'player-center-sessions.json');
    let randomCall = 0;
    const store = new PlayerCenterSessionStore(file, {
        now: () => now.value,
        randomBytes: (size) => Buffer.alloc(size, ++randomCall),
    });
    return { dir, file, store };
};

test('player-center session persists only a token hash and survives restart', async (t) => {
    const now = { value: 1_000_000 };
    const { dir, file, store } = makeStore('restart', now);
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    await store.load();

    const created = await store.create('identity-a', 1234, 'device-token-id');
    const persisted = fs.readFileSync(file, 'utf8');
    assert.equal(persisted.includes(created.rawToken), false);
    assert.equal(persisted.includes(created.session.tokenHash), true);
    assert.equal(created.session.identityId, 'identity-a');
    assert.equal(created.session.currentDeviceTokenId, 'device-token-id');
    assert.equal(created.session.idleExpiresAt, now.value + PLAYER_CENTER_IDLE_TTL_MS);
    assert.equal(created.session.absoluteExpiresAt, now.value + PLAYER_CENTER_ABSOLUTE_TTL_MS);

    const browser = await store.create('identity-b', 1234, undefined, true);
    const reloaded = new PlayerCenterSessionStore(file, { now: () => now.value });
    await reloaded.load();
    assert.equal((await reloaded.use(browser.rawToken))?.playerOnly, true);
    assert.equal((await reloaded.use(created.rawToken))?.playerOnly, undefined);
    assert.equal((await reloaded.use(created.rawToken))?.identityId, 'identity-a');
});

test('using a session safely advances lastUsedAt and idle expiry without exceeding the absolute expiry', async (t) => {
    const now = { value: 2_000_000 };
    const { dir, store } = makeStore('expiry', now);
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    await store.load();
    const created = await store.create('identity-a', 1234);

    now.value += PLAYER_CENTER_IDLE_TTL_MS - 1;
    const used = await store.use(created.rawToken);
    assert.equal(used?.lastUsedAt, now.value);
    assert.equal(used?.idleExpiresAt, Math.min(
        created.session.absoluteExpiresAt,
        now.value + PLAYER_CENTER_IDLE_TTL_MS,
    ));

    now.value = created.session.absoluteExpiresAt;
    assert.equal(await store.use(created.rawToken), undefined);
    assert.equal(store.snapshot().sessions[created.session.sessionId], undefined);
});

test('idle expiry, logout, and malformed tokens cannot restore a player-center session', async (t) => {
    const now = { value: 3_000_000 };
    const { dir, store } = makeStore('invalid', now);
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    await store.load();
    const idle = await store.create('identity-a', 1);
    assert.equal(store.isBoundSessionCurrent(idle.session.sessionId, 'identity-a', 1), true);
    assert.equal(store.isBoundSessionCurrent(idle.session.sessionId, 'identity-b', 1), false);
    assert.equal(store.isBoundSessionCurrent(idle.session.sessionId, 'identity-a', 2), false);
    now.value = idle.session.idleExpiresAt;
    assert.equal(store.isBoundSessionCurrent(idle.session.sessionId, 'identity-a', 1), false);
    assert.equal(await store.use(idle.rawToken), undefined);

    const logout = await store.create('identity-a', 1);
    assert.equal(await store.revokeCurrent(logout.rawToken), true);
    assert.equal(store.isBoundSessionCurrent(logout.session.sessionId, 'identity-a', 1), false);
    assert.equal(await store.use(logout.rawToken), undefined);
    assert.equal(await store.use('not-a-session-token'), undefined);
});

test('account change preserves and rebinds only the current session', async (t) => {
    const now = { value: 4_000_000 };
    const { dir, store } = makeStore('account-change', now);
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    await store.load();
    const current = await store.create('identity-a', 10);
    const other = await store.create('identity-a', 10);
    const unrelated = await store.create('identity-b', 20);

    const result = await store.applyAccountChange('identity-a', current.session.sessionId, 11);
    assert.deepEqual(result.revokedSessionIds, [other.session.sessionId]);
    assert.equal(store.snapshot().sessions[current.session.sessionId].accountUpdatedAt, 11);
    assert.equal(store.snapshot().sessions[other.session.sessionId], undefined);
    assert.ok(store.snapshot().sessions[unrelated.session.sessionId]);
});

test('corrupt primary never reactivates a logged-out session from a legacy snapshot', async (t) => {
    const now = { value: 5_000_000 };
    const { dir, file, store } = makeStore('previous', now);
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    await store.load();
    const first = await store.create('identity-a', 1);
    fs.copyFileSync(file, store.previousPath);
    await store.revokeCurrent(first.rawToken);
    fs.writeFileSync(file, '{broken', 'utf8');

    const recovered = new PlayerCenterSessionStore(file, { now: () => now.value });
    await recovered.load();
    assert.equal(await recovered.use(first.rawToken), undefined);
    assert.deepEqual(recovered.snapshot().sessions, {});
});

test('durably forgotten devices immediately lose bound sessions even before session-file cleanup', async (t) => {
    const now = { value: 6_000_000 };
    const { dir, file } = makeStore('device-guard', now);
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const forgotten = new Set<string>();
    const store = new PlayerCenterSessionStore(file, { now: () => now.value, isDeviceForgotten: id => forgotten.has(id) });
    await store.load();
    const removed = await store.create('identity-a', 1, 'removed-device');
    const other = await store.create('identity-a', 1, 'another-device');
    forgotten.add('removed-device');
    assert.ok(store.snapshot().sessions[removed.session.sessionId]);
    assert.equal(store.isBoundSessionCurrent(removed.session.sessionId, 'identity-a', 1), false);
    assert.equal(await store.useBoundSession(removed.session.sessionId, 'identity-a'), undefined);
    assert.equal(await store.use(removed.rawToken), undefined);
    assert.ok(await store.use(other.rawToken));
});
