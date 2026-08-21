import { createHash } from 'crypto';

import { AbilityAssignment, AbilityId, AbilitySyncState, RosterTeam } from './types';

export const ABILITY_SYNC_PROTOCOL_VERSION = 1;
export const ABILITY_SYNC_COMMAND_TYPE = 'ABILITY_SYNC';
export const ABILITY_SYNC_CATALOG_VERSION = 'ability-catalog-v1';
export const ABILITY_SYNC_GLOBAL_UNIQUE_IDS = new Set<AbilityId>(['istaru']);

export type AbilitySyncSide = 'CT' | 'T';

export interface AbilitySyncSeat {
    playerId: string;
    steamId: string;
    rosterTeam: RosterTeam;
    initialSide: AbilitySyncSide;
    abilityId: AbilityId;
}

export interface AbilitySyncConfig {
    protocolVersion: number;
    syncId: string;
    matchId: string;
    revision: number;
    catalogVersion: string;
    abilityModeEnabled: boolean;
    bannedAbilityIds: AbilityId[];
    seats: AbilitySyncSeat[];
    contentDigest: string;
}

export interface AbilitySyncExpectedSeat {
    playerId: string;
    steamId: string;
    rosterTeam: RosterTeam;
    initialSide: AbilitySyncSide;
}

export interface AbilitySyncValidationContext {
    expectedMatchId: string;
    expectedRevision: number;
    expectedCatalogVersion: string;
    expectedSeatCount: number;
    knownAbilityIds: ReadonlySet<string>;
    expectedSeats?: ReadonlyArray<AbilitySyncExpectedSeat>;
    globalUniqueAbilityIds?: ReadonlySet<string>;
}

export interface AbilitySyncValidationError {
    code: string;
    message: string;
}

export interface AbilitySyncApplyResult {
    ok: boolean;
    applied: boolean;
    duplicate: boolean;
    code?: string;
    message?: string;
}

export interface AbilitySyncBuildPlayer {
    playerId: string;
    steamId: string;
    rosterTeam: RosterTeam;
}

export interface AbilitySyncConfigBuildInput {
    matchId: string;
    revision: number;
    syncId: string;
    catalogVersion: string;
    abilityModeEnabled: boolean;
    bannedAbilityIds: AbilityId[];
    assignments: ReadonlyArray<AbilityAssignment>;
    players: ReadonlyArray<AbilitySyncBuildPlayer>;
    teamASide: AbilitySyncSide;
}

export interface AbilitySyncFinalAck {
    status: 'success' | 'failed';
    syncId: string;
    matchId: string;
    revision: number;
    contentDigest: string;
    appliedSeatCount: number;
    errorCode?: string;
    errorMessage?: string;
}

const error = (code: string, message: string): AbilitySyncValidationError => ({ code, message });

const oppositeSide = (side: AbilitySyncSide): AbilitySyncSide => side === 'CT' ? 'T' : 'CT';

export const buildAbilitySyncConfig = (input: AbilitySyncConfigBuildInput): AbilitySyncConfig => {
    const playerById = new Map(input.players.map((player) => [player.playerId, player]));
    const assignments = input.assignments.map((assignment) => {
        const player = playerById.get(assignment.playerId);
        if (!player || player.rosterTeam !== assignment.team || !player.steamId) {
            throw new Error('职业分配与当前比赛席位不一致，无法同步。');
        }
        return {
            playerId: player.playerId,
            steamId: player.steamId,
            rosterTeam: player.rosterTeam,
            initialSide: player.rosterTeam === 'A' ? input.teamASide : oppositeSide(input.teamASide),
            abilityId: assignment.abilityId,
        };
    });
    const config: AbilitySyncConfig = {
        protocolVersion: ABILITY_SYNC_PROTOCOL_VERSION,
        syncId: input.syncId,
        matchId: input.matchId,
        revision: input.revision,
        catalogVersion: input.catalogVersion,
        abilityModeEnabled: input.abilityModeEnabled,
        bannedAbilityIds: [...new Set(input.bannedAbilityIds)].sort(),
        seats: assignments.sort((left, right) => left.playerId.localeCompare(right.playerId)),
        contentDigest: '',
    };
    config.contentDigest = computeAbilityContentDigest(config);
    return config;
};

