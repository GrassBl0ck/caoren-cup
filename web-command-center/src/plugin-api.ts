// plugin-api.ts
import express from 'express';
import { GamePhase, WsEvents, LiveGameData } from './types';
import { getSession } from './session-manager';
import {
    getGamePlayers,
    normalizeSteamId,
    normalizeTeam,
    findPlayerBySteamId,
} from './player-utils';
import {
    enqueuePluginCommand,
    takeQueuedPluginCommands,
    ackPluginCommand,
} from './plugin-command-queue';
import {
    normalizePluginRound,
    updateMatchFinishState,
    resolveRosterTeamByInitialSide,
} from './game-flow-manager';
import { ADMIN_PASSWORD, PLUGIN_TOKEN } from './game-constants';

type TeamAssignmentSide = 'CT' | 'T';

interface TeamAssignment {
    steamId: string;
    name: string;
    rosterTeam: 'A' | 'B';
    side: TeamAssignmentSide;
}

interface TeamAssignmentBuildResult {
    round: number;
    halftimeSwapped: boolean;
    teamASide: TeamAssignmentSide;
    assignments: TeamAssignment[];
    missingSteamIds: Array<{ playerId: string; name: string; rosterTeam: string }>;
    unassignedPlayers: Array<{ playerId: string; name: string }>;
}

const isPluginLivePhase = (phase: GamePhase) =>
    [GamePhase.LiveGame, GamePhase.MidGameQA].includes(phase);

const isPluginStatsLocked = (session: any): boolean =>
    session.phase === GamePhase.PostGameAccusation ||
    session.phase === GamePhase.Scoreboard ||
    session.liveGameData?.matchFinished === true ||
    session.liveGameData?.statsLocked === true;

const createEmptyLiveGameData = (): LiveGameData => ({
    scoreCT: 0, scoreT: 0, scoreA: 0, scoreB: 0,
    currentRound: 0, pluginConnected: false, winnerTeam: null, matchFinished: false,
    winTarget: 13, lastScoredRound: 0, rawPluginRound: 0, roundBaseOffset: undefined,
    formalStatsStarted: false, statsLocked: false,
    killMatrix: {}, openingKillMatrix: {}, awpKillMatrix: {}, firstKillRounds: {},
});

const oppositeSide = (side: TeamAssignmentSide): TeamAssignmentSide => side === 'CT' ? 'T' : 'CT';

const getFormalRound = (session: any): number => {
    const live = session.liveGameData || {};
    return Math.max(0, Math.floor(Number(live.currentRound || 0)));
};

const getTeamASideForRound = (selectedSide: unknown, round: number): TeamAssignmentSide | null => {
    if (selectedSide !== 'CT' && selectedSide !== 'T') return null;
    const initial = selectedSide as TeamAssignmentSide;
    return round >= 13 ? oppositeSide(initial) : initial;
};

const buildTeamAssignments = (session: any): TeamAssignmentBuildResult => {
    const round = getFormalRound(session);
    const teamASide = getTeamASideForRound(session.selectedSide, round);
    if (!teamASide) {
        throw new Error('???????????????????');
    }

    const assignments: TeamAssignment[] = [];
    const missingSteamIds: TeamAssignmentBuildResult['missingSteamIds'] = [];
    const unassignedPlayers: TeamAssignmentBuildResult['unassignedPlayers'] = [];

    for (const player of getGamePlayers(session)) {
        if (player.role === 'Admin' || player.role === 'Spectator') continue;
        if (player.rosterTeam !== 'A' && player.rosterTeam !== 'B') {
            unassignedPlayers.push({ playerId: player.playerId, name: player.name });
            continue;
        }

        const steamId = normalizeSteamId(player.steamId);
        if (!steamId) {
            missingSteamIds.push({ playerId: player.playerId, name: player.name, rosterTeam: player.rosterTeam });
            continue;
        }

        assignments.push({
            steamId,
            name: player.name,
            rosterTeam: player.rosterTeam,
            side: player.rosterTeam === 'A' ? teamASide : oppositeSide(teamASide),
        });
    }

    return {
        round,
        halftimeSwapped: round >= 13,
        teamASide,
        assignments,
        missingSteamIds,
        unassignedPlayers,
    };
};

