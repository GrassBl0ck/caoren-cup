import assert from 'node:assert/strict';
import test from 'node:test';
import {
  GAME_MODE_MANIFESTS,
  normalizeGameModeId,
  normalizeGameModeOverrides,
  validateModeCvar,
} from './game-mode';
import { getGameModeRoundRules } from './game-mode-rounds';

test('new sessions use an available gamemode manifest', () => {
  assert.equal(GAME_MODE_MANIFESTS.competitive.availability, 'available');
  assert.equal(GAME_MODE_MANIFESTS.rush.requiredMap, 'rush_001');
  assert.equal(GAME_MODE_MANIFESTS.ability.availability, 'available');
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
