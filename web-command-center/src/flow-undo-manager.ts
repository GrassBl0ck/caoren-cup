import { randomUUID } from 'node:crypto';
import {
    FlowUndoActionType,
    FlowUndoRequest,
    FlowUndoStatus,
    GamePhase,
    GameSession,
    Player,
} from './types';

const MAX_FLOW_UNDO_ENTRIES = 50;

const SESSION_FLOW_KEYS = [
    'matchId',
    'phase',
    'matchOptions',
    'teams',
    'captains',
    'rollValues',
    'draftOrder',
    'draftOriginalOrder',
    'draftIndex',
    'draftCaptainsActive',
    'draftPickTimeoutAt',
    'mapPool',
    'bannedMaps',
    'selectedMap',
    'currentBanTeam',
    'banSequence',
    'mapVote',
    'sidePickTeam',
    'sideVote',
    'selectedSide',
    'undercoverCount',
    'detectiveCount',
    'rolesReleased',
    'taskTemplate',
    'questionsUsed',
    'currentQuestion',
    'questionAnswer',
    'secondQuestionAnswered',
    'accusations',
    'timerEndAt',
    'timerPhase',
    'liveGameData',
] as const;

const PLAYER_FLOW_KEYS = [
    'role',
    'rosterTeam',
    'team',
    'isReady',
    'gameRole',
    'undercoverTaskAckStage',
    'stats',
    'sideStats',
    'taskGrid',
    'taskActionLog',
    'abandonCount',
    'replaceCount',
    'hintUsedCount',
    'detectiveQuestionCount',
    'finalScore',
    'scoreBreakdown',
] as const;

interface FlowUndoSnapshot {
    session: Record<string, unknown>;
    players: Record<string, Record<string, unknown>>;
}

export interface FlowUndoEntry {
    id: string;
    sessionId: string;
    actionType: FlowUndoActionType;
    actorId: string;
    actorName: string;
    summary: string;
    createdAt: number;
    restorePhase: GamePhase;
    snapshot: FlowUndoSnapshot;
}

export interface FlowUndoState {
    sessionId: string | null;
    entries: FlowUndoEntry[];
}

export interface PushFlowUndoCheckpointInput {
    actionType: FlowUndoActionType;
    actorId: string;
    actorName: string;
    summary: string;
    createdAt?: number;
}

export type FlowUndoResult =
    | { ok: true; entry: FlowUndoEntry }
    | { ok: false; reason: string };

let undoState: FlowUndoState = { sessionId: null, entries: [] };

const clonePlain = <T>(value: T): T => JSON.parse(JSON.stringify(value));

const captureSnapshot = (session: GameSession): FlowUndoSnapshot => {
    const sessionFields: Record<string, unknown> = {};
    for (const key of SESSION_FLOW_KEYS) {
        const value = (session as any)[key];
        if (value !== undefined) sessionFields[key] = clonePlain(value);
    }
    const players: Record<string, Record<string, unknown>> = {};
    for (const [playerId, player] of Object.entries(session.players)) {
        const fields: Record<string, unknown> = {};
        for (const key of PLAYER_FLOW_KEYS) {
            const value = (player as any)[key];
            if (value !== undefined) fields[key] = clonePlain(value);
        }
        players[playerId] = fields;
    }
    return { session: sessionFields, players };
};

const restorePlayerFlow = (player: Player, snapshot: Record<string, unknown> | undefined) => {
    const currentRole = player.role;
    const preserveAdminRole = currentRole === 'Admin';
    for (const key of PLAYER_FLOW_KEYS) delete (player as any)[key];
    if (snapshot) {
        for (const key of PLAYER_FLOW_KEYS) {
            if (Object.prototype.hasOwnProperty.call(snapshot, key)) {
                (player as any)[key] = clonePlain(snapshot[key]);
            }
        }
    } else {
        player.role = currentRole;
        player.isReady = false;
    }
    if (preserveAdminRole) player.role = 'Admin';
    else if (!player.role) player.role = 'Player';
};

const restoreSnapshot = (session: GameSession, snapshot: FlowUndoSnapshot) => {
    for (const key of SESSION_FLOW_KEYS) delete (session as any)[key];
    for (const key of SESSION_FLOW_KEYS) {
        if (Object.prototype.hasOwnProperty.call(snapshot.session, key)) {
            (session as any)[key] = clonePlain(snapshot.session[key]);
        }
    }

    for (const [playerId, player] of Object.entries(session.players)) {
        restorePlayerFlow(player, snapshot.players[playerId]);
    }

    for (const team of ['A', 'B'] as const) {
        session.teams[team].players = session.teams[team].players.filter(playerId => !!session.players[playerId]);
    }
    if (session.captains.A && !session.players[session.captains.A]) session.captains.A = null;
    if (session.captains.B && !session.players[session.captains.B]) session.captains.B = null;
    session.accusations = Object.fromEntries(
        Object.entries(session.accusations || {}).filter(([playerId]) => !!session.players[playerId]),
    );
};

const getRequiredParticipantIds = (snapshot: FlowUndoSnapshot): string[] => {
    const required = new Set<string>();
    const teams = snapshot.session.teams as GameSession['teams'] | undefined;
    for (const team of ['A', 'B'] as const) {
        for (const playerId of teams?.[team]?.players || []) required.add(playerId);
    }
    const captains = snapshot.session.captains as GameSession['captains'] | undefined;
    if (captains?.A) required.add(captains.A);
    if (captains?.B) required.add(captains.B);
    for (const [playerId, fields] of Object.entries(snapshot.players)) {
        if (fields.role === 'Player' || fields.rosterTeam === 'A' || fields.rosterTeam === 'B') {
            required.add(playerId);
        }
    }
    return [...required];
};

