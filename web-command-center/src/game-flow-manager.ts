// game-flow-manager.ts
import { v4 as uuidv4 } from 'uuid';
import {
    GameSession,
    GamePhase,
    Player,
    RosterTeam,
    Team,
    LiveGameData,
} from './types';
import { getSession } from './session-manager';
import { canTransition } from './state-machine';
import {
    findPlayerById,
    getGamePlayers,
    getTeamPlayers,
    oppositeSide,
} from './player-utils';
import { calculateScores } from './scoring';
import { getDefaultTaskTemplate, assignTaskGridToPlayer } from './task-system';
import {
    clearDraftPickTimer,
    clearMapVoteTimer,
    clearSideVoteTimer,
    setDraftPickTimer,
    setMapVoteTimer,
    setSideVoteTimer,
} from './game-timers';
import {
    DRAFT_PICK_SECONDS,
    MAP_BAN_FIRST_SECONDS,
    MAP_BAN_SECOND_SECONDS,
    MAP_BAN_LATER_SECONDS,
    SIDE_PICK_VOTE_SECONDS,
    MAP_BAN_COUNT_PER_TURN,
} from './game-constants';
import {
    DUEL_DEFAULT_MAP,
    DUEL_DEFAULT_ROUND_TIME_MINUTES,
    DUEL_DEFAULT_WORKSHOP_ID,
    getDuelTotalRounds,
    normalizeDuelMap,
    normalizeDuelRoundTimeMinutes,
    normalizeDuelRounds,
    normalizeDuelUtilityMode,
    resolveDuelMapConfig,
} from './duel-config';
import { enqueuePluginCommand } from './plugin-command-queue';

// ========== Broadcast and notification hooks ==========
let broadcast: (() => void) | null = null;
let notifyMessage: ((msg: string) => void) | null = null;

export const injectFlowBroadcast = (fn: () => void) => { broadcast = fn; };
export const injectNotify = (fn: (msg: string) => void) => { notifyMessage = fn; };

// ========== Internal flow helpers ==========
const randomizeCaptainForTeam = (team: 'A' | 'B') => {
    const session = getSession();
    const candidates = getGamePlayers(session).filter(
        p => p.playerId !== session.captains.A && p.playerId !== session.captains.B
    );
    if (candidates.length > 0) {
        session.captains[team] = candidates[Math.floor(Math.random() * candidates.length)].playerId;
    }
};

const getAvailableDraftPlayers = (): Player[] => {
    const session = getSession();
    const capAId = session.captains.A;
    const capBId = session.captains.B;
    return getGamePlayers(session).filter(
        p => !p.rosterTeam && p.playerId !== capAId && p.playerId !== capBId && p.role !== 'Admin'
    );
};

const isDraftComplete = (): boolean => {
    const session = getSession();
    return session.draftIndex >= session.draftOrder.length || getAvailableDraftPlayers().length === 0;
};

const syncPendingDraftOrderWithRoster = () => {
    const session = getSession();
    const originalOrder = session.draftOriginalOrder || session.draftOrder;
    const assignedCounts: Record<RosterTeam, number> = { A: 0, B: 0 };
    for (const team of ['A', 'B'] as const) {
        assignedCounts[team] = session.teams[team].players.filter(
            id => id !== session.captains.A && id !== session.captains.B
        ).length;
    }

    const remainingOrder: RosterTeam[] = [];
    for (const team of originalOrder) {
        if (assignedCounts[team] > 0) assignedCounts[team]--;
        else remainingOrder.push(team);
    }
    session.draftOrder = remainingOrder;
    session.draftIndex = 0;
};

const getCurrentDraftBatch = () => {
    const session = getSession();
    if (session.draftIndex >= session.draftOrder.length) return null;
    const team = session.draftOrder[session.draftIndex];
    let start = session.draftIndex;
    while (start > 0 && session.draftOrder[start - 1] === team) start--;
    let end = session.draftIndex;
    while (end < session.draftOrder.length && session.draftOrder[end] === team) end++;
    const totalCount = end - start;
    const pickedInBatch = session.draftIndex - start;
    const remainingCount = end - session.draftIndex;
    return { team, start, end, totalCount, pickedInBatch, remainingCount };
};

const removePlayerFromRosterTeams = (playerId: string) => {
    const session = getSession();
    for (const team of ['A', 'B'] as const) {
        session.teams[team].players = session.teams[team].players.filter(id => id !== playerId);
    }
};

const assignPlayerToRosterFlow = (playerId: string, team: RosterTeam): boolean => {
    const session = getSession();
    const player = findPlayerById(session, playerId);
    if (!player || player.role === 'Admin' || player.role === 'Spectator') return false;
    if (playerId === session.captains.A || playerId === session.captains.B) return false;
    removePlayerFromRosterTeams(playerId);
    player.rosterTeam = team;
    if (!session.teams[team].players.includes(playerId)) {
        session.teams[team].players.push(playerId);
    }
    return true;
};

const getAvailableMaps = (): string[] => {
    const session = getSession();
    return session.mapPool.filter(m => !session.bannedMaps.includes(m));
};

const isDuelMode = () => getSession().matchOptions?.matchMode === 'duel';
const DUEL_READY_WAIT_SECONDS = Math.max(1, Number(process.env.DUEL_READY_WAIT_SECONDS || 90));
const DUEL_FALLBACK_MAP_COMMAND = String(process.env.DUEL_FALLBACK_MAP_COMMAND || 'changelevel de_dust2').trim() || 'changelevel de_dust2';
const DUEL_JOIN_EXTEND_THRESHOLD_SECONDS = Math.max(1, Number(process.env.DUEL_JOIN_EXTEND_THRESHOLD_SECONDS || 15));
const DUEL_JOIN_EXTEND_SECONDS = Math.max(1, Number(process.env.DUEL_JOIN_EXTEND_SECONDS || 10));

const getCurrentMapBanCount = (): number => {
    const availableCount = getAvailableMaps().length;
    return Math.min(MAP_BAN_COUNT_PER_TURN, Math.max(1, availableCount - 1));
};

