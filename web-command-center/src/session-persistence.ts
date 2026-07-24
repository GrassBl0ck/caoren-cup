import fs from 'fs';
import path from 'path';
import { GameSession } from './types';
import { createInitialSession, getSession, setSession } from './session-manager';
import { clearFlowUndoHistory } from './flow-undo-manager';

const SNAPSHOT_VERSION = 2;
const SNAPSHOT_DIR = path.resolve(__dirname, '..', 'runtime');
const SNAPSHOT_PATH = path.join(SNAPSHOT_DIR, 'live-session-snapshot.json');

let saveTimer: NodeJS.Timeout | null = null;

const clonePlain = <T>(value: T): T => JSON.parse(JSON.stringify(value));

const sanitizePlayersForSnapshot = (players: GameSession['players']) => {
    const result: GameSession['players'] = {};
    for (const [playerId, player] of Object.entries(players || {})) {
        const {
            sessionCode,
            bindCode,
            ...safePlayer
        } = player;
        result[playerId] = safePlayer;
    }
    return result;
};

export interface SessionSnapshotPayloadV2 {
    version: 2;
    savedAt: number;
    session: Record<string, unknown>;
}

export const buildSessionSnapshotPayload = (session: GameSession): SessionSnapshotPayloadV2 => ({
    version: SNAPSHOT_VERSION,
    savedAt: Date.now(),
    session: {
        sessionId: session.sessionId,
        phase: session.phase,
        matchId: session.matchId,
        lobbyAccess: session.lobbyAccess,
        players: sanitizePlayersForSnapshot(session.players),
        playerOrder: session.playerOrder,
        teams: session.teams,
        captains: session.captains,
        rollValues: session.rollValues,
        draftOrder: session.draftOrder,
        draftOriginalOrder: session.draftOriginalOrder,
        draftIndex: session.draftIndex,
        draftCaptainsActive: session.draftCaptainsActive,
        draftPickTimeoutAt: session.draftPickTimeoutAt,
        mapPool: session.mapPool,
        bannedMaps: session.bannedMaps,
        selectedMap: session.selectedMap,
        currentBanTeam: session.currentBanTeam,
        banSequence: session.banSequence,
        mapVote: session.mapVote,
        sidePickTeam: session.sidePickTeam,
        sideVote: session.sideVote,
        selectedSide: session.selectedSide,
        matchOptions: session.matchOptions,
        undercoverCount: session.undercoverCount,
        detectiveCount: session.detectiveCount,
        rolesReleased: session.rolesReleased,
        duelTempAdminId: session.duelTempAdminId,
        duelAdminVote: session.duelAdminVote,
        duelAdminRequest: session.duelAdminRequest,
        duelTerminateRequest: session.duelTerminateRequest,
        liveGameData: session.liveGameData,
        accusations: session.accusations,
        taskTemplate: session.taskTemplate,
        questionsUsed: session.questionsUsed,
        currentQuestion: session.currentQuestion,
        questionAnswer: session.questionAnswer,
        secondQuestionAnswered: session.secondQuestionAnswered,
        timerEndAt: session.timerEndAt,
        timerPhase: session.timerPhase,
        adminLock: session.adminLock,
        createdAt: session.createdAt,
        autoClearMinutes: session.autoClearMinutes,
    },
});

const normalizeRestoredSession = (raw: any): GameSession => {
    const base = createInitialSession();
    const restored = {
        ...base,
        ...clonePlain(raw),
        rollTimeout: undefined,
    } as GameSession;

    restored.players = restored.players || {};
    restored.playerOrder = Array.isArray(restored.playerOrder) ? restored.playerOrder : Object.keys(restored.players);
    restored.teams = restored.teams || base.teams;
    restored.captains = restored.captains || base.captains;
    restored.rollValues = restored.rollValues || base.rollValues;
    restored.draftOrder = Array.isArray(restored.draftOrder) ? restored.draftOrder : base.draftOrder;
    restored.draftOriginalOrder = Array.isArray(restored.draftOriginalOrder) ? restored.draftOriginalOrder : base.draftOriginalOrder;
    restored.draftIndex = Number.isFinite(restored.draftIndex) ? restored.draftIndex : base.draftIndex;
    restored.mapPool = Array.isArray(restored.mapPool) ? restored.mapPool : base.mapPool;
    restored.bannedMaps = Array.isArray(restored.bannedMaps) ? restored.bannedMaps : base.bannedMaps;
    restored.banSequence = Array.isArray(restored.banSequence) ? restored.banSequence : base.banSequence;
    restored.matchOptions = {
        ...base.matchOptions,
        ...(restored.matchOptions || {}),
    };
    restored.lobbyAccess = restored.lobbyAccess?.inviteCode
        ? restored.lobbyAccess
        : base.lobbyAccess;
    restored.matchOptions.matchMode = restored.matchOptions.matchMode === 'duel' ? 'duel' : 'competitive';
    restored.matchOptions.matchController = restored.matchOptions.matchMode === 'duel' ? 'caoren' : 'matchzy';
    restored.accusations = restored.accusations || {};
    restored.adminLock = restored.adminLock || { holderId: null, acquiredAt: null };
    restored.duelTempAdminId = restored.duelTempAdminId || null;
    restored.duelAdminVote = undefined;
    restored.duelAdminRequest = restored.duelAdminRequest;
    restored.duelTerminateRequest = restored.duelTerminateRequest;
    restored.rollTimeout = undefined;
    for (const player of Object.values(restored.players)) {
        if (player.gameRole !== 'Undercover') player.undercoverTaskAckStage = undefined;
        else if (player.undercoverTaskAckStage !== 'received' && player.undercoverTaskAckStage !== 'read') player.undercoverTaskAckStage = 'none';
    }
    return restored;
};

export const restoreSessionSnapshotData = (parsed: any): boolean => {
    if (!parsed?.session || (parsed.version !== 1 && parsed.version !== SNAPSHOT_VERSION)) return false;
    const restored = normalizeRestoredSession(parsed.session);
    setSession(restored);
    clearFlowUndoHistory();
    return true;
};

export const restoreSessionSnapshot = (): boolean => {
    if (!fs.existsSync(SNAPSHOT_PATH)) return false;
    try {
        const parsed = JSON.parse(fs.readFileSync(SNAPSHOT_PATH, 'utf8'));
        return restoreSessionSnapshotData(parsed);
    } catch (err) {
        console.warn('[SessionPersistence] failed to restore snapshot:', err);
        return false;
    }
};

export const saveSessionSnapshotNow = () => {
    try {
        fs.mkdirSync(SNAPSHOT_DIR, { recursive: true });
        const payload = buildSessionSnapshotPayload(getSession());
        const tempPath = `${SNAPSHOT_PATH}.tmp`;
        fs.writeFileSync(tempPath, JSON.stringify(payload, null, 2), 'utf8');
        fs.renameSync(tempPath, SNAPSHOT_PATH);
    } catch (err) {
        console.warn('[SessionPersistence] failed to save snapshot:', err);
    }
};

export const scheduleSessionSnapshotSave = () => {
    if (saveTimer) clearTimeout(saveTimer);
    saveTimer = setTimeout(() => {
        saveTimer = null;
        saveSessionSnapshotNow();
    }, 500);
};
