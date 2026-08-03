import { GamePhase, GameSession } from './types';

export const GAME_INACTIVITY_TIMEOUT_MS = 2 * 60 * 60 * 1000;
export const GAME_INACTIVITY_CHECK_INTERVAL_MS = 60 * 1000;
export const GAME_INACTIVITY_TERMINATION_REASON = '本场比赛连续 2 小时没有有效操作，已自动结束。';

const buildSemanticDigest = (session: GameSession): string => {
    const semanticSession = JSON.parse(JSON.stringify(session)) as Record<string, any>;
    delete semanticSession.lastActivityAt;

    for (const player of Object.values(semanticSession.players || {}) as Record<string, any>[]) {
        delete player.isOnline;
    }

    if (semanticSession.liveGameData) {
        delete semanticSession.liveGameData.pluginConnected;
        delete semanticSession.liveGameData.lastPluginHeartbeatAt;
    }

    return JSON.stringify(semanticSession);
};

export const recordGameActivity = (session: GameSession, now = Date.now()): void => {
    if (session.phase === GamePhase.Lobby) return;
    session.lastActivityAt = now;
};

export class GameInactivityTracker {
    private sessionId = '';
    private semanticDigest = '';

    observe(session: GameSession, now = Date.now()): boolean {
        const nextDigest = buildSemanticDigest(session);
        const sameSession = this.sessionId === session.sessionId;
        let activityRecorded = false;

        if (session.phase !== GamePhase.Lobby) {
            if (!Number.isFinite(session.lastActivityAt)) {
                recordGameActivity(session, now);
                activityRecorded = true;
            } else if (sameSession && this.semanticDigest && this.semanticDigest !== nextDigest) {
                recordGameActivity(session, now);
                activityRecorded = true;
            }
        }

        this.sessionId = session.sessionId;
        this.semanticDigest = nextDigest;
        return activityRecorded;
    }

    isExpired(session: GameSession, now = Date.now()): boolean {
        return session.phase !== GamePhase.Lobby
            && Number.isFinite(session.lastActivityAt)
            && now - Number(session.lastActivityAt) >= GAME_INACTIVITY_TIMEOUT_MS;
    }
}

export class GameInactivityMonitor {
    private terminating = false;

    constructor(
        private readonly tracker: GameInactivityTracker,
        private readonly getSession: () => GameSession,
        private readonly terminate: (reason: string) => Promise<void>,
        private readonly persistActivity?: () => void,
    ) {}

    async tick(now = Date.now()): Promise<void> {
        const session = this.getSession();
        if (this.tracker.observe(session, now)) this.persistActivity?.();
        if (this.terminating || !this.tracker.isExpired(session, now)) return;

        this.terminating = true;
        try {
            await this.terminate(GAME_INACTIVITY_TERMINATION_REASON);
        } finally {
            this.terminating = false;
        }
    }
}
