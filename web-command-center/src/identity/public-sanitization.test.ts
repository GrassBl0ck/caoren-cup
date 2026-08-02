import assert from 'node:assert/strict';
import test from 'node:test';
import { sanitizeForPublic, sanitizeGameStateForViewer } from '../player-utils';
import { createInitialSession } from '../session-manager';
import { GamePhase } from '../types';

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

test('a socket without current match membership receives only the simple match status', () => {
    const session = createInitialSession();
    session.players.player = { playerId: 'player', name: 'Private Player', role: 'Player', isReady: false };
    session.liveGameData = {
        scoreCT: 1, scoreT: 2, scoreA: 3, scoreB: 4, currentRound: 5, pluginConnected: true,
        winnerTeam: null, matchFinished: false, winTarget: 13, lastScoredRound: 4,
    };

    assert.deepEqual(sanitizeGameStateForViewer(session, null), { matchStatus: 'waiting' });
    session.phase = GamePhase.LiveGame;
    assert.deepEqual(sanitizeGameStateForViewer(session, null), { matchStatus: 'started' });
    session.phase = GamePhase.Scoreboard;
    assert.deepEqual(sanitizeGameStateForViewer(session, null), { matchStatus: 'ended' });
});
