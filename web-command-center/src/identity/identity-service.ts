import crypto from 'node:crypto';
import { generateRandomInitialPassword, generateRandomLoginName, validateLoginName } from './account-foundation';
import { IdentityStore } from './identity-store';
import { hashAccountPassword, verifyAccountPassword } from './password-auth';
import {
    DeviceTokenRecord,
    IdentityRecord,
    LoginAccountRecord,
    LobbyMembershipRecord,
} from './identity-types';

const DAY_MS = 24 * 60 * 60 * 1000;
const TOKEN_IDLE_MS = 30 * DAY_MS;
const TOKEN_ABSOLUTE_MS = 180 * DAY_MS;
const TOKEN_ROTATE_MS = 30 * DAY_MS;

type RandomBytes = (size: number) => Buffer;

interface ServiceOptions {
    now?: () => number;
    randomBytes?: RandomBytes;
}

interface LoginAccountAuthenticationInput {
    loginName: unknown;
    password: unknown;
}

interface AccountRecoveryStartInput {
    steamId: unknown;
    steamNickname: unknown;
}

interface AccountRecoveryCompleteInput {
    identityId: string;
    newPassword: unknown;
}

interface LoginNameChangeInput {
    identityId: string;
    currentPassword: unknown;
    newLoginName: unknown;
    currentSessionId: string;
    currentDeviceTokenId?: string;
}

interface AccountPasswordChangeInput {
    identityId: string;
    currentPassword: unknown;
    newPassword: unknown;
    currentSessionId: string;
    currentDeviceTokenId?: string;
}

const normalizeSteamId = (value: unknown): string => String(value || '').replace(/\D/g, '');
const validSteamId = (value: unknown): string | undefined => {
    const normalized = normalizeSteamId(value);
    return /^7656119\d{10}$/.test(normalized) ? normalized : undefined;
};

const strictSteamId = (value: unknown): string | undefined => {
    const steamId = String(value || '').trim();
    return /^7656119\d{10}$/.test(steamId) ? steamId : undefined;
};

const tokenHash = (secret: string): string => crypto.createHash('sha256').update(secret, 'utf8').digest('hex');

export class LobbyIdentityService {
    private readonly now: () => number;
    private readonly randomBytes: RandomBytes;

    constructor(private readonly store: IdentityStore, options: ServiceOptions = {}) {
        this.now = options.now || Date.now;
        this.randomBytes = options.randomBytes || crypto.randomBytes;
    }

    async joinPlayerCenterMatch(identityId: string, sessionId: string) {
        const now = this.now();
        return this.store.mutate((data) => {
            const identity = data.identities[identityId];
            const account = data.accounts[identityId];
            if (!identity || !account || !account.enabled || account.passwordState !== 'active' || !account.password) {
                return { ok: false as const, reason: 'account_unavailable' as const };
            }
            const memberships = Object.values(data.memberships).filter((membership) =>
                membership.sessionId === sessionId && membership.identityId === identityId,
            );
            if (memberships.some((membership) => !!membership.blockedAt)) {
                return { ok: false as const, reason: 'blocked_for_session' as const };
            }
            const active = memberships.find((membership) => !membership.leftAt);
            if (active) return { ok: true as const, membership: active };

            const nickname = identity.steamNickname || identity.displayName;
            const nicknameInUse = Object.values(data.memberships).some((membership) =>
                membership.sessionId === sessionId && membership.identityId !== identityId &&
                !membership.leftAt && membership.nickname === nickname,
            );
            if (nicknameInUse) return { ok: false as const, reason: 'nickname_in_use' as const };

            const membershipId = this.makeId(16);
            const membership: LobbyMembershipRecord = {
                membershipId,
                sessionId,
                identityId,
                nickname,
                identityLevel: 'longTerm',
                confirmationState: identity.steamId ? 'confirmed' : 'unavailable',
                confirmationReason: identity.steamId ? undefined : 'steam_not_available',
                trustedSteamId: identity.steamId,
                confirmedAt: identity.steamId ? now : undefined,
                joinedAt: now,
                updatedAt: now,
            };
            data.memberships[membershipId] = membership;
            return { ok: true as const, membership };
        });
    }

