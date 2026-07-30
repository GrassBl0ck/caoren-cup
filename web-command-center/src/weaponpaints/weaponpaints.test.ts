import assert from 'node:assert/strict';
import test from 'node:test';
import path from 'node:path';
import { promises as fs } from 'node:fs';

import { WeaponPaintsCatalog } from './catalog';
import { resolveSkinActor } from './permissions';
import { validateWeaponUpdate } from './validation';
import { WeaponPaintsService } from './service';
import type { LoadoutRepository, SkinAuditEntry } from './repository';
import { createWeaponPaintsConnectionOptions, WEAPONPAINTS_WEB_SCHEMA_SQL, WEAPONPAINTS_RESET_SQL } from './mysql-repository';
import { executeWeaponPaintsAction } from './socket-api';
import { resolveWeaponPaintsDataRoot } from './runtime';

const dataRoot = path.resolve(process.cwd(), '..', 'weaponpaints-plugin', 'data');

test('本地目录能够提供完整分类，并拒绝目录外的物品 ID', async () => {
    const catalog = await WeaponPaintsCatalog.load(dataRoot);

    assert.ok(catalog.search('skin', '', { limit: 10 }).items.length > 0);
    assert.ok(catalog.search('sticker', '印花', { limit: 10 }).items.length > 0);
    assert.equal(catalog.hasSimpleItem('sticker', 999_999_999), false);
    assert.equal(catalog.hasWeaponPaint(7, 999_999_999), false);
});

test('本地图片清单为目录物品提供站内 URL，缺图时不返回远程地址', async () => {
    const imageRoot = path.resolve(process.cwd(), 'runtime', `test-weaponpaints-images-${process.pid}`);
    try {
        await fs.mkdir(path.join(imageRoot, 'base'), { recursive: true });
        await fs.mkdir(path.join(imageRoot, 'stickers'), { recursive: true });
        await fs.writeFile(path.join(imageRoot, 'base', 'manifest.json'), JSON.stringify({
            schemaVersion: 1,
            pack: 'base',
            items: [
                { category: 'skin', key: 'weapon_ak47:1466', image: 'images/weapon_ak47-1466.png', available: true },
                { category: 'skin', key: 'weapon_ak47:1449', image: null, available: false },
            ],
        }), 'utf8');
        await fs.writeFile(path.join(imageRoot, 'stickers', 'manifest.json'), JSON.stringify({
            schemaVersion: 1,
            pack: 'stickers',
            items: [
                { category: 'sticker', key: '1', image: 'images/sticker-1.png', available: true },
                { category: 'sticker', key: '10', image: 'https://example.invalid/remote.png', available: true },
            ],
        }), 'utf8');

        const catalog = await WeaponPaintsCatalog.load(dataRoot, imageRoot);
        assert.equal(catalog.search('skin', 'Consequence of the Jinn').items[0]?.imageUrl,
            '/weaponpaints/base/images/weapon_ak47-1466.png');
        assert.equal(catalog.search('skin', 'AUTOEXEC').items[0]?.imageUrl, undefined);
        assert.equal(catalog.search('sticker', 'Shooter').items[0]?.imageUrl,
            '/weaponpaints/stickers/images/sticker-1.png');
        assert.equal(catalog.search('sticker', 'Mountain').items[0]?.imageUrl, undefined);
    } finally {
        await fs.rm(imageRoot, { recursive: true, force: true });
    }
});

