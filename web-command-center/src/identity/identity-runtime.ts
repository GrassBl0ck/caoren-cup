import path from 'node:path';
import { EphemeralTicketService, FixedAccountAdminTicket, SocketLoginTicket } from './auth-core';
import { LobbyIdentityService } from './identity-service';
import { IdentityStore } from './identity-store';
import { SteamAccountClaim } from './identity-types';

const identityStorePath = process.env.IDENTITY_STORE_PATH
    ? path.resolve(process.env.IDENTITY_STORE_PATH)
    : path.resolve(__dirname, '..', '..', 'runtime', 'identity-store.json');

export const identityStore = new IdentityStore(identityStorePath);
export const lobbyIdentityService = new LobbyIdentityService(identityStore);
export const socketLoginTickets = new EphemeralTicketService<SocketLoginTicket>();
export const fixedMemberSocketTickets = new EphemeralTicketService<SocketLoginTicket>();
export const fixedAccountAdminTickets = new EphemeralTicketService<FixedAccountAdminTicket>();
export const deviceEnrollmentTickets = new EphemeralTicketService<{ identityId: string }>();
export const steamClaimTickets = new EphemeralTicketService<SteamAccountClaim>();

let initializePromise: Promise<void> | undefined;

export const initializeIdentityRuntime = (): Promise<void> => {
    if (!initializePromise) {
        initializePromise = identityStore.load().then(async () => {
            await lobbyIdentityService.pruneTemporaryRecords();
        });
    }
    return initializePromise;
};
