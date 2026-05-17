import { createServer } from 'http';
import express from 'express';
import { Server as SocketIOServer } from 'socket.io';
import { v4 as uuidv4 } from 'uuid';
import multer from 'multer';
import {
    GameSession,
    GamePhase,
    Player,
    PlayerRole,
    WsEvents,
    TaskTemplate,
    PluginLivePlayer,
    Team,
    RosterTeam,
    LiveGameData,
} from './types';
import { canTransition, startTimer, getPhaseDuration, getAutoNextPhase } from './state-machine';
import {
  enqueuePluginCommand,
  takeQueuedPluginCommands,
  ackPluginCommand,
  getPluginCommandQueueSummary,
} from './plugin-command-queue';
import { registerCaorenModRoutes } from './routes/caoren-mod-routes';
import { registerMatchOptionsRoutes } from './routes/match-options-routes';
// ========== 默认任务模板生成 ==========
const getDefaultTaskTemplate = (): TaskTemplate => {
    return {
        cells: {
            'A1': { levelLabel: '1', description: '击杀 2 名敌人', level: 1, type: 'count', targetCount: 2, nType: 'none', nValue: 0 },
            'A2': { levelLabel: '3N', description: 'N回合，击杀一名队友', level: 3, type: 'custom', nType: '3N_multi', nMin: 1, nMax: 3, nValue: 0 },
            'A3': { levelLabel: '3', description: '拆包一次', level: 3, type: 'custom', targetCount: 1, nType: 'none', nValue: 0 },
            'B1': { levelLabel: '5/4N', description: '单回合连续击杀队友', level: 5, type: 'custom', nType: '5_4N_single', nMin: 1, nMax: 3, baseLevel: 5, extraLevel: 4, nValue: 0 },
            'B2': { levelLabel: '2', description: '闪瞎 3 名敌人', level: 2, type: 'count', targetCount: 3, nType: 'none', nValue: 0 },
            'B3': { levelLabel: '1', description: '存活到回合结束', level: 1, type: 'custom', targetCount: 1, nType: 'none', nValue: 0 },
            'C1': { levelLabel: '2', description: '赢得 1 个回合', level: 2, type: 'custom', targetCount: 1, nType: 'none', nValue: 0 },
            'C2': { levelLabel: '3N', description: '单回合击杀N名队友', level: 3, type: 'custom', nType: '3N_single', nMin: 1, nMax: 3, nValue: 0 },
            'C3': { levelLabel: '3', description: '造成 1000 点伤害', level: 3, type: 'damage', targetCount: 1000, nType: 'none', nValue: 0 },
        },
        lines: [
            ['A1', 'A2', 'A3'], ['B1', 'B2', 'B3'], ['C1', 'C2', 'C3'],
            ['A1', 'B1', 'C1'], ['A2', 'B2', 'C2'], ['A3', 'B3', 'C3'],
            ['A1', 'B2', 'C3'], ['A3', 'B2', 'C1'],
        ],
        replacementTask: { level: 4, description: '隐藏的未知替换任务' }
    };
};

// ========== 初始化 ==========
const createInitialSession = (): GameSession => {
    const mapPool = ['Dust II', 'Inferno', 'Mirage', 'Ancient', 'Anubis', 'Nuke', 'Overpass', 'Cache', 'Train'];
    const baseOrder: RosterTeam[] = ['A', 'B'];
    const banSequence: RosterTeam[] = [];
    while (banSequence.length < mapPool.length - 1) { banSequence.push(...baseOrder); }
    banSequence.length = mapPool.length - 1;

    return {
        sessionId: uuidv4(),
        phase: GamePhase.Lobby,
        matchId: uuidv4(),
        matchOptions: {
            undercoverModeEnabled: true,
            caorenModifiersEnabled: false,
        },
        players: {},
        playerOrder: [],
        teams: {
            A: { name: 'A', players: [] },
            B: { name: 'B', players: [] },
        },
        captains: { A: null, B: null },
        rollValues: { A: null, B: null },
        draftOrder: [],
        draftIndex: 0,
        mapPool: mapPool,
        bannedMaps: [],
        selectedMap: null,
        currentBanTeam: null,
        banSequence: banSequence,
        sidePickTeam: 'A',
        selectedSide: null,
        undercoverCount: 1,
        detectiveCount: 0,
        rolesReleased: false,
        taskTemplate: getDefaultTaskTemplate(),
        questionsUsed: 0,
        currentQuestion: null,
        questionAnswer: null,
        secondQuestionAnswered: false,
        accusations: {},
        timerEndAt: null,
        timerPhase: null,
        adminLock: { holderId: null, acquiredAt: null },
        liveGameData: undefined,
        rollTimeout: undefined as any,
        createdAt: Date.now(),
        autoClearMinutes: 15,
    };
};

let gameSession = createInitialSession();
const ADMIN_PASSWORD = process.env.ADMIN_PASSWORD || 'CHANGE_ME_ADMIN_PASSWORD';
const PLUGIN_TOKEN = process.env.PLUGIN_TOKEN || 'CHANGE_ME_PLUGIN_TOKEN';

/**
 * Stage 3.11：选人、地图 BP、选边均支持真实倒计时。
 * 玩家点击卡片即投票，不需要确认按钮；倒计时结束后服务器统计最高票并执行。
 */
// 队长选人：一段倒计时内可完成当前蛇形批次（1 人或 2 人）。
const DRAFT_PICK_SECONDS = Number(process.env.DRAFT_PICK_SECONDS || 15);
// 地图 BP：第 1 手 12 秒，第 2 手 11 秒，第 3 手及以后 10 秒。
const MAP_BAN_FIRST_SECONDS = Number(process.env.MAP_BAN_FIRST_SECONDS || 12);
const MAP_BAN_SECOND_SECONDS = Number(process.env.MAP_BAN_SECOND_SECONDS || 11);
const MAP_BAN_LATER_SECONDS = Number(process.env.MAP_BAN_LATER_SECONDS || 10);
const SIDE_PICK_VOTE_SECONDS = Number(process.env.SIDE_PICK_VOTE_SECONDS || 12);
/**
 * 每个地图 Ban 回合要禁用几张图。
 * 默认 1，保持当前 7 图池逐张 Ban 到最后 1 张图的流程。
 * 如果要一轮 Ban 2 张，可在 ecosystem.config.cjs 里设置 MAP_BAN_COUNT_PER_TURN: "2"。
 */
const MAP_BAN_COUNT_PER_TURN = Math.max(1, Number(process.env.MAP_BAN_COUNT_PER_TURN || 1));

let draftPickTimer: ReturnType<typeof setTimeout> | null = null;
let mapVoteTimer: ReturnType<typeof setTimeout> | null = null;
let sideVoteTimer: ReturnType<typeof setTimeout> | null = null;
const clearDraftPickTimer = () => { if (draftPickTimer) { clearTimeout(draftPickTimer); draftPickTimer = null; } };
const clearMapVoteTimer = () => { if (mapVoteTimer) { clearTimeout(mapVoteTimer); mapVoteTimer = null; } };
const clearSideVoteTimer = () => { if (sideVoteTimer) { clearTimeout(sideVoteTimer); sideVoteTimer = null; } };
const clearBpTimers = () => { clearMapVoteTimer(); clearSideVoteTimer(); };
const clearAllFlowTimers = () => { clearDraftPickTimer(); clearMapVoteTimer(); clearSideVoteTimer(); };

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

// ========== 工具函数 ==========
const findPlayerById = (id: string) => gameSession.players[id];
const generateBindCode = () => Math.floor(1000 + Math.random() * 9000).toString();
const normalizeLoginText = (value: unknown): string => String(value || '').trim();
const findPlayerByBindCode = (bindCode: unknown): Player | undefined => {
    const code = normalizeLoginText(bindCode);
    if (!code) return undefined;
    return Object.values(gameSession.players).find(p => p.bindCode === code);
};
const findPlayerByName = (name: unknown): Player | undefined => {
    const normalized = normalizeLoginText(name);
    if (!normalized) return undefined;
    return Object.values(gameSession.players).find(p => p.name === normalized);
};
const getGamePlayers = (): Player[] => Object.values(gameSession.players).filter(p => p.role !== 'Spectator' && p.role !== 'Admin');
const hasAnyDetective = (): boolean => Object.values(gameSession.players).some(p => p.gameRole === 'Detective' && p.role !== 'Spectator' && p.role !== 'Admin');
const getDefaultMatchOptions = () => ({
    undercoverModeEnabled: true,
    caorenModifiersEnabled: false,
});

const ensureMatchOptions = () => {
    if (!gameSession.matchOptions) {
        gameSession.matchOptions = getDefaultMatchOptions();
    }

    gameSession.matchOptions.undercoverModeEnabled = gameSession.matchOptions.undercoverModeEnabled !== false;
    gameSession.matchOptions.caorenModifiersEnabled = gameSession.matchOptions.caorenModifiersEnabled === true;

    return gameSession.matchOptions;
};

const isUndercoverModeEnabled = (): boolean => {
    return ensureMatchOptions().undercoverModeEnabled !== false;
};
const isUndercoverOnlyPhase = (phase: GamePhase): boolean => {
    return phase === GamePhase.MidGameQA || phase === GamePhase.PostGameAccusation;
};

const resolveNextPhaseByMatchOptions = (from: GamePhase, requestedTo: GamePhase): GamePhase => {
    if (isUndercoverModeEnabled()) {
        return requestedTo;
    }

    // 卧底模式关闭后，侦探问答和赛后指认都是非法阶段。
    if (isUndercoverOnlyPhase(requestedTo)) {
        return GamePhase.Scoreboard;
    }

    // 卧底模式关闭后，比赛结束只能直接去积分结算。
    if (from === GamePhase.LiveGame) {
        return GamePhase.Scoreboard;
    }

    return requestedTo;
};

const forceSkipUndercoverOnlyPhaseIfNeeded = () => {
    if (isUndercoverModeEnabled()) return;

    if (!isUndercoverOnlyPhase(gameSession.phase)) return;

    try {
        calculateScores();
    } catch (err) {
        console.error('[MatchOptions] calculateScores failed while force skipping undercover-only phase:', err);
    }

    gameSession.phase = GamePhase.Scoreboard;
    gameSession.timerEndAt = null;
    gameSession.timerPhase = null;
    broadcastState();
};

const clearUndercoverModeState = () => {
    gameSession.undercoverCount = 0;
    gameSession.detectiveCount = 0;
    gameSession.rolesReleased = false;
    gameSession.questionsUsed = 0;
    gameSession.currentQuestion = null;
    gameSession.questionAnswer = null;
    gameSession.secondQuestionAnswered = false;
    gameSession.accusations = {};

    for (const player of getGamePlayers()) {
        player.gameRole = 'Soldier';
        delete player.taskGrid;
        player.abandonCount = 0;
        player.replaceCount = 0;
        player.hintUsedCount = 0;
        player.detectiveQuestionCount = 0;
    }
};

const applyMatchOptions = (rawOptions: any) => {
    gameSession.matchOptions = {
        undercoverModeEnabled: rawOptions?.undercoverModeEnabled !== false,
        caorenModifiersEnabled: rawOptions?.caorenModifiersEnabled === true,
    };

    if (!gameSession.matchOptions.undercoverModeEnabled) {
        clearUndercoverModeState();
        forceSkipUndercoverOnlyPhaseIfNeeded();
    }

    return gameSession.matchOptions;
};
const isPlayerOnline = (playerId: string): boolean => !!io.sockets.adapter.rooms.get(playerId)?.size;

const shouldRevealRoleToViewer = (viewer: Player | undefined, target: Player): boolean => {
    if (!target.gameRole) return false;
    if (viewer?.role === 'Admin') return true;
    if (viewer?.playerId === target.playerId && gameSession.rolesReleased) return true;
    return false;
};

const shouldRevealTaskGridToViewer = (viewer: Player | undefined, target: Player): boolean => {
    if (!target.taskGrid) return false;
    if (viewer?.role === 'Admin') return true;
    if (viewer?.playerId === target.playerId && gameSession.rolesReleased) return true;
    return false;
};

const sanitizeForPublic = (session: GameSession, viewerId?: string | null): any => {
    const viewer = viewerId ? session.players[viewerId] : undefined;
    const s: any = { ...session };
    s.serverNow = Date.now();
    s.players = {};
    for (const [id, p] of Object.entries(session.players)) {
        const revealRole = shouldRevealRoleToViewer(viewer, p);
        const revealTaskGrid = shouldRevealTaskGridToViewer(viewer, p);
        s.players[id] = {
            playerId: p.playerId, name: p.name, role: p.role,
            gameRole: revealRole ? p.gameRole : undefined,
            rosterTeam: p.rosterTeam, team: p.team, isReady: p.isReady,
            steamIdBound: !!p.steamId,
            finalScore: p.finalScore, scoreBreakdown: p.scoreBreakdown,
            stats: p.stats,
            detectiveQuestionCount: viewer?.role === 'Admin' || p.playerId === viewerId ? p.detectiveQuestionCount : undefined,
            taskGrid: revealTaskGrid ? p.taskGrid : undefined,
        };
    }
    delete s.rollTimeout;
    return s;
};

