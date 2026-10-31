import crypto from 'node:crypto';
import { normalizeEmail } from './email-address';

export type EmailCodePurpose = 'register' | 'bind-email' | 'reset-password';
export interface VerificationEmail { email: string; code: string; purpose: EmailCodePurpose; expiresInMinutes: number }
export class EmailAuthError extends Error {
    constructor(message: string, readonly status = 400, readonly retryAfter?: number) { super(message); }
}

interface Challenge {
    email: string;
    purpose: EmailCodePurpose;
    principal: string;
    hash: Buffer;
    expiresAt: number;
    attempts: number;
    ready: boolean;
    eligible: boolean;
}

// Process-local, fail-closed challenges. Restarting the service invalidates pending codes.
export class EmailCodeService {
    private readonly challenges = new Map<string, Challenge>();
    private readonly current = new Map<string, string>();
    private readonly limits = new Map<string, { count: number; until: number }>();
    private readonly secret = crypto.randomBytes(32);
    private readonly now: () => number;
    private readonly generateCode: () => string;
    readonly enabled: boolean;

    constructor(private readonly options: {
        deliver?: (message: VerificationEmail) => Promise<void>;
        now?: () => number;
        generateCode?: () => string;
    } = {}) {
        this.now = options.now || Date.now;
        this.generateCode = options.generateCode || (() => crypto.randomInt(1_000_000).toString().padStart(6, '0'));
        this.enabled = !!options.deliver;
    }

    private hash(id: string, code: string) { return crypto.createHmac('sha256', this.secret).update(`${id}:${code}`).digest(); }
    private key(email: string, purpose: EmailCodePurpose, principal: string) { return `${purpose}:${principal}:${email}`; }
    private clean() {
        const now = this.now();
        for (const [id, entry] of this.challenges) {
            if (entry.expiresAt <= now) this.challenges.delete(id);
        }
        for (const [key, id] of this.current) if (!this.challenges.has(id)) this.current.delete(key);
        for (const [key, entry] of this.limits) if (entry.until <= now) this.limits.delete(key);
    }
    private reserve(key: string, maximum: number, duration: number) {
        if (!this.limits.has(key) && this.limits.size >= 30_000) throw new EmailAuthError('email_service_busy', 503);
        const previous = this.limits.get(key);
        const entry = previous && previous.until > this.now() ? previous : { count: 0, until: this.now() + duration };
        if (entry.count >= maximum) throw new EmailAuthError('email_rate_limited', 429, Math.max(1, Math.ceil((entry.until - this.now()) / 1000)));
        entry.count++;
        this.limits.set(key, entry);
    }

    async request(input: { email: unknown; purpose: EmailCodePurpose; ip: string; principal?: string; eligible?: boolean }) {
        if (!this.enabled) throw new EmailAuthError('email_service_unavailable', 503);
        const email = normalizeEmail(input.email);
        this.clean();
        if (this.challenges.size >= 5_000 || this.limits.size >= 30_000) throw new EmailAuthError('email_service_busy', 503);
        // Reserve before awaiting SMTP: concurrent sends cannot bypass the limits.
        this.reserve(`send-ip:${input.ip}`, 30, 60 * 60_000);
        this.reserve(`send-email:${email}`, 8, 60 * 60_000);
        this.reserve(`cooldown:${email}`, 1, 60_000);
        const principal = input.principal || '';
        const key = this.key(email, input.purpose, principal);
        const previous = this.current.get(key);
        if (previous) this.challenges.delete(previous);
        const id = crypto.randomBytes(24).toString('base64url');
        const code = this.generateCode();
        const entry: Challenge = {
            email, purpose: input.purpose, principal, hash: this.hash(id, code),
            expiresAt: this.now() + 10 * 60_000, attempts: 0, ready: false, eligible: input.eligible !== false,
        };
        this.challenges.set(id, entry);
        this.current.set(key, id);
        try {
            // A recovery request for an unknown address has the same response shape without sending unwanted mail.
            if (entry.eligible) await this.options.deliver!({ email, code, purpose: input.purpose, expiresInMinutes: 10 });
            entry.ready = true;
        } catch {
            this.challenges.delete(id);
            if (this.current.get(key) === id) this.current.delete(key);
            throw new EmailAuthError('email_delivery_failed', 503);
        }
        return { challengeId: id, expiresInSeconds: 600, resendAfterSeconds: 60 };
    }

    consume(input: { challengeId: unknown; code: unknown; email: unknown; purpose: EmailCodePurpose; principal?: string; ip: string }): string {
        this.clean();
        this.reserve(`verify-ip:${input.ip}`, 60, 10 * 60_000);
        const id = typeof input.challengeId === 'string' ? input.challengeId : '';
        const entry = this.challenges.get(id);
        if (!entry || !entry.ready) throw new EmailAuthError('email_code_invalid');
        entry.attempts++;
        let email = '';
        try { email = normalizeEmail(input.email); } catch { /* Count invalid contextual attempts as well. */ }
        const code = typeof input.code === 'string' && /^\d{6}$/.test(input.code) ? input.code : '';
        const matches = entry.eligible && email === entry.email && input.purpose === entry.purpose &&
            (input.principal || '') === entry.principal && crypto.timingSafeEqual(entry.hash, this.hash(id, code));
        if (matches || entry.attempts >= 5) {
            this.challenges.delete(id);
            const key = this.key(entry.email, entry.purpose, entry.principal);
            if (this.current.get(key) === id) this.current.delete(key);
        }
        if (!matches) throw new EmailAuthError('email_code_invalid');
        return email;
    }
}