const pickTopVotedMaps = (available: string[], votes: Record<string, string>, banCount: number): string[] => {
    const tally: Record<string, number> = {};
    available.forEach(m => tally[m] = 0);
    Object.values(votes).forEach(m => {
        if (available.includes(m)) tally[m] = (tally[m] || 0) + 1;
    });
    const withTieBreaker = available
        .map(map => ({ map, count: tally[map] || 0, tie: Math.random() }))
        .sort((a, b) => (b.count - a.count) || (b.tie - a.tie));
    return withTieBreaker.slice(0, Math.min(banCount, Math.max(1, available.length - 1))).map(x => x.map);
};

const getMapBanVoteDurationSeconds = (): number => {
    const session = getSession();
    const turnNo = session.bannedMaps.length + 1;
    if (turnNo === 1) return Math.max(1, MAP_BAN_FIRST_SECONDS);
    if (turnNo === 2) return Math.max(1, MAP_BAN_SECOND_SECONDS);
    return Math.max(1, MAP_BAN_LATER_SECONDS);
};

// ========== Draft flow ==========
const scheduleDraftToMapBan = () => {
    clearDraftPickTimer();
    const session = getSession();
    session.draftPickTimeoutAt = null;
    session.timerEndAt = null;
    session.timerPhase = null;
    if (session.rollTimeout) clearTimeout(session.rollTimeout);
    session.rollTimeout = setTimeout(() => {
        advancePhase(GamePhase.PlayerDraft, GamePhase.MapBan);
    }, 1500);
};

const applyDraftPick = (pickedId?: string, reason: 'manual' | 'timeout' = 'manual'): Player | null => {
    const session = getSession();
    if (session.phase !== GamePhase.PlayerDraft) return null;
    if (session.draftIndex >= session.draftOrder.length) return null;

    const currentTeam = session.draftOrder[session.draftIndex];
    const available = getAvailableDraftPlayers();
    if (available.length === 0) return null;

    let picked = pickedId ? available.find(p => p.playerId === pickedId) : undefined;
    if (!picked) picked = available[Math.floor(Math.random() * available.length)];

    if (!assignPlayerToRosterFlow(picked.playerId, currentTeam)) return null;
    session.draftIndex++;

    if (reason === 'timeout') {
        notifyMessage?.(`选人超时，系统已为 ${currentTeam} 队自动选择 ${picked.name}`);
    }
    return picked;
};

const startDraftPickTimerFunc = (shouldBroadcast = false) => {
    clearDraftPickTimer();
    const session = getSession();
    if (session.phase !== GamePhase.PlayerDraft) return;
    session.draftCaptainsActive = true;

    if (isDraftComplete()) {
        scheduleDraftToMapBan();
        if (shouldBroadcast) broadcast?.();
        return;
    }

    const dur = Math.max(1, DRAFT_PICK_SECONDS);
    session.draftPickTimeoutAt = Date.now() + dur * 1000;
    session.timerEndAt = session.draftPickTimeoutAt;
    session.timerPhase = GamePhase.PlayerDraft;
    const timer = setTimeout(() => {
        setDraftPickTimer(null);
        finishDraftPick('timeout');
    }, dur * 1000);
    setDraftPickTimer(timer);
    if (shouldBroadcast) broadcast?.();
};

const finishDraftPick = (reason: 'timeout' | 'manual' = 'timeout') => {
    const session = getSession();
    if (session.phase !== GamePhase.PlayerDraft) return;
    clearDraftPickTimer();
    session.draftCaptainsActive = true;

    const batch = getCurrentDraftBatch();
    const team = batch?.team;
    if (team) {
        while (!isDraftComplete() && session.draftOrder[session.draftIndex] === team) {
            const picked = applyDraftPick(undefined, reason === 'timeout' ? 'timeout' : 'manual');
            if (!picked) break;
        }
    }

    if (isDraftComplete()) {
        session.draftPickTimeoutAt = null;
        session.timerEndAt = null;
        session.timerPhase = null;
        broadcast?.();
        scheduleDraftToMapBan();
    } else {
        startDraftPickTimerFunc(true);
    }
};

const startCaptainDraftFunc = (shouldBroadcast = true) => {
    const session = getSession();
    if (session.phase !== GamePhase.PlayerDraft) return false;

    syncPendingDraftOrderWithRoster();
    if (isDraftComplete()) {
        session.draftCaptainsActive = false;
        session.draftPickTimeoutAt = null;
        session.timerEndAt = null;
        session.timerPhase = null;
        scheduleDraftToMapBan();
        if (shouldBroadcast) broadcast?.();
        return true;
    }

    startDraftPickTimerFunc(shouldBroadcast);
    return true;
};

// ========== Map ban flow ==========
const startMapVoteFunc = (team: RosterTeam) => {
    clearMapVoteTimer();
    const session = getSession();
    const dur = getMapBanVoteDurationSeconds();
    session.mapVote = {
        team,
        votes: {},
        timeoutAt: Date.now() + dur * 1000,
        banCount: getCurrentMapBanCount(),
    };
    session.currentBanTeam = team;
    session.timerEndAt = session.mapVote.timeoutAt;
    session.timerPhase = GamePhase.MapBan;
    const timer = setTimeout(() => {
        setMapVoteTimer(null);
        finishMapVote('timeout');
    }, dur * 1000);
    setMapVoteTimer(timer);
};

const finishMapVote = (reason: 'timeout' | 'admin' | 'manual' = 'timeout') => {
    const session = getSession();
    if (session.phase !== GamePhase.MapBan || !session.mapVote) return;
    clearMapVoteTimer();

    const available = getAvailableMaps();
    if (available.length === 0) return;

    const votes = session.mapVote.votes ?? {};
    const banCount = Math.min(session.mapVote.banCount || 1, Math.max(1, available.length - 1));
    const banMaps = pickTopVotedMaps(available, votes, banCount);
    const banTeam = session.mapVote.team;

    for (const map of banMaps) {
        if (!session.bannedMaps.includes(map) && getAvailableMaps().length > 1) {
            session.bannedMaps.push(map);
        }
    }

    notifyMessage?.(`地图投票结束，已 Ban：${banMaps.length > 0 ? banMaps.join('、') : '无'}`);

    session.mapVote = undefined;
    session.currentBanTeam = null;
    session.timerEndAt = null;
    session.timerPhase = null;
    broadcast?.();

    if (getAvailableMaps().length === 1) {
        session.selectedMap = getAvailableMaps()[0];
        session.sidePickTeam = banTeam === 'A' ? 'B' : 'A';
        advancePhase(GamePhase.MapBan, GamePhase.SidePick);
    } else {
        const nextIdx = session.bannedMaps.length;
        if (nextIdx < session.banSequence.length) {
            startMapVoteFunc(session.banSequence[nextIdx]);
        } else {
            const remaining = getAvailableMaps();
            session.selectedMap = remaining[Math.floor(Math.random() * remaining.length)];
            session.sidePickTeam = banTeam === 'A' ? 'B' : 'A';
            advancePhase(GamePhase.MapBan, GamePhase.SidePick);
            return;
        }
        broadcast?.();
    }
};

