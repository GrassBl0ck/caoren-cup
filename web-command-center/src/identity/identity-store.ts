import fs from 'node:fs';
import path from 'node:path';
import { IdentityStoreData, IdentityStoreDataV1 } from './identity-types';

const createEmptyData = (): IdentityStoreData => ({
    schemaVersion: 2,
    identities: {},
    memberships: {},
    deviceTokens: {},
});

const cloneData = (data: IdentityStoreData): IdentityStoreData => JSON.parse(JSON.stringify(data));

const isRecord = (value: unknown): value is Record<string, unknown> =>
    !!value && typeof value === 'object' && !Array.isArray(value);

const isPositiveInteger = (value: unknown, maximum: number): value is number =>
    Number.isInteger(value) && Number(value) > 0 && Number(value) <= maximum;

const validateFixedAccountCredentials = (identities: Record<string, unknown>): void => {
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
        const password = fixedAccount.password;
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
            !isPositiveInteger(params.maxmem, 256 * 1024 * 1024) ||
            typeof identityValue.steamId !== 'string' || !/^7656119\d{10}$/.test(identityValue.steamId)) {
            throw new Error('fixed account credential is invalid');
        }
        const salt = Buffer.from(password.salt, 'base64url');
        const hash = Buffer.from(password.hash, 'base64url');
        if (salt.length !== 32 || hash.length !== params.keyLength) throw new Error('fixed account credential is invalid');
    }
};

const parseData = (text: string): { data: IdentityStoreData; migrated: boolean } => {
    const parsed: unknown = JSON.parse(text);
    if (!isRecord(parsed) || !isRecord(parsed.identities) || !isRecord(parsed.memberships) || !isRecord(parsed.deviceTokens)) {
        throw new Error('identity store schema is invalid');
    }
    validateFixedAccountCredentials(parsed.identities);
    if (parsed.schemaVersion === 2) {
        return { data: parsed as unknown as IdentityStoreData, migrated: false };
    }
    if (parsed.schemaVersion === 1) {
        const legacy = parsed as unknown as IdentityStoreDataV1;
        return {
            data: {
                schemaVersion: 2,
                identities: legacy.identities,
                memberships: legacy.memberships,
                deviceTokens: legacy.deviceTokens,
            },
            migrated: true,
        };
    }
    throw new Error('identity store schema is unsupported');
};

interface IdentityStoreOptions {
    fs?: typeof fs;
}

export class IdentityStore {
    private data: IdentityStoreData = createEmptyData();
    private writeChain: Promise<void> = Promise.resolve();
    private readonly fileSystem: typeof fs;
    readonly filePath: string;
    readonly previousPath: string;

    constructor(filePath: string, options: IdentityStoreOptions = {}) {
        this.fileSystem = options.fs || fs;
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
            try {
                const parsed = parseData(this.fileSystem.readFileSync(this.filePath, 'utf8'));
                this.data = parsed.data;
                if (parsed.migrated) await this.persist(this.data);
                return;
            } catch (primaryError) {
                if (!previousExists) throw primaryError;
            }
        }

        this.data = parseData(this.fileSystem.readFileSync(this.previousPath, 'utf8')).data;
        await this.persist(this.data, false);
    }

    snapshot(): IdentityStoreData {
        return cloneData(this.data);
    }

    async mutate<T>(mutator: (draft: IdentityStoreData) => T): Promise<T> {
        let result!: T;
        const operation = this.writeChain.then(async () => {
            const draft = cloneData(this.data);
            result = mutator(draft);
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
