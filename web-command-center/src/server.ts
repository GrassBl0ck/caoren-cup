// server.ts
import { createServer } from 'http';
import path from 'node:path';
import express from 'express';
import { Server as SocketIOServer } from 'socket.io';
import multer from 'multer';
import {
    GamePhase,
    WsEvents,
} from './types';
import { getSession } from './session-manager';
import { restoreSessionSnapshot, saveSessionSnapshotNow, scheduleSessionSnapshotSave } from './session-persistence';
import { findPlayerById, generateBindCode, sanitizeGameStateForViewer } from './player-utils';
import { registerMatchOptionsRoutes } from './routes/match-options-routes';
import { registerCaorenModRoutes } from './routes/caoren-mod-routes';
import {
    LobbyAnnouncement,
    readLobbyAnnouncement,
    registerLobbyAnnouncementRoutes,
} from './routes/lobby-announcement-routes';
import { registerUpdateAnnouncementRoutes } from './routes/update-announcement-routes';
import { registerPluginRoutes } from './plugin-api';
import { registerSocketHandlers } from './socket-handlers';
import { registerGameCodeLogin, v1333ConsumeGameLoginTicket } from './v1333-game-login';
import {
    injectFlowBroadcast,
    injectNotify,
    applyMatchOptions,
    finishDraftPick,
    finishMapVote,
    finishSideVote,
    resumeRestoredPregameFlow,
} from './game-flow-manager';
import {
    enqueuePluginCommand,
    getPluginCommandQueueSummary,
} from './plugin-command-queue';
import { ADMIN_PASSWORD } from './game-constants';
import { DUEL_DEFAULT_MAP, DUEL_DEFAULT_ROUND_TIME_MINUTES, DUEL_DEFAULT_UTILITY_MODE, DUEL_DEFAULT_WORKSHOP_ID, getDefaultDuelRounds, normalizeDuelMap, normalizeDuelRoundTimeMinutes, normalizeDuelRounds, normalizeDuelUtilityMode, normalizeDuelWorkshopId } from './duel-config';
import { registerIdentityAuthRoutes } from './identity/auth-routes';
import { initializeIdentityRuntime, lobbyIdentityService, playerCenterMatchSocketTickets, playerCenterSessionStore } from './identity/identity-runtime';
import { bindPlayerCenterSocketIdentity } from './identity/player-center-socket';
import { attachMembershipToSession, detachMatchMembershipsForScoreboard, removeIdentityFromSession } from './identity/session-integration';
import { registerWeaponPaintsHttpRoutes } from './weaponpaints/http-routes';
import { initializeWeaponPaintsRuntime } from './weaponpaints/runtime';
import { UpdateAnnouncementService } from './update-announcements/update-announcement-service';
import { UpdateAnnouncementStore } from './update-announcements/update-announcement-store';
import { registerUpdateAnnouncementAdminSocketHandlers } from './update-announcements/update-announcement-admin-socket';
import type { PublicUpdateAnnouncement } from './update-announcements/update-announcement-types';
import {
    GAME_INACTIVITY_CHECK_INTERVAL_MS,
    GameInactivityMonitor,
    GameInactivityTracker,
} from './game-inactivity-watchdog';

const app = express();
if (process.env.TRUST_PROXY === 'loopback') app.set('trust proxy', 'loopback');
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
const restoredSessionSnapshot = restoreSessionSnapshot();

const httpServer = createServer(app);
const io = new SocketIOServer(httpServer, {
    cors: { origin: '*', methods: ['GET', 'POST'] },
});

io.use(async (socket, next) => {
    try {
        const result = await bindPlayerCenterSocketIdentity({
            cookieHeader: socket.handshake.headers.cookie,
            socketData: socket.data,
            sessionStore: playerCenterSessionStore,
            service: lobbyIdentityService,
        });
        socket.data.playerCenterSessionInvalid = result === 'invalid';
        next();
    } catch (error) {
        console.error('[PlayerCenter] Socket 会话认证失败：', error);
        socket.data.playerCenterSessionInvalid = true;
        next();
    }
});

const updateAnnouncementStore = new UpdateAnnouncementStore(
    process.env.UPDATE_ANNOUNCEMENT_STORE_PATH
        || path.resolve(__dirname, '..', 'runtime', 'update-announcements.json'),
);
const updateAnnouncementService = new UpdateAnnouncementService(updateAnnouncementStore);

const broadcastUpdateAnnouncements = (announcements: PublicUpdateAnnouncement[]) => {
    io.emit(WsEvents.UPDATE_ANNOUNCEMENTS, { announcements });
};

