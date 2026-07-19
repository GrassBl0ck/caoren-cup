import { getAbilityCatalog } from './ability-catalog';
import {
    AbilityBanResolution,
    AbilityBanState,
    AbilityAssignment,
    AbilityDraftBatch,
    AbilityDraftState,
    AbilityId,
    RosterTeam,
    RuleResult,
} from './types';

export interface SafeBanLimitInput {
    teamSizeA: number;
    teamSizeB: number;
}

export interface CreateAbilityBanStateInput {
    orderedA: string[];
    orderedB: string[];
    banCountPerTeam: number;
    timeoutAt: number;
}

export interface CreateAbilityDraftStateInput {
    firstTeam: RosterTeam;
    orderedA: string[];
    orderedB: string[];
    bannedAbilityIds: AbilityId[];
    timeoutAt: number;
}

const abilities = getAbilityCatalog();
const abilityIds = abilities.map((ability) => ability.id);
const globalUniqueIds = new Set(abilities.filter((ability) => ability.globalUnique).map((ability) => ability.id));
const knownAbilityIds = new Set(abilityIds);

const otherTeam = (team: RosterTeam): RosterTeam => team === 'A' ? 'B' : 'A';

const canAssignAllSeats = (
    teamSizeA: number,
    teamSizeB: number,
    bannedAbilityIds: ReadonlySet<AbilityId>,
): boolean => {
    const available = abilityIds.filter((abilityId) => !bannedAbilityIds.has(abilityId));
    const normalCount = available.filter((abilityId) => !globalUniqueIds.has(abilityId)).length;

    if (teamSizeA === 0 || teamSizeB === 0) {
        return Math.max(teamSizeA, teamSizeB) <= available.length;
    }

    // 双方都有人时，任一方可能先拿走全场唯一职业。另一方仍须能只靠
    // 普通职业填满全部席位，才能保证之后任意合法选择都不会制造死局。
    return Math.max(teamSizeA, teamSizeB) <= normalCount;
};

export const calculateSafeBanLimit = (input: SafeBanLimitInput): number => {
    const teamSizeA = Math.max(0, Math.floor(input.teamSizeA));
    const teamSizeB = Math.max(0, Math.floor(input.teamSizeB));
    const votingTeamCount = Number(teamSizeA > 0) + Number(teamSizeB > 0);
    let safeLimit = 0;

    for (let limit = 0; limit <= abilityIds.length; limit += 1) {
        const maxDistinctBans = Math.min(abilityIds.length, limit * votingTeamCount);
        let safe = true;

        for (let mask = 0; mask < (1 << abilityIds.length); mask += 1) {
            let count = 0;
            for (let bit = 0; bit < abilityIds.length; bit += 1) {
                count += (mask >> bit) & 1;
            }
            if (count > maxDistinctBans) continue;

            const banned = new Set<AbilityId>();
            for (let bit = 0; bit < abilityIds.length; bit += 1) {
                if ((mask & (1 << bit)) !== 0) banned.add(abilityIds[bit]);
            }
            if (!canAssignAllSeats(teamSizeA, teamSizeB, banned)) {
                safe = false;
                break;
            }
        }

        if (!safe) break;
        safeLimit = limit;
    }

    return safeLimit;
};

export const buildAbilityDraftBatches = (
    firstTeam: RosterTeam,
    orderedA: string[],
    orderedB: string[],
): AbilityDraftBatch[] => {
    const remaining: Record<RosterTeam, string[]> = {
        A: [...orderedA],
        B: [...orderedB],
    };
    const batches: AbilityDraftBatch[] = [];
    let team = firstTeam;
    let batchSize = 1;

    while (remaining.A.length > 0 || remaining.B.length > 0) {
        const playerIds = remaining[team].splice(0, batchSize);
        if (playerIds.length > 0) batches.push({ team, playerIds });
        team = otherTeam(team);
        batchSize = 2;
    }

    return batches;
};

export const createAbilityBanState = (input: CreateAbilityBanStateInput): AbilityBanState => ({
    orderedPlayers: {
        A: [...input.orderedA],
        B: [...input.orderedB],
    },
    banCountPerTeam: Math.max(0, Math.floor(input.banCountPerTeam)),
    selections: {},
    confirmedPlayerIds: [],
    timeoutAt: input.timeoutAt,
});

const success = (): RuleResult => ({ ok: true });
const failure = (code: string, message: string): RuleResult => ({ ok: false, code, message });

