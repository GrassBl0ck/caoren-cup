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

test('异能关闭、duel 与分配不完整不封锁旧开赛路径', () => {
    const disabled = completedSession();
    disabled.matchOptions.abilityModeEnabled = false;
    assert.equal(isAbilityPhaseOneFormalStartBlocked(disabled), false);

    const duel = completedSession();
    duel.matchOptions.matchMode = 'duel';
    assert.equal(isAbilityPhaseOneFormalStartBlocked(duel), false);

    const incomplete = completedSession();
    incomplete.abilityAssignments = incomplete.abilityAssignments?.slice(0, 1);
    assert.equal(isAbilityPhaseOneFormalStartBlocked(incomplete), false);
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
