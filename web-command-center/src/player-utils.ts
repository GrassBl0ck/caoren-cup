// player-utils.ts
import { GameSession, Player, Team, RosterTeam } from './types';

// ========== Player utilities ==========
export const findPlayerById = (session: GameSession, id: string): Player | undefined =>
    session.players[id];

export const generateBindCode = (): string =>
    Math.floor(1000 + Math.random() * 9000).toString();

export const normalizeLoginText = (value: unknown): string => String(value || '').trim();

export const findPlayerByBindCode = (session: GameSession, bindCode: unknown): Player | undefined => {
    const code = normalizeLoginText(bindCode);
    if (!code) return undefined;
    return Object.values(session.players).find(p => p.bindCode === code);
};

export const findPlayerByName = (session: GameSession, name: unknown): Player | undefined => {
    const normalized = normalizeLoginText(name);
    if (!normalized) return undefined;
    return Object.values(session.players).find(p => p.name === normalized);
};

export const getGamePlayers = (session: GameSession): Player[] =>
    Object.values(session.players).filter(p => p.role !== 'Spectator' && p.role !== 'Admin');

export const getDuelParticipants = (session: GameSession): Player[] =>
    getGamePlayers(session).filter(p => p.rosterTeam === 'A' || p.rosterTeam === 'B');

// Keep this helper session-scoped so callers do not read stale global state.
export const hasAnyDetective = (session: GameSession): boolean =>
    Object.values(session.players).some(p => p.gameRole === 'Detective' && p.role !== 'Spectator' && p.role !== 'Admin');

// Player queries
export const getTeamPlayers = (session: GameSession, team: RosterTeam): Player[] =>
    getGamePlayers(session).filter(p => p.rosterTeam === team || session.teams[team].players.includes(p.playerId));

// ========== Steam ID / team normalization ==========
export const normalizeSteamId = (steamId: unknown): string => String(steamId || '').replace(/[^0-9]/g, '');

export const findPlayerBySteamId = (session: GameSession, steamId: unknown): Player | undefined => {
    const normalized = normalizeSteamId(steamId);
    if (!normalized) return undefined;
    return getGamePlayers(session).find(p => normalizeSteamId(p.steamId) === normalized);
};

export const normalizeTeam = (team: unknown): Team | undefined => {
    const value = String(team || '').toUpperCase();
    if (value === 'CT' || value === 'COUNTERTERRORIST' || value === 'COUNTER_TERRORIST' || value === '3') return 'CT';
    if (value === 'T' || value === 'TERRORIST' || value === '2') return 'T';
    return undefined;
};

export const otherRosterTeam = (team: RosterTeam): RosterTeam => team === 'A' ? 'B' : 'A';
export const oppositeSide = (side: Team): Team => side === 'CT' ? 'T' : 'CT';

// ========== Permissions / public state helpers ==========
export const shouldRevealRoleToViewer = (viewer: Player | undefined, target: Player, rolesReleased: boolean): boolean => {
    if (!target.gameRole) return false;
    if (viewer?.role === 'Admin') return true;
    if (viewer?.playerId === target.playerId && rolesReleased) return true;
    return false;
};

export const shouldRevealTaskGridToViewer = (viewer: Player | undefined, target: Player, rolesReleased: boolean): boolean => {
    if (!target.taskGrid) return false;
    if (viewer?.role === 'Admin') return true;
    if (viewer?.playerId === target.playerId && rolesReleased) return true;
    return false;
};

export const shouldRevealTaskActionLogToViewer = (viewer: Player | undefined, target: Player, rolesReleased: boolean): boolean => {
    if (!target.taskActionLog) return false;
    if (viewer?.role === 'Admin') return true;
    if (viewer?.playerId === target.playerId && rolesReleased) return true;
    return false;
};

export const sanitizeForPublic = (session: GameSession, viewerId?: string | null): any => {
    const viewer = viewerId ? session.players[viewerId] : undefined;
    const revealAllPostgame = session.phase === 'Scoreboard';
    const s: any = { ...session };
    s.serverNow = Date.now();
    s.players = {};
    for (const [id, p] of Object.entries(session.players)) {
        const revealRole = revealAllPostgame || shouldRevealRoleToViewer(viewer, p, session.rolesReleased);
        const revealTaskGrid = revealAllPostgame || shouldRevealTaskGridToViewer(viewer, p, session.rolesReleased);
        const revealTaskActionLog = revealAllPostgame || shouldRevealTaskActionLogToViewer(viewer, p, session.rolesReleased);
        s.players[id] = {
            playerId: p.playerId, name: p.name, role: p.role,
            gameRole: revealRole ? p.gameRole : undefined,
            rosterTeam: p.rosterTeam, team: p.team, isReady: p.isReady,
            undercoverTaskAckStage: p.undercoverTaskAckStage,
            steamIdBound: !!p.steamId,
            steamId: p.steamId,
            finalScore: p.finalScore, scoreBreakdown: p.scoreBreakdown,
            stats: p.stats,
            sideStats: p.sideStats,
            detectiveQuestionCount: revealAllPostgame || viewer?.role === 'Admin' || p.playerId === viewerId ? p.detectiveQuestionCount : undefined,
            taskGrid: revealTaskGrid ? p.taskGrid : undefined,
            taskActionLog: revealTaskActionLog ? p.taskActionLog : undefined,
        };
    }
    s.duelAdminOnline = Object.values(session.players).some(p => p.role === 'Admin' && p.isOnline);
    delete s.rollTimeout;
    return s;
};

// ========== CSV export helpers ==========
export const toNumber = (value: unknown): number => {
    const n = Number(String(value ?? '').trim());
    return Number.isFinite(n) ? n : 0;
};

export const parseCsvLine = (line: string): string[] => {
    const result: string[] = [];
    let current = '';
    let inQuotes = false;
    for (let i = 0; i < line.length; i++) {
        const ch = line[i];
        const next = line[i + 1];
        if (ch === '"' && inQuotes && next === '"') { current += '"'; i++; continue; }
        if (ch === '"') { inQuotes = !inQuotes; continue; }
        if (ch === ',' && !inQuotes) { result.push(current.trim()); current = ''; continue; }
        current += ch;
    }
    result.push(current.trim());
    return result;
};