    async setLoginAccountEnabled(identityId: string, enabled: boolean, sessionId?: string) {
        const now = this.now();
        return this.store.mutate((data) => {
            const identity = data.identities[identityId];
            const account = data.accounts[identityId];
            if (!identity || !account) return undefined;
            account.enabled = enabled;
            account.updatedAt = now;
            identity.updatedAt = now;
            let membership: LobbyMembershipRecord | undefined;
            if (!enabled && sessionId) {
                membership = Object.values(data.memberships).find((candidate) =>
                    candidate.sessionId === sessionId && candidate.identityId === identityId && !candidate.leftAt,
                );
                if (membership) {
                    membership.blockedAt = membership.blockedAt || now;
                    membership.updatedAt = now;
                }
            }
            return { identity, membership };
        });
    }

    getLoginAccount(identityId: string) {
        return this.store.snapshot().accounts[identityId];
    }

    getIdentity(identityId: string) {
        return this.store.snapshot().identities[identityId];
    }

    findAccountByLoginName(loginNameRaw: unknown): LoginAccountRecord | undefined {
        let loginName: string;
        try {
            loginName = validateLoginName(loginNameRaw);
        } catch {
            return undefined;
        }
        return Object.values(this.store.snapshot().accounts).find((account) => account.loginName === loginName);
    }

    async authenticateLoginAccount(input: LoginAccountAuthenticationInput) {
        const account = this.findAccountByLoginName(input.loginName);
        if (!account || account.passwordState !== 'active' || !account.password) {
            return { ok: false as const, reason: 'invalid_credentials' as const };
        }
        if (!await verifyAccountPassword(input.password, account.password)) {
            return { ok: false as const, reason: 'invalid_credentials' as const };
        }
        const identity = this.store.snapshot().identities[account.identityId];
        if (!identity) return { ok: false as const, reason: 'invalid_credentials' as const };
        if (!account.enabled) return { ok: false as const, reason: 'account_disabled' as const };
        return { ok: true as const, identity, account };
    }

    async openOrBeginAccountRecovery(input: AccountRecoveryStartInput) {
        const steamId = strictSteamId(input.steamId);
        if (!steamId) throw new Error('steam_id_invalid');
        const steamNickname = String(input.steamNickname || '').trim().slice(0, 128) || `Steam ${steamId.slice(-6)}`;
        const snapshot = this.store.snapshot();
        const snapshotIdentity = Object.values(snapshot.identities).find((candidate) => candidate.steamId === steamId);
        if (snapshotIdentity && snapshot.accounts[snapshotIdentity.identityId]) {
            const now = this.now();
            return this.store.mutate((data) => {
                const identity = Object.values(data.identities).find((candidate) => candidate.steamId === steamId);
                const account = identity ? data.accounts[identity.identityId] : undefined;
                if (!identity || !account) throw new Error('account_state_changed');
                identity.steamNickname = steamNickname;
                identity.updatedAt = now;
                return account.enabled
                    ? { kind: 'recovery_required' as const, identityId: identity.identityId, loginName: account.loginName }
                    : { kind: 'account_disabled' as const, identityId: identity.identityId };
            });
        }
        const initialPassword = generateRandomInitialPassword(this.randomBytes);
        const password = await hashAccountPassword(initialPassword, { now: this.now, randomBytes: this.randomBytes });
        const now = this.now();
        return this.store.mutate((data) => {
            let identity = Object.values(data.identities).find((candidate) => candidate.steamId === steamId);
            if (identity && data.accounts[identity.identityId]) {
                identity.steamNickname = steamNickname;
                identity.updatedAt = now;
                if (!data.accounts[identity.identityId].enabled) {
                    return {
                        kind: 'account_disabled' as const,
                        identityId: identity.identityId,
                    };
                }
                return {
                    kind: 'recovery_required' as const,
                    identityId: identity.identityId,
                    loginName: data.accounts[identity.identityId].loginName,
                };
            }
            if (!identity) {
                const identityId = this.makeId(16);
                identity = {
                    identityId,
                    displayName: steamNickname,
                    steamId,
                    steamNickname,
                    createdAt: now,
                    updatedAt: now,
                };
                data.identities[identityId] = identity;
            } else {
                identity.steamNickname = steamNickname;
                identity.updatedAt = now;
            }
            const loginName = generateRandomLoginName(
                new Set(Object.values(data.accounts).map((account) => account.loginName)),
                this.randomBytes,
            );
            data.accounts[identity.identityId] = {
                identityId: identity.identityId,
                loginName,
                enabled: true,
                passwordState: 'active',
                password,
                createdAt: now,
                updatedAt: now,
            };
            return {
                kind: 'created' as const,
                identityId: identity.identityId,
                loginName,
                initialPassword,
            };
        });
    }