const validateSnapshotParticipants = (session: GameSession, snapshot: FlowUndoSnapshot): string | null => {
    for (const playerId of getRequiredParticipantIds(snapshot)) {
        const player = session.players[playerId];
        if (!player) return `撤销记录依赖的参赛者 ${playerId} 已不在大厅，历史已失效。`;
        if (player.role === 'Spectator') return `撤销记录依赖的参赛者 ${player.name} 已变成观战者，历史已失效。`;
    }
    return null;
};

const latestEntry = (): FlowUndoEntry | undefined => undoState.entries[undoState.entries.length - 1];

const isPregamePhase = (phase: GamePhase) => [
    GamePhase.Lobby,
    GamePhase.CaptainSelection,
    GamePhase.Roll,
    GamePhase.PlayerDraft,
    GamePhase.MapBan,
    GamePhase.SidePick,
    GamePhase.PreGameSetup,
].includes(phase);

const toLatestStatus = (entry: FlowUndoEntry): NonNullable<FlowUndoStatus['latest']> => ({
    id: entry.id,
    actionType: entry.actionType,
    summary: entry.summary,
    actorId: entry.actorId,
    actorName: entry.actorName,
    createdAt: entry.createdAt,
    restorePhase: entry.restorePhase,
});

export const prepareFlowUndoCheckpoint = (
    session: GameSession,
    input: PushFlowUndoCheckpointInput,
): FlowUndoEntry => {
    return {
        id: randomUUID(),
        sessionId: session.sessionId,
        actionType: input.actionType,
        actorId: input.actorId,
        actorName: input.actorName,
        summary: input.summary,
        createdAt: input.createdAt ?? Date.now(),
        restorePhase: session.phase,
        snapshot: captureSnapshot(session),
    };
};

export const commitFlowUndoCheckpoint = (entry: FlowUndoEntry): FlowUndoEntry => {
    if (undoState.sessionId !== entry.sessionId) {
        undoState = { sessionId: entry.sessionId, entries: [] };
    }
    undoState.entries.push(entry);
    if (undoState.entries.length > MAX_FLOW_UNDO_ENTRIES) {
        undoState.entries.splice(0, undoState.entries.length - MAX_FLOW_UNDO_ENTRIES);
    }
    return entry;
};

export const pushFlowUndoCheckpoint = (
    session: GameSession,
    input: PushFlowUndoCheckpointInput,
): FlowUndoEntry => commitFlowUndoCheckpoint(prepareFlowUndoCheckpoint(session, input));

export const discardFlowUndoCheckpoint = (entryId: string): boolean => {
    const index = undoState.entries.findIndex(entry => entry.id === entryId);
    if (index < 0) return false;
    undoState.entries.splice(index, 1);
    return true;
};

export const clearFlowUndoHistory = () => {
    undoState = { sessionId: null, entries: [] };
};

export const getFlowUndoStatus = (session: GameSession, actor?: Player): FlowUndoStatus => {
    const entry = latestEntry();
    const historyDepth = undoState.sessionId === session.sessionId ? undoState.entries.length : 0;
    const base: FlowUndoStatus = {
        count: historyDepth,
        historyDepth,
        canUndo: false,
        targetPhase: undoState.sessionId === session.sessionId && entry ? entry.restorePhase : null,
        latest: undoState.sessionId === session.sessionId && entry ? toLatestStatus(entry) : undefined,
    };
    if (!isPregamePhase(session.phase)) {
        return { ...base, disabledReason: '正式比赛开始后不能撤销赛前流程。' };
    }
    const isOfficialAdmin = actor?.role === 'Admin';
    const isDuelTempAdmin = !!actor && session.duelTempAdminId === actor.playerId;
    if (!isOfficialAdmin && !isDuelTempAdmin) {
        return { ...base, disabledReason: '只有管理员才能撤销流程操作。' };
    }
    if (!entry || undoState.sessionId !== session.sessionId) {
        return { ...base, disabledReason: '当前没有可撤销的操作。' };
    }
    if (isDuelTempAdmin && !isOfficialAdmin && (
        entry.actorId !== actor.playerId ||
        entry.actionType !== 'ADVANCE_PHASE' ||
        entry.restorePhase !== GamePhase.Lobby
    )) {
        return { ...base, disabledReason: '单挑临时管理员只能撤销自己从大厅推进到赛前配置的操作。' };
    }
    return { ...base, canUndo: true, disabledReason: undefined };
};

export const undoLatestFlowAction = (
    session: GameSession,
    actor: Player,
    request?: FlowUndoRequest,
): FlowUndoResult => {
    const status = getFlowUndoStatus(session, actor);
    if (!status.canUndo) return { ok: false, reason: status.disabledReason || '当前操作不能撤销。' };
    const entry = latestEntry();
    if (!entry || entry.sessionId !== session.sessionId) {
        return { ok: false, reason: '撤销记录与当前对局不一致。' };
    }
    if (!request ||
        request.expectedPhase !== session.phase ||
        request.expectedHistoryDepth !== undoState.entries.length ||
        request.expectedEntryId !== entry.id
    ) {
        return { ok: false, reason: '当前阶段或撤销记录已变化，请刷新状态后重试。' };
    }
    const participantError = validateSnapshotParticipants(session, entry.snapshot);
    if (participantError) {
        clearFlowUndoHistory();
        return { ok: false, reason: participantError };
    }
    restoreSnapshot(session, entry.snapshot);
    undoState.entries.pop();
    return { ok: true, entry };
};

export const exportFlowUndoState = (): FlowUndoState => clonePlain(undoState);
