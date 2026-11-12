import assert from 'node:assert/strict';
import test from 'node:test';
import { createInitialSession, setSession } from './session-manager';
import { GamePhase } from './types';
import { synchronizeRandomModeRuntime, updateMatchFinishState, resolveRosterTeamByInitialSide } from './game-flow-manager';
import { registerGameModeRoutes } from './routes/game-mode-routes';
import { registerPluginRoutes } from './plugin-api';
import { enqueuePluginCommand, cancelPluginCommands } from './plugin-command-queue';

test('game-side Random selection replaces old competitive flow and enters formal statistics once', () => {
  const session = createInitialSession();
  session.phase = GamePhase.LiveGame;
  session.selectedSide = 'CT';
  session.players.a = { playerId: 'a', name: 'A', role: 'Player', isReady: true, rosterTeam: 'A', steamId: '76561198000000001' };
  session.players.b = { playerId: 'b', name: 'B', role: 'Player', isReady: true, rosterTeam: 'B', steamId: '76561198000000002' };
  session.gameModeRuntime = { modeId: 'competitive', lifecycle: 'Live', revision: 1, warmupSeconds: 15 };
  setSession(session);
  synchronizeRandomModeRuntime({ modeId: 'random', lifecycle: 'Warmup', revision: 2, warmupSeconds: 15 });
  assert.equal(session.matchOptions.gameModeId, 'random');
  assert.equal(session.phase, GamePhase.PreGameSetup);
  const live = { modeId: 'random' as const, lifecycle: 'Live' as const, revision: 3, warmupSeconds: 15 };
  synchronizeRandomModeRuntime(live);
  assert.equal(session.phase, GamePhase.LiveGame);
  assert.equal(session.liveGameData?.formalStatsStarted, true);
  assert.equal(session.liveGameData?.roundBaseOffset, 0);
  const matchId = session.matchId;
  Object.assign(session.liveGameData!, { scoreA: 13, scoreB: 0, lastScoredRound: 13 });
  updateMatchFinishState();
  assert.equal(session.liveGameData!.matchFinished, false);
  synchronizeRandomModeRuntime(live);
  assert.equal(session.matchId, matchId);
  assert.equal(session.liveGameData!.scoreA, 13);
  assert.equal(resolveRosterTeamByInitialSide('CT', 12), 'A');
  assert.equal(resolveRosterTeamByInitialSide('CT', 13), 'B');
  Object.assign(session.liveGameData!, { scoreA: 12, scoreB: 12, lastScoredRound: 24 });
  updateMatchFinishState();
  assert.equal(session.liveGameData!.matchFinished, true);
  assert.equal(session.liveGameData!.winnerTeam, null);
  synchronizeRandomModeRuntime({ ...live, lifecycle: 'Stopped', revision: 4 });
  assert.equal(session.phase, GamePhase.Lobby);
});

test('missing web roster mirrors live mode without inventing a winner or blocking the server', () => {
  const session = createInitialSession();
  session.phase = GamePhase.LiveGame;
  session.gameModeRuntime = { modeId: 'competitive', lifecycle: 'Live', revision: 1, warmupSeconds: 15 };
  setSession(session);
  synchronizeRandomModeRuntime({ modeId: 'random', lifecycle: 'Live', revision: 3, warmupSeconds: 15 });
  assert.equal(session.phase, GamePhase.LiveGame);
  assert.equal(session.matchOptions.gameModeId, 'random');
  assert.equal(session.liveGameData!.formalStatsStarted, false);
  assert.match(session.liveGameData!.statsUnavailableReason!, /统计未就绪/);
  Object.assign(session.liveGameData!, { scoreA: 13, scoreB: 11, lastScoredRound: 24 });
  updateMatchFinishState();
  assert.equal(session.liveGameData!.winnerTeam, null);
  assert.equal(session.liveGameData!.matchFinished, false);
});