// ========== Side pick flow ==========
const startSideVoteFunc = (team: RosterTeam = getSession().sidePickTeam || 'A') => {
    clearSideVoteTimer();
    const session = getSession();
    const dur = SIDE_PICK_VOTE_SECONDS;
    session.sidePickTeam = team;
    session.selectedSide = null;
    session.sideVote = {
        team,
        votes: {},
        timeoutAt: Date.now() + dur * 1000,
    };
    session.timerEndAt = session.sideVote.timeoutAt;
    session.timerPhase = GamePhase.SidePick;
    const timer = setTimeout(() => {
        setSideVoteTimer(null);
        finishSideVote('timeout');
    }, dur * 1000);
    setSideVoteTimer(timer);
};

const finishSideVote = (reason: 'timeout' | 'admin' | 'manual' = 'timeout') => {
    const session = getSession();
    if (session.phase !== GamePhase.SidePick || !session.sideVote) return;
    clearSideVoteTimer();

    const votes = session.sideVote.votes || {};
    let ct = 0, t = 0;
    for (const side of Object.values(votes)) {
        if (side === 'CT') ct++;
        else if (side === 'T') t++;
    }
    const pickedTeam = session.sideVote.team || session.sidePickTeam || 'A';
    const selectedSide: 'CT' | 'T' = ct > t ? 'CT' : (t > ct ? 'T' : (Math.random() < 0.5 ? 'CT' : 'T'));
    const oppositePickedSide: 'CT' | 'T' = selectedSide === 'CT' ? 'T' : 'CT';
    const teamASide: 'CT' | 'T' = pickedTeam === 'A' ? selectedSide : oppositePickedSide;
    session.selectedSide = teamASide;
    session.sideVote = undefined;
    session.timerEndAt = null;
    session.timerPhase = null;
    setRosterLiveSides(teamASide);
    notifyMessage?.(`选边投票结束，${session.sidePickTeam || 'A'} 队选择 ${selectedSide}`);
    broadcast?.();
    advancePhase(GamePhase.SidePick, GamePhase.PreGameSetup);
};

const setRosterLiveSides = (teamASide: Team) => {
    const session = getSession();
    const teamBSide = oppositeSide(teamASide);
    for (const p of getTeamPlayers(session, 'A')) p.team = teamASide;
    for (const p of getTeamPlayers(session, 'B')) p.team = teamBSide;
};

// ========== Live game data updates ==========
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
    roundBaseOffset: undefined,
    formalRoundStartRaw: undefined,
    formalStatsStarted: false,
    killMatrix: {},
    openingKillMatrix: {},
    awpKillMatrix: {},
    firstKillRounds: {},
});

const createEmptyMatchStats = () => ({
    kills: 0,
    deaths: 0,
    assists: 0,
    damage: 0,
    entryCount: 0,
    entryWins: 0,
    enemy2ks: 0,
    enemy3ks: 0,
    enemy4ks: 0,
    enemy5ks: 0,
    headShotKills: 0,
    flashSuccesses: 0,
    enemiesFlashed: 0,
    utilityDamage: 0,
    v1Count: 0,
    v1Wins: 0,
    v2Count: 0,
    v2Wins: 0,
    v3Count: 0,
    v3Wins: 0,
    v4Count: 0,
    v4Wins: 0,
    v5Count: 0,
    v5Wins: 0,
    v6Count: 0,
    v6Wins: 0,
    tradedDeaths: 0,
    equipmentSwing: 0,
    situationSwing: 0,
});

const createEmptySideStats = () => ({
    CT: createEmptyMatchStats(),
    T: createEmptyMatchStats(),
});

const resetUndercoverReadiness = (player: Player) => {
    if (player.gameRole === 'Undercover') {
        player.isReady = false;
        player.undercoverTaskAckStage = 'none';
    } else {
        player.undercoverTaskAckStage = undefined;
    }
};

const ensureUndercoverTaskGrid = (player: Player, session: GameSession) => {
    if (player.gameRole !== 'Undercover') {
        delete player.taskGrid;
        player.undercoverTaskAckStage = undefined;
        return;
    }

    if (!player.taskGrid) assignTaskGridToPlayer(player, session.taskTemplate!);
    player.undercoverTaskAckStage =
        player.undercoverTaskAckStage === 'received' || player.undercoverTaskAckStage === 'read'
            ? player.undercoverTaskAckStage
            : 'none';
};

const prepareReleasedRoleState = () => {
    const session = getSession();
    for (const player of getGamePlayers(session)) {
        resetUndercoverReadiness(player);
        if (player.gameRole === 'Undercover') {
            ensureUndercoverTaskGrid(player, session);
        } else {
            delete player.taskGrid;
        }
    }
};

const normalizePluginRound = (rawRound: unknown): number => {
    const session = getSession();
    if (!session.liveGameData) session.liveGameData = createEmptyLiveGameData();
    const live = session.liveGameData;
    const raw = Math.floor(Number(rawRound || 0));
    if (!Number.isFinite(raw) || raw <= 0) return Math.max(0, live.currentRound || 0);
    live.rawPluginRound = raw;
    if (live.formalStatsStarted === true && typeof live.formalRoundStartRaw === 'number') {
        return Math.max(1, raw - live.formalRoundStartRaw + 1);
    }
    if (typeof live.roundBaseOffset !== 'number') {
        live.roundBaseOffset = Math.max(0, raw - 1);
    }
    return Math.max(1, raw - live.roundBaseOffset);
};

const nonNegativeInt = (value: unknown): number => {
    const n = Math.floor(Number(value || 0));
    return Number.isFinite(n) ? Math.max(0, n) : 0;
};

