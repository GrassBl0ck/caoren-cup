import { v4 as uuidv4 } from 'uuid';
import { GamePhase, GameSession, Player } from '../types';
import { LobbyMembershipRecord } from './identity-types';

export const applyMembershipToPlayer = (player: Player, membership: LobbyMembershipRecord): Player => {
    player.name = membership.nickname;
    player.identityId = membership.identityId;
    player.membershipId = membership.membershipId;
    player.identityLevel = membership.identityLevel;
    player.confirmationState = membership.confirmationState;
    player.confirmationReason = membership.confirmationReason;
    player.steamId = membership.identityLevel === 'longTerm'
        ? (membership.trustedSteamId || membership.claimedSteamId || player.steamId)
        : undefined;
    return player;
};

export const attachMembershipToSession = (session: GameSession, membership: LobbyMembershipRecord): Player => {
    let player = Object.values(session.players).find((candidate) =>
        candidate.membershipId === membership.membershipId ||
        (!!candidate.identityId && candidate.identityId === membership.identityId),
    );
    if (!player) {
        const playerId = uuidv4();
        player = {
            playerId,
            name: membership.nickname,
            role: session.phase === GamePhase.Lobby ? 'Player' : 'Spectator',
            isReady: false,
        };
        session.players[playerId] = player;
        session.playerOrder.push(playerId);
    }
    applyMembershipToPlayer(player, membership);
    player.isOnline = true;
    return player;
};

export const removeIdentityFromSession = (session: GameSession, identityId: string): Player | undefined => {
    const player = Object.values(session.players).find((candidate) =>
        candidate.identityId === identityId && candidate.role !== 'Admin',
    );
    if (!player) return undefined;
    const playerId = player.playerId;
    delete session.players[playerId];
    session.playerOrder = session.playerOrder.filter((candidate) => candidate !== playerId);
    session.teams.A.players = session.teams.A.players.filter((candidate) => candidate !== playerId);
    session.teams.B.players = session.teams.B.players.filter((candidate) => candidate !== playerId);
    if (session.captains.A === playerId) session.captains.A = null;
    if (session.captains.B === playerId) session.captains.B = null;
    delete session.accusations[playerId];
    return player;
};
