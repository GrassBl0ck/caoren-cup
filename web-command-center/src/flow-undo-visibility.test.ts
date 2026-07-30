import assert from 'node:assert/strict';
import test from 'node:test';
import { clearFlowUndoHistory, pushFlowUndoCheckpoint } from './flow-undo-manager';
import { sanitizeGameStateForViewer } from './player-utils';
import { createInitialSession } from './session-manager';
import { GamePhase, Player } from './types';

test.afterEach(() => clearFlowUndoHistory());

test('only official or duel temporary administrators receive sanitized undo status', () => {
    const session = createInitialSession();
    session.phase = GamePhase.PlayerDraft;
    const admin: Player = { playerId: 'admin', name: 'Admin', role: 'Admin', isReady: false };
    const temporary: Player = { playerId: 'temporary', name: 'Temporary', role: 'Player', isReady: false };
    const player: Player = { playerId: 'player', name: 'Player', role: 'Player', isReady: false };
    session.players = { admin, temporary, player };
    session.playerOrder = ['admin', 'temporary', 'player'];
    session.duelTempAdminId = temporary.playerId;
    pushFlowUndoCheckpoint(session, {
        actionType: 'ADVANCE_PHASE', actorId: admin.playerId, actorName: admin.name, summary: '安全摘要',
    });

    const publicState = sanitizeGameStateForViewer(session, player.playerId);
    const adminState = sanitizeGameStateForViewer(session, admin.playerId);
    const temporaryState = sanitizeGameStateForViewer(session, temporary.playerId);

    assert.equal(publicState.flowUndoStatus, undefined);
    assert.equal(JSON.stringify(publicState).includes('安全摘要'), false);
    assert.equal(adminState.flowUndoStatus.latest.summary, '安全摘要');
    assert.equal(temporaryState.flowUndoStatus.latest.summary, '安全摘要');
    assert.equal(adminState.flowUndoStatus.historyDepth, 1);
    assert.equal(adminState.flowUndoStatus.targetPhase, GamePhase.PlayerDraft);
    assert.equal(temporaryState.flowUndoStatus.canUndo, false);
    assert.equal(JSON.stringify(adminState.flowUndoStatus).includes('snapshot'), false);
});
