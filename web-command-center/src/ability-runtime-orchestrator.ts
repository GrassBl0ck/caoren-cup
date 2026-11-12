import { isBridgeMatchRuntimeEnabled } from './match-runtime-orchestrator';
import type { BridgeCommand } from './plugin-command-queue';
import { enqueuePluginCommand } from './plugin-command-queue';
import { GamePhase, type AbilitySyncState, type GameSession } from './types';

export const ABILITY_RUNTIME_COMMAND_TYPE = 'ABILITY_RUNTIME';

type ConfirmedRuntimeIdentity = Pick<AbilitySyncState, 'matchId' | 'revision' | 'contentDigest'> & {
    runtimeEpoch: number;
};

const confirmedIdentity = (session: GameSession): ConfirmedRuntimeIdentity | null => {
    if (session.bridgeProtocolCapabilities?.abilitySync !== 1) return null;
    const state = session.abilitySyncState;
    if (session.matchOptions?.abilityModeEnabled !== true
        || !state
        || state.status !== 'confirmed'
        || state.matchId !== session.matchId
        || state.revision <= 0
        || !/^[a-f0-9]{64}$/.test(state.contentDigest)) return null;
    const runtimeEpoch = isBridgeMatchRuntimeEnabled(session)
        ? session.matchRuntime?.matchId === session.matchId
            && Number.isSafeInteger(session.matchRuntime.runtimeEpoch)
            && session.matchRuntime.runtimeEpoch > 0
                ? session.matchRuntime.runtimeEpoch
                : 0
        : 1;
    if (runtimeEpoch <= 0) return null;
    return {
        matchId: state.matchId,
        revision: state.revision,
        contentDigest: state.contentDigest,
        runtimeEpoch,
    };
};

export const enqueueAbilityRuntimeStart = (session: GameSession, roundKey: string): BridgeCommand | null => {
    const identity = confirmedIdentity(session);
    if (!identity || session.phase !== GamePhase.LiveGame || !roundKey.trim()) return null;
    return enqueuePluginCommand(ABILITY_RUNTIME_COMMAND_TYPE, {
        action: 'start',
        ...identity,
        roundKey: roundKey.trim(),
    });
};

export const enqueueAbilityRuntimeStop = (session: GameSession, reason: string): BridgeCommand | null => {
    const identity = confirmedIdentity(session);
    if (!identity) return null;
    return enqueuePluginCommand(ABILITY_RUNTIME_COMMAND_TYPE, {
        action: 'stop',
        ...identity,
        reason: String(reason || 'stopped'),
    });
};

export const enqueueAbilityRuntimeSubstitution = (
    session: GameSession,
    seatId: string,
    steamId: string,
): BridgeCommand | null => {
    const identity = confirmedIdentity(session);
    if (!identity || !seatId.trim() || !/^7656119[0-9]{10}$/.test(steamId)) return null;
    return enqueuePluginCommand(ABILITY_RUNTIME_COMMAND_TYPE, {
        action: 'substitute',
        ...identity,
        seatId: seatId.trim(),
        steamId,
    });
};

export const enqueueAbilityRuntimeRoundControl = (
    session: GameSession,
    roundKey: string,
    outcome: 'draw' | 'restarted' | 'admin_cancelled',
): BridgeCommand | null => {
    const identity = confirmedIdentity(session);
    if (!identity || !roundKey.trim()) return null;
    return enqueuePluginCommand(ABILITY_RUNTIME_COMMAND_TYPE, {
        action: 'round_control',
        ...identity,
        roundKey: roundKey.trim(),
        outcome,
    });
};

export const enqueueAbilityRuntimeRestore = (
    session: GameSession,
    roundKey: string,
    checkpointId: string,
    newRuntimeEpoch: number,
): BridgeCommand | null => {
    const identity = confirmedIdentity(session);
    if (!identity
        || !isBridgeMatchRuntimeEnabled(session)
        || !roundKey.trim()
        || !/^[a-zA-Z0-9][a-zA-Z0-9._-]{0,63}$/.test(checkpointId)
        || checkpointId.includes('..')
        || !Number.isSafeInteger(newRuntimeEpoch)
        || newRuntimeEpoch <= identity.runtimeEpoch) return null;
    return enqueuePluginCommand(ABILITY_RUNTIME_COMMAND_TYPE, {
        action: 'restore',
        ...identity,
        runtimeEpoch: newRuntimeEpoch,
        roundKey: roundKey.trim(),
        checkpointId,
    });
};