let scoreboardCleanupSessionId = '';
const gameInactivityTracker = new GameInactivityTracker();

const broadcastState = () => {
    const session = getSession();
    gameInactivityTracker.observe(session);
    for (const socket of io.sockets.sockets.values()) {
        const viewerId = socket.data?.playerId || null;
        socket.emit(WsEvents.GAME_STATE, sanitizeGameStateForViewer(session, viewerId));
    }
    scheduleSessionSnapshotSave();
    if (session.phase === GamePhase.Scoreboard && scoreboardCleanupSessionId !== session.sessionId) {
        scoreboardCleanupSessionId = session.sessionId;
        void finalizeScoreboardMemberships(session).catch((error) => {
            console.error('[PlayerCenter] 结算阶段成员清理失败：', error);
            if (scoreboardCleanupSessionId === session.sessionId) scoreboardCleanupSessionId = '';
        });
    }
};

const notifyMessage = (msg: string) => {
    io.emit(WsEvents.NOTIFICATION, { message: msg });
};

const clearSocketMatchPermission = (socket: any, message: string) => {
    const playerId = String(socket.data.playerId || '');
    if (playerId) socket.leave(playerId);
    socket.data.playerId = null;
    socket.emit(WsEvents.LOGIN_RESPONSE, { success: false, resetClient: true, message });
};

const finalizeScoreboardMemberships = async (scoreboardSession: ReturnType<typeof getSession>) => {
    await lobbyIdentityService.leaveSessionMemberships(scoreboardSession.sessionId);
    if (getSession().sessionId !== scoreboardSession.sessionId || scoreboardSession.phase !== GamePhase.Scoreboard) return;
    const detachedPlayerIds = new Set(detachMatchMembershipsForScoreboard(scoreboardSession));
    for (const socket of io.sockets.sockets.values()) {
        if (!detachedPlayerIds.has(String(socket.data.playerId || ''))) continue;
        clearSocketMatchPermission(socket, '本场比赛已结束，你已返回玩家中心。');
        socket.emit('PLAYER_CENTER_MATCH_ENDED', { message: '本场比赛已结束。' });
    }
    saveSessionSnapshotNow();
    broadcastState();
};

const invalidatePlayerCenterSockets = (event: { identityId: string; preserveSessionId?: string; revokedSessionId?: string }) => {
    for (const socket of io.sockets.sockets.values()) {
        if (socket.data.identityId !== event.identityId) continue;
        if (event.preserveSessionId && socket.data.playerCenterSessionId === event.preserveSessionId) continue;
        if (event.revokedSessionId && socket.data.playerCenterSessionId !== event.revokedSessionId) continue;
        clearSocketMatchPermission(socket, '玩家中心会话已失效，比赛权限已清除。');
        delete socket.data.identityId;
        delete socket.data.playerCenterSessionId;
        socket.emit('PLAYER_CENTER_SESSION_INVALID');
    }
};

registerIdentityAuthRoutes(app, {
    consumeGameLoginTicket: v1333ConsumeGameLoginTicket,
    playerCenterMatchSocketTickets,
    onPlayerCenterSessionsRevoked: invalidatePlayerCenterSockets,
    onDesktopAuthAudit: (event) => {
        // 不记录 Bearer、完整 SteamID 或 Cookie；HTTP 风险由 transport 字段明确保留。
        console.info('[DesktopAuthAudit]', JSON.stringify(event));
    },
    onPlayerCenterMatchJoined: (membership) => {
        const player = attachMembershipToSession(getSession(), membership);
        if (!player.bindCode) player.bindCode = generateBindCode();
        broadcastState();
        saveSessionSnapshotNow();
    },
    onPlayerCenterMatchLeft: (identityId) => {
        const removed = removeIdentityFromSession(getSession(), identityId);
        for (const socket of io.sockets.sockets.values()) {
            if (socket.data.identityId !== identityId) continue;
            clearSocketMatchPermission(socket, '你已退出本场比赛，玩家中心账号仍保持登录。');
            socket.emit('PLAYER_CENTER_MATCH_ENDED', { message: '你已退出本场比赛。' });
        }
        if (removed) {
            broadcastState();
            saveSessionSnapshotNow();
        }
    },
});

const broadcastAnnouncement = (announcement: LobbyAnnouncement) => {
    io.emit(WsEvents.LOBBY_ANNOUNCEMENT, { announcement });
};

injectFlowBroadcast(broadcastState);
injectNotify(notifyMessage);
if (restoredSessionSnapshot) resumeRestoredPregameFlow();