const broadcastState = () => {
    for (const socket of io.sockets.sockets.values()) {
        const viewerId = socket.data?.playerId || null;
        socket.emit(WsEvents.GAME_STATE, sanitizeForPublic(gameSession, viewerId));
    }
};
// ========== 第二阶段：网页 CaorenCup 修改命令队列 ==========
// 已拆分到 ./plugin-command-queue.ts

// ========== 第一阶段：赛前本局模式设置 ==========

registerMatchOptionsRoutes(app, {
  adminPassword: ADMIN_PASSWORD,
  getPhase: () => gameSession.phase,
  ensureMatchOptions,
  applyMatchOptions,
  notify: (message: string) => io.emit(WsEvents.NOTIFICATION, { message }),
  broadcastState,
});


// ========== 第二阶段：CaorenCup 修改可视化面板 API ==========
// 已拆分到 ./routes/caoren-mod-routes.ts
registerCaorenModRoutes(app, {
  adminPassword: ADMIN_PASSWORD,
  getPhase: () => gameSession.phase,
  ensureMatchOptions,
  enqueuePluginCommand,
  getPluginCommandQueueSummary,
  notify: (message: string) => io.emit(WsEvents.NOTIFICATION, { message }),
  broadcastState,
});

const sendPrivateData = (socketId: string, playerId: string) => {
    const player = findPlayerById(playerId);
    if (!player) return;
    const socket = io.sockets.sockets.get(socketId);
    if (!socket) return;
    const reveal = player.role === 'Admin' || !!gameSession.rolesReleased;
    socket.emit(WsEvents.PRIVATE_DATA, {
        bindCode: player.bindCode,
        taskGrid: reveal ? player.taskGrid : undefined,
        gameRole: reveal ? player.gameRole : undefined,
    });
};


// ========== CS2 插件接入辅助 ==========
const normalizeSteamId = (steamId: unknown): string => String(steamId || '').replace(/[^0-9]/g, '');
const findPlayerBySteamId = (steamId: unknown): Player | undefined => {
    const normalized = normalizeSteamId(steamId);
    if (!normalized) return undefined;
    return getGamePlayers().find(p => normalizeSteamId(p.steamId) === normalized);
};
const ensureStats = (player: Player) => {
    if (!player.stats) player.stats = { kills: 0, deaths: 0, assists: 0, damage: 0 };
    return player.stats;
};
const normalizeTeam = (team: unknown): Team | undefined => {
    const value = String(team || '').toUpperCase();
    if (value === 'CT' || value === 'COUNTERTERRORIST' || value === 'COUNTER_TERRORIST' || value === '3') return 'CT';
    if (value === 'T' || value === 'TERRORIST' || value === '2') return 'T';
    return undefined;
};
const otherRosterTeam = (team: RosterTeam): RosterTeam => team === 'A' ? 'B' : 'A';
const oppositeSide = (side: Team): Team => side === 'CT' ? 'T' : 'CT';
const requirePluginAuth = (req: any, res: any, next: any) => {
    const token = req.header('x-caoren-plugin-token') || req.query?.token;
    if (!PLUGIN_TOKEN || token !== PLUGIN_TOKEN) return res.status(401).json({ success: false, error: '插件认证失败' });
    next();
};
const isPluginLivePhase = () => [GamePhase.LiveGame, GamePhase.MidGameQA, GamePhase.PostGameAccusation, GamePhase.Scoreboard].includes(gameSession.phase);
const createEmptyLiveGameData = (): LiveGameData => ({
    scoreCT: 0,
    scoreT: 0,
    scoreA: 0,
    scoreB: 0,
    currentRound: 0,
    pluginConnected: false,
    winnerTeam: null,
    matchFinished: false,
    winTarget: 13,
    lastScoredRound: 0,
    rawPluginRound: 0,
    roundBaseOffset: undefined as number | undefined,
});
const normalizePluginRound = (rawRound: unknown): number => {
    if (!gameSession.liveGameData) gameSession.liveGameData = createEmptyLiveGameData();
    const live = gameSession.liveGameData;
    const raw = Math.floor(Number(rawRound || 0));
    if (!Number.isFinite(raw) || raw <= 0) return Math.max(0, live.currentRound || 0);
    live.rawPluginRound = raw;
    if (typeof live.roundBaseOffset !== 'number') {
        // 插件可能在正式比赛前已经经历过若干回合：第一次被网页接收的插件回合视作正式第 1 回合。
        live.roundBaseOffset = Math.max(0, raw - 1);
    }
    return Math.max(1, raw - live.roundBaseOffset);
};

const resetFormalMatchCounters = () => {
    const oldLive = gameSession.liveGameData || createEmptyLiveGameData();
    const raw = Math.max(1, Math.floor(Number(oldLive.rawPluginRound || oldLive.currentRound || 1)));
    const keepMap = oldLive.mapName;
    const keepPluginConnected = oldLive.pluginConnected;
    const keepHeartbeatAt = oldLive.lastPluginHeartbeatAt;
    gameSession.liveGameData = createEmptyLiveGameData();
    gameSession.liveGameData.rawPluginRound = raw;
    gameSession.liveGameData.roundBaseOffset = Math.max(0, raw - 1);
    gameSession.liveGameData.currentRound = 1;
    gameSession.liveGameData.mapName = keepMap;
    gameSession.liveGameData.pluginConnected = keepPluginConnected;
    gameSession.liveGameData.lastPluginHeartbeatAt = keepHeartbeatAt;
    for (const p of getGamePlayers()) {
        p.stats = { kills: 0, deaths: 0, assists: 0, damage: 0 };
        p.finalScore = undefined;
        p.scoreBreakdown = undefined;
    }
};

const getTeamPlayers = (team: RosterTeam): Player[] => getGamePlayers().filter(p => p.rosterTeam === team || gameSession.teams[team].players.includes(p.playerId));
const setRosterLiveSides = (teamASide: Team) => {
    const teamBSide = oppositeSide(teamASide);
    for (const p of getTeamPlayers('A')) p.team = teamASide;
    for (const p of getTeamPlayers('B')) p.team = teamBSide;
};
const getRequiredWinTarget = (scoreA: number, scoreB: number): number => {
    const minScore = Math.min(scoreA, scoreB);
    if (minScore < 12) return 13;
    return 16 + Math.floor((minScore - 12) / 3) * 3;
};
const getMatchWinner = (scoreA: number, scoreB: number): RosterTeam | null => {
    const target = getRequiredWinTarget(scoreA, scoreB);
    if (scoreA >= target && scoreA > scoreB) return 'A';
    if (scoreB >= target && scoreB > scoreA) return 'B';
    return null;
};
const updateMatchFinishState = () => {
    if (!gameSession.liveGameData) return;
    const scoreA = gameSession.liveGameData.scoreA || 0;
    const scoreB = gameSession.liveGameData.scoreB || 0;
    const winTarget = getRequiredWinTarget(scoreA, scoreB);
    const winner = getMatchWinner(scoreA, scoreB);
    gameSession.liveGameData.winTarget = winTarget;
    gameSession.liveGameData.winnerTeam = winner;
    gameSession.liveGameData.matchFinished = !!winner;
};
const resolveRosterTeamByInitialSide = (side: Team, roundNumber?: number): RosterTeam | null => {
    if (!gameSession.selectedSide) return null;

    // selectedSide 表示 A 队开局选择的阵营。
    // CS2 MR12 常规局第 13 回合换边，因此在无法通过真人绑定玩家判断时，
    // 用这个规则兜底。加时/特殊赛制下仍建议至少两边各有一名真人绑定，
    // 或由管理员手动修正比分。
    let teamASide: Team = gameSession.selectedSide;
    const round = Number(roundNumber || gameSession.liveGameData?.currentRound || 0);
    if (round >= 13 && round <= 24) teamASide = oppositeSide(teamASide);

    return side === teamASide ? 'A' : 'B';
};

const resolveWinnerRosterTeamByLiveSide = (winnerSide: Team, livePlayers?: any[], roundNumber?: number): RosterTeam | null => {
    const votes: Record<RosterTeam, number> = { A: 0, B: 0 };

    // 第一优先级：用插件快照里“已绑定真人玩家的当前 CT/T 阵营”判断。
    // 这可以自动适配换边。
    for (const raw of livePlayers || []) {
        const side = normalizeTeam(raw.team);
        if (side !== winnerSide) continue;
        const player = findPlayerBySteamId(raw.steamId);
        if (player?.rosterTeam) votes[player.rosterTeam] += 1;
    }

    // 第二优先级：用网页当前记录的玩家阵营判断。
    // 这是为了兼容插件快照暂时没带全玩家的情况。
    if (votes.A === 0 && votes.B === 0) {
        for (const player of getGamePlayers()) {
            if (player.team === winnerSide && player.rosterTeam) votes[player.rosterTeam] += 1;
        }
    }

    if (votes.A > votes.B) return 'A';
    if (votes.B > votes.A) return 'B';

    // 第三优先级：机器人测试/某一边没有真人时，根据选边结果兜底。
    return resolveRosterTeamByInitialSide(winnerSide, roundNumber);
};
const resetCurrentGame = (reason = '管理员开启新一轮') => {
    clearAllFlowTimers();
    const oldPlayers = gameSession.players;
    const oldOrder = gameSession.playerOrder;
    const oldRollTimeout = gameSession.rollTimeout;
    if (oldRollTimeout) clearTimeout(oldRollTimeout);

    gameSession = createInitialSession();
    gameSession.players = {};
    gameSession.playerOrder = [];

    // 正常“开启新一轮”时保留房间内的玩家和绑定关系，方便连续开赛。
    // 强制“终止本局游戏”不要调用这个函数，应该调用 terminateCurrentGameAndKickAll。
    for (const playerId of oldOrder) {
        const old = oldPlayers[playerId];
        if (!old) continue;
        const resetPlayer: Player = {
            playerId: old.playerId,
            name: old.name,
            role: old.role,
            steamId: old.steamId,
            bindCode: old.bindCode || generateBindCode(),
            isReady: false,
        };
        gameSession.players[playerId] = resetPlayer;
        gameSession.playerOrder.push(playerId);
    }

    io.emit(WsEvents.NOTIFICATION, { message: reason + '，已回到大厅并开启新一轮。' });
    broadcastState();
};

const terminateCurrentGameAndKickAll = (reason = '管理员强制终止本局游戏') => {
    clearAllFlowTimers();
    if (gameSession.rollTimeout) clearTimeout(gameSession.rollTimeout);

    // 先通知所有当前网页客户端回到登录界面。
    io.emit(WsEvents.LOGIN_RESPONSE, {
        success: false,
        resetClient: true,
        message: reason + '，所有玩家已被踢出房间，请重新进入。',
    });

    gameSession = createInitialSession();

    io.emit(WsEvents.NOTIFICATION, { message: reason + '，房间已清空，请重新进入。' });
    broadcastState();
};