const getPlayerTeam = (
    orderedPlayers: Record<RosterTeam, string[]>,
    playerId: string,
): RosterTeam | null => {
    if (orderedPlayers.A.includes(playerId)) return 'A';
    if (orderedPlayers.B.includes(playerId)) return 'B';
    return null;
};

export const updateAbilityBanSelection = (
    state: AbilityBanState,
    playerId: string,
    selectedAbilityIds: AbilityId[],
): RuleResult => {
    if (!getPlayerTeam(state.orderedPlayers, playerId)) {
        return failure('UNKNOWN_PLAYER', '未找到该参赛玩家。');
    }
    if (state.confirmedPlayerIds.includes(playerId)) {
        return failure('BAN_ALREADY_CONFIRMED', 'Ban 选择已经确认，不能再修改。');
    }
    if (!Array.isArray(selectedAbilityIds)) {
        return failure('INVALID_ABILITY', '职业选择格式无效。');
    }
    if (selectedAbilityIds.some((abilityId) => !knownAbilityIds.has(abilityId))) {
        return failure('INVALID_ABILITY', '选择中包含不存在的职业。');
    }
    if (new Set(selectedAbilityIds).size !== selectedAbilityIds.length) {
        return failure('DUPLICATE_ABILITY', '同一职业不能重复选择。');
    }
    if (selectedAbilityIds.length > state.banCountPerTeam) {
        return failure('BAN_LIMIT_EXCEEDED', `每名玩家最多选择 ${state.banCountPerTeam} 个不同职业。`);
    }

    state.selections[playerId] = [...selectedAbilityIds];
    return success();
};

export const confirmAbilityBan = (state: AbilityBanState, playerId: string): RuleResult => {
    if (!getPlayerTeam(state.orderedPlayers, playerId)) {
        return failure('UNKNOWN_PLAYER', '未找到该参赛玩家。');
    }
    if (state.confirmedPlayerIds.includes(playerId)) {
        return failure('BAN_ALREADY_CONFIRMED', 'Ban 选择已经确认。');
    }

    state.confirmedPlayerIds.push(playerId);
    return success();
};

const randomIndex = (length: number, random: () => number): number => {
    const value = Number(random());
    if (!Number.isFinite(value)) return 0;
    return Math.min(length - 1, Math.max(0, Math.floor(value * length)));
};

const resolveTeamBans = (
    votes: Partial<Record<AbilityId, number>>,
    limit: number,
    random: () => number,
): AbilityId[] => {
    const selected: AbilityId[] = [];
    const voteCounts = [...new Set(Object.values(votes).filter((value): value is number => typeof value === 'number'))]
        .sort((left, right) => right - left);

    for (const voteCount of voteCounts) {
        const tied = abilityIds.filter((abilityId) => votes[abilityId] === voteCount);
        while (tied.length > 0 && selected.length < limit) {
            if (tied.length <= limit - selected.length) {
                selected.push(...tied);
                break;
            }
            const index = randomIndex(tied.length, random);
            selected.push(tied.splice(index, 1)[0]);
        }
        if (selected.length >= limit) break;
    }

    return selected;
};

export const resolveAbilityBans = (
    state: AbilityBanState,
    random: () => number,
): AbilityBanResolution => {
    const votes: AbilityBanResolution['votes'] = { A: {}, B: {} };
    for (const playerId of state.confirmedPlayerIds) {
        const team = getPlayerTeam(state.orderedPlayers, playerId);
        if (!team) continue;
        for (const abilityId of state.selections[playerId] ?? []) {
            votes[team][abilityId] = (votes[team][abilityId] ?? 0) + 1;
        }
    }

    const teamBans: AbilityBanResolution['teamBans'] = {
        A: resolveTeamBans(votes.A, state.banCountPerTeam, random),
        B: resolveTeamBans(votes.B, state.banCountPerTeam, random),
    };

    return {
        teamBans,
        bannedAbilityIds: [...new Set([...teamBans.A, ...teamBans.B])],
        votes,
    };
};

export const createAbilityDraftState = (input: CreateAbilityDraftStateInput): AbilityDraftState => ({
    batches: buildAbilityDraftBatches(input.firstTeam, input.orderedA, input.orderedB),
    currentBatchIndex: 0,
    bannedAbilityIds: [...new Set(input.bannedAbilityIds.filter((abilityId) => knownAbilityIds.has(abilityId)))],
    choices: {},
    confirmedPlayerIds: [],
    assignments: [],
    timeoutAt: input.timeoutAt,
});

const getActiveBatch = (state: AbilityDraftState): AbilityDraftBatch | null => (
    state.batches[state.currentBatchIndex] ?? null
);