    async completeAccountRecovery(input: AccountRecoveryCompleteInput) {
        const password = await hashAccountPassword(input.newPassword, { now: this.now, randomBytes: this.randomBytes });
        const now = this.now();
        return this.store.mutate((data) => {
            const identity = data.identities[input.identityId];
            const account = data.accounts[input.identityId];
            if (!identity || !account) throw new Error('account_not_found');
            if (!account.enabled) throw new Error('account_disabled');
            account.password = password;
            account.passwordState = 'active';
            account.updatedAt = now;
            identity.updatedAt = now;
            const revokedDeviceTokenIds: string[] = [];
            for (const token of Object.values(data.deviceTokens)) {
                if (token.identityId !== input.identityId || token.status === 'revoked') continue;
                token.status = 'revoked';
                token.revokedAt = now;
                revokedDeviceTokenIds.push(token.tokenId);
            }
            return {
                account: { identityId: account.identityId, loginName: account.loginName },
                revocation: {
                    identityId: input.identityId,
                    preserveSessionId: undefined,
                    revokeOtherPlayerCenterSessions: true as const,
                    revokedDeviceTokenIds,
                },
            };
        });
    }

    async changeLoginName(input: LoginNameChangeInput) {
        const newLoginName = validateLoginName(input.newLoginName);
        const snapshot = this.store.snapshot();
        const currentAccount = snapshot.accounts[input.identityId];
        if (!currentAccount || currentAccount.passwordState !== 'active' || !currentAccount.password ||
            !await verifyAccountPassword(input.currentPassword, currentAccount.password)) {
            throw new Error('current_password_incorrect');
        }
        const verifiedHash = currentAccount.password.hash;
        const now = this.now();
        return this.store.mutate((data) => {
            const account = data.accounts[input.identityId];
            if (!account || account.passwordState !== 'active' || !account.password || account.password.hash !== verifiedHash) {
                throw new Error('current_password_incorrect');
            }
            if (Object.values(data.accounts).some((candidate) =>
                candidate.identityId !== input.identityId && candidate.loginName === newLoginName,
            )) {
                throw new Error('login_name_in_use');
            }
            account.loginName = newLoginName;
            account.updatedAt = now;
            const revokedDeviceTokenIds: string[] = [];
            for (const token of Object.values(data.deviceTokens)) {
                if (token.identityId !== input.identityId || token.status === 'revoked' ||
                    token.tokenId === input.currentDeviceTokenId) continue;
                token.status = 'revoked';
                token.revokedAt = now;
                revokedDeviceTokenIds.push(token.tokenId);
            }
            return {
                account: { identityId: account.identityId, loginName: account.loginName },
                revocation: {
                    identityId: input.identityId,
                    preserveSessionId: input.currentSessionId,
                    revokeOtherPlayerCenterSessions: true as const,
                    revokedDeviceTokenIds,
                },
            };
        });
    }

    async changeAccountPassword(input: AccountPasswordChangeInput) {
        const snapshot = this.store.snapshot();
        const currentAccount = snapshot.accounts[input.identityId];
        if (!currentAccount || currentAccount.passwordState !== 'active' || !currentAccount.password ||
            !await verifyAccountPassword(input.currentPassword, currentAccount.password)) {
            throw new Error('current_password_incorrect');
        }
        const verifiedHash = currentAccount.password.hash;
        const password = await hashAccountPassword(input.newPassword, { now: this.now, randomBytes: this.randomBytes });
        const now = this.now();
        return this.store.mutate((data) => {
            const account = data.accounts[input.identityId];
            if (!account || account.passwordState !== 'active' || !account.password || account.password.hash !== verifiedHash) {
                throw new Error('current_password_incorrect');
            }
            account.password = password;
            account.updatedAt = now;
            const identity = data.identities[input.identityId];
            if (identity) identity.updatedAt = now;
            const revokedDeviceTokenIds: string[] = [];
            for (const token of Object.values(data.deviceTokens)) {
                if (token.identityId !== input.identityId || token.status === 'revoked' ||
                    token.tokenId === input.currentDeviceTokenId) continue;
                token.status = 'revoked';
                token.revokedAt = now;
                revokedDeviceTokenIds.push(token.tokenId);
            }
            return {
                account: { identityId: account.identityId, loginName: account.loginName },
                revocation: {
                    identityId: input.identityId,
                    preserveSessionId: input.currentSessionId,
                    revokeOtherPlayerCenterSessions: true as const,
                    revokedDeviceTokenIds,
                },
            };
        });
    }

