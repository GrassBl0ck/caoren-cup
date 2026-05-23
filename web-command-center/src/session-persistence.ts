import fs from 'fs';
import path from 'path';
import { GameSession } from './types';
import { createInitialSession, getSession, setSession } from './session-manager';

const SNAPSHOT_VERSION = 1;
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

const sanitizeSessionForSnapshot = (session: GameSession) => ({
    version: SNAPSHOT_VERSION,
    savedAt: Date.now(),
    session: {
        phase: session.phase,
        matchId: session.matchId,
        players: sanitizePlayersForSnapshot(session.players),
        playerOrder: session.playerOrder,
        teams: session.teams,
        captains: session.captains,
        selectedMap: session.selectedMap,
        selectedSide: session.selectedSide,
        matchOptions: session.matchOptions,
        liveGameData: session.liveGameData,
        accusations: session.accusations,
        taskTemplate: session.taskTemplate,
        questionsUsed: session.questionsUsed,
        currentQuestion: session.currentQuestion,
        questionAnswer: session.questionAnswer,
        secondQuestionAnswered: session.secondQuestionAnswered,
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
    restored.matchOptions = restored.matchOptions || base.matchOptions;
    restored.accusations = restored.accusations || {};
    restored.adminLock = restored.adminLock || { holderId: null, acquiredAt: null };
    restored.timerEndAt = null;
    restored.timerPhase = null;
    restored.rollTimeout = undefined;
    for (const player of Object.values(restored.players)) {
        if (player.gameRole !== 'Undercover') player.undercoverTaskAckStage = undefined;
        else if (player.undercoverTaskAckStage !== 'received' && player.undercoverTaskAckStage !== 'read') player.undercoverTaskAckStage = 'none';
    }
    return restored;
};

export const restoreSessionSnapshot = (): boolean => {
    if (!fs.existsSync(SNAPSHOT_PATH)) return false;
    try {
        const parsed = JSON.parse(fs.readFileSync(SNAPSHOT_PATH, 'utf8'));
        if (parsed?.version !== SNAPSHOT_VERSION || !parsed?.session) return false;
        setSession(normalizeRestoredSession(parsed.session));
        return true;
    } catch (err) {
        console.warn('[SessionPersistence] failed to restore snapshot:', err);
        return false;
    }
};

export const saveSessionSnapshotNow = () => {
    try {
        fs.mkdirSync(SNAPSHOT_DIR, { recursive: true });
        const payload = sanitizeSessionForSnapshot(getSession());
        fs.writeFileSync(SNAPSHOT_PATH, JSON.stringify(payload, null, 2), 'utf8');
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
