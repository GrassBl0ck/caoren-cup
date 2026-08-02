import assert from 'node:assert/strict';
import test from 'node:test';
import path from 'node:path';
import { promises as fs } from 'node:fs';

import { WeaponPaintsCatalog } from './catalog';
import { resolveSkinActor } from './permissions';
import { validateWeaponUpdate } from './validation';
import { WeaponPaintsService } from './service';
import type { LoadoutRepository, SkinAuditEntry } from './repository';
import { buildTeamCopyStatements, createWeaponPaintsConnectionOptions, WEAPONPAINTS_WEB_SCHEMA_SQL, WEAPONPAINTS_RESET_SQL } from './mysql-repository';
import { executeWeaponPaintsAction } from './socket-api';
import { resolveWeaponPaintsDataRoot } from './runtime';
import { parseSelectedPaints } from './http-routes';

const dataRoot = path.resolve(process.cwd(), '..', 'weaponpaints-plugin', 'data');

test('本地目录能够提供完整分类，并拒绝目录外的物品 ID', async () => {
    const catalog = await WeaponPaintsCatalog.load(dataRoot);

    assert.ok(catalog.search('skin', '', { limit: 10 }).items.length > 0);
    assert.ok(catalog.search('sticker', '印花', { limit: 10 }).items.length > 0);
    assert.equal(catalog.hasSimpleItem('sticker', 999_999_999), false);
    assert.equal(catalog.hasWeaponPaint(7, 999_999_999), false);
});

test('枪械、刀具和手套按型号分组，并使用已保存涂装作为预览', async () => {
    const catalog = await WeaponPaintsCatalog.load(dataRoot);
    const guns = catalog.searchGroups('skin', '', { kind: 'gun', team: 2, selectedPaints: new Map([[7, 1466]]) });
    const knives = catalog.searchGroups('skin', '', { kind: 'knife', team: 2 });
    const gloves = catalog.searchGroups('glove', '', { team: 2 });

    assert.ok(guns.groups.length < catalog.search('skin', '', { kind: 'gun' }).total);
    assert.equal(new Set(guns.groups.map((group) => group.defIndex)).size, guns.groups.length);
    assert.equal(new Set(knives.groups.map((group) => group.defIndex)).size, knives.groups.length);
    assert.equal(new Set(gloves.groups.map((group) => group.defIndex)).size, gloves.groups.length);
    assert.equal(guns.groups.find((group) => group.defIndex === 7)?.representative.id, 1466);
    assert.equal(catalog.searchGroups('skin', '精灵之噬', {
        kind: 'gun', team: 2, selectedPaints: new Map([[7, 0]]),
    }).groups[0]?.representative.id, 1466);
});

test('枪械目录按阵营隐藏不可用的专属武器', async () => {
    const catalog = await WeaponPaintsCatalog.load(dataRoot);
    const tDefIndexes = new Set(catalog.searchGroups('skin', '', { kind: 'gun', team: 2 }).groups.map((group) => group.defIndex));
    const ctDefIndexes = new Set(catalog.searchGroups('skin', '', { kind: 'gun', team: 3 }).groups.map((group) => group.defIndex));

    assert.equal(tDefIndexes.has(7), true, 'T 应显示 AK-47');
    assert.equal(tDefIndexes.has(16), false, 'T 不应显示 M4A4');
    assert.equal(tDefIndexes.has(60), false, 'T 不应显示 M4A1-S');
    assert.equal(ctDefIndexes.has(7), false, 'CT 不应显示 AK-47');
    assert.equal(ctDefIndexes.has(16), true, 'CT 应显示 M4A4');
    assert.equal(ctDefIndexes.has(60), true, 'CT 应显示 M4A1-S');
});

