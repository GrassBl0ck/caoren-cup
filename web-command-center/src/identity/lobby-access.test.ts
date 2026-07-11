import assert from 'node:assert/strict';
import test from 'node:test';
import { LobbyInviteGuard, createLobbyAccess, rotateLobbyInvite } from './lobby-access';

const HOUR = 60 * 60 * 1000;
const MINUTE = 60 * 1000;

test('lobby invite is eight characters and expires after twelve hours', () => {
    const access = createLobbyAccess(1_000, () => Buffer.alloc(16, 7));
    const guard = new LobbyInviteGuard();

    assert.match(access.inviteCode, /^[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]{8}$/);
    assert.equal(guard.verify(access, 'client-a', access.inviteCode, 1_000).ok, true);
    assert.deepEqual(
        guard.verify(access, 'client-a', access.inviteCode, 1_000 + 12 * HOUR),
        { ok: false, reason: 'expired' },
    );
});

test('lobby invite blocks a source after five wrong attempts', () => {
    const access = createLobbyAccess(10_000, () => Buffer.alloc(16, 11));
    const guard = new LobbyInviteGuard();

    for (let attempt = 1; attempt <= 5; attempt++) {
        assert.deepEqual(guard.verify(access, 'client-b', 'WRONG123', 10_000), {
            ok: false,
            reason: 'invalid',
            attemptsRemaining: 5 - attempt,
        });
    }
    assert.deepEqual(guard.verify(access, 'client-b', access.inviteCode, 10_000), {
        ok: false,
        reason: 'rate_limited',
        retryAt: 10_000 + 15 * MINUTE,
    });
    assert.equal(guard.verify(access, 'client-b', access.inviteCode, 10_000 + 15 * MINUTE).ok, true);
});

test('rotating a lobby invite invalidates the previous code', () => {
    const initial = createLobbyAccess(20_000, () => Buffer.alloc(16, 3));
    const rotated = rotateLobbyInvite(initial, 21_000, () => Buffer.alloc(16, 17));
    const guard = new LobbyInviteGuard();

    assert.notEqual(rotated.inviteCode, initial.inviteCode);
    assert.equal(guard.verify(rotated, 'client-c', initial.inviteCode, 21_000).ok, false);
    assert.equal(guard.verify(rotated, 'client-c', rotated.inviteCode, 21_000).ok, true);
});
