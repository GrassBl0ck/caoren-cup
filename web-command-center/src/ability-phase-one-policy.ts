import { AbilityPhaseOnePublicPolicy, GamePhase, GameSession, Player } from './types';

export const ABILITY_PHASE_ONE_START_BLOCK_MESSAGE = '异能职业配置仅在网页完成，游戏插件尚未同步，阶段 1 不能正式开赛。请先终止本局，或返回大厅关闭异能模式后重新开始。';
export const ABILITY_ROSTER_LOCK_MESSAGE = '异能 BP 已锁定本局参赛阵容，当前不能踢出、禁用或移除参赛者。请先终止本局，或返回大厅后再调整阵容。';

const matchRosterPlayers = (session: GameSession): Player[] => Object.values(session.players || {})
    .filter((player) => player.role !== 'Admin'
        && player.role !== 'Spectator'
        && (player.rosterTeam === 'A' || player.rosterTeam === 'B'));

export const hasCompleteAbilityAssignments = (session: GameSession): boolean => {
    const players = matchRosterPlayers(session);
    if (players.length === 0) return false;
    const assignments = Array.isArray(session.abilityAssignments) ? session.abilityAssignments : [];
    return players.every((player) => assignments.some((assignment) => (
        assignment?.playerId === player.playerId
        && assignment?.team === player.rosterTeam
        && typeof assignment?.abilityId === 'string'
        && assignment.abilityId.trim().length > 0
    )));
};

export const isAbilityPhaseOneFormalStartBlocked = (session: GameSession): boolean => (
    session.phase === GamePhase.PreGameSetup
    && session.matchOptions?.abilityModeEnabled === true
    && session.matchOptions?.matchMode !== 'duel'
    && hasCompleteAbilityAssignments(session)
);

export const isAbilityRosterMutationBlocked = (session: GameSession): boolean => (
    session.phase === GamePhase.AbilityBan
    || session.phase === GamePhase.AbilityDraft
    || isAbilityPhaseOneFormalStartBlocked(session)
);

export const isProtectedAbilityRosterPlayer = (session: GameSession, player: Player | undefined): boolean => (
    !!player
    && player.role !== 'Admin'
    && player.role !== 'Spectator'
    && (player.rosterTeam === 'A' || player.rosterTeam === 'B')
    && isAbilityRosterMutationBlocked(session)
);

export const getAbilityPhaseOnePublicPolicy = (session: GameSession): AbilityPhaseOnePublicPolicy => ({
    formalMatchStartBlocked: isAbilityPhaseOneFormalStartBlocked(session),
    rosterMutationBlocked: isAbilityRosterMutationBlocked(session),
    formalMatchStartMessage: ABILITY_PHASE_ONE_START_BLOCK_MESSAGE,
    rosterMutationMessage: ABILITY_ROSTER_LOCK_MESSAGE,
});
