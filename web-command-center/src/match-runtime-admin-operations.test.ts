import assert from 'node:assert/strict';
import test, { afterEach } from 'node:test';

import {
    MatchRuntimeConfirmationStore,
    applyMatchRuntimeAdminAction,
} from './match-runtime-admin-operations';
import { cancelPluginCommands } from './plugin-command-queue';
import { createInitialSession } from './session-manager';
import { GamePhase } from './types';
import { applyMatchOptions } from './game-flow-manager';
import { setSession } from './session-manager';

afterEach(() => cancelPluginCommands(command => command.type === 'MATCH_RUNTIME_COMMAND'));

const session = () => {
    const current = createInitialSession();
    current.bridgeProtocolCapabilities = { matchRuntime: 1, abilitySync: 1 };
    current.matchId = 'match-admin';
    current.phase = GamePhase.Lobby; current.matchOptions.matchRuntimeEnabled = true;
    current.players = {
        a1: { playerId: 'a1', name: 'A1', role: 'Player', rosterTeam: 'A', steamId: '76561198000000001', isReady: false },
        b1: { playerId: 'b1', name: 'B1', role: 'Player', rosterTeam: 'B', steamId: '76561198100000001', isReady: false },
    };
    current.playerOrder = ['a1', 'b1'];
    current.teams.A.players = ['a1'];
    current.teams.B.players = ['b1'];
    current.captains = { A: 'a1', B: 'b1' };
    current.selectedMap = 'Mirage';
    current.selectedSide = 'CT';
    return current;
};

test('controller can change only in lobby and clears stale runtime state', () => {
    const current = session();
    current.matchRuntime = {
        matchId: 'old', revision: 1, runtimeEpoch: 1, stage: 'aborted', pauseState: 'running',
        scoreA: 0, scoreB: 0, completedRounds: 0, overtimeIndex: 0, overtimeRoundInStage: 0,
        teamAEngineSide: 'counterTerrorist', sideSwapPending: false, currentRoundKey: null, lastEventSequence: 1,
    };

    const changed = applyMatchRuntimeAdminAction(current, 'SET_MATCH_CONTROLLER', { controller: 'caoren' }, 'admin-1');
    current.phase = GamePhase.PreGameSetup;
    const locked = applyMatchRuntimeAdminAction(current, 'SET_MATCH_CONTROLLER', { controller: 'matchzy' }, 'admin-1');

    assert.equal(changed.ok, true);
    assert.equal(current.matchOptions.matchController, 'caoren');
    assert.equal(current.matchRuntime, undefined);
    assert.equal(current.matchRuntimeRevision, 2);
    assert.equal(locked.ok, false);
    assert.equal(locked.code, 'PHASE_LOCKED');
});

test('prepare and stage actions enqueue structured runtime commands', () => {
    const current = session();
    current.matchOptions.matchController = 'caoren'; current.matchOptions.matchRuntimeEnabled = true;
    current.phase = GamePhase.PreGameSetup;

    const prepare = applyMatchRuntimeAdminAction(current, 'MATCH_RUNTIME_PREPARE', {}, 'admin-1');
    current.matchRuntime = {
        matchId: 'match-admin', revision: 1, runtimeEpoch: 3, stage: 'staging', pauseState: 'running',
        scoreA: 0, scoreB: 0, completedRounds: 0, overtimeIndex: 0, overtimeRoundInStage: 0,
        teamAEngineSide: 'counterTerrorist', sideSwapPending: false, currentRoundKey: null, lastEventSequence: 0,
    };
    const warmup = applyMatchRuntimeAdminAction(current, 'MATCH_RUNTIME_WARMUP', {}, 'admin-1');

    assert.equal(prepare.command?.payload.action, 'load');
    assert.equal(warmup.command?.payload.action, 'begin_warmup');
    assert.equal(warmup.command?.payload.expectedStage, 'staging');
});