test('分组目录只接受有界的已保存涂装映射', () => {
    assert.deepEqual([...parseSelectedPaints('7:1466,16:309').entries()], [[7, 1466], [16, 309]]);
    assert.deepEqual([...parseSelectedPaints('bad,7:-1,0:1,16:abc').entries()], []);
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

test('玩家中心身份只能编辑本人 SteamID，管理员可代管已验证玩家', () => {
    const player = { identityId: 'identity-player-1', steamId: '76561198000000001' } as const;
    const admin = { playerId: 'admin-1', role: 'Admin' } as const;

    assert.deepEqual(resolveSkinActor(player, undefined, new Set()), {
        actorPlayerId: 'identity:identity-player-1',
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

test('未加入比赛的玩家中心身份可以使用身份库 SteamID，且旧 playerId 权限不能绕过', () => {
    const playerCenterIdentity = {
        identityId: 'identity-player-center',
        steamId: '76561198000000001',
    };

    assert.deepEqual(resolveSkinActor(playerCenterIdentity as any, undefined, new Set()), {
        actorPlayerId: 'identity:identity-player-center',
        actorRole: 'Player',
        targetSteamId: '76561198000000001',
    });
    assert.throws(
        () => resolveSkinActor({
            playerId: 'legacy-player',
            role: 'Player',
            steamId: '76561198000000001',
            identityLevel: 'longTerm',
            confirmationState: 'confirmed',
        } as any, undefined, new Set()),
        /玩家中心/,
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
        team: 2, weaponDefIndex: 7, paintId: 0, wear: 0.1, seed: 4,
        nameTag: '', statTrakEnabled: false, statTrakCount: 0, stickers: [],
    });

    assert.equal(calls[0]?.kind, 'save');
    assert.deepEqual(calls.at(-1), { kind: 'command', value: 'wp_refresh 76561198000000001 safe' });
    await assert.rejects(
        service.saveWeapon(actor, { team: 3, weaponDefIndex: 7, paintId: 999_999_999 }),
        /本地目录/,
    );
    await assert.rejects(
        service.saveWeapon(actor, { team: 3, weaponDefIndex: 7, paintId: 0 }),
        /当前阵营/,
    );
    await assert.rejects(
        service.saveWeapon(actor, {
            team: 3, weaponDefIndex: 500, paintId: 0,
            stickers: [{ slot: 0, id: 1 }],
        }),
        /只有枪械可以使用印花/,
    );
    await assert.rejects(
        service.saveWeapon(actor, {
            team: 3, weaponDefIndex: 500, paintId: 0,
            keychain: { id: 1 },
        }),
        /只有枪械可以使用挂件/,
    );
});

test('复制阵营时仅传递目标阵营可用武器和枪械印花规则', async () => {
    const catalog = await WeaponPaintsCatalog.load(dataRoot);
    let capturedRules: { excludedWeaponDefIndexes: readonly number[]; stickerEligibleWeaponDefIndexes: readonly number[] } | undefined;
    const repository: LoadoutRepository = {
        health: async () => ({ ok: true }),
        load: async (steamId) => ({ steamId, weapons: [], cosmetics: [] }),
        saveWeapon: async () => undefined,
        saveCosmetic: async () => undefined,
        copyTeam: async (_steamId, _fromTeam, _toTeam, rules) => { capturedRules = rules; },
        reset: async () => undefined,
        audit: async () => undefined,
    };
    const service = new WeaponPaintsService(catalog, repository, () => undefined);
    const actor = { actorPlayerId: 'player-1', actorRole: 'Player' as const, targetSteamId: '76561198000000001' };

    await service.copyTeam(actor, 2, 3);

    assert.ok(capturedRules?.excludedWeaponDefIndexes.includes(7));
    assert.ok(capturedRules?.excludedWeaponDefIndexes.includes(16));
    assert.ok(capturedRules?.stickerEligibleWeaponDefIndexes.includes(9));
    assert.equal(capturedRules?.stickerEligibleWeaponDefIndexes.includes(16), false);
    assert.equal(capturedRules?.stickerEligibleWeaponDefIndexes.includes(500), false);
});

test('复制阵营 SQL 过滤专属枪械、非枪械印花和探员', () => {
    const statements = buildTeamCopyStatements('76561198000000001', 2, 3, {
        excludedWeaponDefIndexes: [7, 16],
        stickerEligibleWeaponDefIndexes: [9],
    });
    const sql = statements.map((statement) => statement.sql).join('\n');

    assert.match(statements[0].sql, /weapon_defindex` NOT IN \(\?,\?\)/);
    assert.deepEqual(statements[0].params.slice(-2), [7, 16]);
    assert.match(statements[1].sql, /weapon_defindex` IN \(\?\)/);
    assert.deepEqual(statements[1].params.slice(-1), [9]);
    assert.match(statements[2].sql, /kind` <> 'Agent'/);
    assert.doesNotMatch(sql, /\b(?:DELETE|DROP|TRUNCATE)\b/i);
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
        identityId: 'server-authenticated-identity', steamId: '76561198000000001',
    };

    await executeWeaponPaintsAction(service, player, verifiedSteamIds, { action: 'load', playerId: 'forged-player' });
    assert.deepEqual(loaded, ['76561198000000001']);
    await assert.rejects(
        executeWeaponPaintsAction(service, undefined, verifiedSteamIds, { action: 'load' }),
        /登录/,
    );
});