const applyPluginKillEvent = (payload: any) => {
    const attacker = findPlayerBySteamId(payload.attackerSteamId);
    const victim = findPlayerBySteamId(payload.victimSteamId);
    const assister = findPlayerBySteamId(payload.assisterSteamId);
    if (attacker && attacker.playerId !== victim?.playerId) ensureStats(attacker).kills += 1;
    if (victim) ensureStats(victim).deaths += 1;
    if (assister && assister.playerId !== attacker?.playerId && assister.playerId !== victim?.playerId) ensureStats(assister).assists += 1;
};
const applyPluginDamageEvent = (payload: any) => {
    const attacker = findPlayerBySteamId(payload.attackerSteamId);
    const victim = findPlayerBySteamId(payload.victimSteamId);
    const damage = Math.max(0, Number(payload.damage || 0));
    if (!attacker || damage <= 0 || attacker.playerId === victim?.playerId) return;
    ensureStats(attacker).damage += damage;
};
const applyPluginRoundEndEvent = (payload: any) => {
    if (!gameSession.liveGameData) gameSession.liveGameData = createEmptyLiveGameData();
    const live = gameSession.liveGameData;
    const formalRound = typeof payload.round === 'number' ? normalizePluginRound(payload.round) : (live.currentRound || 0);
    if (formalRound) live.currentRound = formalRound;

    const winnerSide = normalizeTeam(payload.winner);
    const alreadyProcessed = !!formalRound && live.lastScoredRound === formalRound;

    // 重要：CT/T 参考比分和 A/B 正式比分都由后端按 round_end 事件累加。
    // 插件在正式比赛前的历史回合会通过 normalizePluginRound 折算，不再污染正式回合数。
    if (winnerSide && !alreadyProcessed) {
        if (winnerSide === 'CT') live.scoreCT += 1;
        else if (winnerSide === 'T') live.scoreT += 1;

        const winnerRoster = resolveWinnerRosterTeamByLiveSide(winnerSide, payload.players || [], formalRound);
        if (winnerRoster && !live.matchFinished) {
            if (winnerRoster === 'A') live.scoreA += 1;
            else live.scoreB += 1;
        } else if (!winnerRoster) {
            io.emit(WsEvents.NOTIFICATION, { message: `插件收到 ${winnerSide} 回合胜利，但无法判断对应 A/B 队；请检查是否已完成选边，或两边是否至少各有一名真人绑定。` });
        }

        if (formalRound) live.lastScoredRound = formalRound;
    }

    updateMatchFinishState();
    if (live.matchFinished && gameSession.phase === GamePhase.LiveGame) {
        const winnerLabel = live.winnerTeam === 'A' ? 'A队' : 'B队';
        io.emit(WsEvents.NOTIFICATION, { message: `比赛结束：${winnerLabel} 获胜，比分 ${live.scoreA}:${live.scoreB}` });
        advancePhase(
            GamePhase.LiveGame,
            isUndercoverModeEnabled() ? GamePhase.PostGameAccusation : GamePhase.Scoreboard
        );
    }
};
const updateLivePlayersFromSnapshot = (players: any[]) => {
    if (!gameSession.liveGameData) return;
    const livePlayers: Record<string, PluginLivePlayer> = {};
    for (const raw of players || []) {
        const steamId = normalizeSteamId(raw.steamId);
        if (!steamId) continue;
        const livePlayer: PluginLivePlayer = {
            steamId,
            name: String(raw.name || ''),
            team: normalizeTeam(raw.team),
            kills: Number(raw.kills || 0),
            deaths: Number(raw.deaths || 0),
            assists: Number(raw.assists || 0),
            damage: Number(raw.damage || 0),
            isAlive: typeof raw.isAlive === 'boolean' ? raw.isAlive : undefined,
        };
        livePlayers[steamId] = livePlayer;
        const player = findPlayerBySteamId(steamId);
        if (player) {
            if (livePlayer.team) player.team = livePlayer.team;
            player.stats = {
                ...(player.stats || {}),
                kills: livePlayer.kills,
                deaths: livePlayer.deaths,
                assists: livePlayer.assists,
                damage: livePlayer.damage,
            };
        }
    }
    gameSession.liveGameData.players = livePlayers;
};

// ========== 队长、地图辅助 ==========
const randomizeCaptain = (team: 'A' | 'B') => {
    const candidates = getGamePlayers().filter(p => p.playerId !== gameSession.captains.A && p.playerId !== gameSession.captains.B);
    if (candidates.length > 0) gameSession.captains[team] = candidates[Math.floor(Math.random() * candidates.length)].playerId;
};
const getAvailableMaps = () => gameSession.mapPool.filter(m => !gameSession.bannedMaps.includes(m));

const getAvailableDraftPlayers = (): Player[] => {
    const capAId = gameSession.captains.A;
    const capBId = gameSession.captains.B;
    return getGamePlayers().filter(p => !p.rosterTeam && p.playerId !== capAId && p.playerId !== capBId && p.role !== 'Admin');
};

const getCurrentDraftBatch = () => {
    if (gameSession.draftIndex >= gameSession.draftOrder.length) return null;
    const team = gameSession.draftOrder[gameSession.draftIndex];
    let start = gameSession.draftIndex;
    while (start > 0 && gameSession.draftOrder[start - 1] === team) start--;
    let end = gameSession.draftIndex;
    while (end < gameSession.draftOrder.length && gameSession.draftOrder[end] === team) end++;
    const totalCount = end - start;
    const pickedInBatch = gameSession.draftIndex - start;
    const remainingCount = end - gameSession.draftIndex;
    return { team, start, end, totalCount, pickedInBatch, remainingCount };
};

const isDraftComplete = (): boolean => {
    return gameSession.draftIndex >= gameSession.draftOrder.length || getAvailableDraftPlayers().length === 0;
};

const scheduleDraftToMapBan = () => {
    clearDraftPickTimer();
    gameSession.draftPickTimeoutAt = null;
    gameSession.timerEndAt = null;
    gameSession.timerPhase = null;
    if (gameSession.rollTimeout) clearTimeout(gameSession.rollTimeout);
    gameSession.rollTimeout = setTimeout(() => advancePhase(GamePhase.PlayerDraft, GamePhase.MapBan), 1500);
};

const removePlayerFromRosterTeams = (playerId: string) => {
    for (const team of ['A', 'B'] as const) {
        gameSession.teams[team].players = gameSession.teams[team].players.filter(id => id !== playerId);
    }
};

const assignPlayerToRoster = (playerId: string, team: RosterTeam): boolean => {
    const player = findPlayerById(playerId);
    if (!player || player.role === 'Admin' || player.role === 'Spectator') return false;
    if (playerId === gameSession.captains.A || playerId === gameSession.captains.B) return false;
    removePlayerFromRosterTeams(playerId);
    player.rosterTeam = team;
    if (!gameSession.teams[team].players.includes(playerId)) gameSession.teams[team].players.push(playerId);
    return true;
};

const applyDraftPick = (pickedId?: string, reason: 'manual' | 'timeout' = 'manual'): Player | null => {
    if (gameSession.phase !== GamePhase.PlayerDraft) return null;
    if (gameSession.draftIndex >= gameSession.draftOrder.length) return null;

    const currentTeam = gameSession.draftOrder[gameSession.draftIndex];
    const available = getAvailableDraftPlayers();
    if (available.length === 0) return null;

    let picked = pickedId ? available.find(p => p.playerId === pickedId) : undefined;
    if (!picked) picked = available[Math.floor(Math.random() * available.length)];

    if (!assignPlayerToRoster(picked.playerId, currentTeam)) return null;
    gameSession.draftIndex++;

    if (reason === 'timeout') {
        io.emit(WsEvents.NOTIFICATION, { message: `选人倒计时结束，系统为${currentTeam}队自动选择：${picked.name}` });
    }
    return picked;
};

const startDraftPickTimer = (shouldBroadcast = false) => {
    clearDraftPickTimer();
    if (gameSession.phase !== GamePhase.PlayerDraft) return;

    if (isDraftComplete()) {
        scheduleDraftToMapBan();
        if (shouldBroadcast) broadcastState();
        return;
    }

    const dur = Math.max(1, DRAFT_PICK_SECONDS);
    gameSession.draftPickTimeoutAt = Date.now() + dur * 1000;
    gameSession.timerEndAt = gameSession.draftPickTimeoutAt;
    gameSession.timerPhase = GamePhase.PlayerDraft;
    draftPickTimer = setTimeout(() => finishDraftPick('timeout'), dur * 1000);
    if (shouldBroadcast) broadcastState();
};

const finishDraftPick = (reason: 'timeout' | 'manual' = 'timeout') => {
    if (gameSession.phase !== GamePhase.PlayerDraft) return;
    clearDraftPickTimer();

    const batch = getCurrentDraftBatch();
    const team = batch?.team;
    if (team) {
        while (!isDraftComplete() && gameSession.draftOrder[gameSession.draftIndex] === team) {
            const picked = applyDraftPick(undefined, reason === 'timeout' ? 'timeout' : 'manual');
            if (!picked) break;
        }
    }

    if (isDraftComplete()) {
        gameSession.draftPickTimeoutAt = null;
        gameSession.timerEndAt = null;
        gameSession.timerPhase = null;
        broadcastState();
        scheduleDraftToMapBan();
    } else {
        startDraftPickTimer(true);
    }
};

const getCurrentMapBanCount = (): number => {
    const availableCount = getAvailableMaps().length;
    // 必须至少留下 1 张作为本局地图。
    return Math.min(MAP_BAN_COUNT_PER_TURN, Math.max(1, availableCount - 1));
};

const pickTopVotedMaps = (available: string[], votes: Record<string, string>, banCount: number): string[] => {
    const tally: Record<string, number> = {};
    available.forEach(m => tally[m] = 0);
    Object.values(votes).forEach(m => {
        if (available.includes(m)) tally[m] = (tally[m] || 0) + 1;
    });

    // 没有人投票时，从可用地图中随机 Ban，避免流程卡住。
    const withTieBreaker = available
        .map(map => ({ map, count: tally[map] || 0, tie: Math.random() }))
        .sort((a, b) => (b.count - a.count) || (b.tie - a.tie));

    return withTieBreaker.slice(0, Math.min(banCount, Math.max(1, available.length - 1))).map(x => x.map);
};

const getMapBanVoteDurationSeconds = (): number => {
    const turnNo = gameSession.bannedMaps.length + 1;
    if (turnNo === 1) return Math.max(1, MAP_BAN_FIRST_SECONDS);
    if (turnNo === 2) return Math.max(1, MAP_BAN_SECOND_SECONDS);
    return Math.max(1, MAP_BAN_LATER_SECONDS);
};

const startMapVote = (team: RosterTeam) => {
    clearMapVoteTimer();
    const dur = getMapBanVoteDurationSeconds();
    gameSession.mapVote = {
        team,
        votes: {},
        timeoutAt: Date.now() + dur * 1000,
        banCount: getCurrentMapBanCount(),
    };
    gameSession.currentBanTeam = team;
    gameSession.timerEndAt = gameSession.mapVote.timeoutAt;
    gameSession.timerPhase = GamePhase.MapBan;
    mapVoteTimer = setTimeout(() => finishMapVote('timeout'), dur * 1000);
};

const finishMapVote = (reason: 'timeout' | 'admin' | 'manual' = 'timeout') => {
    if (gameSession.phase !== GamePhase.MapBan || !gameSession.mapVote) return;
    clearMapVoteTimer();

    const available = getAvailableMaps();
    if (available.length === 0) return;

    const votes = gameSession.mapVote.votes ?? {};
    const banCount = Math.min(gameSession.mapVote.banCount || 1, Math.max(1, available.length - 1));
    const banMaps = pickTopVotedMaps(available, votes, banCount);

    for (const map of banMaps) {
        if (!gameSession.bannedMaps.includes(map) && getAvailableMaps().length > 1) {
            gameSession.bannedMaps.push(map);
        }
    }

    const resultText = banMaps.length > 0 ? banMaps.join('、') : '无';
    io.emit(WsEvents.NOTIFICATION, { message: `地图投票结束，已 Ban：${resultText}` });

    gameSession.mapVote = undefined;
    gameSession.currentBanTeam = null;
    gameSession.timerEndAt = null;
    gameSession.timerPhase = null;
    broadcastState();

    if (getAvailableMaps().length === 1) {
        gameSession.selectedMap = getAvailableMaps()[0];
        advancePhase(GamePhase.MapBan, GamePhase.SidePick);
    } else {
        const nextIdx = gameSession.bannedMaps.length;
        if (nextIdx < gameSession.banSequence.length) startMapVote(gameSession.banSequence[nextIdx]);
        else {
            const remaining = getAvailableMaps();
            gameSession.selectedMap = remaining[Math.floor(Math.random() * remaining.length)];
            advancePhase(GamePhase.MapBan, GamePhase.SidePick);
            return;
        }
        broadcastState();
    }
};

const startSideVote = (team: RosterTeam = gameSession.sidePickTeam || 'A') => {
    clearSideVoteTimer();
    const dur = SIDE_PICK_VOTE_SECONDS;
    gameSession.sidePickTeam = team;
    gameSession.selectedSide = null;
    gameSession.sideVote = {
        team,
        votes: {},
        timeoutAt: Date.now() + dur * 1000,
    };
    gameSession.timerEndAt = gameSession.sideVote.timeoutAt;
    gameSession.timerPhase = GamePhase.SidePick;
    sideVoteTimer = setTimeout(() => finishSideVote('timeout'), dur * 1000);
};

const finishSideVote = (reason: 'timeout' | 'admin' | 'manual' = 'timeout') => {
    if (gameSession.phase !== GamePhase.SidePick || !gameSession.sideVote) return;
    clearSideVoteTimer();

    const votes = gameSession.sideVote.votes || {};
    let ct = 0, t = 0;
    for (const side of Object.values(votes)) {
        if (side === 'CT') ct++;
        else if (side === 'T') t++;
    }

    // 无票或平票时随机，避免流程卡住。
    const selectedSide: 'CT' | 'T' = ct > t ? 'CT' : (t > ct ? 'T' : (Math.random() < 0.5 ? 'CT' : 'T'));
    gameSession.selectedSide = selectedSide;
    gameSession.sideVote = undefined;
    gameSession.timerEndAt = null;
    gameSession.timerPhase = null;
    setRosterLiveSides(selectedSide);
    io.emit(WsEvents.NOTIFICATION, { message: `选边投票结束，${gameSession.sidePickTeam || 'A'}队选择 ${selectedSide}` });
    broadcastState();
    advancePhase(GamePhase.SidePick, GamePhase.PreGameSetup);
};

