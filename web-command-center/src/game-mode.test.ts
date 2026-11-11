import assert from 'node:assert/strict';
import test from 'node:test';
import {
  GAME_MODE_MANIFESTS,
  normalizeGameModeId,
  normalizeGameModeOverrides,
  validateModeCvar,
  isLoadedGameMode,
} from './game-mode';
import { getGameModeRoundRules, getRoundOutcome } from './game-mode-rounds';

test('new sessions use an available gamemode manifest', () => {
  assert.equal(GAME_MODE_MANIFESTS.competitive.availability, 'available');
  assert.equal(GAME_MODE_MANIFESTS.rush.requiredMap, 'rush_001');
  assert.equal(GAME_MODE_MANIFESTS.ability.availability, 'available');
});

test('random requires a loaded provider and rejects fixed-rule overrides', () => {
  assert.equal(normalizeGameModeId('random'), 'random');
  assert.equal(isLoadedGameMode('random'), false);
  assert.equal(isLoadedGameMode('random', ['competitive']), false);
  assert.equal(isLoadedGameMode('random', ['random']), true);
  assert.deepEqual(normalizeGameModeOverrides('random', { mp_maxrounds: '13', mp_halftime: '0', mp_startmoney: '16000' }), {});
  const rules = getGameModeRoundRules('random', { gameModeCvarOverrides: { mp_maxrounds: '13', mp_halftime: '0' } });
  assert.equal(rules.maxRounds, 24);
  assert.equal(rules.halftimeRound, 13);
  assert.equal(rules.halftimeEnabled, true);
});

test('random plays all 24 rounds and ties do not pick a winner', () => {
  const rules = getGameModeRoundRules('random', {});
  for (const [a, b] of [[13, 0], [20, 3], [12, 0]])
    assert.deepEqual(getRoundOutcome(rules, a, b, a + b), { finished: false, winner: null });
  assert.deepEqual(getRoundOutcome(rules, 24, 0, 24), { finished: true, winner: 'A' });
  assert.deepEqual(getRoundOutcome(rules, 11, 13, 24), { finished: true, winner: 'B' });
  assert.deepEqual(getRoundOutcome(rules, 12, 12, 24), { finished: true, winner: null });
  assert.deepEqual(getRoundOutcome(getGameModeRoundRules('competitive', {}), 13, 0, 13), { finished: true, winner: 'A' });
  assert.deepEqual(getRoundOutcome(getGameModeRoundRules('rush', {}), 8, 0, 8), { finished: true, winner: 'A' });
  const duel = getGameModeRoundRules('duel', {});
  assert.equal(getRoundOutcome(duel, 13, 0, 13).finished, false);
  assert.equal(getRoundOutcome(duel, 20, 16, 36).winner, 'A');
});

test('legacy options migrate without making competitive undercover by default', () => {
  assert.equal(normalizeGameModeId(undefined, { undercoverModeEnabled: false }), 'competitive');
  assert.equal(normalizeGameModeId(undefined, { undercoverModeEnabled: true }), 'undercover');
  assert.equal(normalizeGameModeId(undefined, { unbalancedModeEnabled: true }), 'unbalanced');
});

test('mode cvar overrides are allowlisted and range checked', () => {
  const overrides = normalizeGameModeOverrides('competitive', {
    mp_freezetime: '30',
    mp_maxrounds: '999',
    sv_cheats: '1',
  });
  assert.deepEqual(overrides, { mp_freezetime: '30' });
  const definition = GAME_MODE_MANIFESTS.competitive.cvars.find(item => item.name === 'mp_freezetime')!;
  assert.equal(validateModeCvar(definition, '61'), false);
  assert.equal(validateModeCvar(definition, '15'), true);
});


test('round monitoring follows Gamemode max rounds and halftime rules', () => {
  assert.deepEqual(getGameModeRoundRules('competitive', { gameModeId: 'competitive' }), {
    isDuel: false,
    maxRounds: 24,
    winTarget: 13,
    halftimeEnabled: true,
    halftimeRound: 13,
  });
  assert.deepEqual(getGameModeRoundRules('rush', { gameModeId: 'rush' }), {
    isDuel: false,
    maxRounds: 15,
    winTarget: 8,
    halftimeEnabled: true,
    halftimeRound: 9,
  });
  assert.deepEqual(getGameModeRoundRules('duel', {
    gameModeId: 'duel',
    duelRounds: { pistol: 8, rifle: 16, sniper: 12 },
  }), {
    isDuel: true,
    maxRounds: 36,
    winTarget: 36,
    halftimeEnabled: false,
    halftimeRound: 0,
  });
});
