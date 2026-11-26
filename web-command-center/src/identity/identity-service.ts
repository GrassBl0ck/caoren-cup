import crypto from 'node:crypto';
import { generateRandomInitialPassword, generateRandomLoginName, validateLoginName } from './account-foundation';
import { IdentityStore } from './identity-store';
import { hashAccountPassword, verifyAccountPassword } from './password-auth';
import { normalizeEmail } from './email-address';
import {
    DeviceTokenRecord,
    IdentityRecord,
    LoginAccountRecord,
    LobbyMembershipRecord,
    isDesktopAvatar,
    isVerifiedSteamIdentity,
    ownsSteamIdentity,
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
    loginName?: unknown;
    email?: unknown;
    password: unknown;
}

interface AccountRecoveryStartInput {
    steamId: unknown;
    steamNickname: unknown;
    existingOnly?: boolean;
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

    async joinPlayerCenterMatch(identityId: string, sessionId: string, allowPendingBinding = false, selectedSteamId?: string) {
        const now = this.now();
        return this.store.mutate((data) => {
            const identity = data.identities[identityId];
            const account = data.accounts[identityId];
            if (!identity || !account || !account.enabled ||
                (account.passwordState === 'active' && !account.password)) {
                return { ok: false as const, reason: 'account_unavailable' as const };
            }
            if (selectedSteamId !== undefined && !isVerifiedSteamIdentity(identity, selectedSteamId)) {
                return { ok: false as const, reason: 'game_binding_required' as const };
            }
            const boundSteamId = selectedSteamId ?? (identity.gameBoundAt ? identity.steamId : undefined);
            if (!boundSteamId && !allowPendingBinding) {
                return { ok: false as const, reason: 'game_binding_required' as const };
            }
            const memberships = Object.values(data.memberships).filter((membership) =>
                membership.sessionId === sessionId && membership.identityId === identityId,
            );
            if (memberships.some((membership) => !!membership.blockedAt)) {
                return { ok: false as const, reason: 'blocked_for_session' as const };
            }
            const active = memberships.find((membership) => !membership.leftAt);
            if (active) {
                if (selectedSteamId !== undefined && selectedSteamId !== active.trustedSteamId) {
                    return { ok: false as const, reason: 'match_steam_locked' as const };
                }
                return { ok: true as const, membership: active, created: false };
            }

            const nickname = selectedSteamId === undefined ? identity.steamNickname || identity.displayName : identity.displayName;
            const nicknameInUse = Object.values(data.memberships).some((membership) =>
                membership.sessionId === sessionId && membership.identityId !== identityId &&
                !membership.leftAt && !membership.blockedAt && membership.nickname === nickname,
            );
            if (nicknameInUse) return { ok: false as const, reason: 'nickname_in_use' as const };

            const membershipId = this.makeId(16);
            const membership: LobbyMembershipRecord = {
                membershipId,
                sessionId,
                identityId,
                nickname,
                identityLevel: 'longTerm',
                confirmationState: boundSteamId ? 'confirmed' : 'unavailable',
                confirmationReason: boundSteamId ? undefined : 'steam_not_available',
                trustedSteamId: boundSteamId,
                confirmedAt: boundSteamId ? now : undefined,
                joinedAt: now,
                updatedAt: now,
            };
            data.memberships[membershipId] = membership;
            return { ok: true as const, membership, created: true };
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
        return this.store.record('accounts', identityId);
    }

    getIdentity(identityId: string) {
        return this.store.record('identities', identityId);
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

    findAccountByEmail(raw: unknown): LoginAccountRecord | undefined {
        let email: string;
        try { email = normalizeEmail(raw); } catch { return undefined; }
        return Object.values(this.store.snapshot().accounts).find((account) => account.email === email && !!account.emailVerifiedAt);
    }

    // Only call after the server has consumed an email ownership challenge.
    async registerVerifiedEmailAccount(input: { email: unknown; loginName?: unknown; password: unknown; age14?: boolean }) {
        const email = normalizeEmail(input.email);
        const password = await hashAccountPassword(input.password, { now: this.now, randomBytes: this.randomBytes });
        return this.store.mutate((data) => {
            const loginName = generateRandomLoginName(new Set(Object.values(data.accounts).map(account => account.loginName)), this.randomBytes);
            if (Object.values(data.accounts).some((account) => account.email === email)) throw new Error('registration_unavailable');
            if (Object.values(data.accounts).some((account) => account.loginName.toLowerCase() === loginName.toLowerCase())) {
                throw new Error('login_name_taken');
            }
            const now = this.now();
            const identityId = this.makeId(16);
            const identity: IdentityRecord = { identityId, displayName: '玩家', createdAt: now, updatedAt: now };
            const account: LoginAccountRecord = {
                identityId, loginName, enabled: true, passwordState: 'active', password,
                email, emailVerifiedAt: now, age14ConfirmedAt: input.age14 === true ? now : undefined, createdAt: now, updatedAt: now,
            };
            data.identities[identityId] = identity;
            data.accounts[identityId] = account;
            return { identity, account };
        });
    }

    async bindVerifiedEmail(input: { identityId: string; email: unknown; currentPassword: unknown }) {
        const email = normalizeEmail(input.email);
        const original = this.getLoginAccount(input.identityId);
        if (!original?.enabled || !original.password || !await verifyAccountPassword(input.currentPassword, original.password)) {
            throw new Error('current_password_incorrect');
        }
        return this.store.mutate((data) => {
            const account = data.accounts[input.identityId];
            if (!account?.enabled || account.password?.hash !== original.password?.hash) throw new Error('account_changed');
            if (account.email) throw new Error('email_already_bound');
            if (Object.values(data.accounts).some((candidate) => candidate.email === email)) throw new Error('email_unavailable');
            account.email = email;
            account.emailVerifiedAt = this.now();
            account.updatedAt = Math.max(this.now(), account.updatedAt + 1);
            return account;
        });
    }

    async resetPasswordByVerifiedEmail(emailRaw: unknown, newPassword: unknown) {
        const email = normalizeEmail(emailRaw);
        const password = await hashAccountPassword(newPassword, { now: this.now, randomBytes: this.randomBytes });
        return this.store.mutate((data) => {
            const account = Object.values(data.accounts).find((candidate) => candidate.email === email && !!candidate.emailVerifiedAt);
            if (!account?.enabled) throw new Error('recovery_unavailable');
            account.password = password;
            account.passwordState = 'active';
            account.updatedAt = Math.max(this.now(), account.updatedAt + 1);
            for (const token of Object.values(data.deviceTokens)) {
                if (token.identityId === account.identityId) { token.status = 'revoked'; token.revokedAt = this.now(); }
            }
            return account;
        });
    }

    // Steam identity comes from a consumed CS2 bridge ticket, never from client form fields.
    async bindVerifiedGameIdentity(identityId: string, steamIdRaw: unknown, nicknameRaw: unknown, sessionId?: string) {
        const steamId = strictSteamId(steamIdRaw);
        if (!steamId) throw new Error('steam_id_invalid');
        return this.store.mutate((data) => {
            const identity = data.identities[identityId];
            if (!identity || !data.accounts[identityId]?.enabled) throw new Error('account_unavailable');
            const owner = Object.values(data.identities).find((candidate) => ownsSteamIdentity(candidate, steamId));
            if (owner && owner.identityId !== identityId) throw new Error('steam_identity_in_use');
            if (identity.steamId && identity.steamId !== steamId) throw new Error('steam_already_bound');
            identity.steamId = steamId;
            identity.steamNickname = String(nicknameRaw || '').trim().slice(0, 128) || identity.displayName;
            if (!identity.customDisplayName) identity.displayName = identity.steamNickname;
            const now = this.now();
            identity.gameBoundAt = now;
            identity.updatedAt = now;
            for (const membership of Object.values(data.memberships)) {
                if (!sessionId || membership.sessionId !== sessionId || membership.identityId !== identityId
                    || membership.identityLevel !== 'longTerm' || membership.leftAt || membership.blockedAt
                    || (membership.trustedSteamId && membership.trustedSteamId !== steamId)) continue;
                membership.confirmationState = 'confirmed';
                membership.confirmationReason = undefined;
                membership.trustedSteamId = steamId;
                membership.confirmedAt = now;
                membership.updatedAt = now;
            }
            return identity;
        });
    }

    // 仅由校验成功的 Steam OpenID 回调调用。保留旧的游戏绑定及本场已经选定的身份。
    async linkVerifiedSteamIdentity(identityId: string, verifiedSteamId: string) {
        const steamId = strictSteamId(verifiedSteamId);
        if (!steamId) throw new Error('steam_id_invalid');
        return this.store.mutate((data) => {
            const identity = data.identities[identityId];
            if (!identity || !data.accounts[identityId]?.enabled) throw new Error('account_unavailable');
            const owner = Object.values(data.identities).find((candidate) => ownsSteamIdentity(candidate, steamId));
            if (owner && owner.identityId !== identityId) throw new Error('steam_identity_in_use');
            const now = this.now();
            if (!identity.steamId || identity.steamId === steamId) {
                identity.steamId = steamId;
                identity.gameBoundAt ??= now;
            } else if (!ownsSteamIdentity(identity, steamId)) {
                (identity.additionalSteamIds ??= []).push(steamId);
            }
            identity.updatedAt = now;
            return identity;
        });
    }

    async updateDesktopProfile(identityId: string, nameRaw: unknown, avatarRaw?: unknown, mode?: unknown, lobbySessionId?: string) {
        const name = typeof nameRaw === 'string' ? nameRaw.trim().normalize('NFC') : '';
        if (!name || [...name].length > 32 || Buffer.byteLength(name, 'utf8') > 127 || /[\p{Cc}\p{Cf}]/u.test(name)) {
            throw new Error('display_name_invalid');
        }
        const avatar = isDesktopAvatar(avatarRaw) ? avatarRaw : undefined;
        if (avatarRaw !== undefined && avatarRaw !== null && !avatar) throw new Error('avatar_invalid');
        if (mode !== undefined && mode !== 'steam' && mode !== 'logo' && mode !== 'custom') throw new Error('avatar_invalid');
        if ((mode === 'custom' && !avatar) || (avatar && (mode === 'logo' || mode === 'steam'))) throw new Error('avatar_invalid');
        return this.store.mutate(data => {
            const identity = data.identities[identityId];
            if (!identity || !data.accounts[identityId]?.enabled) throw new Error('account_unavailable');
            if (lobbySessionId) {
                const active = Object.values(data.memberships).filter(m => m.sessionId === lobbySessionId && !m.leftAt && !m.blockedAt);
                const member = active.find(m => m.identityId === identityId);
                if (member && active.some(m => m.identityId !== identityId && m.nickname === name)) throw new Error('nickname_in_use');
                if (member) { member.nickname = name; member.updatedAt = this.now(); }
            }
            identity.displayName = name;
            identity.customDisplayName = true;
            if (avatarRaw !== undefined || mode !== undefined) {
                if (avatar) identity.avatarJpeg = avatar;
                else delete identity.avatarJpeg;
                if (mode === 'steam') identity.steamAvatarEnabled = true;
                else {
                    delete identity.steamAvatarEnabled;
                    delete identity.steamAvatarJpegs;
                }
                if (mode === 'logo') identity.avatarLogo = true;
                else delete identity.avatarLogo;
            }
            identity.updatedAt = this.now();
            return identity;
        });
    }

    async rememberSteamAvatar(identityId: string, steamId: string, jpeg: unknown) {
        const image = isDesktopAvatar(jpeg) ? jpeg : undefined;
        if (!image && jpeg !== null) throw new Error('avatar_invalid');
        const current = this.getIdentity(identityId);
        if (!current || !isVerifiedSteamIdentity(current, steamId)) throw new Error('game_binding_required');
        if (!current.steamAvatarEnabled) throw new Error('steam_avatar_disabled');
        if (current.steamAvatarJpegs?.[steamId] === image) return current;
        return this.store.mutate(data => {
            const identity = data.identities[identityId];
            if (!identity || !data.accounts[identityId]?.enabled || !isVerifiedSteamIdentity(identity, steamId))
                throw new Error('game_binding_required');
            if (!identity.steamAvatarEnabled) throw new Error('steam_avatar_disabled');
            if (!image) {
                if (identity.steamAvatarJpegs) delete identity.steamAvatarJpegs[steamId];
            } else (identity.steamAvatarJpegs ??= {})[steamId] = image;
            return identity;
        });
    }

    desktopRoomProfiles(identityIds: readonly string[]) {
        const identities = this.store.snapshot().identities;
        return Object.fromEntries(identityIds.flatMap(id => identities[id] ? [[id, identities[id]]] : [])) as Record<string, IdentityRecord>;
    }

    avatarIdentities() {
        return Object.values(this.store.snapshot().identities);
    }

    gameDisplayNames(steamIds: unknown): Record<string, string> | undefined {
        if (!Array.isArray(steamIds)) return undefined;
        const requested = new Set(steamIds.slice(0, 128).map(strictSteamId).filter((id): id is string => !!id));
        const data = this.store.snapshot();
        const entries: [string, string][] = [];
        for (const identity of Object.values(data.identities)) {
            if (!identity.customDisplayName || !data.accounts[identity.identityId]?.enabled) continue;
            for (const steamId of requested) {
                if (isVerifiedSteamIdentity(identity, steamId)) entries.push([steamId, identity.displayName]);
            }
        }
        return Object.fromEntries(entries);
    }

    async findOrCreateSteamAccount(steamIdRaw: unknown) {
        const steamId = strictSteamId(steamIdRaw);
        if (!steamId) throw new Error('steam_id_invalid');
        const now = this.now();
        return this.store.mutate((data) => {
            const matches = Object.values(data.identities).filter((identity) => ownsSteamIdentity(identity, steamId));
            if (matches.length > 1) throw new Error('steam_identity_conflict');
            let identity = matches[0];
            if (!identity) {
                const identityId = this.makeId(16);
                identity = {
                    identityId,
                    displayName: `Steam ${steamId.slice(-6)}`,
                    steamId,
                    createdAt: now,
                    updatedAt: now,
                };
                data.identities[identityId] = identity;
            }
            let account = data.accounts[identity.identityId];
            if (account && !account.enabled) throw new Error('account_disabled');
            if (!account) {
                account = {
                    identityId: identity.identityId,
                    loginName: generateRandomLoginName(
                        new Set(Object.values(data.accounts).map((candidate) => candidate.loginName)),
                        this.randomBytes,
                    ),
                    enabled: true,
                    passwordState: 'steam_only',
                    createdAt: now,
                    updatedAt: now,
                };
                data.accounts[identity.identityId] = account;
            }
            return { identity, account };
        });
    }

    async setSteamBackupPassword(identityId: string, newPassword: unknown) {
        const password = await hashAccountPassword(newPassword, { now: this.now, randomBytes: this.randomBytes });
        const now = this.now();
        return this.store.mutate((data) => {
            const account = data.accounts[identityId];
            if (!account?.enabled || account.passwordState !== 'steam_only') throw new Error('backup_password_unavailable');
            account.passwordState = 'active';
            account.password = password;
            account.updatedAt = now;
            return account;
        });
    }

    async authenticateLoginAccount(input: LoginAccountAuthenticationInput) {
        const account = input.email !== undefined ? this.findAccountByEmail(input.email) : this.findAccountByLoginName(input.loginName);
        if (!account || account.passwordState !== 'active' || !account.password) {
            return { ok: false as const, reason: 'invalid_credentials' as const };
        }
        if (!await verifyAccountPassword(input.password, account.password)) {
            return { ok: false as const, reason: 'invalid_credentials' as const };
        }
        const identity = this.store.snapshot().identities[account.identityId];
        if (!identity) return { ok: false as const, reason: 'invalid_credentials' as const };
        if (!account.enabled) return { ok: false as const, reason: 'account_disabled' as const };
        if (!identity.gameBoundAt && !account.emailVerifiedAt) return { ok: false as const, reason: 'game_entry_required' as const };
        return { ok: true as const, identity, account };
    }

    async registerAccountFromGame(input: {
        steamId: unknown;
        steamNickname: unknown;
        loginName: unknown;
        password: unknown;
    }) {
        const steamId = strictSteamId(input.steamId);
        if (!steamId) throw new Error('steam_id_invalid');
        const steamNickname = String(input.steamNickname || '').trim().slice(0, 128) || `Steam ${steamId.slice(-6)}`;
        const loginName = validateLoginName(input.loginName);
        const password = await hashAccountPassword(input.password, { now: this.now, randomBytes: this.randomBytes });
        const now = this.now();
        return this.store.mutate((data) => {
            const existingIdentity = Object.values(data.identities).find(candidate => ownsSteamIdentity(candidate, steamId));
            if (existingIdentity && data.accounts[existingIdentity.identityId]) throw new Error('account_exists');
            const duplicateLogin = Object.values(data.accounts).some(account => account.loginName.toLowerCase() === loginName.toLowerCase());
            if (duplicateLogin) throw new Error('login_name_taken');
            const identityId = existingIdentity?.identityId || this.makeId(16);
            const identity = existingIdentity || {
                identityId,
                displayName: steamNickname,
                steamId,
                steamNickname,
                gameBoundAt: now,
                createdAt: now,
                updatedAt: now,
            };
            identity.displayName = steamNickname;
            identity.steamId = steamId;
            identity.steamNickname = steamNickname;
            identity.gameBoundAt = now;
            identity.updatedAt = now;
            data.identities[identityId] = identity;
            data.accounts[identityId] = {
                identityId,
                loginName,
                enabled: true,
                passwordState: 'active',
                password,
                createdAt: now,
                updatedAt: now,
            };
            return { identity, account: data.accounts[identityId] };
        });
    }

    async openOrBeginAccountRecovery(input: AccountRecoveryStartInput) {
        const steamId = strictSteamId(input.steamId);
        if (!steamId) throw new Error('steam_id_invalid');
        const steamNickname = String(input.steamNickname || '').trim().slice(0, 128) || `Steam ${steamId.slice(-6)}`;
        const snapshot = this.store.snapshot();
        const snapshotIdentity = Object.values(snapshot.identities).find((candidate) => ownsSteamIdentity(candidate, steamId));
        if (snapshotIdentity && snapshotIdentity.steamId !== steamId) throw new Error('use_email_recovery');
        if (input.existingOnly && (!snapshotIdentity || !snapshot.accounts[snapshotIdentity.identityId])) {
            throw new Error('account_registration_on_web');
        }
        if (snapshotIdentity && snapshot.accounts[snapshotIdentity.identityId]) {
            const now = this.now();
            return this.store.mutate((data) => {
                const identity = Object.values(data.identities).find((candidate) => candidate.steamId === steamId);
                const account = identity ? data.accounts[identity.identityId] : undefined;
                if (!identity || !account) throw new Error('account_state_changed');
                if (input.existingOnly && account.emailVerifiedAt) throw new Error('use_email_recovery');
                identity.steamNickname = steamNickname;
                identity.gameBoundAt = now;
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
            let identity = Object.values(data.identities).find((candidate) => ownsSteamIdentity(candidate, steamId));
            if (identity && identity.steamId !== steamId) throw new Error('use_email_recovery');
            if (identity && data.accounts[identity.identityId]) {
                identity.steamNickname = steamNickname;
                identity.gameBoundAt = now;
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
                    gameBoundAt: now,
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
            if (account.emailVerifiedAt) throw new Error('use_email_recovery');
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

    checkCsBindings(steamIds: unknown): Record<string, boolean> | undefined {
        if (!Array.isArray(steamIds)) return undefined;
        const requested = new Set(steamIds.slice(0, 128).map(strictSteamId).filter((id): id is string => !!id));
        if (requested.size === 0) return {};
        const snapshot = this.store.snapshot();
        const bound = new Set<string>();
        for (const identity of Object.values(snapshot.identities)) {
            if (!snapshot.accounts[identity.identityId]?.enabled) continue;
            for (const steamId of requested) {
                if (isVerifiedSteamIdentity(identity, steamId)) bound.add(steamId);
            }
        }
        return Object.fromEntries([...requested].map(id => [id, bound.has(id)]));
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
            if (!identity || (!identity.steamId && !data.accounts[identityId]?.emailVerifiedAt)) throw new Error('identity_not_confirmed');
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
            if (!identity || (!identity.steamId && !data.accounts[token.identityId]?.emailVerifiedAt)) return { ok: false as const, reason: 'identity_not_confirmed' };
            const account = data.accounts[token.identityId];
            if (!account) return { ok: false as const, reason: 'account_unavailable' };
            if (!account.enabled) return { ok: false as const, reason: 'account_disabled' };
            if (account.passwordState === 'active' && !account.password) {
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

    async confirmMinimumAge(identityId: string) {
        return this.store.mutate(data => {
            const account = data.accounts[identityId];
            if (!account?.enabled) throw new Error('account_unavailable');
            account.age14ConfirmedAt ??= this.now();
        });
    }

    async eraseAccount(identityId: string, password: unknown) {
        const account = this.getLoginAccount(identityId);
        if (!account?.enabled || !account.password || !await verifyAccountPassword(password, account.password))
            throw new Error('current_password_incorrect');
        const identity = this.getIdentity(identityId);
        await this.store.eraseAccount(identityId, account.password.hash);
        return identity;
    }

    rememberedDevices(identityId: string, currentTokenId?: string) {
        const tokens = Object.values(this.store.snapshot().deviceTokens).filter(t => t.identityId === identityId);
        const currentDevice = tokens.find(t => t.tokenId === currentTokenId)?.deviceId;
        const seen = new Set<string>();
        return tokens.sort((a, b) => b.lastUsedAt - a.lastUsedAt).filter(t => {
            if (t.status === 'revoked' || Math.min(t.idleExpiresAt, t.absoluteExpiresAt) <= this.now() || seen.has(t.deviceId)) return false;
            seen.add(t.deviceId);
            return true;
        }).map(t => ({ id: t.familyId, lastUsedAt: t.lastUsedAt, current: t.deviceId === currentDevice }));
    }

    deviceWasForgotten(tokenId?: string) {
        return !!tokenId && !!this.store.record('deviceTokens', tokenId)?.deviceForgottenAt;
    }

    async forgetDevice(identityId: string, familyId: string) {
        return this.store.mutate(data => {
            const tokens = Object.values(data.deviceTokens).filter(t => t.identityId === identityId);
            const device = tokens.find(t => t.familyId === familyId);
            if (!device) return [];
            return tokens.filter(t => t.deviceId === device.deviceId).map(t => {
                t.status = 'revoked';
                t.revokedAt = t.deviceForgottenAt = this.now();
                return t.tokenId;
            });
        });
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
        const presenceFor = (membership: LobbyMembershipRecord, identity?: IdentityRecord) => {
            if (!identity || membership.sessionId !== sessionId || membership.identityLevel !== 'longTerm' ||
                membership.blockedAt || membership.leftAt) return;
            const steamId = membership.trustedSteamId || identity.steamId;
            if (!steamId || !ownsSteamIdentity(identity, steamId) || !trustedPlayers.has(steamId) ||
                (membership.claimedSteamId && membership.claimedSteamId !== steamId)) return;
            const nickname = steamId === identity.steamId ? trustedPlayers.get(steamId) : undefined;
            if (membership.confirmationState !== 'confirmed' || membership.trustedSteamId !== steamId ||
                (nickname && identity.steamNickname !== nickname)) return { identity, steamId, nickname };
        };
        const needsUpdate = Object.values(snapshot.memberships).some((membership) =>
            presenceFor(membership, snapshot.identities[membership.identityId]) !== undefined);
        if (!needsUpdate) return [];
        return this.store.mutate((data) => {
            const updated: LobbyMembershipRecord[] = [];
            for (const membership of Object.values(data.memberships)) {
                const presence = presenceFor(membership, data.identities[membership.identityId]);
                if (!presence) continue;
                const { identity, steamId, nickname } = presence;
                if (nickname) {
                    identity.steamNickname = nickname;
                    identity.updatedAt = now;
                }
                if (membership.confirmationState === 'confirmed' && membership.trustedSteamId === steamId) continue;
                membership.confirmationState = 'confirmed';
                membership.confirmationReason = undefined;
                membership.trustedSteamId = steamId;
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
        return Object.values(this.store.snapshot().identities).find((identity) => ownsSteamIdentity(identity, steamId));
    }

    async visitDesktopRoom(identityId: string, readGuide: boolean) {
        const current = this.getIdentity(identityId)?.desktopRoomGuide;
        if (current === 'done' || (current === 'pending' && !readGuide)) return current;
        return this.store.mutate(data => {
            const identity = data.identities[identityId];
            if (!identity) throw new Error('account_unavailable');
            if (readGuide) identity.desktopRoomGuide = 'done';
            else if (!identity.desktopRoomGuide) {
                const played = Object.values(data.memberships).some(seat => seat.identityId === identityId);
                identity.desktopRoomGuide = played ? 'done' : 'pending';
            }
            return identity.desktopRoomGuide;
        });
    }

    getMembership(membershipId: string): LobbyMembershipRecord | undefined {
        return this.store.record('memberships', membershipId);
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
