import type { Socket } from 'socket.io';

import { getSession } from '../session-manager';
import { findPlayerById } from '../player-utils';
import type { Player } from '../types';
import { resolveSkinActor, type SkinActorPlayer } from './permissions';
import { weaponPaintsRuntime } from './runtime';
import type { WeaponPaintsService } from './service';

export const WEAPONPAINTS_ACTION = 'WEAPONPAINTS_ACTION';
export const WEAPONPAINTS_STATUS = 'WEAPONPAINTS_STATUS';

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
    switch (String(payload.action || '')) {
        case 'load': return service.load(actor);
        case 'saveWeapon': return service.saveWeapon(actor, payload.weapon || {});
        case 'saveCosmetic': return service.saveCosmetic(actor, payload.cosmetic || {});
        case 'copyTeam': return service.copyTeam(actor, payload.fromTeam, payload.toTeam);
        case 'reset': return service.reset(actor, payload.team);
        case 'forceRefresh': return service.forceRefresh(actor);
        default: throw new Error('未知的换肤操作。');
    }
};

export const registerWeaponPaintsSocketHandlers = (socket: Socket) => {
    socket.on(WEAPONPAINTS_STATUS, async (callback?: (response: unknown) => void) => {
        const respond = typeof callback === 'function' ? callback : () => undefined;
        const session = getSession();
        const player = socket.data.playerId ? findPlayerById(session, socket.data.playerId) : undefined;
        const verified = Object.values(session.players)
            .filter((candidate) => candidate.identityLevel === 'longTerm' && candidate.confirmationState === 'confirmed' && /^7656119\d{10}$/.test(String(candidate.steamId || '')))
            .map((candidate) => ({ steamId: candidate.steamId, name: candidate.name }));
        const health = await weaponPaintsRuntime.health();
        respond({
            success: true,
            health,
            isAdmin: player?.role === 'Admin',
            canUse: player?.role === 'Admin' || (player?.identityLevel === 'longTerm' && player?.confirmationState === 'confirmed' && !!player?.steamId),
            selfSteamId: player?.role === 'Admin' ? undefined : player?.steamId,
            targets: player?.role === 'Admin' ? verified : undefined,
        });
    });

    socket.on(WEAPONPAINTS_ACTION, async (payload: Record<string, any>, callback?: (response: unknown) => void) => {
        const respond = typeof callback === 'function' ? callback : () => undefined;
        try {
            const session = getSession();
            const player = socket.data.playerId ? findPlayerById(session, socket.data.playerId) : undefined;
            const data = await executeWeaponPaintsAction(
                weaponPaintsRuntime.requireService(),
                player,
                verifiedSteamIdsFrom(session.players),
                payload || {},
            );
            respond({ success: true, data });
        } catch (error) {
            respond({ success: false, error: error instanceof Error ? error.message : '换肤操作失败。' });
        }
    });
};
