export enum GamePhase {
    Lobby = 'Lobby',
    CaptainSelection = 'CaptainSelection',
    Roll = 'Roll',
    PlayerDraft = 'PlayerDraft',
    MapBan = 'MapBan',
    SidePick = 'SidePick',
    PreGameSetup = 'PreGameSetup',
    LiveGame = 'LiveGame',
    MidGameQA = 'MidGameQA',
    PostGameAccusation = 'PostGameAccusation',
    Scoreboard = 'Scoreboard',
}

export type PlayerRole = 'Player' | 'Spectator' | 'Admin';
export type GameRole = 'Soldier' | 'Undercover' | 'Detective';
export type Team = 'CT' | 'T' | 'Unassigned';
export type RosterTeam = 'A' | 'B';
export type CellStatus = 'Incomplete' | 'Partial' | 'Complete' | 'Abandoned';
export type UndercoverTaskAckStage = 'none' | 'received' | 'read';

export type NTaskType = '3N_multi' | '3N_single' | '5_4N_multi' | '5_4N_single' | 'none';

export interface TaskActionLogEntry {
    id: string;
    timestamp: number;
    round: number;
    playerId: string;
    playerName: string;
    cellId: string;
    taskDescription: string;
    action: string;
    beforeStatus: CellStatus;
    afterStatus: CellStatus;
    beforeNValue?: number;
    afterNValue?: number;
    beforeCompletedRound?: number;
    afterCompletedRound?: number;
    beforeHintUsed?: boolean;
    afterHintUsed?: boolean;
    beforeReplaced?: boolean;
    afterReplaced?: boolean;
}

export interface TaskCell {
    cellId: string;
    description: string;
    level: number;
    levelLabel?: string;
    type: 'count' | 'damage' | 'custom';
    targetCount?: number;
    currentCount?: number;
    status: CellStatus;

    nType: NTaskType;
    nMin?: number;
    nMax?: number;
    baseLevel?: number;
    extraLevel?: number;
    nValue?: number;

    isHintUsed: boolean;
    isReplaced?: boolean;
    borderHistory?: string[];

    requiresN?: boolean;
    completedRound?: number;
    progressRounds?: number[];
}

export interface MatchStats {
    kills: number;
    deaths: number;
    assists: number;
    damage: number;
    [key: string]: any;
}

export interface SideMatchStats {
    CT: MatchStats;
    T: MatchStats;
}

export interface Player {
    playerId: string;
    name: string;
    role: PlayerRole;
    gameRole?: GameRole;
    steamId?: string;
    sessionCode?: string;
    bindCode?: string;
    rosterTeam?: RosterTeam;
    team?: Team;
    isReady: boolean;
    undercoverTaskAckStage?: UndercoverTaskAckStage;
    stats?: MatchStats;
    sideStats?: SideMatchStats;
    taskGrid?: Record<string, TaskCell>;
    taskActionLog?: TaskActionLogEntry[];
    abandonCount?: number;
    replaceCount?: number;
    hintUsedCount?: number;
    detectiveQuestionCount?: number;
    finalScore?: number;
    scoreBreakdown?: Record<string, number>;
    [key: string]: any;
}

export interface TeamData {
    name: RosterTeam;
    players: string[];
}

export interface AdminLock {
    holderId: string | null;
    acquiredAt: number | null;
}

export interface TaskTemplate {
    cells: Record<string, Partial<TaskCell>>;
    lines: string[][];
    replacementTask: { level: number; description: string };
}

export interface MapVoteState {
    team: RosterTeam;
    votes: Record<string, string>;
    timeoutAt: number;
    banCount?: number;
}

export interface SideVoteState {
    team: RosterTeam;
    votes: Record<string, 'CT' | 'T'>;
    timeoutAt: number;
}

export interface PluginLivePlayer {
    steamId: string;
    name: string;
    team?: Team;
    kills: number;
    deaths: number;
    assists: number;
    damage: number;
    isAlive?: boolean;
}

export interface LiveGameData {
    scoreCT: number;
    scoreT: number;
    scoreA: number;
    scoreB: number;
    currentRound: number;
    pluginConnected: boolean;
    lastPluginHeartbeatAt?: number;
    winnerTeam: RosterTeam | null;
    matchFinished: boolean;
    winTarget: number;
    lastScoredRound: number;
    mapName?: string;
    players?: Record<string, PluginLivePlayer>;
    killMatrix?: Record<string, Record<string, number>>;
    openingKillMatrix?: Record<string, Record<string, number>>;
    awpKillMatrix?: Record<string, Record<string, number>>;
    firstKillRounds?: Record<string, boolean>;
    suppressSnapshotStatsUntil?: number;
    rawPluginRound?: number;
    roundBaseOffset?: number;
    formalRoundStartRaw?: number;
    formalStatsStarted?: boolean;
    [key: string]: any;
}
export interface MatchOptions {
    undercoverModeEnabled: boolean;
    caorenModifiersEnabled: boolean;
}

export interface GameSession {
    sessionId: string;
    phase: GamePhase;
    matchId: string;
    matchOptions: MatchOptions;
    players: Record<string, Player>;
    playerOrder: string[];
    teams: {
        A: TeamData;
        B: TeamData;
    };
    captains: {
        A: string | null;
        B: string | null;
    };
    rollValues: { A: number | null; B: number | null };
    draftOrder: RosterTeam[];
    draftIndex: number;
    draftPickTimeoutAt?: number | null;
    mapPool: string[];
    bannedMaps: string[];
    selectedMap: string | null;
    currentBanTeam: RosterTeam | null;
    banSequence: RosterTeam[];
    mapVote?: MapVoteState;
    sidePickTeam: RosterTeam | null;
    sideVote?: SideVoteState;
    selectedSide: 'CT' | 'T' | null;
    undercoverCount: number;
    detectiveCount: number;
    rolesReleased?: boolean;
    taskTemplate?: TaskTemplate;
    questionsUsed: number;
    currentQuestion: string | null;
    questionAnswer: string | null;
    secondQuestionAnswered?: boolean;
    accusations: Record<string, { own: string | null; enemy: string | null }>;
    timerEndAt: number | null;
    timerPhase: GamePhase | null;
    adminLock: AdminLock;
    liveGameData?: LiveGameData;
    rollTimeout?: any;
    createdAt: number;
    autoClearMinutes: number;
    [key: string]: any;
}

export enum WsEvents {
    LOGIN = 'LOGIN',
    RESUME = 'RESUME',
    ADMIN_ACTION = 'ADMIN_ACTION',
    VOTE = 'VOTE',
    DRAFT_PICK = 'DRAFT_PICK',
    SIDE_PICK = 'SIDE_PICK',
    TASK_ACTION = 'TASK_ACTION',
    SUBMIT_QUESTION = 'SUBMIT_QUESTION',
    UNDERCOVER_TASK_ACK = 'UNDERCOVER_TASK_ACK',
    LOGIN_RESPONSE = 'LOGIN_RESPONSE',
    GAME_STATE = 'GAME_STATE',
    PRIVATE_DATA = 'PRIVATE_DATA',
    NOTIFICATION = 'NOTIFICATION',
}
