import assert from 'node:assert/strict';
import test, { afterEach } from 'node:test';

import { GamePhase, type GameSession } from './types';
import {
    enqueueAbilityRuntimeStart,
    enqueueAbilityRuntimeRestore,
    enqueueAbilityRuntimeStop,
    enqueueAbilityRuntimeSubstitution,
} from './ability-runtime-orchestrator';
import { cancelPluginCommands } from './plugin-command-queue';

afterEach(() => cancelPluginCommands(command => command.type === 'ABILITY_RUNTIME'));

const session = (): GameSession => ({
    matchId: 'match-1',
    phase: GamePhase.LiveGame,
    bridgeProtocolCapabilities: { matchRuntime: 1, abilitySync: 1 },
    matchOptions: { abilityModeEnabled: true, matchController: 'matchzy' } as GameSession['matchOptions'],
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
        runtimeEpoch: 1,
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
        runtimeEpoch: 1,
        seatId: 'A1',
        steamId: '76561198000000999',
    });
    assert.equal('command' in substitute!.payload, false);
});

test('caoren controller uses current runtime epoch for substitution and restore', () => {
    const current = session();
    current.matchOptions.matchController = 'caoren'; current.matchOptions.matchRuntimeEnabled = true;
    current.matchRuntime = {
        matchId: 'match-1', revision: 2, runtimeEpoch: 7, stage: 'secondHalf', pauseState: 'technicalPaused',
        scoreA: 8, scoreB: 7, completedRounds: 15, overtimeIndex: 0, overtimeRoundInStage: 0,
        teamAEngineSide: 'terrorist', sideSwapPending: false, currentRoundKey: null, lastEventSequence: 30,
    };

    const substitute = enqueueAbilityRuntimeSubstitution(current, 'A1', '76561198000000999');
    const restore = enqueueAbilityRuntimeRestore(current, 'match-1:8:round-12', 'round-12', 8);

    assert.equal(substitute?.payload.runtimeEpoch, 7);
    assert.deepEqual(restore?.payload, {
        action: 'restore',
        matchId: 'match-1',
        revision: 2,
        contentDigest: 'a'.repeat(64),
        runtimeEpoch: 8,
        roundKey: 'match-1:8:round-12',
        checkpointId: 'round-12',
    });
});
