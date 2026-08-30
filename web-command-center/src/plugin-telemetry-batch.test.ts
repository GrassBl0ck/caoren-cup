import assert from 'node:assert/strict';
import test from 'node:test';
import { applyPluginTelemetryBatch } from './plugin-api';
import { createInitialSession } from './session-manager';

test('batched high-frequency events preserve existing combat and fun statistics', () => {
    const session = createInitialSession();
    session.liveGameData = { currentRound: 1, roundStats: {} } as any;
    session.players.attacker = {
        playerId: 'attacker', name: 'Attacker', role: 'Player', isReady: true,
        steamId: '76561198000000001', team: 'CT',
    };
    session.players.victim = {
        playerId: 'victim', name: 'Victim', role: 'Player', isReady: true,
        steamId: '76561198000000002', team: 'T',
    };

    const processed = applyPluginTelemetryBatch(session, [
        { type: 'weapon_fire', payload: { round: 1, steamId: '76561198000000001', team: 'CT', weapon: 'weapon_ak47' } },
        { type: 'weapon_fire', payload: { round: 1, steamId: '76561198000000001', team: 'CT', weapon: 'weapon_ak47' } },
        { type: 'player_jump', payload: { round: 1, steamId: '76561198000000001', team: 'CT' } },
        { type: 'player_crouch_sample', payload: { round: 1, steamId: '76561198000000001', team: 'CT', seconds: 2 } },
        {
            type: 'player_hurt',
            payload: {
                round: 1,
                attackerSteamId: '76561198000000001', attackerTeam: 'CT',
                victimSteamId: '76561198000000002', victimTeam: 'T',
                damage: 30, rawDamage: 30, health: 70, maxHealth: 100,
                weapon: 'weapon_ak47',
            },
        },
    ]);

    assert.equal(processed, 5);
    assert.equal(session.players.attacker.stats?.shotsFired, 2);
    assert.equal(session.players.attacker.stats?.shotsHit, 1);
    assert.equal(session.players.attacker.stats?.damage, 30);
    assert.equal(session.players.attacker.stats?.jumpCount, 1);
    assert.equal(session.players.attacker.stats?.crouchSeconds, 2);
});

test('batch rejects critical events so kill and round ordering stays on the immediate channel', () => {
    const session = createInitialSession();
    session.liveGameData = { currentRound: 1, roundStats: {} } as any;

    assert.throws(
        () => applyPluginTelemetryBatch(session, [{ type: 'player_death', payload: {} }]),
        /不允许批量处理的插件事件/,
    );
});
