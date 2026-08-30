import path from 'node:path';
import {
    AccountRecoveryTicket,
    EphemeralTicketService,
    PlayerCenterBootstrapTicket,
    PlayerCenterMatchSocketTicket,
} from './auth-core';
import { LobbyIdentityService } from './identity-service';
import { IdentityStore } from './identity-store';
import { PlayerCenterSessionStore } from './player-center-session-store';

const identityStorePath = process.env.IDENTITY_STORE_PATH
    ? path.resolve(process.env.IDENTITY_STORE_PATH)
    : path.resolve(__dirname, '..', '..', 'runtime', 'identity-store.json');

const playerCenterSessionStorePath = process.env.PLAYER_CENTER_SESSION_STORE_PATH
    ? path.resolve(process.env.PLAYER_CENTER_SESSION_STORE_PATH)
    : path.resolve(__dirname, '..', '..', 'runtime', 'player-center-sessions.json');

export const identityStore = new IdentityStore(identityStorePath);
export const playerCenterSessionStore = new PlayerCenterSessionStore(playerCenterSessionStorePath);
export const lobbyIdentityService = new LobbyIdentityService(identityStore);
export const playerCenterBootstrapTickets = new EphemeralTicketService<PlayerCenterBootstrapTicket>();
export const playerCenterMatchSocketTickets = new EphemeralTicketService<PlayerCenterMatchSocketTicket>();
export const accountRecoveryTickets = new EphemeralTicketService<AccountRecoveryTicket>();

let initializePromise: Promise<void> | undefined;

export const initializeIdentityRuntime = (): Promise<void> => {
    if (!initializePromise) {
        initializePromise = Promise.all([identityStore.load(), playerCenterSessionStore.load()]).then(() => undefined);
    }
    return initializePromise;
};
