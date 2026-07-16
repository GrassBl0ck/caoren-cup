import crypto from 'node:crypto';
import { IdentityStore } from './identity-store';
import { hashFixedMemberPassword, verifyFixedMemberPassword } from './password-auth';
import {
    DeviceTokenRecord,
    IdentityRecord,
    LobbyMembershipRecord,
    PluginConfirmationChallenge,
    SteamAccountClaim,
} from './identity-types';

const DAY_MS = 24 * 60 * 60 * 1000;
const CHALLENGE_TTL_MS = 10 * 60 * 1000;
const TOKEN_IDLE_MS = 90 * DAY_MS;
const TOKEN_ABSOLUTE_MS = 365 * DAY_MS;
const TOKEN_ROTATE_MS = 30 * DAY_MS;
const CODE_ALPHABET = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';

type RandomBytes = (size: number) => Buffer;

interface ServiceOptions {
    now?: () => number;
    randomBytes?: RandomBytes;
}

interface TemporaryMembershipInput {
    sessionId: string;
    nickname: string;
    steamClaim?: SteamAccountClaim;
}

interface DeviceAuthenticationInput {
    sessionId: string;
    steamClaim?: SteamAccountClaim;
}

interface FixedAccountInput {
    steamId: string;
    nickname: string;
    password: string;
    sessionId?: string;
    nicknameInUse?: (nickname: string, identityId: string) => boolean;
}

