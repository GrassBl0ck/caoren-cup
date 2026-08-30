import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { generateRandomLoginName, validateLoginName } from './account-foundation';
import { IdentityStoreData, IdentityStoreDataLegacy, LoginAccountRecord } from './identity-types';

const createEmptyData = (): IdentityStoreData => ({
    schemaVersion: 3,
    identities: {},
    accounts: {},
    memberships: {},
    deviceTokens: {},
});

const cloneData = (data: IdentityStoreData): IdentityStoreData => JSON.parse(JSON.stringify(data));

const omitLegacyMembershipAuthFields = (data: IdentityStoreData): IdentityStoreData => {
    for (const membership of Object.values(data.memberships)) {
        delete membership.claimedSteamId;
        delete membership.claimPersonaName;
        delete membership.challenge;
    }
    return data;
};

const isRecord = (value: unknown): value is Record<string, unknown> =>
    !!value && typeof value === 'object' && !Array.isArray(value);

const isPositiveInteger = (value: unknown, maximum: number): value is number =>
    Number.isInteger(value) && Number(value) > 0 && Number(value) <= maximum;

class IdentityMigrationError extends Error {}

const validatePasswordCredential = (password: Record<string, unknown>): void => {
    const params = password.params;
    if (password.algorithm !== 'scrypt' ||
        typeof password.salt !== 'string' || !/^[A-Za-z0-9_-]+$/.test(password.salt) ||
        typeof password.hash !== 'string' || !/^[A-Za-z0-9_-]+$/.test(password.hash) ||
        typeof password.updatedAt !== 'number' || !Number.isFinite(password.updatedAt) ||
        !isRecord(params) ||
        !isPositiveInteger(params.N, 1_048_576) ||
        !isPositiveInteger(params.r, 32) ||
        !isPositiveInteger(params.p, 16) ||
        !isPositiveInteger(params.keyLength, 128) ||
        !isPositiveInteger(params.maxmem, 256 * 1024 * 1024)) {
        throw new Error('account password credential is invalid');
    }
    const salt = Buffer.from(password.salt, 'base64url');
    const hash = Buffer.from(password.hash, 'base64url');
    if (salt.length !== 32 || hash.length !== params.keyLength) throw new Error('account password credential is invalid');
};

const validateLegacyIdentities = (identities: Record<string, unknown>): void => {
    const steamIdentityIds = new Map<string, string>();
    for (const identityValue of Object.values(identities)) {
        if (!isRecord(identityValue)) throw new Error('identity store schema is invalid');
        if (typeof identityValue.identityId !== 'string') throw new Error('identity store schema is invalid');
        if (typeof identityValue.steamId === 'string' && identityValue.steamId) {
            const existing = steamIdentityIds.get(identityValue.steamId);
            if (existing && existing !== identityValue.identityId) throw new Error('identity SteamID is duplicated');
            steamIdentityIds.set(identityValue.steamId, identityValue.identityId);
        }
        if (identityValue.fixedAccount === undefined) continue;
        const fixedAccount = identityValue.fixedAccount;
        if (!isRecord(fixedAccount) || typeof fixedAccount.enabled !== 'boolean' || !isRecord(fixedAccount.password)) {
            throw new Error('fixed account credential is invalid');
        }
        if (typeof identityValue.steamId !== 'string' || !/^7656119\d{10}$/.test(identityValue.steamId)) {
            throw new Error('fixed account credential is invalid');
        }
        try {
            validatePasswordCredential(fixedAccount.password);
        } catch {
            throw new Error('fixed account credential is invalid');
        }
    }
};