test('发布版优先读取网页包内目录，开发环境回退到仓库目录', async () => {
    const runtimeRoot = path.resolve(process.cwd(), 'runtime', `test-weaponpaints-root-${process.pid}`);
    try {
        await fs.mkdir(path.join(runtimeRoot, 'weaponpaints-data'), { recursive: true });
        assert.equal(resolveWeaponPaintsDataRoot(runtimeRoot), path.join(runtimeRoot, 'weaponpaints-data'));
        assert.equal(
            resolveWeaponPaintsDataRoot(path.join(runtimeRoot, 'without-package')),
            path.resolve(runtimeRoot, 'weaponpaints-plugin', 'data'),
        );
        assert.equal(
            resolveWeaponPaintsDataRoot(runtimeRoot, './configured-data'),
            path.resolve(runtimeRoot, 'configured-data'),
        );
    } finally {
        await fs.rm(runtimeRoot, { recursive: true, force: true });
    }
});

test('玩家只能编辑已确认的本人 SteamID，管理员可代管已验证玩家', () => {
    const player = {
        playerId: 'player-1',
        role: 'Player',
        steamId: '76561198000000001',
        identityLevel: 'longTerm',
        confirmationState: 'confirmed',
    } as const;
    const admin = { playerId: 'admin-1', role: 'Admin' } as const;

    assert.deepEqual(resolveSkinActor(player, undefined, new Set()), {
        actorPlayerId: 'player-1',
        actorRole: 'Player',
        targetSteamId: '76561198000000001',
    });
    assert.throws(
        () => resolveSkinActor(player, '76561198000000002', new Set(['76561198000000002'])),
        /只能编辑本人/,
    );
    assert.deepEqual(resolveSkinActor(admin, '76561198000000002', new Set(['76561198000000002'])), {
        actorPlayerId: 'admin-1',
        actorRole: 'Admin',
        targetSteamId: '76561198000000002',
    });
    assert.throws(
        () => resolveSkinActor(admin, '76561198000000003', new Set(['76561198000000002'])),
        /尚未验证/,
    );
});

test('武器高级参数执行严格范围校验', () => {
    assert.deepEqual(validateWeaponUpdate({
        team: 3,
        weaponDefIndex: 7,
        paintId: 1,
        wear: 0.12,
        seed: 321,
        nameTag: '草人杯',
        statTrakEnabled: true,
        statTrakCount: 12,
        keychain: { id: 1, offsetX: 0, offsetY: 0, offsetZ: 0, seed: 10 },
        stickers: [{ slot: 0, id: 1, schema: 0, offsetX: 0, offsetY: 0, wear: 0, scale: 1, rotation: 0 }],
    }).seed, 321);

    assert.throws(() => validateWeaponUpdate({ team: 3, weaponDefIndex: 7, paintId: 1, wear: 1.1 }), /磨损/);
    assert.throws(() => validateWeaponUpdate({ team: 3, weaponDefIndex: 7, paintId: 1, seed: 1001 }), /Seed/);
    assert.throws(() => validateWeaponUpdate({ team: 3, weaponDefIndex: 7, paintId: 1, stickers: [{ slot: 5, id: 1 }] }), /印花槽/);
});

test('保存武器时校验本地目录、记录审计并请求安全刷新', async () => {
    const catalog = await WeaponPaintsCatalog.load(dataRoot);
    const calls: Array<{ kind: string; value: unknown }> = [];
    const repository: LoadoutRepository = {
        health: async () => ({ ok: true }),
        load: async (steamId) => ({ steamId, weapons: [], cosmetics: [] }),
        saveWeapon: async (steamId, update, audit) => { calls.push({ kind: 'save', value: { steamId, update, audit } }); },
        saveCosmetic: async () => undefined,
        copyTeam: async () => undefined,
        reset: async () => undefined,
        audit: async (entry: SkinAuditEntry) => { calls.push({ kind: 'audit', value: entry }); },
    };
    const service = new WeaponPaintsService(catalog, repository, (command) => calls.push({ kind: 'command', value: command }));
    const actor = { actorPlayerId: 'player-1', actorRole: 'Player' as const, targetSteamId: '76561198000000001' };

    await service.saveWeapon(actor, {
        team: 3, weaponDefIndex: 7, paintId: 0, wear: 0.1, seed: 4,
        nameTag: '', statTrakEnabled: false, statTrakCount: 0, stickers: [],
    });

    assert.equal(calls[0]?.kind, 'save');
    assert.deepEqual(calls.at(-1), { kind: 'command', value: 'wp_refresh 76561198000000001 safe' });
    await assert.rejects(
        service.saveWeapon(actor, { team: 3, weaponDefIndex: 7, paintId: 999_999_999 }),
        /本地目录/,
    );
});

