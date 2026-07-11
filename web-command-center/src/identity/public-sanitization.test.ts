import assert from 'node:assert/strict';
import test from 'node:test';
import { sanitizeForPublic } from '../player-utils';
import { createInitialSession } from '../session-manager';
import { GamePhase } from '../types';

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
