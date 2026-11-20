import {
    applyRuntimeCombatStatistics, applyRuntimeRoundStatistics, eventRoundNumber,
    restoreStatsCheckpoint, RUNTIME_COMBAT_EVENT_TYPES,
} from './match-runtime-statistics';
import { isBridgeMatchRuntimeEnabled } from './match-runtime-orchestrator';
import type {
    MatchRuntimeEvent,
    MatchRuntimeProjectionResult,
    MatchRuntimeSnapshot,
} from './match-runtime-types';
import { GamePhase, type GameSession, type LiveGameData } from './types';

const result = (applied: boolean, code: string): MatchRuntimeProjectionResult => ({ applied, code });

const clone = <T>(value: T): T => JSON.parse(JSON.stringify(value));

const ensureLiveData = (session: GameSession): LiveGameData => {
    if (!session.liveGameData) {
        session.liveGameData = {
            scoreCT: 0,
            scoreT: 0,
            scoreA: 0,
            scoreB: 0,
            currentRound: 0,
            pluginConnected: true,
            winnerTeam: null,
            matchFinished: false,
            winTarget: 13,
            lastScoredRound: 0,
        };
    }
    return session.liveGameData;
};

const validSnapshot = (snapshot: MatchRuntimeSnapshot): boolean =>
    !!snapshot
    && typeof snapshot.matchId === 'string'
    && Number.isSafeInteger(snapshot.revision) && snapshot.revision > 0
    && Number.isSafeInteger(snapshot.runtimeEpoch) && snapshot.runtimeEpoch > 0
    && Number.isSafeInteger(snapshot.lastEventSequence) && snapshot.lastEventSequence >= 0;

export const applyMatchRuntimeSnapshot = (
    session: GameSession,
    snapshot: MatchRuntimeSnapshot,
): MatchRuntimeProjectionResult => {
    if (!isBridgeMatchRuntimeEnabled(session)) return result(false, 'NOT_CAOREN_CONTROLLER');
    if (!validSnapshot(snapshot) || snapshot.matchId !== session.matchId) return result(false, 'IDENTITY_MISMATCH');
    if (session.matchRuntime?.matchId !== snapshot.matchId) {
        session.matchRuntimeStatsCheckpoints = undefined;
        session.matchRuntimeStatsJournal = undefined;
    }
    const currentEpoch = Number(session.matchRuntime?.runtimeEpoch || 0);
    if (snapshot.runtimeEpoch < currentEpoch) return result(false, 'STALE_EPOCH');
    if (currentEpoch > 0 && snapshot.runtimeEpoch > currentEpoch) {
        restoreStatsCheckpoint(session, Math.max(1, Math.floor(snapshot.completedRounds) + 1));
    }

    const previous = currentEpoch === snapshot.runtimeEpoch && session.matchRuntime?.revision === snapshot.revision
        ? session.matchRuntime : undefined;
    session.matchRuntime = clone(snapshot);
    if (!Array.isArray(snapshot.readySeats)) session.matchRuntime.readySeats = previous?.readySeats;
    if (snapshot.stage === 'warmup' && snapshot.warmupEndsAtUnixMs == null)
        session.matchRuntime.warmupEndsAtUnixMs = previous?.warmupEndsAtUnixMs;
    session.matchRuntimeRevision = snapshot.revision;
    if (snapshot.runtimeEpoch > Number(session.matchRuntimeEventEpoch || 0)) {
        session.matchRuntimeEventEpoch = snapshot.runtimeEpoch;
        session.matchRuntimeLastEventSequence = 0;
    }
    const live = ensureLiveData(session);
    live.pluginConnected = true;
    live.scoreA = Math.max(0, Math.floor(snapshot.scoreA));
    live.scoreB = Math.max(0, Math.floor(snapshot.scoreB));
    live.currentRound = Math.max(0, Math.floor(snapshot.completedRounds)) + (snapshot.currentRoundKey ? 1 : 0);
    live.matchController = 'caoren';
    live.matchFinished = snapshot.stage === 'completed';
    live.statsLocked = snapshot.stage === 'completed' || snapshot.stage === 'aborted';
    live.winnerTeam = snapshot.stage === 'completed'
        ? snapshot.scoreA > snapshot.scoreB ? 'A' : snapshot.scoreB > snapshot.scoreA ? 'B' : null
        : null;
    return result(true, 'APPLIED');
};

const knownEventTypes = new Set([
    'warmup_started',
    'ready_check_started',
    'roster_seat_added',
    'going_live',
    'match_started',
    'round_started',
    'round_completed',
    'halftime_started',
    'side_swapped',
    'overtime_started',
    'overtime_halftime_started',
    'match_completed',
    'match_aborted',
]);

