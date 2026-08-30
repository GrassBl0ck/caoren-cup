import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';

export const PLAYER_CENTER_IDLE_TTL_MS = 7 * 24 * 60 * 60 * 1000;
export const PLAYER_CENTER_ABSOLUTE_TTL_MS = 30 * 24 * 60 * 60 * 1000;

export interface PlayerCenterSessionRecord {
    sessionId: string;
    identityId: string;
    tokenHash: string;
    accountUpdatedAt: number;
    createdAt: number;
    lastUsedAt: number;
    idleExpiresAt: number;
    absoluteExpiresAt: number;
    currentDeviceTokenId?: string;
}

interface PlayerCenterSessionStoreData {
    schemaVersion: 1;
    sessions: Record<string, PlayerCenterSessionRecord>;
}

interface PlayerCenterSessionStoreOptions {
    fs?: typeof fs;
    now?: () => number;
    randomBytes?: (size: number) => Buffer;
}

const emptyData = (): PlayerCenterSessionStoreData => ({ schemaVersion: 1, sessions: {} });
const clone = <T>(value: T): T => JSON.parse(JSON.stringify(value));
const isRecord = (value: unknown): value is Record<string, unknown> =>
    !!value && typeof value === 'object' && !Array.isArray(value);

const parseData = (text: string): PlayerCenterSessionStoreData => {
    const parsed: unknown = JSON.parse(text);
    if (!isRecord(parsed) || parsed.schemaVersion !== 1 || !isRecord(parsed.sessions)) {
        throw new Error('player-center session store schema is invalid');
    }
    for (const [sessionId, value] of Object.entries(parsed.sessions)) {
        if (!isRecord(value) || value.sessionId !== sessionId ||
            typeof value.identityId !== 'string' || !value.identityId ||
            typeof value.tokenHash !== 'string' || !/^[A-Za-z0-9_-]{43}$/.test(value.tokenHash) ||
            typeof value.accountUpdatedAt !== 'number' || !Number.isFinite(value.accountUpdatedAt) ||
            typeof value.createdAt !== 'number' || !Number.isFinite(value.createdAt) ||
            typeof value.lastUsedAt !== 'number' || !Number.isFinite(value.lastUsedAt) ||
            typeof value.idleExpiresAt !== 'number' || !Number.isFinite(value.idleExpiresAt) ||
            typeof value.absoluteExpiresAt !== 'number' || !Number.isFinite(value.absoluteExpiresAt) ||
            (value.currentDeviceTokenId !== undefined &&
                (typeof value.currentDeviceTokenId !== 'string' || !/^[A-Za-z0-9_-]{8,128}$/.test(value.currentDeviceTokenId))) ||
            value.lastUsedAt < value.createdAt || value.idleExpiresAt < value.lastUsedAt ||
            value.absoluteExpiresAt < value.createdAt) {
            throw new Error('player-center session store schema is invalid');
        }
    }
    return parsed as unknown as PlayerCenterSessionStoreData;
};

const tokenHash = (rawToken: string): string =>
    crypto.createHash('sha256').update(rawToken, 'utf8').digest('base64url');

const parseToken = (raw: unknown): { sessionId: string; rawToken: string } | undefined => {
    const rawToken = String(raw || '').trim();
    const separator = rawToken.indexOf('.');
    if (separator <= 0 || separator === rawToken.length - 1 || rawToken.indexOf('.', separator + 1) !== -1) return undefined;
    const sessionId = rawToken.slice(0, separator);
    const secret = rawToken.slice(separator + 1);
    if (!/^[A-Za-z0-9_-]{22}$/.test(sessionId) || !/^[A-Za-z0-9_-]{43}$/.test(secret)) return undefined;
    return { sessionId, rawToken };
};

const tokenMatches = (record: PlayerCenterSessionRecord, rawToken: string): boolean => {
    const actual = Buffer.from(tokenHash(rawToken), 'base64url');
    const expected = Buffer.from(record.tokenHash, 'base64url');
    return actual.length === expected.length && crypto.timingSafeEqual(actual, expected);
};

export class PlayerCenterSessionStore {
    private data: PlayerCenterSessionStoreData = emptyData();
    private writeChain: Promise<void> = Promise.resolve();
    private readonly fileSystem: typeof fs;
    private readonly now: () => number;
    private readonly randomBytes: (size: number) => Buffer;
    readonly filePath: string;
    readonly previousPath: string;

