export interface SkinActorPlayer {
    playerId: string;
    role: string;
    steamId?: string;
    identityLevel?: string;
    confirmationState?: string;
}

export interface ResolvedSkinActor {
    actorPlayerId: string;
    actorRole: 'Admin' | 'Player';
    targetSteamId: string;
}

const STEAM_ID = /^7656119\d{10}$/;

export const resolveSkinActor = (
    player: SkinActorPlayer | undefined,
    requestedSteamId: unknown,
    verifiedSteamIds: ReadonlySet<string>,
): ResolvedSkinActor => {
    if (!player) throw new Error('请先登录草人杯大厅。');
    const requested = String(requestedSteamId || '').trim();
    if (player.role === 'Admin') {
        if (!STEAM_ID.test(requested)) throw new Error('请选择一名已验证的玩家。');
        if (!verifiedSteamIds.has(requested)) throw new Error('目标 SteamID 尚未验证，不能代管。');
        return { actorPlayerId: player.playerId, actorRole: 'Admin', targetSteamId: requested };
    }
    if (player.identityLevel !== 'longTerm' || player.confirmationState !== 'confirmed' || !STEAM_ID.test(String(player.steamId || ''))) {
        throw new Error('只有已由游戏服务器验证的本人 SteamID 才能使用换肤。');
    }
    if (requested && requested !== player.steamId) throw new Error('玩家只能编辑本人 SteamID。');
    return { actorPlayerId: player.playerId, actorRole: 'Player', targetSteamId: String(player.steamId) };
};