    listLobbySteamIds(sessionId: string): string[] {
        const ids = new Set(
            Object.values(this.store.snapshot().memberships)
                .filter((membership) => membership.sessionId === sessionId && !membership.leftAt && !membership.blockedAt)
                .map((membership) => strictSteamId(membership.trustedSteamId || membership.claimedSteamId))
                .filter((steamId): steamId is string => !!steamId),
        );
        return [...ids].sort();
    }

    async issueDeviceToken(identityId: string, deviceId: string) {
        const now = this.now();
        const tokenId = this.makeId(12);
        const familyId = this.makeId(12);
        const secret = this.randomBytes(32).toString('base64url');
        const rawToken = `ccdt_${tokenId}.${secret}`;
        await this.store.mutate((data) => {
            const identity = data.identities[identityId];
            if (!identity?.steamId) throw new Error('identity_not_confirmed');
            data.deviceTokens[tokenId] = {
                tokenId,
                identityId,
                deviceId: String(deviceId || '').trim() || 'desktop',
                tokenHash: tokenHash(secret),
                familyId,
                status: 'active',
                createdAt: now,
                lastUsedAt: now,
                idleExpiresAt: now + TOKEN_IDLE_MS,
                absoluteExpiresAt: now + TOKEN_ABSOLUTE_MS,
                rotateAfter: now + TOKEN_ROTATE_MS,
            };
        });
        return { rawToken, tokenId, idleExpiresAt: now + TOKEN_IDLE_MS, absoluteExpiresAt: now + TOKEN_ABSOLUTE_MS };
    }

    async authenticatePlayerCenterDeviceToken(rawToken: unknown) {
        const parsed = this.parseToken(rawToken);
        if (!parsed) return { ok: false as const, reason: 'invalid' };
        const now = this.now();
        return this.store.mutate((data) => {
            const token = data.deviceTokens[parsed.tokenId];
            if (!token || !this.tokenMatches(token, parsed.secret)) return { ok: false as const, reason: 'invalid' };
            if (token.status === 'revoked') return { ok: false as const, reason: 'revoked' };
            const idleExpiresAt = Math.min(token.idleExpiresAt, token.lastUsedAt + TOKEN_IDLE_MS);
            const absoluteExpiresAt = Math.min(token.absoluteExpiresAt, token.createdAt + TOKEN_ABSOLUTE_MS);
            if (now >= idleExpiresAt || now >= absoluteExpiresAt) return { ok: false as const, reason: 'expired' };
            const identity = data.identities[token.identityId];
            if (!identity?.steamId) return { ok: false as const, reason: 'identity_not_confirmed' };
            const account = data.accounts[token.identityId];
            if (!account) return { ok: false as const, reason: 'account_unavailable' };
            if (!account.enabled) return { ok: false as const, reason: 'account_disabled' };
            if (account.passwordState !== 'active' || !account.password) {
                return { ok: false as const, reason: 'password_state_invalid' };
            }

            if (token.status === 'pending_rotation') {
                token.status = 'active';
                if (token.rotatedFromTokenId) {
                    const previous = data.deviceTokens[token.rotatedFromTokenId];
                    if (previous) {
                        previous.status = 'revoked';
                        previous.revokedAt = now;
                    }
                }
            }
            token.lastUsedAt = now;
            token.absoluteExpiresAt = absoluteExpiresAt;
            token.idleExpiresAt = Math.min(now + TOKEN_IDLE_MS, absoluteExpiresAt);
            return { ok: true as const, identity, account, tokenId: token.tokenId };
        });
    }

    async revokeDeviceToken(rawToken: unknown): Promise<boolean> {
        const parsed = this.parseToken(rawToken);
        if (!parsed) return false;
        const now = this.now();
        return this.store.mutate((data) => {
            const token = data.deviceTokens[parsed.tokenId];
            if (!token || !this.tokenMatches(token, parsed.secret)) return false;
            token.status = 'revoked';
            token.revokedAt = now;
            return true;
        });
    }

