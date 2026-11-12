import { isCurrentAbilitySyncConfirmed } from './ability-phase-one-policy';
import type { BridgeCommand } from './plugin-command-queue';
import { enqueuePluginCommand } from './plugin-command-queue';
import { getGamePlayers } from './player-utils';
import type { GameSession } from './types';
import type { MatchRuntimeStage } from './match-runtime-types';

export const isBridgeMatchRuntimeEnabled = (session: GameSession): boolean =>
    session.matchOptions?.matchRuntimeEnabled === true
    && session.matchOptions?.matchController === 'caoren'
    && session.matchOptions?.matchMode !== 'duel'
    && ['competitive', 'ability'].includes(session.matchOptions?.gameModeId || 'competitive');

export const MATCH_RUNTIME_COMMAND_TYPE = 'MATCH_RUNTIME_COMMAND';

export type MatchRuntimeAction =
    | 'load'
    | 'begin_warmup'
    | 'begin_ready_check'
    | 'begin_going_live'
    | 'confirm_side_swap'
    | 'tactical_pause'
    | 'technical_pause'
    | 'confirm_unpause'
    | 'force_pause'
    | 'force_unpause'
    | 'substitute'
    | 'restore'
    | 'abort';

const normalizeMapName = (value: string): string => {
    const normalized = String(value || '').trim().toLowerCase().replace(/[^a-z0-9_]/g, '_');
    if (!normalized) return '';
    return normalized.startsWith('de_') ? normalized : `de_${normalized}`;
};

export const buildMatchRuntimeDefinition = (session: GameSession): Record<string, unknown> | null => {
    if ((session.selectedSide !== 'CT' && session.selectedSide !== 'T') || !session.selectedMap) return null;
    const players = getGamePlayers(session).filter(player => player.rosterTeam === 'A' || player.rosterTeam === 'B');
    if (players.length === 0 || players.some(player => !/^7656119[0-9]{10}$/.test(String(player.steamId || '')))) return null;
    if (!players.some(player => player.rosterTeam === 'A') || !players.some(player => player.rosterTeam === 'B')) return null;
    return {
        action: 'load',
        matchRuntimeEnabled: true,
        matchId: session.matchId,
        revision: session.matchOptions.abilityModeEnabled === true
            && session.abilitySyncState?.status === 'confirmed'
            && session.abilitySyncState.matchId === session.matchId
                ? session.abilitySyncState.revision
                : Math.max(1, Math.floor(Number(session.matchRuntimeRevision || 1))),
        mapName: normalizeMapName(session.selectedMap),
        teamAName: String(session.matchOptions.teamAName || 'A队').trim().slice(0, 32) || 'A队',
        teamBName: String(session.matchOptions.teamBName || 'B队').trim().slice(0, 32) || 'B队',
        teamAInitialSide: session.selectedSide,
        seats: players.map(player => ({
            seatId: player.playerId,
            roster: player.rosterTeam,
            steamId: player.steamId,
            name: player.name,
            isCaptain: session.captains[player.rosterTeam!] === player.playerId,
        })),
    };
};

export const enqueueMatchRuntimeCommand = (
    session: GameSession,
    action: MatchRuntimeAction,
    payload: Record<string, unknown> = {},
    expectedStage?: MatchRuntimeStage,
): BridgeCommand | null => {
    if (!isBridgeMatchRuntimeEnabled(session) || session.bridgeProtocolCapabilities?.matchRuntime !== 1) return null;
    if (action === 'load') {
        if (session.matchOptions.abilityModeEnabled === true && !isCurrentAbilitySyncConfirmed(session)) return null;
        const loadPayload = buildMatchRuntimeDefinition(session);
        return loadPayload ? enqueuePluginCommand(MATCH_RUNTIME_COMMAND_TYPE, loadPayload) : null;
    }
    const runtime = session.matchRuntime;
    if (!runtime || runtime.matchId !== session.matchId || runtime.runtimeEpoch <= 0) return null;
    return enqueuePluginCommand(MATCH_RUNTIME_COMMAND_TYPE, {
        action,
        matchId: runtime.matchId,
        revision: runtime.revision,
        runtimeEpoch: runtime.runtimeEpoch,
        ...(expectedStage ? { expectedStage } : {}),
        ...payload,
    });
};