const getScoreDerivedCurrentRound = (live: Partial<LiveGameData> | null | undefined): number => {
    if (!live) return 0;
    const currentRound = nonNegativeInt(live.currentRound);
    const completedByCtT = nonNegativeInt(live.scoreCT) + nonNegativeInt(live.scoreT);
    const completedByRoster = nonNegativeInt(live.scoreA) + nonNegativeInt(live.scoreB);
    const completedRounds = Math.max(completedByCtT, completedByRoster, nonNegativeInt(live.lastScoredRound));
    if (completedRounds <= 0) return currentRound;
    return Math.max(currentRound, completedRounds);
};

const syncCurrentRoundFromScores = (live: LiveGameData | null | undefined): number => {
    if (!live) return 0;
    const round = getScoreDerivedCurrentRound(live);
    if (round > 0) live.currentRound = round;
    return live.currentRound;
};

const updateMatchFinishState = () => {
    const session = getSession();
    if (!session.liveGameData) return;
    const scoreA = session.liveGameData.scoreA || 0;
    const scoreB = session.liveGameData.scoreB || 0;
    const winTarget = getRequiredWinTarget(scoreA, scoreB);
    const winner = getMatchWinner(scoreA, scoreB);
    session.liveGameData.winTarget = winTarget;
    session.liveGameData.winnerTeam = winner;
    session.liveGameData.matchFinished = session.matchOptions?.matchMode === 'duel'
        ? (session.liveGameData.scoreA + session.liveGameData.scoreB) >= winTarget
        : !!winner;
    syncCurrentRoundFromScores(session.liveGameData);
};

const getRequiredWinTarget = (scoreA: number, scoreB: number): number => {
    const session = getSession();
    if (session.matchOptions?.matchMode === 'duel') {
        return getDuelTotalRounds(session.matchOptions.duelRounds);
    }
    const minScore = Math.min(scoreA, scoreB);
    if (minScore < 12) return 13;
    return 16 + Math.floor((minScore - 12) / 3) * 3;
};

const getMatchWinner = (scoreA: number, scoreB: number): RosterTeam | null => {
    const target = getRequiredWinTarget(scoreA, scoreB);
    const session = getSession();
    if (session.matchOptions?.matchMode === 'duel') {
        if (scoreA + scoreB < target) return null;
        if (scoreA > scoreB) return 'A';
        if (scoreB > scoreA) return 'B';
        return null;
    }
    if (scoreA >= target && scoreA > scoreB) return 'A';
    if (scoreB >= target && scoreB > scoreA) return 'B';
    return null;
};

const resolveRosterTeamByInitialSide = (side: Team, roundNumber?: number): RosterTeam | null => {
    const session = getSession();
    if (!session.selectedSide) return null;
    let teamASide: Team = session.selectedSide;
    if (session.matchOptions?.matchMode === 'duel') {
        if (side === 'CT') return 'A';
        if (side === 'T') return 'B';
        return null;
    }
    const round = Number(roundNumber || session.liveGameData?.currentRound || 0);
    if (round >= 13 && round <= 24) teamASide = oppositeSide(teamASide);
    return side === teamASide ? 'A' : 'B';
};
const setupDuelFromLobby = (): boolean => {
    const session = getSession();
    const duelPlayers = getGamePlayers(session).filter(p => p.role !== 'Admin' && p.role !== 'Spectator');
    if (duelPlayers.length < 1) {
        notifyMessage?.('单挑模式至少需要 1 名参赛玩家。');
        return false;
    }

    for (const player of duelPlayers) {
        const existingRoster = player.rosterTeam === 'A' || player.rosterTeam === 'B' ? player.rosterTeam : undefined;
        removePlayerFromRosterTeams(player.playerId);
        player.rosterTeam = existingRoster;
        player.team = undefined;
        player.gameRole = 'Soldier';
        player.isReady = false;
        player.undercoverTaskAckStage = undefined;
        delete player.taskGrid;
    }

    for (const player of duelPlayers) player.role = 'Player';
    let hasA = duelPlayers.some(player => player.rosterTeam === 'A');
    let hasB = duelPlayers.some(player => player.rosterTeam === 'B');
    for (const player of duelPlayers) {
        if (player.rosterTeam) continue;
        if (!hasA) {
            player.rosterTeam = 'A';
            hasA = true;
        } else if (!hasB) {
            player.rosterTeam = 'B';
            hasB = true;
        }
        if (hasA && hasB) break;
    }

    for (const player of duelPlayers) {
        if (player.rosterTeam === 'A') player.team = 'CT';
        else if (player.rosterTeam === 'B') player.team = 'T';
    }

    session.teams.A.players = duelPlayers.filter(player => player.rosterTeam === 'A').map(player => player.playerId);
    session.teams.B.players = duelPlayers.filter(player => player.rosterTeam === 'B').map(player => player.playerId);
    session.captains.A = session.teams.A.players[0] || null;
    session.captains.B = session.teams.B.players[0] || null;
    session.selectedSide = 'CT';
    session.sidePickTeam = 'A';
    const duelMap = resolveDuelMapConfig(session.matchOptions?.duelMap || DUEL_DEFAULT_MAP, session.matchOptions?.duelMapWorkshopId);
    session.matchOptions.duelMap = duelMap.name;
    session.matchOptions.duelMapWorkshopId = duelMap.workshopId;
    session.selectedMap = duelMap.name;
    session.mapPool = [session.selectedMap];
    session.bannedMaps = [];
    session.banSequence = [];
    session.undercoverCount = 0;
    session.detectiveCount = 0;
    session.rolesReleased = true;
    session.questionsUsed = 0;
    session.currentQuestion = null;
    session.questionAnswer = null;
    session.secondQuestionAnswered = false;
    session.accusations = {};
    return true;
};

