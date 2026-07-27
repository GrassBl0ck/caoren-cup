import {
    normalizeDuelRoundTimeMinutes,
    normalizeDuelRounds,
    normalizeDuelUtilityMode,
} from './duel-config';

type DuelRuntimeMatchOptions = {
    duelRounds?: unknown;
    duelRoundTimeMinutes?: unknown;
    duelUtilityMode?: unknown;
} | null | undefined;

export function buildDuelRuntimeConfigPayload(
    matchId: string,
    matchOptions: DuelRuntimeMatchOptions,
    requestedAt: number,
) {
    const normalizedMatchId = String(matchId || '').trim();
    if (!normalizedMatchId) throw new Error('matchId is required for duel runtime configuration.');

    return {
        matchId: normalizedMatchId,
        rounds: normalizeDuelRounds(matchOptions?.duelRounds),
        roundTimeMinutes: normalizeDuelRoundTimeMinutes(matchOptions?.duelRoundTimeMinutes),
        utilityMode: normalizeDuelUtilityMode(matchOptions?.duelUtilityMode),
        requestedAt,
    };
}