    async beginDeviceTokenRotation(rawToken: unknown) {
        const parsed = this.parseToken(rawToken);
        if (!parsed) return { ok: false as const, reason: 'invalid' };
        const now = this.now();
        const newTokenId = this.makeId(12);
        const secret = this.randomBytes(32).toString('base64url');
        const newRawToken = `ccdt_${newTokenId}.${secret}`;
        return this.store.mutate((data) => {
            const previous = data.deviceTokens[parsed.tokenId];
            if (!previous || !this.tokenMatches(previous, parsed.secret) || previous.status === 'revoked') {
                return { ok: false as const, reason: 'invalid' };
            }
            const idleExpiresAt = Math.min(previous.idleExpiresAt, previous.lastUsedAt + TOKEN_IDLE_MS);
            const absoluteExpiresAt = Math.min(previous.absoluteExpiresAt, previous.createdAt + TOKEN_ABSOLUTE_MS);
            if (now >= idleExpiresAt || now >= absoluteExpiresAt) {
                return { ok: false as const, reason: 'expired' };
            }
            data.deviceTokens[newTokenId] = {
                tokenId: newTokenId,
                identityId: previous.identityId,
                deviceId: previous.deviceId,
                tokenHash: tokenHash(secret),
                familyId: previous.familyId,
                status: 'pending_rotation',
                createdAt: now,
                lastUsedAt: now,
                idleExpiresAt: Math.min(now + TOKEN_IDLE_MS, absoluteExpiresAt),
                absoluteExpiresAt,
                rotateAfter: now + TOKEN_ROTATE_MS,
                rotatedFromTokenId: previous.tokenId,
            };
            return { ok: true as const, rawToken: newRawToken, tokenId: newTokenId };
        });
    }

    async confirmDeviceTokenRotation(rawToken: unknown): Promise<boolean> {
        const parsed = this.parseToken(rawToken);
        if (!parsed) return false;
        const now = this.now();
        return this.store.mutate((data) => {
            const replacement = data.deviceTokens[parsed.tokenId];
            if (!replacement || !this.tokenMatches(replacement, parsed.secret) || replacement.status !== 'pending_rotation') return false;
            replacement.status = 'active';
            if (replacement.rotatedFromTokenId) {
                const previous = data.deviceTokens[replacement.rotatedFromTokenId];
                if (previous) {
                    previous.status = 'revoked';
                    previous.revokedAt = now;
                }
            }
            return true;
        });
    }

    async revokeAllDeviceTokens(identityId: string): Promise<number> {
        const now = this.now();
        return this.store.mutate((data) => {
            let revoked = 0;
            for (const token of Object.values(data.deviceTokens)) {
                if (token.identityId !== identityId || token.status === 'revoked') continue;
                token.status = 'revoked';
                token.revokedAt = now;
                revoked += 1;
            }
            return revoked;
        });
    }

    listDeviceTokens(identityId: string) {
        return Object.values(this.store.snapshot().deviceTokens)
            .filter((token) => token.identityId === identityId)
            .map((token) => ({
                tokenId: token.tokenId,
                deviceId: token.deviceId,
                status: token.status,
                createdAt: token.createdAt,
                lastUsedAt: token.lastUsedAt,
                idleExpiresAt: token.idleExpiresAt,
                revokedAt: token.revokedAt,
            }));
    }

    async revokeDeviceTokenById(identityId: string, tokenId: string): Promise<boolean> {
        const now = this.now();
        return this.store.mutate((data) => {
            const token = data.deviceTokens[tokenId];
            if (!token || token.identityId !== identityId || token.status === 'revoked') return false;
            token.status = 'revoked';
            token.revokedAt = now;
            return true;
        });
    }

