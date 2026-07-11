export type IdentityLevel = 'temporary' | 'longTerm';
export type ConfirmationState = 'pending' | 'confirmed' | 'unavailable' | 'mismatch';

export interface SteamAccountClaim {
    steamId: string;
    personaName?: string;
}

export interface IdentityRecord {
    identityId: string;
    displayName: string;
    steamId?: string;
    createdAt: number;
    updatedAt: number;
}

export interface ConfirmationChallengeRecord {
    challengeId: string;
    code: string;
    expiresAt: number;
    failedAttempts: number;
}

export interface LobbyMembershipRecord {
    membershipId: string;
    sessionId: string;
    identityId: string;
    nickname: string;
    identityLevel: IdentityLevel;
    confirmationState: ConfirmationState;
    confirmationReason?: string;
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
    schemaVersion: 1;
    identities: Record<string, IdentityRecord>;
    memberships: Record<string, LobbyMembershipRecord>;
    deviceTokens: Record<string, DeviceTokenRecord>;
}

export interface PluginConfirmationChallenge {
    challengeId: string;
    membershipId: string;
    steamId: string;
    code: string;
    expiresAt: number;
}
