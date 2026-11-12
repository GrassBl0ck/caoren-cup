import assert from 'node:assert/strict';
import test from 'node:test';
import { createInitialSession, setSession } from './session-manager';
import { applyMatchOptions } from './game-flow-manager';
import { enqueueMatchRuntimeCommand, isBridgeMatchRuntimeEnabled } from './match-runtime-orchestrator';
import { applyMatchRuntimeHeartbeat, shouldTreatRoundStartAsLegacyMatchzy } from './match-runtime-projection';
import { applyMatchRuntimeAdminAction, MatchRuntimeConfirmationStore } from './match-runtime-admin-operations';

test('existing caoren Core sessions do not implicitly opt into bridge control', () => {
    const session = createInitialSession();
    assert.equal(session.matchOptions.matchController, 'caoren');
    assert.equal(session.matchOptions.matchRuntimeEnabled, false);
    assert.equal(isBridgeMatchRuntimeEnabled(session), false);
    delete session.matchOptions.matchRuntimeEnabled;
    assert.equal(isBridgeMatchRuntimeEnabled(session), false);
    assert.equal(enqueueMatchRuntimeCommand(session, 'abort'), null);
    assert.equal(shouldTreatRoundStartAsLegacyMatchzy(session, 'round_start'), false);
});

test('legacy and disabled heartbeats cannot project runtime state or promote a match', () => {
    const session = createInitialSession();
    session.matchOptions.matchRuntimeEnabled = true;
    const before = session.phase;
    const event = { type: 'match_started', matchId: session.matchId, runtimeEpoch: 1, sequence: 1 };
    assert.deepEqual(applyMatchRuntimeHeartbeat(session, { matchRuntimeEvents: [event as any] }), { snapshotApplied: false, eventsApplied: 0 });
    assert.equal(session.phase, before);
});

test('only competitive and ability can opt in; Random, duel and RUSH keep their current Core path', () => {
    for (const mode of ['random', 'duel', 'rush', 'undercover', 'unbalanced'] as const) {
        const session = createInitialSession();
        setSession(session);
        const options = applyMatchOptions({ ...session.matchOptions, gameModeId: mode, matchRuntimeEnabled: true, matchController: 'caoren' });
        assert.equal(options.matchRuntimeEnabled, false, mode);
        assert.equal(isBridgeMatchRuntimeEnabled(session), false, mode);
    }
    for (const mode of ['competitive', 'ability'] as const) {
        const session = createInitialSession();
        setSession(session);
        applyMatchOptions({ ...session.matchOptions, gameModeId: mode, matchRuntimeEnabled: true, matchController: 'caoren' });
        assert.equal(isBridgeMatchRuntimeEnabled(session), true, mode);
    }
});

test('choosing a caoren controller without explicit enabled still leaves bridge runtime disabled', () => {
    const session = createInitialSession();
    const result = applyMatchRuntimeAdminAction(session, 'SET_MATCH_CONTROLLER', { controller: 'caoren' }, 'admin', new MatchRuntimeConfirmationStore());
    assert.equal(result.ok, true);
    assert.equal(session.matchOptions.matchRuntimeEnabled, false);
});

test('disabling opt-in clears old runtime identity even when the controller name does not change', () => {
    const session = createInitialSession();
    session.matchOptions.matchRuntimeEnabled = true;
    session.matchRuntime = { matchId: session.matchId, runtimeEpoch: 7 } as any;
    setSession(session);
    applyMatchOptions({ ...session.matchOptions, matchRuntimeEnabled: false });
    assert.equal(session.matchRuntime, undefined);
    assert.equal(session.matchOptions.matchRuntimeEnabled, false);
});

test('saving MatchZy rollback selection preserves it while bridge runtime is disabled', () => {
    const session = createInitialSession();
    setSession(session);
    applyMatchOptions({ ...session.matchOptions, matchController: 'matchzy', matchRuntimeEnabled: false });
    assert.equal(session.matchOptions.matchController, 'matchzy');
    assert.equal(session.matchOptions.matchRuntimeEnabled, false);
    applyMatchOptions({ gameModeId: 'competitive' });
    assert.equal(session.matchOptions.matchController, 'matchzy');
});

test('enabling before the first bridge snapshot remains an explicit selection, not proof of a running match', () => {
    const session = createInitialSession();
    setSession(session);
    applyMatchOptions({ ...session.matchOptions, matchRuntimeEnabled: true, matchController: 'caoren' });
    assert.equal(session.matchOptions.matchRuntimeEnabled, true);
    assert.equal(session.matchRuntime, undefined);
    // No advertised protocol means no load/abort can be sent to an old bridge.
    assert.equal(enqueueMatchRuntimeCommand(session, 'abort'), null);
});
