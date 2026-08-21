import assert from 'node:assert/strict';
import test from 'node:test';
import { createInitialSession } from './session-manager';
import {
    hasCompleteAbilityAssignments,
    isAbilityPhaseOneFormalStartBlocked,
    isAbilityRosterMutationBlocked,
} from './ability-phase-one-policy';
import { GamePhase } from './types';

const completedSession = () => {
    const session = createInitialSession();
    session.phase = GamePhase.PreGameSetup;
    session.matchOptions.abilityModeEnabled = true;
    session.matchOptions.matchMode = 'competitive';
    session.players = {
        a1: { playerId: 'a1', name: 'A1', role: 'Player', rosterTeam: 'A', isReady: true },
        b1: { playerId: 'b1', name: 'B1', role: 'Player', rosterTeam: 'B', isReady: true },
    };
    session.abilityAssignments = [
        { playerId: 'a1', team: 'A', abilityId: 'medic' },
        { playerId: 'b1', team: 'B', abilityId: 'witch' },
    ];
    return session;
};

test('完整异能分配在 PreGameSetup 同时封锁正式开赛与阵容变更', () => {
    const session = completedSession();
    assert.equal(hasCompleteAbilityAssignments(session), true);
    assert.equal(isAbilityPhaseOneFormalStartBlocked(session), true);
    assert.equal(isAbilityRosterMutationBlocked(session), true);
});

test('异能关闭或 duel 不封锁旧路径，但未完成同步的异能模式仍封锁开赛', () => {
    const disabled = completedSession();
    disabled.matchOptions.abilityModeEnabled = false;
    assert.equal(isAbilityPhaseOneFormalStartBlocked(disabled), false);

    const duel = completedSession();
    duel.matchOptions.matchMode = 'duel';
    assert.equal(isAbilityPhaseOneFormalStartBlocked(duel), false);

    const incomplete = completedSession();
    incomplete.abilityAssignments = incomplete.abilityAssignments?.slice(0, 1);
    assert.equal(isAbilityPhaseOneFormalStartBlocked(incomplete), true);
});

test('桥接已接收或确认属于旧比赛时仍保持开赛门禁', () => {
    const bridgeReceived = completedSession();
    bridgeReceived.abilitySyncState = {
        status: 'bridge_received',
        matchId: bridgeReceived.matchId,
        revision: 1,
        catalogVersion: 'ability-catalog-v1',
        contentDigest: 'digest-1',
        syncId: 'sync-1',
        commandId: 'command-1',
        seatCount: 2,
    };
    assert.equal(isAbilityPhaseOneFormalStartBlocked(bridgeReceived), true);

    const staleConfirmed = completedSession();
    staleConfirmed.abilitySyncState = {
        status: 'confirmed',
        matchId: 'old-match',
        revision: 1,
        catalogVersion: 'ability-catalog-v1',
        contentDigest: 'digest-1',
        syncId: 'sync-old',
        seatCount: 2,
    };
    assert.equal(isAbilityPhaseOneFormalStartBlocked(staleConfirmed), true);
});

test('当前比赛、修订号和摘要均匹配的最终确认才解除门禁', () => {
    const session = completedSession();
    session.abilitySyncState = {
        status: 'confirmed',
        matchId: session.matchId,
        revision: 1,
        catalogVersion: 'ability-catalog-v1',
        contentDigest: 'digest-1',
        syncId: 'sync-1',
        seatCount: 2,
    };
    assert.equal(isAbilityPhaseOneFormalStartBlocked(session), false);
});

test('AbilityBan 与 AbilityDraft 始终锁定阵容', () => {
    const session = createInitialSession();
    session.phase = GamePhase.AbilityBan;
    assert.equal(isAbilityRosterMutationBlocked(session), true);
    session.phase = GamePhase.AbilityDraft;
    assert.equal(isAbilityRosterMutationBlocked(session), true);
    session.phase = GamePhase.Lobby;
    assert.equal(isAbilityRosterMutationBlocked(session), false);
});
