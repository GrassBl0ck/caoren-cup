import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { buildDuelRuntimeConfigPayload } from './duel-runtime-config';

test('builds the only formal-start payload owned by the plugin', () => {
    const payload = buildDuelRuntimeConfigPayload('match-1', {
        duelRounds: { pistol: 8, rifle: 16, sniper: 12 },
        duelRoundTimeMinutes: 1.25,
        duelUtilityMode: 'random2',
    }, 1234);

    assert.deepEqual(payload, {
        matchId: 'match-1',
        rounds: { pistol: 8, rifle: 16, sniper: 12 },
        roundTimeMinutes: 1.25,
        utilityMode: 'random2',
        requestedAt: 1234,
    });
    assert.equal('command' in payload, false);
});

test('formal duel start queues only the plugin-owned runtime configuration', () => {
    const source = readFileSync(path.join(__dirname, 'game-flow-manager.ts'), 'utf8');
    assert.equal(source.includes('queueDuelRulesCommands'), false);
    assert.equal(source.includes("buildDuelRuntimeConfigPayload(session.matchId, session.matchOptions, Date.now())"), true);
});
