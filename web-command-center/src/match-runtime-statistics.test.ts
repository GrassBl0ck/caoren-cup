import { buildSessionSnapshotPayload, deserializeSessionSnapshot } from './session-persistence';
import { readFileSync } from 'node:fs';
import assert from 'node:assert/strict';
import test from 'node:test';
import { createInitialSession, setSession } from './session-manager';
import { GamePhase } from './types';
import { applyMatchRuntimeHeartbeat, applyMatchRuntimeSnapshot } from './match-runtime-projection';
import { MatchRuntimeEvent, MatchRuntimeSnapshot } from './match-runtime-types';
import { applyPluginTelemetryBatch, registerPluginRoutes } from './plugin-api';
import { updateMatchFinishState } from './game-flow-manager';
import { calculateScores } from './scoring';

const players = [
    { steamId: '76561198000000001', team: 'CT', isAlive: true, health: 100 },
    { steamId: '76561198000000002', team: 'T', isAlive: true, health: 100 },
];
const setup = () => {
    const session = createInitialSession();
    session.matchId = 'runtime-stats';
    session.phase = GamePhase.PreGameSetup;
    session.matchOptions.matchRuntimeEnabled = true;
    session.matchOptions.matchController = 'caoren';
    session.bridgeProtocolCapabilities = { matchRuntime: 1, abilitySync: 1 };
    for (const [index, raw] of players.entries()) {
        const id = index ? 'b' : 'a';
        session.players[id] = { playerId: id, name: id, role: 'Player', isReady: true, steamId: raw.steamId, team: raw.team as 'CT' | 'T', rosterTeam: index ? 'B' : 'A' };
    }
    setSession(session);
    return session;
};
const snapshot = (overrides: Partial<MatchRuntimeSnapshot> = {}): MatchRuntimeSnapshot => ({
    matchId: 'runtime-stats', revision: 1, runtimeEpoch: 1, stage: 'firstHalf',
    pauseState: 'running', scoreA: 0, scoreB: 0, completedRounds: 0,
    overtimeIndex: 0, overtimeRoundInStage: 0, teamAEngineSide: 'counterTerrorist',
    sideSwapPending: false, currentRoundKey: 'runtime-stats:1:round-1', lastEventSequence: 0,
    ...overrides,
});
const event = (sequence: number, type: string, roundNumber = 1, runtimeEpoch = 1): MatchRuntimeEvent => ({
    sequence, type, matchId: 'runtime-stats', revision: 1, runtimeEpoch,
    roundNumber, roundKey: `runtime-stats:${runtimeEpoch}:round-${roundNumber}`,
    occurredAtUnixMs: 100000 + (roundNumber - 1) * 20000 + (type === 'round_completed' ? 10000 : 0),
    players, winningEngineSide: type === 'round_completed' ? 'counterTerrorist' : undefined,
});
const send = (session: ReturnType<typeof setup>, events: MatchRuntimeEvent[], snap = snapshot()) =>
    applyMatchRuntimeHeartbeat(session, { matchRuntimeEnabled: true, matchRuntime: snap, matchRuntimeEvents: events });
const routes = () => {
    const handlers = new Map<string, (req: any, res: any) => any>();
    registerPluginRoutes({ post(path: string, ...chain: any[]) { handlers.set(path, chain.at(-1)); }, get() {} } as any,
        { broadcastState() {}, requestStateBroadcast() {}, flushStateBroadcast() {}, notifyMessage() {} });
    return handlers;
};
const response = () => ({ code: 200, body: null as any, status(code: number) { this.code = code; return this; }, json(body: any) { this.body = body; } });

