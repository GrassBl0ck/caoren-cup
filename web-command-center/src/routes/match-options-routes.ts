import type { Express } from 'express';
import { GamePhase, MatchOptions, Player } from '../types';

type RegisterMatchOptionsRoutesDeps = {
  adminPassword?: string;
  getPhase: () => GamePhase;
  getPlayerById?: (playerId: string) => Player | undefined;
  ensureMatchOptions: () => MatchOptions;
  applyMatchOptions: (rawOptions: unknown) => MatchOptions;
  notify: (message: string) => void;
  broadcastState: () => void;
};

export function registerMatchOptionsRoutes(app: Express, deps: RegisterMatchOptionsRoutesDeps) {
  app.get('/api/match-options', (_req, res) => {
    res.json({
      success: true,
      phase: deps.getPhase(),
      matchOptions: deps.ensureMatchOptions(),
    });
  });

  app.post('/api/admin/match-options', (req, res) => {
    const adminPassword = String(req.body?.adminPassword || '');
    const playerId = String(req.body?.playerId || '');
    const player = playerId && deps.getPlayerById ? deps.getPlayerById(playerId) : undefined;
    const isLoggedInAdmin = player?.role === 'Admin';
    const hasPassword = !!deps.adminPassword && adminPassword === deps.adminPassword;

    if (!isLoggedInAdmin && !hasPassword) {
      return res.status(401).json({
        success: false,
        error: '管理员密码错误',
      });
    }

    const phase = deps.getPhase();

    if (phase !== GamePhase.Lobby) {
      return res.status(400).json({
        success: false,
        error: '只能在大厅阶段修改本局模式',
        phase,
      });
    }

    const matchOptions = deps.applyMatchOptions(req.body?.matchOptions || {});

    deps.notify(
      matchOptions.matchMode === 'duel'
        ? `本局模式已更新：单挑模式，地图 ${matchOptions.duelMap}`
        : `本局模式已更新：竞技模式，卧底模式${matchOptions.undercoverModeEnabled ? '开启' : '关闭'}，CaorenCup 修改${matchOptions.caorenModifiersEnabled ? '开启' : '关闭'}`
    );

    deps.broadcastState();

    res.json({
      success: true,
      phase: deps.getPhase(),
      matchOptions,
    });
  });
}
