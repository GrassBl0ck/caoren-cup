import type { Socket } from 'socket.io';

import { V1333_PLUGIN_ONLINE_TTL_MS } from '../game-constants';
import type { LobbyIdentityService } from '../identity/identity-service';
import type { PlayerCenterSessionStore } from '../identity/player-center-session-store';
import { lobbyIdentityService, playerCenterSessionStore } from '../identity/identity-runtime';
import { cancelPluginCommands } from '../plugin-command-queue';
import { getSession } from '../session-manager';
import { findPlayerById } from '../player-utils';
import type { Player } from '../types';
import { resolveSkinActor, type SkinActorPlayer } from './permissions';
import { buildBridgeHealth } from './health';
import { weaponPaintsRuntime } from './runtime';
import type { WeaponPaintsSettingsStore } from './settings';
import type { WeaponPaintsService } from './service';

export const WEAPONPAINTS_ACTION = 'WEAPONPAINTS_ACTION';
export const WEAPONPAINTS_ADMIN = 'WEAPONPAINTS_ADMIN';
export const WEAPONPAINTS_STATUS = 'WEAPONPAINTS_STATUS';

export const executeWeaponPaintsAdminAction = async (
    settings: WeaponPaintsSettingsStore,
    player: SkinActorPlayer | undefined,
    payload: Record<string, any>,
) => {
    if (player?.role !== 'Admin') throw new Error('只有管理员可以修改网页换肤总开关。');
    if (payload?.action !== 'setEnabled') throw new Error('未知的换肤管理操作。');
    if (typeof payload.enabled !== 'boolean') throw new Error('网页换肤总开关必须是布尔值。');
    await settings.setEnabled(payload.enabled);
    const canceledRefreshCommands = payload.enabled ? 0 : cancelPluginCommands((command) =>
        command.type === 'EXECUTE_SERVER_COMMAND' &&
        /^wp_refresh\s/i.test(String(command.payload?.command || '')));
    return { enabled: payload.enabled, canceledRefreshCommands };
};

export const resolvePlayerCenterSkinActor = async (input: {
    socketData: Record<string, any>;
    sessionStore: PlayerCenterSessionStore;
    service: LobbyIdentityService;
    requestedSteamId: unknown;
}) => {
    const identityId = String(input.socketData.identityId || '');
    const session = await input.sessionStore.useBoundSession(
        input.socketData.playerCenterSessionId,
        identityId,
    );
    const account = session ? input.service.getLoginAccount(identityId) : undefined;
    const identity = session ? input.service.getIdentity(identityId) : undefined;
    if (!session || !account || !identity || !account.enabled || account.passwordState !== 'active' ||
        !account.password || account.updatedAt !== session.accountUpdatedAt) {
        delete input.socketData.identityId;
        delete input.socketData.playerCenterSessionId;
        throw new Error('玩家中心会话已失效，请重新登录。');
    }
    return resolveSkinActor({ identityId, steamId: identity.steamId }, input.requestedSteamId, new Set());
};

export const authorizeWeaponPaintsSocketActor = async (input: {
    socketData: Record<string, any>;
    players: Record<string, Player>;
    sessionStore: PlayerCenterSessionStore;
    identityService: LobbyIdentityService;
    requestedSteamId: unknown;
}) => {
    const playerId = String(input.socketData.playerId || '');
    const player = playerId ? input.players[playerId] : undefined;
    if (player?.role === 'Admin') {
        return resolveSkinActor(player, input.requestedSteamId, verifiedSteamIdsFrom(input.players));
    }
    return resolvePlayerCenterSkinActor({
        socketData: input.socketData,
        sessionStore: input.sessionStore,
        service: input.identityService,
        requestedSteamId: input.requestedSteamId,
    });
};

export const resolveWeaponPaintsSocketStatus = async (input: {
    socketData: Record<string, any>;
    players: Record<string, Player>;
    sessionStore: PlayerCenterSessionStore;
    identityService: LobbyIdentityService;
}) => {
    const playerId = String(input.socketData.playerId || '');
    const player = playerId ? input.players[playerId] : undefined;
    if (player?.role === 'Admin') return { isAdmin: true as const };
    const actor = await resolvePlayerCenterSkinActor({
        socketData: input.socketData,
        sessionStore: input.sessionStore,
        service: input.identityService,
        requestedSteamId: undefined,
    });
    return { isAdmin: false as const, actor };
};

