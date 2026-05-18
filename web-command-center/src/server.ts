// server.ts
import { createServer } from 'http';
import express from 'express';
import { Server as SocketIOServer } from 'socket.io';
import multer from 'multer';
import {
    GamePhase,
    WsEvents,
    TaskCell,
} from './types';
import { getSession } from './session-manager';
import {
    getGamePlayers,
    normalizeSteamId,
    sanitizeForPublic,
} from './player-utils';
import { calculateScores, parseCsv, csvRowToStats } from './scoring';
import { registerMatchOptionsRoutes } from './routes/match-options-routes';
import { registerCaorenModRoutes } from './routes/caoren-mod-routes';
import { registerPluginRoutes } from './plugin-api';
import { registerSocketHandlers } from './socket-handlers';
import { registerGameCodeLogin } from './v1333-game-login';
import {
    injectFlowBroadcast,
    injectNotify,
    applyMatchOptions,
    finishDraftPick,
    finishMapVote,
    finishSideVote,
} from './game-flow-manager';
import {
    enqueuePluginCommand,
    getPluginCommandQueueSummary,
} from './plugin-command-queue';
import { ADMIN_PASSWORD } from './game-constants';

const app = express();
app.use(express.json({ limit: '1mb' }));
app.use(express.static('public', {
    setHeaders: (res, filePath) => {
        const normalizedFilePath = filePath.replace(/\\/g, '/');
        if (normalizedFilePath.endsWith('.html')) {
            res.setHeader('Content-Type', 'text/html; charset=utf-8');
        }
        if (normalizedFilePath.includes('/assets/audio/')) {
            res.setHeader('Cache-Control', 'public, max-age=31536000, immutable');
        } else {
            res.setHeader('Cache-Control', 'no-cache');
        }
    }
}));
const upload = multer({ storage: multer.memoryStorage() });

const httpServer = createServer(app);
const io = new SocketIOServer(httpServer, {
    cors: { origin: '*', methods: ['GET', 'POST'] },
});

const broadcastState = () => {
    const session = getSession();
    for (const socket of io.sockets.sockets.values()) {
        const viewerId = socket.data?.playerId || null;
        socket.emit(WsEvents.GAME_STATE, sanitizeForPublic(session, viewerId));
    }
};

const notifyMessage = (msg: string) => {
    io.emit(WsEvents.NOTIFICATION, { message: msg });
};

injectFlowBroadcast(broadcastState);
injectNotify(notifyMessage);

registerMatchOptionsRoutes(app, {
    adminPassword: ADMIN_PASSWORD,
    getPhase: () => getSession().phase,
    ensureMatchOptions: () => {
        const session = getSession();
        if (!session.matchOptions) {
            session.matchOptions = { undercoverModeEnabled: true, caorenModifiersEnabled: false };
        }
        session.matchOptions.undercoverModeEnabled = session.matchOptions.undercoverModeEnabled !== false;
        session.matchOptions.caorenModifiersEnabled = session.matchOptions.caorenModifiersEnabled === true;
        return session.matchOptions;
    },
    applyMatchOptions,
    notify: notifyMessage,
    broadcastState,
});

registerCaorenModRoutes(app, {
    adminPassword: ADMIN_PASSWORD,
    getPhase: () => getSession().phase,
    ensureMatchOptions: () => {
        const session = getSession();
        if (!session.matchOptions) {
            session.matchOptions = { undercoverModeEnabled: true, caorenModifiersEnabled: false };
        }
        session.matchOptions.undercoverModeEnabled = session.matchOptions.undercoverModeEnabled !== false;
        session.matchOptions.caorenModifiersEnabled = session.matchOptions.caorenModifiersEnabled === true;
        return session.matchOptions;
    },
    enqueuePluginCommand,
    getPluginCommandQueueSummary,
    notify: notifyMessage,
    broadcastState,
});

registerPluginRoutes(app, {
    broadcastState,
    notifyMessage,
});

registerSocketHandlers(io, {
    broadcastState,
    notifyMessage,
});

registerGameCodeLogin(app, io, {
    broadcastState,
});

// CSV 上传路由
app.post('/api/upload-csv', upload.single('csvfile'), (req, res) => {
    const session = getSession();
    if (session.phase !== GamePhase.Scoreboard) return res.status(400).json({ error: '只能在结算阶段上传CSV' });
    if (!req.file) return res.status(400).json({ error: '没有上传文件' });
    try {
        const csvText = req.file.buffer.toString('utf-8');
        const rows = parseCsv(csvText);
        let matchedCount = 0;
        const players = getGamePlayers(session);
        for (const player of players) {
            let csvRow = player.steamId
                ? rows.find(r => normalizeSteamId(r.steamid64) === normalizeSteamId(player.steamId))
                : undefined;
            if (!csvRow) csvRow = rows.find(r => String(r.name) === player.name);
            if (csvRow) {
                player.stats = csvRowToStats(csvRow);
                matchedCount++;
            }
        }
        calculateScores(session);
        broadcastState();

        let txtReport = "=== 草人杯 卧底任务战报 ===\n\n";
        for (const p of players) {
            if (p.gameRole === 'Undercover' && p.taskGrid) {
                txtReport += `[卧底] ${p.name} 的任务完成情况:\n`;
                for (const cell of Object.values(p.taskGrid) as TaskCell[]) {
                    txtReport += `  - ${cell.cellId}: ${cell.description} | 状态: ${cell.status} | N值: ${cell.nValue || 0}\n`;
                }
                txtReport += `  最终总分: ${p.finalScore}\n\n`;
            }
        }
        res.json({ success: true, matchedPlayers: matchedCount, report: txtReport });
    } catch (e) {
        res.status(500).json({ error: 'CSV 解析失败' });
    }
});

// 定时器轮询
setInterval(() => {
    const session = getSession();
    const now = Date.now();
    if (session.phase === GamePhase.PlayerDraft && session.draftPickTimeoutAt && now > session.draftPickTimeoutAt) {
        finishDraftPick('timeout');
    }
    if (session.phase === GamePhase.MapBan && session.mapVote && now > session.mapVote.timeoutAt) {
        finishMapVote('timeout');
    }
    if (session.phase === GamePhase.SidePick && session.sideVote && now > session.sideVote.timeoutAt) {
        finishSideVote('timeout');
    }
}, 1000);

const PORT = process.env.PORT || 3000;
httpServer.listen(PORT, () => console.log(`草人杯指挥台已启动: http://localhost:${PORT}`));