const queueDuelRulesCommands = (delaySeconds = 0) => {
    const session = getSession();
    const rounds = normalizeDuelRounds(session.matchOptions?.duelRounds);
    const totalRounds = getDuelTotalRounds(rounds);
    const roundTime = normalizeDuelRoundTimeMinutes(session.matchOptions?.duelRoundTimeMinutes || DUEL_DEFAULT_ROUND_TIME_MINUTES);
    const commands = [
        { command: `mp_maxrounds ${totalRounds}`, label: `duel total rounds ${totalRounds}` },
        { command: 'mp_winlimit 0', label: 'duel no win limit' },
        { command: 'mp_match_can_clinch 0', label: 'duel force full rounds' },
        { command: `mp_roundtime ${roundTime}`, label: `duel round time ${roundTime}` },
        { command: 'mp_freezetime 2', label: 'duel freeze time' },
        { command: 'mp_round_restart_delay 2', label: 'duel short round end' },
        { command: 'mp_free_armor 0', label: 'duel no free armor' },
        { command: 'mp_halftime 0', label: 'duel no halftime' },
        { command: 'mp_autoteambalance 0', label: 'duel no auto balance' },
        { command: 'mp_limitteams 0', label: 'duel no team limit' },
        { command: 'sv_showimpacts 0', label: 'duel hide bullet impacts' },
        { command: 'sv_showimpacts_time 0', label: 'duel clear bullet impact timer' },
        { command: 'mp_warmup_end', label: 'duel end warmup' },
        { command: 'mp_restartgame 1', label: 'duel restart' },
    ];

    for (const item of commands) {
        enqueuePluginCommand('EXECUTE_SERVER_COMMAND', {
            command: item.command,
            label: item.label,
            matchId: session.matchId,
            delaySeconds,
            requestedAt: Date.now(),
        });
    }
};

const queueDuelMapOnlySetup = () => {
    const session = getSession();
    const duelMap = resolveDuelMapConfig(session.matchOptions?.duelMap || DUEL_DEFAULT_MAP, session.matchOptions?.duelMapWorkshopId);
    const waitSeconds = Math.max(1, Math.floor(DUEL_READY_WAIT_SECONDS));
    session.matchOptions.duelMap = duelMap.name;
    session.matchOptions.duelMapWorkshopId = duelMap.workshopId;
    enqueuePluginCommand('EXECUTE_SERVER_COMMAND', {
        command: duelMap.command,
        label: `duel map ${duelMap.name}`,
        matchId: session.matchId,
        requestedAt: Date.now(),
    });
    const warmupCommands = [
        { command: 'mp_warmup_pausetimer 0', label: 'duel warmup timer enabled' },
        { command: `mp_warmuptime ${waitSeconds}`, label: `duel warmup ${waitSeconds}s` },
        { command: 'mp_warmup_start', label: 'duel warmup start' },
    ];
    for (const item of warmupCommands) {
        enqueuePluginCommand('EXECUTE_SERVER_COMMAND', {
            command: item.command,
            label: item.label,
            matchId: session.matchId,
            delaySeconds: 12,
            requestedAt: Date.now(),
        });
    }
};

const queueDuelFormalStart = (delaySeconds = 0) => {
    const session = getSession();
    queueDuelRulesCommands(delaySeconds);
    enqueuePluginCommand('CONFIGURE_DUEL_MODE', {
        matchId: session.matchId,
        rounds: normalizeDuelRounds(session.matchOptions?.duelRounds),
        utilityMode: normalizeDuelUtilityMode(session.matchOptions?.duelUtilityMode),
        requestedAt: Date.now(),
    });
};

const rollbackDuelToLobby = (reason = '单挑等待结束后参赛玩家不足，已回到大厅。') => {
    const session = getSession();
    session.phase = GamePhase.Lobby;
    session.matchId = uuidv4();
    session.liveGameData = undefined;
    session.timerEndAt = null;
    session.timerPhase = null;
    session.selectedMap = null;
    session.selectedSide = null;
    session.sidePickTeam = null;
    session.teams.A.players = [];
    session.teams.B.players = [];
    session.captains.A = null;
    session.captains.B = null;
    session.mapPool = ['Dust II', 'Inferno', 'Mirage', 'Ancient', 'Anubis', 'Nuke', 'Overpass', 'Cache', 'Train'];
    session.bannedMaps = [];
    session.banSequence = [];
    while (session.banSequence.length < session.mapPool.length - 1) session.banSequence.push('A', 'B');
    session.banSequence.length = session.mapPool.length - 1;
    for (const player of getGamePlayers(session)) {
        if (player.role !== 'Admin') player.role = 'Player';
        player.rosterTeam = undefined;
        player.team = undefined;
        player.gameRole = 'Soldier';
        player.isReady = false;
        player.undercoverTaskAckStage = undefined;
        delete player.taskGrid;
    }
    session.matchOptions.duelMap = DUEL_DEFAULT_MAP;
    session.matchOptions.duelMapWorkshopId = DUEL_DEFAULT_WORKSHOP_ID;
    enqueuePluginCommand('EXECUTE_SERVER_COMMAND', {
        command: DUEL_FALLBACK_MAP_COMMAND,
        label: 'duel fallback dust2',
        matchId: session.matchId,
        requestedAt: Date.now(),
    });
    notifyMessage?.(reason);
    broadcast?.();
};

const hasRequiredDuelPlayers = () => {
    const session = getSession();
    const players = getGamePlayers(session).filter(p => p.role !== 'Admin' && p.role !== 'Spectator');
    return players.some(p => p.rosterTeam === 'A') &&
        players.some(p => p.rosterTeam === 'B') &&
        players.every(p => p.rosterTeam === 'A' || p.rosterTeam === 'B');
};

const beginDuelFormalMatch = (matchId: string) => {
    const session = getSession();
    if (session.phase !== GamePhase.LiveGame || session.matchId !== matchId || session.matchOptions?.matchMode !== 'duel') return;
    const remainingMs = Number(session.timerEndAt || 0) - Date.now();
    if (session.liveGameData?.duelWaitingForPlayers === true && remainingMs > 0) {
        setTimeout(() => beginDuelFormalMatch(matchId), remainingMs);
        return;
    }
    if (!hasRequiredDuelPlayers()) {
        rollbackDuelToLobby('90 秒等待结束后，单挑分队仍未满足 A/B 双方都有玩家且无未分队参赛玩家，已终止单挑并回到大厅。');
        return;
    }
    const rawPluginRound = resetFormalMatchCounters();
    session.liveGameData!.rawPluginRound = 1;
    session.liveGameData!.roundBaseOffset = 0;
    session.liveGameData!.formalRoundStartRaw = 1;
    queueDuelFormalStart(0);
    enqueuePluginCommand('RESET_LIVE_MATCH_STATS', { currentRound: rawPluginRound });
    session.timerEndAt = null;
    session.timerPhase = null;
    notifyMessage?.('单挑等待结束，双方已就位；服务器即将重启并从手枪第 1 回合开始正式统计。');
    broadcast?.();
};