test('authoritative round initializes once, records combat, and finalizes KAST/survival/save without scoring twice', () => {
    const session = setup();
    const start = event(2, 'round_started');
    send(session, [event(1, 'match_started'), start]);
    send(session, [start, { ...start, sequence: 3 }]);
    assert.equal(session.players.a.stats.roundsPlayed, 1);
    assert.equal(session.players.a.sideStats.CT.roundsPlayed, 1);
    const combat = { matchId: session.matchId, revision: 1, runtimeEpoch: 1, roundKey: start.roundKey, roundNumber: 1, eventId: '00000000000000000000000000000001', occurredAtUnixMs: 105000 };
    applyPluginTelemetryBatch(session, [{
        type: 'player_hurt', payload: { ...combat, attackerSteamId: players[0].steamId, victimSteamId: players[1].steamId,
            attackerTeam: 'CT', victimTeam: 'T', damage: 30, rawDamage: 30, health: 70, weapon: 'ak47' },
    }]);
    assert.equal(session.players.a.stats.damage, 30);
    const end = event(4, 'round_completed');
    send(session, [end], snapshot({ completedRounds: 1, scoreA: 1, currentRoundKey: null }));
    send(session, [{ ...end, sequence: 5 }], snapshot({ completedRounds: 1, scoreA: 1, currentRoundKey: null }));
    assert.equal(session.players.a.stats.kastRounds, 1);
    assert.equal(session.players.a.stats.survivalSeconds, 10);
    assert.equal(session.players.b.stats.lostRoundsPlayed, 1);
    assert.equal(session.players.b.stats.saveRounds, 1);
    assert.equal(session.liveGameData!.scoreA, 1);
    assert.equal(session.liveGameData!.scoreCT, 0);
    const handler = routes().get('/api/plugin/event')!;
    for (const type of ['round_start', 'round_end']) {
        const res = response();
        handler({ body: { matchId: session.matchId, type, payload: { round: 1, winner: 'CT', players } } }, res);
        assert.equal(res.code, 200);
    }
    assert.equal(session.players.a.stats.roundsPlayed, 1);
    assert.equal(session.liveGameData!.scoreA, 1);
});

test('batched historical events use each event round and restore invalidates future checkpoints before replay', () => {
    const session = setup();
    const history = [event(1, 'match_started'), event(2, 'round_started'), event(3, 'round_completed'),
        event(4, 'round_started', 2), event(5, 'round_completed', 2), event(6, 'round_started', 3)];
    send(session, history, snapshot({ completedRounds: 2, scoreA: 2, currentRoundKey: 'runtime-stats:1:round-3' }));
    assert.deepEqual(Object.keys(session.matchRuntimeStatsCheckpoints!), ['1', '2', '3']);
    assert.equal(session.players.a.stats.roundsPlayed, 3);
    send(session, history, snapshot({ completedRounds: 2, scoreA: 2, currentRoundKey: 'runtime-stats:1:round-3' }));
    assert.equal(session.players.a.stats.roundsPlayed, 3);
    applyMatchRuntimeSnapshot(session, snapshot({ runtimeEpoch: 2, completedRounds: 1, scoreA: 1, currentRoundKey: null }));
    assert.equal(session.players.a.stats.roundsPlayed, 1);
    assert.equal(session.players.a.stats.kastRounds, 1);
    assert.deepEqual(Object.keys(session.matchRuntimeStatsCheckpoints!), ['1']);
    const replay = [event(7, 'round_started', 2, 2), event(8, 'round_completed', 2, 2)];
    send(session, replay, snapshot({ runtimeEpoch: 2, completedRounds: 2, scoreA: 2, currentRoundKey: null }));
    send(session, replay, snapshot({ runtimeEpoch: 2, completedRounds: 2, scoreA: 2, currentRoundKey: null }));
    assert.equal(session.players.a.stats.roundsPlayed, 2);
    assert.equal(session.players.a.stats.kastRounds, 2);
    assert.equal(session.players.a.stats.survivalSeconds, 20);
    assert.equal(session.players.b.stats.lostRoundsPlayed, 2);
});

test('warmup, malformed round identities, and old epoch combat cannot inflate formal statistics', () => {
    const session = setup();
    send(session, [event(1, 'round_started')]);
    assert.equal(session.players.a.stats, undefined);
    send(session, [event(2, 'match_started'), { ...event(3, 'round_started'), roundNumber: 99 }]);
    assert.equal(session.players.a.stats, undefined);
    send(session, [event(4, 'round_started')]);
    applyPluginTelemetryBatch(session, [
        { type: 'player_jump', payload: { steamId: players[0].steamId, team: 'CT', roundKey: null } },
        { type: 'player_jump', payload: { steamId: players[0].steamId, team: 'CT', matchId: session.matchId, revision: 1, runtimeEpoch: 0, roundKey: 'runtime-stats:0:round-1', roundNumber: 1 } },
    ]);
    assert.equal(session.players.a.stats.jumpCount, 0);
    assert.equal(session.players.a.stats.roundsPlayed, 1);
});