interface FixedAccountAuthenticationInput {
    sessionId: string;
    steamId: string;
    password: string;
    nicknameInUse?: (nickname: string, identityId: string) => boolean;
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

const validNickname = (value: unknown): string => {
    const nickname = String(value || '').trim();
    if (!nickname || nickname.length > 32 || /[<>&"'`\\\x00-\x1f\x7f]/.test(nickname)) throw new Error('nickname_invalid');
    return nickname;
};

const tokenHash = (secret: string): string => crypto.createHash('sha256').update(secret, 'utf8').digest('hex');

export class LobbyIdentityService {
    private readonly now: () => number;
    private readonly randomBytes: RandomBytes;

    constructor(private readonly store: IdentityStore, options: ServiceOptions = {}) {
        this.now = options.now || Date.now;
        this.randomBytes = options.randomBytes || crypto.randomBytes;
    }

    async createOrUpdateFixedAccount(input: FixedAccountInput): Promise<{ identity: IdentityRecord; created: boolean }> {
        const steamId = strictSteamId(input.steamId);
        if (!steamId) throw new Error('steam_id_invalid');
        const nickname = validNickname(input.nickname);
        const password = await hashFixedMemberPassword(input.password, { now: this.now, randomBytes: this.randomBytes });
        const now = this.now();
        return this.store.mutate((data) => {
            const matching = Object.values(data.identities).filter((identity) => identity.steamId === steamId);
            if (matching.length > 1) throw new Error('steam_id_conflict');
            let identity = matching[0];
            const created = !identity;
            if (!identity) {
                const identityId = this.makeId(16);
                identity = {
                    identityId,
                    displayName: nickname,
                    steamId,
                    createdAt: now,
                    updatedAt: now,
                };
                data.identities[identityId] = identity;
            }
            if (input.nicknameInUse?.(nickname, identity.identityId)) throw new Error('nickname_in_use');
            if (input.sessionId) this.assertNicknameAvailable(data, input.sessionId, nickname, identity.identityId);
            const enabled = identity.fixedAccount?.enabled ?? true;
            identity.displayName = nickname;
            identity.fixedAccount = { enabled, password };
            identity.updatedAt = now;
            if (input.sessionId) {
                for (const membership of Object.values(data.memberships)) {
                    if (membership.sessionId === input.sessionId && membership.identityId === identity.identityId && !membership.leftAt) {
                        membership.nickname = nickname;
                        membership.updatedAt = now;
                    }
                }
            }
            return { identity, created };
        });
    }

    async authenticateFixedAccount(input: FixedAccountAuthenticationInput) {
        const steamId = strictSteamId(input.steamId);
        if (!steamId) return { ok: false as const, reason: 'account_not_found' as const };
        const snapshot = this.store.snapshot();
        const identity = Object.values(snapshot.identities).find((candidate) => candidate.steamId === steamId);
        if (!identity?.fixedAccount) return { ok: false as const, reason: 'account_not_found' as const };
        if (!identity.fixedAccount.enabled) return { ok: false as const, reason: 'account_disabled' as const };
        if (!await verifyFixedMemberPassword(input.password, identity.fixedAccount.password)) {
            return { ok: false as const, reason: 'password_incorrect' as const };
        }
        if (input.nicknameInUse?.(identity.displayName, identity.identityId)) {
            return { ok: false as const, reason: 'nickname_in_use' as const };
        }
        const verifiedHash = identity.fixedAccount.password.hash;
        const now = this.now();
        return this.store.mutate((data) => {
            const currentIdentity = data.identities[identity.identityId];
            if (!currentIdentity?.fixedAccount || currentIdentity.steamId !== steamId) {
                return { ok: false as const, reason: 'account_not_found' as const };
            }
            if (!currentIdentity.fixedAccount.enabled) return { ok: false as const, reason: 'account_disabled' as const };
            if (currentIdentity.fixedAccount.password.hash !== verifiedHash) {
                return { ok: false as const, reason: 'password_incorrect' as const };
            }
            const memberships = Object.values(data.memberships).filter((membership) =>
                membership.sessionId === input.sessionId && membership.identityId === currentIdentity.identityId,
            );
            if (memberships.some((membership) => !!membership.blockedAt)) {
                return { ok: false as const, reason: 'blocked_for_session' as const };
            }
            let membership = memberships.find((candidate) => !candidate.leftAt);
            if (membership) return { ok: true as const, identity: currentIdentity, membership };
            const nicknameInUse = Object.values(data.memberships).some((candidate) =>
                candidate.sessionId === input.sessionId &&
                candidate.identityId !== currentIdentity.identityId &&
                !candidate.leftAt &&
                candidate.nickname === currentIdentity.displayName,
            );
            if (nicknameInUse) return { ok: false as const, reason: 'nickname_in_use' as const };
            const membershipId = this.makeId(16);
            membership = {
                membershipId,
                sessionId: input.sessionId,
                identityId: currentIdentity.identityId,
                nickname: currentIdentity.displayName,
                identityLevel: 'longTerm',
                confirmationState: 'pending',
                claimedSteamId: steamId,
                joinedAt: now,
                updatedAt: now,
            };
            data.memberships[membershipId] = membership;
            return { ok: true as const, identity: currentIdentity, membership };
        });
    }

    async renameFixedAccount(identityId: string, nicknameRaw: unknown, sessionId?: string): Promise<IdentityRecord | undefined> {
        const nickname = validNickname(nicknameRaw);
        const now = this.now();
        return this.store.mutate((data) => {
            const identity = data.identities[identityId];
            if (!identity?.fixedAccount) return undefined;
            if (sessionId) this.assertNicknameAvailable(data, sessionId, nickname, identityId);
            identity.displayName = nickname;
            identity.updatedAt = now;
            if (sessionId) {
                for (const membership of Object.values(data.memberships)) {
                    if (membership.sessionId === sessionId && membership.identityId === identityId && !membership.leftAt) {
                        membership.nickname = nickname;
                        membership.updatedAt = now;
                    }
                }
            }
            return identity;
        });
    }

    async resetFixedAccountPassword(identityId: string, rawPassword: unknown): Promise<IdentityRecord | undefined> {
        const password = await hashFixedMemberPassword(rawPassword, { now: this.now, randomBytes: this.randomBytes });
        const now = this.now();
        return this.store.mutate((data) => {
            const identity = data.identities[identityId];
            if (!identity?.fixedAccount) return undefined;
            identity.fixedAccount.password = password;
            identity.updatedAt = now;
            return identity;
        });
    }

    async setFixedAccountEnabled(identityId: string, enabled: boolean, sessionId?: string) {
        const now = this.now();
        return this.store.mutate((data) => {
            const identity = data.identities[identityId];
            if (!identity?.fixedAccount) return undefined;
            identity.fixedAccount.enabled = enabled;
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

    listFixedAccounts(sessionId?: string) {
        const data = this.store.snapshot();
        return Object.values(data.identities)
            .filter((identity) => !!identity.steamId && !!identity.fixedAccount)
            .map((identity) => {
                const membership = sessionId
                    ? Object.values(data.memberships).find((candidate) =>
                        candidate.sessionId === sessionId && candidate.identityId === identity.identityId && !candidate.leftAt,
                    )
                    : undefined;
                return {
                    identityId: identity.identityId,
                    steamId: identity.steamId!,
                    nickname: identity.displayName,
                    enabled: identity.fixedAccount!.enabled,
                    passwordUpdatedAt: identity.fixedAccount!.password.updatedAt,
                    membership,
                };
            })
            .sort((left, right) => left.nickname.localeCompare(right.nickname, 'zh-CN'));
    }

    listLobbySteamIds(sessionId: string): string[] {
        const ids = new Set(
            Object.values(this.store.snapshot().memberships)
                .filter((membership) => membership.sessionId === sessionId && !membership.leftAt && !membership.blockedAt)
                .map((membership) => strictSteamId(membership.claimedSteamId))
                .filter((steamId): steamId is string => !!steamId),
        );
        return [...ids].sort();
    }

    async createTemporaryMembership(input: TemporaryMembershipInput): Promise<LobbyMembershipRecord> {
        const now = this.now();
        const nickname = validNickname(input.nickname);
        const claimedSteamId = input.steamClaim ? validSteamId(input.steamClaim.steamId) : undefined;
        if (input.steamClaim?.steamId && !claimedSteamId) throw new Error('steam_id_invalid');

        return this.store.mutate((data) => {
            const duplicateName = Object.values(data.memberships).some((membership) =>
                membership.sessionId === input.sessionId && !membership.leftAt && membership.nickname === nickname,
            );
            if (duplicateName) throw new Error('nickname_in_use');

            const identityId = this.makeId(16);
            const membershipId = this.makeId(16);
            data.identities[identityId] = {
                identityId,
                displayName: nickname,
                createdAt: now,
                updatedAt: now,
            };
            const claimInUse = claimedSteamId && Object.values(data.memberships).some((membership) =>
                membership.sessionId === input.sessionId && !membership.leftAt && membership.claimedSteamId === claimedSteamId,
            );
            const membership: LobbyMembershipRecord = {
                membershipId,
                sessionId: input.sessionId,
                identityId,
                nickname,
                identityLevel: 'temporary',
                confirmationState: claimedSteamId && !claimInUse ? 'pending' : 'unavailable',
                confirmationReason: claimInUse ? 'claim_in_use' : (claimedSteamId ? undefined : 'steam_not_available'),
                claimedSteamId: claimInUse ? undefined : claimedSteamId,
                claimPersonaName: claimInUse ? undefined : String(input.steamClaim?.personaName || '').trim() || undefined,
                joinedAt: now,
                updatedAt: now,
            };
            data.memberships[membershipId] = membership;
            return membership;
        });
    }

    async getConfirmationChallenges(
        sessionId: string,
        trustedPlayers: Array<{ steamId: unknown; name?: unknown }>,
    ): Promise<PluginConfirmationChallenge[]> {
        const now = this.now();
        const onlineSteamIds = new Set(trustedPlayers.map((player) => validSteamId(player.steamId)).filter(Boolean));
        const snapshotMemberships = Object.values(this.store.snapshot().memberships).filter((membership) =>
            membership.sessionId === sessionId &&
            membership.identityLevel === 'temporary' &&
            membership.confirmationState === 'pending' &&
            !membership.leftAt &&
            !membership.blockedAt &&
            !!membership.claimedSteamId &&
            onlineSteamIds.has(membership.claimedSteamId),
        );
        const needsNewChallenge = snapshotMemberships.some((membership) =>
            !membership.challenge || membership.challenge.expiresAt <= now || membership.challenge.failedAttempts >= 5,
        );
        if (!needsNewChallenge) {
            return snapshotMemberships.map((membership) => ({
                challengeId: membership.challenge!.challengeId,
                membershipId: membership.membershipId,
                steamId: membership.claimedSteamId!,
                code: membership.challenge!.code,
                expiresAt: membership.challenge!.expiresAt,
            }));
        }
        return this.store.mutate((data) => {
            const challenges: PluginConfirmationChallenge[] = [];
            for (const membership of Object.values(data.memberships)) {
                if (membership.sessionId !== sessionId ||
                    membership.identityLevel !== 'temporary' ||
                    membership.confirmationState !== 'pending' ||
                    membership.leftAt ||
                    membership.blockedAt ||
                    !membership.claimedSteamId) continue;
                if (!onlineSteamIds.has(membership.claimedSteamId)) continue;
                if (!membership.challenge || membership.challenge.expiresAt <= now || membership.challenge.failedAttempts >= 5) {
                    membership.challenge = {
                        challengeId: this.makeId(12),
                        code: this.makeCode(6),
                        expiresAt: now + CHALLENGE_TTL_MS,
                        failedAttempts: 0,
                    };
                    membership.updatedAt = now;
                }
                challenges.push({
                    challengeId: membership.challenge.challengeId,
                    membershipId: membership.membershipId,
                    steamId: membership.claimedSteamId,
                    code: membership.challenge.code,
                    expiresAt: membership.challenge.expiresAt,
                });
            }
            return challenges;
        });
    }

    async confirmChallenge(membershipId: string, rawCode: unknown, trustedSteamIdRaw: unknown) {
        const now = this.now();
        const code = String(rawCode || '').trim().toUpperCase();
        const trustedSteamId = validSteamId(trustedSteamIdRaw);
        return this.store.mutate((data) => {
            const membership = data.memberships[membershipId];
            if (!membership || !membership.challenge) return { ok: false as const, reason: 'challenge_not_found' };
            if (membership.challenge.expiresAt <= now) return { ok: false as const, reason: 'challenge_expired' };
            if (membership.challenge.code !== code) {
                membership.challenge.failedAttempts += 1;
                membership.updatedAt = now;
                if (membership.challenge.failedAttempts >= 5) membership.challenge.expiresAt = now;
                return { ok: false as const, reason: 'challenge_invalid' };
            }
            if (!trustedSteamId || membership.claimedSteamId !== trustedSteamId) {
                membership.confirmationState = 'mismatch';
                membership.confirmationReason = 'steam_mismatch';
                membership.updatedAt = now;
                return { ok: false as const, reason: 'steam_mismatch' };
            }
            return this.promoteDraft(data, membership, trustedSteamId, membership.claimPersonaName || membership.nickname, now);
        });
    }

    async confirmTrustedIdentity(membershipId: string, steamIdRaw: unknown, trustedNameRaw: unknown) {
        const steamId = validSteamId(steamIdRaw);
        if (!steamId) return { ok: false as const, reason: 'steam_id_invalid' };
        const now = this.now();
        return this.store.mutate((data) => {
            const membership = data.memberships[membershipId];
            if (!membership) return { ok: false as const, reason: 'membership_not_found' };
            const currentIdentity = data.identities[membership.identityId];
            if (currentIdentity?.steamId && currentIdentity.steamId !== steamId) {
                membership.confirmationState = 'mismatch';
                membership.confirmationReason = 'steam_mismatch';
                membership.claimedSteamId = steamId;
                membership.trustedSteamId = undefined;
                membership.updatedAt = now;
                membership.challenge = undefined;
                return { ok: false as const, reason: 'steam_mismatch' };
            }
            const existing = Object.values(data.identities).find((identity) => identity.steamId === steamId);
            if (existing && existing.identityId !== membership.identityId) {
                const replacedIdentityId = membership.identityId;
                for (const linkedMembership of Object.values(data.memberships)) {
                    if (linkedMembership.identityId === replacedIdentityId) linkedMembership.identityId = existing.identityId;
                }
                for (const token of Object.values(data.deviceTokens)) {
                    if (token.identityId === replacedIdentityId) token.identityId = existing.identityId;
                }
                delete data.identities[replacedIdentityId];
                membership.identityId = existing.identityId;
                membership.identityLevel = 'longTerm';
                membership.confirmationState = 'confirmed';
                membership.confirmationReason = undefined;
                membership.claimedSteamId = steamId;
                membership.trustedSteamId = steamId;
                membership.confirmedAt = now;
                membership.updatedAt = now;
                membership.challenge = undefined;
                return { ok: true as const, identity: existing, membership };
            }
            return this.promoteDraft(data, membership, steamId, String(trustedNameRaw || '').trim() || membership.nickname, now);
        });
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

    async authenticateDeviceToken(rawToken: unknown, input: DeviceAuthenticationInput) {
        const parsed = this.parseToken(rawToken);
        if (!parsed) return { ok: false as const, reason: 'invalid' };
        const now = this.now();
        return this.store.mutate((data) => {
            const token = data.deviceTokens[parsed.tokenId];
            if (!token || !this.tokenMatches(token, parsed.secret)) return { ok: false as const, reason: 'invalid' };
            if (token.status === 'revoked') return { ok: false as const, reason: 'revoked' };
            if (now >= token.idleExpiresAt || now >= token.absoluteExpiresAt) return { ok: false as const, reason: 'expired' };
            const identity = data.identities[token.identityId];
            if (!identity?.steamId) return { ok: false as const, reason: 'identity_not_confirmed' };

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
            token.idleExpiresAt = Math.min(now + TOKEN_IDLE_MS, token.absoluteExpiresAt);
            const claim = input.steamClaim ? validSteamId(input.steamClaim.steamId) : undefined;
            let membership = Object.values(data.memberships).find((candidate) =>
                candidate.sessionId === input.sessionId && candidate.identityId === identity.identityId && !candidate.leftAt,
            );
            if (!membership) {
                const membershipId = this.makeId(16);
                membership = {
                    membershipId,
                    sessionId: input.sessionId,
                    identityId: identity.identityId,
                    nickname: identity.displayName,
                    identityLevel: 'longTerm',
                    confirmationState: !claim ? 'unavailable' : (claim === identity.steamId ? 'pending' : 'mismatch'),
                    confirmationReason: !claim ? 'steam_not_available' : (claim === identity.steamId ? undefined : 'steam_mismatch'),
                    claimedSteamId: claim,
                    claimPersonaName: String(input.steamClaim?.personaName || '').trim() || undefined,
                    joinedAt: now,
                    updatedAt: now,
                };
                data.memberships[membershipId] = membership;
            } else if (claim && claim !== identity.steamId) {
                membership.claimedSteamId = claim;
                membership.claimPersonaName = String(input.steamClaim?.personaName || '').trim() || undefined;
                membership.confirmationState = 'mismatch';
                membership.confirmationReason = 'steam_mismatch';
                membership.trustedSteamId = undefined;
                membership.confirmedAt = undefined;
                membership.updatedAt = now;
            } else if (membership.confirmationState !== 'confirmed') {
                membership.claimedSteamId = claim;
                membership.claimPersonaName = String(input.steamClaim?.personaName || '').trim() || undefined;
                membership.confirmationState = claim ? 'pending' : 'unavailable';
                membership.confirmationReason = claim ? undefined : 'steam_not_available';
                membership.trustedSteamId = undefined;
                membership.confirmedAt = undefined;
                membership.updatedAt = now;
            }
            return { ok: true as const, identity, membership, needsRotation: now >= token.rotateAfter };
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
            if (now >= previous.idleExpiresAt || now >= previous.absoluteExpiresAt) {
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
                idleExpiresAt: Math.min(now + TOKEN_IDLE_MS, previous.absoluteExpiresAt),
                absoluteExpiresAt: previous.absoluteExpiresAt,
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

    async confirmLongTermPresence(sessionId: string, steamIdsRaw: unknown[]): Promise<LobbyMembershipRecord[]> {
        const now = this.now();
        const trustedSteamIds = new Set(steamIdsRaw.map(validSteamId).filter(Boolean));
        const snapshot = this.store.snapshot();
        const needsConfirmation = Object.values(snapshot.memberships).some((membership) => {
            if (membership.sessionId !== sessionId || membership.identityLevel !== 'longTerm' || membership.blockedAt) return false;
            const identity = snapshot.identities[membership.identityId];
            return !!identity?.steamId &&
                (!membership.claimedSteamId || membership.claimedSteamId === identity.steamId) &&
                trustedSteamIds.has(identity.steamId) &&
                (membership.confirmationState !== 'confirmed' || membership.trustedSteamId !== identity.steamId);
        });
        if (!needsConfirmation) return [];
        return this.store.mutate((data) => {
            const updated: LobbyMembershipRecord[] = [];
            for (const membership of Object.values(data.memberships)) {
                if (membership.sessionId !== sessionId || membership.identityLevel !== 'longTerm' || membership.blockedAt) continue;
                const identity = data.identities[membership.identityId];
                if (!identity?.steamId || !trustedSteamIds.has(identity.steamId)) continue;
                if (membership.claimedSteamId && membership.claimedSteamId !== identity.steamId) continue;
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

    async clearMembershipClaim(membershipId: string): Promise<LobbyMembershipRecord | undefined> {
        const now = this.now();
        return this.store.mutate((data) => {
            const membership = data.memberships[membershipId];
            if (!membership || membership.identityLevel === 'longTerm') return undefined;
            membership.claimedSteamId = undefined;
            membership.claimPersonaName = undefined;
            membership.trustedSteamId = undefined;
            membership.confirmationState = 'unavailable';
            membership.confirmationReason = 'claim_cleared_by_admin';
            membership.challenge = undefined;
            membership.updatedAt = now;
            return membership;
        });
    }

    async carryMembershipToSession(membershipId: string, newSessionId: string): Promise<LobbyMembershipRecord | undefined> {
        const now = this.now();
        return this.store.mutate((data) => {
            const oldMembership = data.memberships[membershipId];
            if (!oldMembership) return undefined;
            const existing = Object.values(data.memberships).find((membership) =>
                membership.sessionId === newSessionId && membership.identityId === oldMembership.identityId && !membership.leftAt,
            );
            if (existing) return existing;
            const identity = data.identities[oldMembership.identityId];
            const newMembershipId = this.makeId(16);
            const claimedSteamId = oldMembership.identityLevel === 'longTerm'
                ? identity?.steamId
                : oldMembership.claimedSteamId;
            const membership: LobbyMembershipRecord = {
                membershipId: newMembershipId,
                sessionId: newSessionId,
                identityId: oldMembership.identityId,
                nickname: oldMembership.nickname,
                identityLevel: oldMembership.identityLevel,
                confirmationState: claimedSteamId ? 'pending' : 'unavailable',
                confirmationReason: claimedSteamId ? undefined : 'steam_not_available',
                claimedSteamId,
                claimPersonaName: oldMembership.claimPersonaName,
                joinedAt: now,
                updatedAt: now,
            };
            data.memberships[newMembershipId] = membership;
            return membership;
        });
    }

    async pruneTemporaryRecords(retentionMs = 30 * DAY_MS): Promise<{ memberships: number; identities: number }> {
        const cutoff = this.now() - retentionMs;
        return this.store.mutate((data) => {
            let memberships = 0;
            let identities = 0;
            const candidateIdentityIds = new Set<string>();
            for (const [membershipId, membership] of Object.entries(data.memberships)) {
                if (membership.identityLevel !== 'temporary' || membership.updatedAt > cutoff) continue;
                candidateIdentityIds.add(membership.identityId);
                delete data.memberships[membershipId];
                memberships += 1;
            }
            for (const identityId of candidateIdentityIds) {
                const identity = data.identities[identityId];
                const stillReferenced = Object.values(data.memberships).some((membership) => membership.identityId === identityId);
                const hasTokens = Object.values(data.deviceTokens).some((token) => token.identityId === identityId);
                if (!identity?.steamId && !stillReferenced && !hasTokens) {
                    delete data.identities[identityId];
                    identities += 1;
                }
            }
            return { memberships, identities };
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

    private promoteDraft(data: ReturnType<IdentityStore['snapshot']>, membership: LobbyMembershipRecord, steamId: string, trustedName: string, now: number) {
        const collision = Object.values(data.identities).find((identity) => identity.steamId === steamId && identity.identityId !== membership.identityId);
        if (collision) return { ok: false as const, reason: 'steam_already_bound' };
        const identity = data.identities[membership.identityId];
        if (!identity) return { ok: false as const, reason: 'identity_not_found' };
        if (identity.steamId && identity.steamId !== steamId) {
            membership.confirmationState = 'mismatch';
            membership.confirmationReason = 'steam_mismatch';
            membership.claimedSteamId = steamId;
            membership.trustedSteamId = undefined;
            membership.updatedAt = now;
            membership.challenge = undefined;
            return { ok: false as const, reason: 'steam_mismatch' };
        }
        identity.steamId = steamId;
        identity.updatedAt = now;
        if (!identity.displayName) identity.displayName = trustedName;
        membership.identityLevel = 'longTerm';
        membership.confirmationState = 'confirmed';
        membership.confirmationReason = undefined;
        membership.claimedSteamId = steamId;
        membership.trustedSteamId = steamId;
        membership.confirmedAt = now;
        membership.updatedAt = now;
        membership.challenge = undefined;
        return { ok: true as const, identity, membership };
    }

    private assertNicknameAvailable(
        data: ReturnType<IdentityStore['snapshot']>,
        sessionId: string,
        nickname: string,
        identityId: string,
    ): void {
        const collision = Object.values(data.memberships).some((membership) =>
            membership.sessionId === sessionId &&
            membership.identityId !== identityId &&
            !membership.leftAt &&
            membership.nickname === nickname,
        );
        if (collision) throw new Error('nickname_in_use');
    }

    private makeId(size: number): string {
        return this.randomBytes(size).toString('hex');
    }

    private makeCode(length: number): string {
        const bytes = this.randomBytes(length);
        let code = '';
        for (let index = 0; index < length; index++) code += CODE_ALPHABET[bytes[index] % CODE_ALPHABET.length];
        return code;
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
