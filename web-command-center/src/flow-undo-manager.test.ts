import assert from 'node:assert/strict';
import test from 'node:test';
import { createInitialSession } from './session-manager';
import { GamePhase, Player } from './types';
import {
    clearFlowUndoHistory,
    exportFlowUndoState,
    getFlowUndoStatus,
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

const currentRequest = (session: ReturnType<typeof createInitialSession>, actor: Player) => {
    const status = getFlowUndoStatus(session, actor);
    assert.ok(status.latest);
    return {
        expectedPhase: session.phase,
        expectedHistoryDepth: status.count,
        expectedEntryId: status.latest.id,
    };
};

test.beforeEach(() => clearFlowUndoHistory());

test('undo restores flow fields while preserving current identities and later spectators', () => {
    const session = createInitialSession();
    session.phase = GamePhase.PlayerDraft;
    const admin = makePlayer('admin', 'Admin', 'Admin');
    const kept: Player = { ...makePlayer('kept', 'Kept'), identityId: 'identity-before', rosterTeam: 'A' };
    addPlayer(session, admin);
    addPlayer(session, kept);
    session.teams.A.players = ['kept'];
    session.draftIndex = 1;

    pushFlowUndoCheckpoint(session, {
        actionType: 'ASSIGN_ROSTER_TEAM',
        actorId: admin.playerId,
        actorName: admin.name,
        summary: 'Move Kept to team B',
    });

    session.phase = GamePhase.MapBan;
    session.draftIndex = 4;
    kept.rosterTeam = 'B';
    kept.identityId = 'identity-current';
    session.teams.A.players = [];
    session.teams.B.players = ['kept'];
    const joined = { ...makePlayer('joined', 'Joined', 'Spectator'), identityId: 'identity-joined', isReady: true };
    addPlayer(session, joined);

    const result = undoLatestFlowAction(session, admin, currentRequest(session, admin) as any);

    assert.equal(result.ok, true);
    assert.equal(session.phase, GamePhase.PlayerDraft);
    assert.equal(session.draftIndex, 1);
    assert.equal(session.players.kept.rosterTeam, 'A');
    assert.equal(session.players.kept.identityId, 'identity-current');
    assert.equal(session.players.joined.identityId, 'identity-joined');
    assert.equal(session.players.joined.role, 'Spectator');
    assert.equal(session.players.joined.rosterTeam, undefined);
    assert.equal(session.players.joined.isReady, false);
    assert.deepEqual(session.teams.A.players, ['kept']);
    assert.deepEqual(session.teams.B.players, []);
});

test('missing required participant rejects undo and clears invalid history', () => {
    const session = createInitialSession();
    session.phase = GamePhase.PlayerDraft;
    const admin = makePlayer('admin', 'Admin', 'Admin');
    const drafted: Player = { ...makePlayer('drafted', 'Drafted'), rosterTeam: 'A' };
    addPlayer(session, admin);
    addPlayer(session, drafted);
    session.teams.A.players = ['drafted'];
    pushFlowUndoCheckpoint(session, {
        actionType: 'ADVANCE_PHASE', actorId: admin.playerId, actorName: admin.name, summary: 'Advance draft',
    });
    session.phase = GamePhase.MapBan;
    delete session.players.drafted;
    session.playerOrder = session.playerOrder.filter(id => id !== drafted.playerId);
    const request = currentRequest(session, admin);

    const result = undoLatestFlowAction(session, admin, request as any);

    assert.equal(result.ok, false);
    assert.match('reason' in result ? result.reason : '', /参赛者.*不在|失效/);
    assert.equal(session.phase, GamePhase.MapBan);
    assert.equal(getFlowUndoStatus(session, admin).count, 0);
});

test('required participant becoming spectator rejects undo and clears invalid history', () => {
    const session = createInitialSession();
    session.phase = GamePhase.CaptainSelection;
    const admin = makePlayer('admin', 'Admin', 'Admin');
    const captain: Player = { ...makePlayer('captain', 'Captain'), rosterTeam: 'A' };
    addPlayer(session, admin);
    addPlayer(session, captain);
    session.captains.A = captain.playerId;
    session.teams.A.players = [captain.playerId];
    pushFlowUndoCheckpoint(session, {
        actionType: 'ADVANCE_PHASE', actorId: admin.playerId, actorName: admin.name, summary: 'Advance captain selection',
    });
    session.phase = GamePhase.Roll;
    captain.role = 'Spectator';
    const request = currentRequest(session, admin);

    const result = undoLatestFlowAction(session, admin, request as any);

    assert.equal(result.ok, false);
    assert.match('reason' in result ? result.reason : '', /观战|失效/);
    assert.equal(session.phase, GamePhase.Roll);
    assert.equal(getFlowUndoStatus(session, admin).count, 0);
});

test('stale duplicate request cannot pop a second checkpoint', () => {
    const session = createInitialSession();
    const admin = makePlayer('admin', 'Admin', 'Admin');
    addPlayer(session, admin);
    session.phase = GamePhase.Lobby;
    pushFlowUndoCheckpoint(session, {
        actionType: 'ADVANCE_PHASE', actorId: admin.playerId, actorName: admin.name, summary: 'First advance',
    });
    session.phase = GamePhase.CaptainSelection;
    pushFlowUndoCheckpoint(session, {
        actionType: 'ADVANCE_PHASE', actorId: admin.playerId, actorName: admin.name, summary: 'Second advance',
    });
    session.phase = GamePhase.Roll;
    const request = currentRequest(session, admin);

    assert.equal(undoLatestFlowAction(session, admin, request as any).ok, true);
    assert.equal(session.phase, GamePhase.CaptainSelection);
    assert.equal(undoLatestFlowAction(session, admin, request as any).ok, false);
    assert.equal(session.phase, GamePhase.CaptainSelection);
    assert.equal(getFlowUndoStatus(session, admin).count, 1);
});

test('official admin can undo any checkpoint while duel temporary admin can only undo their phase advance', () => {
    const session = createInitialSession();
    const admin = makePlayer('admin', 'Admin', 'Admin');
    const temporary = makePlayer('temporary', 'Temporary');
    addPlayer(session, admin);
    addPlayer(session, temporary);
    session.duelTempAdminId = temporary.playerId;
    session.phase = GamePhase.Lobby;

    pushFlowUndoCheckpoint(session, {
        actionType: 'ASSIGN_ROSTER_TEAM', actorId: temporary.playerId, actorName: temporary.name, summary: 'Temporary assignment',
    });
    assert.equal(getFlowUndoStatus(session, temporary).canUndo, false);
    assert.equal(undoLatestFlowAction(session, temporary, currentRequest(session, admin) as any).ok, false);
    assert.equal(undoLatestFlowAction(session, admin, currentRequest(session, admin) as any).ok, true);

    pushFlowUndoCheckpoint(session, {
        actionType: 'ADVANCE_PHASE', actorId: temporary.playerId, actorName: temporary.name, summary: 'Start duel setup',
    });
    session.phase = GamePhase.PreGameSetup;
    assert.equal(getFlowUndoStatus(session, temporary).canUndo, true);
    assert.equal(undoLatestFlowAction(session, temporary, currentRequest(session, temporary) as any).ok, true);
    assert.equal(session.phase, GamePhase.Lobby);
});

test('history is capped at fifty entries and status exposes safe concurrency fields', () => {
    const session = createInitialSession();
    session.phase = GamePhase.CaptainSelection;
    const admin = makePlayer('admin', 'Admin', 'Admin');
    addPlayer(session, admin);
    for (let index = 0; index < 55; index += 1) {
        pushFlowUndoCheckpoint(session, {
            actionType: 'ADVANCE_PHASE', actorId: admin.playerId, actorName: admin.name, summary: `Action ${index}`,
        });
    }
    const status = getFlowUndoStatus(session, admin) as any;
    assert.equal(exportFlowUndoState().entries.length, 50);
    assert.equal(exportFlowUndoState().entries[0].summary, 'Action 5');
    assert.equal(status.historyDepth, 50);
    assert.equal(status.targetPhase, GamePhase.CaptainSelection);
    assert.equal(typeof status.latest.id, 'string');
});

test('live and postgame phases cannot be undone', () => {
    const session = createInitialSession();
    const admin = makePlayer('admin', 'Admin', 'Admin');
    addPlayer(session, admin);
    pushFlowUndoCheckpoint(session, {
        actionType: 'ADVANCE_PHASE', actorId: admin.playerId, actorName: admin.name, summary: 'Before live game',
    });
    session.phase = GamePhase.LiveGame;
    assert.equal(getFlowUndoStatus(session, admin).canUndo, false);
    assert.match(getFlowUndoStatus(session, admin).disabledReason || '', /正式比赛/);
});
