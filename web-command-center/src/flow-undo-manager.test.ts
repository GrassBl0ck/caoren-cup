import assert from 'node:assert/strict';
import test from 'node:test';
import { createInitialSession } from './session-manager';
import { GamePhase, Player } from './types';
import {
    clearFlowUndoHistory,
    exportFlowUndoState,
    getFlowUndoStatus,
    importFlowUndoState,
    pushFlowUndoCheckpoint,
    undoLatestFlowAction,
} from './flow-undo-manager';

const makePlayer = (playerId: string, name: string, role: Player['role'] = 'Player'): Player => ({
    playerId,
    name,
    role,
    isReady: false,
    isOnline: true,
});

const addPlayer = (session: ReturnType<typeof createInitialSession>, player: Player) => {
    session.players[player.playerId] = player;
    session.playerOrder.push(player.playerId);
};

test.beforeEach(() => clearFlowUndoHistory());

test('undo restores flow fields while preserving current identities, new players, and kicked players', () => {
    const session = createInitialSession();
    session.phase = GamePhase.PlayerDraft;
    const admin = makePlayer('admin', 'Admin', 'Admin');
    const kept: Player = { ...makePlayer('kept', 'Kept'), identityId: 'identity-before', rosterTeam: 'A' };
    const kicked: Player = { ...makePlayer('kicked', 'Kicked'), rosterTeam: 'B' };
    addPlayer(session, admin);
    addPlayer(session, kept);
    addPlayer(session, kicked);
    session.teams.A.players = ['kept'];
    session.teams.B.players = ['kicked'];
    session.draftIndex = 1;

    pushFlowUndoCheckpoint(session, {
        actionType: 'ASSIGN_ROSTER_TEAM',
        actorId: admin.playerId,
        actorName: admin.name,
        summary: '将 Kept 分入 B 队',
    });

    session.phase = GamePhase.MapBan;
    session.draftIndex = 4;
    kept.rosterTeam = 'B';
    kept.identityId = 'identity-current';
    session.teams.A.players = [];
    session.teams.B.players = ['kept'];
    delete session.players.kicked;
    session.playerOrder = session.playerOrder.filter(id => id !== 'kicked');
    const joined = { ...makePlayer('joined', 'Joined', 'Spectator'), identityId: 'identity-joined', rosterTeam: 'A' as const, isReady: true };
    addPlayer(session, joined);
    session.teams.A.players.push('joined');

    const result = undoLatestFlowAction(session, admin);

    assert.equal(result.ok, true);
    assert.equal(session.phase, GamePhase.PlayerDraft);
    assert.equal(session.draftIndex, 1);
    assert.equal(session.players.kept.rosterTeam, 'A');
    assert.equal(session.players.kept.identityId, 'identity-current');
    assert.equal(session.players.kicked, undefined);
    assert.equal(session.players.joined.identityId, 'identity-joined');
    assert.equal(session.players.joined.role, 'Spectator');
    assert.equal(session.players.joined.rosterTeam, undefined);
    assert.equal(session.players.joined.isReady, false);
    assert.deepEqual(session.teams.A.players, ['kept']);
    assert.deepEqual(session.teams.B.players, []);
});

test('official admin can undo any checkpoint while duel temporary admin can only undo their own top checkpoint', () => {
    const session = createInitialSession();
    session.phase = GamePhase.PreGameSetup;
    const admin = makePlayer('admin', 'Admin', 'Admin');
    const temporary = makePlayer('temporary', 'Temporary');
    const other = makePlayer('other', 'Other');
    addPlayer(session, admin);
    addPlayer(session, temporary);
    addPlayer(session, other);
    session.duelTempAdminId = temporary.playerId;

    pushFlowUndoCheckpoint(session, {
        actionType: 'ADVANCE_PHASE', actorId: other.playerId, actorName: other.name, summary: '其他人的操作',
    });
    assert.equal(getFlowUndoStatus(session, temporary).canUndo, false);
    assert.match(getFlowUndoStatus(session, temporary).disabledReason || '', /只能撤销自己的操作/);
    assert.equal(undoLatestFlowAction(session, temporary).ok, false);
    assert.equal(undoLatestFlowAction(session, admin).ok, true);

    pushFlowUndoCheckpoint(session, {
        actionType: 'ASSIGN_ROSTER_TEAM', actorId: temporary.playerId, actorName: temporary.name, summary: '临时管理员分队',
    });
    assert.equal(getFlowUndoStatus(session, temporary).canUndo, true);
    assert.equal(undoLatestFlowAction(session, temporary).ok, true);
});

test('history is capped at fifty entries and survives import only for the same session', () => {
    const session = createInitialSession();
    session.phase = GamePhase.CaptainSelection;
    const admin = makePlayer('admin', 'Admin', 'Admin');
    addPlayer(session, admin);
    for (let index = 0; index < 55; index += 1) {
        pushFlowUndoCheckpoint(session, {
            actionType: 'ADVANCE_PHASE', actorId: admin.playerId, actorName: admin.name, summary: `操作 ${index}`,
        });
    }
    assert.equal(exportFlowUndoState().entries.length, 50);
    assert.equal(exportFlowUndoState().entries[0].summary, '操作 5');

    const persisted = exportFlowUndoState();
    clearFlowUndoHistory();
    assert.equal(importFlowUndoState(persisted, session.sessionId), true);
    assert.equal(getFlowUndoStatus(session, admin).count, 50);

    clearFlowUndoHistory();
    assert.equal(importFlowUndoState(persisted, 'different-session'), false);
    assert.equal(exportFlowUndoState().entries.length, 0);
});

test('live and postgame phases cannot be undone', () => {
    const session = createInitialSession();
    const admin = makePlayer('admin', 'Admin', 'Admin');
    addPlayer(session, admin);
    pushFlowUndoCheckpoint(session, {
        actionType: 'ADVANCE_PHASE', actorId: admin.playerId, actorName: admin.name, summary: '进入正式比赛前',
    });
    session.phase = GamePhase.LiveGame;
    assert.equal(getFlowUndoStatus(session, admin).canUndo, false);
    assert.match(getFlowUndoStatus(session, admin).disabledReason || '', /正式比赛/);
    assert.equal(undoLatestFlowAction(session, admin).ok, false);
});

test('import drops a damaged checkpoint instead of exposing it for restore', () => {
    const session = createInitialSession();
    session.phase = GamePhase.PlayerDraft;
    const admin = makePlayer('admin', 'Admin', 'Admin');
    addPlayer(session, admin);
    pushFlowUndoCheckpoint(session, {
        actionType: 'ADVANCE_PHASE', actorId: admin.playerId, actorName: admin.name, summary: '损坏记录',
    });
    const persisted: any = exportFlowUndoState();
    persisted.entries[0].snapshot = {};

    clearFlowUndoHistory();
    assert.equal(importFlowUndoState(persisted, session.sessionId), true);
    assert.equal(getFlowUndoStatus(session, admin).count, 0);
    assert.equal(getFlowUndoStatus(session, admin).canUndo, false);
});
