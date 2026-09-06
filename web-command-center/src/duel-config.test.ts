import assert from 'node:assert/strict';
import test from 'node:test';
import { getDefaultDuelRounds, normalizeDuelRounds } from './duel-config';

test('duel rounds accept a one-round match', () => {
    assert.deepEqual(normalizeDuelRounds({ pistol: 0, rifle: 1, sniper: 0 }), {
        pistol: 0,
        rifle: 1,
        sniper: 0,
    });
});

test('duel rounds reject an all-zero match', () => {
    assert.deepEqual(normalizeDuelRounds({ pistol: 0, rifle: 0, sniper: 0 }), getDefaultDuelRounds());
});
