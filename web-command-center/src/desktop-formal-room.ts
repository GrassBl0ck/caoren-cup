import { readFileSync } from 'node:fs';
import { normalizeEmail } from './identity/email-address';
import type { LoginAccountRecord } from './identity/identity-types';
import { randomUUID } from 'node:crypto';
import { GamePhase, type GameSession, type Player } from './types';

export const roomParticipants = (session: GameSession): Player[] =>
    Object.values(session.players).filter(player => player.role === 'Player');

export const roomUnreadyPlayers = (session: GameSession): Player[] =>
    roomParticipants(session).filter(player => !player.lobbyReady);

export function canStartDesktopRoom(session: GameSession): boolean {
    const participants = roomParticipants(session);
    return participants.length > 0 &&
        participants.every(player => player.lobbyReady && /^7656119\d{10}$/.test(player.steamId ?? ''));
}

type AdminAccountLookup = { getLoginAccount(id: string): Pick<LoginAccountRecord, 'enabled' | 'email' | 'emailVerifiedAt'> | undefined };

export function loadDesktopRoomAdmins(configPath?: string) {
    const emails = new Set<string>();
    if (configPath) {
        try {
            const config = JSON.parse(readFileSync(configPath, 'utf8').replace(/^\uFEFF/, ''));
            if (!Array.isArray(config.adminEmails)) throw new Error('adminEmails must be an array');
            const normalized = config.adminEmails.map(normalizeEmail);
            for (const email of normalized) emails.add(email);
        } catch {
            console.error('[DesktopRoom] Cannot load adminEmails from', configPath);
        }
    }
    // Older installations identify hosts by ID. An explicit file is authoritative.
    const legacyIds = () => (process.env.CAOREN_ROOM_ADMIN_IDENTITY_IDS ?? '').split(',').map(id => id.trim()).filter(Boolean);
    return {
        configured: () => configPath ? emails.size > 0 : legacyIds().length > 0,
        allows(identityId: string, accounts: AdminAccountLookup): boolean {
            if (!identityId) return false;
            if (!configPath) return legacyIds().includes(identityId);
            if (!emails.size) return false;
            const account = accounts.getLoginAccount(identityId);
            return !!(account?.enabled && account.emailVerifiedAt && account.email && emails.has(account.email.trim().toLowerCase()));
        },
    };
}

export const desktopRoomAdmins = loadDesktopRoomAdmins(process.env.CAOREN_ROOM_ADMIN_CONFIG);

interface ForceReadyPreview {
    token: string;
    sessionId: string;
    participants: string;
    expiresAt: number;
}

export class ForceRoomReady {
    private pending = new Map<string, ForceReadyPreview>();
    constructor(private readonly now: () => number = Date.now) {}

    private fingerprint(session: GameSession): string {
        return JSON.stringify(roomParticipants(session).map(player => [player.playerId, player.lobbyReady ?? null]).sort());
    }

    preview(session: GameSession, actorId: string): { token: string; players: { id: string; name: string }[] } | undefined {
        if (session.phase !== GamePhase.Lobby) return;
        const players = roomUnreadyPlayers(session);
        if (!players.length) return;
        const now = this.now();
        for (const [id, entry] of this.pending) if (entry.expiresAt <= now) this.pending.delete(id);
        const token = randomUUID();
        this.pending.set(actorId, {
            token, sessionId: session.sessionId, participants: this.fingerprint(session), expiresAt: now + 60_000,
        });
        return { token, players: players.map(player => ({ id: player.playerId, name: player.name })) };
    }

    confirm(session: GameSession, actorId: string, token: unknown): boolean {
        const preview = this.pending.get(actorId);
        if (!preview || preview.token !== token) return false;
        this.pending.delete(actorId);
        if (session.phase !== GamePhase.Lobby || preview.sessionId !== session.sessionId ||
            preview.expiresAt <= this.now() || preview.participants !== this.fingerprint(session)) return false;
        for (const player of roomUnreadyPlayers(session)) player.lobbyReady = 'forced';
        return true;
    }
}