const enqueueTeamAssignments = (
    session: any,
    reason: string,
    lockTeams = true
): TeamAssignmentBuildResult & { commandId: string; queuedAt: number; reason: string } => {
    const built = buildTeamAssignments(session);
    if (built.assignments.length === 0) {
        throw new Error('????????? A/B ????');
    }

    const queued = enqueuePluginCommand('APPLY_TEAM_ASSIGNMENTS', {
        matchId: session.matchId,
        round: built.round,
        lockTeams,
        reason,
        halftimeSwapped: built.halftimeSwapped,
        teamASide: built.teamASide,
        assignments: built.assignments,
        requestedAt: Date.now(),
        label: built.halftimeSwapped ? '?????????' : '??????????',
    });

    if (!session.liveGameData) session.liveGameData = createEmptyLiveGameData();
    session.liveGameData.teamLockEnabled = lockTeams;
    session.liveGameData.teamLockLastSyncedAt = Date.now();
    session.liveGameData.teamLockLastRound = built.round;
    session.liveGameData.teamLockLastCommandId = queued.id;
    session.liveGameData.teamLockAssignments = built.assignments;
    session.liveGameData.teamLockMissingSteamIds = built.missingSteamIds;
    session.liveGameData.teamLockUnassignedPlayers = built.unassignedPlayers;
    session.liveGameData.teamLockHalftimeSwapped = built.halftimeSwapped;

    return {
        ...built,
        commandId: queued.id,
        queuedAt: queued.createdAt,
        reason,
    };
};

