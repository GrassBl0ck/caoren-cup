import assert from 'assert';

import {
  canEnqueueServerCommand,
  classifyServerCommand,
} from './match-command-policy';
import { GamePhase, GameSession } from './types';

const baseSession = (overrides: Partial<GameSession> = {}): GameSession => ({
  sessionId: 'test-session',
  phase: GamePhase.PreGameSetup,
  matchId: 'test-match',
  matchOptions: {
    matchMode: 'competitive',
    matchController: 'matchzy',
    undercoverModeEnabled: true,
    caorenModifiersEnabled: false,
  },
  players: {},
  playerOrder: [],
  teams: {
    A: { name: 'A', players: [] },
    B: { name: 'B', players: [] },
  },
  captains: { A: null, B: null },
  rollValues: { A: null, B: null },
  draftOrder: [],
  draftIndex: 0,
  mapPool: [],
  bannedMaps: [],
  selectedMap: null,
  currentBanTeam: null,
  banSequence: [],
  sidePickTeam: null,
  selectedSide: null,
  undercoverCount: 0,
  detectiveCount: 0,
  questionsUsed: 0,
  currentQuestion: null,
  questionAnswer: null,
  accusations: {},
  timerEndAt: null,
  timerPhase: null,
  adminLock: { holderId: null, acquiredAt: null },
  createdAt: 0,
  autoClearMinutes: 15,
  ...overrides,
});

const assertAllowed = (session: GameSession, command: string) => {
  const result = canEnqueueServerCommand(session, command);
  assert.equal(result.allowed, true, `${command} should be allowed, got: ${result.reason}`);
};

const assertRejected = (session: GameSession, command: string) => {
  const result = canEnqueueServerCommand(session, command);
  assert.equal(result.allowed, false, `${command} should be rejected`);
  assert.ok(result.reason, `${command} should include a rejection reason`);
};

assert.equal(classifyServerCommand('mp_restartgame 1'), 'match-flow');
assert.equal(classifyServerCommand('mp_warmup_end'), 'match-flow');
assert.equal(classifyServerCommand('css_dmg t 2 100 5'), 'caoren-modifier');
assert.equal(classifyServerCommand('host_workshop_map 3250543760'), 'map');
assert.equal(classifyServerCommand('wp_refresh 76561198000000001 safe'), 'cosmetic');
assert.equal(classifyServerCommand('sv_cheats 1'), 'unknown');

const matchzyCompetitive = baseSession();
assertRejected(matchzyCompetitive, 'mp_restartgame 1');
assertRejected(matchzyCompetitive, 'mp_warmup_end');
assertRejected(matchzyCompetitive, 'mp_pause_match');
assertRejected(matchzyCompetitive, 'mp_unpause_match');

assertAllowed(
  baseSession({
    matchOptions: {
      matchMode: 'competitive',
      matchController: 'matchzy',
      undercoverModeEnabled: true,
      caorenModifiersEnabled: true,
    },
  }),
  'css_dmg t 2 100 5'
);

assertAllowed(
  baseSession({
    matchOptions: {
      matchMode: 'duel',
      matchController: 'caoren',
      undercoverModeEnabled: false,
      caorenModifiersEnabled: false,
    },
  }),
  'mp_warmup_start'
);
assertAllowed(
  baseSession({
    matchOptions: {
      matchMode: 'duel',
      matchController: 'caoren',
      undercoverModeEnabled: false,
      caorenModifiersEnabled: false,
    },
  }),
  'mp_warmup_end'
);
assertAllowed(
  baseSession({
    matchOptions: {
      matchMode: 'duel',
      matchController: 'caoren',
      undercoverModeEnabled: false,
      caorenModifiersEnabled: false,
    },
  }),
  'mp_restartgame 1'
);

assertRejected(matchzyCompetitive, 'sv_cheats 1');
assertAllowed(matchzyCompetitive, 'wp_refresh 76561198000000001 safe');
assertAllowed(matchzyCompetitive, 'wp_refresh 76561198000000001');
assertRejected(matchzyCompetitive, 'wp_refresh all');
assertRejected(matchzyCompetitive, 'wp_refresh 76561198000000001 unsafe');
assertRejected(matchzyCompetitive, 'wp_refresh 76561198000000001 safe; sv_cheats 1');
assertRejected(matchzyCompetitive, "wp_refresh 76561198000000001 safe\nsv_cheats 1");

console.log('match-command-policy tests passed');
