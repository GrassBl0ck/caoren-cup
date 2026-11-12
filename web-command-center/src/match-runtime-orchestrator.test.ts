import assert from 'node:assert/strict';
import test from 'node:test';

import { cancelPluginCommands } from './plugin-command-queue';
import {
    MATCH_RUNTIME_COMMAND_TYPE,
    enqueueMatchRuntimeCommand,
} from './match-runtime-orchestrator';
import { GamePhase, type GameSession } from './types';

const session = (controller: 'matchzy' | 'caoren' = 'caoren'): GameSession => ({
    sessionId: 'session-1',
    bridgeProtocolCapabilities: { matchRuntime: 1, abilitySync: 1 },
    matchId: 'match-web',
    phase: GamePhase.PreGameSetup,
    matchOptions: {
        matchMode: 'competitive',
        matchController: controller, matchRuntimeEnabled: true,
        abilityModeEnabled: false,
        undercoverModeEnabled: false,
        caorenModifiersEnabled: false,
    },
    selectedMap: 'Mirage',
    selectedSide: 'CT',
    players: {
        a1: { playerId: 'a1', name: 'A1', role: 'Player', rosterTeam: 'A', steamId: '76561198000000001', isReady: false },
        b1: { playerId: 'b1', name: 'B1', role: 'Player', rosterTeam: 'B', steamId: '76561198100000001', isReady: false },
    },
    playerOrder: ['a1', 'b1'],
    teams: { A: { name: 'A', players: ['a1'] }, B: { name: 'B', players: ['b1'] } },
    captains: { A: 'a1', B: 'b1' },
    rollValues: { A: null, B: null },
    draftOrder: [],
    draftIndex: 0,
    mapPool: ['Mirage'],
    bannedMaps: [],
    currentBanTeam: null,
    banSequence: [],
    sidePickTeam: 'A',
    undercoverCount: 0,
    detectiveCount: 0,
    questionsUsed: 0,
    currentQuestion: null,
    questionAnswer: null,
    accusations: {},
    timerEndAt: null,
    timerPhase: null,
    adminLock: { holderId: null, acquiredAt: null },
    createdAt: 1,
    autoClearMinutes: 15,
    matchRuntimeRevision: 3,
} as GameSession);

test('load command builds immutable map roster side and captain definition', () => {
    cancelPluginCommands(() => true);
    const current = session();
    current.matchOptions.teamAName = '草一队';
    current.matchOptions.teamBName = '草二队';

    const command = enqueueMatchRuntimeCommand(current, 'load');

    assert.ok(command);
    assert.equal(command.type, MATCH_RUNTIME_COMMAND_TYPE);
    assert.equal(command.payload.action, 'load');
    assert.equal(command.payload.matchId, 'match-web');
    assert.equal(command.payload.revision, 3);
    assert.equal(command.payload.mapName, 'de_mirage');
    assert.equal(command.payload.teamAInitialSide, 'CT');
    assert.equal(command.payload.teamAName, '草一队');
    assert.equal(command.payload.teamBName, '草二队');
    assert.deepEqual(command.payload.seats, [
        { seatId: 'a1', roster: 'A', steamId: '76561198000000001', name: 'A1', isCaptain: true },
        { seatId: 'b1', roster: 'B', steamId: '76561198100000001', name: 'B1', isCaptain: true },
    ]);
});

test('runtime actions carry current identity expected stage and caller payload', () => {
    cancelPluginCommands(() => true);
    const current = session();
    current.matchRuntime = {
        matchId: 'match-web', revision: 3, runtimeEpoch: 7, stage: 'readyCheck', pauseState: 'running',
        scoreA: 0, scoreB: 0, completedRounds: 0, overtimeIndex: 0, overtimeRoundInStage: 0,
        teamAEngineSide: 'counterTerrorist', sideSwapPending: false, currentRoundKey: null, lastEventSequence: 4,
    };

    const command = enqueueMatchRuntimeCommand(current, 'begin_going_live', { operatorId: 'admin-1' }, 'readyCheck');

    assert.ok(command);
    assert.deepEqual(command.payload, {
        action: 'begin_going_live',
        matchId: 'match-web',
        revision: 3,
        runtimeEpoch: 7,
        expectedStage: 'readyCheck',
        operatorId: 'admin-1',
    });
});

test('matchzy controller and incomplete caoren definitions cannot enqueue runtime commands', () => {
    cancelPluginCommands(() => true);
    assert.equal(enqueueMatchRuntimeCommand(session('matchzy'), 'load'), null);
    const incomplete = session();
    incomplete.selectedSide = null;
    assert.equal(enqueueMatchRuntimeCommand(incomplete, 'load'), null);
});
