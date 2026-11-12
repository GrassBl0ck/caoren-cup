import assert from 'node:assert/strict';
import test from 'node:test';

import {
    applyMatchRuntimeHeartbeat,
    applyMatchRuntimeEvent,
    applyMatchRuntimeSnapshot,
    shouldTreatRoundStartAsLegacyMatchzy,
} from './match-runtime-projection';
import type { MatchRuntimeEvent, MatchRuntimeSnapshot } from './match-runtime-types';
import { buildSessionSnapshotPayload, deserializeSessionSnapshot } from './session-persistence';
import { createInitialSession } from './session-manager';
import { GamePhase } from './types';

const snapshot = (overrides: Partial<MatchRuntimeSnapshot> = {}): MatchRuntimeSnapshot => ({
    matchId: 'match-projection',
    revision: 2,
    runtimeEpoch: 4,
    stage: 'firstHalf',
    pauseState: 'running',
    scoreA: 3,
    scoreB: 2,
    completedRounds: 5,
    overtimeIndex: 0,
    overtimeRoundInStage: 0,
    teamAEngineSide: 'counterTerrorist',
    sideSwapPending: false,
    currentRoundKey: 'match-projection:4:round-6',
    lastEventSequence: 8,
    ...overrides,
});

const session = () => {
    const current = createInitialSession();
    current.bridgeProtocolCapabilities = { matchRuntime: 1, abilitySync: 1 };
    current.matchId = 'match-projection';
    current.matchOptions.matchController = 'caoren'; current.matchOptions.matchRuntimeEnabled = true;
    current.phase = GamePhase.PreGameSetup;
    return current;
};

const event = (sequence: number, type = 'match_started', runtimeEpoch = 4): MatchRuntimeEvent => ({
    sequence,
    type,
    matchId: 'match-projection',
    revision: 2,
    runtimeEpoch,
    roundKey: 'match-projection:4:round-1',
});

test('snapshot projects score and identity without guessing that the match started', () => {
    const current = session();

    const result = applyMatchRuntimeSnapshot(current, snapshot());

    assert.equal(result.applied, true);
    assert.equal(current.phase, GamePhase.PreGameSetup);
    assert.equal(current.matchRuntime?.runtimeEpoch, 4);
    assert.equal(current.liveGameData?.scoreA, 3);
    assert.equal(current.liveGameData?.scoreB, 2);
    assert.equal(current.liveGameData?.currentRound, 6);
});

test('only explicit match_started promotes pregame to live and duplicates are ignored', () => {
    const current = session();
    applyMatchRuntimeSnapshot(current, snapshot());

    const first = applyMatchRuntimeEvent(current, event(1));
    const duplicate = applyMatchRuntimeEvent(current, event(1));

    assert.equal(first.applied, true);
    assert.equal(current.phase, GamePhase.LiveGame);
    assert.equal(current.liveGameData?.formalStatsStarted, true);
    assert.equal(duplicate.applied, false);
    assert.equal(duplicate.code, 'DUPLICATE_OR_STALE_EVENT');
});

test('old epoch events and mismatched matches cannot mutate the session', () => {
    const current = session();
    applyMatchRuntimeSnapshot(current, snapshot());

    assert.equal(applyMatchRuntimeEvent(current, event(9, 'match_started', 3)).applied, false);
    assert.equal(applyMatchRuntimeEvent(current, { ...event(9), matchId: 'other' }).applied, false);
    assert.equal(current.phase, GamePhase.PreGameSetup);
});

test('new restore epoch resets event sequence only after its snapshot is accepted', () => {
    const current = session();
    applyMatchRuntimeSnapshot(current, snapshot());
    applyMatchRuntimeEvent(current, event(8, 'round_completed'));

    const premature = applyMatchRuntimeEvent(current, event(1, 'round_started', 5));
    const restoredSnapshot = applyMatchRuntimeSnapshot(current, snapshot({ runtimeEpoch: 5, lastEventSequence: 0 }));
    const restoredEvent = applyMatchRuntimeEvent(current, event(1, 'round_started', 5));

    assert.equal(premature.applied, false);
    assert.equal(restoredSnapshot.applied, true);
    assert.equal(restoredEvent.applied, true);
    assert.equal(current.matchRuntimeEventEpoch, 5);
    assert.equal(current.matchRuntimeLastEventSequence, 1);
});