const verifiedSteamIdsFrom = (players: Record<string, Player>) => new Set(
    Object.values(players)
        .filter((player) => player.identityLevel === 'longTerm' && player.confirmationState === 'confirmed' && /^7656119\d{10}$/.test(String(player.steamId || '')))
        .map((player) => String(player.steamId)),
);

export const executeWeaponPaintsAction = async (
    service: WeaponPaintsService,
    player: SkinActorPlayer | undefined,
    verifiedSteamIds: ReadonlySet<string>,
    payload: Record<string, any>,
) => {
    const actor = resolveSkinActor(player, payload.targetSteamId, verifiedSteamIds);
    return executeResolvedWeaponPaintsAction(service, actor, payload);
};

const executeResolvedWeaponPaintsAction = async (
    service: WeaponPaintsService,
    actor: ReturnType<typeof resolveSkinActor>,
    payload: Record<string, any>,
) => {
    switch (String(payload.action || '')) {
        case 'load': return service.load(actor);
        case 'saveWeapon': return service.saveWeapon(actor, payload.weapon || {});
        case 'saveCosmetic': return service.saveCosmetic(actor, payload.cosmetic || {});
        case 'reset': return service.reset(actor, payload.team);
        case 'forceRefresh': return service.forceRefresh(actor);
        default: throw new Error('未知的换肤操作。');
    }
};

export const registerWeaponPaintsSocketHandlers = (socket: Socket) => {
    socket.on(WEAPONPAINTS_STATUS, async (callback?: (response: unknown) => void) => {
        const respond = typeof callback === 'function' ? callback : () => undefined;
        const session = getSession();
        const verified = Object.values(session.players)
            .filter((candidate) => candidate.identityLevel === 'longTerm' && candidate.confirmationState === 'confirmed' && /^7656119\d{10}$/.test(String(candidate.steamId || '')))
            .map((candidate) => ({ steamId: candidate.steamId, name: candidate.name }));
        const health = await weaponPaintsRuntime.health(buildBridgeHealth(
            session.liveGameData,
            Date.now(),
            V1333_PLUGIN_ONLINE_TTL_MS,
        ));
        let statusAccess: Awaited<ReturnType<typeof resolveWeaponPaintsSocketStatus>> | undefined;
        const hadPlayerCenterSession = !!socket.data.identityId || !!socket.data.playerCenterSessionId;
        try {
            statusAccess = await resolveWeaponPaintsSocketStatus({
                socketData: socket.data,
                players: session.players,
                sessionStore: playerCenterSessionStore,
                identityService: lobbyIdentityService,
            });
        } catch {
            if (hadPlayerCenterSession && !socket.data.identityId) socket.emit('PLAYER_CENTER_SESSION_INVALID');
        }
        const isAdmin = statusAccess?.isAdmin === true;
        respond({
            success: true,
            health,
            isAdmin,
            canUse: health.settings.enabled && !!statusAccess,
            targets: isAdmin ? verified : undefined,
        });
    });

    socket.on(WEAPONPAINTS_ADMIN, async (payload: Record<string, any>, callback?: (response: unknown) => void) => {
        const respond = typeof callback === 'function' ? callback : () => undefined;
        try {
            const session = getSession();
            const player = socket.data.playerId ? findPlayerById(session, socket.data.playerId) : undefined;
            const data = await executeWeaponPaintsAdminAction(
                weaponPaintsRuntime.settings,
                player,
                payload || {},
            );
            respond({ success: true, data });
        } catch (error) {
            respond({ success: false, error: error instanceof Error ? error.message : '换肤管理操作失败。' });
        }
    });

    socket.on(WEAPONPAINTS_ACTION, async (payload: Record<string, any>, callback?: (response: unknown) => void) => {
        const respond = typeof callback === 'function' ? callback : () => undefined;
        const hadPlayerCenterSession = !!socket.data.identityId || !!socket.data.playerCenterSessionId;
        try {
            const session = getSession();
            const actor = await authorizeWeaponPaintsSocketActor({
                socketData: socket.data,
                players: session.players,
                sessionStore: playerCenterSessionStore,
                identityService: lobbyIdentityService,
                requestedSteamId: payload?.targetSteamId,
            });
            const data = await executeResolvedWeaponPaintsAction(
                weaponPaintsRuntime.requireService(),
                actor,
                payload || {},
            );
            respond({ success: true, data });
        } catch (error) {
            if (hadPlayerCenterSession && !socket.data.identityId) socket.emit('PLAYER_CENTER_SESSION_INVALID');
            respond({ success: false, error: error instanceof Error ? error.message : '换肤操作失败。' });
        }
    });
};