const extendDuelWaitingIfLateJoin = () => {
    const session = getSession();
    if (session.phase !== GamePhase.LiveGame || session.matchOptions?.matchMode !== 'duel') return false;
    if (session.liveGameData?.duelWaitingForPlayers !== true || !session.timerEndAt) return false;
    const remainingMs = session.timerEndAt - Date.now();
    if (remainingMs < 0 || remainingMs >= DUEL_JOIN_EXTEND_THRESHOLD_SECONDS * 1000) return false;
    session.timerEndAt += DUEL_JOIN_EXTEND_SECONDS * 1000;
    session.liveGameData.duelWaitExtendedAt = Date.now();
    session.liveGameData.duelWaitExtendCount = Number(session.liveGameData.duelWaitExtendCount || 0) + 1;
    notifyMessage?.(`单挑等待剩余不足 ${DUEL_JOIN_EXTEND_THRESHOLD_SECONDS} 秒时有玩家加入，等待时间已延长 ${DUEL_JOIN_EXTEND_SECONDS} 秒。`);
    broadcast?.();
    return true;
};

const resetFormalMatchCounters = (): number => {
    const session = getSession();
    const oldLive = session.liveGameData || createEmptyLiveGameData();
    const raw = Math.max(1, Math.floor(Number(oldLive.rawPluginRound || oldLive.currentRound || 1)));
    const keepMap = oldLive.mapName;
    const keepPluginConnected = oldLive.pluginConnected;
    const keepHeartbeatAt = oldLive.lastPluginHeartbeatAt;
    session.liveGameData = createEmptyLiveGameData();
    session.liveGameData.rawPluginRound = raw;
    session.liveGameData.roundBaseOffset = Math.max(0, raw - 1);
    session.liveGameData.formalRoundStartRaw = raw;
    session.liveGameData.currentRound = 1;
    session.liveGameData.formalStatsStarted = true;
    session.liveGameData.mapName = keepMap;
    session.liveGameData.pluginConnected = keepPluginConnected;
    session.liveGameData.lastPluginHeartbeatAt = keepHeartbeatAt;
    session.liveGameData.suppressSnapshotStatsUntil = Date.now() + 15000;
    for (const p of getGamePlayers(session)) {
        p.stats = createEmptyMatchStats();
        p.sideStats = createEmptySideStats();
        p.finalScore = undefined;
        p.scoreBreakdown = undefined;
    }
    return raw;
};

// ========== Role assignment ==========
const randomRemainingRoles = (onlyTeam?: RosterTeam) => {
    const session = getSession();
    const undercoverEnabled = session.matchOptions?.undercoverModeEnabled !== false;
    if (!undercoverEnabled) {
        const teamsToAssign: RosterTeam[] = onlyTeam ? [onlyTeam] : ['A', 'B'];
        for (const team of teamsToAssign) {
            for (const player of getTeamPlayers(session, team)) {
                player.gameRole = 'Soldier';
                delete player.taskGrid;
                player.undercoverTaskAckStage = undefined;
                player.detectiveQuestionCount = 0;
            }
        }
        session.undercoverCount = 0;
        session.detectiveCount = 0;
        session.rolesReleased = false;
        broadcast?.();
        return;
    }
    const teamsToAssign: RosterTeam[] = onlyTeam ? [onlyTeam] : ['A', 'B'];
    for (const team of teamsToAssign) {
        const players = getTeamPlayers(session, team);
        const assignedU = players.filter(p => p.gameRole === 'Undercover').length;
        const assignedD = players.filter(p => p.gameRole === 'Detective').length;
        let needU = Math.max(0, session.undercoverCount - assignedU);
        let needD = Math.max(0, session.detectiveCount - assignedD);
        const unassigned = players.filter(p => !p.gameRole);
        const shuffled = [...unassigned].sort(() => Math.random() - 0.5);
        for (const p of shuffled) {
            if (needU > 0) { p.gameRole = 'Undercover'; needU--; }
            else if (needD > 0) { p.gameRole = 'Detective'; needD--; }
            else { p.gameRole = 'Soldier'; }
            resetUndercoverReadiness(p);
            if (p.gameRole === 'Undercover' && session.rolesReleased) ensureUndercoverTaskGrid(p, session);
            if (p.gameRole !== 'Undercover') delete p.taskGrid;
        }
    }
    broadcast?.();
};

// ========== Phase progression ==========
const resolveNextPhaseByMatchOptions = (from: GamePhase, requestedTo: GamePhase): GamePhase => {
    const session = getSession();
    const undercoverEnabled = session.matchOptions?.undercoverModeEnabled !== false;
    if (undercoverEnabled) return requestedTo;
    if (requestedTo === GamePhase.MidGameQA || requestedTo === GamePhase.PostGameAccusation) return GamePhase.Scoreboard;
    if (from === GamePhase.LiveGame) return GamePhase.Scoreboard;
    return requestedTo;
};

const advancePhase = (from: GamePhase, to: GamePhase, triggeredBy?: string) => {
    let nextTo = resolveNextPhaseByMatchOptions(from, to);
    const session = getSession();
    if (session.phase !== from) return;
    if (from === GamePhase.Lobby && isDuelMode()) {
        if (!setupDuelFromLobby()) return;
        nextTo = GamePhase.PreGameSetup;
    }
    if (!canTransition(from, nextTo)) return;

    if (from === GamePhase.Roll) {
        if (session.rollValues.A === null) session.rollValues.A = Math.floor(Math.random() * 100) + 1;
        if (session.rollValues.B === null) session.rollValues.B = Math.floor(Math.random() * 100) + 1;
        broadcast?.();
        if (session.rollTimeout) clearTimeout(session.rollTimeout);
        session.rollTimeout = setTimeout(() => {
            session.phase = GamePhase.PlayerDraft;
            performPhaseTransition(GamePhase.PlayerDraft);
        }, 3000);
        return;
    }

    if (from === GamePhase.PlayerDraft) {
        clearDraftPickTimer();
        session.draftCaptainsActive = false;
        session.draftPickTimeoutAt = null;
        while (session.draftIndex < session.draftOrder.length) {
            const currentTeam = session.draftOrder[session.draftIndex];
            const available = getGamePlayers(session).filter(
                p => !p.rosterTeam && p.playerId !== session.captains.A && p.playerId !== session.captains.B
            );
            if (available.length === 0) break;
            const picked = available[Math.floor(Math.random() * available.length)];
            assignPlayerToRosterFlow(picked.playerId, currentTeam);
            session.draftIndex++;
        }
        broadcast?.();
    }

    if (from === GamePhase.MapBan) {
        clearMapVoteTimer();
        if (session.mapVote) {
            session.mapVote = undefined;
            session.currentBanTeam = null;
            session.timerEndAt = null;
            session.timerPhase = null;
        }
        if (!session.selectedMap) {
            const remaining = getAvailableMaps();
            if (remaining.length > 0) {
                session.selectedMap = remaining[Math.floor(Math.random() * remaining.length)];
            }
        }
    }

    if (from === GamePhase.SidePick) {
        clearSideVoteTimer();
        session.sideVote = undefined;
    }

    if (from === GamePhase.MidGameQA) {
        session.currentQuestion = null;
        session.questionAnswer = null;
        session.questionsUsed = 0;
        broadcast?.();
    }

    session.phase = nextTo;
    performPhaseTransition(nextTo);
};

