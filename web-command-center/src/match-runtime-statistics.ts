import type { GameSession } from './types';
import type { MatchRuntimeEvent, MatchRuntimeRoundStatisticsJournal as RoundJournal } from './match-runtime-types';
import { applyRoundStatisticsStart, applyRoundStatisticsEnd, applyKillEvent, applyDamageEvent,
    applyBlindEvent, applyWeaponFireEvent, applyJumpEvent, applyCrouchSampleEvent } from './plugin-round-statistics';

const clone = <T>(value: T): T => JSON.parse(JSON.stringify(value));

export const captureStatsCheckpoint = (session: GameSession, roundNumber: number): void => {
    if (!Number.isSafeInteger(roundNumber) || roundNumber <= 0) return;
    if (!session.matchRuntimeStatsCheckpoints) session.matchRuntimeStatsCheckpoints = {};
    if (session.matchRuntimeStatsCheckpoints[roundNumber]) return;
    const players: Record<string, { stats?: any; sideStats?: any }> = {};
    for (const [playerId, player] of Object.entries(session.players || {})) {
        players[playerId] = {
            ...(player.stats !== undefined ? { stats: clone(player.stats) } : {}),
            ...(player.sideStats !== undefined ? { sideStats: clone(player.sideStats) } : {}),
        };
    }
    session.matchRuntimeStatsCheckpoints[roundNumber] = {
        ...(session.liveGameData ? { liveGameData: clone(session.liveGameData) } : {}),
        players,
    };
};

export const restoreStatsCheckpoint = (session: GameSession, roundNumber: number): boolean => {
    const checkpoint = session.matchRuntimeStatsCheckpoints?.[roundNumber];
    if (!checkpoint) return false;
    session.liveGameData = checkpoint.liveGameData ? clone(checkpoint.liveGameData) : undefined;
    for (const [playerId, player] of Object.entries(session.players || {})) {
        const saved = checkpoint.players[playerId];
        if (saved?.stats !== undefined) player.stats = clone(saved.stats);
        else delete player.stats;
        if (saved?.sideStats !== undefined) player.sideStats = clone(saved.sideStats);
        else delete player.sideStats;
    }
    // 目标回合及之后的检查点来自已撤销的时间线，重打时必须重新捕获。
    for (const key of Object.keys(session.matchRuntimeStatsCheckpoints || {})) {
        if (Number(key) >= roundNumber) delete session.matchRuntimeStatsCheckpoints![Number(key)];
    }
    for (const key of Object.keys(session.matchRuntimeStatsJournal || {})) {
        if (Number(key) >= roundNumber) delete session.matchRuntimeStatsJournal[Number(key)];
    }
    return true;
};

// roundKey 属于事件自身，不能用一批心跳的最新 snapshot 推算历史回合。
export const eventRoundNumber = (event: MatchRuntimeEvent): number | null => {
    const prefix = `${event.matchId}:${event.runtimeEpoch}:round-`;
    if (typeof event.roundKey !== 'string' || !event.roundKey.startsWith(prefix)) return null;
    const suffix = event.roundKey.slice(prefix.length);
    if (!/^[1-9]\d*$/.test(suffix)) return null;
    const round = Number(suffix);
    if (!Number.isSafeInteger(round) || (event.roundNumber !== undefined && event.roundNumber !== round)) return null;
    return round;
};


const statisticLiveKeys = ['roundStats', 'killMatrix', 'openingKillMatrix', 'awpKillMatrix', 'firstKillRounds'] as const;
const combatHandlers: Record<string, (session: any, payload: any) => void> = {
    player_death: applyKillEvent, player_hurt: applyDamageEvent, player_blind: applyBlindEvent,
    weapon_fire: applyWeaponFireEvent, player_jump: applyJumpEvent, player_crouch_sample: applyCrouchSampleEvent,
};

export const RUNTIME_COMBAT_EVENT_TYPES = new Set(Object.keys(combatHandlers));

const journal = (session: GameSession): Record<number, RoundJournal> =>
    session.matchRuntimeStatsJournal ||= {};

const restoreStatisticFields = (session: GameSession, checkpoint: NonNullable<GameSession['matchRuntimeStatsCheckpoints']>[number]) => {
    for (const [id, player] of Object.entries(session.players)) {
        const saved = checkpoint.players[id];
        if (saved?.stats !== undefined) player.stats = clone(saved.stats); else delete player.stats;
        if (saved?.sideStats !== undefined) player.sideStats = clone(saved.sideStats); else delete player.sideStats;
    }
    const live = session.liveGameData!;
    for (const key of statisticLiveKeys) {
        if (checkpoint.liveGameData?.[key] !== undefined) live[key] = clone(checkpoint.liveGameData[key]) as any;
        else delete live[key];
    }
};

