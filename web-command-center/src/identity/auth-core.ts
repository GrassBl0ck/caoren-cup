import crypto from 'node:crypto';

interface TicketOptions {
    now?: () => number;
    randomBytes?: (size: number) => Buffer;
}

export interface SocketLoginTicket {
    membershipId: string;
    sessionId: string;
}

export type FixedAccountAdminOperation = 'create' | 'rename' | 'reset_password' | 'set_enabled';

export interface FixedAccountAdminTicket {
    sessionId: string;
    adminPlayerId: string;
    operation: FixedAccountAdminOperation;
    identityId?: string;
    steamId?: string;
}

export class EphemeralTicketService<T> {
    private readonly values = new Map<string, { value: T; expiresAt: number }>();
    private readonly now: () => number;
    private readonly randomBytes: (size: number) => Buffer;

    constructor(options: TicketOptions = {}) {
        this.now = options.now || Date.now;
        this.randomBytes = options.randomBytes || crypto.randomBytes;
    }

    issue(value: T, ttlMs: number): { ticket: string; expiresAt: number } {
        const ticket = this.randomBytes(32).toString('base64url');
        const expiresAt = this.now() + ttlMs;
        this.values.set(ticket, { value, expiresAt });
        return { ticket, expiresAt };
    }

    consume(ticketRaw: unknown): T | undefined {
        const ticket = String(ticketRaw || '').trim();
        const entry = this.values.get(ticket);
        if (!entry) return undefined;
        this.values.delete(ticket);
        if (this.now() >= entry.expiresAt) return undefined;
        return entry.value;
    }
}

export const isDeviceAuthTransportAllowed = (input: {
    production: boolean;
    secure: boolean;
    hostname: string;
}): boolean => {
    if (!input.production) return true;
    return input.secure === true;
};

export const lastForwardedValue = (rawValue: unknown): string | undefined => {
    const values = String(rawValue || '').split(',').map((value) => value.trim()).filter(Boolean);
    return values.length > 0 ? values[values.length - 1] : undefined;
};

export const socketLoginTicketMatchesSession = (ticket: SocketLoginTicket, activeSessionId: string): boolean =>
    !!ticket.membershipId && !!ticket.sessionId && ticket.sessionId === activeSessionId;