    constructor(filePath: string, options: PlayerCenterSessionStoreOptions = {}) {
        this.fileSystem = options.fs || fs;
        this.now = options.now || Date.now;
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
            this.data = emptyData();
            await this.persist(this.data);
            return;
        }
        if (primaryExists) {
            try {
                this.data = parseData(this.fileSystem.readFileSync(this.filePath, 'utf8'));
                return;
            } catch (error) {
                if (!previousExists) throw error;
            }
        }
        const restored = parseData(this.fileSystem.readFileSync(this.previousPath, 'utf8'));
        await this.persist(restored, false);
        this.data = restored;
    }

    snapshot(): PlayerCenterSessionStoreData {
        return clone(this.data);
    }

    async create(identityId: string, accountUpdatedAt: number, currentDeviceTokenId?: string) {
        const createdAt = this.now();
        const sessionId = this.randomBytes(16).toString('base64url');
        const secret = this.randomBytes(32).toString('base64url');
        const rawToken = `${sessionId}.${secret}`;
        const session: PlayerCenterSessionRecord = {
            sessionId,
            identityId,
            tokenHash: tokenHash(rawToken),
            accountUpdatedAt,
            createdAt,
            lastUsedAt: createdAt,
            idleExpiresAt: createdAt + PLAYER_CENTER_IDLE_TTL_MS,
            absoluteExpiresAt: createdAt + PLAYER_CENTER_ABSOLUTE_TTL_MS,
            currentDeviceTokenId,
        };
        await this.mutate((data) => {
            if (data.sessions[sessionId]) throw new Error('player-center session id collision');
            data.sessions[sessionId] = session;
        });
        return { rawToken, session: clone(session) };
    }

    async use(raw: unknown): Promise<PlayerCenterSessionRecord | undefined> {
        const parsed = parseToken(raw);
        if (!parsed) return undefined;
        return this.mutate((data) => {
            const session = data.sessions[parsed.sessionId];
            if (!session || !tokenMatches(session, parsed.rawToken)) return undefined;
            const now = this.now();
            if (now >= session.idleExpiresAt || now >= session.absoluteExpiresAt) {
                delete data.sessions[session.sessionId];
                return undefined;
            }
            session.lastUsedAt = Math.max(session.lastUsedAt, now);
            session.idleExpiresAt = Math.min(session.absoluteExpiresAt, now + PLAYER_CENTER_IDLE_TTL_MS);
            return clone(session);
        });
    }

    async useBoundSession(sessionIdRaw: unknown, identityIdRaw: unknown): Promise<PlayerCenterSessionRecord | undefined> {
        const sessionId = String(sessionIdRaw || '');
        const identityId = String(identityIdRaw || '');
        if (!/^[A-Za-z0-9_-]{22}$/.test(sessionId) || !identityId) return undefined;
        return this.mutate((data) => {
            const session = data.sessions[sessionId];
            if (!session || session.identityId !== identityId) return undefined;
            const now = this.now();
            if (now >= session.idleExpiresAt || now >= session.absoluteExpiresAt) {
                delete data.sessions[session.sessionId];
                return undefined;
            }
            session.lastUsedAt = Math.max(session.lastUsedAt, now);
            session.idleExpiresAt = Math.min(session.absoluteExpiresAt, now + PLAYER_CENTER_IDLE_TTL_MS);
            return clone(session);
        });
    }

    async revokeCurrent(raw: unknown): Promise<boolean> {
        const parsed = parseToken(raw);
        if (!parsed) return false;
        return this.mutate((data) => {
            const session = data.sessions[parsed.sessionId];
            if (!session || !tokenMatches(session, parsed.rawToken)) return false;
            delete data.sessions[session.sessionId];
            return true;
        });
    }

    async applyAccountChange(identityId: string, preserveSessionId: string | undefined, accountUpdatedAt: number) {
        return this.mutate((data) => {
            const revokedSessionIds: string[] = [];
            for (const session of Object.values(data.sessions)) {
                if (session.identityId !== identityId) continue;
                if (preserveSessionId && session.sessionId === preserveSessionId) {
                    session.accountUpdatedAt = accountUpdatedAt;
                    continue;
                }
                revokedSessionIds.push(session.sessionId);
                delete data.sessions[session.sessionId];
            }
            return { revokedSessionIds };
        });
    }

    private async mutate<T>(mutator: (draft: PlayerCenterSessionStoreData) => T): Promise<T> {
        let result!: T;
        const operation = this.writeChain.then(async () => {
            const draft = clone(this.data);
            result = mutator(draft);
            await this.persist(draft);
            this.data = draft;
        });
        this.writeChain = operation.then(() => undefined, () => undefined);
        await operation;
        return result;
    }

    private async persist(data: PlayerCenterSessionStoreData, backupCurrent = true): Promise<void> {
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