export const applyMatchRuntimeEvent = (
    session: GameSession,
    event: MatchRuntimeEvent,
): MatchRuntimeProjectionResult => {
    const runtime = session.matchRuntime;
    if (!runtime
        || event.matchId !== session.matchId
        || event.matchId !== runtime.matchId
        || event.revision !== runtime.revision
        || event.runtimeEpoch !== runtime.runtimeEpoch)
        return result(false, 'IDENTITY_MISMATCH');
    if (!Number.isSafeInteger(event.sequence)
        || event.sequence <= Number(session.matchRuntimeLastEventSequence || 0))
        return result(false, 'DUPLICATE_OR_STALE_EVENT');
    if (!knownEventTypes.has(event.type)) return result(false, 'UNKNOWN_EVENT');

    const live = ensureLiveData(session);
    if (event.type === 'roster_seat_added') {
        const seat = event.addedSeat;
        const candidate = seat && Object.values(session.players).find(player =>
            player.playerId === seat.seatId && player.steamId === seat.steamId);
        if (!candidate || candidate.role !== 'Spectator' || (seat?.roster !== 'a' && seat?.roster !== 'b'))
            return result(false, 'INVALID_ROSTER_UPDATE');
        const team = seat.roster === 'a' ? 'A' : 'B';
        candidate.role = 'Player';
        candidate.rosterTeam = team;
        candidate.gameRole = 'Soldier';
        candidate.isReady = false;
        if (!session.teams[team].players.includes(candidate.playerId))
            session.teams[team].players.push(candidate.playerId);
    } else if (event.type === 'match_started') {
        if (session.phase === GamePhase.PreGameSetup) session.phase = GamePhase.LiveGame;
        live.formalStatsStarted = true;
        live.formalRoundStartRaw = 1;
    } else if (event.type === 'round_started' || event.type === 'round_completed') {
        if (live.formalStatsStarted === true) applyRuntimeRoundStatistics(session, event);
    } else if (event.type === 'match_completed') {
        live.matchFinished = true;
        live.statsLocked = true;
    } else if (event.type === 'match_aborted') {
        live.statsLocked = true;
    }
    session.matchRuntimeEventEpoch = event.runtimeEpoch;
    session.matchRuntimeLastEventSequence = event.sequence;
    return result(true, 'APPLIED');
};

export const applyMatchCombatBatch = (session: GameSession, batch: any): number => {
    const runtime = session.matchRuntime;
    if (!runtime || !batch || batch.matchId !== runtime.matchId || batch.revision !== runtime.revision
        || batch.runtimeEpoch !== runtime.runtimeEpoch || !Array.isArray(batch.events)
        || batch.events.length > 128 || !Number.isSafeInteger(batch.through) || batch.through < 0)
        return 0;

    let previousSequence = 0;
    for (const item of batch.events) {
        const event = item?.payload;
        if (!RUNTIME_COMBAT_EVENT_TYPES.has(item?.type) || !event
            || event.matchId !== runtime.matchId || event.revision !== runtime.revision
            || event.runtimeEpoch !== runtime.runtimeEpoch || !eventRoundNumber(event)
            || typeof event.eventId !== 'string' || !/^[a-f0-9]{32}$/.test(event.eventId)
            || !Number.isSafeInteger(event.eventSequence) || event.eventSequence <= previousSequence
            || !Number.isFinite(event.occurredAtUnixMs) || event.occurredAtUnixMs <= 0)
            return 0;
        previousSequence = event.eventSequence;
    }
    if (previousSequence !== batch.through) return 0;

    const live = ensureLiveData(session);
    live.formalTelemetryPending = batch.events.length > 0;
    live.formalTelemetryIncomplete = batch.incomplete === true;
    for (const entry of batch.events) applyRuntimeCombatStatistics(session, entry.type, entry.payload);
    return previousSequence;
};

export interface MatchRuntimeHeartbeatPayload {
    matchRuntimeEnabled?: boolean;
    matchRuntime?: MatchRuntimeSnapshot;
    matchRuntimeEvents?: MatchRuntimeEvent[];
    demoSegments?: unknown;
    restorePoints?: unknown;
}

export const applyMatchRuntimeHeartbeat = (
    session: GameSession,
    payload: MatchRuntimeHeartbeatPayload,
): { snapshotApplied: boolean; eventsApplied: number } => {
    if (payload.matchRuntimeEnabled !== true || !isBridgeMatchRuntimeEnabled(session)) return { snapshotApplied: false, eventsApplied: 0 };
    const snapshotApplied = payload.matchRuntime
        ? applyMatchRuntimeSnapshot(session, payload.matchRuntime).applied
        : false;
    let eventsApplied = 0;
    const events = Array.isArray(payload.matchRuntimeEvents)
        ? payload.matchRuntimeEvents
            .filter((event): event is MatchRuntimeEvent => !!event
                && typeof event === 'object'
                && Number.isSafeInteger((event as MatchRuntimeEvent).sequence))
            .sort((left, right) => left.sequence - right.sequence)
        : [];
    for (const event of events) {
        if (applyMatchRuntimeEvent(session, event).applied) eventsApplied++;
    }
    if (snapshotApplied && Array.isArray(payload.demoSegments)) {
        const live = ensureLiveData(session);
        const incoming = payload.demoSegments
            .filter((item): item is string => typeof item === 'string' && item.trim().length > 0)
            .map(item => item.trim().slice(0, 180));
        live.demoSegments = Array.from(new Set([...(live.demoSegments || []), ...incoming])).slice(-32);
    }
    if (snapshotApplied && Array.isArray(payload.restorePoints))
        session.matchRestorePoints = clone(payload.restorePoints.slice(0, 64));
    return { snapshotApplied, eventsApplied };
};

export const shouldTreatRoundStartAsLegacyMatchzy = (
    session: GameSession,
    eventType: string,
): boolean => eventType === 'round_start'
    && session.matchOptions?.matchMode !== 'duel'
    && session.matchOptions?.matchController === 'matchzy'
    && !isBridgeMatchRuntimeEnabled(session);
