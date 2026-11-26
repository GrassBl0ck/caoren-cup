import { isIP } from 'node:net';
import { AccountLoginGuard } from './identity/password-auth';
import { isCurrentAbilitySyncConfirmed } from './ability-phase-one-policy';
// server.ts
import { createServer } from 'http';
import path from 'node:path';
import express from 'express';
import { Server as SocketIOServer } from 'socket.io';
import {
    GamePhase,
    WsEvents,
} from './types';
import { getSession } from './session-manager';
import { restoreSessionSnapshot, saveSessionSnapshotNow, scheduleSessionSnapshotSave } from './session-persistence';
import { formalServerLifecycle } from './formal-server-lifecycle';
import { loadTaskPresets } from './task-preset-store';
import { generateBindCode, sanitizeGameStateForViewer } from './player-utils';
import { desktopRoomAdmins } from './desktop-formal-room';
import { registerMatchAdminSessions } from './routes/match-admin-session';
import { registerMatchOptionsRoutes } from './routes/match-options-routes';
import { registerGameModeRoutes } from './routes/game-mode-routes';
import { registerCaorenModRoutes } from './routes/caoren-mod-routes';
import {
    LobbyAnnouncement,
    readLobbyAnnouncement,
    registerLobbyAnnouncementRoutes,
} from './routes/lobby-announcement-routes';
import { registerUpdateAnnouncementRoutes } from './routes/update-announcement-routes';
import { registerPluginRoutes } from './plugin-api';
import { PluginStateBroadcastScheduler } from './plugin-broadcast-scheduler';
import { registerSocketHandlers } from './socket-handlers';
import { syncRoomPresence } from './desktop-formal-room';
import { registerGameCodeLogin, v1333ConsumeGameLoginTicket } from './v1333-game-login';
import {
    injectFlowBroadcast,
    injectNotify,
    applyMatchOptions,
    advancePhase,
    finishDraftPick,
    finishMapVote,
    finishSideVote,
    resumeRestoredPregameFlow,
    pollAbilityFlowTimeouts,
} from './game-flow-manager';
import {
    enqueuePluginCommand,
    getPluginCommandQueueSummary,
} from './plugin-command-queue';
import { enqueueCurrentAbilitySync } from './ability-sync-orchestrator';
import {
    ABILITY_BAN_DEFAULT_SECONDS,
    ABILITY_DRAFT_BATCH_DEFAULT_SECONDS,
    ADMIN_PASSWORD,
} from './game-constants';
import { DUEL_DEFAULT_MAP, DUEL_DEFAULT_ROUND_TIME_MINUTES, DUEL_DEFAULT_UTILITY_MODE, DUEL_DEFAULT_WORKSHOP_ID, getDefaultDuelRounds, normalizeDuelMap, normalizeDuelRoundTimeMinutes, normalizeDuelRounds, normalizeDuelUtilityMode, normalizeDuelWorkshopId } from './duel-config';
import { registerIdentityAuthRoutes } from './identity/auth-routes';
import { registerBoardPages } from './portal-routes';
import { normalizeUnbalancedRosterOptions } from './unbalanced-roster';
import { GAME_MODE_MANIFESTS, normalizeGameModeId, normalizeGameModeOverrides, modeUsesUndercoverFlow, modeUsesUnbalancedRoster } from './game-mode';
import { gameAvatars } from './game-avatars';
import { initializeIdentityRuntime, lobbyIdentityService, playerCenterBootstrapTickets, playerCenterMatchSocketTickets, playerCenterSessionStore } from './identity/identity-runtime';
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
registerBoardPages(app, {
    service: lobbyIdentityService,
    sessions: playerCenterSessionStore,
    pagesDirectory: path.resolve(__dirname, '..', 'pages'),
});
// Keep external thumbnail storage reachable at the same URLs as the desktop image bundle.
if (process.env.WEAPONPAINTS_IMAGE_ROOT) {
    for (const pack of ['base', 'stickers']) {
        app.use('/weaponpaints/' + pack + '/images', express.static(
            path.resolve(process.env.WEAPONPAINTS_IMAGE_ROOT, pack, 'images'), { index: false, redirect: false }));
    }
}
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
// Older catalogs and the desktop cache use the original PNG paths. In web
// distribution packages the same images are stored with a WebP suffix.
app.get(/^\/weaponpaints\/(base|stickers)\/images\/([\w-]+\.(?:png|jpe?g|webp))$/i, (req, res, next) => {
    const images = process.env.WEAPONPAINTS_IMAGE_ROOT || path.resolve('public', 'weaponpaints');
    const root = path.resolve(images, req.params[0], 'images');
    res.sendFile(req.params[1] + '.webp', { root, maxAge: '1d' }, err => {
        if (err && !res.headersSent) next();
    });
});
const restoredSessionSnapshot = restoreSessionSnapshot();
loadTaskPresets();

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

