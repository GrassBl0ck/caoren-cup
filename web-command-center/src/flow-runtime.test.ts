import assert from 'node:assert/strict';
import test from 'node:test';
import { resumeRestoredPregameFlow } from './game-flow-manager';
import {
    clearAllFlowTimers,
    getDraftPickTimer,
    getMapVoteTimer,
    getSideVoteTimer,
} from './game-timers';
import { createInitialSession, setSession } from './session-manager';
import { GamePhase } from './types';

test.afterEach(() => clearAllFlowTimers());

test('restored map vote keeps votes and receives a fresh full timer', () => {
    const session = createInitialSession();
    session.phase = GamePhase.MapBan;
    session.bannedMaps = ['Dust II'];
    session.mapVote = {
        team: 'B',
        votes: { player: 'Inferno' },
        timeoutAt: Date.now() - 10_000,
        banCount: 1,
    };
    session.currentBanTeam = 'B';
    setSession(session);
    const before = Date.now();

    resumeRestoredPregameFlow();

    assert.deepEqual(session.mapVote?.votes, { player: 'Inferno' });
    assert.ok(Number(session.mapVote?.timeoutAt) > before);
    assert.equal(session.timerEndAt, session.mapVote?.timeoutAt);
    assert.equal(session.timerPhase, GamePhase.MapBan);
    assert.notEqual(getMapVoteTimer(), null);
});

test('restored active draft and side vote receive new timers without automatic delayed advance', () => {
    const draft = createInitialSession();
    draft.phase = GamePhase.PlayerDraft;
    draft.draftOrder = ['A'];
    draft.draftOriginalOrder = ['A'];
    draft.draftIndex = 0;
    draft.draftCaptainsActive = true;
    draft.players.player = { playerId: 'player', name: 'Player', role: 'Player', isReady: false };
    draft.playerOrder = ['player'];
    setSession(draft);
    resumeRestoredPregameFlow();
    assert.ok(Number(draft.draftPickTimeoutAt) > Date.now());
    assert.notEqual(getDraftPickTimer(), null);
    clearAllFlowTimers();

    const side = createInitialSession();
    side.phase = GamePhase.SidePick;
    side.sideVote = { team: 'A', votes: { player: 'CT' }, timeoutAt: Date.now() - 1 };
    setSession(side);
    resumeRestoredPregameFlow();
    assert.deepEqual(side.sideVote?.votes, { player: 'CT' });
    assert.ok(Number(side.sideVote?.timeoutAt) > Date.now());
    assert.notEqual(getSideVoteTimer(), null);
});

test('restoring flow cancels a stale delayed phase callback', async () => {
    const session = createInitialSession();
    session.phase = GamePhase.Roll;
    let fired = false;
    session.rollTimeout = setTimeout(() => { fired = true; }, 10);
    setSession(session);

    resumeRestoredPregameFlow();
    await new Promise(resolve => setTimeout(resolve, 30));

    assert.equal(fired, false);
    assert.equal(session.rollTimeout, undefined);
});