test('restore confirmation token is one-time operator and payload bound', () => {
    const current = session();
    current.matchOptions.matchController = 'caoren'; current.matchOptions.matchRuntimeEnabled = true;
    current.phase = GamePhase.LiveGame;
    current.matchRuntime = {
        matchId: 'match-admin', revision: 1, runtimeEpoch: 3, stage: 'secondHalf', pauseState: 'technicalPaused',
        scoreA: 8, scoreB: 7, completedRounds: 15, overtimeIndex: 0, overtimeRoundInStage: 0,
        teamAEngineSide: 'terrorist', sideSwapPending: false, currentRoundKey: null, lastEventSequence: 30,
    };
    const confirmations = new MatchRuntimeConfirmationStore(() => 1_000);

    const preview = applyMatchRuntimeAdminAction(current, 'MATCH_RUNTIME_RESTORE', { round: 12 }, 'admin-1', confirmations);
    const wrongActor = applyMatchRuntimeAdminAction(current, 'MATCH_RUNTIME_RESTORE', {
        round: 12, confirmationToken: preview.confirmationToken,
    }, 'admin-2', confirmations);
    const confirmed = applyMatchRuntimeAdminAction(current, 'MATCH_RUNTIME_RESTORE', {
        round: 12, confirmationToken: preview.confirmationToken,
    }, 'admin-1', confirmations);
    const replay = applyMatchRuntimeAdminAction(current, 'MATCH_RUNTIME_RESTORE', {
        round: 12, confirmationToken: preview.confirmationToken,
    }, 'admin-1', confirmations);

    assert.equal(preview.needsConfirmation, true);
    assert.equal(wrongActor.ok, false);
    assert.equal(confirmed.ok, true);
    assert.deepEqual(confirmed.command?.payload, {
        action: 'restore', matchId: 'match-admin', revision: 1, runtimeEpoch: 3,
        expectedStage: 'secondHalf', targetRound: 12, requestedAt: 1_000,
    });
    assert.equal(replay.ok, false);
});

test('substitute validates seat steam id and safe runtime stage', () => {
    const current = session();
    current.matchOptions.matchController = 'caoren'; current.matchOptions.matchRuntimeEnabled = true;
    current.phase = GamePhase.LiveGame;
    current.matchRuntime = {
        matchId: 'match-admin', revision: 1, runtimeEpoch: 3, stage: 'secondHalf', pauseState: 'technicalPaused',
        scoreA: 8, scoreB: 7, completedRounds: 15, overtimeIndex: 0, overtimeRoundInStage: 0,
        teamAEngineSide: 'terrorist', sideSwapPending: false, currentRoundKey: null, lastEventSequence: 30,
    };

    const invalid = applyMatchRuntimeAdminAction(current, 'MATCH_RUNTIME_SUBSTITUTE', {
        seatId: 'a1', steamId: '../bad', name: '替补',
    }, 'admin-1');
    const valid = applyMatchRuntimeAdminAction(current, 'MATCH_RUNTIME_SUBSTITUTE', {
        seatId: 'a1', steamId: '76561198222222222', name: '替补',
    }, 'admin-1');

    assert.equal(invalid.ok, false);
    assert.equal(valid.ok, true);
    assert.equal(valid.command?.payload.action, 'substitute');
    assert.equal(valid.command?.payload.steamId, '76561198222222222');
});

test('lobby match options controller change invalidates stale runtime identity', () => {
    const current = session();
    current.matchOptions.matchController = 'matchzy';
    current.matchRuntimeRevision = 4;
    current.matchRuntime = {
        matchId: 'old', revision: 4, runtimeEpoch: 2, stage: 'aborted', pauseState: 'running',
        scoreA: 0, scoreB: 0, completedRounds: 0, overtimeIndex: 0, overtimeRoundInStage: 0,
        teamAEngineSide: 'counterTerrorist', sideSwapPending: false, currentRoundKey: null, lastEventSequence: 1,
    };
    current.matchRuntimeEventEpoch = 2;
    current.matchRuntimeLastEventSequence = 1;
    setSession(current);

    applyMatchOptions({ matchMode: 'competitive', matchController: 'caoren', matchRuntimeEnabled: true });

    assert.equal(current.matchOptions.matchController, 'caoren');
    assert.equal(current.matchRuntime, undefined);
    assert.equal(current.matchRuntimeEventEpoch, undefined);
    assert.equal(current.matchRuntimeLastEventSequence, undefined);
    assert.equal(current.matchRuntimeRevision, 5);
});