test('web refuses Random selection and start when provider is not loaded', () => {
  const handlers = new Map<string, (req: any, res: any) => void>();
  const app = { get() {}, post(path: string, handler: any) { handlers.set(path, handler); } };
  const session = createInitialSession();
  const commands: unknown[] = [];
  registerGameModeRoutes(app as any, {
    adminPassword: 'test-only', getPhase: () => GamePhase.Lobby,
    getAvailableGameModes: () => ['competitive'], ensureMatchOptions: () => session.matchOptions,
    applyMatchOptions: options => Object.assign(session.matchOptions, options),
    enqueuePluginCommand: (...args) => commands.push(args), notify() {}, broadcastState() {},
  });
  let status = 200;
  const response = { status(code: number) { status = code; return this; }, json() {} };
  handlers.get('/api/admin/game-mode')!({ body: { adminPassword: 'test-only', gameModeId: 'random' } }, response);
  assert.equal(status, 400);
  session.matchOptions.gameModeId = 'random';
  handlers.get('/api/admin/game-mode/start')!({ body: { adminPassword: 'test-only' } }, response);
  assert.equal(status, 400);
  assert.equal(commands.length, 0);
});

for (const tied of [false, true]) test(`real round-event handler plays through 24 and deduplicates (${tied ? 'tie' : 'winner'})`, () => {
  const session = createInitialSession();
  session.selectedSide = 'CT';
  session.players.a = { playerId: 'a', name: 'A', role: 'Player', isReady: true, rosterTeam: 'A', steamId: '76561198000000001' };
  session.players.b = { playerId: 'b', name: 'B', role: 'Player', isReady: true, rosterTeam: 'B', steamId: '76561198000000002' };
  setSession(session);
  synchronizeRandomModeRuntime({ modeId: 'random', lifecycle: 'Live', revision: 1, warmupSeconds: 15 });
  const handlers = new Map<string, (req: any, res: any) => void>();
  const app = { get() {}, post(path: string, ...chain: any[]) { handlers.set(path, chain[chain.length - 1]); } };
  const notifications: string[] = [];
  registerPluginRoutes(app as any, { broadcastState() {}, requestStateBroadcast() {}, flushStateBroadcast() {}, notifyMessage: msg => notifications.push(msg) });
  const response = { status() { return this; }, json() {} };
  const event = handlers.get('/api/plugin/event')!;
  for (let round = 1; round <= 24; round++) {
    const winner = !tied && round === 13 ? 'T' : 'CT';
    const request = { body: { matchId: session.matchId, type: 'round_end', payload: { round, winner, players: [] } } };
    event(request, response);
    event(request, response);
    assert.equal(session.liveGameData!.scoreA + session.liveGameData!.scoreB, round);
    assert.equal(session.liveGameData!.matchFinished, round === 24);
    assert.equal(session.liveGameData!.statsLocked === true, round === 24);
  }
  assert.equal(session.liveGameData!.currentRound, 24);
  assert.equal(session.liveGameData!.winnerTeam, tied ? null : 'A');
  assert.equal(notifications.length, 1);
  assert.match(notifications[0], tied ? /双方平局，比分 12:12/ : /A队获胜，比分 13:11/);
});


test('actual lobby menu hides unloaded Random and enables it after server confirmation', async () => {
  const fs = await import('node:fs');
  const vm = await import('node:vm');
  const source = fs.readFileSync('public/js/lobby-app.js', 'utf8');
  const start = source.indexOf('function syncMatchOptionsPanel(');
  const end = source.indexOf('async function refreshMatchOptions(', start);
  assert.ok(start >= 0 && end > start);
  const panel = { style: { display: '' } };
  const option = { hidden: false, disabled: false };
  const select = { value: '', disabled: false, querySelector: () => option };
  const context = vm.createContext({
    document: { getElementById: (id: string) => id === 'match-options-panel' ? panel : id === 'game-mode-id' ? select : null, querySelector: () => null },
    getPendingMatchOptions: () => null, DUEL_DEFAULT_MAP: 'duel_01', syncCaorenModPanel() {}, syncAbilityModeConfigPanel() {},
  });
  vm.runInContext(source.slice(start, end), context);
  context.syncMatchOptionsPanel({ phase: 'Lobby', matchOptions: {} }, true);
  assert.equal(option.hidden, true);
  assert.equal(option.disabled, true);
  context.syncMatchOptionsPanel({ phase: 'Lobby', availableGameModes: ['random'], matchOptions: { gameModeId: 'random' } }, true);
  assert.equal(option.hidden, false);
  assert.equal(option.disabled, false);
  assert.equal(select.value, 'random');
  context.syncMatchOptionsPanel({ phase: 'LiveGame', availableGameModes: ['random'], matchOptions: { gameModeId: 'random' } }, true);
  assert.equal(select.disabled, true);
});

