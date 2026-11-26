import type { Express, Request } from 'express';
import { GamePhase, MatchOptions } from '../types';

type RegisterMatchOptionsRoutesDeps = {
  isMatchAdmin: (req: Request) => boolean;
  getPhase: () => GamePhase;
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
    if (!deps.isMatchAdmin(req)) {
      return res.status(403).json({
        success: false,
        error: '请先以本房间管理员身份登录',
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

    const rawOptions = req.body?.matchOptions || {};
    if (rawOptions.matchMode === 'duel') {
      return res.status(400).json({
        success: false,
        error: '网页单挑已归档，请在游戏内使用 /duel 指令。',
        phase,
      });
    }
    const matchOptions = deps.applyMatchOptions(rawOptions);

    deps.notify(
      matchOptions.matchMode === 'duel'
        ? `本局模式已更新：单挑模式，地图 ${matchOptions.duelMap}`
        : `本局模式已更新：竞技模式，控制器 ${matchOptions.matchController === 'caoren' ? '草人杯' : 'MatchZy'}，卧底模式${matchOptions.undercoverModeEnabled ? '开启' : '关闭'}，CaorenCup 修改${matchOptions.caorenModifiersEnabled ? '开启' : '关闭'}`
    );

    deps.broadcastState();

    res.json({
      success: true,
      phase: deps.getPhase(),
      matchOptions,
    });
  });
}