    async confirmLongTermPresence(
        sessionId: string,
        trustedPlayersRaw: Array<unknown | { steamId: unknown; name?: unknown }>,
    ): Promise<LobbyMembershipRecord[]> {
        const now = this.now();
        const trustedPlayers = new Map<string, string | undefined>();
        for (const rawPlayer of trustedPlayersRaw) {
            const structured = !!rawPlayer && typeof rawPlayer === 'object' && 'steamId' in rawPlayer;
            const player = structured ? rawPlayer as { steamId: unknown; name?: unknown } : undefined;
            const steamId = validSteamId(player ? player.steamId : rawPlayer);
            if (!steamId) continue;
            const nickname = player ? String(player.name || '').trim().slice(0, 128) || undefined : undefined;
            trustedPlayers.set(steamId, nickname);
        }
        const snapshot = this.store.snapshot();
        const needsUpdate = Object.values(snapshot.memberships).some((membership) => {
            if (membership.sessionId !== sessionId || membership.identityLevel !== 'longTerm' || membership.blockedAt) return false;
            const identity = snapshot.identities[membership.identityId];
            return !!identity?.steamId &&
                (!membership.claimedSteamId || membership.claimedSteamId === identity.steamId) &&
                trustedPlayers.has(identity.steamId) &&
                (membership.confirmationState !== 'confirmed' || membership.trustedSteamId !== identity.steamId ||
                    (!!trustedPlayers.get(identity.steamId) && identity.steamNickname !== trustedPlayers.get(identity.steamId)));
        });
        if (!needsUpdate) return [];
        return this.store.mutate((data) => {
            const updated: LobbyMembershipRecord[] = [];
            for (const membership of Object.values(data.memberships)) {
                if (membership.sessionId !== sessionId || membership.identityLevel !== 'longTerm' || membership.blockedAt) continue;
                const identity = data.identities[membership.identityId];
                if (!identity?.steamId || !trustedPlayers.has(identity.steamId)) continue;
                if (membership.claimedSteamId && membership.claimedSteamId !== identity.steamId) continue;
                const trustedNickname = trustedPlayers.get(identity.steamId);
                if (trustedNickname && identity.steamNickname !== trustedNickname) {
                    identity.steamNickname = trustedNickname;
                    identity.updatedAt = now;
                }
                if (membership.confirmationState === 'confirmed' && membership.trustedSteamId === identity.steamId) continue;
                membership.confirmationState = 'confirmed';
                membership.confirmationReason = undefined;
                membership.trustedSteamId = identity.steamId;
                membership.confirmedAt = now;
                membership.updatedAt = now;
                updated.push(membership);
            }
            return updated;
        });
    }

    async blockMembership(membershipId: string): Promise<boolean> {
        const now = this.now();
        return this.store.mutate((data) => {
            const membership = data.memberships[membershipId];
            if (!membership) return false;
            membership.blockedAt = now;
            membership.updatedAt = now;
            return true;
        });
    }

    async leaveMembership(membershipId: string): Promise<boolean> {
        const now = this.now();
        return this.store.mutate((data) => {
            const membership = data.memberships[membershipId];
            if (!membership) return false;
            membership.leftAt = now;
            membership.updatedAt = now;
            return true;
        });
    }

    async leaveSessionMemberships(sessionId: string): Promise<number> {
        const now = this.now();
        return this.store.mutate((data) => {
            let count = 0;
            for (const membership of Object.values(data.memberships)) {
                if (membership.sessionId !== sessionId || membership.leftAt) continue;
                membership.leftAt = now;
                membership.updatedAt = now;
                count += 1;
            }
            return count;
        });
    }

    findIdentityBySteamId(steamIdRaw: unknown): IdentityRecord | undefined {
        const steamId = validSteamId(steamIdRaw);
        if (!steamId) return undefined;
        return Object.values(this.store.snapshot().identities).find((identity) => identity.steamId === steamId);
    }

    getMembership(membershipId: string): LobbyMembershipRecord | undefined {
        return this.store.snapshot().memberships[membershipId];
    }

    listMemberships(sessionId: string): LobbyMembershipRecord[] {
        return Object.values(this.store.snapshot().memberships).filter((membership) => membership.sessionId === sessionId && !membership.leftAt);
    }

    private createRecoveryAccount(
        data: ReturnType<IdentityStore['snapshot']>,
        identityId: string,
        now: number,
    ): LoginAccountRecord {
        const loginNames = new Set(Object.values(data.accounts).map((account) => account.loginName));
        const account: LoginAccountRecord = {
            identityId,
            loginName: generateRandomLoginName(loginNames, this.randomBytes),
            enabled: true,
            passwordState: 'recovery_required' as const,
            createdAt: now,
            updatedAt: now,
        };
        data.accounts[identityId] = account;
        return account;
    }

    private makeId(size: number): string {
        return this.randomBytes(size).toString('hex');
    }

    private parseToken(rawToken: unknown): { tokenId: string; secret: string } | undefined {
        const match = /^ccdt_([a-f0-9]+)\.([A-Za-z0-9_-]+)$/.exec(String(rawToken || '').trim());
        return match ? { tokenId: match[1], secret: match[2] } : undefined;
    }

    private tokenMatches(token: DeviceTokenRecord, secret: string): boolean {
        const expected = Buffer.from(token.tokenHash, 'hex');
        const actual = Buffer.from(tokenHash(secret), 'hex');
        return expected.length === actual.length && crypto.timingSafeEqual(expected, actual);
    }
}
