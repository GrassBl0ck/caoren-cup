import type { Express } from 'express';
import { GamePhase, MatchOptions, Player } from '../types';
import {
  GAME_MODE_MANIFESTS,
  isAvailableGameMode,
  normalizeGameModeId,
  normalizeGameModeOverrides,
} from '../game-mode';

interface RegisterGameModeRoutesDeps {
  adminPassword?: string;
  getPhase: () => GamePhase;
  getPlayerById?: (playerId: string) => Player | undefined;
  ensureMatchOptions: () => MatchOptions;
  applyMatchOptions: (rawOptions: unknown) => MatchOptions;
  enqueuePluginCommand: (type: string, payload: Record<string, any>) => unknown;
  notify: (message: string) => void;
  broadcastState: () => void;
}

const isAdminRequest = (req: any, deps: RegisterGameModeRoutesDeps): boolean => {
  const adminPassword = String(req.body?.adminPassword || '');
  const playerId = String(req.body?.playerId || '');
  const player = playerId && deps.getPlayerById ? deps.getPlayerById(playerId) : undefined;
  return player?.role === 'Admin' || (!!deps.adminPassword && adminPassword === deps.adminPassword);
};

const canChangeMode = (phase: GamePhase) => phase === GamePhase.Lobby || phase === GamePhase.PreGameSetup;

export function registerGameModeRoutes(app: Express, deps: RegisterGameModeRoutesDeps) {
  app.get('/api/game-modes', (_req, res) => {
    const options = deps.ensureMatchOptions();
    res.json({
      success: true,
      phase: deps.getPhase(),
      current: options.gameModeId,
      modes: Object.values(GAME_MODE_MANIFESTS).map(mode => ({
        ...mode,
        available: isAvailableGameMode(mode.id),
      })),
    });
  });

  app.post('/api/admin/game-mode', (req, res) => {
    if (!isAdminRequest(req, deps)) {
      return res.status(401).json({ success: false, error: '管理员密码错误' });
    }
    const phase = deps.getPhase();
    if (!canChangeMode(phase)) {
      return res.status(400).json({ success: false, error: 'Warmup 或正式比赛开始后不能切换模式', phase });
    }

    const rawOptions = req.body?.matchOptions && typeof req.body.matchOptions === 'object' ? req.body.matchOptions : {};
    const raw = { ...rawOptions, ...(req.body?.gameMode || {}), ...req.body };
    const modeId = normalizeGameModeId(raw.gameModeId, rawOptions);
    if (!isAvailableGameMode(modeId)) {
      return res.status(400).json({ success: false, error: `模式尚未加载：${modeId}`, phase });
    }

    const matchOptions = deps.applyMatchOptions({
      ...deps.ensureMatchOptions(),
      ...rawOptions,
      gameModeId: modeId,
      gameModeWarmupSeconds: raw.warmupSeconds ?? raw.gameModeWarmupSeconds,
      gameModeCvarOverrides: normalizeGameModeOverrides(modeId, raw.cvarOverrides ?? raw.gameModeCvarOverrides),
    });
    deps.enqueuePluginCommand('GAME_MODE_SELECT', {
      modeId,
      warmupSeconds: matchOptions.gameModeWarmupSeconds,
      cvarOverrides: matchOptions.gameModeCvarOverrides,
      label: `选择 ${modeId} 模式`,
    });
    deps.notify(`本局模式已选择：${GAME_MODE_MANIFESTS[modeId].displayName}`);
    deps.broadcastState();
    return res.json({ success: true, phase: deps.getPhase(), matchOptions });
  });

  app.post('/api/admin/game-mode/start', (req, res) => {
    if (!isAdminRequest(req, deps)) {
      return res.status(401).json({ success: false, error: '管理员密码错误' });
    }
    const phase = deps.getPhase();
    if (!canChangeMode(phase)) {
      return res.status(400).json({ success: false, error: '只能在大厅或赛前配置阶段开始模式', phase });
    }
    const matchOptions = deps.ensureMatchOptions();
    if (!isAvailableGameMode(matchOptions.gameModeId)) {
      return res.status(400).json({ success: false, error: `模式尚未加载：${matchOptions.gameModeId}`, phase });
    }
    deps.enqueuePluginCommand('GAME_MODE_START', {
      modeId: matchOptions.gameModeId,
      warmupSeconds: matchOptions.gameModeWarmupSeconds,
      cvarOverrides: matchOptions.gameModeCvarOverrides,
      mapName: String(req.body?.mapName || '').trim() || undefined,
      label: `启动 ${matchOptions.gameModeId} 模式`,
    });
    deps.notify(`正在启动 ${GAME_MODE_MANIFESTS[matchOptions.gameModeId].displayName}，即将进入 Warmup。`);
    return res.json({ success: true, phase, matchOptions });
  });

  app.post('/api/admin/game-mode/stop', (req, res) => {
    if (!isAdminRequest(req, deps)) {
      return res.status(401).json({ success: false, error: '管理员密码错误' });
    }
    deps.enqueuePluginCommand('GAME_MODE_STOP', { label: '停止当前游戏模式' });
    deps.notify('已请求停止当前游戏模式并恢复托管 CVar。');
    return res.json({ success: true, phase: deps.getPhase(), matchOptions: deps.ensureMatchOptions() });
  });
}
