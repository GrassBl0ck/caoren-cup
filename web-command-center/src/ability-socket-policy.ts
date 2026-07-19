import {
    AbilityBanState,
    AbilityDraftState,
    GamePhase,
    PlayerRole,
    RosterTeam,
} from './types';

export type AbilitySocketAction =
    | 'ABILITY_BAN_UPDATE'
    | 'ABILITY_BAN_CONFIRM'
    | 'ABILITY_PICK_UPDATE'
    | 'ABILITY_PICK_CONFIRM';

export interface AbilitySocketActor {
    playerId: string;
    role: PlayerRole;
    rosterTeam?: RosterTeam;
}

export interface AbilitySocketPolicyInput {
    event: AbilitySocketAction;
    actor: AbilitySocketActor | null | undefined;
    phase: GamePhase;
    banState?: AbilityBanState;
    draftState?: AbilityDraftState;
    payload: unknown;
}

export interface AbilitySocketPolicyResult {
    allowed: boolean;
    code: string;
    message: string;
}

const reject = (code: string, message: string): AbilitySocketPolicyResult => ({
    allowed: false,
    code,
    message,
});

const allow = (): AbilitySocketPolicyResult => ({ allowed: true, code: 'OK', message: '' });

const asRecord = (value: unknown): Record<string, unknown> | null => (
    value !== null && typeof value === 'object' && !Array.isArray(value)
        ? value as Record<string, unknown>
        : null
);

const isRosterTeam = (value: unknown): value is RosterTeam => value === 'A' || value === 'B';

export const authorizeAbilitySocketAction = (
    input: AbilitySocketPolicyInput,
): AbilitySocketPolicyResult => {
    const payload = asRecord(input.payload);
    if (!input.actor) {
        return reject('ACTOR_NOT_AUTHENTICATED', '当前连接尚未登录，无法执行异能 BP 操作。');
    }
    if (!payload || payload.playerId !== input.actor.playerId) {
        return reject('ACTOR_MISMATCH', '当前连接不能替其他玩家执行异能 BP 操作。');
    }
    if (input.actor.role !== 'Player' || !isRosterTeam(input.actor.rosterTeam)) {
        return reject('ACTOR_NOT_PARTICIPANT', '只有已分队的参赛玩家可以参与异能 BP。');
    }

    if (input.event === 'ABILITY_BAN_UPDATE' || input.event === 'ABILITY_BAN_CONFIRM') {
        if (input.phase !== GamePhase.AbilityBan || !input.banState) {
            return reject('ABILITY_BAN_PHASE_REQUIRED', '当前不是职业 Ban 阶段。');
        }
        if (!input.banState.orderedPlayers[input.actor.rosterTeam].includes(input.actor.playerId)) {
            return reject('ABILITY_BAN_TEAM_MISMATCH', '你的参赛队伍与当前 Ban 名单不一致。');
        }
        if (input.event === 'ABILITY_BAN_UPDATE'
            && (!Array.isArray(payload.selectedAbilityIds)
                || payload.selectedAbilityIds.some((abilityId) => typeof abilityId !== 'string'))) {
            return reject('INVALID_ABILITY_PAYLOAD', '职业选择数据格式无效。');
        }
        return allow();
    }

    if (input.phase !== GamePhase.AbilityDraft || !input.draftState) {
        return reject('ABILITY_DRAFT_PHASE_REQUIRED', '当前不是职业选择阶段。');
    }
    const activeBatch = input.draftState.batches[input.draftState.currentBatchIndex];
    if (!activeBatch
        || activeBatch.team !== input.actor.rosterTeam
        || !activeBatch.playerIds.includes(input.actor.playerId)) {
        return reject('DRAFT_NOT_ACTIVE_PLAYER', '你不在当前职业选择批次中。');
    }
    if (input.event === 'ABILITY_PICK_UPDATE'
        && (typeof payload.abilityId !== 'string' || payload.abilityId.length === 0)) {
        return reject('INVALID_ABILITY_PAYLOAD', '职业 ID 格式无效。');
    }
    return allow();
};

export const shouldFinishAbilityBanEarly = (
    state: AbilityBanState,
    onlinePlayerIds: ReadonlySet<string>,
): boolean => [...state.orderedPlayers.A, ...state.orderedPlayers.B]
    .filter((playerId) => onlinePlayerIds.has(playerId))
    .every((playerId) => state.confirmedPlayerIds.includes(playerId));

export const shouldFinishAbilityDraftBatchEarly = (state: AbilityDraftState): boolean => {
    const activeBatch = state.batches[state.currentBatchIndex];
    return !!activeBatch
        && activeBatch.playerIds.every((playerId) => state.confirmedPlayerIds.includes(playerId));
};
