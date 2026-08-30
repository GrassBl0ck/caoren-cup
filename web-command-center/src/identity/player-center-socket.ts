import { LobbyIdentityService } from './identity-service';
import { PlayerCenterSessionStore } from './player-center-session-store';

const PLAYER_CENTER_COOKIE_PREFIX = 'caoren_player_center=';

const readCookie = (cookieHeader: string | undefined): string => {
    for (const part of String(cookieHeader || '').split(';')) {
        const value = part.trim();
        if (!value.startsWith(PLAYER_CENTER_COOKIE_PREFIX)) continue;
        try {
            return decodeURIComponent(value.slice(PLAYER_CENTER_COOKIE_PREFIX.length));
        } catch {
            return '';
        }
    }
    return '';
};

export const bindPlayerCenterSocketIdentity = async (input: {
    cookieHeader: string | undefined;
    socketData: Record<string, any>;
    sessionStore: PlayerCenterSessionStore;
    service: LobbyIdentityService;
}): Promise<'missing' | 'invalid' | 'authenticated'> => {
    const rawToken = readCookie(input.cookieHeader);
    if (!rawToken) return 'missing';
    const session = await input.sessionStore.use(rawToken);
    const account = session ? input.service.getLoginAccount(session.identityId) : undefined;
    const identity = session ? input.service.getIdentity(session.identityId) : undefined;
    if (!session || !account || !identity || !account.enabled || account.passwordState !== 'active' ||
        !account.password || account.updatedAt !== session.accountUpdatedAt) {
        await input.sessionStore.revokeCurrent(rawToken);
        delete input.socketData.identityId;
        delete input.socketData.playerCenterSessionId;
        return 'invalid';
    }
    input.socketData.identityId = session.identityId;
    input.socketData.playerCenterSessionId = session.sessionId;
    return 'authenticated';
};