test('只有管理员可以重置和立即强刷', async () => {
    const catalog = await WeaponPaintsCatalog.load(dataRoot);
    const commands: string[] = [];
    const repository: LoadoutRepository = {
        health: async () => ({ ok: true }),
        load: async (steamId) => ({ steamId, weapons: [], cosmetics: [] }),
        saveWeapon: async () => undefined,
        saveCosmetic: async () => undefined,
        copyTeam: async () => undefined,
        reset: async () => undefined,
        audit: async () => undefined,
    };
    const service = new WeaponPaintsService(catalog, repository, (command) => commands.push(command));
    const player = { actorPlayerId: 'player-1', actorRole: 'Player' as const, targetSteamId: '76561198000000001' };
    const admin = { actorPlayerId: 'admin-1', actorRole: 'Admin' as const, targetSteamId: '76561198000000001' };

    await assert.rejects(service.reset(player), /管理员/);
    await assert.rejects(service.forceRefresh(player), /管理员/);
    await service.reset(admin);
    await service.forceRefresh(admin);
    assert.deepEqual(commands, [
        'wp_refresh 76561198000000001 safe',
        'wp_refresh 76561198000000001',
    ]);
});

test('数据库配置只能指向新库，审计与重置 SQL 不包含删除操作', () => {
    assert.equal(createWeaponPaintsConnectionOptions({
        WEAPONPAINTS_DB_USER: 'caoren',
        WEAPONPAINTS_DB_PASSWORD: 'secret',
    }).database, 'caoren_weaponpaints');
    assert.throws(() => createWeaponPaintsConnectionOptions({
        WEAPONPAINTS_DB_USER: 'caoren',
        WEAPONPAINTS_DB_PASSWORD: 'secret',
        WEAPONPAINTS_DB_NAME: 'weaponpaints_old',
    }), /caoren_weaponpaints/);
    const sql = `${WEAPONPAINTS_WEB_SCHEMA_SQL}\n${WEAPONPAINTS_RESET_SQL}`;
    assert.match(sql, /CREATE TABLE IF NOT EXISTS `web_audit_log`/);
    assert.doesNotMatch(sql, /\b(?:DELETE|DROP|TRUNCATE)\b/i);
});

test('Socket 操作以服务器认证身份为准，不接受前端冒充玩家', async () => {
    const catalog = await WeaponPaintsCatalog.load(dataRoot);
    const loaded: string[] = [];
    const repository: LoadoutRepository = {
        health: async () => ({ ok: true }),
        load: async (steamId) => { loaded.push(steamId); return { steamId, weapons: [], cosmetics: [] }; },
        saveWeapon: async () => undefined,
        saveCosmetic: async () => undefined,
        copyTeam: async () => undefined,
        reset: async () => undefined,
        audit: async () => undefined,
    };
    const service = new WeaponPaintsService(catalog, repository, () => undefined);
    const verifiedSteamIds = new Set(['76561198000000001']);
    const player = {
        playerId: 'server-authenticated-player', role: 'Player', steamId: '76561198000000001',
        identityLevel: 'longTerm', confirmationState: 'confirmed',
    };

    await executeWeaponPaintsAction(service, player, verifiedSteamIds, { action: 'load', playerId: 'forged-player' });
    assert.deepEqual(loaded, ['76561198000000001']);
    await assert.rejects(
        executeWeaponPaintsAction(service, undefined, verifiedSteamIds, { action: 'load' }),
        /登录/,
    );
});
