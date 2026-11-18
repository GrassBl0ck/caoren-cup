import { GameSession, Player } from './types';

export type UnbalancedRosterOptions = {
    unbalancedModeEnabled?: boolean;
    unbalancedTeamASize?: number;
    unbalancedTeamBSize?: number;
};

export type UnbalancedRosterValidation = {
    valid: boolean;
    blockers: string[];
    targetA: number;
    targetB: number;
    actualA: number;
    actualB: number;
    unassigned: number;
    missingSteamIds: string[];
};

const participants = (session: GameSession): Player[] =>
    Object.values(session.players).filter(p => p.role !== 'Admin' && p.role !== 'Spectator');

const positiveInt = (value: unknown, fallback: number): number => {
    const n = Math.floor(Number(value));
    return Number.isFinite(n) && n > 0 ? n : fallback;
};

export const normalizeUnbalancedRosterOptions = (raw: UnbalancedRosterOptions | undefined) => ({
    unbalancedModeEnabled: raw?.unbalancedModeEnabled === true,
    unbalancedTeamASize: positiveInt(raw?.unbalancedTeamASize, 3),
    unbalancedTeamBSize: positiveInt(raw?.unbalancedTeamBSize, 5),
});

export const validateUnbalancedRoster = (session: GameSession): UnbalancedRosterValidation => {
    const options = normalizeUnbalancedRosterOptions(session.matchOptions);
    const players = participants(session);
    const actualA = players.filter(p => p.rosterTeam === 'A').length;
    const actualB = players.filter(p => p.rosterTeam === 'B').length;
    const unassigned = players.filter(p => p.rosterTeam !== 'A' && p.rosterTeam !== 'B').length;
    const missingSteamIds = players.filter(p => !p.steamId).map(p => p.name);
    const blockers: string[] = [];
    if (!options.unbalancedModeEnabled || session.matchOptions.matchMode === 'duel') {
        return { valid: true, blockers, targetA: actualA, targetB: actualB, actualA, actualB, unassigned, missingSteamIds };
    }
    // The administrator confirms the actual rosters; retired target-size controls must not gate entry.
    if (actualA === 0 || actualB === 0) blockers.push('请由管理员确认 A/B 两队均已分配玩家。');
    if (unassigned > 0) blockers.push(`还有 ${unassigned} 名玩家未分入 A/B 队。`);
    return { valid: blockers.length === 0, blockers, targetA: actualA, targetB: actualB, actualA, actualB, unassigned, missingSteamIds };
};

export const validateUnbalancedRosterForTeamLock = (session: GameSession): UnbalancedRosterValidation => {
    const result = validateUnbalancedRoster(session);
    if (result.valid && session.matchOptions.unbalancedModeEnabled === true && result.missingSteamIds.length > 0) {
        result.blockers.push(`以下玩家未绑定 SteamID：${result.missingSteamIds.join('、')}。`);
        result.valid = false;
    }
    return result;
};