const createEmptyMatchStats = () => ({
    roundsPlayed: 0,
    kastRounds: 0,
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
const requirePluginAuth = (req: any, res: any, next: any) => {
    const token = req.header('x-caoren-plugin-token') || req.query?.token;
    if (!PLUGIN_TOKEN || token !== PLUGIN_TOKEN) return res.status(401).json({ success: false, error: '??????' });
    next();
};

export function registerPluginRoutes(app: express.Express, deps: {
    broadcastState: () => void;
    notifyMessage: (msg: string) => void;
}) {
    const { broadcastState, notifyMessage } = deps;

    app.get('/api/plugin/state', requirePluginAuth, (req, res) => {
        const session = getSession();
        res.json({
            success: true,
            sessionId: session.sessionId,
            matchId: session.matchId,
            phase: session.phase,
            selectedMap: session.selectedMap,
            selectedSide: session.selectedSide,
            liveGameData: session.liveGameData,
            players: getGamePlayers(session).map(p => ({
                playerId: p.playerId,
                name: p.name,
                steamId: p.steamId,
                rosterTeam: p.rosterTeam,
                team: p.team,
                gameRole: p.gameRole,
                isReady: p.isReady,
                undercoverTaskAckStage: p.undercoverTaskAckStage,
            })),
        });
    });

    app.post('/api/plugin/heartbeat', requirePluginAuth, (req, res) => {
        const session = getSession();
        if (!session.liveGameData) session.liveGameData = createEmptyLiveGameData();
        session.liveGameData.pluginConnected = true;
        session.liveGameData.lastPluginHeartbeatAt = Date.now();
        if (req.body?.mapName) session.liveGameData.mapName = String(req.body.mapName);
        broadcastState();
        res.json({
            success: true,
            matchId: session.matchId,
            phase: session.phase,
            commands: takeQueuedPluginCommands(),
        });
    });

    app.post('/api/plugin/command-ack', requirePluginAuth, (req, res) => {
        const ok = ackPluginCommand(req.body?.commandId);
        if (!ok) return res.status(404).json({ success: false, error: '???????????' });
        res.json({ success: true });
    });

    app.post('/api/admin/team-lock/sync', (req, res) => {
        const adminPassword = String(req.body?.adminPassword || '');
        if (!ADMIN_PASSWORD || adminPassword !== ADMIN_PASSWORD) {
            return res.status(401).json({ success: false, error: '???????' });
        }

        const session = getSession();
        if (!session.liveGameData) session.liveGameData = createEmptyLiveGameData();
        try {
            const result = enqueueTeamAssignments(session, 'manual-sync', true);
            notifyMessage(`????????????${result.assignments.length} ???????`);
            broadcastState();
            res.json({ success: true, ...result });
        } catch (err) {
            const message = err instanceof Error ? err.message : '??????';
            res.status(400).json({ success: false, error: message });
        }
    });

    app.post('/api/admin/team-lock/clear', (req, res) => {
        const adminPassword = String(req.body?.adminPassword || '');
        if (!ADMIN_PASSWORD || adminPassword !== ADMIN_PASSWORD) {
            return res.status(401).json({ success: false, error: '???????' });
        }

        const session = getSession();
        if (!session.liveGameData) session.liveGameData = createEmptyLiveGameData();
        const queued = enqueuePluginCommand('CLEAR_TEAM_ASSIGNMENTS', {
            matchId: session.matchId,
            requestedAt: Date.now(),
            label: '????????',
        });
        session.liveGameData.teamLockEnabled = false;
        session.liveGameData.teamLockAssignments = [];
        session.liveGameData.teamLockLastCommandId = queued.id;
        notifyMessage('??????????????');
        broadcastState();
        res.json({ success: true, commandId: queued.id, queuedAt: queued.createdAt });
    });

    app.post('/api/plugin/bind', requirePluginAuth, (req, res) => {
        const bindCode = String(req.body?.bindCode || '').trim();
        const steamId = normalizeSteamId(req.body?.steamId);
        const inGameName = String(req.body?.name || '').trim();
        if (!bindCode || !steamId) return res.status(400).json({ success: false, error: 'bindCode ? steamId ??' });
        const session = getSession();
        const player = getGamePlayers(session).find(p => p.bindCode === bindCode);
        if (!player) return res.status(404).json({ success: false, error: '?????????' });
        player.steamId = steamId;
        if (inGameName && !player.name) player.name = inGameName;
        broadcastState();
        res.json({ success: true, playerId: player.playerId, name: player.name, steamId: player.steamId });
    });

    app.post('/api/plugin/snapshot', requirePluginAuth, (req, res) => {
        const session = getSession();
        if (!isPluginLivePhase(session.phase)) return res.json({ success: true, ignored: true, reason: `???? ${session.phase} ???????` });
        if (req.body?.matchId && req.body.matchId !== session.matchId) return res.status(409).json({ success: false, error: 'matchId ???' });
        if (!session.liveGameData) session.liveGameData = createEmptyLiveGameData();
        if (isPluginStatsLocked(session)) return res.json({ success: true, ignored: true, reason: '???????' });
        if (typeof req.body?.currentRound === 'number') session.liveGameData.currentRound = Math.max(session.liveGameData.currentRound || 0, normalizePluginRound(req.body.currentRound));
        if (req.body?.mapName) session.liveGameData.mapName = String(req.body.mapName);
        session.liveGameData.pluginConnected = true;
        session.liveGameData.lastPluginHeartbeatAt = Date.now();
        const suppressStats = Date.now() < Number(session.liveGameData.suppressSnapshotStatsUntil || 0);
        const players = Array.isArray(req.body?.players) ? req.body.players : [];
        updateLivePlayersFromSnapshot(session, players, { updateStats: isFormalStatsStarted(session) && !suppressStats });
        updateMatchFinishState();
        broadcastState();
        res.json({ success: true, matchedPlayers: getGamePlayers(session).filter(p => p.steamId && p.stats).length });
    });

    app.post('/api/plugin/event', requirePluginAuth, (req, res) => {
        const session = getSession();
        if (!isPluginLivePhase(session.phase)) return res.json({ success: true, ignored: true, reason: `???? ${session.phase} ???????` });
        if (req.body?.matchId && req.body.matchId !== session.matchId) return res.status(409).json({ success: false, error: 'matchId ???' });
        if (!session.liveGameData) session.liveGameData = createEmptyLiveGameData();
        if (isPluginStatsLocked(session)) return res.json({ success: true, ignored: true, reason: '???????' });
        session.liveGameData.pluginConnected = true;
        session.liveGameData.lastPluginHeartbeatAt = Date.now();

        const type = String(req.body?.type || '');
        const payload = req.body?.payload || {};
        if (typeof payload.round === 'number') session.liveGameData.currentRound = Math.max(session.liveGameData.currentRound || 0, normalizePluginRound(payload.round));

        if (!isFormalStatsStarted(session)) {
            if (type === 'round_start' && Array.isArray(payload.players)) {
                updateLivePlayersFromSnapshot(session, payload.players, { updateStats: false });
            }
            broadcastState();
            return res.json({ success: true, ignored: true, reason: '????????' });
        }

        switch (type) {
            case 'round_start':
                applyRoundStartEvent(session, payload);
                break;
            case 'player_death':
                applyKillEvent(session, payload);
                break;
            case 'player_hurt':
                applyDamageEvent(session, payload);
                break;
            case 'round_end':
                applyRoundEndEvent(session, payload, notifyMessage);
                break;
            default:
                return res.status(400).json({ success: false, error: `????: ${type}` });
        }
        broadcastState();
        res.json({ success: true });
    });
}

// ??????
function updateLivePlayersFromSnapshot(session: any, players: any[], options: { updateStats: boolean }) {
    for (const raw of players) {
        const steamId = normalizeSteamId(raw.steamId);
        if (!steamId) continue;
        const player = findPlayerBySteamId(session, steamId);
        if (!player) continue;
        const side = normalizeTeam(raw.team);
        if (side) player.team = side;
        if (options.updateStats) {
            const stats = ensurePlayerStats(player);
            stats.kills = Number(raw.kills || 0);
            stats.deaths = Number(raw.deaths || 0);
            stats.assists = Number(raw.assists || 0);
            stats.damage = Number(raw.damage || 0);
        }
    }
}

function isFormalStatsStarted(session: any): boolean {
    return session.liveGameData?.formalStatsStarted === true;
}

function ensurePlayerStats(player: any) {
    if (!player.stats) player.stats = createEmptyMatchStats();
    return player.stats;
}

function ensureSideStats(player: any, side: string | null | undefined) {
    const normalizedSide = normalizeTeam(side);
    if (normalizedSide !== 'CT' && normalizedSide !== 'T') return null;
    if (!player.sideStats) player.sideStats = { CT: createEmptyMatchStats(), T: createEmptyMatchStats() };
    if (!player.sideStats.CT) player.sideStats.CT = createEmptyMatchStats();
    if (!player.sideStats.T) player.sideStats.T = createEmptyMatchStats();
    return player.sideStats[normalizedSide];
}

function incrementStat(stats: any, key: string, amount = 1) {
    if (!stats) return;
    stats[key] = Number(stats[key] || 0) + amount;
}

function sideStatsForSteamId(session: any, steamId: string, side: string | null | undefined) {
    const player = findPlayerBySteamId(session, steamId);
    return player ? ensureSideStats(player, side) : null;
}

function applyRoundStartEvent(session: any, payload: any) {
    const live = session.liveGameData;
    const round = Math.max(1, normalizePluginRound(Number(payload.round || live?.currentRound || 1)));
    if (round) live.currentRound = round;
    const roundStats = getRoundStats(live, round);
    roundStats.aliveBySide = { CT: {}, T: {} };
    roundStats.oneVsRecordedBySteamId = {};
    if (!roundStats.sideBySteamId) roundStats.sideBySteamId = {};
    if (!roundStats.roundStartedSteamIds) roundStats.roundStartedSteamIds = {};
    const players = Array.isArray(payload.players) ? payload.players : [];
    for (const raw of players) {
        const steamId = normalizeSteamId(raw.steamId);
        const side = normalizeTeam(raw.team);
        if (!steamId || (side !== 'CT' && side !== 'T')) continue;
        if (raw.isAlive !== false) roundStats.aliveBySide[side][steamId] = true;
        roundStats.sideBySteamId[steamId] = side;
        const player = findPlayerBySteamId(session, steamId);
        if (player) player.team = side;
        if (player && !roundStats.roundStartedSteamIds[steamId]) {
            roundStats.roundStartedSteamIds[steamId] = true;
            incrementStat(ensurePlayerStats(player), 'roundsPlayed');
            incrementStat(ensureSideStats(player, side), 'roundsPlayed');
        }
    }

    if (
        round === 13 &&
        live.teamLockEnabled === true &&
        live.teamLockLastAutoSideSwapRound !== 13
    ) {
        try {
            const result = enqueueTeamAssignments(session, 'halftime-auto-swap', true);
            live.teamLockLastAutoSideSwapRound = 13;
            live.teamLockLastAutoSideSwapCommandId = result.commandId;
        } catch (err) {
            live.teamLockLastAutoSideSwapError = err instanceof Error ? err.message : '????????';
        }
    }
}

function aliveCount(roundStats: any, side: string | null): number {
    if (side !== 'CT' && side !== 'T') return 0;
    const alive = roundStats.aliveBySide?.[side];
    if (!alive) return 0;
    return Object.keys(alive).filter(id => alive[id]).length;
}

function ensureRoundAliveState(session: any, roundStats: any) {
    if (roundStats.aliveBySide?.CT && roundStats.aliveBySide?.T) return;
    roundStats.aliveBySide = { CT: {}, T: {} };
    for (const player of getGamePlayers(session)) {
        if (player.role === 'Admin') continue;
        const steamId = normalizeSteamId(player.steamId);
        const side = normalizeTeam(player.team);
        if (!steamId || (side !== 'CT' && side !== 'T')) continue;
        roundStats.aliveBySide[side][steamId] = true;
    }
}

const SITUATION_SWING_MATRIX: Record<number, Record<number, number>> = {
    1: { 1: 0.22, 2: 0.24, 3: 0.22, 4: 0.18, 5: 0.14, 6: 0.12 },
    2: { 1: 0.10, 2: 0.16, 3: 0.18, 4: 0.18, 5: 0.15, 6: 0.12 },
    3: { 1: 0.05, 2: 0.12, 3: 0.14, 4: 0.15, 5: 0.14, 6: 0.11 },
    4: { 1: 0.03, 2: 0.08, 3: 0.12, 4: 0.13, 5: 0.13, 6: 0.10 },
    5: { 1: 0.015, 2: 0.05, 3: 0.09, 4: 0.12, 5: 0.12, 6: 0.10 },
    6: { 1: 0.01, 2: 0.04, 3: 0.07, 4: 0.10, 5: 0.11, 6: 0.10 },
};

function situationSwingValue(ownAlive: number, enemyAlive: number): number {
    const own = Math.max(1, Math.min(6, ownAlive));
    const enemy = Math.max(1, Math.min(6, enemyAlive));
    return SITUATION_SWING_MATRIX[own]?.[enemy] ?? 0.08;
}
function recordOneVsXStates(session: any, roundStats: any) {
    const aliveBySide = roundStats.aliveBySide;
    if (!aliveBySide?.CT || !aliveBySide?.T) return;
    const ctAlive = Object.keys(aliveBySide.CT).filter(id => aliveBySide.CT[id]);
    const tAlive = Object.keys(aliveBySide.T).filter(id => aliveBySide.T[id]);
    const candidates = [
        { side: 'CT', alive: ctAlive, enemyCount: tAlive.length },
        { side: 'T', alive: tAlive, enemyCount: ctAlive.length },
    ];
    for (const candidate of candidates) {
        if (candidate.alive.length !== 1 || candidate.enemyCount < 1) continue;
        const x = Math.min(6, candidate.enemyCount);
        const steamId = candidate.alive[0];
        const key = `${steamId}:v${x}`;
        if (!roundStats.oneVsRecordedBySteamId) roundStats.oneVsRecordedBySteamId = {};
        if (roundStats.oneVsRecordedBySteamId[key]) continue;
        roundStats.oneVsRecordedBySteamId[key] = { steamId, side: candidate.side, x };
        const player = findPlayerBySteamId(session, steamId);
        if (!player) continue;
        incrementStat(ensurePlayerStats(player), `v${x}Count`);
        incrementStat(ensureSideStats(player, candidate.side), `v${x}Count`);
    }
}

function finalizeOneVsXWins(session: any, roundStats: any, winnerSide: string | null) {
    if (winnerSide !== 'CT' && winnerSide !== 'T') return;
    for (const record of Object.values(roundStats.oneVsRecordedBySteamId || {}) as any[]) {
        if (record.side !== winnerSide) continue;
        const player = findPlayerBySteamId(session, record.steamId);
        if (!player) continue;
        incrementStat(ensurePlayerStats(player), `v${record.x}Wins`);
        incrementStat(ensureSideStats(player, record.side), `v${record.x}Wins`);
    }
}

function getRoundStats(live: any, round: number) {
    if (!live.roundStats) live.roundStats = {};
    if (!live.roundStats[round]) live.roundStats[round] = {
        killCountBySteamId: {},
        firstDeathSeen: false,
        firstDeathVictimSeen: false,
        openingKillRecorded: false,
    };
    return live.roundStats[round];
}

function markKastContributor(roundStats: any, steamId: string | null | undefined) {
    const normalized = normalizeSteamId(steamId);
    if (!normalized) return;
    if (!roundStats.kastContributors) roundStats.kastContributors = {};
    roundStats.kastContributors[normalized] = true;
}

function finalizeKastRounds(session: any, roundStats: any) {
    if (!roundStats || roundStats.kastFinalized) return;
    roundStats.kastFinalized = true;
    for (const steamId of Object.keys(roundStats.roundStartedSteamIds || {})) {
        const side = roundStats.sideBySteamId?.[steamId] || (roundStats.aliveBySide?.CT?.[steamId] !== undefined ? 'CT' : (roundStats.aliveBySide?.T?.[steamId] !== undefined ? 'T' : undefined));
        const survived = side === 'CT' || side === 'T' ? roundStats.aliveBySide?.[side]?.[steamId] === true : false;
        if (!survived && !roundStats.kastContributors?.[steamId]) continue;
        const player = findPlayerBySteamId(session, steamId);
        if (!player) continue;
        incrementStat(ensurePlayerStats(player), 'kastRounds');
        incrementStat(ensureSideStats(player, side), 'kastRounds');
    }
}

function recordKillMatrix(live: any, matrixKey: string, attackerSteamId: string | null | undefined, victimSteamId: string | null | undefined) {
    const attackerId = normalizeSteamId(attackerSteamId);
    const victimId = normalizeSteamId(victimSteamId);
    if (!attackerId || !victimId || attackerId === victimId) return;
    if (!live[matrixKey]) live[matrixKey] = {};
    if (!live[matrixKey][attackerId]) live[matrixKey][attackerId] = {};
    live[matrixKey][attackerId][victimId] = Number(live[matrixKey][attackerId][victimId] || 0) + 1;
}

function shouldRecordOpponentMatrixKill(attacker: any, victim: any): boolean {
    if (!attacker || !victim || attacker.playerId === victim.playerId) return false;
    if (attacker.rosterTeam !== 'A' && attacker.rosterTeam !== 'B') return false;
    if (victim.rosterTeam !== 'A' && victim.rosterTeam !== 'B') return false;
    return attacker.rosterTeam !== victim.rosterTeam;
}

function markTradedDeaths(session: any, roundStats: any, payload: any, now: number) {
    const killedSteamId = normalizeSteamId(payload.victimSteamId);
    if (!killedSteamId) return;
    const pendingTrades = roundStats.pendingTradesByKillerSteamId?.[killedSteamId] || [];
    const tradeIndex = pendingTrades.findIndex((trade: any) => trade.expiresAt >= now && trade.victimSide && trade.victimSide === normalizeTeam(payload.attackerTeam));
    if (tradeIndex < 0) return;
    const [trade] = pendingTrades.splice(tradeIndex, 1);
    const tradedVictim = findPlayerBySteamId(session, trade.victimSteamId);
    if (!tradedVictim) return;
    incrementStat(ensurePlayerStats(tradedVictim), 'tradedDeaths');
    incrementStat(ensureSideStats(tradedVictim, trade.victimSide), 'tradedDeaths');
    markKastContributor(roundStats, trade.victimSteamId);
}

function addPendingTrade(roundStats: any, payload: any, now: number) {
    const attackerSteamId = normalizeSteamId(payload.attackerSteamId);
    const victimSteamId = normalizeSteamId(payload.victimSteamId);
    const victimSide = normalizeTeam(payload.victimTeam);
    if (!attackerSteamId || !victimSteamId || !victimSide || attackerSteamId === victimSteamId) return;
    if (!roundStats.pendingTradesByKillerSteamId) roundStats.pendingTradesByKillerSteamId = {};
    if (!roundStats.pendingTradesByKillerSteamId[attackerSteamId]) roundStats.pendingTradesByKillerSteamId[attackerSteamId] = [];
    roundStats.pendingTradesByKillerSteamId[attackerSteamId].push({
        victimSteamId,
        victimSide,
        expiresAt: now + 5000,
    });
}

function normalizeWeaponName(rawWeapon: any): string {
    return String(rawWeapon || '').toLowerCase().replace(/^weapon_/, '');
}

function primaryWeaponValue(rawWeapon: any): number {
    const weapon = normalizeWeaponName(rawWeapon);
    const values: Record<string, number> = {
        galilar: 0.72,
        famas: 0.82,
        ak47: 1.0,
        m4a1: 1.0,
        m4a1_silencer: 0.98,
        sg556: 1.04,
        aug: 1.04,
        ssg08: 0.66,
        awp: 1.12,
        g3sg1: 1.08,
        scar20: 1.08,
        mac10: 0.46,
        mp9: 0.50,
        mp7: 0.58,
        mp5sd: 0.58,
        ump45: 0.52,
        p90: 0.70,
        bizon: 0.42,
        nova: 0.38,
        sawedoff: 0.36,
        mag7: 0.52,
        xm1014: 0.64,
        m249: 1.05,
        negev: 0.50,
    };
    return values[weapon] || 0;
}

function fallbackWeaponValue(rawWeapon: any): number {
    const weapon = normalizeWeaponName(rawWeapon);
    const values: Record<string, number> = {
        glock: 0.20,
        hkp2000: 0.20,
        usp_silencer: 0.20,
        p250: 0.26,
        tec9: 0.32,
        fiveseven: 0.34,
        cz75a: 0.34,
        elite: 0.30,
        deagle: 0.46,
        revolver: 0.44,
        taser: 0.22,
    };
    if (values[weapon] !== undefined) return values[weapon];
    if (weapon.includes('knife')) return 0.10;
    return 0.16;
}

function armorValue(rawEquipment: any): number {
    const armor = Number(rawEquipment?.armor || 0);
    if (armor <= 0) return 0;
    return rawEquipment?.hasHelmet ? 0.18 : 0.11;
}

function equipmentValue(rawEquipment: any, fallbackWeapon: any): number {
    const weapons = Array.isArray(rawEquipment?.weapons) ? rawEquipment.weapons : [];
    let weaponScore = 0;
    for (const weapon of weapons) weaponScore = Math.max(weaponScore, primaryWeaponValue(weapon));
    if (weaponScore <= 0) weaponScore = fallbackWeaponValue(rawEquipment?.activeWeapon || fallbackWeapon);
    return weaponScore + armorValue(rawEquipment);
}
function equipmentSwingDelta(payload: any): number {
    const attackerValue = equipmentValue(payload.attackerEquipment, payload.weapon);
    const victimValue = equipmentValue(payload.victimEquipment, null);
    const diff = Math.max(-0.7, Math.min(0.7, victimValue - attackerValue));
    return diff * 0.08;
}

function applyKillEvent(session: any, payload: any) {
    const now = Date.now();
    const live = session.liveGameData;
    const round = Math.max(1, normalizePluginRound(Number(payload.round || live?.currentRound || 1)));
    const roundStats = getRoundStats(live, round);
    const attacker = findPlayerBySteamId(session, payload.attackerSteamId);
    const victim = findPlayerBySteamId(session, payload.victimSteamId);
    const assister = findPlayerBySteamId(session, payload.assisterSteamId);
    const attackerSide = normalizeTeam(payload.attackerTeam);
    const victimSide = normalizeTeam(payload.victimTeam);
    if (attacker && attackerSide) attacker.team = attackerSide;
    if (victim && victimSide) victim.team = victimSide;
    ensureRoundAliveState(session, roundStats);
    const attackerSideStats = attacker ? ensureSideStats(attacker, attackerSide) : null;
    const victimSideStats = victim ? ensureSideStats(victim, victimSide) : null;
    const assisterSideStats = assister ? ensureSideStats(assister, attackerSide) : null;
    const ownAliveBeforeKill = aliveCount(roundStats, attackerSide);
    const enemyAliveBeforeKill = aliveCount(roundStats, victimSide);

    if (attacker && attacker.playerId !== victim?.playerId) {
        markTradedDeaths(session, roundStats, payload, now);
        const attackerStats = ensurePlayerStats(attacker);
        incrementStat(attackerStats, 'kills');
        incrementStat(attackerSideStats, 'kills');
        const situationDelta = situationSwingValue(ownAliveBeforeKill, enemyAliveBeforeKill);
        incrementStat(attackerStats, 'situationSwing', situationDelta);
        incrementStat(attackerSideStats, 'situationSwing', situationDelta);
        const equipmentDelta = equipmentSwingDelta(payload);
        incrementStat(attackerStats, 'equipmentSwing', equipmentDelta);
        incrementStat(attackerSideStats, 'equipmentSwing', equipmentDelta);
        if (payload.headshot) {
            incrementStat(attackerStats, 'headShotKills');
            incrementStat(attackerSideStats, 'headShotKills');
        }
        const attackerSteamId = normalizeSteamId(payload.attackerSteamId);
        if (attackerSteamId) {
            const previousKills = Number(roundStats.killCountBySteamId[attackerSteamId] || 0);
            roundStats.killCountBySteamId[attackerSteamId] = previousKills + 1;
            if (previousKills === 1) {
                incrementStat(attackerStats, 'enemy2ks');
                incrementStat(attackerSideStats, 'enemy2ks');
            } else if (previousKills === 2) {
                incrementStat(attackerStats, 'enemy3ks');
                incrementStat(attackerSideStats, 'enemy3ks');
            } else if (previousKills === 3) {
                incrementStat(attackerStats, 'enemy4ks');
                incrementStat(attackerSideStats, 'enemy4ks');
            } else if (previousKills === 4) {
                incrementStat(attackerStats, 'enemy5ks');
                incrementStat(attackerSideStats, 'enemy5ks');
            }
            if (!roundStats.firstDeathSeen) {
                roundStats.firstDeathSeen = true;
                incrementStat(attackerStats, 'entryCount');
                incrementStat(attackerStats, 'entryWins');
                incrementStat(attackerSideStats, 'entryCount');
                incrementStat(attackerSideStats, 'entryWins');
            }
        }
        markKastContributor(roundStats, payload.attackerSteamId);
        const isOpponentKill = shouldRecordOpponentMatrixKill(attacker, victim);
        if (isOpponentKill) recordKillMatrix(live, 'killMatrix', payload.attackerSteamId, payload.victimSteamId);
        if (isOpponentKill && !roundStats.openingKillRecorded) {
            roundStats.openingKillRecorded = true;
            recordKillMatrix(live, 'openingKillMatrix', payload.attackerSteamId, payload.victimSteamId);
        }
        if (isOpponentKill && String(payload.weapon || '').toLowerCase().includes('awp')) {
            recordKillMatrix(live, 'awpKillMatrix', payload.attackerSteamId, payload.victimSteamId);
        }
    }
    if (victim) {
        const victimStats = ensurePlayerStats(victim);
        incrementStat(victimStats, 'deaths');
        incrementStat(victimSideStats, 'deaths');
        const victimSituationDelta = situationSwingValue(enemyAliveBeforeKill, ownAliveBeforeKill) * 0.7;
        incrementStat(victimStats, 'situationSwing', -victimSituationDelta);
        incrementStat(victimSideStats, 'situationSwing', -victimSituationDelta);
        const victimSteamId = normalizeSteamId(payload.victimSteamId);
        if (victimSteamId && victimSide && roundStats.aliveBySide?.[victimSide]) {
            roundStats.aliveBySide[victimSide][victimSteamId] = false;
        }
        if (!roundStats.firstDeathVictimSeen) {
            roundStats.firstDeathVictimSeen = true;
            incrementStat(victimStats, 'entryCount');
            incrementStat(victimSideStats, 'entryCount');
        }
    }
    if (attacker && victim && attacker.playerId !== victim.playerId) {
        addPendingTrade(roundStats, payload, now);
    }
    recordOneVsXStates(session, roundStats);
    if (assister && assister.playerId !== attacker?.playerId && assister.playerId !== victim?.playerId) {
        const assisterStats = ensurePlayerStats(assister);
        incrementStat(assisterStats, 'assists');
        incrementStat(assisterSideStats, 'assists');
        markKastContributor(roundStats, payload.assisterSteamId);
    }
}

function applyDamageEvent(session: any, payload: any) {
    const attacker = findPlayerBySteamId(session, payload.attackerSteamId);
    const victim = findPlayerBySteamId(session, payload.victimSteamId);
    const damage = Math.max(0, Number(payload.damage || 0));
    if (!attacker || damage <= 0 || attacker.playerId === victim?.playerId) return;
    const attackerStats = ensurePlayerStats(attacker);
    const attackerSideStats = ensureSideStats(attacker, payload.attackerTeam);
    incrementStat(attackerStats, 'damage', damage);
    incrementStat(attackerSideStats, 'damage', damage);
}
function applyRoundEndEvent(session: any, payload: any, notify: (msg: string) => void) {
    const live = session.liveGameData;
    const formalRound = typeof payload.round === 'number' ? normalizePluginRound(payload.round) : live?.currentRound || 0;
    if (formalRound) live.currentRound = formalRound;
    const winnerSide = normalizeTeam(payload.winner);
    const alreadyProcessed = !!formalRound && live.lastScoredRound === formalRound;
    const roundStats = formalRound ? getRoundStats(live, formalRound) : null;
    if (winnerSide && !alreadyProcessed) {
        if (roundStats) finalizeKastRounds(session, roundStats);
        if (roundStats) finalizeOneVsXWins(session, roundStats, winnerSide);
        if (winnerSide === 'CT') live.scoreCT += 1;
        else if (winnerSide === 'T') live.scoreT += 1;
        let winnerRoster = null;
        const players = payload.players || [];
        const votes: Record<string, number> = { A: 0, B: 0 };
        for (const raw of players) {
            const side = normalizeTeam(raw.team);
            if (side !== winnerSide) continue;
            const player = findPlayerBySteamId(session, raw.steamId);
            if (player?.rosterTeam) votes[player.rosterTeam] += 1;
        }
        if (votes.A === 0 && votes.B === 0) {
            for (const player of getGamePlayers(session)) {
                if (player.team === winnerSide && player.rosterTeam) votes[player.rosterTeam] += 1;
            }
        }
        if (votes.A > votes.B) winnerRoster = 'A';
        else if (votes.B > votes.A) winnerRoster = 'B';
        else {
            winnerRoster = resolveRosterTeamByInitialSide(winnerSide, formalRound);
        }
        if (winnerRoster && !live.matchFinished) {
            if (winnerRoster === 'A') live.scoreA += 1;
            else live.scoreB += 1;
        }
        if (formalRound) live.lastScoredRound = formalRound;
    }
    updateMatchFinishState();
    if (live.matchFinished) live.statsLocked = true;
    if (live.matchFinished && session.phase === GamePhase.LiveGame) {
        const winnerLabel = live.winnerTeam === 'A' ? 'A?' : 'B?';
        notify(`?????${winnerLabel} ????? ${live.scoreA}:${live.scoreB}`);
    }
}