test('real snapshot route keeps 13:12 overtime live; authoritative completion alone finishes and restore reopens', async () => {
    const session = setup();
    send(session, [event(1, 'match_started')]);
    const ot = snapshot({ stage: 'overtime', completedRounds: 25, scoreA: 13, scoreB: 12, currentRoundKey: 'runtime-stats:1:round-26', overtimeIndex: 1 });
    const handler = routes().get('/api/plugin/snapshot')!;
    await handler({ body: { matchId: session.matchId, matchRuntimeEnabled: true, matchRuntimeProtocolVersion: 1, matchRuntime: ot, players: [] } }, response());
    updateMatchFinishState();
    assert.equal(session.liveGameData!.matchFinished, false);
    assert.equal(session.liveGameData!.statsLocked, false);
    assert.equal(session.liveGameData!.winnerTeam, null);
    session.players.a.gameRole = 'Soldier';
    session.players.a.stats ||= { kills: 0, deaths: 0, assists: 0, damage: 0 };
    calculateScores(session);
    assert.equal(session.players.a.scoreBreakdown?.gameResult, 0);
    await handler({ body: { matchId: session.matchId, matchRuntimeEnabled: true, matchRuntimeProtocolVersion: 1,
        matchRuntime: { ...ot, scoreA: 16, scoreB: 14, stage: 'completed' }, players: [] } }, response());
    updateMatchFinishState();
    assert.equal(session.liveGameData!.matchFinished, true);
    assert.equal(session.liveGameData!.statsLocked, true);
    assert.equal(session.liveGameData!.winnerTeam, 'A');
    await handler({ body: { matchId: session.matchId, matchRuntimeEnabled: true, matchRuntimeProtocolVersion: 1,
        matchRuntime: { ...ot, runtimeEpoch: 2 }, players: [] } }, response());
    assert.equal(session.liveGameData!.matchFinished, false);
    assert.equal(session.liveGameData!.statsLocked, false);
    assert.equal(session.liveGameData!.winnerTeam, null);
});

test('default Core competitive retains normal 13-point finish behavior', () => {
    const session = setup();
    session.matchOptions.matchRuntimeEnabled = false;
    session.liveGameData = { scoreA: 13, scoreB: 12, scoreCT: 13, scoreT: 12, currentRound: 25, lastScoredRound: 25 } as any;
    updateMatchFinishState();
    assert.equal(session.liveGameData!.matchFinished, true);
    assert.equal(session.liveGameData!.winnerTeam, 'A');
});