test('heartbeat route rejects late requests before state mutation or command dequeue and accepts restarts', () => {
  const session = createInitialSession();
  session.selectedSide = 'CT';
  session.players.a = { playerId: 'a', name: 'A', role: 'Player', isReady: true, rosterTeam: 'A', steamId: '76561198000000001' };
  session.players.b = { playerId: 'b', name: 'B', role: 'Player', isReady: true, rosterTeam: 'B', steamId: '76561198000000002' };
  setSession(session);
  const handlers = new Map<string, (req: any, res: any) => void>();
  const app = { get() {}, post(path: string, ...chain: any[]) { handlers.set(path, chain[chain.length - 1]); } };
  let broadcasts = 0;
  registerPluginRoutes(app as any, { broadcastState() { broadcasts++; }, requestStateBroadcast() {}, flushStateBroadcast() {}, notifyMessage() {} });
  const heartbeat = (instance: string, epoch: string, sequence: number, revision: number, lifecycle = 'Live') => {
    const response = { code: 200, body: undefined as any, status(code: number) { this.code = code; return this; }, json(body: any) { this.body = body; } };
    handlers.get('/api/plugin/heartbeat')!({ body: {
      heartbeatInstanceId: instance, heartbeatStartedAtTicks: epoch, heartbeatSequence: sequence,
      gameMode: { modeId: 'random', lifecycle, revision, warmupSeconds: 15 }, availableGameModes: ['random'],
      mapName: `${instance}-${sequence}`,
    } }, response);
    return response;
  };
  // Live arrives first. Its older Warmup request must not roll back the match.
  assert.equal(heartbeat('bridge-one', '100', 3, 3).code, 200);
  const firstMatch = session.matchId;
  Object.assign(session.liveGameData!, { scoreA: 7, scoreB: 4, lastScoredRound: 11 });
  const pending = enqueuePluginCommand('GAME_MODE_STOP', { label: 'heartbeat-order-test' });
  const acceptedBroadcasts = broadcasts;
  assert.equal(heartbeat('bridge-one', '100', 2, 2, 'Warmup').code, 409);
  assert.equal(heartbeat('bridge-one', '100', 3, 3).code, 409);
  assert.equal(session.phase, GamePhase.LiveGame);
  assert.equal(session.matchId, firstMatch);
  assert.equal(session.liveGameData!.scoreA, 7);
  assert.equal(session.liveGameData!.mapName, 'bridge-one-3');
  assert.equal(broadcasts, acceptedBroadcasts);
  const next = heartbeat('bridge-one', '100', 4, 3);
  assert.equal(next.code, 200);
  assert.equal(session.liveGameData!.scoreA, 7);
  assert.ok(next.body.commands.some((command: any) => command.id === pending.id));
  cancelPluginCommands(command => command.id === pending.id);
  // Core may restart while the bridge remains loaded: request order, not Core revision, wins.
  assert.equal(heartbeat('bridge-one', '100', 5, 1).code, 200);
  assert.notEqual(session.matchId, firstMatch);
  assert.equal(session.gameModeRuntime!.revision, 1);
  const secondMatch = session.matchId;
  assert.equal(heartbeat('bridge-one', '100', 6, 1).code, 200);
  assert.equal(session.matchId, secondMatch);
  // A bridge reload resets request sequence. Retired and never-before-seen older instances stay rejected.
  assert.equal(heartbeat('bridge-two', '200', 1, 1).code, 200);
  assert.equal(session.matchId, secondMatch);
  assert.equal(heartbeat('bridge-one', '100', 999, 99, 'Warmup').code, 409);
  assert.equal(heartbeat('old-unseen', '50', 999, 99, 'Warmup').code, 409);
  assert.equal(session.phase, GamePhase.LiveGame);
  assert.equal(session.matchId, secondMatch);
  assert.equal(session.gameModeRuntime!.revision, 1);
  assert.equal(heartbeat('bridge-two', '200', 2, 1, 'Warmup').code, 200);
  assert.equal(session.phase, GamePhase.PreGameSetup);
});