const performPhaseTransition = (to: GamePhase) => {
    const session = getSession();
    switch (to) {
        case GamePhase.CaptainSelection:
            const gamePlayers = getGamePlayers(session);
            if (gamePlayers.length >= 2) { randomizeCaptainForTeam('A'); randomizeCaptainForTeam('B'); }
            else if (gamePlayers.length === 1) { session.captains.A = gamePlayers[0].playerId; session.captains.B = null; }
            else { session.captains.A = null; session.captains.B = null; }
            break;
        case GamePhase.Roll:
            session.rollValues = { A: null, B: null };
            break;
        case GamePhase.PlayerDraft:
            const totalGamePlayers = getGamePlayers(session).length;
            const totalPicks = Math.max(0, totalGamePlayers - 2);
            session.draftOrder = [];
            const rollA = session.rollValues.A || 0;
            const rollB = session.rollValues.B || 0;
            const firstTeam: RosterTeam = rollB > rollA ? 'B' : 'A';
            const secondTeam: RosterTeam = firstTeam === 'A' ? 'B' : 'A';
            const pickSequence: RosterTeam[] = [firstTeam, secondTeam, secondTeam, firstTeam];
            for (let i = 0; i < totalPicks; i++) session.draftOrder.push(pickSequence[i % 4]);
            session.draftOriginalOrder = [...session.draftOrder];
            session.draftIndex = 0;
            session.draftCaptainsActive = false;
            session.draftPickTimeoutAt = null;
            session.teams.A.players = [session.captains.A!].filter(Boolean);
            session.teams.B.players = [session.captains.B!].filter(Boolean);
            const pA = findPlayerById(session, session.captains.A!); if (pA) { pA.rosterTeam = 'A'; pA.team = undefined; }
            const pB = findPlayerById(session, session.captains.B!); if (pB) { pB.rosterTeam = 'B'; pB.team = undefined; }
            const baseOrder: RosterTeam[] = [firstTeam, secondTeam];
            const banSequence: RosterTeam[] = [];
            while (banSequence.length < session.mapPool.length - 1) { banSequence.push(...baseOrder); }
            banSequence.length = session.mapPool.length - 1;
            session.banSequence = banSequence;
            session.timerEndAt = null;
            session.timerPhase = null;
            if (isDraftComplete()) scheduleDraftToMapBan();
            break;
        case GamePhase.MapBan:
            session.bannedMaps = [];
            session.selectedMap = null;
            startMapVoteFunc(session.banSequence[0]);
            break;
        case GamePhase.SidePick:
            startSideVoteFunc(session.sidePickTeam || 'A');
            break;
        case GamePhase.PreGameSetup:
            if (session.selectedSide) setRosterLiveSides(session.selectedSide);
            const undercoverEnabled = session.matchOptions?.undercoverModeEnabled !== false;
            if (isDuelMode()) {
                session.undercoverCount = 0;
                session.detectiveCount = 0;
                session.rolesReleased = true;
                Object.values(session.players).forEach(p => {
                    if (p.role !== 'Spectator' && p.role !== 'Admin') p.gameRole = p.rosterTeam ? 'Soldier' : undefined;
                    delete p.taskGrid;
                    p.undercoverTaskAckStage = undefined;
                    p.detectiveQuestionCount = 0;
                    p.abandonCount = 0;
                    p.replaceCount = 0;
                    p.hintUsedCount = 0;
                });
            } else if (undercoverEnabled) {
                session.rolesReleased = false;
                Object.values(session.players).forEach(p => {
                    p.gameRole = undefined;
                    p.isReady = false;
                    p.undercoverTaskAckStage = undefined;
                    delete p.taskGrid;
                });
            } else {
                session.undercoverCount = 0;
                session.detectiveCount = 0;
                session.rolesReleased = true;
                session.questionsUsed = 0;
                session.currentQuestion = null;
                session.questionAnswer = null;
                session.secondQuestionAnswered = false;
                session.accusations = {};
                Object.values(session.players).forEach(p => {
                    if (p.role !== 'Spectator' && p.role !== 'Admin') p.gameRole = 'Soldier';
                    delete p.taskGrid;
                    p.undercoverTaskAckStage = undefined;
                    p.detectiveQuestionCount = 0;
                    p.abandonCount = 0;
                    p.replaceCount = 0;
                    p.hintUsedCount = 0;
                });
            }
            break;
        case GamePhase.LiveGame:
            session.matchId = uuidv4();
            session.rolesReleased = true;
            const ue = session.matchOptions?.undercoverModeEnabled !== false;
            if (ue) {
                const unassigned = getGamePlayers(session).filter(p => !p.gameRole);
                if (unassigned.length > 0) randomRemainingRoles();
            } else {
                for (const player of getGamePlayers(session)) {
                    player.gameRole = 'Soldier';
                    delete player.taskGrid;
                    player.detectiveQuestionCount = 0;
                }
                session.undercoverCount = 0;
                session.detectiveCount = 0;
            }
            Object.values(session.players).forEach(p => {
                p.stats = createEmptyMatchStats();
                p.sideStats = createEmptySideStats();
                p.finalScore = undefined;
                p.scoreBreakdown = undefined;
                p.detectiveQuestionCount = ue && p.gameRole === 'Detective' ? 0 : undefined;
                if (ue && p.gameRole === 'Undercover') ensureUndercoverTaskGrid(p, session);
                if (!ue || p.gameRole !== 'Undercover') delete p.taskGrid;
            });
            session.liveGameData = createEmptyLiveGameData();
            if (isDuelMode()) {
                session.liveGameData.winTarget = getDuelTotalRounds(session.matchOptions?.duelRounds);
                session.liveGameData.duelWaitingForPlayers = true;
                session.liveGameData.duelWaitSeconds = DUEL_READY_WAIT_SECONDS;
                session.timerEndAt = Date.now() + DUEL_READY_WAIT_SECONDS * 1000;
                session.timerPhase = GamePhase.LiveGame;
                queueDuelMapOnlySetup();
                const matchId = session.matchId;
                setTimeout(() => beginDuelFormalMatch(matchId), DUEL_READY_WAIT_SECONDS * 1000);
                notifyMessage?.(`单挑准备等待 ${DUEL_READY_WAIT_SECONDS} 秒：玩家可以在这段时间进出服务器。时间到后双方都在才正式重启开赛。`);
            }
            break;
        case GamePhase.MidGameQA:
            advancePhase(GamePhase.MidGameQA, GamePhase.PostGameAccusation);
            return;
        case GamePhase.PostGameAccusation:
            const undCov = session.matchOptions?.undercoverModeEnabled !== false;
            if (!undCov) {
                calculateScores(session);
                session.phase = GamePhase.Scoreboard;
                session.timerEndAt = null;
                session.timerPhase = null;
                broadcast?.();
                return;
            }
            const newAccusations: Record<string, { own: string | null; enemy: string | null }> = {};
            for (const p of getGamePlayers(session)) newAccusations[p.playerId] = { own: null, enemy: null };
            session.accusations = newAccusations;
            break;
        case GamePhase.Scoreboard:
            calculateScores(session);
            break;
    }

    let timerEnd: number | null = null;
    let timerPhase: GamePhase | null = null;
    if (to === GamePhase.PlayerDraft && session.draftPickTimeoutAt) { timerEnd = session.draftPickTimeoutAt; timerPhase = GamePhase.PlayerDraft; }
    else if (to === GamePhase.MapBan && session.mapVote?.timeoutAt) { timerEnd = session.mapVote.timeoutAt; timerPhase = GamePhase.MapBan; }
    else if (to === GamePhase.SidePick && session.sideVote?.timeoutAt) { timerEnd = session.sideVote.timeoutAt; timerPhase = GamePhase.SidePick; }
    session.timerEndAt = timerEnd;
    session.timerPhase = timerPhase;

    broadcast?.();
};