// ========== 角色分配 ==========
const randomRemainingRoles = (onlyTeam?: RosterTeam) => {
    if (!isUndercoverModeEnabled()) {
        const teamsToAssign: RosterTeam[] = onlyTeam ? [onlyTeam] : ['A', 'B'];

        for (const team of teamsToAssign) {
            for (const player of getTeamPlayers(team)) {
                player.gameRole = 'Soldier';
                delete player.taskGrid;
                player.detectiveQuestionCount = 0;
            }
        }

        gameSession.undercoverCount = 0;
        gameSession.detectiveCount = 0;
        gameSession.rolesReleased = false;
        broadcastState();
        return;
    }

    const teamsToAssign: RosterTeam[] = onlyTeam ? [onlyTeam] : ['A', 'B'];
    for (const team of teamsToAssign) {
        const players = getTeamPlayers(team);
        const assignedU = players.filter(p => p.gameRole === 'Undercover').length;
        const assignedD = players.filter(p => p.gameRole === 'Detective').length;
        let needU = Math.max(0, gameSession.undercoverCount - assignedU);
        let needD = Math.max(0, gameSession.detectiveCount - assignedD);
        const unassigned = players.filter(p => !p.gameRole);
        const shuffled = [...unassigned].sort(() => Math.random() - 0.5);
        for (const p of shuffled) {
            if (needU > 0) { p.gameRole = 'Undercover'; needU--; }
            else if (needD > 0) { p.gameRole = 'Detective'; needD--; }
            else { p.gameRole = 'Soldier'; }
        }
    }
    broadcastState();
};

const assignTaskGridToPlayer = (player: Player) => {

    if (!isUndercoverModeEnabled()) return;
    if (!gameSession.taskTemplate) return;
    const grid: Record<string, any> = {};
    for (const [cellId, cell] of Object.entries(gameSession.taskTemplate.cells)) {
        grid[cellId] = {
            ...cell,
            cellId,
            currentCount: 0, status: 'Incomplete', isHintUsed: false, isReplaced: false, borderHistory: []
        };
    }
    player.taskGrid = grid;
};

// ========== CSV 解析与计分 ==========
type CsvValue = string | number;
type CsvRow = Record<string, CsvValue> & { steamid64: string; name: string; kills: number; deaths: number; assists: number; damage: number; team?: string; };
const parseCsvLine = (line: string): string[] => {
    const result: string[] = [];
    let current = '';
    let inQuotes = false;
    for (let i = 0; i < line.length; i++) {
        const ch = line[i];
        const next = line[i + 1];
        if (ch === '"' && inQuotes && next === '"') { current += '"'; i++; continue; }
        if (ch === '"') { inQuotes = !inQuotes; continue; }
        if (ch === ',' && !inQuotes) { result.push(current.trim()); current = ''; continue; }
        current += ch;
    }
    result.push(current.trim());
    return result;
};
const toNumber = (value: unknown): number => {
    const n = Number(String(value ?? '').trim());
    return Number.isFinite(n) ? n : 0;
};
const parseCsv = (csvText: string): CsvRow[] => {
    const lines = csvText.replace(/^\uFEFF/, '').trim().split(/\r?\n/).filter(Boolean);
    if (lines.length < 2) return [];
    const headers = parseCsvLine(lines[0]).map(h => h.trim().toLowerCase());
    const rows: CsvRow[] = [];
    for (let i = 1; i < lines.length; i++) {
        const cols = parseCsvLine(lines[i]);
        const raw: Record<string, CsvValue> = {};
        headers.forEach((h, idx) => { raw[h] = cols[idx] ?? ''; });
        const damage = toNumber(raw.damage || raw.health_points_dealt_total || raw.health_points_removed_total);
        rows.push({
            ...raw,
            steamid64: String(raw.steamid64 || ''),
            name: String(raw.name || ''),
            team: String(raw.team || ''),
            matchid: String(raw.matchid || ''),
            mapnumber: toNumber(raw.mapnumber),
            kills: toNumber(raw.kills),
            deaths: toNumber(raw.deaths),
            assists: toNumber(raw.assists),
            damage,
        });
    }
    return rows;
};
const csvRowToStats = (row: CsvRow) => ({
    matchid: String(row.matchid || ''),
    mapnumber: toNumber(row.mapnumber),
    team: String(row.team || ''),
    kills: toNumber(row.kills),
    deaths: toNumber(row.deaths),
    assists: toNumber(row.assists),
    damage: toNumber(row.damage),
    enemy5ks: toNumber(row.enemy5ks),
    enemy4ks: toNumber(row.enemy4ks),
    enemy3ks: toNumber(row.enemy3ks),
    enemy2ks: toNumber(row.enemy2ks),
    utilityCount: toNumber(row.utility_count),
    utilityDamage: toNumber(row.utility_damage),
    utilitySuccesses: toNumber(row.utility_successes),
    utilityEnemies: toNumber(row.utility_enemies),
    flashCount: toNumber(row.flash_count),
    flashSuccesses: toNumber(row.flash_successes),
    healthPointsRemovedTotal: toNumber(row.health_points_removed_total),
    healthPointsDealtTotal: toNumber(row.health_points_dealt_total),
    shotsFiredTotal: toNumber(row.shots_fired_total),
    shotsOnTargetTotal: toNumber(row.shots_on_target_total),
    v1Count: toNumber(row.v1_count),
    v1Wins: toNumber(row.v1_wins),
    v2Count: toNumber(row.v2_count),
    v2Wins: toNumber(row.v2_wins),
    entryCount: toNumber(row.entry_count),
    entryWins: toNumber(row.entry_wins),
    equipmentValue: toNumber(row.equipment_value),
    moneySaved: toNumber(row.money_saved),
    killReward: toNumber(row.kill_reward),
    liveTime: toNumber(row.live_time),
    headShotKills: toNumber(row.head_shot_kills),
    cashEarned: toNumber(row.cash_earned),
    enemiesFlashed: toNumber(row.enemies_flashed),
    raw: row,
});

const calculateScores = () => {
    const players = getGamePlayers();
    const accusations = gameSession.accusations;
    updateMatchFinishState();

    const live = gameSession.liveGameData;
    const scoreA = live?.scoreA ?? 0;
    const scoreB = live?.scoreB ?? 0;
    const winnerRoster = live?.winnerTeam ?? getMatchWinner(scoreA, scoreB);

    const getRoundRecord = (player: Player): { won: number; lost: number } => {
        if (player.rosterTeam === 'A') return { won: scoreA, lost: scoreB };
        if (player.rosterTeam === 'B') return { won: scoreB, lost: scoreA };
        return { won: 0, lost: 0 };
    };

    const getSideSize = (player: Player): number => {
        if (!player.rosterTeam) return Math.max(1, Math.ceil(players.length / 2));
        const n = players.filter(p => p.rosterTeam === player.rosterTeam).length;
        return Math.max(1, n);
    };

    const isCorrectUndercoverTarget = (targetId: string | null): boolean => {
        if (!targetId) return false;
        const target = findPlayerById(targetId);
        return !!target && target.gameRole === 'Undercover';
    };

    const getAccuseVoteWeight = (accuser: Player, type: 'own' | 'enemy'): number => {
        // 侦探的指认票翻倍：己方 4 票，敌方 2 票。
        if (accuser.gameRole === 'Detective') return type === 'own' ? 4 : 2;
        // 其他角色按普通票权：己方 2 票，敌方 1 票。
        return type === 'own' ? 2 : 1;
    };

    const countCorrectAccuseVotes = (accuser: Player): number => {
        const acc = accusations[accuser.playerId];
        if (!acc) return 0;
        let votes = 0;
        if (isCorrectUndercoverTarget(acc.own)) votes += getAccuseVoteWeight(accuser, 'own');
        if (isCorrectUndercoverTarget(acc.enemy)) votes += getAccuseVoteWeight(accuser, 'enemy');
        // 士兵最多正确获得 3 票指认分：己方 2 + 敌方 1。
        if (accuser.gameRole === 'Soldier') votes = Math.min(votes, 3);
        return votes;
    };

    const countReceivedAccuseVotes = (targetPlayerId: string): number => {
        let votes = 0;
        for (const [accuserId, acc] of Object.entries(accusations)) {
            const accuser = findPlayerById(accuserId);
            if (!accuser || accuser.role === 'Spectator' || accuser.role === 'Admin') continue;
            if (acc.own === targetPlayerId) votes += getAccuseVoteWeight(accuser, 'own');
            if (acc.enemy === targetPlayerId) votes += getAccuseVoteWeight(accuser, 'enemy');
        }
        return votes;
    };

    const countLines = (player: Player): number => {
        if (!player.taskGrid || !gameSession.taskTemplate) return 0;
        const grid = player.taskGrid, lines = gameSession.taskTemplate.lines;
        let completedLines = 0;
        for (const line of lines) {
            if (line.every(cellId => grid[cellId] && (grid[cellId].status === 'Complete' || (grid[cellId].nValue && grid[cellId].nValue! > 0)))) completedLines++;
        }
        return completedLines;
    };

    for (const player of players) {
        const stats = player.stats;
        const role = player.gameRole;
        const breakdown: Record<string, number> = {};
        if (!stats || !role) {
            player.finalScore = undefined;
            player.scoreBreakdown = breakdown;
            continue;
        }

        const { kills, deaths, assists, damage } = stats;
        const roundRecord = getRoundRecord(player);
        const didWin = !!winnerRoster && player.rosterTeam === winnerRoster;
        const gameResultScore = winnerRoster ? (didWin ? 1 : -1) : 0;
        let score = 0;

        if (!isUndercoverModeEnabled()) {
            breakdown['击杀'] = kills * 5;
            breakdown['死亡'] = deaths * -2;
            breakdown['助攻'] = assists * 2;
            breakdown['游戏胜负'] = gameResultScore > 0 ? 30 : (gameResultScore < 0 ? -10 : 0);
            breakdown['回合胜负'] = roundRecord.won * 10 + roundRecord.lost * -4;
            breakdown['伤害'] = Math.floor(damage / 100) * 1;

            score =
                breakdown['击杀'] +
                breakdown['死亡'] +
                breakdown['助攻'] +
                breakdown['游戏胜负'] +
                breakdown['回合胜负'] +
                breakdown['伤害'];

            player.finalScore = Math.round(score * 100) / 100;
            player.scoreBreakdown = breakdown;
            continue;
        }

        if (role === 'Soldier') {
            breakdown['击杀'] = kills * 5;
            breakdown['死亡'] = deaths * -2;
            breakdown['助攻'] = assists * 2;
            breakdown['游戏胜负'] = gameResultScore > 0 ? 30 : (gameResultScore < 0 ? -10 : 0);
            breakdown['回合胜负'] = roundRecord.won * 10 + roundRecord.lost * -4;
            breakdown['伤害'] = Math.floor(damage / 100) * 1;

            const correctVotes = countCorrectAccuseVotes(player);
            breakdown['指认成功票数'] = correctVotes;
            breakdown['指认成功'] = correctVotes * 15;

            score = breakdown['击杀'] + breakdown['死亡'] + breakdown['助攻'] + breakdown['游戏胜负'] + breakdown['回合胜负'] + breakdown['伤害'] + breakdown['指认成功'];
        } else if (role === 'Undercover') {
            breakdown['击杀'] = kills * -2;
            breakdown['死亡'] = deaths * 5;
            breakdown['助攻'] = assists * -1;
            breakdown['游戏胜负'] = gameResultScore > 0 ? -10 : (gameResultScore < 0 ? 40 : 0);
            breakdown['回合胜负'] = roundRecord.won * -4 + roundRecord.lost * 10;
            breakdown['伤害'] = Math.floor(damage / 100) * -0.75;

            const receivedVotes = countReceivedAccuseVotes(player.playerId);
            breakdown['被指认票数'] = receivedVotes;
            breakdown['被指认'] = receivedVotes * -5;

            const exposeThreshold = getSideSize(player) * 2;
            breakdown['暴露惩罚'] = receivedVotes >= exposeThreshold ? -50 : 0;

            let taskCellScore = 0, lineCount = 0;
            if (player.taskGrid) {
                for (const cell of Object.values(player.taskGrid)) {
                    if (cell.status === 'Complete') {
                        taskCellScore += cell.level * 5;
                    } else if (cell.nValue && cell.nValue > 0) {
                        taskCellScore += cell.baseLevel ? cell.baseLevel + (cell.nValue - 1) * (cell.extraLevel || 0) : cell.level * 5;
                    }
                }
                lineCount = countLines(player);
            }
            breakdown['任务等级'] = taskCellScore;
            breakdown['连线数'] = lineCount;
            breakdown['连线'] = lineCount * 14;
            breakdown['任务'] = taskCellScore + breakdown['连线'];

            score = breakdown['击杀'] + breakdown['死亡'] + breakdown['助攻'] + breakdown['游戏胜负'] + breakdown['回合胜负'] + breakdown['伤害'] + breakdown['被指认'] + breakdown['暴露惩罚'] + breakdown['任务'];
        } else if (role === 'Detective') {
            breakdown['击杀'] = kills * 4;
            breakdown['死亡'] = deaths * -2;
            breakdown['助攻'] = assists * 2;
            breakdown['游戏胜负'] = gameResultScore > 0 ? 30 : 0;
            breakdown['回合胜负'] = roundRecord.won * 8 + roundRecord.lost * -4;
            breakdown['伤害'] = Math.floor(damage / 100) * 0.9;
            breakdown['问答问题数'] = Math.max(0, Math.min(2, Number(player.detectiveQuestionCount || 0)));
            breakdown['问答惩罚'] = breakdown['问答问题数'] >= 2 ? -12 : 0;

            const correctVotes = countCorrectAccuseVotes(player);
            breakdown['指认成功票数'] = correctVotes;
            breakdown['指认成功'] = correctVotes * 20;

            score = breakdown['击杀'] + breakdown['死亡'] + breakdown['助攻'] + breakdown['游戏胜负'] + breakdown['回合胜负'] + breakdown['伤害'] + breakdown['问答惩罚'] + breakdown['指认成功'];
        }

        player.finalScore = Math.round(score * 100) / 100;
        player.scoreBreakdown = breakdown;
    }
};

