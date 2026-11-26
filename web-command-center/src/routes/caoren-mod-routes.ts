import type { Request, Express } from 'express';

import { GamePhase } from '../types';
import { getCaorenModuleDefinitions, buildCaorenCommandFromRequest } from '../caoren-modules';
import type {
  BridgeCommand,
  PluginCommandQueueSummaryItem,
} from '../plugin-command-queue';

type MatchOptionsLike = {
  matchMode?: 'competitive' | 'duel';
  matchController?: 'matchzy' | 'caoren';
  undercoverModeEnabled?: boolean;
  caorenModifiersEnabled?: boolean;
};

interface RegisterCaorenModRoutesDeps {
  isMatchAdmin: (req: Request) => boolean;
  getPhase: () => GamePhase;
  ensureMatchOptions: () => MatchOptionsLike;
  enqueuePluginCommand: (
    type: string,
    payload: Record<string, any>
  ) => BridgeCommand;
  getPluginCommandQueueSummary: () => PluginCommandQueueSummaryItem[];
  notify: (message: string) => void;
  broadcastState: () => void;
}

export const registerCaorenModRoutes = (
  app: Express,
  deps: RegisterCaorenModRoutesDeps
) => {
  app.get('/api/caoren-modules', (req, res) => {
    res.json({
      success: true,
      modules: getCaorenModuleDefinitions(),
      queue: deps.isMatchAdmin(req) ? deps.getPluginCommandQueueSummary() : [],
      phase: deps.getPhase(),
      matchOptions: deps.ensureMatchOptions(),
      allowedPhases: Object.values(GamePhase),
    });
  });

  app.post('/api/admin/caoren-mod-command', (req, res) => {
    if (!deps.isMatchAdmin(req)) {
      return res.status(401).json({
        success: false,
        error: '管理员会话已失效',
      });
    }

    const matchOptions = deps.ensureMatchOptions();

    if (matchOptions.caorenModifiersEnabled !== true) {
      return res.status(400).json({
        success: false,
        error: '\u672c\u5c40\u672a\u542f\u7528 CaorenCup \u4fee\u6539\u3002\u8bf7\u5148\u5728\u672c\u5c40\u6a21\u5f0f\u8bbe\u7f6e\u4e2d\u5f00\u542f\u3002',
        phase: deps.getPhase(),
        matchOptions,
      });
    }

    try {
      const built = buildCaorenCommandFromRequest(req.body || {});
      const queued = deps.enqueuePluginCommand('EXECUTE_SERVER_COMMAND', {
        command: built.command,
        label: built.label,
        moduleId: built.moduleId,
        moduleTitle: built.moduleTitle,
        action: built.action,
        requestedAt: Date.now(),
      });

      deps.notify(`CaorenCup \u4fee\u6539\u5df2\u52a0\u5165\u4e0b\u53d1\u961f\u5217\uff1a${built.label}`);
      deps.broadcastState();

      res.json({
        success: true,
        commandId: queued.id,
        queuedAt: queued.createdAt,
        command: built.command,
        label: built.label,
        queue: deps.getPluginCommandQueueSummary(),
      });
    } catch (err) {
      const message = err instanceof Error ? err.message : 'CaorenCup \u4fee\u6539\u547d\u4ee4\u751f\u6210\u5931\u8d25';
      res.status(400).json({ success: false, error: message });
    }
  });
};
