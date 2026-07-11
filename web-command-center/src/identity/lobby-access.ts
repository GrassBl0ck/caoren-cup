import crypto from 'node:crypto';

const INVITE_ALPHABET = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
const INVITE_TTL_MS = 12 * 60 * 60 * 1000;
const ATTEMPT_WINDOW_MS = 10 * 60 * 1000;
const BLOCK_MS = 15 * 60 * 1000;
const MAX_FAILURES = 5;

export interface LobbyAccessState {
    inviteCode: string;
    inviteCreatedAt: number;
    inviteExpiresAt: number;
}

type RandomBytes = (size: number) => Buffer;

const makeCode = (length: number, randomBytes: RandomBytes): string => {
    const bytes = randomBytes(length);
    let code = '';
    for (let index = 0; index < length; index++) {
        code += INVITE_ALPHABET[bytes[index] % INVITE_ALPHABET.length];
    }
    return code;
};

export const createLobbyAccess = (
    now = Date.now(),
    randomBytes: RandomBytes = crypto.randomBytes,
): LobbyAccessState => ({
    inviteCode: makeCode(8, randomBytes),
    inviteCreatedAt: now,
    inviteExpiresAt: now + INVITE_TTL_MS,
});

export const rotateLobbyInvite = (
    _current: LobbyAccessState,
    now = Date.now(),
    randomBytes: RandomBytes = crypto.randomBytes,
): LobbyAccessState => createLobbyAccess(now, randomBytes);

interface AttemptState {
    windowStartedAt: number;
    failures: number;
    blockedUntil?: number;
}

export type InviteVerificationResult =
    | { ok: true }
    | { ok: false; reason: 'expired' }
    | { ok: false; reason: 'invalid'; attemptsRemaining: number }
    | { ok: false; reason: 'rate_limited'; retryAt: number };

export class LobbyInviteGuard {
    private readonly attempts = new Map<string, AttemptState>();

    verify(access: LobbyAccessState, sourceKey: string, rawCode: unknown, now = Date.now()): InviteVerificationResult {
        const key = String(sourceKey || 'unknown');
        const code = String(rawCode || '').trim().toUpperCase();
        let state = this.attempts.get(key);

        if (state?.blockedUntil && now < state.blockedUntil) {
            return { ok: false, reason: 'rate_limited', retryAt: state.blockedUntil };
        }
        if (state && now - state.windowStartedAt >= ATTEMPT_WINDOW_MS) {
            this.attempts.delete(key);
            state = undefined;
        }
        if (now >= access.inviteExpiresAt) return { ok: false, reason: 'expired' };
        if (code === access.inviteCode) {
            this.attempts.delete(key);
            return { ok: true };
        }

        state = state || { windowStartedAt: now, failures: 0 };
        state.failures += 1;
        if (state.failures >= MAX_FAILURES) state.blockedUntil = now + BLOCK_MS;
        this.attempts.set(key, state);
        return { ok: false, reason: 'invalid', attemptsRemaining: Math.max(0, MAX_FAILURES - state.failures) };
    }
}