const canonicalContent = (config: AbilitySyncConfig): string => JSON.stringify({
    protocolVersion: config.protocolVersion,
    matchId: config.matchId,
    revision: config.revision,
    catalogVersion: config.catalogVersion,
    abilityModeEnabled: config.abilityModeEnabled,
    bannedAbilityIds: [...new Set(config.bannedAbilityIds)].sort(),
    seats: [...config.seats]
        .map((seat) => ({
            playerId: seat.playerId,
            steamId: seat.steamId,
            rosterTeam: seat.rosterTeam,
            initialSide: seat.initialSide,
            abilityId: seat.abilityId,
        }))
        .sort((left, right) => left.playerId.localeCompare(right.playerId)),
});

export const computeAbilityContentDigest = (config: AbilitySyncConfig): string => (
    createHash('sha256').update(canonicalContent(config), 'utf8').digest('hex')
);

export const createAbilitySyncState = (
    config: AbilitySyncConfig,
    commandId: string,
    now = Date.now(),
): AbilitySyncState => ({
    status: 'waiting_bridge',
    syncId: config.syncId,
    matchId: config.matchId,
    revision: config.revision,
    catalogVersion: config.catalogVersion,
    contentDigest: config.contentDigest,
    seatCount: config.seats.length,
    commandId,
    updatedAt: now,
});

export const markAbilitySyncBridgeReceived = (
    state: AbilitySyncState | undefined,
    commandId: string,
    now = Date.now(),
): AbilitySyncState | undefined => {
    if (!state || state.commandId !== commandId) return state;
    if (state.status === 'confirmed'
        || state.status === 'disabled'
        || state.status === 'plugin_validating'
        || state.status === 'failed') return state;
    return { ...state, status: 'bridge_received', updatedAt: now };
};

export const markAbilitySyncPluginValidating = (
    state: AbilitySyncState | undefined,
    input: Pick<AbilitySyncFinalAck, 'syncId' | 'matchId' | 'revision' | 'contentDigest'>,
    now = Date.now(),
): AbilitySyncState | undefined => {
    if (!state
        || state.status === 'confirmed'
        || state.status === 'disabled'
        || state.syncId !== input.syncId
        || state.matchId !== input.matchId
        || state.revision !== input.revision
        || state.contentDigest !== input.contentDigest) {
        return state;
    }
    return { ...state, status: 'plugin_validating', updatedAt: now };
};

export const applyAbilitySyncFinalAck = (
    state: AbilitySyncState | undefined,
    ack: AbilitySyncFinalAck,
    now = Date.now(),
): { accepted: boolean; state?: AbilitySyncState } => {
    if (!state
        || state.status === 'disabled'
        || state.syncId !== ack.syncId
        || state.matchId !== ack.matchId
        || state.revision !== ack.revision
        || state.contentDigest !== ack.contentDigest) {
        return { accepted: false, state };
    }
    if (ack.status === 'success' && ack.appliedSeatCount === state.seatCount) {
        return {
            accepted: true,
            state: {
                ...state,
                status: 'confirmed',
                appliedSeatCount: ack.appliedSeatCount,
                errorCode: undefined,
                errorMessage: undefined,
                updatedAt: now,
            },
        };
    }
    return {
        accepted: true,
        state: {
            ...state,
            status: 'failed',
            appliedSeatCount: ack.appliedSeatCount,
            errorCode: ack.errorCode || 'ABILITY_SYNC_FAILED',
            errorMessage: ack.errorMessage || '娱乐插件拒绝了整份职业配置。',
            updatedAt: now,
        },
    };
};

const isCompleteSteamId = (steamId: unknown): steamId is string => (
    typeof steamId === 'string' && /^7656119\d{10}$/.test(steamId)
);

