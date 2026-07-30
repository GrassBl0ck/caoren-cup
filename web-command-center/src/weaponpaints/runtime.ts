import path from 'node:path';

import { enqueuePluginCommand } from '../plugin-command-queue';
import { WeaponPaintsCatalog } from './catalog';
import { buildWeaponPaintsHealth, type WeaponPaintsHealthInput } from './health';
import { createWeaponPaintsConnectionOptions, MySqlLoadoutRepository } from './mysql-repository';
import { WeaponPaintsService } from './service';
import { requireWeaponPaintsWebEnabled, WeaponPaintsSettingsStore } from './settings';

export class WeaponPaintsRuntime {
    catalog?: WeaponPaintsCatalog;
    service?: WeaponPaintsService;
    readonly settings: WeaponPaintsSettingsStore;
    private repository?: MySqlLoadoutRepository;
    private catalogError?: string;
    private databaseError?: string;

    constructor(settings?: WeaponPaintsSettingsStore) {
        this.settings = settings || new WeaponPaintsSettingsStore(
            path.resolve(__dirname, '..', '..', 'runtime', 'weaponpaints-settings.json'),
        );
    }

    async initialize() {
        await this.settings.initialize();
        const dataRoot = process.env.WEAPONPAINTS_DATA_ROOT
            ? path.resolve(process.env.WEAPONPAINTS_DATA_ROOT)
            : path.resolve(process.cwd(), '..', 'weaponpaints-plugin', 'data');
        const imageRoot = process.env.WEAPONPAINTS_IMAGE_ROOT
            ? path.resolve(process.env.WEAPONPAINTS_IMAGE_ROOT)
            : path.resolve(process.cwd(), 'public', 'weaponpaints');
        try {
            this.catalog = await WeaponPaintsCatalog.load(dataRoot, imageRoot);
            this.catalogError = undefined;
        } catch (error) {
            this.catalog = undefined;
            this.catalogError = error instanceof Error ? error.message : '本地物品目录加载失败。';
        }
        if (!this.catalog) return;
        try {
            const options = createWeaponPaintsConnectionOptions(process.env);
            this.repository = new MySqlLoadoutRepository(options);
            this.service = new WeaponPaintsService(this.catalog, this.repository, (command) => {
                enqueuePluginCommand('EXECUTE_SERVER_COMMAND', { command, label: '换肤配置刷新' });
            });
            await this.repository.initialize();
            this.databaseError = undefined;
        } catch (error) {
            this.databaseError = error instanceof Error ? error.message : '换肤数据库初始化失败。';
        }
    }

    requireCatalog() {
        requireWeaponPaintsWebEnabled(this.settings);
        if (!this.catalog) throw new Error(this.catalogError || '本地物品目录尚未就绪。');
        return this.catalog;
    }

    requireService() {
        requireWeaponPaintsWebEnabled(this.settings);
        if (!this.service) throw new Error(this.databaseError || this.catalogError || '换肤服务尚未配置。');
        return this.service;
    }

    async health(bridge: WeaponPaintsHealthInput['bridge'] = { ok: false, lastHeartbeatAt: null }) {
        const database = this.repository ? await this.repository.health() : { ok: false, error: this.databaseError || '未配置换肤数据库。' };
        return buildWeaponPaintsHealth({
            settings: this.settings.snapshot(),
            catalog: this.catalog
                ? { ok: true, counts: this.catalog.summary() }
                : { ok: false, error: this.catalogError || '本地物品目录未加载。' },
            database,
            bridge,
        });
    }
}

export const weaponPaintsRuntime = new WeaponPaintsRuntime();
export const initializeWeaponPaintsRuntime = () => weaponPaintsRuntime.initialize();