app.post('/api/upload-csv', upload.single('csvfile'), (req, res) => {
    if (gameSession.phase !== GamePhase.Scoreboard) return res.status(400).json({ error: '只能在结算阶段上传CSV' });
    if (!req.file) return res.status(400).json({ error: '没有上传文件' });
    try {
        const csvText = req.file.buffer.toString('utf-8');
        const rows = parseCsv(csvText);
        let matchedCount = 0;
        for (const player of getGamePlayers()) {
            let csvRow: CsvRow | undefined;
            if (player.steamId) csvRow = rows.find(r => normalizeSteamId(r.steamid64) === normalizeSteamId(player.steamId));
            if (!csvRow) csvRow = rows.find(r => String(r.name) === player.name);
            if (csvRow) { player.stats = csvRowToStats(csvRow); matchedCount++; }
        }
        calculateScores(); broadcastState();

        let txtReport = "=== 草人杯 卧底任务战报 ===\n\n";
        for (const p of getGamePlayers()) {
            if (p.gameRole === 'Undercover' && p.taskGrid) {
                txtReport += `[卧底] ${p.name} 的任务完成情况:\n`;
                for (const cell of Object.values(p.taskGrid)) {
                    txtReport += `  - ${cell.cellId}: ${cell.description} | 状态: ${cell.status} | N值: ${cell.nValue || 0}\n`;
                }
                txtReport += `  最终总分: ${p.finalScore}\n\n`;
            }
        }

        res.json({ success: true, matchedPlayers: matchedCount, report: txtReport });
    } catch (e) { res.status(500).json({ error: 'CSV 解析失败' }); }
});

// ========== CS2 插件 REST API ==========
app.get('/api/plugin/state', requirePluginAuth, (req, res) => {
    res.json({
        success: true,
        sessionId: gameSession.sessionId,
        matchId: gameSession.matchId,
        phase: gameSession.phase,
        selectedMap: gameSession.selectedMap,
        selectedSide: gameSession.selectedSide,
        liveGameData: gameSession.liveGameData,
        players: getGamePlayers().map(p => ({
            playerId: p.playerId,
            name: p.name,
            steamId: p.steamId,
            rosterTeam: p.rosterTeam,
            team: p.team,
            gameRole: p.gameRole,
            isReady: p.isReady,
        })),
    });
});

app.post('/api/plugin/heartbeat', requirePluginAuth, (req, res) => {
    if (!gameSession.liveGameData) gameSession.liveGameData = createEmptyLiveGameData();
    gameSession.liveGameData.pluginConnected = true;
    gameSession.liveGameData.lastPluginHeartbeatAt = Date.now();
    if (req.body?.mapName) gameSession.liveGameData.mapName = String(req.body.mapName);
    broadcastState();
    res.json({
        success: true,
        matchId: gameSession.matchId,
        phase: gameSession.phase,
        commands: takeQueuedPluginCommands(),
    });
});

app.post('/api/plugin/command-ack', requirePluginAuth, (req, res) => {
    const ok = ackPluginCommand(req.body?.commandId);
    if (!ok) {
        return res.status(404).json({ success: false, error: '未找到待确认命令或命令已过期' });
    }
    res.json({ success: true });
});

app.post('/api/plugin/bind', requirePluginAuth, (req, res) => {
    const bindCode = String(req.body?.bindCode || '').trim();
    const steamId = normalizeSteamId(req.body?.steamId);
    const inGameName = String(req.body?.name || '').trim();
    if (!bindCode || !steamId) return res.status(400).json({ success: false, error: 'bindCode 和 steamId 必填' });
    const player = getGamePlayers().find(p => p.bindCode === bindCode);
    if (!player) return res.status(404).json({ success: false, error: '绑定码无效或已过期' });
    player.steamId = steamId;
    if (inGameName && !player.name) player.name = inGameName;
    broadcastState();
    res.json({ success: true, playerId: player.playerId, name: player.name, steamId: player.steamId });
});

app.post('/api/plugin/snapshot', requirePluginAuth, (req, res) => {
    if (!isPluginLivePhase()) return res.json({ success: true, ignored: true, reason: `当前阶段 ${gameSession.phase} 不接收实时战绩` });
    if (req.body?.matchId && req.body.matchId !== gameSession.matchId) return res.status(409).json({ success: false, error: 'matchId 不匹配' });
    if (!gameSession.liveGameData) gameSession.liveGameData = createEmptyLiveGameData();
    // 快照只同步地图、回合号、玩家实时战绩；比分只在 round_end 事件里累加。
    // 这样可避免插件本地计数、机器人测试或热重载把网页比分覆盖回旧值。
    if (typeof req.body?.currentRound === 'number') gameSession.liveGameData.currentRound = Math.max(gameSession.liveGameData.currentRound || 0, normalizePluginRound(req.body.currentRound));
    if (req.body?.mapName) gameSession.liveGameData.mapName = String(req.body.mapName);
    gameSession.liveGameData.pluginConnected = true;
    gameSession.liveGameData.lastPluginHeartbeatAt = Date.now();
    updateLivePlayersFromSnapshot(req.body?.players || []);
    updateMatchFinishState();
    broadcastState();
    res.json({ success: true, matchedPlayers: getGamePlayers().filter(p => p.steamId && p.stats).length });
});

app.post('/api/plugin/event', requirePluginAuth, (req, res) => {
    if (!isPluginLivePhase()) return res.json({ success: true, ignored: true, reason: `当前阶段 ${gameSession.phase} 不接收实时事件` });
    if (req.body?.matchId && req.body.matchId !== gameSession.matchId) return res.status(409).json({ success: false, error: 'matchId 不匹配' });
    if (!gameSession.liveGameData) gameSession.liveGameData = createEmptyLiveGameData();
    gameSession.liveGameData.pluginConnected = true;
    gameSession.liveGameData.lastPluginHeartbeatAt = Date.now();

    const type = String(req.body?.type || '');
    const payload = req.body?.payload || {};
    if (typeof payload.round === 'number') gameSession.liveGameData.currentRound = Math.max(gameSession.liveGameData.currentRound || 0, normalizePluginRound(payload.round));
    if (payload.mapName) gameSession.liveGameData.mapName = String(payload.mapName);
    if (payload.players) updateLivePlayersFromSnapshot(payload.players || []);

    switch (type) {
        case 'round_start':
            break;
        case 'player_death':
            applyPluginKillEvent(payload);
            break;
        case 'player_hurt':
            applyPluginDamageEvent(payload);
            break;
        case 'round_end':
            applyPluginRoundEndEvent(payload);
            break;
        default:
            return res.status(400).json({ success: false, error: `未知插件事件: ${type}` });
    }
    broadcastState();
    res.json({ success: true });
});

// ========== 阶段推进辅助 ==========
const performPhaseTransition = (to: GamePhase) => {
    if (to === GamePhase.CaptainSelection) {
        const gamePlayers = getGamePlayers();
        if (gamePlayers.length >= 2) { randomizeCaptain('A'); randomizeCaptain('B'); }
        else if (gamePlayers.length === 1) { gameSession.captains.A = gamePlayers[0].playerId; gameSession.captains.B = null; }
        else { gameSession.captains.A = null; gameSession.captains.B = null; }
    } else if (to === GamePhase.Roll) {
        gameSession.rollValues = { A: null, B: null };
    } else if (to === GamePhase.PlayerDraft) {
        const totalGamePlayers = getGamePlayers().length;
        const totalPicks = Math.max(0, totalGamePlayers - 2);
        gameSession.draftOrder = [];

        // 修正逻辑：根据 Roll 点结果决定先手顺序
        const rollA = gameSession.rollValues.A || 0;
        const rollB = gameSession.rollValues.B || 0;
        const firstTeam: RosterTeam = rollB > rollA ? 'B' : 'A';
        const secondTeam: RosterTeam = firstTeam === 'A' ? 'B' : 'A';

        // 生成蛇形选人序列
        const pickSequence: RosterTeam[] = [firstTeam, secondTeam, secondTeam, firstTeam];
        for (let i = 0; i < totalPicks; i++) gameSession.draftOrder.push(pickSequence[i % 4]);
        gameSession.draftIndex = 0;

        gameSession.teams.A.players = [gameSession.captains.A!].filter(Boolean);
        gameSession.teams.B.players = [gameSession.captains.B!].filter(Boolean);
        const pA = findPlayerById(gameSession.captains.A!); if (pA) { pA.rosterTeam = 'A'; pA.team = undefined; }
        const pB = findPlayerById(gameSession.captains.B!); if (pB) { pB.rosterTeam = 'B'; pB.team = undefined; }

        // 生成 Ban 图序列，同样由先手队伍开始
        const baseOrder: RosterTeam[] = [firstTeam, secondTeam];
        const banSequence: RosterTeam[] = [];
        while (banSequence.length < gameSession.mapPool.length - 1) {
            banSequence.push(...baseOrder);
        }
        banSequence.length = gameSession.mapPool.length - 1;
        gameSession.banSequence = banSequence;
        startDraftPickTimer(false);
    } else if (to === GamePhase.MapBan) {
        gameSession.bannedMaps = [];
        gameSession.selectedMap = null;
        startMapVote(gameSession.banSequence[0]);
    } else if (to === GamePhase.SidePick) {
        startSideVote(gameSession.sidePickTeam || 'A');
    } else if (to === GamePhase.PreGameSetup) {
        if (gameSession.selectedSide) setRosterLiveSides(gameSession.selectedSide);

        if (isUndercoverModeEnabled()) {
            gameSession.rolesReleased = false;
            Object.values(gameSession.players).forEach(p => p.gameRole = undefined);
        } else {
            gameSession.undercoverCount = 0;
            gameSession.detectiveCount = 0;
            gameSession.rolesReleased = true;
            gameSession.questionsUsed = 0;
            gameSession.currentQuestion = null;
            gameSession.questionAnswer = null;
            gameSession.secondQuestionAnswered = false;
            gameSession.accusations = {};
            Object.values(gameSession.players).forEach(p => {
                if (p.role !== 'Spectator' && p.role !== 'Admin') p.gameRole = 'Soldier';
                delete p.taskGrid;
                p.detectiveQuestionCount = 0;
                p.abandonCount = 0;
                p.replaceCount = 0;
                p.hintUsedCount = 0;
            });
        }
    } else if (to === GamePhase.LiveGame) {
        gameSession.matchId = uuidv4();
        gameSession.rolesReleased = true;

        if (isUndercoverModeEnabled()) {
            const unassigned = getGamePlayers().filter(p => !p.gameRole);
            if (unassigned.length > 0) randomRemainingRoles();
        } else {
            clearUndercoverModeState();
            gameSession.rolesReleased = true;
        }

        Object.values(gameSession.players).forEach(p => {
            p.stats = { kills: 0, deaths: 0, assists: 0, damage: 0 };
            p.finalScore = undefined;
            p.scoreBreakdown = undefined;
            p.detectiveQuestionCount = isUndercoverModeEnabled() && p.gameRole === 'Detective' ? 0 : undefined;
            if (isUndercoverModeEnabled() && p.gameRole === 'Undercover') assignTaskGridToPlayer(p);
            if (!isUndercoverModeEnabled()) delete p.taskGrid;
        });
        gameSession.liveGameData = createEmptyLiveGameData();
    } else if (to === GamePhase.MidGameQA) {
        // Stage 3.13：中场问答改为游戏内语音进行，网页不再停留该流程。
        advancePhase(GamePhase.MidGameQA, GamePhase.PostGameAccusation);
        return;
    } else if (to === GamePhase.PostGameAccusation) {
        if (!isUndercoverModeEnabled()) {
            calculateScores();
            gameSession.phase = GamePhase.Scoreboard;
            gameSession.timerEndAt = null;
            gameSession.timerPhase = null;
            broadcastState();
            return;
        }

        const newAccusations: Record<string, { own: string | null; enemy: string | null }> = {};
        for (const p of getGamePlayers()) newAccusations[p.playerId] = { own: null, enemy: null };
        gameSession.accusations = newAccusations;
    } else if (to === GamePhase.Scoreboard) {
        calculateScores();
    }

    let timerEnd: number | null = null;
    let timerPhase: GamePhase | null = null;

    if (to === GamePhase.PlayerDraft && gameSession.draftPickTimeoutAt) {
        timerEnd = gameSession.draftPickTimeoutAt;
        timerPhase = GamePhase.PlayerDraft;
    } else if (to === GamePhase.MapBan && gameSession.mapVote?.timeoutAt) {
        timerEnd = gameSession.mapVote.timeoutAt;
        timerPhase = GamePhase.MapBan;
    } else if (to === GamePhase.SidePick && gameSession.sideVote?.timeoutAt) {
        timerEnd = gameSession.sideVote.timeoutAt;
        timerPhase = GamePhase.SidePick;
    }

    gameSession.timerEndAt = timerEnd;
    gameSession.timerPhase = timerPhase;

    broadcastState();
};

