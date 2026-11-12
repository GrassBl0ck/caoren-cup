import { applyRuntimeCombatStatistics, RUNTIME_COMBAT_EVENT_TYPES } from './match-runtime-statistics';
import {
    updateLivePlayersFromSnapshot, isFormalStatsStarted,
    applyKillEvent, applyDamageEvent, applyBlindEvent, applyWeaponFireEvent, applyJumpEvent, applyCrouchSampleEvent,
    getRoundStats, rawRoundKey, isAlreadyScoredRound, markScoredRound,
    applyRoundStatisticsStart, applyRoundStatisticsEnd,
} from './plugin-round-statistics';
import { cancelPluginCommands } from './plugin-command-queue';
import { enqueueCurrentAbilitySync } from './ability-sync-orchestrator';
import { isBridgeMatchRuntimeEnabled } from './match-runtime-orchestrator';
import { markStandardMatchLiveFromMatchZy } from './game-flow-manager';
// plugin-api.ts
import express from 'express';
import { GamePhase, WsEvents, LiveGameData } from './types';
import { getSession } from './session-manager';
import type { GameModeId, GameModeLifecycle } from './types';
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
    getPluginCommand,
} from './plugin-command-queue';
import {
    applyAbilitySyncFinalAck,
    markAbilitySyncBridgeReceived,
    markAbilitySyncPluginValidating,
} from './ability-sync-service';
import {
    normalizePluginRound,
    updateMatchFinishState,
    resolveRosterTeamByInitialSide,
    synchronizeRandomModeRuntime,
} from './game-flow-manager';
import { ADMIN_PASSWORD, PLUGIN_TOKEN } from './game-constants';
import { resolveDuelMapConfig } from './duel-config';
import { lobbyIdentityService } from './identity/identity-runtime';
import { applyMembershipToPlayer } from './identity/session-integration';
import { validateUnbalancedRosterForTeamLock } from './unbalanced-roster';
import { getGameModeRoundRules } from './game-mode-rounds';
import { GAME_MODE_MANIFESTS } from './game-mode';
import { PluginHeartbeatOrder } from './plugin-heartbeat-order';
import { scheduleSessionSnapshotSave } from './session-persistence';
import { enqueueAbilityRuntimeStop } from './ability-runtime-orchestrator';
import {
    applyMatchRuntimeHeartbeat,
    shouldTreatRoundStartAsLegacyMatchzy,
} from './match-runtime-projection';
import type { MatchRuntimeEvent, MatchRuntimeSnapshot } from './match-runtime-types';
import { isCurrentAbilitySyncConfirmed } from './ability-phase-one-policy';
import { buildMatchRuntimeDefinition } from './match-runtime-orchestrator';

type TeamAssignmentSide = 'CT' | 'T';

export interface PluginTelemetryEvent {
    type: string;
    payload: any;
}

export const HIGH_FREQUENCY_PLUGIN_EVENTS = new Set([
    'weapon_fire',
    'player_hurt',
    'player_jump',
    'player_crouch_sample',
]);

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
    (!isBridgeMatchRuntimeEnabled(session) && (session.liveGameData?.matchFinished === true || session.liveGameData?.statsLocked === true));

const createEmptyLiveGameData = (): LiveGameData => ({
    scoreCT: 0, scoreT: 0, scoreA: 0, scoreB: 0,
    currentRound: 0, pluginConnected: false, winnerTeam: null, matchFinished: false,
    winTarget: 0, roundLimit: 0, halftimeEnabled: false, halftimeRound: 0,
    lastScoredRound: 0, rawPluginRound: 0, roundBaseOffset: undefined,
    formalRoundStartRaw: undefined, formalStatsStarted: false, statsLocked: false,
    killMatrix: {}, openingKillMatrix: {}, awpKillMatrix: {}, firstKillRounds: {},
});

const oppositeSide = (side: TeamAssignmentSide): TeamAssignmentSide => side === 'CT' ? 'T' : 'CT';

