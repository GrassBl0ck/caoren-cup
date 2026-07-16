import assert from 'node:assert/strict';
import test from 'node:test';
import {
    clearFlowUndoHistory,
    exportFlowUndoState,
    getFlowUndoStatus,
    pushFlowUndoCheckpoint,
} from './flow-undo-manager';
import { createInitialSession, getSession, setSession } from './session-manager';
import {
    buildSessionSnapshotPayload,
    restoreSessionSnapshotData,
} from './session-persistence';
import { GamePhase, Player } from './types';

const admin: Player = {
    playerId: 'admin',
    name: 'Admin',
    role: 'Admin',
    isReady: false,
    isOnline: true,
};

test.beforeEach(() => {
    setSession(createInitialSession());
    clearFlowUndoHistory();
});

test('schema v2 persists complete pregame flow and undo history without login secrets', () => {
    const session = createInitialSession();
    session.phase = GamePhase.MapBan;
    session.players.admin = { ...admin, bindCode: 'BIND-SECRET', sessionCode: 'SESSION-SECRET' };
    session.playerOrder = ['admin'];
    session.draftOrder = ['A', 'B'];
    session.draftOriginalOrder = ['A', 'B'];
    session.draftIndex = 1;
    session.bannedMaps = ['Dust II'];
    session.currentBanTeam = 'B';
    session.mapVote = { team: 'B', votes: { admin: 'Inferno' }, timeoutAt: Date.now() + 5_000, banCount: 1 };
    session.timerEndAt = session.mapVote.timeoutAt;
    session.timerPhase = GamePhase.MapBan;
    pushFlowUndoCheckpoint(session, {
        actionType: 'ADMIN_BAN_MAP', actorId: 'admin', actorName: 'Admin', summary: '强 Ban Inferno',
    });

    const payload = buildSessionSnapshotPayload(session);
    const serialized = JSON.stringify(payload);

    assert.equal(payload.version, 2);
    assert.equal(serialized.includes('BIND-SECRET'), false);
    assert.equal(serialized.includes('SESSION-SECRET'), false);
    assert.equal(payload.flowUndo.entries.length, 1);

    setSession(createInitialSession());
    clearFlowUndoHistory();
    assert.equal(restoreSessionSnapshotData(payload), true);
    assert.equal(getSession().phase, GamePhase.MapBan);
    assert.deepEqual(getSession().draftOriginalOrder, ['A', 'B']);
    assert.deepEqual(getSession().bannedMaps, ['Dust II']);
    assert.equal(getSession().mapVote?.votes.admin, 'Inferno');
    assert.equal(getFlowUndoStatus(getSession(), getSession().players.admin).count, 1);
});

test('schema v1 restores current session with empty undo history', () => {
    const current = createInitialSession();
    current.players.admin = { ...admin };
    current.playerOrder = ['admin'];
    pushFlowUndoCheckpoint(current, {
        actionType: 'ADVANCE_PHASE', actorId: 'admin', actorName: 'Admin', summary: '旧历史',
    });
    assert.equal(exportFlowUndoState().entries.length, 1);

    const v1 = {
        version: 1,
        savedAt: Date.now(),
        session: {
            ...current,
            phase: GamePhase.CaptainSelection,
            rollTimeout: undefined,
        },
    };

    assert.equal(restoreSessionSnapshotData(v1), true);
    assert.equal(getSession().phase, GamePhase.CaptainSelection);
    assert.equal(exportFlowUndoState().entries.length, 0);
});

test('unknown snapshot schema is rejected without replacing current state', () => {
    const session = createInitialSession();
    session.phase = GamePhase.SidePick;
    setSession(session);

    assert.equal(restoreSessionSnapshotData({ version: 99, session: {} }), false);
    assert.equal(getSession().phase, GamePhase.SidePick);
});