// ========== 阶段推进核心 ==========

const advancePhase = (from: GamePhase, to: GamePhase, triggeredBy?: string) => {
    to = resolveNextPhaseByMatchOptions(from, to);

    if (gameSession.phase !== from) return;
    if (!canTransition(from, to)) return;

    if (from === GamePhase.Roll) {
        if (gameSession.rollValues.A === null) gameSession.rollValues.A = Math.floor(Math.random() * 100) + 1;
        if (gameSession.rollValues.B === null) gameSession.rollValues.B = Math.floor(Math.random() * 100) + 1;
        // 去掉了之前替换队长的错误逻辑，先手判断在 performPhaseTransition 中处理。
        broadcastState();
        if (gameSession.rollTimeout) clearTimeout(gameSession.rollTimeout);
        gameSession.rollTimeout = setTimeout(() => {
            gameSession.phase = GamePhase.PlayerDraft;
            performPhaseTransition(GamePhase.PlayerDraft);
        }, 3000);
        return;
    }

    if (from === GamePhase.PlayerDraft) {
        clearDraftPickTimer();
        gameSession.draftPickTimeoutAt = null;
        while (gameSession.draftIndex < gameSession.draftOrder.length) {
            const currentTeam = gameSession.draftOrder[gameSession.draftIndex];
            const available = getGamePlayers().filter(p => !p.rosterTeam && p.playerId !== gameSession.captains.A && p.playerId !== gameSession.captains.B);
            if (available.length === 0) break;
            const picked = available[Math.floor(Math.random() * available.length)];
            if (currentTeam === 'A') { gameSession.teams.A.players.push(picked.playerId); picked.rosterTeam = 'A'; }
            else { gameSession.teams.B.players.push(picked.playerId); picked.rosterTeam = 'B'; }
            gameSession.draftIndex++;
        }
        broadcastState();
    }
    if (from === GamePhase.MapBan) {
        clearMapVoteTimer();
        if (gameSession.mapVote) {
            gameSession.mapVote = undefined;
            gameSession.currentBanTeam = null;
            gameSession.timerEndAt = null;
            gameSession.timerPhase = null;
        }
        if (!gameSession.selectedMap) {
            const remaining = getAvailableMaps();
            if (remaining.length > 0) {
                gameSession.selectedMap = remaining[Math.floor(Math.random() * remaining.length)];
            }
        }
    }
    if (from === GamePhase.SidePick) {
        clearSideVoteTimer();
        gameSession.sideVote = undefined;
    }
    if (from === GamePhase.MidGameQA) { gameSession.currentQuestion = null; gameSession.questionAnswer = null; gameSession.questionsUsed = 0; broadcastState(); }

    gameSession.phase = to;
    performPhaseTransition(to);
};