const validateCurrentData = (parsed: Record<string, unknown>): IdentityStoreData => {
    if (!isRecord(parsed.accounts)) throw new Error('identity store schema is invalid');
    const identities = parsed.identities as Record<string, unknown>;
    const steamIdentityIds = new Map<string, string>();
    for (const [identityKey, identityValue] of Object.entries(identities)) {
        if (!isRecord(identityValue) || identityValue.identityId !== identityKey || typeof identityValue.displayName !== 'string' ||
            typeof identityValue.createdAt !== 'number' || typeof identityValue.updatedAt !== 'number' ||
            identityValue.fixedAccount !== undefined) {
            throw new Error('identity store schema is invalid');
        }
        if (identityValue.steamId !== undefined) {
            if (typeof identityValue.steamId !== 'string' || !/^7656119\d{10}$/.test(identityValue.steamId)) {
                throw new Error('identity SteamID is invalid');
            }
            const existing = steamIdentityIds.get(identityValue.steamId);
            if (existing) throw new Error('identity SteamID is duplicated');
            steamIdentityIds.set(identityValue.steamId, identityKey);
        }
        if (identityValue.steamNickname !== undefined && typeof identityValue.steamNickname !== 'string') {
            throw new Error('identity Steam nickname is invalid');
        }
    }

    const loginNames = new Set<string>();
    for (const [identityId, accountValue] of Object.entries(parsed.accounts)) {
        const identity = identities[identityId];
        if (!isRecord(accountValue) || accountValue.identityId !== identityId || !isRecord(identity) ||
            typeof accountValue.enabled !== 'boolean' || typeof accountValue.createdAt !== 'number' ||
            typeof accountValue.updatedAt !== 'number') {
            throw new Error('login account schema is invalid');
        }
        if (typeof identity.steamId !== 'string' || !/^7656119\d{10}$/.test(identity.steamId)) {
            throw new Error('login account identity has no trusted SteamID');
        }
        const loginName = validateLoginName(accountValue.loginName);
        if (loginNames.has(loginName)) throw new Error('login account name is duplicated');
        loginNames.add(loginName);
        if (accountValue.passwordState === 'recovery_required') {
            if (accountValue.password !== undefined) throw new Error('recovery-required account must not have a password');
        } else if (accountValue.passwordState === 'active' && isRecord(accountValue.password)) {
            validatePasswordCredential(accountValue.password);
        } else {
            throw new Error('login account password state is invalid');
        }
    }
    return parsed as unknown as IdentityStoreData;
};

const migrateLegacyData = (legacy: IdentityStoreDataLegacy, randomBytes: (size: number) => Buffer): IdentityStoreData => {
    const identities: IdentityStoreData['identities'] = {};
    const accounts: Record<string, LoginAccountRecord> = {};
    const loginNames = new Set<string>();
    for (const [identityId, legacyIdentity] of Object.entries(legacy.identities)) {
        const { fixedAccount, ...identity } = legacyIdentity;
        const steamId = typeof identity.steamId === 'string' && /^7656119\d{10}$/.test(identity.steamId)
            ? identity.steamId
            : undefined;
        identities[identityId] = {
            ...identity,
            steamId,
        };
        if (!steamId) continue;
        const loginName = generateRandomLoginName(loginNames, randomBytes);
        loginNames.add(loginName);
        accounts[identityId] = {
            identityId,
            loginName,
            enabled: fixedAccount?.enabled ?? true,
            passwordState: 'recovery_required',
            createdAt: identity.createdAt,
            updatedAt: identity.updatedAt,
        };
    }
    return {
        schemaVersion: 3,
        identities,
        accounts,
        memberships: legacy.memberships,
        deviceTokens: {},
    };
};

const parseData = (text: string, randomBytes: (size: number) => Buffer): { data: IdentityStoreData; migrated: boolean } => {
    const parsed: unknown = JSON.parse(text);
    if (!isRecord(parsed) || !isRecord(parsed.identities) || !isRecord(parsed.memberships) || !isRecord(parsed.deviceTokens)) {
        throw new Error('identity store schema is invalid');
    }
    if (parsed.schemaVersion === 3) {
        return { data: validateCurrentData(parsed), migrated: false };
    }
    if (parsed.schemaVersion === 1 || parsed.schemaVersion === 2) {
        try {
            validateLegacyIdentities(parsed.identities);
            const migrated = migrateLegacyData(parsed as unknown as IdentityStoreDataLegacy, randomBytes);
            validateCurrentData(migrated as unknown as Record<string, unknown>);
            return { data: migrated, migrated: true };
        } catch (error) {
            throw new IdentityMigrationError(error instanceof Error ? error.message : 'identity migration failed');
        }
    }
    throw new Error('identity store schema is unsupported');
};

