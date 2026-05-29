// server.ts
import { createServer } from 'http';
import express from 'express';
import { Server as SocketIOServer } from 'socket.io';
import multer from 'multer';
import {
    GamePhase,
    WsEvents,
} from './types';
import { getSession } from './session-manager';
import { restoreSessionSnapshot, scheduleSessionSnapshotSave } from './session-persistence';
import { sanitizeForPublic } from './player-utils';
import { registerMatchOptionsRoutes } from './routes/match-options-routes';
import { registerCaorenModRoutes } from './routes/caoren-mod-routes';
import {
    LobbyAnnouncement,
    readLobbyAnnouncement,
    registerLobbyAnnouncementRoutes,
} from './routes/lobby-announcement-routes';
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
        if (normalizedFilePath.endsWith('.js')) {
            res.setHeader('Content-Type', 'application/javascript; charset=utf-8');
        }
        if (normalizedFilePath.endsWith('.css')) {
            res.setHeader('Content-Type', 'text/css; charset=utf-8');
        }
        if (normalizedFilePath.endsWith('.json')) {
            res.setHeader('Content-Type', 'application/json; charset=utf-8');
        }
        if (normalizedFilePath.includes('/assets/audio/')) {
            res.setHeader('Cache-Control', 'public, max-age=31536000, immutable');
        } else {
            res.setHeader('Cache-Control', 'no-cache');
        }
    }
}));
const upload = multer({ storage: multer.memoryStorage() });

restoreSessionSnapshot();

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
    scheduleSessionSnapshotSave();
};

const notifyMessage = (msg: string) => {
    io.emit(WsEvents.NOTIFICATION, { message: msg });
};

const broadcastAnnouncement = (announcement: LobbyAnnouncement) => {
    io.emit(WsEvents.LOBBY_ANNOUNCEMENT, { announcement });
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

registerLobbyAnnouncementRoutes(app, {
    adminPassword: ADMIN_PASSWORD,
    notify: notifyMessage,
    broadcastAnnouncement,
});

registerPluginRoutes(app, {
    broadcastState,
    notifyMessage,
});

registerSocketHandlers(io, {
    broadcastState,
    notifyMessage,
});

io.on('connection', (socket) => {
    socket.emit(WsEvents.LOBBY_ANNOUNCEMENT, { announcement: readLobbyAnnouncement() });
});

registerGameCodeLogin(app, io, {
    broadcastState,
});

// MatchZy CSV import is intentionally disabled. Official stats now come from the bridge plugin.
app.post('/api/upload-csv', upload.single('csvfile'), (_req, res) => {
    res.status(410).json({
        success: false,
        error: 'MatchZy CSV ' + '\u5df2\u505c\u7528\uff0c\u5f53\u524d\u7248\u672c\u4f7f\u7528\u63d2\u4ef6\u5b9e\u65f6\u6570\u636e\u7edf\u8ba1\u3002',
    });
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