// ========== Socket 事件 ==========
io.on('connection', (socket) => {
    console.log(`客户端连接: ${socket.id}`);
    socket.data.playerId = null;

    socket.on(WsEvents.LOGIN, (data: { name: string; extraParam?: string }) => {
        const name = normalizeLoginText(data.name);
        const extraParam = normalizeLoginText(data.extraParam);

        // 现在不再使用会话码。普通玩家可以用“原昵称”或“绑定码”回到原身份。
        // 管理员为了避免被人只靠昵称冒充，必须输入管理员密码，或使用自己的绑定码恢复。
        const existingByBind = findPlayerByBindCode(extraParam);
        const existingByName = findPlayerByName(name);
        const existing = existingByBind || existingByName;

        if (existing) {
            if (existing.role === 'Admin' && extraParam !== ADMIN_PASSWORD && extraParam !== existing.bindCode) {
                socket.emit(WsEvents.LOGIN_RESPONSE, { success: false, message: '管理员恢复身份需要输入管理员密码，或输入该管理员账号的绑定码' });
                return;
            }

            socket.data.playerId = existing.playerId;
            socket.join(existing.playerId);
            socket.emit(WsEvents.LOGIN_RESPONSE, {
                success: true,
                playerId: existing.playerId,
                bindCode: existing.bindCode,
                message: `欢迎，${existing.name}！已恢复你的房间身份。`,
            });
            sendPrivateData(socket.id, existing.playerId);
            broadcastState();
            return;
        }

        if (!name) {
            socket.emit(WsEvents.LOGIN_RESPONSE, { success: false, message: '请输入昵称；如果要恢复身份，也可以输入原昵称或绑定码' });
            return;
        }

        let role: PlayerRole = 'Player';
        if (extraParam === 'spec') role = 'Spectator';
        else if (extraParam === ADMIN_PASSWORD) role = 'Admin';
        if (gameSession.phase !== GamePhase.Lobby && role !== 'Admin') role = 'Spectator';

        const playerId = uuidv4(), bindCode = generateBindCode();
        const newPlayer: Player = { playerId, name, role, bindCode, isReady: false };
        gameSession.players[playerId] = newPlayer;
        gameSession.playerOrder.push(playerId);
        socket.data.playerId = playerId;
        socket.join(playerId);
        socket.emit(WsEvents.LOGIN_RESPONSE, { success: true, playerId, bindCode, message: role === 'Spectator' && gameSession.phase !== GamePhase.Lobby ? `欢迎，${name}！当前对局已经开始，你已作为旁观者加入。你的绑定码是: ${bindCode}` : `欢迎，${name}！你的绑定码是: ${bindCode}` });
        sendPrivateData(socket.id, playerId);
        broadcastState();
    });

    // 管理员操作
    socket.on(WsEvents.ADMIN_ACTION, (data: { playerId: string; action: string; payload?: any }) => {
        const admin = findPlayerById(data.playerId);
        if (!admin || admin.role !== 'Admin') { socket.emit(WsEvents.NOTIFICATION, { message: '只有管理员才能执行此操作' }); return; }

        if (data.action === 'ADVANCE_PHASE') {
            const current = gameSession.phase;
            let nextPhase: GamePhase | null = null;

            switch (current) {
                case GamePhase.Lobby:
                    nextPhase = GamePhase.CaptainSelection;
                    break;

                case GamePhase.CaptainSelection:
                    nextPhase = GamePhase.Roll;
                    break;

                case GamePhase.Roll:
                    nextPhase = GamePhase.PlayerDraft;
                    break;

                case GamePhase.PlayerDraft:
                    nextPhase = GamePhase.MapBan;
                    break;

                case GamePhase.MapBan:
                    nextPhase = GamePhase.SidePick;
                    break;

                case GamePhase.SidePick:
                    nextPhase = GamePhase.PreGameSetup;
                    break;

                case GamePhase.PreGameSetup:
                    nextPhase = GamePhase.LiveGame;
                    break;

                case GamePhase.LiveGame:
                    // 关键修改：
                    // 卧底模式开启：比赛结束后进入赛后指认。
                    // 卧底模式关闭：比赛结束后直接进入积分结算。
                    nextPhase = isUndercoverModeEnabled()
                        ? GamePhase.PostGameAccusation
                        : GamePhase.Scoreboard;
                    break;

                case GamePhase.MidGameQA:
                    // 卧底模式关闭时，理论上不应该进入 MidGameQA。
                    // 如果因为旧状态已经进来了，直接去积分结算。
                    nextPhase = isUndercoverModeEnabled()
                        ? GamePhase.PostGameAccusation
                        : GamePhase.Scoreboard;
                    break;

                case GamePhase.PostGameAccusation:
                    nextPhase = GamePhase.Scoreboard;
                    break;

                case GamePhase.Scoreboard:
                    resetCurrentGame('管理员开启新一轮');
                    return;

                default:
                    break;
            }

            if (!nextPhase) {
                socket.emit(WsEvents.NOTIFICATION, { message: '当前阶段无法继续推进。' });
                return;
            }

            nextPhase = resolveNextPhaseByMatchOptions(current, nextPhase);

            // 进入正式比赛前的检查。
            if (current === GamePhase.PreGameSetup && nextPhase === GamePhase.LiveGame) {
                if (isUndercoverModeEnabled()) {
                    const unassigned = getGamePlayers().filter(p => !p.gameRole);

                    if (unassigned.length > 0) {
                        socket.emit(WsEvents.NOTIFICATION, {
                            message: `还有 ${unassigned.length} 名玩家未分配身份，请先由管理员分配/随机补齐。`,
                        });
                        return;
                    }

                    if (!gameSession.rolesReleased) {
                        socket.emit(WsEvents.NOTIFICATION, {
                            message: '身份尚未发放给玩家。请先点击“发放身份给玩家”，再进入正式对局。',
                        });
                        return;
                    }
                } else {
                    // 卧底模式关闭时，不要求卧底/侦探身份，也不要求发布身份。
                    // 所有人都按普通 Soldier 处理。
                    gameSession.undercoverCount = 0;
                    gameSession.detectiveCount = 0;
                    gameSession.rolesReleased = true;
                    gameSession.questionsUsed = 0;
                    gameSession.currentQuestion = null;
                    gameSession.questionAnswer = null;
                    gameSession.secondQuestionAnswered = false;
                    gameSession.accusations = {};

                    for (const player of getGamePlayers()) {
                        player.gameRole = 'Soldier';
                        delete player.taskGrid;
                        player.abandonCount = 0;
                        player.replaceCount = 0;
                        player.hintUsedCount = 0;
                        player.detectiveQuestionCount = 0;
                    }
                }
            }

            // 卧底模式关闭时，禁止进入侦探问答和赛后指认。
            if (
                !isUndercoverModeEnabled() &&
                (nextPhase === GamePhase.MidGameQA || nextPhase === GamePhase.PostGameAccusation)
            ) {
                nextPhase = GamePhase.Scoreboard;
            }

            // 进入积分结算前，先计算分数。
            if (nextPhase === GamePhase.Scoreboard) {
                try {
                    calculateScores();
                } catch (err) {
                    console.error('[ADVANCE_PHASE] calculateScores failed:', err);
                }
            }

            advancePhase(current, nextPhase, admin.name);
            return;
        }



        else if (data.action === 'TERMINATE_GAME') {
            terminateCurrentGameAndKickAll('管理员强制终止本局游戏');
        } else if (data.action === 'FORCE_READY') {
            if (gameSession.phase === GamePhase.PreGameSetup) { Object.values(gameSession.players).forEach(p => p.isReady = true); broadcastState(); }
        } else if (data.action === 'RERANDOM_CAPTAIN') {
            if (gameSession.phase !== GamePhase.CaptainSelection) return;
            const target = data.payload?.team;
            if (target === 'A' || target === 'B') { randomizeCaptain(target); broadcastState(); }
        } else if (data.action === 'SET_CAPTAIN') {
            if (gameSession.phase !== GamePhase.CaptainSelection) return;
            const { team, playerId: newId } = data.payload || {};
            const targetPlayer = findPlayerById(newId);
            if ((team === 'A' || team === 'B') && targetPlayer && targetPlayer.role !== 'Admin') {
                gameSession.captains[team as 'A' | 'B'] = newId;
                broadcastState();
            }
        } else if (data.action === 'ADMIN_BAN_MAP') {
            if (gameSession.phase === GamePhase.MapBan && gameSession.mapVote) {
                const map = data.payload?.map;
                if (getAvailableMaps().includes(map)) {
                    clearMapVoteTimer();
                    gameSession.bannedMaps.push(map); gameSession.mapVote = undefined; gameSession.currentBanTeam = null;
                    gameSession.timerEndAt = null; gameSession.timerPhase = null;
                    broadcastState();
                    if (getAvailableMaps().length === 1) { gameSession.selectedMap = getAvailableMaps()[0]; advancePhase(GamePhase.MapBan, GamePhase.SidePick); }
                    else { const nextIdx = gameSession.bannedMaps.length; if (nextIdx < gameSession.banSequence.length) startMapVote(gameSession.banSequence[nextIdx]); broadcastState(); }
                }
            }
        } else if (data.action === 'SET_ROLES_COUNT') {
            if (!isUndercoverModeEnabled()) {
                socket.emit(WsEvents.NOTIFICATION, { message: '卧底模式已关闭，本局不需要设置卧底/侦探数量。' });
                return;
            }
            if (gameSession.phase === GamePhase.Lobby) {
                const u = data.payload?.undercoverCount, d = data.payload?.detectiveCount;
                if (typeof u === 'number' && u >= 0) gameSession.undercoverCount = u;
                if (typeof d === 'number' && d >= 0) gameSession.detectiveCount = d;
                broadcastState();
            }
        } else if (data.action === 'SET_PLAYER_ROLE') {
            if (!isUndercoverModeEnabled()) return;
            if (gameSession.phase === GamePhase.PreGameSetup) {
                const { playerId: targetId, gameRole } = data.payload || {};
                const player = findPlayerById(targetId);
                if (player && ['Undercover', 'Detective', 'Soldier'].includes(gameRole)) { player.gameRole = gameRole; broadcastState(); }
            }
        } else if (data.action === 'RANDOM_REMAINING_ROLES') {
            if (!isUndercoverModeEnabled()) return;
            if (gameSession.phase === GamePhase.PreGameSetup) randomRemainingRoles();
        } else if (data.action === 'RELEASE_ROLES') {
            if (!isUndercoverModeEnabled()) return;
            if (gameSession.phase !== GamePhase.PreGameSetup) return;
            const unassigned = getGamePlayers().filter(p => !p.gameRole);
            if (unassigned.length > 0) {
                socket.emit(WsEvents.NOTIFICATION, { message: `还有 ${unassigned.length} 名玩家未分配身份，不能发放。` });
                return;
            }
            gameSession.rolesReleased = true;
            io.emit(WsEvents.NOTIFICATION, { message: '管理员已发放身份。玩家现在只能看到自己的身份。' });
            broadcastState();
        } else if (data.action === 'ASSIGN_ROSTER_TEAM') {
            if (gameSession.phase !== GamePhase.PlayerDraft) return;
            const targetId = String(data.payload?.playerId || '');
            const team = data.payload?.team as RosterTeam;
            if (team !== 'A' && team !== 'B') return;
            if (!assignPlayerToRoster(targetId, team)) {
                socket.emit(WsEvents.NOTIFICATION, { message: '无法分配该玩家：队长、管理员或旁观者不能在这里直接改队。' });
                return;
            }
            io.emit(WsEvents.NOTIFICATION, { message: `管理员已将 ${findPlayerById(targetId)?.name || '玩家'} 分入 ${team} 队。` });
            if (isDraftComplete()) {
                gameSession.draftPickTimeoutAt = null;
                gameSession.timerEndAt = null;
                gameSession.timerPhase = null;
                broadcastState();
                scheduleDraftToMapBan();
            } else {
                broadcastState();
            }
        } else if (data.action === 'KICK_PLAYER') {
            const targetId = String(data.payload?.playerId || '');
            const target = findPlayerById(targetId);
            if (!target || target.role === 'Admin') return;
            removePlayerFromRosterTeams(targetId);
            if (gameSession.captains.A === targetId) gameSession.captains.A = null;
            if (gameSession.captains.B === targetId) gameSession.captains.B = null;
            delete gameSession.accusations[targetId];
            delete gameSession.players[targetId];
            gameSession.playerOrder = gameSession.playerOrder.filter(id => id !== targetId);
            io.to(targetId).emit(WsEvents.LOGIN_RESPONSE, { success: false, resetClient: true, message: '你已被管理员移出房间。' });
            io.emit(WsEvents.NOTIFICATION, { message: `管理员已踢出玩家：${target.name}` });
            broadcastState();
        } else if (data.action === 'RESET_FORMAL_MATCH_COUNTERS') {
            if (gameSession.phase !== GamePhase.LiveGame) return;
            resetFormalMatchCounters();
            io.emit(WsEvents.NOTIFICATION, { message: '管理员已将当前插件回合视为正式第 1 回合，并重置比分与战绩。' });
            broadcastState();
        } else if (data.action === 'UPDATE_LIVE_DATA') {
            if (![GamePhase.LiveGame, GamePhase.PostGameAccusation, GamePhase.Scoreboard].includes(gameSession.phase)) return;
            if (!gameSession.liveGameData) gameSession.liveGameData = createEmptyLiveGameData();
            const { scoreA, scoreB, scoreCT, scoreT, round } = data.payload || {};
            if (typeof scoreA === 'number') gameSession.liveGameData.scoreA = scoreA;
            if (typeof scoreB === 'number') gameSession.liveGameData.scoreB = scoreB;
            if (typeof scoreCT === 'number') gameSession.liveGameData.scoreCT = scoreCT;
            if (typeof scoreT === 'number') gameSession.liveGameData.scoreT = scoreT;
            if (typeof round === 'number') gameSession.liveGameData.currentRound = round;
            updateMatchFinishState();
            if (gameSession.phase === GamePhase.Scoreboard) calculateScores();
            broadcastState();
        } else if (data.action === 'SET_DETECTIVE_QUESTION_COUNT') {
            if (!isUndercoverModeEnabled()) return;
            if (![GamePhase.LiveGame, GamePhase.PostGameAccusation, GamePhase.Scoreboard].includes(gameSession.phase)) return;
            const target = findPlayerById(data.payload?.playerId);
            const count = Math.max(0, Math.min(2, Math.floor(Number(data.payload?.count ?? 0))));
            if (!target || target.gameRole !== 'Detective') return;
            target.detectiveQuestionCount = count;
            if (gameSession.phase === GamePhase.Scoreboard) calculateScores();
            broadcastState();
        } else if (data.action === 'UPDATE_TASK_TEMPLATE') {
            if (data.payload && data.payload.taskTemplate) {
                gameSession.taskTemplate = data.payload.taskTemplate;
                broadcastState();
                socket.emit(WsEvents.NOTIFICATION, { message: '任务模板已更新！' });
            }
        }
    });

    // 队长Roll
    socket.on('ROLL', (data: { playerId: string; value: number }) => {
        const player = findPlayerById(data.playerId);
        if (!player || gameSession.phase !== GamePhase.Roll) return;
        const isCapA = gameSession.captains.A === data.playerId, isCapB = gameSession.captains.B === data.playerId;
        if (!isCapA && !isCapB) return;
        if (isCapA && gameSession.rollValues.A !== null) return;
        if (isCapB && gameSession.rollValues.B !== null) return;
        if (isCapA) gameSession.rollValues.A = data.value;
        if (isCapB) gameSession.rollValues.B = data.value;
        broadcastState();
        if (gameSession.rollValues.A !== null && gameSession.rollValues.B !== null) {
            if (gameSession.rollTimeout) clearTimeout(gameSession.rollTimeout);
            gameSession.rollTimeout = setTimeout(() => {
                gameSession.phase = GamePhase.PlayerDraft;
                performPhaseTransition(GamePhase.PlayerDraft);
            }, 3000);
        }
    });

    // 队长选人：加入真实倒计时。队长未在倒计时内选人时，系统自动随机选一名可选玩家。
    socket.on(WsEvents.DRAFT_PICK, (data: { playerId: string; pickedId: string }) => {
        const drafter = findPlayerById(data.playerId);
        if (!drafter || gameSession.phase !== GamePhase.PlayerDraft) return;
        if (gameSession.draftIndex >= gameSession.draftOrder.length) return;
        const currentTeam = gameSession.draftOrder[gameSession.draftIndex];
        const isCapA = gameSession.captains.A === data.playerId && currentTeam === 'A';
        const isCapB = gameSession.captains.B === data.playerId && currentTeam === 'B';
        if (!isCapA && !isCapB) return;

        const picked = applyDraftPick(data.pickedId, 'manual');
        if (!picked) {
            broadcastState();
            return;
        }

        if (isDraftComplete()) {
            clearDraftPickTimer();
            gameSession.draftPickTimeoutAt = null;
            gameSession.timerEndAt = null;
            gameSession.timerPhase = null;
            broadcastState();
            scheduleDraftToMapBan();
        } else if (gameSession.draftOrder[gameSession.draftIndex] === currentTeam) {
            // 仍在同一批次（例如 B 队一段倒计时内需要选 2 人）：不重置倒计时，只刷新界面。
            broadcastState();
        } else {
            startDraftPickTimer(true);
        }
    });

    // 地图投票：官匹风格，点击即记录，倒计时结束后统一执行最高票。
    socket.on(WsEvents.VOTE, (data: { playerId: string; map: string }) => {
        if (gameSession.phase !== GamePhase.MapBan || !gameSession.mapVote) return;
        const player = findPlayerById(data.playerId);
        if (!player || player.role === 'Spectator' || player.role === 'Admin') return;
        const team = player.rosterTeam;
        if (!team || team !== gameSession.mapVote.team) return;
        if (!getAvailableMaps().includes(data.map)) return;
        gameSession.mapVote.votes[data.playerId] = data.map;
        broadcastState();
    });

    // 选边投票：由 sidePickTeam 队内所有玩家投票，倒计时结束后票数最多的一方生效。
    socket.on(WsEvents.SIDE_PICK, (data: { playerId: string; side: 'CT' | 'T' }) => {
        if (gameSession.phase !== GamePhase.SidePick || !gameSession.sideVote) return;
        const player = findPlayerById(data.playerId);
        if (!player || player.role === 'Spectator' || player.role === 'Admin') return;
        if (player.rosterTeam !== gameSession.sideVote.team) return;
        if (data.side !== 'CT' && data.side !== 'T') return;
        gameSession.sideVote.votes[data.playerId] = data.side;
        broadcastState();
    });

    // 准备就绪：只记录准备状态，不再自动分配/发放身份。身份必须由管理员手动发放。
    socket.on('PLAYER_READY', (data: { playerId: string }) => {
        if (gameSession.phase !== GamePhase.PreGameSetup) return;
        const player = findPlayerById(data.playerId);
        if (!player || player.role === 'Admin' || player.role === 'Spectator') return;
        player.isReady = true;
        broadcastState();
        const allReady = getGamePlayers().every(p => p.isReady);
        if (allReady) io.emit(WsEvents.NOTIFICATION, { message: '所有参赛玩家已准备，等待管理员分配并发放身份。' });
    });

    // 任务操作
    socket.on(WsEvents.TASK_ACTION, (data: { playerId: string; action: string; cellId: string; nValue?: number }) => {
        const player = findPlayerById(data.playerId);
        if (!player || !player.taskGrid) return;
        const cell = player.taskGrid[data.cellId];
        if (!cell) return;

        switch (data.action) {
            case 'MARK_COMPLETE':
                if (cell.status === 'Abandoned' || cell.nType !== 'none') return;
                cell.status = 'Complete';
                if (!cell.borderHistory) cell.borderHistory = [];
                if (!cell.borderHistory.includes('green')) cell.borderHistory.push('green');
                break;
            case 'UNDO_COMPLETE':
                if (cell.status === 'Abandoned' || cell.isReplaced) return;
                cell.status = 'Incomplete';
                if (cell.borderHistory) cell.borderHistory = cell.borderHistory.filter((c: string) => c !== 'green' && c !== 'orange');
                cell.nValue = 0;
                break;
            case 'ABANDON':
                if (cell.status === 'Complete' || cell.status === 'Abandoned') return;
                if (player.abandonCount === undefined) player.abandonCount = 0;
                if (player.abandonCount >= 1) return;
                cell.status = 'Abandoned'; player.abandonCount++;
                break;
            case 'REQUEST_HINT':
                if (cell.status === 'Complete' || cell.status === 'Abandoned' || cell.isHintUsed) return;
                cell.isHintUsed = true;
                if (!cell.borderHistory) cell.borderHistory = [];
                if (!cell.borderHistory.includes('blue')) cell.borderHistory.push('blue');
                break;
            case 'REPLACE':
                if (cell.status === 'Complete' || cell.status === 'Abandoned') return;
                if (player.replaceCount === undefined) player.replaceCount = 0;
                if (player.replaceCount >= 1) return;
                const repTask = gameSession.taskTemplate?.replacementTask as any;
                if (!repTask || cell.level > repTask.level) {
                    socket.emit(WsEvents.NOTIFICATION, { message: '目标任务等级过高，无法替换' });
                    return;
                }
                cell.status = 'Incomplete'; cell.isReplaced = true; cell.description = repTask.description; cell.level = repTask.level;
                cell.levelLabel = repTask.level.toString();
                if (!cell.borderHistory) cell.borderHistory = [];
                if (!cell.borderHistory.includes('purple')) cell.borderHistory.push('purple');
                player.replaceCount++;
                break;
            case 'N_ADD':
            case 'N_SUB':
            case 'N_SET':
                if (cell.nType === 'none' || cell.status === 'Abandoned' || cell.status === 'Complete') return;
                let newVal = cell.nValue || 0;
                if (data.action === 'N_ADD') newVal++;
                if (data.action === 'N_SUB') newVal--;
                if (data.action === 'N_SET' && data.nValue !== undefined) newVal = data.nValue;

                if (newVal < 0) newVal = 0;
                if (cell.nMax && newVal > cell.nMax) newVal = cell.nMax;
                cell.nValue = newVal;

                if (!cell.borderHistory) cell.borderHistory = [];
                cell.borderHistory = cell.borderHistory.filter((c: string) => c !== 'green' && c !== 'orange');

                if (cell.nValue > 0 && cell.nValue === cell.nMax) {
                    cell.status = 'Complete';
                    cell.borderHistory.push('green');
                } else if (cell.nValue > 0 && cell.nValue < (cell.nMax || 99)) {
                    cell.status = 'Incomplete';
                    cell.borderHistory.push('orange');
                } else {
                    cell.status = 'Incomplete';
                }
                break;
        }
        sendPrivateData(socket.id, player.playerId);
        broadcastState();
    });

    // Stage 3.13：中场问答改为游戏内语音交流，网页不再接收问题和回答。
    // 指认投票
    socket.on('ACCUSE', (data: { playerId: string; targetId: string; type: 'own' | 'enemy' }) => {
        if (!isUndercoverModeEnabled()) return;
        if (gameSession.phase !== GamePhase.PostGameAccusation) return;
        const accuser = findPlayerById(data.playerId);
        if (!accuser || accuser.role === 'Spectator' || accuser.role === 'Admin') return;
        const target = findPlayerById(data.targetId);
        if (!target || target.role === 'Spectator' || target.role === 'Admin') return;
        if (!gameSession.accusations[data.playerId]) gameSession.accusations[data.playerId] = { own: null, enemy: null };
        const acc = gameSession.accusations[data.playerId];
        if (data.type === 'own') acc.own = data.targetId;
        else acc.enemy = data.targetId;
        broadcastState();
        if (getGamePlayers().every(p => { const a = gameSession.accusations[p.playerId]; return a && a.own && a.enemy; })) {
            advancePhase(GamePhase.PostGameAccusation, GamePhase.Scoreboard);
        }
    });

    // 退出游戏
    socket.on('PLAYER_QUIT', (data: { playerId: string; confirmName: string }) => {
        const player = findPlayerById(data.playerId);
        if (!player || player.name !== data.confirmName) { socket.emit(WsEvents.NOTIFICATION, { message: '名字不匹配，无法退出' }); return; }
        if (gameSession.phase !== GamePhase.Lobby) { socket.emit(WsEvents.NOTIFICATION, { message: '只有在大厅阶段才能退出' }); return; }
        delete gameSession.players[player.playerId];
        gameSession.playerOrder = gameSession.playerOrder.filter(id => id !== player.playerId);
        socket.leave(player.playerId); socket.data.playerId = null;
        broadcastState();
        socket.emit(WsEvents.LOGIN_RESPONSE, { success: false, message: '你已退出游戏' });
    });

    socket.on('disconnect', () => console.log(`客户端断开: ${socket.id}`));
});

