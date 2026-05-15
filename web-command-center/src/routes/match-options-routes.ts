import type { Express } from 'express';
import { GamePhase } from '../types';

type MatchOptions = {
  undercoverModeEnabled: boolean;
  caorenModifiersEnabled: boolean;
};

type RegisterMatchOptionsRoutesDeps = {
  adminPassword?: string;
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
    const adminPassword = String(req.body?.adminPassword || '');

    if (!deps.adminPassword || adminPassword !== deps.adminPassword) {
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
      `本局模式已更新：卧底模式${matchOptions.undercoverModeEnabled ? '开启' : '关闭'}，CaorenCup 修改${matchOptions.caorenModifiersEnabled ? '开启' : '关闭'}`
    );

    deps.broadcastState();

    res.json({
      success: true,
      phase: deps.getPhase(),
      matchOptions,
    });
  });
}
