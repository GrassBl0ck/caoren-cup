import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';

import {
    cancelPluginCommands,
    enqueuePluginCommand,
    getPluginCommandQueueSummary,
} from '../plugin-command-queue';
import { buildBridgeHealth, buildWeaponPaintsHealth } from './health';
import { WeaponPaintsRuntime } from './runtime';
import { requireWeaponPaintsWebEnabled, WeaponPaintsSettingsStore } from './settings';
import { executeWeaponPaintsAdminAction } from './socket-api';

const withTempSettings = async (work: (settingsPath: string) => Promise<void>) => {
    const directory = await fs.mkdtemp(path.join(os.tmpdir(), 'caoren-wp-settings-'));
    try {
        await work(path.join(directory, 'weaponpaints-settings.json'));
    } finally {
        await fs.rm(directory, { recursive: true, force: true });
    }
};

test('网页换肤设置首次启动默认开启，并在重载后保留管理员选择', async () => {
    await withTempSettings(async (settingsPath) => {
        const first = new WeaponPaintsSettingsStore(settingsPath);
        await first.initialize();
        assert.deepEqual(first.snapshot(), {
            enabled: true,
            error: undefined,
        });

        await first.setEnabled(false);
        const reloaded = new WeaponPaintsSettingsStore(settingsPath);
        await reloaded.initialize();
        assert.equal(reloaded.snapshot().enabled, false);
        assert.equal(reloaded.snapshot().error, undefined);
    });
});

test('损坏的网页换肤设置会安全关闭并报告错误', async () => {
    await withTempSettings(async (settingsPath) => {
        await fs.writeFile(settingsPath, '{not-json', 'utf8');
        const store = new WeaponPaintsSettingsStore(settingsPath);
        await store.initialize();

        assert.equal(store.snapshot().enabled, false);
        assert.match(store.snapshot().error || '', /配置文件/);
    });
});

test('健康状态同时汇总总开关、目录、数据库和桥接心跳', () => {
    const healthy = buildWeaponPaintsHealth({
        settings: { enabled: true },
        catalog: { ok: true },
        database: { ok: true },
        bridge: { ok: true, lastHeartbeatAt: 123 },
    });
    assert.equal(healthy.ok, true);
    assert.equal(healthy.status, 'healthy');

    const disabled = buildWeaponPaintsHealth({
        settings: { enabled: false },
        catalog: { ok: true },
        database: { ok: true },
        bridge: { ok: true, lastHeartbeatAt: 123 },
    });
    assert.equal(disabled.ok, false);
    assert.equal(disabled.status, 'disabled');
});

test('关闭网页总开关时可清理待发送或待重试的刷新命令', () => {
    cancelPluginCommands(() => true);
    enqueuePluginCommand('EXECUTE_SERVER_COMMAND', {
        command: 'wp_refresh 76561198000000001 safe',
        label: '安全刷新',
    });
    enqueuePluginCommand('TEST_ONLY_KEEP', {
        label: '结束热身',
    });

    const removed = cancelPluginCommands((command) =>
        command.type === 'EXECUTE_SERVER_COMMAND' &&
        String(command.payload?.command || '').startsWith('wp_refresh '));

    assert.equal(removed, 1);
    assert.deepEqual(
        getPluginCommandQueueSummary().map((command) => command.label),
        ['结束热身'],
    );
    cancelPluginCommands(() => true);
});

test('管理员可关闭网页换肤并清理刷新，普通玩家不能修改总开关', async () => {
    await withTempSettings(async (settingsPath) => {
        const store = new WeaponPaintsSettingsStore(settingsPath);
        await store.initialize();
        cancelPluginCommands(() => true);
        enqueuePluginCommand('EXECUTE_SERVER_COMMAND', {
            command: 'wp_refresh 76561198000000001 safe',
            label: '安全刷新',
        });

        await assert.rejects(
            () => executeWeaponPaintsAdminAction(store, { playerId: 'p1', role: 'Player' }, {
                action: 'setEnabled',
                enabled: false,
            }),
            /管理员/,
        );

        const result = await executeWeaponPaintsAdminAction(
            store,
            { playerId: 'admin', role: 'Admin' },
            { action: 'setEnabled', enabled: false },
        );
        assert.deepEqual(result, { enabled: false, canceledRefreshCommands: 1 });
        assert.equal(getPluginCommandQueueSummary().length, 0);
        assert.throws(() => requireWeaponPaintsWebEnabled(store), /已关闭/);
    });
});

test('管理员总开关只接受明确的布尔值', async () => {
    await withTempSettings(async (settingsPath) => {
        const store = new WeaponPaintsSettingsStore(settingsPath);
        await store.initialize();
        await assert.rejects(
            () => executeWeaponPaintsAdminAction(
                store,
                { playerId: 'admin', role: 'Admin' },
                { action: 'setEnabled', enabled: 'false' },
            ),
            /布尔值/,
        );
        assert.equal(store.snapshot().enabled, true);
    });
});

test('桥接健康状态复用既有心跳超时判断', () => {
    assert.deepEqual(
        buildBridgeHealth({ pluginConnected: true, lastPluginHeartbeatAt: 9_500 }, 10_000, 1_000),
        { ok: true, lastHeartbeatAt: 9_500 },
    );
    assert.deepEqual(
        buildBridgeHealth({ pluginConnected: true, lastPluginHeartbeatAt: 8_999 }, 10_000, 1_000),
        { ok: false, lastHeartbeatAt: 8_999 },
    );
});

test('运行时目录和数据库入口都会执行网页总开关保护', async () => {
    await withTempSettings(async (settingsPath) => {
        const store = new WeaponPaintsSettingsStore(settingsPath);
        await store.initialize();
        await store.setEnabled(false);
        const runtime = new WeaponPaintsRuntime(store);

        assert.throws(() => runtime.requireCatalog(), /已关闭/);
        assert.throws(() => runtime.requireService(), /已关闭/);
    });
});
