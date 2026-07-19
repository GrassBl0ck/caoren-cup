import assert from 'node:assert/strict';
import test from 'node:test';
import { sanitizeForPublic } from '../player-utils';
import { createInitialSession } from '../session-manager';
import { GamePhase } from '../types';

const createAbilitySession = () => {
    const session = createInitialSession();
    session.players = {
        a1: { playerId: 'a1', name: 'A1', role: 'Player', rosterTeam: 'A', isReady: true, steamId: '76561198000000041' },
        a2: { playerId: 'a2', name: 'A2', role: 'Player', rosterTeam: 'A', isReady: true, steamId: '76561198000000042' },
        b1: { playerId: 'b1', name: 'B1', role: 'Player', rosterTeam: 'B', isReady: true, steamId: '76561198000000043' },
        b2: { playerId: 'b2', name: 'B2', role: 'Player', rosterTeam: 'B', isReady: true, steamId: '76561198000000044' },
        admin: { playerId: 'admin', name: 'Admin', role: 'Admin', isReady: true },
        spectator: { playerId: 'spectator', name: 'Spectator', role: 'Spectator', isReady: true },
    };
    session.teams.A.players = ['a1', 'a2'];
    session.teams.B.players = ['b1', 'b2'];
    return session;
};

test('lobby invite is visible only to an authenticated admin', () => {
    const session = createInitialSession();
    session.lobbyAccess = { inviteCode: 'SECRET88', inviteCreatedAt: 1, inviteExpiresAt: 2 };
    session.players.admin = { playerId: 'admin', name: 'Admin', role: 'Admin', isReady: true };
    session.players.player = {
        playerId: 'player',
        name: 'Player',
        role: 'Player',
        isReady: false,
        identityLevel: 'temporary',
        confirmationState: 'mismatch',
        confirmationReason: 'steam_mismatch',
    };

    assert.equal(sanitizeForPublic(session, 'admin').lobbyAccess.inviteCode, 'SECRET88');
    assert.equal(sanitizeForPublic(session, 'player').lobbyAccess.inviteCode, undefined);
    assert.equal(sanitizeForPublic(session, null).lobbyAccess.inviteCode, undefined);
});

test('identity confirmation reason is private to the player and admin', () => {
    const session = createInitialSession();
    session.players.admin = { playerId: 'admin', name: 'Admin', role: 'Admin', isReady: true };
    session.players.player = {
        playerId: 'player',
        name: 'Player',
        role: 'Player',
        isReady: false,
        identityLevel: 'temporary',
        confirmationState: 'mismatch',
        confirmationReason: 'steam_mismatch',
    };
    session.players.other = { playerId: 'other', name: 'Other', role: 'Player', isReady: false };

    assert.equal(sanitizeForPublic(session, 'player').players.player.confirmationReason, 'steam_mismatch');
    assert.equal(sanitizeForPublic(session, 'admin').players.player.confirmationReason, 'steam_mismatch');
    assert.equal(sanitizeForPublic(session, 'other').players.player.confirmationReason, undefined);
});

test('full SteamID is private before scoreboard but remains available for postmatch matrices', () => {
    const session = createInitialSession();
    session.players.admin = { playerId: 'admin', name: 'Admin', role: 'Admin', isReady: true };
    session.players.player = {
        playerId: 'player',
        name: 'Player',
        role: 'Player',
        isReady: false,
        steamId: '76561198000000041',
    };
    session.players.other = { playerId: 'other', name: 'Other', role: 'Player', isReady: false };

    assert.equal(sanitizeForPublic(session, 'player').players.player.steamId, '76561198000000041');
    assert.equal(sanitizeForPublic(session, 'admin').players.player.steamId, '76561198000000041');
    assert.equal(sanitizeForPublic(session, 'other').players.player.steamId, undefined);
    assert.equal(sanitizeForPublic(session, null).players.player.steamId, undefined);

    session.phase = GamePhase.Scoreboard;
    assert.equal(sanitizeForPublic(session, 'other').players.player.steamId, '76561198000000041');
});

