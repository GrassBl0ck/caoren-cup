import assert from 'node:assert/strict';
import test from 'node:test';
import {
    advancePhase,
    finishDraftPick,
    finishMapVote,
    finishSideVote,
    resumeRestoredPregameFlow,
} from './game-flow-manager';
import { clearFlowUndoHistory, exportFlowUndoState } from './flow-undo-manager';
import {
    clearAllFlowTimers,
    getDraftPickTimer,
    getMapVoteTimer,
    getSideVoteTimer,
} from './game-timers';
import { createInitialSession, setSession } from './session-manager';
import { GamePhase } from './types';

test.afterEach(() => {
    clearAllFlowTimers();
    clearFlowUndoHistory();
});

test('manual phase advance records one unified checkpoint with its actor', () => {
    const session = createInitialSession();
    session.players.admin = { playerId: 'admin', name: 'Admin', role: 'Admin', isReady: false };
    session.players.player = { playerId: 'player', name: 'Player', role: 'Player', isReady: false };
    session.playerOrder = ['admin', 'player'];
    setSession(session);

    assert.equal(advancePhase(GamePhase.Lobby, GamePhase.CaptainSelection, 'Admin', 'admin'), true);

    const entries = exportFlowUndoState().entries;
    assert.equal(session.phase, GamePhase.CaptainSelection);
    assert.equal(entries.length, 1);
    assert.equal(entries[0].actorId, 'admin');
    assert.equal(entries[0].restorePhase, GamePhase.Lobby);
});

test('delayed roll transition records its checkpoint only when the phase actually changes', async () => {
    const session = createInitialSession();
    session.phase = GamePhase.Roll;
    session.rollValues = { A: 91, B: 63 };
    session.captains = { A: 'captain-a', B: 'captain-b' };
    session.players['captain-a'] = { playerId: 'captain-a', name: 'A', role: 'Player', isReady: false };
    session.players['captain-b'] = { playerId: 'captain-b', name: 'B', role: 'Player', isReady: false };
    session.playerOrder = ['captain-a', 'captain-b'];
    setSession(session);

    assert.equal(advancePhase(GamePhase.Roll, GamePhase.PlayerDraft, 'Admin', 'admin'), true);
    assert.equal(exportFlowUndoState().entries.length, 0);

    await new Promise(resolve => setTimeout(resolve, 3_100));

    assert.equal(session.phase, GamePhase.PlayerDraft);
    assert.equal(exportFlowUndoState().entries.length, 1);
    assert.equal(exportFlowUndoState().entries[0].restorePhase, GamePhase.Roll);
});

test('map and side vote completion record system phase checkpoints', () => {
    const map = createInitialSession();
    map.phase = GamePhase.MapBan;
    map.mapPool = ['Dust II', 'Inferno'];
    map.banSequence = ['A'];
    map.mapVote = { team: 'A', votes: { player: 'Dust II' }, timeoutAt: Date.now() + 10_000, banCount: 1 };
    map.currentBanTeam = 'A';
    map.players.player = { playerId: 'player', name: 'Player', role: 'Player', rosterTeam: 'A', isReady: false };
    map.playerOrder = ['player'];
    map.teams.A.players = ['player'];
    setSession(map);

    finishMapVote('manual');

    assert.equal(map.phase, GamePhase.SidePick);
    assert.equal(exportFlowUndoState().entries.length, 1);
    assert.equal(exportFlowUndoState().entries[0].actorId, 'SYSTEM');
    assert.equal(exportFlowUndoState().entries[0].restorePhase, GamePhase.MapBan);

    clearFlowUndoHistory();
    const side = createInitialSession();
    side.phase = GamePhase.SidePick;
    side.selectedMap = 'Inferno';
    side.sidePickTeam = 'A';
    side.sideVote = { team: 'A', votes: { player: 'CT' }, timeoutAt: Date.now() + 10_000 };
    side.players.player = { playerId: 'player', name: 'Player', role: 'Player', rosterTeam: 'A', isReady: false };
    side.playerOrder = ['player'];
    side.teams.A.players = ['player'];
    setSession(side);

    finishSideVote('manual');

    assert.equal(side.phase, GamePhase.PreGameSetup);
    assert.equal(exportFlowUndoState().entries.length, 1);
    assert.equal(exportFlowUndoState().entries[0].actorId, 'SYSTEM');
    assert.equal(exportFlowUndoState().entries[0].restorePhase, GamePhase.SidePick);
});

test('completed draft automatically advances and records one system checkpoint', async () => {
    const session = createInitialSession();
    session.phase = GamePhase.PlayerDraft;
    session.draftOrder = [];
    session.draftOriginalOrder = [];
    session.draftIndex = 0;
    session.draftCaptainsActive = true;
    session.banSequence = ['A'];
    setSession(session);

    finishDraftPick('manual');
    assert.equal(exportFlowUndoState().entries.length, 0);

    await new Promise(resolve => setTimeout(resolve, 1_600));

    assert.equal(session.phase, GamePhase.MapBan);
    assert.equal(exportFlowUndoState().entries.length, 1);
    assert.equal(exportFlowUndoState().entries[0].actorId, 'SYSTEM');
    assert.equal(exportFlowUndoState().entries[0].restorePhase, GamePhase.PlayerDraft);
});

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

test('restored completed pregame phases stay paused without starting a timer', () => {
    const draft = createInitialSession();
    draft.phase = GamePhase.PlayerDraft;
    draft.draftOrder = ['A'];
    draft.draftOriginalOrder = ['A'];
    draft.draftIndex = 1;
    draft.draftCaptainsActive = true;
    setSession(draft);
    resumeRestoredPregameFlow();
    assert.equal(getDraftPickTimer(), null);
    assert.equal(draft.timerEndAt, null);

    const map = createInitialSession();
    map.phase = GamePhase.MapBan;
    map.mapVote = undefined;
    setSession(map);
    resumeRestoredPregameFlow();
    assert.equal(getMapVoteTimer(), null);
    assert.equal(map.timerEndAt, null);

    const side = createInitialSession();
    side.phase = GamePhase.SidePick;
    side.sideVote = undefined;
    setSession(side);
    resumeRestoredPregameFlow();
    assert.equal(getSideVoteTimer(), null);
    assert.equal(side.timerEndAt, null);
});