// ========== Match options ==========
const clearUndercoverModeState = () => {
    const session = getSession();
    session.undercoverCount = 0;
    session.detectiveCount = 0;
    session.rolesReleased = false;
    session.questionsUsed = 0;
    session.currentQuestion = null;
    session.questionAnswer = null;
    session.secondQuestionAnswered = false;
    session.accusations = {};
    for (const player of getGamePlayers(session)) {
        player.gameRole = 'Soldier';
        delete player.taskGrid;
        player.undercoverTaskAckStage = undefined;
        player.abandonCount = 0;
        player.replaceCount = 0;
        player.hintUsedCount = 0;
        player.detectiveQuestionCount = 0;
    }
};

const forceSkipUndercoverOnlyPhaseIfNeeded = () => {
    const session = getSession();
    if (session.matchOptions?.undercoverModeEnabled !== false) return;
    if (![GamePhase.MidGameQA, GamePhase.PostGameAccusation].includes(session.phase)) return;
    try { calculateScores(session); } catch (err) { console.error('[MatchOptions] calculateScores failed:', err); }
    session.phase = GamePhase.Scoreboard;
    session.timerEndAt = null;
    session.timerPhase = null;
};

const applyMatchOptions = (rawOptions: unknown) => {
    const session = getSession();
    session.matchOptions = {
        matchMode: (rawOptions as any)?.matchMode === 'duel' ? 'duel' : 'competitive',
        undercoverModeEnabled: (rawOptions as any)?.undercoverModeEnabled !== false,
        caorenModifiersEnabled: (rawOptions as any)?.caorenModifiersEnabled === true,
        duelMap: resolveDuelMapConfig((rawOptions as any)?.duelMap || DUEL_DEFAULT_MAP, (rawOptions as any)?.duelMapWorkshopId).name,
        duelMapWorkshopId: resolveDuelMapConfig((rawOptions as any)?.duelMap || DUEL_DEFAULT_MAP, (rawOptions as any)?.duelMapWorkshopId).workshopId,
        duelRoundTimeMinutes: normalizeDuelRoundTimeMinutes((rawOptions as any)?.duelRoundTimeMinutes || DUEL_DEFAULT_ROUND_TIME_MINUTES),
        duelRounds: normalizeDuelRounds((rawOptions as any)?.duelRounds),
        duelUtilityMode: normalizeDuelUtilityMode((rawOptions as any)?.duelUtilityMode),
    };
    if (session.matchOptions.matchMode === 'duel') {
        session.matchOptions.undercoverModeEnabled = false;
        clearUndercoverModeState();
        return session.matchOptions;
    }
    if (!session.matchOptions.undercoverModeEnabled) {
        clearUndercoverModeState();
        forceSkipUndercoverOnlyPhaseIfNeeded();
    }
    return session.matchOptions;
};

// ========== Unified exports ==========
export {
    // Flow operations
    advancePhase,
    performPhaseTransition,
    // Draft
    applyDraftPick,
    finishDraftPick,
    startDraftPickTimerFunc as startDraftPickTimer,
    // Map ban
    finishMapVote,
    startMapVoteFunc as startMapVote,
    getAvailableMaps,
    // Side pick
    finishSideVote,
    startSideVoteFunc as startSideVote,
    setRosterLiveSides,
    // Roles
    randomRemainingRoles,
    // LiveGame
    normalizePluginRound,
    getScoreDerivedCurrentRound,
    syncCurrentRoundFromScores,
    updateMatchFinishState,
    resolveRosterTeamByInitialSide,
    resetFormalMatchCounters,
    extendDuelWaitingIfLateJoin,
    // Match options
    applyMatchOptions,
    clearUndercoverModeState,
    forceSkipUndercoverOnlyPhaseIfNeeded,
    prepareReleasedRoleState,
    // Cleanup
    assignPlayerToRosterFlow as assignPlayerToRoster,
    removePlayerFromRosterTeams,
    getAvailableDraftPlayers,
    isDraftComplete,
    syncPendingDraftOrderWithRoster,
    startCaptainDraftFunc as startCaptainDraft,
};