test('ability Ban votes are visible only to the viewer team before resolution', () => {
    const session = createAbilitySession();
    session.phase = GamePhase.AbilityBan;
    session.abilityBanState = {
        orderedPlayers: { A: ['a1', 'a2'], B: ['b1', 'b2'] },
        banCountPerTeam: 1,
        selections: {
            a1: ['medic'],
            a2: ['tank'],
            b1: ['assassin'],
            b2: ['witch'],
        },
        confirmedPlayerIds: ['a1', 'b1'],
        timeoutAt: 12345,
    };

    const stateForA = sanitizeForPublic(session, 'a1').abilityBanState;
    const stateForB = sanitizeForPublic(session, 'b1').abilityBanState;
    const stateForAdmin = sanitizeForPublic(session, 'admin').abilityBanState;
    const stateForSpectator = sanitizeForPublic(session, 'spectator').abilityBanState;
    const stateForAnonymous = sanitizeForPublic(session, null).abilityBanState;

    assert.deepEqual(stateForA.selections, { a1: ['medic'], a2: ['tank'] });
    assert.deepEqual(stateForA.confirmedPlayerIds, ['a1']);
    assert.deepEqual(stateForB.selections, { b1: ['assassin'], b2: ['witch'] });
    assert.deepEqual(stateForB.confirmedPlayerIds, ['b1']);
    assert.deepEqual(stateForAdmin.selections, session.abilityBanState.selections);
    assert.deepEqual(stateForAdmin.confirmedPlayerIds, ['a1', 'b1']);
    assert.deepEqual(stateForSpectator.selections, {});
    assert.deepEqual(stateForSpectator.confirmedPlayerIds, []);
    assert.deepEqual(stateForAnonymous.selections, {});
    assert.deepEqual(stateForAnonymous.confirmedPlayerIds, []);

    stateForAdmin.orderedPlayers.A.push('changed');
    stateForAdmin.selections.a1.push('balance');
    stateForAdmin.confirmedPlayerIds.push('a2');
    assert.deepEqual(session.abilityBanState.orderedPlayers.A, ['a1', 'a2']);
    assert.deepEqual(session.abilityBanState.selections.a1, ['medic']);
    assert.deepEqual(session.abilityBanState.confirmedPlayerIds, ['a1', 'b1']);
});

test('resolved Ban exposes only the final banned abilities and no historical votes', () => {
    const session = createAbilitySession();
    session.phase = GamePhase.AbilityDraft;
    session.abilityBanState = {
        orderedPlayers: { A: ['a1', 'a2'], B: ['b1', 'b2'] },
        banCountPerTeam: 1,
        selections: { a1: ['medic'], b1: ['assassin'] },
        confirmedPlayerIds: ['a1', 'b1'],
        timeoutAt: 12345,
    };
    session.abilityDraftState = {
        batches: [{ team: 'A', playerIds: ['a1'] }],
        currentBatchIndex: 0,
        bannedAbilityIds: ['medic', 'assassin'],
        choices: {},
        confirmedPlayerIds: [],
        assignments: [],
        timeoutAt: 23456,
    };

    for (const viewerId of ['a1', 'b1', 'admin', 'spectator', null]) {
        const publicSession = sanitizeForPublic(session, viewerId);
        assert.equal(publicSession.abilityBanState, undefined);
        assert.deepEqual(publicSession.abilityDraftState.bannedAbilityIds, ['medic', 'assassin']);
    }
});