setInterval(() => {
    if (gameSession.phase === GamePhase.PlayerDraft && gameSession.draftPickTimeoutAt && Date.now() > gameSession.draftPickTimeoutAt) finishDraftPick('timeout');
    if (gameSession.phase === GamePhase.MapBan && gameSession.mapVote && Date.now() > gameSession.mapVote.timeoutAt) finishMapVote('timeout');
    if (gameSession.phase === GamePhase.SidePick && gameSession.sideVote && Date.now() > gameSession.sideVote.timeoutAt) finishSideVote('timeout');
}, 1000);

const PORT = process.env.PORT || 3000;
httpServer.listen(PORT, () => console.log(`草人杯指挥台已启动: http://localhost:${PORT}`));

// v1.3.3 game-code-login start
type V1333GameLoginTicket = {
  code: string;
  steamId: string;
  name: string;
  expiresAt: number;
};

const v1333NormalizeConnectUrl = (raw: unknown): string => {
  const value = String(raw || '').trim();
  if (!value) return '';
  if (/^steam:\/\//i.test(value)) return value;
  if (/^connect\s+/i.test(value)) return `steam://connect/${value.replace(/^connect\s+/i, '').trim()}`;
  if (/^[a-z0-9.-]+:\d+(?:\/.*)?$/i.test(value) || /^\d{1,3}(?:\.\d{1,3}){3}:\d+(?:\/.*)?$/.test(value)) {
    return `steam://connect/${value}`;
  }
  return value;
};

const v1333NumberEnv = (raw: unknown, fallback: number, min: number): number => {
  const value = Number(raw);
  return Number.isFinite(value) ? Math.max(min, value) : fallback;
};

const V1333_GAME_SERVER_CONNECT_URL = v1333NormalizeConnectUrl(process.env.GAME_SERVER_CONNECT_URL || process.env.GAME_SERVER_ADDRESS || '');
const V1333_GAME_LOGIN_CODE_TTL_SECONDS = v1333NumberEnv(process.env.GAME_LOGIN_CODE_TTL_SECONDS, 21600, 300);
const V1333_PLUGIN_ONLINE_TTL_MS = v1333NumberEnv(process.env.PLUGIN_ONLINE_TTL_MS, 15000, 3000);
const v1333GameLoginTickets = new Map<string, V1333GameLoginTicket>();

const v1333MakeGameLoginCode = (): string => {
  const alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
  for (let attempt = 0; attempt < 20; attempt++) {
    let code = '';
    for (let i = 0; i < 6; i++) code += alphabet[Math.floor(Math.random() * alphabet.length)];
    if (!v1333GameLoginTickets.has(code)) return code;
  }
  return Math.floor(100000 + Math.random() * 900000).toString();
};

const v1333CleanupGameLoginTickets = () => {
  const now = Date.now();
  for (const [code, ticket] of v1333GameLoginTickets.entries()) {
    if (ticket.expiresAt <= now) v1333GameLoginTickets.delete(code);
  }
};

const v1333IssueGameLoginCode = (steamIdRaw: unknown, nameRaw: unknown): V1333GameLoginTicket => {
  v1333CleanupGameLoginTickets();

  const steamId = normalizeSteamId(steamIdRaw);
  if (!steamId) throw new Error('invalid steamId');

  const name = normalizeLoginText(nameRaw) || `Steam ${steamId.slice(-6)}`;

  for (const [oldCode, ticket] of v1333GameLoginTickets.entries()) {
    if (ticket.steamId === steamId) v1333GameLoginTickets.delete(oldCode);
  }

  const ticket: V1333GameLoginTicket = {
    code: v1333MakeGameLoginCode(),
    steamId,
    name,
    expiresAt: Date.now() + V1333_GAME_LOGIN_CODE_TTL_SECONDS * 1000,
  };

  v1333GameLoginTickets.set(ticket.code, ticket);
  return ticket;
};

const v1333GetGameLoginTicket = (codeRaw: unknown): V1333GameLoginTicket | undefined => {
  v1333CleanupGameLoginTickets();

  const code = normalizeLoginText(codeRaw).toUpperCase();
  if (!code) return undefined;

  const ticket = v1333GameLoginTickets.get(code);
  if (!ticket) return undefined;

  if (ticket.expiresAt <= Date.now()) {
    v1333GameLoginTickets.delete(code);
    return undefined;
  }

  return ticket;
};

const v1333EnsurePlayerFromGameLoginTicket = (ticket: V1333GameLoginTicket): Player => {
  const existing = findPlayerBySteamId(ticket.steamId);
  if (existing) {
    existing.name = ticket.name || existing.name;
    existing.steamId = ticket.steamId;
    return existing;
  }

  const playerId = uuidv4();
  const player: Player = {
    playerId,
    name: ticket.name || `Steam ${ticket.steamId.slice(-6)}`,
    role: gameSession.phase === GamePhase.Lobby ? 'Player' : 'Spectator',
    steamId: ticket.steamId,
    bindCode: generateBindCode(),
    isReady: false,
  };

  gameSession.players[playerId] = player;
  gameSession.playerOrder.push(playerId);
  return player;
};

const v1333CreateAdminPlayer = (): Player => {
  const existing = getGamePlayers().find(p => p.role === 'Admin' && p.name === 'Admin');
  if (existing) return existing;

  const playerId = uuidv4();
  const player: Player = {
    playerId,
    name: 'Admin',
    role: 'Admin',
    bindCode: generateBindCode(),
    isReady: true,
  };

  gameSession.players[playerId] = player;
  gameSession.playerOrder.push(playerId);
  return player;
};

const v1333AttachPlayerToSocket = (socket: any, player: Player, message: string, loginCode?: string) => {
  socket.data.playerId = player.playerId;
  socket.join(player.playerId);

  socket.emit(WsEvents.LOGIN_RESPONSE, {
    success: true,
    playerId: player.playerId,
    player,
    role: player.role,
    name: player.name,
    bindCode: loginCode || player.bindCode,
    loginCode,
    message,
  });

  sendPrivateData(socket.id, player.playerId);
  broadcastState();
};

app.get('/api/public/server-status', (_req, res) => {
  const live = gameSession.liveGameData;
  const lastHeartbeatAt = live?.lastPluginHeartbeatAt || null;
  const heartbeatFresh = !!lastHeartbeatAt && Date.now() - Number(lastHeartbeatAt) < V1333_PLUGIN_ONLINE_TTL_MS;
  const online = live?.pluginConnected === true && heartbeatFresh;

  res.json({
    success: true,
    online,
    pluginConnected: live?.pluginConnected === true,
    lastHeartbeatAt,
    mapName: live?.mapName || '',
    connectUrl: V1333_GAME_SERVER_CONNECT_URL,
    connectUrlConfigured: !!V1333_GAME_SERVER_CONNECT_URL,
  });
});

app.post('/api/plugin/game-login-code', requirePluginAuth, (req, res) => {
  try {
    const ticket = v1333IssueGameLoginCode(req.body?.steamId, req.body?.name);

    return res.json({
      success: true,
      code: ticket.code,
      expiresInSeconds: V1333_GAME_LOGIN_CODE_TTL_SECONDS,
      steamId: ticket.steamId,
      name: ticket.name,
    });
  } catch (err: any) {
    return res.status(400).json({
      success: false,
      error: err?.message || 'failed to create game login code',
    });
  }
});

io.on('connection', (socket) => {
  socket.on('GAME_CODE_LOGIN', (payload: any) => {
    const credentialRaw = normalizeLoginText(payload?.credential);
    const credential = credentialRaw.toUpperCase();

    if (!credential) {
      socket.emit(WsEvents.LOGIN_RESPONSE, { success: false, message: '请输入游戏内返回的码或管理员密码。' });
      return;
    }

    if (credentialRaw === ADMIN_PASSWORD) {
      const admin = v1333CreateAdminPlayer();
      v1333AttachPlayerToSocket(socket, admin, '管理员登录成功。');
      return;
    }

    if (credential === 'SPEC') {
      const playerId = uuidv4();
      const spectator: Player = {
        playerId,
        name: 'Spectator',
        role: 'Spectator',
        bindCode: generateBindCode(),
        isReady: false,
      };
      gameSession.players[playerId] = spectator;
      gameSession.playerOrder.push(playerId);
      v1333AttachPlayerToSocket(socket, spectator, '旁观者登录成功。');
      return;
    }

    const ticket = v1333GetGameLoginTicket(credential);
    if (!ticket) {
      socket.emit(WsEvents.LOGIN_RESPONSE, { success: false, message: '游戏内返回的码无效或已过期，请回到游戏里重新输入 !cclogin 获取新码。' });
      return;
    }

    const player = v1333EnsurePlayerFromGameLoginTicket(ticket);
    v1333AttachPlayerToSocket(socket, player, `欢迎，${player.name}！已进入草人杯大厅。`, ticket.code);
  });
});
// v1.3.3 game-code-login end

