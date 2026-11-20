import assert from 'node:assert/strict';
import test from 'node:test';
import { FormalServerLifecycle } from './formal-server-lifecycle';
import { createInitialSession } from './session-manager';
import { GamePhase } from './types';
import type { MatchRuntimeSnapshot } from './match-runtime-types';

test('opens only one instance and only the expected instance and map mark it ready', async () => {
    const session = createInitialSession();
    session.phase = GamePhase.PreGameSetup;
    session.matchOptions.matchRuntimeEnabled = true;
    session.matchOptions.matchController = 'caoren';
    session.selectedMap = 'Mirage';
    session.selectedSide = 'CT';
    const calls: string[] = [];
    const lifecycle = new FormalServerLifecycle({
        async start(matchId, map) { calls.push(matchId + ':' + map); },
        async stop() { assert.fail('cannot stop an unfinished match'); },
        async archive() { assert.fail('cannot archive an unfinished match'); },
    });

    await Promise.all([lifecycle.open(session, () => {}), lifecycle.open(session, () => {})]);
    assert.deepEqual(calls, [session.matchId + ':de_mirage']);
    lifecycle.observe(session, 'different-match', 'de_mirage', () => {});
    lifecycle.observe(session, session.matchId, 'de_ancient', () => {});
    assert.equal(session.formalServer?.stage, 'starting');
    lifecycle.observe(session, session.matchId, 'de_mirage', () => {});
    assert.equal(session.formalServer?.stage, 'ready');
});

test('game remains running until archived data is ready, then only its instance stops', async () => {
    const session = createInitialSession();
    session.phase = GamePhase.Scoreboard;
    session.matchRuntime = { matchId: session.matchId, stage: 'completed', lastEventSequence: 11 } as MatchRuntimeSnapshot;
    session.matchRuntimeLastEventSequence = 11;
    session.formalServer = { matchId: session.matchId, mapName: 'de_mirage', stage: 'ready' };
    const stopped: string[] = [];
    let archived = false;
    const lifecycle = new FormalServerLifecycle({
        async start() { assert.fail('cannot start a finished match'); },
        async archive() { return archived; },
        async stop(matchId) { stopped.push(matchId); },
    });

    lifecycle.observe(session, session.matchId, 'de_mirage', () => {});
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(session.formalServer.stage, 'archiving');
    assert.deepEqual(stopped, []);

    archived = true;
    lifecycle.observe(session, session.matchId, 'de_mirage', () => {});
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(session.formalServer.stage, 'closed');
    assert.deepEqual(stopped, [session.matchId]);
});

test('resumes closing a settled instance after web restart without another game heartbeat', async () => {
    const session = createInitialSession();
    session.phase = GamePhase.Scoreboard;
    session.matchRuntime = { matchId: session.matchId, stage: 'completed', lastEventSequence: 12 } as MatchRuntimeSnapshot;
    session.matchRuntimeLastEventSequence = 11;
    session.formalServer = { matchId: session.matchId, mapName: 'de_mirage', stage: 'stopping' };
    const stopped: string[] = [];
    const lifecycle = new FormalServerLifecycle({
        async start() { assert.fail('cannot start a finished match'); },
        async archive() { return true; },
        async stop(matchId) { stopped.push(matchId); },
    });
    lifecycle.poll(session, () => {});
    await new Promise(resolve => setImmediate(resolve));
    assert.deepEqual(stopped, []);

    session.matchRuntimeLastEventSequence = 12;
    session.liveGameData = { formalTelemetryPending: true } as NonNullable<typeof session.liveGameData>;
    lifecycle.poll(session, () => {});
    await new Promise(resolve => setImmediate(resolve));
    assert.deepEqual(stopped, []);

    session.liveGameData.formalTelemetryPending = false;
    lifecycle.poll(session, () => {});
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(session.formalServer.stage, 'closed');
    assert.deepEqual(stopped, [session.matchId]);
    lifecycle.poll(session, () => {});
    assert.deepEqual(stopped, [session.matchId]);
});

test('a missing initial game heartbeat allows the administrator to retry launch', async () => {
    const session = createInitialSession();
    session.phase = GamePhase.PreGameSetup;
    session.matchOptions.matchRuntimeEnabled = true;
    session.matchOptions.matchController = 'caoren';
    session.selectedMap = 'Mirage';
    session.selectedSide = 'CT';
    let starts = 0;
    let now = 100;
    const lifecycle = new FormalServerLifecycle({
        async start() { starts++; },
        async archive() { return false; },
        async stop() { assert.fail('cannot stop an unfinished match'); },
    }, () => now);
    await lifecycle.open(session, () => {});
    lifecycle.poll(session, () => {});
    assert.equal(session.formalServer?.stage, 'starting');

    now += 45_000;
    lifecycle.poll(session, () => {});
    assert.equal(session.formalServer?.stage, 'error');
    await lifecycle.open(session, () => {});
    assert.equal(starts, 2);
});