test('production bridge JSON: late death/damage recalculate completed round, deduplicate and restore rejects old epoch', () => {
    const fixture = JSON.parse(readFileSync('src/fixtures/authoritative-round-telemetry.json', 'utf8'));
    const session = setup();
    session.matchId = 'telemetry';
    const snap = snapshot({ matchId: 'telemetry', currentRoundKey: null, completedRounds: 1, scoreA: 1, stage: 'completed' });
    send(session, [{ ...event(1, 'match_started'), matchId: 'telemetry' }, fixture.started, fixture.completed], snap);
    const handler = routes().get('/api/plugin/event')!;
    const push = (type: string, payload: any) => handler({ body: { matchId: 'telemetry', type, payload } }, response());
    // 独立HTTP队列允许完成先到；按事件发生时刻回放战绩，完成比分保持权威值。
    push('player_death', fixture.death);
    applyPluginTelemetryBatch(session, [{ type: 'player_hurt', payload: fixture.damage }]);
    push('player_death', fixture.death);
    applyPluginTelemetryBatch(session, [{ type: 'player_hurt', payload: fixture.damage }]);
    assert.equal(session.players.a.stats.kills, 1);
    assert.equal(session.players.a.stats.damage, 100);
    assert.equal(session.players.b.stats.deaths, 1);
    assert.equal(session.players.b.stats.survivalSeconds, 0.501);
    assert.equal(session.players.a.stats.kastRounds, 1);
    assert.equal(session.players.b.stats.kastRounds, 0);
    assert.equal(session.players.b.stats.saveRounds, 0);
    assert.equal(session.liveGameData!.scoreA, 1);
    assert.equal(session.liveGameData!.matchFinished, true);
    applyPluginTelemetryBatch(session, [{ type: 'player_hurt', payload: { ...fixture.damage, eventId: 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', occurredAtUnixMs: 2500 } }]);
    applyPluginTelemetryBatch(session, [{ type: 'player_hurt', payload: fixture.closedRoundDamage }]);
    assert.equal(session.players.a.stats.damage, 100);
    send(session, [fixture.restoredStarted], { ...snap, stage: 'firstHalf', runtimeEpoch: 2, completedRounds: 0, scoreA: 0, currentRoundKey: 'telemetry:2:round-1' });
    assert.equal(session.players.a.stats.roundsPlayed, 1);
    assert.equal(session.players.a.stats.kills || 0, 0);
    assert.equal(session.players.a.stats.kastRounds || 0, 0);
    push('player_death', fixture.death);
    applyPluginTelemetryBatch(session, [{ type: 'player_hurt', payload: fixture.damage }]);
    assert.equal(session.players.a.stats.kills || 0, 0);
    assert.equal(session.players.a.stats.damage || 0, 0);
});

test('OT round 25 and later swapped-side rounds count by event players without old halftime controls', () => {
    const session = setup();
    const aT = [{ ...players[0], team: 'T' }, { ...players[1], team: 'CT' }];
    const list = [event(1, 'match_started'), event(2, 'round_started', 25), event(3, 'round_completed', 25),
        { ...event(4, 'round_started', 28), players: aT, teamAEngineSide: 'terrorist' as const },
        { ...event(5, 'round_completed', 28), players: aT, teamAEngineSide: 'terrorist' as const, winningEngineSide: 'terrorist' as const }];
    send(session, list, snapshot({ stage: 'overtime', completedRounds: 28, scoreA: 14, scoreB: 14, currentRoundKey: null, teamAEngineSide: 'terrorist', overtimeIndex: 1 }));
    assert.equal(session.players.a.stats.roundsPlayed, 2);
    assert.equal(session.players.a.sideStats.CT.roundsPlayed, 1);
    assert.equal(session.players.a.sideStats.T.roundsPlayed, 1);
    assert.equal(session.players.a.sideStats.CT.kastRounds, 1);
    assert.equal(session.players.a.sideStats.T.kastRounds, 1);
    assert.equal(session.players.b.stats.lostRoundsPlayed, 2);
    assert.ok(session.liveGameData!.roundStats![25]);
    assert.ok(session.liveGameData!.roundStats![28]);
    assert.equal(session.liveGameData!.teamLockLastAutoSideSwapCommandId, undefined);
    assert.equal(session.liveGameData!.scoreA, 14);
    assert.equal(session.liveGameData!.scoreB, 14);
    assert.equal(session.liveGameData!.matchFinished, false);
});

test('late earlier-round combat rebuilds subsequent round checkpoints while retaining later counters', () => {
    const session = setup();
    send(session, [event(1, 'match_started'), event(2, 'round_started'), event(3, 'round_completed'),
        event(4, 'round_started', 2), event(5, 'round_completed', 2)],
        snapshot({ completedRounds: 2, scoreA: 2, currentRoundKey: null }));
    const hurt = { type: 'player_hurt', payload: { matchId: session.matchId, revision: 1, runtimeEpoch: 1,
        roundNumber: 1, roundKey: 'runtime-stats:1:round-1', eventId: 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb', occurredAtUnixMs: 105000,
        attackerSteamId: players[0].steamId, victimSteamId: players[1].steamId, attackerTeam: 'CT', victimTeam: 'T',
        damage: 30, rawDamage: 30, health: 70, weapon: 'ak47' } };
    applyPluginTelemetryBatch(session, [hurt]);
    assert.equal(session.players.a.stats.damage, 30);
    assert.equal(session.players.a.stats.roundsPlayed, 2);
    assert.equal(session.players.a.stats.kastRounds, 2);
    assert.equal(session.matchRuntimeStatsCheckpoints![2].players.a.stats.damage, 30);
    applyMatchRuntimeSnapshot(session, snapshot({ runtimeEpoch: 2, completedRounds: 1, scoreA: 1, currentRoundKey: null }));
    assert.equal(session.players.a.stats.damage, 30);
    assert.equal(session.players.a.stats.roundsPlayed, 1);
});

test('same-millisecond death/fire follow producer sequence despite HTTP arrival order', () => {
    const session = setup();
    send(session, [event(1, 'match_started'), event(2, 'round_started')]);
    const raw = { matchId: session.matchId, revision: 1, runtimeEpoch: 1, roundNumber: 1,
        roundKey: 'runtime-stats:1:round-1', occurredAtUnixMs: 105000 };
    const death = { ...raw, eventId: 'cccccccccccccccccccccccccccccccc', eventSequence: 3,
        attackerSteamId: players[0].steamId, victimSteamId: players[1].steamId, attackerTeam: 'CT', victimTeam: 'T', weapon: 'ak47' };
    routes().get('/api/plugin/event')!({ body: { matchId: session.matchId, type: 'player_death', payload: death } }, response());
    applyPluginTelemetryBatch(session, [
        { type: 'weapon_fire', payload: { ...raw, eventId: 'dddddddddddddddddddddddddddddddd', eventSequence: 1, steamId: players[0].steamId, team: 'CT', weapon: 'ak47' } },
        { type: 'weapon_fire', payload: { ...raw, eventId: 'eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee', eventSequence: 2, steamId: players[0].steamId, team: 'CT', weapon: 'ak47' } },
    ]);
    assert.equal(session.players.a.stats.killShotsTotal, 2);
    assert.equal(session.players.a.stats.killShotsCount, 1);
    assert.equal(session.players.a.stats.shotsFired, 2);
});

test('formal combat arriving before start heartbeat is buffered without advancing phase and survives web persistence', () => {
    const session = setup();
    applyMatchRuntimeSnapshot(session, snapshot({ stage: 'goingLive', currentRoundKey: null }));
    const raw = { matchId: session.matchId, revision: 1, runtimeEpoch: 1, roundNumber: 1,
        roundKey: 'runtime-stats:1:round-1', occurredAtUnixMs: 105000, eventId: 'ffffffffffffffffffffffffffffffff',
        steamId: players[0].steamId, team: 'CT', weapon: 'ak47' };
    routes().get('/api/plugin/events')!({ body: { matchId: session.matchId, events: [{ type: 'weapon_fire', payload: raw }] } }, response());
    assert.equal(session.phase, GamePhase.PreGameSetup);
    assert.equal(session.players.a.stats, undefined);
    send(session, [event(1, 'match_started'), event(2, 'round_started')]);
    assert.equal(session.players.a.stats.shotsFired, 1);
    const restored = deserializeSessionSnapshot(buildSessionSnapshotPayload(session))!;
    setSession(restored);
    send(restored, [event(3, 'round_completed')], snapshot({ completedRounds: 1, scoreA: 1, currentRoundKey: null }));
    applyPluginTelemetryBatch(restored, [{ type: 'weapon_fire', payload: raw }]);
    assert.equal(restored.players.a.stats.shotsFired, 1);
    assert.equal(restored.players.a.stats.roundsPlayed, 1);
    assert.equal(restored.players.a.stats.kastRounds, 1);
});

test('restore discards pending old epoch combat even when its start checkpoint never arrived', () => {
    const session = setup();
    send(session, [event(1, 'match_started')]);
    applyPluginTelemetryBatch(session, [{ type: 'weapon_fire', payload: { matchId: session.matchId,
        revision: 1, runtimeEpoch: 1, roundNumber: 1, roundKey: 'runtime-stats:1:round-1', occurredAtUnixMs: 105000,
        eventId: '11111111111111111111111111111111', steamId: players[0].steamId, team: 'CT', weapon: 'ak47' } }]);
    assert.equal(session.matchRuntimeStatsCheckpoints, undefined);
    send(session, [event(1, 'round_started', 1, 2)], snapshot({ runtimeEpoch: 2, currentRoundKey: 'runtime-stats:2:round-1' }));
    assert.equal(session.players.a.stats.roundsPlayed, 1);
    assert.equal(session.players.a.stats.shotsFired, 0);
});

test('next-round combat leading the periodic old snapshot waits for its own authoritative start', () => {
    const session = setup();
    send(session, [event(1, 'match_started'), event(2, 'round_started')]);
    applyPluginTelemetryBatch(session, [{ type: 'player_hurt', payload: { matchId: session.matchId,
        revision: 1, runtimeEpoch: 1, roundNumber: 2, roundKey: 'runtime-stats:1:round-2', occurredAtUnixMs: 125000,
        eventId: '22222222222222222222222222222222', attackerSteamId: players[0].steamId,
        victimSteamId: players[1].steamId, attackerTeam: 'CT', victimTeam: 'T',
        damage: 30, rawDamage: 30, health: 70, weapon: 'ak47' } }]);
    assert.equal(session.matchRuntime!.completedRounds, 0);
    assert.equal(session.players.a.stats.damage, 0);
    send(session, [event(3, 'round_completed'), event(4, 'round_started', 2)],
        snapshot({ completedRounds: 1, scoreA: 1, currentRoundKey: 'runtime-stats:1:round-2' }));
    assert.equal(session.players.a.stats.damage, 30);
    assert.equal(session.players.a.stats.roundsPlayed, 2);
    assert.equal(session.players.a.stats.kastRounds, 1);
});
