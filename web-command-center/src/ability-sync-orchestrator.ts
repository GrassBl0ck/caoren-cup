import { v4 as uuidv4 } from 'uuid';

import { getAbilityCatalog } from './ability-catalog';
import {
    ABILITY_SYNC_CATALOG_VERSION,
    ABILITY_SYNC_COMMAND_TYPE,
    AbilitySyncConfig,
    buildAbilitySyncConfig,
    computeAbilityContentDigest,
    createAbilitySyncState,
} from './ability-sync-service';
import { getGamePlayers, normalizeSteamId } from './player-utils';
import { enqueuePluginCommand } from './plugin-command-queue';
import { GameSession } from './types';

const getCurrentAbilityConfig = (session: GameSession, revision: number, syncId: string): AbilitySyncConfig => {
    if (session.selectedSide !== 'CT' && session.selectedSide !== 'T') {
        throw new Error('还没有完成选边，不能同步异能职业配置。');
    }
    const players = getGamePlayers(session)
        .filter((player) => player.rosterTeam === 'A' || player.rosterTeam === 'B')
        .map((player) => ({
            playerId: player.playerId,
            steamId: normalizeSteamId(player.steamId),
            rosterTeam: player.rosterTeam!,
        }));
    const assignments = Array.isArray(session.abilityAssignments) ? session.abilityAssignments : [];
    if (assignments.length !== players.length || players.length === 0) {
        throw new Error('职业分配数量与当前比赛席位不一致，无法同步。');
    }
    return buildAbilitySyncConfig({
        matchId: session.matchId,
        revision,
        syncId,
        catalogVersion: ABILITY_SYNC_CATALOG_VERSION,
        abilityModeEnabled: session.matchOptions.abilityModeEnabled === true,
        bannedAbilityIds: session.abilityDraftState?.bannedAbilityIds || [],
        assignments,
        players,
        teamASide: session.selectedSide,
    });
};

const getNextConfigIdentity = (session: GameSession): { revision: number; syncId: string } => {
    const state = session.abilitySyncState;
    if (state?.matchId === session.matchId && state.revision > 0) {
        return { revision: state.revision, syncId: state.syncId };
    }
    return {
        revision: Math.max(1, Number(session.abilitySyncRevision || 0) + 1),
        syncId: uuidv4(),
    };
};

export const buildCurrentAbilitySyncConfig = (session: GameSession): AbilitySyncConfig => {
    const identity = getNextConfigIdentity(session);
    const candidate = getCurrentAbilityConfig(session, identity.revision, identity.syncId);
    const prior = session.abilitySyncState;
    if (prior?.matchId === session.matchId && prior.contentDigest === candidate.contentDigest) return candidate;
    if (prior?.matchId === session.matchId && prior.revision === identity.revision) {
        return getCurrentAbilityConfig(session, identity.revision + 1, uuidv4());
    }
    return candidate;
};

export const enqueueCurrentAbilitySync = (session: GameSession) => {
    const config = buildCurrentAbilitySyncConfig(session);
    const queued = enqueuePluginCommand(ABILITY_SYNC_COMMAND_TYPE, {
        ...config,
        label: '异能职业配置可靠同步',
    });
    session.abilitySyncRevision = config.revision;
    session.abilitySyncState = createAbilitySyncState(config, queued.id);
    return { config, queued };
};

export const isSameAbilitySyncContent = (left: AbilitySyncConfig, right: AbilitySyncConfig): boolean => (
    left.matchId === right.matchId
    && left.revision === right.revision
    && left.catalogVersion === right.catalogVersion
    && left.abilityModeEnabled === right.abilityModeEnabled
    && left.contentDigest === right.contentDigest
    && computeAbilityContentDigest(left) === computeAbilityContentDigest(right)
);

export const getAbilityCatalogVersion = () => getAbilityCatalog()[0]?.catalogVersion || ABILITY_SYNC_CATALOG_VERSION;