const ensureMatchOptions = () => {
    const session = getSession();
    if (!session.matchOptions) {
        session.matchOptions = {
            matchMode: 'competitive',
            matchController: 'matchzy',
            undercoverModeEnabled: true,
            caorenModifiersEnabled: false,
            duelMap: DUEL_DEFAULT_MAP,
            duelMapWorkshopId: DUEL_DEFAULT_WORKSHOP_ID,
            duelRoundTimeMinutes: DUEL_DEFAULT_ROUND_TIME_MINUTES,
            duelRounds: getDefaultDuelRounds(),
            duelUtilityMode: DUEL_DEFAULT_UTILITY_MODE,
        };
    }
    session.matchOptions.matchMode = session.matchOptions.matchMode === 'duel' ? 'duel' : 'competitive';
    session.matchOptions.matchController = session.matchOptions.matchMode === 'duel' ? 'caoren' : 'matchzy';
    session.matchOptions.undercoverModeEnabled = session.matchOptions.matchMode === 'duel'
        ? false
        : session.matchOptions.undercoverModeEnabled !== false;
    session.matchOptions.caorenModifiersEnabled = session.matchOptions.caorenModifiersEnabled === true;
    session.matchOptions.duelMap = normalizeDuelMap(session.matchOptions.duelMap);
    session.matchOptions.duelMapWorkshopId = normalizeDuelWorkshopId(session.matchOptions.duelMapWorkshopId) || DUEL_DEFAULT_WORKSHOP_ID;
    session.matchOptions.duelRoundTimeMinutes = normalizeDuelRoundTimeMinutes(session.matchOptions.duelRoundTimeMinutes);
    session.matchOptions.duelRounds = normalizeDuelRounds(session.matchOptions.duelRounds);
    session.matchOptions.duelUtilityMode = normalizeDuelUtilityMode(session.matchOptions.duelUtilityMode);
    return session.matchOptions;
};

registerMatchOptionsRoutes(app, {
    adminPassword: ADMIN_PASSWORD,
    getPhase: () => getSession().phase,
    getPlayerById: (playerId: string) => findPlayerById(getSession(), playerId),
    ensureMatchOptions,
    applyMatchOptions,
    notify: notifyMessage,
    broadcastState,
});

registerCaorenModRoutes(app, {
    adminPassword: ADMIN_PASSWORD,
    getPhase: () => getSession().phase,
    ensureMatchOptions,
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

registerUpdateAnnouncementRoutes(app, {
    adminPassword: ADMIN_PASSWORD,
    service: updateAnnouncementService,
    broadcastPublic: broadcastUpdateAnnouncements,
});

registerUpdateAnnouncementAdminSocketHandlers({
    io,
    service: updateAnnouncementService,
    getSession,
    broadcastPublic: broadcastUpdateAnnouncements,
});

registerPluginRoutes(app, {
    broadcastState,
    notifyMessage,
});

registerWeaponPaintsHttpRoutes(app);

const socketHandlers = registerSocketHandlers(io, {
    broadcastState,
    notifyMessage,
    persistSessionNow: saveSessionSnapshotNow,
});

const gameInactivityMonitor = new GameInactivityMonitor(
    gameInactivityTracker,
    getSession,
    socketHandlers.terminateCurrentGameAndKickAll,
    scheduleSessionSnapshotSave,
);

setInterval(() => {
    void gameInactivityMonitor.tick().catch((error) => {
        console.error('[GameInactivity] 自动结束无操作比赛失败：', error);
    });
}, GAME_INACTIVITY_CHECK_INTERVAL_MS);

io.on('connection', (socket) => {
    if (socket.data.playerCenterSessionInvalid) socket.emit('PLAYER_CENTER_SESSION_INVALID');
    socket.emit(WsEvents.LOBBY_ANNOUNCEMENT, { announcement: readLobbyAnnouncement() });
    if (updateAnnouncementService.isAvailable()) {
        socket.emit(WsEvents.UPDATE_ANNOUNCEMENTS, {
            announcements: updateAnnouncementService.listPublic(),
        });
    }
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
Promise.all([
    initializeIdentityRuntime(),
    initializeWeaponPaintsRuntime(),
    updateAnnouncementService.initialize(),
])
    .then(() => httpServer.listen(PORT, () => console.log(`草人杯指挥台已启动: http://localhost:${PORT}`)))
    .catch((error) => {
        console.error('[Startup] 核心服务初始化失败，服务未启动：', error);
        process.exitCode = 1;
    });
