import { GamePhase, GameSession } from './types';

const PHASE_TRANSITIONS: Partial<Record<GamePhase, GamePhase[]>> = {
    [GamePhase.Lobby]: [GamePhase.CaptainSelection],
    [GamePhase.CaptainSelection]: [GamePhase.Roll],
    [GamePhase.Roll]: [GamePhase.PlayerDraft],
    [GamePhase.PlayerDraft]: [GamePhase.MapBan],
    [GamePhase.MapBan]: [GamePhase.SidePick],
    [GamePhase.SidePick]: [GamePhase.PreGameSetup],
    [GamePhase.PreGameSetup]: [GamePhase.LiveGame],

    // 第一阶段新增：
    // 卧底模式关闭时，LiveGame 允许直接进入 Scoreboard。
    [GamePhase.LiveGame]: [
        GamePhase.MidGameQA,
        GamePhase.PostGameAccusation,
        GamePhase.Scoreboard,
    ],

    [GamePhase.MidGameQA]: [GamePhase.PostGameAccusation, GamePhase.Scoreboard],
    [GamePhase.PostGameAccusation]: [GamePhase.Scoreboard],
    [GamePhase.Scoreboard]: [GamePhase.Lobby],
};

export function canTransition(from: GamePhase, to: GamePhase): boolean {
    const allowed = PHASE_TRANSITIONS[from];
    if (!allowed) return false;
    return allowed.includes(to);
}

export function getAutoNextPhase(phase: GamePhase): GamePhase | null {
    return null;
}

export function getPhaseDuration(phase: GamePhase): number | null {
    switch (phase) {
        case GamePhase.CaptainSelection:
            return 9999;
        case GamePhase.Roll:
            return 9999;
        case GamePhase.PlayerDraft:
            return 15;
        case GamePhase.MapBan:
            return 12;
        case GamePhase.SidePick:
            return 12;
        case GamePhase.PreGameSetup:
            return 9999;
        case GamePhase.MidGameQA:
            return 9999;
        case GamePhase.PostGameAccusation:
            return 9999;
        default:
            return null;
    }
}

export function handleTimeout(session: GameSession): Partial<GameSession> | null {
    switch (session.phase) {
        case GamePhase.Roll:
            return {};
        case GamePhase.PlayerDraft:
            return {};
        case GamePhase.MapBan:
            return {};
        case GamePhase.SidePick:
            return {};
        case GamePhase.PreGameSetup:
            return { phase: GamePhase.LiveGame };
        case GamePhase.MidGameQA:
            return { phase: GamePhase.PostGameAccusation };
        case GamePhase.PostGameAccusation:
            return { phase: GamePhase.Scoreboard };
        default:
            return null;
    }
}

export function startTimer(session: GameSession): number | null {
    const duration = getPhaseDuration(session.phase);
    if (duration === null) return null;
    return Date.now() + duration * 1000;
}