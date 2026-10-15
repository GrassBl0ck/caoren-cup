import crypto from 'node:crypto';

interface TicketOptions {
    now?: () => number;
    randomBytes?: (size: number) => Buffer;
}

export interface PlayerCenterBootstrapTicket {
    identityId: string;
    accountUpdatedAt: number;
    currentDeviceTokenId?: string;
}

export interface PlayerCenterMatchSocketTicket {
    identityId: string;
    sessionId: string;
    membershipId: string;
}

export interface AccountRecoveryTicket {
    identityId: string;
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
    if (input.secure) return true;
    if (input.production) return false;
    return ['localhost', '127.0.0.1', '::1', '[::1]'].includes(input.hostname.toLowerCase());
};

export const lastForwardedValue = (rawValue: unknown): string | undefined => {
    const values = String(rawValue || '').split(',').map((value) => value.trim()).filter(Boolean);
    return values.length > 0 ? values[values.length - 1] : undefined;
};