export const validateAbilitySyncConfig = (
    config: AbilitySyncConfig,
    context: AbilitySyncValidationContext,
): AbilitySyncValidationError | null => {
    if (config.protocolVersion !== ABILITY_SYNC_PROTOCOL_VERSION) {
        return error('PROTOCOL_VERSION_MISMATCH', '同步协议版本不一致。');
    }
    if (!config.syncId.trim() || !config.matchId.trim()) {
        return error('MISSING_ID', '同步 ID 或比赛 ID 缺失。');
    }
    if (config.matchId !== context.expectedMatchId) {
        return error('MATCH_MISMATCH', '同步配置不属于当前比赛。');
    }
    if (config.revision !== context.expectedRevision) {
        return error('REVISION_MISMATCH', '同步配置修订号不是当前修订号。');
    }
    if (config.catalogVersion !== context.expectedCatalogVersion) {
        return error('CATALOG_VERSION_MISMATCH', '职业目录版本不一致。');
    }
    if (!Array.isArray(config.seats) || config.seats.length !== context.expectedSeatCount) {
        return error('SEAT_COUNT_MISMATCH', '同步席位数量与当前阵容不一致。');
    }

    const playerIds = new Set<string>();
    const steamIds = new Set<string>();
    const banned = new Set(config.bannedAbilityIds);
    for (const bannedAbilityId of banned) {
        if (!context.knownAbilityIds.has(bannedAbilityId)) {
            return error('UNKNOWN_ABILITY', '最终 Ban 列表包含未知职业。');
        }
    }
    const teamAbilities = new Map<RosterTeam, Set<string>>([
        ['A', new Set<string>()],
        ['B', new Set<string>()],
    ]);
    const globalUniqueIds = context.globalUniqueAbilityIds || ABILITY_SYNC_GLOBAL_UNIQUE_IDS;
    let globalUniqueCount = 0;

    for (const seat of config.seats) {
        if (!seat.playerId.trim() || playerIds.has(seat.playerId)) {
            return error('DUPLICATE_PLAYER_ID', '玩家席位 ID 缺失或重复。');
        }
        playerIds.add(seat.playerId);
        if (!isCompleteSteamId(seat.steamId)) {
            return error('INVALID_STEAM_ID', '存在不完整或格式无效的 SteamID。');
        }
        if (steamIds.has(seat.steamId)) {
            return error('DUPLICATE_STEAM_ID', '同步配置包含重复 SteamID。');
        }
        steamIds.add(seat.steamId);
        if (seat.rosterTeam !== 'A' && seat.rosterTeam !== 'B') {
            return error('INVALID_ROSTER_TEAM', '席位 A/B 队信息无效。');
        }
        if (seat.initialSide !== 'CT' && seat.initialSide !== 'T') {
            return error('INVALID_INITIAL_SIDE', '席位初始 CT/T 信息无效。');
        }
        if (!context.knownAbilityIds.has(seat.abilityId)) {
            return error('UNKNOWN_ABILITY', '同步配置包含未知职业。');
        }
        if (banned.has(seat.abilityId)) {
            return error('BANNED_ABILITY', '同步配置包含最终 Ban 职业。');
        }
        const abilities = teamAbilities.get(seat.rosterTeam)!;
        if (abilities.has(seat.abilityId)) {
            return error('DUPLICATE_TEAM_ABILITY', '同队职业重复。');
        }
        abilities.add(seat.abilityId);
        if (globalUniqueIds.has(seat.abilityId)) globalUniqueCount += 1;

        const expected = context.expectedSeats?.find((item) => item.playerId === seat.playerId);
        if (expected && (expected.steamId !== seat.steamId
            || expected.rosterTeam !== seat.rosterTeam
            || expected.initialSide !== seat.initialSide)) {
            return error('TEAM_SIDE_MISMATCH', '席位玩家、A/B 队或初始 CT/T 分配不一致。');
        }
    }

    if (globalUniqueCount > 1) {
        return error('GLOBAL_UNIQUE_ABILITY', '伊斯塔露等全场唯一职业不能超过一人。');
    }
    if (computeAbilityContentDigest(config) !== config.contentDigest) {
        return error('CONTENT_DIGEST_MISMATCH', '同步内容摘要不匹配。');
    }
    return null;
};

export class AbilitySyncApplier {
    private appliedConfig: AbilitySyncConfig | undefined;

    public constructor(private readonly context: AbilitySyncValidationContext) {}

    public get current(): Readonly<AbilitySyncConfig> | undefined {
        return this.appliedConfig ? { ...this.appliedConfig, seats: this.appliedConfig.seats.map((seat) => ({ ...seat })) } : undefined;
    }

    public apply(config: AbilitySyncConfig): AbilitySyncApplyResult {
        const validationError = validateAbilitySyncConfig(config, this.context);
        if (validationError) return { ok: false, applied: false, duplicate: false, ...validationError };

        if (this.appliedConfig) {
            if (this.appliedConfig.syncId === config.syncId && this.appliedConfig.contentDigest !== config.contentDigest) {
                return { ok: false, applied: false, duplicate: false, code: 'SYNC_ID_CONTENT_MISMATCH', message: '同一同步 ID 的内容不一致。' };
            }
            if (this.appliedConfig.syncId === config.syncId || this.appliedConfig.contentDigest === config.contentDigest) {
                return { ok: true, applied: false, duplicate: true };
            }
        }

        this.appliedConfig = {
            ...config,
            bannedAbilityIds: [...config.bannedAbilityIds],
            seats: config.seats.map((seat) => ({ ...seat })),
        };
        return { ok: true, applied: true, duplicate: false };
    }
}
