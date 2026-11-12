export type MatchRuntimeStage =
    | 'disabled'
    | 'staging'
    | 'warmup'
    | 'readyCheck'
    | 'goingLive'
    | 'firstHalf'
    | 'halftime'
    | 'secondHalf'
    | 'overtimeHalftime'
    | 'overtime'
    | 'recovering'
    | 'completed'
    | 'aborted';

export type MatchRuntimePauseState =
    | 'running'
    | 'tacticalPaused'
    | 'technicalPaused'
    | 'restorePending'
    | 'restoring';

export interface MatchRuntimeSnapshot {
    matchId: string;
    revision: number;
    runtimeEpoch: number;
    stage: MatchRuntimeStage;
    pauseState: MatchRuntimePauseState;
    scoreA: number;
    scoreB: number;
    completedRounds: number;
    overtimeIndex: number;
    overtimeRoundInStage: number;
    teamAEngineSide: 'terrorist' | 'counterTerrorist';
    sideSwapPending: boolean;
    currentRoundKey: string | null;
    lastEventSequence: number;
}

export interface MatchRuntimeEvent {
    sequence: number;
    type: string;
    matchId: string;
    revision: number;
    runtimeEpoch: number;
    roundKey?: string | null;
    roundNumber?: number;
    occurredAtUnixMs?: number;
    teamAEngineSide?: 'terrorist' | 'counterTerrorist';
    winningEngineSide?: 'terrorist' | 'counterTerrorist';
    players?: Array<{ steamId: string; team: string; isAlive?: boolean; health?: number; maxHealth?: number }>;
}

export interface MatchRuntimeProjectionResult {
    applied: boolean;
    code: string;
}

export interface MatchRuntimeRoundStatisticsJournal {
    roundKey: string;
    started?: MatchRuntimeEvent;
    completed?: MatchRuntimeEvent;
    combat: Array<{ type: string; payload: any }>;
}
