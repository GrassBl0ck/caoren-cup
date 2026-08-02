import { PasswordCredential } from './password-auth';

export type IdentityLevel = 'temporary' | 'longTerm';
export type ConfirmationState = 'pending' | 'confirmed' | 'unavailable' | 'mismatch';

export interface IdentityRecord {
    identityId: string;
    displayName: string;
    steamId?: string;
    steamNickname?: string;
    createdAt: number;
    updatedAt: number;
}

export type LoginAccountPasswordState = 'active' | 'recovery_required';

export interface LoginAccountRecord {
    identityId: string;
    loginName: string;
    enabled: boolean;
    passwordState: LoginAccountPasswordState;
    password?: PasswordCredential;
    createdAt: number;
    updatedAt: number;
}

export interface ConfirmationChallengeRecord {
    challengeId: string;
    code: string;
    expiresAt: number;
    failedAttempts: number;
    trustedSteamNickname?: string;
}

export interface LobbyMembershipRecord {
    membershipId: string;
    sessionId: string;
    identityId: string;
    nickname: string;
    identityLevel: IdentityLevel;
    confirmationState: ConfirmationState;
    confirmationReason?: string;
    // Legacy invitation records may still contain claim/challenge fields. They remain
    // readable so existing identity stores can load without a production migration;
    // Step7 has no writer or authentication path that creates or consumes them.
    claimedSteamId?: string;
    claimPersonaName?: string;
    trustedSteamId?: string;
    confirmedAt?: number;
    joinedAt: number;
    updatedAt: number;
    leftAt?: number;
    blockedAt?: number;
    challenge?: ConfirmationChallengeRecord;
}

export type DeviceTokenStatus = 'active' | 'pending_rotation' | 'revoked';

export interface DeviceTokenRecord {
    tokenId: string;
    identityId: string;
    deviceId: string;
    tokenHash: string;
    familyId: string;
    status: DeviceTokenStatus;
    createdAt: number;
    lastUsedAt: number;
    idleExpiresAt: number;
    absoluteExpiresAt: number;
    rotateAfter: number;
    revokedAt?: number;
    rotatedFromTokenId?: string;
}

export interface IdentityStoreData {
    schemaVersion: 3;
    identities: Record<string, IdentityRecord>;
    accounts: Record<string, LoginAccountRecord>;
    memberships: Record<string, LobbyMembershipRecord>;
    deviceTokens: Record<string, DeviceTokenRecord>;
}

export interface LegacyIdentityRecord {
    identityId: string;
    displayName: string;
    steamId?: string;
    fixedAccount?: {
        enabled: boolean;
        password: PasswordCredential;
    };
    createdAt: number;
    updatedAt: number;
}

export interface IdentityStoreDataLegacy {
    schemaVersion: 1 | 2;
    identities: Record<string, LegacyIdentityRecord>;
    memberships: Record<string, LobbyMembershipRecord>;
    deviceTokens: Record<string, DeviceTokenRecord>;
}
