import fs from 'node:fs/promises';
import path from 'node:path';

export type WeaponPaintsSettingsSnapshot = {
    enabled: boolean;
    error?: string;
};

export class WeaponPaintsSettingsStore {
    private state: WeaponPaintsSettingsSnapshot = { enabled: true };

    constructor(private readonly settingsPath: string) {}

    async initialize(): Promise<void> {
        try {
            const parsed = JSON.parse(await fs.readFile(this.settingsPath, 'utf8'));
            if (typeof parsed?.enabled !== 'boolean') {
                throw new Error('enabled 必须是布尔值。');
            }
            this.state = { enabled: parsed.enabled };
        } catch (error: any) {
            if (error?.code === 'ENOENT') {
                this.state = { enabled: true };
                return;
            }
            this.state = {
                enabled: false,
                error: `网页换肤配置文件无法读取：${error instanceof Error ? error.message : '未知错误'}`,
            };
        }
    }

    snapshot(): WeaponPaintsSettingsSnapshot {
        return { enabled: this.state.enabled, error: this.state.error };
    }

    async setEnabled(enabled: boolean): Promise<void> {
        await fs.mkdir(path.dirname(this.settingsPath), { recursive: true });
        const tempPath = `${this.settingsPath}.tmp-${process.pid}-${Date.now()}`;
        await fs.writeFile(tempPath, `${JSON.stringify({ enabled, updatedAt: Date.now() }, null, 2)}\n`, 'utf8');
        await fs.rename(tempPath, this.settingsPath);
        this.state = { enabled };
    }
}

export const requireWeaponPaintsWebEnabled = (store: WeaponPaintsSettingsStore): void => {
    const settings = store.snapshot();
    if (settings.enabled && !settings.error) return;
    throw new Error(settings.error || '网页换肤总开关已关闭。');
};
