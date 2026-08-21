import fs from 'fs';
import path from 'path';
import { randomUUID } from 'crypto';
import { GamePhase, GameSession } from './types';
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
    session: Record<string, any>;
}

export const buildSessionSnapshotPayload = (
    session: GameSession,
    savedAt = Date.now(),
): SessionSnapshotPayloadV2 => ({
    version: SNAPSHOT_VERSION,
    savedAt,
    session: {
        sessionId: session.sessionId,
        phase: session.phase,
        matchId: session.matchId,
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
        abilityBanState: session.abilityBanState,
        abilityDraftState: session.abilityDraftState,
        abilityAssignments: session.abilityAssignments,
        abilitySyncRevision: session.abilitySyncRevision,
        abilitySyncState: session.abilitySyncState,
        liveGameData: session.liveGameData,
        accusations: session.accusations,
        taskTemplate: session.taskTemplate,
        questionsUsed: session.questionsUsed,
        currentQuestion: session.currentQuestion,
        questionAnswer: session.questionAnswer,
        secondQuestionAnswered: session.secondQuestionAnswered,
        adminLock: session.adminLock,
        createdAt: session.createdAt,
        autoClearMinutes: session.autoClearMinutes,
        lastActivityAt: session.lastActivityAt,
    },
});

export const serializeSessionSnapshot = buildSessionSnapshotPayload;

const normalizeRestoredSession = (raw: any, version: 1 | 2): GameSession => {
    const base = createInitialSession();
    const allowedRaw = buildSessionSnapshotPayload(raw as GameSession).session;
    const restored = {
        ...base,
        ...clonePlain(allowedRaw),
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
    // Older snapshots may contain fields removed from the current session model. Restoring
    // through the current snapshot whitelist keeps those fields out of memory and broadcasts.
    restored.matchOptions.matchMode = restored.matchOptions.matchMode === 'duel' ? 'duel' : 'competitive';
    restored.matchOptions.matchController = restored.matchOptions.matchMode === 'duel' ? 'caoren' : 'matchzy';
    restored.sidePickTeam = version === 2 && (restored.sidePickTeam === 'A' || restored.sidePickTeam === 'B')
        ? restored.sidePickTeam
        : null;
    if (version === 1) {
        if (restored.phase === GamePhase.AbilityBan || restored.phase === GamePhase.AbilityDraft) {
            restored.phase = GamePhase.PreGameSetup;
        }
        restored.matchOptions.abilityModeEnabled = false;
        restored.matchOptions.abilityBanCountPerTeam = base.matchOptions.abilityBanCountPerTeam;
        restored.matchOptions.abilityBanSeconds = base.matchOptions.abilityBanSeconds;
        restored.matchOptions.abilityDraftBatchSeconds = base.matchOptions.abilityDraftBatchSeconds;
        restored.abilityBanState = undefined;
        restored.abilityDraftState = undefined;
        restored.abilityAssignments = undefined;
    }
    restored.accusations = restored.accusations || {};
    restored.adminLock = restored.adminLock || { holderId: null, acquiredAt: null };
    restored.timerEndAt = null;
    restored.timerPhase = null;
    if (version === 2
        && restored.phase === GamePhase.AbilityBan
        && typeof restored.abilityBanState?.timeoutAt === 'number'
        && Number.isFinite(restored.abilityBanState.timeoutAt)) {
        restored.timerEndAt = restored.abilityBanState.timeoutAt;
        restored.timerPhase = GamePhase.AbilityBan;
    } else if (version === 2
        && restored.phase === GamePhase.AbilityDraft
        && !restored.abilityDraftState?.failure
        && typeof restored.abilityDraftState?.timeoutAt === 'number'
        && Number.isFinite(restored.abilityDraftState.timeoutAt)) {
        restored.timerEndAt = restored.abilityDraftState.timeoutAt;
        restored.timerPhase = GamePhase.AbilityDraft;
    }
    restored.rollTimeout = undefined;
    for (const player of Object.values(restored.players)) {
        if (player.gameRole !== 'Undercover') player.undercoverTaskAckStage = undefined;
        else if (player.undercoverTaskAckStage !== 'received' && player.undercoverTaskAckStage !== 'read') player.undercoverTaskAckStage = 'none';
    }
    return restored;
};

export const deserializeSessionSnapshot = (snapshot: unknown): GameSession | null => {
    if (!snapshot || typeof snapshot !== 'object') return null;
    const parsed = snapshot as { version?: unknown; session?: unknown };
    if ((parsed.version !== 1 && parsed.version !== 2)
        || !parsed.session
        || typeof parsed.session !== 'object'
        || Array.isArray(parsed.session)) {
        return null;
    }
    return normalizeRestoredSession(parsed.session, parsed.version);
};

export const restoreSessionSnapshotData = (parsed: unknown): boolean => {
    const restored = deserializeSessionSnapshot(parsed);
    if (!restored) return false;
    setSession(restored);
    clearFlowUndoHistory();
    return true;
};

export interface SnapshotFileSystem {
    mkdirSync: (directoryPath: string, options: { recursive: true }) => unknown;
    writeFileSync: (filePath: string, data: string, encoding: 'utf8') => unknown;
    renameSync: (oldPath: string, newPath: string) => unknown;
    unlinkSync: (filePath: string) => unknown;
}

const assertValidBpTimeouts = (payload: unknown) => {
    if (!payload || typeof payload !== 'object') return;
    const snapshot = payload as { version?: unknown; session?: unknown };
    if (snapshot.version !== SNAPSHOT_VERSION || !snapshot.session || typeof snapshot.session !== 'object') return;
    const session = snapshot.session as Record<string, unknown>;
    for (const stateKey of ['abilityBanState', 'abilityDraftState'] as const) {
        const state = session[stateKey];
        if (state === undefined) continue;
        const timeoutAt = state && typeof state === 'object'
            ? (state as { timeoutAt?: unknown }).timeoutAt
            : undefined;
        if (typeof timeoutAt !== 'number' || !Number.isFinite(timeoutAt)) {
            throw new TypeError(`${stateKey}.timeoutAt must be a finite number.`);
        }
    }
};

export const writeSnapshotAtomically = (
    snapshotPath: string,
    payload: unknown,
    fileSystem: SnapshotFileSystem = fs,
) => {
    assertValidBpTimeouts(payload);
    const snapshotDir = path.dirname(snapshotPath);
    const tempPath = path.join(
        snapshotDir,
        `.${path.basename(snapshotPath)}.${process.pid}.${Date.now()}.${randomUUID()}.tmp`,
    );
    let tempMayExist = false;
    try {
        fileSystem.mkdirSync(snapshotDir, { recursive: true });
        const serialized = JSON.stringify(payload, null, 2);
        if (serialized === undefined) throw new TypeError('Snapshot payload cannot be serialized.');
        tempMayExist = true;
        fileSystem.writeFileSync(tempPath, serialized, 'utf8');
        fileSystem.renameSync(tempPath, snapshotPath);
        tempMayExist = false;
    } catch (err) {
        if (tempMayExist) {
            try {
                fileSystem.unlinkSync(tempPath);
            } catch {
                // Best-effort cleanup only; preserve the original write error.
            }
        }
        throw err;
    }
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
        const payload = serializeSessionSnapshot(getSession());
        writeSnapshotAtomically(SNAPSHOT_PATH, payload);
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