const assignmentConflict = (
    state: AbilityDraftState,
    team: RosterTeam,
    abilityId: AbilityId,
    assignments: AbilityAssignment[] = state.assignments,
): RuleResult | null => {
    if (assignments.some((assignment) => assignment.team === team && assignment.abilityId === abilityId)) {
        return failure('ABILITY_TAKEN_BY_TEAM', '该职业已经被同队玩家占用。');
    }
    if (globalUniqueIds.has(abilityId)
        && assignments.some((assignment) => assignment.abilityId === abilityId)) {
        return failure('GLOBAL_ABILITY_TAKEN', '伊斯塔露是全场唯一职业，已经被其他玩家占用。');
    }
    return null;
};

const validateAbilityChoice = (
    state: AbilityDraftState,
    team: RosterTeam,
    abilityId: AbilityId,
    assignments: AbilityAssignment[] = state.assignments,
): RuleResult | null => {
    if (!knownAbilityIds.has(abilityId)) {
        return failure('INVALID_ABILITY', '选择了不存在的职业。');
    }
    if (state.bannedAbilityIds.includes(abilityId)) {
        return failure('ABILITY_BANNED', '该职业已经被 Ban，不能选择。');
    }
    return assignmentConflict(state, team, abilityId, assignments);
};

export const updateAbilityChoice = (
    state: AbilityDraftState,
    playerId: string,
    abilityId: AbilityId,
): RuleResult => {
    const activeBatch = getActiveBatch(state);
    if (!activeBatch?.playerIds.includes(playerId)) {
        return failure('DRAFT_NOT_ACTIVE_PLAYER', '该玩家不在当前选角批次中。');
    }
    if (state.confirmedPlayerIds.includes(playerId)) {
        return failure('ABILITY_ALREADY_CONFIRMED', '职业选择已经确认，不能再修改。');
    }
    const invalid = validateAbilityChoice(state, activeBatch.team, abilityId);
    if (invalid) return invalid;

    state.choices[playerId] = abilityId;
    return success();
};

export const confirmAbilityChoice = (state: AbilityDraftState, playerId: string): RuleResult => {
    const activeBatch = getActiveBatch(state);
    if (!activeBatch?.playerIds.includes(playerId)) {
        return failure('DRAFT_NOT_ACTIVE_PLAYER', '该玩家不在当前选角批次中。');
    }
    if (state.confirmedPlayerIds.includes(playerId)) {
        return failure('ABILITY_ALREADY_CONFIRMED', '职业选择已经确认。');
    }
    const abilityId = state.choices[playerId];
    if (!abilityId) {
        return failure('ABILITY_NOT_SELECTED', '请先选择职业再确认。');
    }
    const invalid = validateAbilityChoice(state, activeBatch.team, abilityId);
    if (invalid) return invalid;

    state.confirmedPlayerIds.push(playerId);
    state.assignments.push({ playerId, team: activeBatch.team, abilityId });
    return success();
};

const getLegalAbilityIds = (
    state: AbilityDraftState,
    team: RosterTeam,
    assignments: AbilityAssignment[],
): AbilityId[] => abilityIds.filter((abilityId) => (
    validateAbilityChoice(state, team, abilityId, assignments) === null
));

export const finishCurrentAbilityBatch = (
    state: AbilityDraftState,
    random: () => number,
): RuleResult => {
    const activeBatch = getActiveBatch(state);
    if (!activeBatch) {
        return failure('DRAFT_COMPLETE', '全部职业批次已经完成。');
    }

    const generatedAssignments: AbilityAssignment[] = [];
    const assignments = [...state.assignments];
    for (const playerId of activeBatch.playerIds) {
        if (state.confirmedPlayerIds.includes(playerId)) continue;
        const legalAbilityIds = getLegalAbilityIds(state, activeBatch.team, assignments);
        if (legalAbilityIds.length === 0) {
            return failure('NO_LEGAL_ABILITY', '没有可供该玩家分配的合法职业。');
        }
        const abilityId = legalAbilityIds[randomIndex(legalAbilityIds.length, random)];
        const assignment = { playerId, team: activeBatch.team, abilityId };
        generatedAssignments.push(assignment);
        assignments.push(assignment);
    }

    for (const assignment of generatedAssignments) {
        state.choices[assignment.playerId] = assignment.abilityId;
        state.confirmedPlayerIds.push(assignment.playerId);
        state.assignments.push(assignment);
    }
    state.currentBatchIndex += 1;
    return success();
};
