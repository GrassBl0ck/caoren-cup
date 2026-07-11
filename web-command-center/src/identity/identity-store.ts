import fs from 'node:fs';
import path from 'node:path';
import { IdentityStoreData } from './identity-types';

const createEmptyData = (): IdentityStoreData => ({
    schemaVersion: 1,
    identities: {},
    memberships: {},
    deviceTokens: {},
});

const cloneData = (data: IdentityStoreData): IdentityStoreData => JSON.parse(JSON.stringify(data));

const parseData = (text: string): IdentityStoreData => {
    const parsed = JSON.parse(text);
    if (parsed?.schemaVersion !== 1 || !parsed.identities || !parsed.memberships || !parsed.deviceTokens) {
        throw new Error('identity store schema is invalid');
    }
    return parsed as IdentityStoreData;
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
                this.data = parseData(this.fileSystem.readFileSync(this.filePath, 'utf8'));
                return;
            } catch (primaryError) {
                if (!previousExists) throw primaryError;
            }
        }

        this.data = parseData(this.fileSystem.readFileSync(this.previousPath, 'utf8'));
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