const hasMatchSocket = (playerId: string) => !!io.sockets.adapter.rooms.get(playerId)?.size;
const roomViewerId = (socket: import('socket.io').Socket, room: ReturnType<typeof getSession>) => {
    const seat = room.players[socket.data.playerId];
    if (!seat) return null;
    if (seat.identityId) {
        const account = lobbyIdentityService.getLoginAccount(seat.identityId);
        if (socket.data.playerCenterSessionInvalid || socket.data.identityId !== seat.identityId || !account?.age14ConfirmedAt || !account.enabled ||
            !playerCenterSessionStore.isBoundSessionCurrent(socket.data.playerCenterSessionId, seat.identityId, account.updatedAt)) return null;
        if (seat.membershipId) {
            const member = lobbyIdentityService.getMembership(seat.membershipId);
            if (member?.sessionId !== room.sessionId || member.leftAt || member.blockedAt) return null;
        }
        if (seat.role === 'Admin' && !desktopRoomAdmins.allows(seat.identityId, lobbyIdentityService)) return null;
    }
    return seat.playerId;
};

const broadcastState = () => {
    const session = getSession();
    syncRoomPresence(session, hasMatchSocket);
    gameInactivityTracker.observe(session);
    for (const socket of io.sockets.sockets.values()) {
        const viewerId = roomViewerId(socket, session);
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

const pluginStateBroadcastScheduler = new PluginStateBroadcastScheduler(broadcastState, 500);

const notifyMessage = (msg: string) => {
    const room = getSession();
    for (const socket of io.sockets.sockets.values()) {
        if (roomViewerId(socket, room)) socket.emit(WsEvents.NOTIFICATION, { message: msg });
    }
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
    desktopRoomActions: {
        hasMatchSocket,
        publish: broadcastState,
        start: (host) => advancePhase(GamePhase.Lobby, GamePhase.CaptainSelection, host.name, host.playerId),
        changeMode: (modeId) => {
            const current = getSession().matchOptions;
            current.abilityModeEnabled = modeId === 'ability';
            applyMatchOptions({
                ...current, gameModeId: modeId, matchMode: 'competitive',
                matchController: 'caoren', matchRuntimeEnabled: true,
            });
        },
    },
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
    onAccountErased: identityId => {
        const room = getSession();
        removeIdentityFromSession(room, identityId);
        for (const seat of Object.values(room.players)) {
            if (seat.identityId !== identityId) continue;
            if (seat.role === 'Admin') {
                delete room.players[seat.playerId];
                room.playerOrder = room.playerOrder.filter(id => id !== seat.playerId);
            } else {
                // Preserve a locked draft slot and its match-local key while removing account details.
                seat.name = '已注销玩家';
                seat.isOnline = false;
                delete seat.membershipId;
                delete seat.identityId;
                delete seat.steamId;
                delete seat.bindCode;
                delete seat.sessionCode;
            }
        }
        saveSessionSnapshotNow();
        broadcastState();
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
if (restoredSessionSnapshot) {
    resumeRestoredPregameFlow();
    const restoredSession = getSession();
    if (restoredSession.phase === GamePhase.PreGameSetup
        && restoredSession.matchOptions.matchMode !== 'duel'
        && restoredSession.matchOptions.abilityModeEnabled === true
        && restoredSession.abilitySyncState?.status !== 'disabled') {
        try {
            enqueueCurrentAbilitySync(restoredSession);
        } catch (error) {
            console.error('[AbilitySync] 恢复同步队列失败：', error);
        }
    }
}

const ensureMatchOptions = () => {
    const session = getSession();
    if (!session.matchOptions) {
        session.matchOptions = {
            gameModeId: 'competitive',
            gameModeWarmupSeconds: 15,
            gameModeCvarOverrides: {},
            matchMode: 'competitive',
            matchController: 'caoren',
            undercoverModeEnabled: false,
            caorenModifiersEnabled: false,
            duelMap: DUEL_DEFAULT_MAP,
            duelMapWorkshopId: DUEL_DEFAULT_WORKSHOP_ID,
            duelRoundTimeMinutes: DUEL_DEFAULT_ROUND_TIME_MINUTES,
            duelRounds: getDefaultDuelRounds(),
            duelUtilityMode: DUEL_DEFAULT_UTILITY_MODE,
            unbalancedModeEnabled: false,
            unbalancedTeamASize: 3,
            unbalancedTeamBSize: 5,
        };
    }
    const gameModeId = normalizeGameModeId(session.matchOptions.gameModeId, session.matchOptions);
    const gameModeManifest = GAME_MODE_MANIFESTS[gameModeId];
    session.matchOptions.gameModeId = gameModeId;
    session.matchOptions.gameModeWarmupSeconds = Math.max(1, Math.min(300, Math.floor(Number(session.matchOptions.gameModeWarmupSeconds || gameModeManifest.defaultWarmupSeconds))));
    session.matchOptions.gameModeCvarOverrides = normalizeGameModeOverrides(gameModeId, session.matchOptions.gameModeCvarOverrides);
    session.matchOptions.matchMode = session.matchOptions.matchMode === 'duel' ? 'duel' : 'competitive';
    const restoredCaorenRuntime = session.matchOptions.matchRuntimeEnabled === true
        && ['competitive','ability'].includes(gameModeId)
        && session.matchOptions.matchMode !== 'duel'
        && session.matchOptions.matchController === 'caoren';
    session.matchOptions.matchController = session.matchOptions.matchMode === 'duel' ? 'caoren' : session.matchOptions.matchController === 'matchzy' ? 'matchzy' : 'caoren';
    session.matchOptions.matchRuntimeEnabled = restoredCaorenRuntime;
    session.matchOptions.abilityModeEnabled = session.matchOptions.matchMode === 'duel'
        ? false
        : session.matchOptions.abilityModeEnabled === true;
    const abilityBanCount = Number(session.matchOptions.abilityBanCountPerTeam);
    session.matchOptions.abilityBanCountPerTeam = Number.isFinite(abilityBanCount)
        ? Math.max(0, Math.floor(abilityBanCount))
        : 1;
    const abilityBanSeconds = Number(session.matchOptions.abilityBanSeconds);
    session.matchOptions.abilityBanSeconds = Number.isFinite(abilityBanSeconds)
        ? Math.max(1, Math.floor(abilityBanSeconds))
        : ABILITY_BAN_DEFAULT_SECONDS;
    const abilityDraftBatchSeconds = Number(session.matchOptions.abilityDraftBatchSeconds);
    session.matchOptions.abilityDraftBatchSeconds = Number.isFinite(abilityDraftBatchSeconds)
        ? Math.max(1, Math.floor(abilityDraftBatchSeconds))
        : ABILITY_DRAFT_BATCH_DEFAULT_SECONDS;
    session.matchOptions.undercoverModeEnabled = session.matchOptions.matchMode === 'duel' ? false : modeUsesUndercoverFlow(gameModeId);
    session.matchOptions.caorenModifiersEnabled = session.matchOptions.caorenModifiersEnabled === true;
    session.matchOptions.unbalancedModeEnabled = modeUsesUnbalancedRoster(gameModeId);
    session.matchOptions.duelMap = normalizeDuelMap(session.matchOptions.duelMap);
    session.matchOptions.duelMapWorkshopId = normalizeDuelWorkshopId(session.matchOptions.duelMapWorkshopId) || DUEL_DEFAULT_WORKSHOP_ID;
    session.matchOptions.duelRoundTimeMinutes = normalizeDuelRoundTimeMinutes(session.matchOptions.duelRoundTimeMinutes);
    session.matchOptions.duelRounds = normalizeDuelRounds(session.matchOptions.duelRounds);
    session.matchOptions.duelUtilityMode = normalizeDuelUtilityMode(session.matchOptions.duelUtilityMode);
    Object.assign(session.matchOptions, normalizeUnbalancedRosterOptions(session.matchOptions));
    return session.matchOptions;
};

// Both legacy login messages share a failure budget across reconnects.
const legacyLoginGuard = new AccountLoginGuard();
io.on('connection', socket => {
    socket.use(([event, payload], next) => {
        if (event !== 'LOGIN' && event !== 'GAME_CODE_LOGIN') {
            if (socket.data.playerId && !roomViewerId(socket, getSession())) {
                clearSocketMatchPermission(socket, '房间会话已失效，请重新进入。');
            }
            return next();
        }
        const remote = socket.handshake.address;
        const forwarded = String(socket.handshake.headers['x-forwarded-for'] || '').split(',').at(-1)?.trim() || '';
        const localProxy = process.env.TRUST_PROXY === 'loopback' && ['127.0.0.1', '::1', '::ffff:127.0.0.1'].includes(remote);
        const keys = [localProxy && isIP(forwarded) ? forwarded : remote];
        if (legacyLoginGuard.check(keys).blocked) {
            socket.emit(WsEvents.LOGIN_RESPONSE, { success: false, message: '尝试次数过多，请稍后登录。' });
            return;
        }
        const supplied = event === 'LOGIN' ? payload?.extraParam : payload?.credential;
        if (!ADMIN_PASSWORD || typeof supplied !== 'string' || supplied.trim() !== ADMIN_PASSWORD) legacyLoginGuard.recordFailure(keys);
        else legacyLoginGuard.clear(keys);
        next();
    });
});

const isMatchAdmin = registerMatchAdminSessions(io, getSession, (identityId, centerSessionId) => {
    const account = lobbyIdentityService.getLoginAccount(identityId);
    return !!account?.enabled && playerCenterSessionStore.isBoundSessionCurrent(centerSessionId, identityId, account.updatedAt) &&
        desktopRoomAdmins.allows(identityId, lobbyIdentityService);
});

registerMatchOptionsRoutes(app, {
    isMatchAdmin,
    getPhase: () => getSession().phase,
    ensureMatchOptions,
    applyMatchOptions,
    notify: notifyMessage,
    broadcastState,
});

registerGameModeRoutes(app, {
    isAbilitySyncConfirmed: () => isCurrentAbilitySyncConfirmed(getSession()),
    isMatchAdmin,
    getPhase: () => getSession().phase,
    getAvailableGameModes: () => getSession().availableGameModes,
    ensureMatchOptions,
    applyMatchOptions,
    enqueuePluginCommand,
    notify: notifyMessage,
    broadcastState,
});

registerCaorenModRoutes(app, {
    isMatchAdmin,
    getPhase: () => getSession().phase,
    ensureMatchOptions,
    enqueuePluginCommand,
    getPluginCommandQueueSummary,
    notify: notifyMessage,
    broadcastState,
});

registerLobbyAnnouncementRoutes(app, {
    isMatchAdmin,
    notify: notifyMessage,
    broadcastAnnouncement,
});

registerUpdateAnnouncementRoutes(app, {
    isMatchAdmin,
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
    isMatchAdmin,
    broadcastState,
    requestStateBroadcast: () => pluginStateBroadcastScheduler.request(),
    flushStateBroadcast: () => pluginStateBroadcastScheduler.flush(),
    notifyMessage,
});

registerWeaponPaintsHttpRoutes(app);

const socketHandlers = registerSocketHandlers(io, {
    hasMatchSocket,
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
    identityService: lobbyIdentityService,
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
    pollAbilityFlowTimeouts(now);
}, 1000);

setInterval(() => {
    formalServerLifecycle.poll(getSession(), broadcastState);
}, 2500);


const PORT = process.env.PORT || 3000;
Promise.all([
    initializeIdentityRuntime().then(() => gameAvatars.initialize(lobbyIdentityService.avatarIdentities())),
    initializeWeaponPaintsRuntime(),
    updateAnnouncementService.initialize(),
])
    .then(() => httpServer.listen({ port: Number(PORT), host: process.env.WEB_BIND_HOST || undefined },
        () => console.log(`草人杯指挥台已启动: http://${process.env.WEB_BIND_HOST || 'localhost'}:${PORT}`)))
    .catch((error) => {
        console.error('[Startup] 核心服务初始化失败，服务未启动：', error);
        process.exitCode = 1;
    });
