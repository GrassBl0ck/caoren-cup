import crypto from 'node:crypto';

export interface PasswordCredential {
    algorithm: 'scrypt';
    salt: string;
    hash: string;
    params: {
        N: number;
        r: number;
        p: number;
        keyLength: number;
        maxmem: number;
    };
    updatedAt: number;
}

export const DEFAULT_SCRYPT_PARAMS = {
    N: 16_384,
    r: 8,
    p: 1,
    keyLength: 64,
    maxmem: 64 * 1024 * 1024,
} as const;

interface PasswordHashOptions {
    now?: () => number;
    randomBytes?: (size: number) => Buffer;
}

const deriveScrypt = (
    password: string,
    salt: Buffer,
    params: PasswordCredential['params'],
): Promise<Buffer> => new Promise((resolve, reject) => {
    crypto.scrypt(password, salt, params.keyLength, {
        N: params.N,
        r: params.r,
        p: params.p,
        maxmem: params.maxmem,
    }, (error, derivedKey) => {
        if (error) reject(error);
        else resolve(derivedKey);
    });
});

export const validateAccountPassword = (value: unknown): string => {
    if (typeof value !== 'string') throw new Error('password_invalid');
    const length = Array.from(value).length;
    if (length < 8 || length > 128) throw new Error('password_invalid');
    return value;
};

export const hashAccountPassword = async (
    rawPassword: unknown,
    options: PasswordHashOptions = {},
): Promise<PasswordCredential> => {
    const password = validateAccountPassword(rawPassword);
    const params = { ...DEFAULT_SCRYPT_PARAMS };
    const salt = (options.randomBytes || crypto.randomBytes)(32);
    const hash = await deriveScrypt(password, salt, params);
    return {
        algorithm: 'scrypt',
        salt: salt.toString('base64url'),
        hash: hash.toString('base64url'),
        params,
        updatedAt: (options.now || Date.now)(),
    };
};

export const verifyAccountPassword = async (
    rawPassword: unknown,
    credential: PasswordCredential,
): Promise<boolean> => {
    let password: string;
    try {
        password = validateAccountPassword(rawPassword);
    } catch {
        return false;
    }
    if (credential?.algorithm !== 'scrypt') return false;
    try {
        const expected = Buffer.from(credential.hash, 'base64url');
        const actual = await deriveScrypt(password, Buffer.from(credential.salt, 'base64url'), credential.params);
        return expected.length === actual.length && crypto.timingSafeEqual(expected, actual);
    } catch {
        return false;
    }
};

interface LoginGuardOptions {
    now?: () => number;
    attemptWindowMs?: number;
    blockMs?: number;
    maxFailures?: number;
}

interface LoginAttemptState {
    windowStartedAt: number;
    failures: number;
    blockedUntil?: number;
}

export type LoginGuardResult = { blocked: false } | { blocked: true; retryAt: number };

export class AccountLoginGuard {
    private readonly attempts = new Map<string, LoginAttemptState>();
    private readonly now: () => number;
    private readonly attemptWindowMs: number;
    private readonly blockMs: number;
    private readonly maxFailures: number;

    constructor(options: LoginGuardOptions = {}) {
        this.now = options.now || Date.now;
        this.attemptWindowMs = options.attemptWindowMs || 10 * 60 * 1000;
        this.blockMs = options.blockMs || 15 * 60 * 1000;
        this.maxFailures = options.maxFailures || 10;
    }

    check(keys: string[]): LoginGuardResult {
        const now = this.now();
        for (const key of new Set(keys)) {
            const state = this.currentState(key, now);
            if (state?.blockedUntil && now < state.blockedUntil) {
                return { blocked: true, retryAt: state.blockedUntil };
            }
        }
        return { blocked: false };
    }

    recordFailure(keys: string[]): LoginGuardResult {
        const now = this.now();
        let retryAt = 0;
        for (const key of new Set(keys)) {
            const state = this.currentState(key, now) || { windowStartedAt: now, failures: 0 };
            state.failures += 1;
            if (state.failures >= this.maxFailures) state.blockedUntil = now + this.blockMs;
            this.attempts.set(key, state);
            retryAt = Math.max(retryAt, state.blockedUntil || 0);
        }
        return retryAt > now ? { blocked: true, retryAt } : { blocked: false };
    }

    clear(keys: string[]): void {
        for (const key of new Set(keys)) this.attempts.delete(key);
    }

    private currentState(key: string, now: number): LoginAttemptState | undefined {
        const state = this.attempts.get(key);
        if (!state) return undefined;
        if (state.blockedUntil && now >= state.blockedUntil) {
            this.attempts.delete(key);
            return undefined;
        }
        if (!state.blockedUntil && now - state.windowStartedAt >= this.attemptWindowMs) {
            this.attempts.delete(key);
            return undefined;
        }
        return state;
    }
}