const getFormalRound = (session: any): number => {
    const live = session.liveGameData || {};
    return Math.max(0, Math.floor(Number(live.currentRound || 0)));
};

const getTeamASideForRound = (selectedSide: unknown, round: number): TeamAssignmentSide | null => {
    if (selectedSide !== 'CT' && selectedSide !== 'T') return null;
    const session = getSession();
    const initial = selectedSide as TeamAssignmentSide;
    const rules = getGameModeRoundRules(session.matchOptions?.gameModeId, session.matchOptions);
    if (!rules.halftimeEnabled || round < rules.halftimeRound) return initial;
    return oppositeSide(initial);
};

const buildTeamAssignments = (session: any): TeamAssignmentBuildResult => {
    const round = getFormalRound(session);
    const rules = getGameModeRoundRules(session.matchOptions?.gameModeId, session.matchOptions);
    const teamASide = getTeamASideForRound(session.selectedSide, round);
    if (!teamASide) {
        throw new Error('还没有完成选边，不能同步分队到游戏内。');
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
        halftimeSwapped: rules.halftimeEnabled && round >= rules.halftimeRound,
        teamASide,
        assignments,
        missingSteamIds,
        unassignedPlayers,
    };
};

const hasTeamAssignmentMismatch = (session: any, round: number): boolean => {
    const teamASide = getTeamASideForRound(session.selectedSide, round);
    if (!teamASide) return false;
    let hasBoundRosterPlayer = false;
    for (const player of getGamePlayers(session)) {
        if (player.role === 'Admin' || player.role === 'Spectator') continue;
        if (player.rosterTeam !== 'A' && player.rosterTeam !== 'B') continue;
        if (!normalizeSteamId(player.steamId)) continue;
        hasBoundRosterPlayer = true;
        const expectedSide = player.rosterTeam === 'A' ? teamASide : oppositeSide(teamASide);
        const currentSide = normalizeTeam(player.team);
        if (currentSide !== expectedSide) return true;
    }
    return !hasBoundRosterPlayer;
};
const enqueueTeamAssignments = (
    session: any,
    reason: string,
    lockTeams = true
): TeamAssignmentBuildResult & { commandId: string; queuedAt: number; reason: string } => {
    const rosterCheck = validateUnbalancedRosterForTeamLock(session);
    if (!rosterCheck.valid) throw new Error(rosterCheck.blockers.join(' '));
    const built = buildTeamAssignments(session);
    if (built.assignments.length === 0) {
        throw new Error('没有可同步的已绑定 A/B 队玩家。');
    }

    const rules = getGameModeRoundRules(session.matchOptions?.gameModeId, session.matchOptions);
    const queued = enqueuePluginCommand('APPLY_TEAM_ASSIGNMENTS', {
        matchId: session.matchId,
        round: built.round,
        validFromRound: built.halftimeSwapped ? rules.halftimeRound : 0,
        validUntilRound: built.halftimeSwapped ? 0 : Math.max(0, rules.halftimeRound - 1),
        lockTeams,
        reason,
        halftimeSwapped: built.halftimeSwapped,
        teamASide: built.teamASide,
        assignments: built.assignments,
        requestedAt: Date.now(),
        label: built.halftimeSwapped ? '半场自动换边并锁队' : '按网页分队同步并锁队',
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

const requirePluginAuth = (req: any, res: any, next: any) => {
    const token = req.header('x-caoren-plugin-token') || req.query?.token;
    if (!PLUGIN_TOKEN || token !== PLUGIN_TOKEN) return res.status(401).json({ success: false, error: '插件认证失败' });
    next();
};

export function registerPluginRoutes(app: express.Express, deps: {
    broadcastState: () => void;
    requestStateBroadcast: () => void;
    flushStateBroadcast: () => void;
    notifyMessage: (msg: string) => void;
}) {
    const { broadcastState, requestStateBroadcast, flushStateBroadcast, notifyMessage } = deps;
    const heartbeatOrder = new PluginHeartbeatOrder();

    app.get('/api/plugin/state', requirePluginAuth, (req, res) => {
        const session = getSession();
        res.json({
            success: true,
            generatedAt: Date.now(),
            sessionId: session.sessionId,
            matchId: session.matchId,
            phase: session.phase,
            selectedMap: session.selectedMap,
            selectedSide: session.selectedSide,
            liveGameData: session.liveGameData,
            lobbySteamIds: lobbyIdentityService.listLobbySteamIds(session.sessionId),
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
        // Reject before touching the session or taking commands from the outbound queue.
        if (!heartbeatOrder.accept(req.body)) {
            return res.status(409).json({ success: false, ignored: true, error: '过期或无效的插件心跳' });
        }
        const session = getSession();
        session.bridgeProtocolCapabilities = {
            matchRuntime: req.body?.matchRuntimeProtocolVersion === 1 ? 1 : 0,
            abilitySync: req.body?.abilitySyncProtocolVersion === 1 ? 1 : 0,
        };
        if (session.bridgeProtocolCapabilities.matchRuntime !== 1) cancelPluginCommands(command => command.type === 'MATCH_RUNTIME_COMMAND');
        if (session.bridgeProtocolCapabilities.abilitySync !== 1) {
            cancelPluginCommands(command => command.type === 'ABILITY_SYNC' || command.type === 'ABILITY_RUNTIME');
            if (session.matchOptions.abilityModeEnabled === true && session.abilitySyncState) {
                session.abilitySyncState.status = 'waiting_bridge';
                session.abilitySyncState.commandId = '';
                session.abilitySyncState.errorMessage = '等待支持异能同步协议的桥接与异能插件。';
            }
        }
        if (session.bridgeProtocolCapabilities.abilitySync === 1 && session.abilitySyncState?.status === 'waiting_bridge' && !session.abilitySyncState.commandId) {
            try { enqueueCurrentAbilitySync(session); } catch { /* keep the explicit waiting state until roster is valid */ }
        }

        if (!session.liveGameData) session.liveGameData = createEmptyLiveGameData();
        if (Array.isArray(req.body?.availableGameModes)) {
            session.availableGameModes = req.body.availableGameModes.filter((id: unknown): id is GameModeId =>
                typeof id === 'string' && Object.prototype.hasOwnProperty.call(GAME_MODE_MANIFESTS, id));
        } else if (req.body && Object.prototype.hasOwnProperty.call(req.body, 'availableGameModes')) {
            session.availableGameModes = [];
        }
        const runtime = req.body?.gameMode;
        if (runtime && typeof runtime === 'object' &&
            typeof runtime.modeId === 'string' &&
            Object.prototype.hasOwnProperty.call(GAME_MODE_MANIFESTS, runtime.modeId) &&
            ['Lobby', 'Warmup', 'Live', 'Stopped', 'Error'].includes(runtime.lifecycle) &&
            Number.isFinite(Number(runtime.warmupSeconds)) &&
            Number.isFinite(Number(runtime.revision))) {
            synchronizeRandomModeRuntime({
                modeId: runtime.modeId as GameModeId,
                lifecycle: runtime.lifecycle as GameModeLifecycle,
                warmupSeconds: Math.max(1, Math.floor(Number(runtime.warmupSeconds))),
                revision: Math.max(0, Math.floor(Number(runtime.revision))),
                ...(typeof runtime.error === 'string' ? { error: runtime.error } : {}),
            });
        }
        // A newly confirmed Live transition replaces liveGameData; apply heartbeat
        // metadata to the new object rather than the discarded pregame snapshot.
        session.liveGameData.pluginConnected = true;
        session.liveGameData.lastPluginHeartbeatAt = Date.now();
        if (req.body?.mapName) session.liveGameData.mapName = String(req.body.mapName);
        const projection = applyMatchRuntimeHeartbeat(session, {
            matchRuntimeEnabled: req.body?.matchRuntimeEnabled === true && req.body?.matchRuntimeProtocolVersion === 1,
            matchRuntime: req.body?.matchRuntime as MatchRuntimeSnapshot | undefined,
            matchRuntimeEvents: req.body?.matchRuntimeEvents as MatchRuntimeEvent[] | undefined,
            demoSegments: req.body?.demoSegments,
            restorePoints: req.body?.matchRestorePoints,
        });
        if (projection.snapshotApplied || projection.eventsApplied > 0) scheduleSessionSnapshotSave();
        broadcastState();
        res.json({
            success: true,
            matchId: session.matchId,
            phase: session.phase,
            gameModeId: session.gameModeRuntime?.modeId,
            gameModeRevision: session.gameModeRuntime?.revision,
            scoreCT: session.liveGameData.scoreCT || 0,
            scoreT: session.liveGameData.scoreT || 0,
            currentRound: session.liveGameData.currentRound || 0,
            matchFinished: session.liveGameData.matchFinished === true,
            matchController: session.matchOptions.matchController || 'caoren',
            matchRuntimeEnabled: session.matchOptions.matchRuntimeEnabled === true,
            matchRuntime: session.matchRuntime,
            matchRuntimeDefinition: session.matchRuntime ? buildMatchRuntimeDefinition(session) : undefined,
            matchRestorePoints: session.matchRestorePoints || [],
            abilityModeEnabled: session.matchOptions.abilityModeEnabled === true,
            abilitySyncConfirmed: isCurrentAbilitySyncConfirmed(session),
            commands: takeQueuedPluginCommands(),
        });
    });

    app.post('/api/plugin/command-ack', requirePluginAuth, (req, res) => {
        const commandId = req.body?.commandId;
        const command = getPluginCommand(commandId);
        const ok = ackPluginCommand(commandId);
        if (!ok) return res.status(404).json({ success: false, error: '未找到要确认的插件命令' });
        const session = getSession();
        if (command?.type === 'ABILITY_SYNC') {
            const nextState = markAbilitySyncBridgeReceived(session.abilitySyncState, String(commandId || '').trim());
            if (nextState !== session.abilitySyncState) {
                session.abilitySyncState = nextState;
                scheduleSessionSnapshotSave();
                broadcastState();
            }
        }
        res.json({ success: true });
    });

    app.post('/api/plugin/ability-sync-ack', requirePluginAuth, (req, res) => {
        const body = req.body || {};
        const status = body.status === 'success' || body.status === 'failed' ? body.status : null;
        const ack = {
            status,
            syncId: String(body.syncId || '').trim(),
            matchId: String(body.matchId || '').trim(),
            revision: Number(body.revision),
            contentDigest: String(body.contentDigest || '').trim(),
            appliedSeatCount: Number(body.appliedSeatCount),
            ...(typeof body.errorCode === 'string' ? { errorCode: body.errorCode.slice(0, 80) } : {}),
            ...(typeof body.errorMessage === 'string' ? { errorMessage: body.errorMessage.slice(0, 240) } : {}),
        } as const;
        if (!ack.status || !ack.syncId || !ack.matchId || !Number.isSafeInteger(ack.revision)
            || !/^([a-f0-9]{64})$/.test(ack.contentDigest)
            || !Number.isSafeInteger(ack.appliedSeatCount) || ack.appliedSeatCount < 0) {
            return res.status(400).json({ success: false, error: '异能同步最终 ACK 格式无效' });
        }
        const session = getSession();
        if (session.phase !== GamePhase.PreGameSetup || session.matchOptions.abilityModeEnabled !== true) {
            return res.status(409).json({ success: false, error: '当前比赛已不再接收异能同步最终 ACK' });
        }
        const result = applyAbilitySyncFinalAck(session.abilitySyncState, ack);
        if (!result.accepted || !result.state) {
            return res.status(409).json({ success: false, error: '异能同步最终 ACK 已过期或不属于当前比赛' });
        }
        session.abilitySyncState = result.state;
        scheduleSessionSnapshotSave();
        broadcastState();
        res.json({ success: true, status: result.state.status });
    });

    app.post('/api/plugin/ability-sync-progress', requirePluginAuth, (req, res) => {
        const body = req.body || {};
        const input = {
            syncId: String(body.syncId || '').trim(),
            matchId: String(body.matchId || '').trim(),
            revision: Number(body.revision),
            contentDigest: String(body.contentDigest || '').trim(),
        };
        if (!input.syncId || !input.matchId || !Number.isSafeInteger(input.revision)
            || !/^([a-f0-9]{64})$/.test(input.contentDigest)) {
            return res.status(400).json({ success: false, error: '异能同步进度格式无效' });
        }
        const session = getSession();
        if (session.phase !== GamePhase.PreGameSetup || session.matchOptions.abilityModeEnabled !== true) {
            return res.status(409).json({ success: false, error: '当前比赛已不再接收异能同步进度' });
        }
        const nextState = markAbilitySyncPluginValidating(session.abilitySyncState, input);
        if (nextState === session.abilitySyncState) {
            return res.status(409).json({ success: false, error: '异能同步进度已过期或不属于当前比赛' });
        }
        session.abilitySyncState = nextState;
        scheduleSessionSnapshotSave();
        broadcastState();
        res.json({ success: true, status: nextState?.status });
    });

    app.post('/api/plugin/duel-map', requirePluginAuth, (req, res) => {
        const session = getSession();
        const map = resolveDuelMapConfig(req.body?.map, req.body?.workshopId);
        session.matchOptions.matchMode = 'duel';
        session.matchOptions.undercoverModeEnabled = false;
        session.matchOptions.duelMap = map.name;
        session.matchOptions.duelMapWorkshopId = map.workshopId;
        notifyMessage(`\u4e0b\u4e00\u5c40\u5355\u6311\u5730\u56fe\u5df2\u8bbe\u7f6e\u4e3a\uff1a${map.name}`);
        broadcastState();
        res.json({ success: true, map: map.name, workshopId: map.workshopId });
    });

    app.post('/api/admin/team-lock/sync', (req, res) => {
        const adminPassword = String(req.body?.adminPassword || '');
        if (!ADMIN_PASSWORD || adminPassword !== ADMIN_PASSWORD) {
            return res.status(401).json({ success: false, error: '管理员密码错误' });
        }

        const session = getSession();
        if (!session.liveGameData) session.liveGameData = createEmptyLiveGameData();
        try {
            const result = enqueueTeamAssignments(session, 'manual-sync', true);
            notifyMessage(`强制分队已加入下发队列：${result.assignments.length} 名已绑定玩家。`);
            broadcastState();
            res.json({ success: true, ...result });
        } catch (err) {
            const message = err instanceof Error ? err.message : '同步分队失败';
            res.status(400).json({ success: false, error: message });
        }
    });

    app.post('/api/admin/team-lock/clear', (req, res) => {
        const adminPassword = String(req.body?.adminPassword || '');
        if (!ADMIN_PASSWORD || adminPassword !== ADMIN_PASSWORD) {
            return res.status(401).json({ success: false, error: '管理员密码错误' });
        }

        const session = getSession();
        if (!session.liveGameData) session.liveGameData = createEmptyLiveGameData();
        const queued = enqueuePluginCommand('CLEAR_TEAM_ASSIGNMENTS', {
            matchId: session.matchId,
            requestedAt: Date.now(),
            label: '解除网页强制分队',
        });
        session.liveGameData.teamLockEnabled = false;
        session.liveGameData.teamLockAssignments = [];
        session.liveGameData.teamLockLastCommandId = queued.id;
        notifyMessage('解除强制分队已加入下发队列。');
        broadcastState();
        res.json({ success: true, commandId: queued.id, queuedAt: queued.createdAt });
    });

    app.post('/api/plugin/snapshot', requirePluginAuth, async (req, res) => {
        const session = getSession();
        const runtimeProjection = applyMatchRuntimeHeartbeat(session, {
            matchRuntimeEnabled: req.body?.matchRuntimeEnabled === true && req.body?.matchRuntimeProtocolVersion === 1,
            matchRuntime: req.body?.matchRuntime as MatchRuntimeSnapshot | undefined,
            matchRuntimeEvents: req.body?.matchRuntimeEvents as MatchRuntimeEvent[] | undefined,
            demoSegments: req.body?.demoSegments,
            restorePoints: req.body?.matchRestorePoints,
        });
        if (runtimeProjection.snapshotApplied || runtimeProjection.eventsApplied > 0) scheduleSessionSnapshotSave();
        const players = Array.isArray(req.body?.players) ? req.body.players : [];
        const confirmedMemberships = await lobbyIdentityService.confirmLongTermPresence(
            session.sessionId,
            players,
        );
        for (const membership of confirmedMemberships) {
            const sessionPlayer = Object.values(session.players).find((player) => player.membershipId === membership.membershipId);
            if (sessionPlayer) applyMembershipToPlayer(sessionPlayer, membership);
        }
        if (!isPluginLivePhase(session.phase)) {
            if (confirmedMemberships.length > 0) broadcastState();
            return res.json({
                success: true,
                ignored: true,
                reason: `当前阶段 ${session.phase} 不接收实时战绩`,
            });
        }
        if (req.body?.matchId && req.body.matchId !== session.matchId) return res.status(409).json({ success: false, error: 'matchId 不匹配' });
        if (!session.liveGameData) session.liveGameData = createEmptyLiveGameData();
        if (isPluginStatsLocked(session)) return res.json({ success: true, ignored: true, reason: '赛后战绩已锁定' });
        if (typeof req.body?.currentRound === 'number' && !isFormalStatsStarted(session)) {
            session.liveGameData.currentRound = Math.max(session.liveGameData.currentRound || 0, normalizePluginRound(req.body.currentRound));
        }
        if (!isFormalStatsStarted(session) && typeof req.body?.scoreCT === 'number') session.liveGameData.scoreCT = Math.max(0, Math.floor(req.body.scoreCT));
        if (!isFormalStatsStarted(session) && typeof req.body?.scoreT === 'number') session.liveGameData.scoreT = Math.max(0, Math.floor(req.body.scoreT));
        if (req.body?.mapName) session.liveGameData.mapName = String(req.body.mapName);
        session.liveGameData.pluginConnected = true;
        session.liveGameData.lastPluginHeartbeatAt = Date.now();
        updateLivePlayersFromSnapshot(session, players, { updateStats: false });
        updateMatchFinishState();
        broadcastState();
        res.json({
            success: true,
            matchedPlayers: getGamePlayers(session).filter(p => p.steamId && p.stats).length,
        });
    });

    app.post('/api/plugin/event', requirePluginAuth, (req, res) => {
        const session = getSession();
        const type = String(req.body?.type || '');
        const payload = req.body?.payload || {};

        const projection = applyMatchRuntimeHeartbeat(session, {
            matchRuntimeEnabled: req.body?.matchRuntimeEnabled === true && req.body?.matchRuntimeProtocolVersion === 1,
            matchRuntime: req.body?.matchRuntime as MatchRuntimeSnapshot | undefined,
            matchRuntimeEvents: req.body?.matchRuntimeEvents as MatchRuntimeEvent[] | undefined,
            demoSegments: req.body?.demoSegments,
            restorePoints: req.body?.matchRestorePoints,
        });
        if (projection.snapshotApplied || projection.eventsApplied > 0) scheduleSessionSnapshotSave();

        const promotedFromPreGame = session.phase === GamePhase.PreGameSetup &&
            shouldTreatRoundStartAsLegacyMatchzy(session, type) &&
            markStandardMatchLiveFromMatchZy(`round-${Math.max(1, Math.floor(Number(payload.round || 1)))}`);

        if (!isPluginLivePhase(session.phase) && !(isBridgeMatchRuntimeEnabled(session) && session.phase === GamePhase.PreGameSetup && session.matchRuntime)) return res.json({ success: true, ignored: true, reason: `当前阶段 ${session.phase} 不接收实时事件` });
        if (req.body?.matchId && req.body.matchId !== session.matchId) return res.status(409).json({ success: false, error: 'matchId 不匹配' });
        if (!session.liveGameData) session.liveGameData = createEmptyLiveGameData();
        if (isPluginStatsLocked(session)) return res.json({ success: true, ignored: true, reason: '赛后战绩已锁定' });
        session.liveGameData.pluginConnected = true;
        session.liveGameData.lastPluginHeartbeatAt = Date.now();

        if (!isFormalStatsStarted(session) && !isBridgeMatchRuntimeEnabled(session)) {
            if (type === 'round_start' && Array.isArray(payload.players)) {
                updateLivePlayersFromSnapshot(session, payload.players, { updateStats: false });
            }
            if (HIGH_FREQUENCY_PLUGIN_EVENTS.has(type)) requestStateBroadcast();
            else flushStateBroadcast();
            return res.json({ success: true, ignored: true, reason: '正式统计尚未开始' });
        }

        if (!applyPluginEvent(session, type, payload, notifyMessage)) {
            return res.status(400).json({ success: false, error: `未知事件: ${type}` });
        }
        if (HIGH_FREQUENCY_PLUGIN_EVENTS.has(type)) requestStateBroadcast();
        else flushStateBroadcast();
        res.json({ success: true });
    });

    app.post('/api/plugin/events', requirePluginAuth, (req, res) => {
        const session = getSession();
        const events = Array.isArray(req.body?.events) ? req.body.events : [];
        if (events.length === 0 || events.length > 4096) {
            return res.status(400).json({ success: false, error: '批量事件数量必须在 1～4096 条之间' });
        }
        if (!isPluginLivePhase(session.phase) && !(isBridgeMatchRuntimeEnabled(session) && session.phase === GamePhase.PreGameSetup && session.matchRuntime)) {
            return res.json({ success: true, ignored: true, reason: `当前阶段 ${session.phase} 不接收实时事件` });
        }
        if (req.body?.matchId && req.body.matchId !== session.matchId) {
            return res.status(409).json({ success: false, error: 'matchId 不匹配' });
        }
        if (!session.liveGameData) session.liveGameData = createEmptyLiveGameData();
        if (isPluginStatsLocked(session)) return res.json({ success: true, ignored: true, reason: '赛后战绩已锁定' });
        session.liveGameData.pluginConnected = true;
        session.liveGameData.lastPluginHeartbeatAt = Date.now();
        if (!isFormalStatsStarted(session) && !isBridgeMatchRuntimeEnabled(session)) {
            requestStateBroadcast();
            return res.json({ success: true, ignored: true, reason: '正式统计尚未开始' });
        }
        try {
            const processed = applyPluginTelemetryBatch(session, events);
            requestStateBroadcast();
            return res.json({ success: true, processed });
        } catch (error) {
            const message = error instanceof Error ? error.message : '批量事件无效';
            return res.status(400).json({ success: false, error: message });
        }
    });
}

function applyPluginEvent(session: any, type: string, payload: any, notifyMessage: (msg: string) => void): boolean {
    if (isBridgeMatchRuntimeEnabled(session)) {
        if (type === 'round_start' || type === 'round_end') return true;
        if (!RUNTIME_COMBAT_EVENT_TYPES.has(type)) return false;
        applyRuntimeCombatStatistics(session, type, payload);
        return true;
    }
    switch (type) {
        case 'round_start': applyRoundStartEvent(session, payload); return true;
        case 'player_death': applyKillEvent(session, payload); return true;
        case 'player_hurt': applyDamageEvent(session, payload); return true;
        case 'player_blind': applyBlindEvent(session, payload); return true;
        case 'weapon_fire': applyWeaponFireEvent(session, payload); return true;
        case 'player_jump': applyJumpEvent(session, payload); return true;
        case 'player_crouch_sample': applyCrouchSampleEvent(session, payload); return true;
        case 'round_end': applyRoundEndEvent(session, payload, notifyMessage); return true;
        default: return false;
    }
}

export function applyPluginTelemetryBatch(session: any, events: PluginTelemetryEvent[]): number {
    for (const event of events) {
        if (!event || !HIGH_FREQUENCY_PLUGIN_EVENTS.has(String(event.type || ''))) {
            throw new Error(`不允许批量处理的插件事件: ${String(event?.type || '')}`);
        }
    }
    for (const event of events) {
        applyPluginEvent(session, event.type, event.payload || {}, () => undefined);
    }
    return events.length;
}

function applyRoundStartEvent(session: any, payload: any) {
    const live = session.liveGameData;
    const round = Math.max(1, normalizePluginRound(Number(payload.round || live?.currentRound || 1)));
    live.currentRound = Math.max(Number(live.currentRound || 0), round);
    const roundStats = getRoundStats(live, round);
    if (roundStats.roundStartApplied) return;
    applyRoundStatisticsStart(session, payload, round);
    const rules = getGameModeRoundRules(session.matchOptions?.gameModeId, session.matchOptions);
    const shouldRefreshHalftimeLock =
        rules.halftimeEnabled &&
        round >= rules.halftimeRound &&
        live.teamLockEnabled === true &&
        Number(live.teamLockLastAutoSideSwapRound || 0) !== round &&
        Date.now() - Number(live.teamLockLastAutoSideSwapAt || 0) > 3000 &&
        (live.teamLockHalftimeSwapped !== true || hasTeamAssignmentMismatch(session, round));

    if (shouldRefreshHalftimeLock) {
        try {
            const result = enqueueTeamAssignments(session, 'halftime-auto-swap', true);
            live.teamLockLastAutoSideSwapRound = round;
            live.teamLockLastAutoSideSwapAt = Date.now();
            live.teamLockLastAutoSideSwapCommandId = result.commandId;
        } catch (err) {
            live.teamLockLastAutoSideSwapError = err instanceof Error ? err.message : 'halftime auto side swap failed';
        }
    }
}

function applyRoundEndEvent(session: any, payload: any, notify: (msg: string) => void) {
    const live = session.liveGameData;
    const formalRound = typeof payload.round === 'number' ? normalizePluginRound(payload.round) : live?.currentRound || 0;
    if (formalRound) live.currentRound = Math.max(Number(live.currentRound || 0), formalRound);
    const winnerSide = normalizeTeam(payload.winner);
    const rawKey = rawRoundKey(payload, formalRound);
    const alreadyProcessed = !!formalRound && isAlreadyScoredRound(live, rawKey);
    if (winnerSide && !alreadyProcessed) {
        if (formalRound) applyRoundStatisticsEnd(session, payload, formalRound, winnerSide, rawKey);
        if (winnerSide === 'CT') live.scoreCT += 1;
        else if (winnerSide === 'T') live.scoreT += 1;
        let winnerRoster = resolveRosterTeamByInitialSide(winnerSide, formalRound);
        if (!winnerRoster) {
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
        }
        if (winnerRoster && !live.matchFinished) {
            if (winnerRoster === 'A') live.scoreA += 1;
            else live.scoreB += 1;
        }
        if (formalRound) markScoredRound(live, rawKey, formalRound);
    }
    const wasFinished = live.matchFinished === true;
    updateMatchFinishState();
    if (live.matchFinished) live.statsLocked = true;
    if (!wasFinished && live.matchFinished) enqueueAbilityRuntimeStop(session, 'match_finished');
    if (live.matchFinished && session.phase === GamePhase.LiveGame) {
        const result = live.winnerTeam ? `${live.winnerTeam}队获胜` : '双方平局';
        notify(`比赛结束：${result}，比分 ${live.scoreA}:${live.scoreB}`);
    }
}
