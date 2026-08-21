import assert from 'node:assert/strict';
import test from 'node:test';
import { createInitialSession } from '../session-manager';
import { GamePhase } from '../types';
import { removeIdentityFromSession } from './session-integration';

test('removing a disabled identity clears its current player and roster projections', () => {
    const session = createInitialSession();
    session.players.member = {
        playerId: 'member',
        name: 'Fixed Member',
        role: 'Player',
        identityId: 'fixed-identity',
        membershipId: 'login-membership',
        isReady: true,
        rosterTeam: 'A',
    };
    session.players.other = {
        playerId: 'other',
        name: 'Other',
        role: 'Player',
        identityId: 'other-identity',
        isReady: false,
    };
    session.playerOrder = ['member', 'other'];
    session.teams.A.players = ['member'];
    session.teams.B.players = ['other', 'member'];
    session.captains.A = 'member';
    session.accusations.member = { own: 'other', enemy: null };

    const removed = removeIdentityFromSession(session, 'fixed-identity');

    assert.equal(removed?.playerId, 'member');
    assert.equal(session.players.member, undefined);
    assert.deepEqual(session.playerOrder, ['other']);
    assert.deepEqual(session.teams.A.players, []);
    assert.deepEqual(session.teams.B.players, ['other']);
    assert.equal(session.captains.A, null);
    assert.equal(session.accusations.member, undefined);
    assert.equal(session.players.other?.name, 'Other');
});

test('异能锁定阶段拒绝身份移除导致参赛者离开阵容', () => {
    for (const phase of [GamePhase.AbilityBan, GamePhase.AbilityDraft, GamePhase.PreGameSetup]) {
        const session = createInitialSession();
        session.phase = phase;
        session.matchOptions.abilityModeEnabled = true;
        session.players.member = {
            playerId: 'member',
            name: 'Fixed Member',
            role: 'Player',
            identityId: 'fixed-identity',
            rosterTeam: 'A',
            isReady: true,
        };
        session.teams.A.players = ['member'];
        if (phase === GamePhase.PreGameSetup) {
            session.abilityAssignments = [{ playerId: 'member', team: 'A', abilityId: 'medic' }];
        }

        assert.equal(removeIdentityFromSession(session, 'fixed-identity'), undefined);
        assert.ok(session.players.member);
        assert.deepEqual(session.teams.A.players, ['member']);
    }
});