test('ability Draft hides the active batch from enemies but publishes completed batches', () => {
    const session = createAbilitySession();
    session.phase = GamePhase.AbilityDraft;
    session.abilityDraftState = {
        batches: [
            { team: 'A', playerIds: ['a1'] },
            { team: 'B', playerIds: ['b1', 'b2'] },
            { team: 'A', playerIds: ['a2'] },
        ],
        currentBatchIndex: 1,
        bannedAbilityIds: ['medic'],
        choices: {
            a1: 'tank',
            b1: 'assassin',
            b2: 'witch',
        },
        confirmedPlayerIds: ['a1', 'b1'],
        assignments: [
            { playerId: 'a1', team: 'A', abilityId: 'tank' },
            { playerId: 'b1', team: 'B', abilityId: 'assassin' },
        ],
        timeoutAt: 23456,
        failure: { code: 'TEST', message: 'test failure', failedAt: 20000 },
    };

    const stateForA = sanitizeForPublic(session, 'a2').abilityDraftState;
    const stateForB = sanitizeForPublic(session, 'b2').abilityDraftState;
    const stateForAdmin = sanitizeForPublic(session, 'admin').abilityDraftState;
    const stateForSpectator = sanitizeForPublic(session, 'spectator').abilityDraftState;
    const stateForAnonymous = sanitizeForPublic(session, null).abilityDraftState;

    assert.deepEqual(stateForA.choices, { a1: 'tank' });
    assert.deepEqual(stateForA.confirmedPlayerIds, ['a1']);
    assert.deepEqual(stateForA.assignments, [{ playerId: 'a1', team: 'A', abilityId: 'tank' }]);
    assert.deepEqual(stateForB.choices, { a1: 'tank', b1: 'assassin', b2: 'witch' });
    assert.deepEqual(stateForB.confirmedPlayerIds, ['a1', 'b1']);
    assert.deepEqual(stateForB.assignments, [
        { playerId: 'a1', team: 'A', abilityId: 'tank' },
        { playerId: 'b1', team: 'B', abilityId: 'assassin' },
    ]);
    assert.deepEqual(stateForAdmin.choices, session.abilityDraftState.choices);
    assert.deepEqual(stateForAdmin.confirmedPlayerIds, ['a1', 'b1']);
    assert.deepEqual(stateForAdmin.assignments, session.abilityDraftState.assignments);
    assert.deepEqual(stateForSpectator.choices, { a1: 'tank' });
    assert.deepEqual(stateForSpectator.confirmedPlayerIds, ['a1']);
    assert.deepEqual(stateForSpectator.assignments, [{ playerId: 'a1', team: 'A', abilityId: 'tank' }]);
    assert.deepEqual(stateForAnonymous.choices, { a1: 'tank' });
    assert.deepEqual(stateForAnonymous.confirmedPlayerIds, ['a1']);
    assert.deepEqual(stateForAnonymous.assignments, [{ playerId: 'a1', team: 'A', abilityId: 'tank' }]);

    assert.equal(sanitizeForPublic(session, 'a2').players.a2.steamId, '76561198000000042');
    assert.equal(sanitizeForPublic(session, 'b2').players.a2.steamId, undefined);
    assert.equal(sanitizeForPublic(session, 'admin').players.a2.steamId, '76561198000000042');
    assert.equal(sanitizeForPublic(session, 'spectator').players.a2.steamId, undefined);
    assert.equal(sanitizeForPublic(session, null).players.a2.steamId, undefined);

    stateForAdmin.batches[0].playerIds.push('changed');
    stateForAdmin.bannedAbilityIds.push('glass_cannon');
    stateForAdmin.choices.a1 = 'witch';
    stateForAdmin.confirmedPlayerIds.push('b2');
    stateForAdmin.assignments[0].abilityId = 'witch';
    stateForAdmin.failure.message = 'changed';
    assert.deepEqual(session.abilityDraftState.batches[0].playerIds, ['a1']);
    assert.deepEqual(session.abilityDraftState.bannedAbilityIds, ['medic']);
    assert.equal(session.abilityDraftState.choices.a1, 'tank');
    assert.deepEqual(session.abilityDraftState.confirmedPlayerIds, ['a1', 'b1']);
    assert.equal(session.abilityDraftState.assignments[0].abilityId, 'tank');
    assert.equal(session.abilityDraftState.failure?.message, 'test failure');
});