interface IdentityStoreOptions {
    fs?: typeof fs;
    randomBytes?: (size: number) => Buffer;
}

export class IdentityStore {
    private data: IdentityStoreData = createEmptyData();
    private writeChain: Promise<void> = Promise.resolve();
    private readonly fileSystem: typeof fs;
    private readonly randomBytes: (size: number) => Buffer;
    readonly filePath: string;
    readonly previousPath: string;

    constructor(filePath: string, options: IdentityStoreOptions = {}) {
        this.fileSystem = options.fs || fs;
        this.randomBytes = options.randomBytes || crypto.randomBytes;
        this.filePath = path.resolve(filePath);
        const extension = path.extname(this.filePath) || '.json';
        this.previousPath = path.join(
            path.dirname(this.filePath),
            `${path.basename(this.filePath, path.extname(this.filePath))}.previous${extension}`,
        );
    }

    async load(): Promise<void> {
        const primaryExists = this.fileSystem.existsSync(this.filePath);
        const previousExists = this.fileSystem.existsSync(this.previousPath);
        if (!primaryExists && !previousExists) {
            this.data = createEmptyData();
            await this.persist(this.data);
            return;
        }

        if (primaryExists) {
            let parsed: { data: IdentityStoreData; migrated: boolean } | undefined;
            try {
                parsed = parseData(this.fileSystem.readFileSync(this.filePath, 'utf8'), this.randomBytes);
            } catch (primaryError) {
                if (primaryError instanceof IdentityMigrationError) throw primaryError;
                if (!previousExists) throw primaryError;
            }
            if (parsed) {
                if (parsed.migrated) {
                    omitLegacyMembershipAuthFields(parsed.data);
                    await this.persist(parsed.data);
                }
                this.data = parsed.data;
                return;
            }
        }

        const restored = omitLegacyMembershipAuthFields(
            parseData(this.fileSystem.readFileSync(this.previousPath, 'utf8'), this.randomBytes).data,
        );
        await this.persist(restored, false);
        this.data = restored;
    }

    snapshot(): IdentityStoreData {
        return cloneData(this.data);
    }

    async mutate<T>(mutator: (draft: IdentityStoreData) => T): Promise<T> {
        let result!: T;
        const operation = this.writeChain.then(async () => {
            const draft = cloneData(this.data);
            result = mutator(draft);
            omitLegacyMembershipAuthFields(draft);
            await this.persist(draft);
            this.data = draft;
        });
        this.writeChain = operation.then(() => undefined, () => undefined);
        await operation;
        return result;
    }

    async flush(): Promise<void> {
        await this.writeChain;
    }

    private async persist(data: IdentityStoreData, backupCurrent = true): Promise<void> {
        const directory = path.dirname(this.filePath);
        this.fileSystem.mkdirSync(directory, { recursive: true });
        const tempPath = `${this.filePath}.tmp-${process.pid}-${Date.now()}`;
        try {
            this.fileSystem.writeFileSync(tempPath, JSON.stringify(data, null, 2), 'utf8');
            if (backupCurrent && this.fileSystem.existsSync(this.filePath)) {
                this.fileSystem.copyFileSync(this.filePath, this.previousPath);
            }
            this.fileSystem.renameSync(tempPath, this.filePath);
        } finally {
            if (this.fileSystem.existsSync(tempPath)) this.fileSystem.rmSync(tempPath, { force: true });
        }
    }
}
