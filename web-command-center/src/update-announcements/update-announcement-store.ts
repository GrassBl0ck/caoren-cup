import fs from 'node:fs';
import path from 'node:path';
import { createSeedUpdateAnnouncementData } from './update-announcement-seeds';
import {
    UnsupportedUpdateAnnouncementSchemaError,
    UpdateAnnouncementStoreData,
} from './update-announcement-types';

const cloneData = (data: UpdateAnnouncementStoreData): UpdateAnnouncementStoreData =>
    JSON.parse(JSON.stringify(data));

const isRecord = (value: unknown): value is Record<string, unknown> =>
    !!value && typeof value === 'object' && !Array.isArray(value);

const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const VERSION_PATTERN = /^v\d+\.\d+\.\d+$/;
const STATUSES = new Set(['draft', 'published', 'hidden']);

const validateAnnouncement = (recordId: string, value: unknown): void => {
    if (!isRecord(value)
        || value.id !== recordId
        || typeof value.id !== 'string'
        || !UUID_PATTERN.test(value.id)
        || typeof value.version !== 'string'
        || !VERSION_PATTERN.test(value.version)
        || typeof value.title !== 'string'
        || !isRecord(value.sections)
        || typeof value.sections.webHtml !== 'string'
        || typeof value.sections.gamePluginHtml !== 'string'
        || typeof value.sections.bridgePluginHtml !== 'string'
        || typeof value.status !== 'string'
        || !STATUSES.has(value.status)
        || !Number.isInteger(value.reminderRevision)
        || Number(value.reminderRevision) < 0
        || typeof value.createdAt !== 'number'
        || !Number.isFinite(value.createdAt)
        || typeof value.updatedAt !== 'number'
        || !Number.isFinite(value.updatedAt)
        || (value.publishedAt !== null
            && (typeof value.publishedAt !== 'number' || !Number.isFinite(value.publishedAt)))) {
        throw new Error('update announcement store schema is invalid');
    }
    if ((value.status === 'draft' && (value.publishedAt !== null || value.reminderRevision !== 0))
        || (value.status !== 'draft' && (value.publishedAt === null || Number(value.reminderRevision) < 1))) {
        throw new Error('update announcement store schema is invalid');
    }
};

const parseData = (text: string): UpdateAnnouncementStoreData => {
    const parsed: unknown = JSON.parse(text);
    if (!isRecord(parsed)) throw new Error('update announcement store schema is invalid');
    if (parsed.schemaVersion !== 1) throw new UnsupportedUpdateAnnouncementSchemaError();
    if (!isRecord(parsed.announcements)) throw new Error('update announcement store schema is invalid');
    const versions = new Set<string>();
    for (const [id, value] of Object.entries(parsed.announcements)) {
        validateAnnouncement(id, value);
        const version = (value as Record<string, unknown>).version as string;
        if (versions.has(version)) throw new Error('update announcement store schema is invalid');
        versions.add(version);
    }
    return parsed as unknown as UpdateAnnouncementStoreData;
};

interface UpdateAnnouncementStoreOptions {
    fs?: typeof fs;
}

export class UpdateAnnouncementStore {
    private data: UpdateAnnouncementStoreData = createSeedUpdateAnnouncementData();
    private writeChain: Promise<void> = Promise.resolve();
    private readonly fileSystem: typeof fs;
    readonly filePath: string;
    readonly previousPath: string;

    constructor(filePath: string, options: UpdateAnnouncementStoreOptions = {}) {
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
            this.data = createSeedUpdateAnnouncementData();
            await this.persist(this.data, false);
            return;
        }
        if (primaryExists) {
            try {
                this.data = parseData(this.fileSystem.readFileSync(this.filePath, 'utf8'));
                return;
            } catch (error) {
                if (error instanceof UnsupportedUpdateAnnouncementSchemaError || !previousExists) throw error;
            }
        }
        this.data = parseData(this.fileSystem.readFileSync(this.previousPath, 'utf8'));
        await this.persist(this.data, false);
    }

    snapshot(): UpdateAnnouncementStoreData {
        return cloneData(this.data);
    }

    async mutate<T>(mutator: (draft: UpdateAnnouncementStoreData) => T): Promise<T> {
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

    private async persist(data: UpdateAnnouncementStoreData, backupCurrent = true): Promise<void> {
        const directory = path.dirname(this.filePath);
        this.fileSystem.mkdirSync(directory, { recursive: true });
        const tempPath = `${this.filePath}.tmp-${process.pid}-${Date.now()}`;
        try {
            this.fileSystem.writeFileSync(tempPath, JSON.stringify(data, null, 2), 'utf8');
            if (backupCurrent && this.fileSystem.existsSync(this.filePath)) {
                parseData(this.fileSystem.readFileSync(this.filePath, 'utf8'));
                this.fileSystem.copyFileSync(this.filePath, this.previousPath);
            }
            this.fileSystem.renameSync(tempPath, this.filePath);
        } finally {
            if (this.fileSystem.existsSync(tempPath)) this.fileSystem.rmSync(tempPath, { force: true });
        }
    }
}