// HTTP 心跳与战斗批次可能乱序；回放受影响回合之后的战绩，不触碰权威比分和流程。
const recalculateFromRound = (session: GameSession, changedRound: number) => {
    const entries = journal(session);
    if (!entries[changedRound]?.started) return;
    const checkpointRounds = Object.keys(session.matchRuntimeStatsCheckpoints || {}).map(Number).sort((a, b) => a - b);
    const checkpointRound = checkpointRounds.filter(n => n <= changedRound).at(-1) ?? checkpointRounds[0];
    const fromRound = Math.min(changedRound, checkpointRound ?? changedRound);
    const baseline = checkpointRound === undefined ? undefined : session.matchRuntimeStatsCheckpoints![checkpointRound];
    if (baseline) restoreStatisticFields(session, baseline);
    for (const key of checkpointRounds) if (key >= fromRound) delete session.matchRuntimeStatsCheckpoints![key];
    for (const round of Object.keys(entries).map(Number).sort((a, b) => a - b)) {
        const entry = entries[round];
        if (round < fromRound || !entry.started) continue;
        captureStatsCheckpoint(session, round);
        applyRoundStatisticsStart(session, { players: entry.started.players, timestamp: entry.started.occurredAtUnixMs }, round, entry.roundKey);
        const startTime = Number(entry.started.occurredAtUnixMs);
        const endTime = Number(entry.completed?.occurredAtUnixMs);
        for (const item of entry.combat.slice().sort((a, b) => a.payload.occurredAtUnixMs - b.payload.occurredAtUnixMs || Number(a.payload.eventSequence || 0) - Number(b.payload.eventSequence || 0))) {
            const at = item.payload.occurredAtUnixMs;
            if (at < startTime || (entry.completed && at > endTime)) continue;
            combatHandlers[item.type](session, { ...item.payload, authoritativeRound: round });
        }
        if (entry.completed) {
            const end = entry.completed;
            const side = end.winningEngineSide === 'counterTerrorist' ? 'CT' : end.winningEngineSide === 'terrorist' ? 'T' : null;
            if (side) applyRoundStatisticsEnd(session, { players: end.players, timestamp: end.occurredAtUnixMs, runtimeRoundKey: entry.roundKey }, round, side, entry.roundKey);
        }
    }
};

export const applyRuntimeRoundStatistics = (session: GameSession, event: MatchRuntimeEvent): boolean => {
    const round = eventRoundNumber(event);
    if (!round) return false;
    const entries = journal(session);
    const entry: RoundJournal = entries[round] ||= { roundKey: event.roundKey!, combat: [] };
    if (entry.roundKey !== event.roundKey) return false;
    if (event.type === 'round_started') {
        if (entry.started) return false;
        entry.started = clone(event);
    } else {
        if (entry.completed) return false;
        entry.completed = clone(event);
    }
    recalculateFromRound(session, round);
    return true;
};

export const applyRuntimeCombatStatistics = (session: GameSession, type: string, payload: any): boolean => {
    const runtime = session.matchRuntime;
    if (!combatHandlers[type] || !runtime || !payload || payload.matchId !== runtime.matchId
        || payload.revision !== runtime.revision || payload.runtimeEpoch !== runtime.runtimeEpoch
        || typeof payload.eventId !== 'string' || !/^[a-f0-9]{32}$/.test(payload.eventId)
        || !Number.isFinite(payload.occurredAtUnixMs) || payload.occurredAtUnixMs <= 0) return false;
    const round = eventRoundNumber(payload);
    if (!round || round > runtime.completedRounds + 1) return false;
    const entries = journal(session);
    const entry: RoundJournal = entries[round] ||= { roundKey: payload.roundKey, combat: [] };
    if (entry.roundKey !== payload.roundKey || entry.combat.some(item => item.payload.eventId === payload.eventId)) return false;
    if (entry.completed && payload.occurredAtUnixMs > Number(entry.completed.occurredAtUnixMs)) return false;
    entry.combat.push({ type, payload: clone(payload) });
    recalculateFromRound(session, round);
    return true;
};