test('snapshot v3 preserves caoren runtime while v2 migrates to matchzy', () => {
    const current = session();
    applyMatchRuntimeSnapshot(current, snapshot());
    current.matchRestorePoints = [{ roundNumber: 1, valveBackupFile: 'round1.txt' }];
    const payload = buildSessionSnapshotPayload(current);

    const restoredV3 = deserializeSessionSnapshot(payload);
    const restoredV2 = deserializeSessionSnapshot({ ...payload, version: 2 });

    assert.equal(payload.version, 3);
    assert.equal(restoredV3?.matchOptions.matchController, 'caoren');
    assert.equal(restoredV3?.matchRuntime?.runtimeEpoch, 4);
    assert.deepEqual(restoredV3?.matchRestorePoints, [{ roundNumber: 1, valveBackupFile: 'round1.txt' }]);
    assert.equal(restoredV2?.matchOptions.matchController, 'caoren');
    assert.equal(restoredV2?.matchRuntime, undefined);
    assert.equal(restoredV2?.matchRestorePoints, undefined);
});

test('heartbeat applies snapshot then ordered events and legacy promotion is controller-specific', () => {
    const current = session();

    const result = applyMatchRuntimeHeartbeat(current, { matchRuntimeEnabled: true,
        matchRuntime: snapshot(),
        matchRuntimeEvents: [event(2, 'round_started'), event(1, 'match_started')],
        demoSegments: ['first.dem', '', 42 as unknown as string, 'second.dem'],
    });

    assert.equal(result.snapshotApplied, true);
    assert.equal(result.eventsApplied, 2);
    assert.equal(current.phase, GamePhase.LiveGame);
    assert.equal(current.matchRuntimeLastEventSequence, 2);
    assert.deepEqual(current.liveGameData?.demoSegments, ['first.dem', 'second.dem']);
    assert.equal(shouldTreatRoundStartAsLegacyMatchzy(current, 'round_start'), false);
    current.matchOptions.matchController = 'matchzy';
    assert.equal(shouldTreatRoundStartAsLegacyMatchzy(current, 'round_start'), true);
});

test('malformed heartbeat event entries are ignored without throwing', () => {
    const current = session();
    applyMatchRuntimeSnapshot(current, snapshot());

    assert.doesNotThrow(() => applyMatchRuntimeHeartbeat(current, { matchRuntimeEnabled: true,
        matchRuntimeEvents: [null, 'bad', { sequence: 'bad' }] as unknown as MatchRuntimeEvent[],
    }));
    assert.equal(current.matchRuntimeLastEventSequence, 0);
});

test('bridge restart heartbeat preserves persisted restore points and merges demo segments', () => {
    const current = session();
    applyMatchRuntimeSnapshot(current, snapshot());
    current.matchRestorePoints = [{ roundNumber: 1, valveBackupFile: 'round1.txt' }];
    current.liveGameData!.demoSegments = ['before-restart.dem'];

    applyMatchRuntimeHeartbeat(current, { matchRuntimeEnabled: true,
        matchRuntime: snapshot({ runtimeEpoch: 0 }) as MatchRuntimeSnapshot,
        restorePoints: [],
        demoSegments: [],
    });

    assert.deepEqual(current.matchRestorePoints, [{ roundNumber: 1, valveBackupFile: 'round1.txt' }]);
    assert.deepEqual(current.liveGameData?.demoSegments, ['before-restart.dem']);

    applyMatchRuntimeHeartbeat(current, { matchRuntimeEnabled: true,
        matchRuntime: snapshot(),
        restorePoints: [{ roundNumber: 1, valveBackupFile: 'round1.txt' }],
        demoSegments: ['before-restart.dem', 'after-restart.dem'],
    });

    assert.deepEqual(current.liveGameData?.demoSegments, ['before-restart.dem', 'after-restart.dem']);
});

test('new epoch snapshot restores the full web stats checkpoint for its target round', () => {
    const current = session();
    current.players.a1 = {
        playerId: 'a1', name: 'A1', role: 'Player', isReady: true,
        stats: { kills: 3, damage: 450, firstDeaths: 1 },
        sideStats: { CT: { kills: 3 }, T: { kills: 0 } },
    } as any;
    applyMatchRuntimeSnapshot(current, snapshot({ completedRounds: 5, currentRoundKey: 'match-projection:4:round-6' }));
    applyMatchRuntimeEvent(current, event(1, 'match_started'));
    applyMatchRuntimeEvent(current, { ...event(9, 'round_started'), roundKey: 'match-projection:4:round-6' });
    current.players.a1.stats.kills = 9;
    current.players.a1.stats.damage = 999;
    current.players.a1.sideStats.CT.kills = 9;

    applyMatchRuntimeSnapshot(current, snapshot({
        runtimeEpoch: 5,
        completedRounds: 5,
        currentRoundKey: null,
        lastEventSequence: 0,
    }));

    assert.equal(current.players.a1.stats.kills, 3);
    assert.equal(current.players.a1.stats.damage, 450);
    assert.equal(current.players.a1.stats.firstDeaths, 1);
    assert.equal(current.players.a1.sideStats.CT.kills, 3);
});
