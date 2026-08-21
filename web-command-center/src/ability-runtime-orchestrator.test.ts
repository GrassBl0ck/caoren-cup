import assert from 'node:assert/strict';
import test, { afterEach } from 'node:test';

import { GamePhase, type GameSession } from './types';
import {
    enqueueAbilityRuntimeStart,
    enqueueAbilityRuntimeStop,
    enqueueAbilityRuntimeSubstitution,
} from './ability-runtime-orchestrator';
import { cancelPluginCommands } from './plugin-command-queue';

afterEach(() => cancelPluginCommands(command => command.type === 'ABILITY_RUNTIME'));

const session = (): GameSession => ({
    matchId: 'match-1',
    phase: GamePhase.LiveGame,
    matchOptions: { abilityModeEnabled: true } as GameSession['matchOptions'],
    abilitySyncState: {
        status: 'confirmed',
        syncId: 'sync-1',
        matchId: 'match-1',
        revision: 2,
        catalogVersion: 'ability-catalog-v1',
        contentDigest: 'a'.repeat(64),
        seatCount: 10,
    },
} as GameSession);

test('confirmed current sync queues a structured runtime start identity', () => {
    const queued = enqueueAbilityRuntimeStart(session(), 'round-1');

    assert.equal(queued?.type, 'ABILITY_RUNTIME');
    assert.deepEqual(queued?.payload, {
        action: 'start',
        matchId: 'match-1',
        revision: 2,
        contentDigest: 'a'.repeat(64),
        roundKey: 'round-1',
    });
});

test('unconfirmed stale or disabled config never queues runtime start', () => {
    const unconfirmed = session();
    unconfirmed.abilitySyncState!.status = 'plugin_validating';
    assert.equal(enqueueAbilityRuntimeStart(unconfirmed, 'round-1'), null);

    const stale = session();
    stale.abilitySyncState!.matchId = 'old-match';
    assert.equal(enqueueAbilityRuntimeStart(stale, 'round-1'), null);

    const disabled = session();
    disabled.matchOptions.abilityModeEnabled = false;
    assert.equal(enqueueAbilityRuntimeStart(disabled, 'round-1'), null);
});

test('stop and substitute reuse the confirmed identity without free-form server text', () => {
    const current = session();
    const stop = enqueueAbilityRuntimeStop(current, 'match_finished');
    const substitute = enqueueAbilityRuntimeSubstitution(current, 'A1', '76561198000000999');

    assert.equal(stop?.payload.action, 'stop');
    assert.equal(stop?.payload.reason, 'match_finished');
    assert.deepEqual(substitute?.payload, {
        action: 'substitute',
        matchId: 'match-1',
        revision: 2,
        contentDigest: 'a'.repeat(64),
        seatId: 'A1',
        steamId: '76561198000000999',
    });
    assert.equal('command' in substitute!.payload, false);
});
