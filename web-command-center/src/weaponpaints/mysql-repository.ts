import mysql, { Pool, PoolConnection, RowDataPacket } from 'mysql2/promise';

import type {
    CosmeticUpdate,
    LoadoutRepository,
    PlayerLoadout,
    SkinAuditEntry,
    TeamCopyRules,
} from './repository';
import type { WeaponUpdate } from './validation';

const DATABASE_NAME = 'caoren_weaponpaints';

export const WEAPONPAINTS_WEB_SCHEMA_SQL = `
CREATE TABLE IF NOT EXISTS \`web_audit_log\` (
    \`id\` BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    \`actor_player_id\` VARCHAR(64) NOT NULL,
    \`actor_role\` VARCHAR(16) NOT NULL,
    \`target_steamid\` VARCHAR(18) NOT NULL,
    \`action\` VARCHAR(32) NOT NULL,
    \`details_json\` TEXT NOT NULL,
    \`created_at\` TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (\`id\`),
    KEY \`idx_web_audit_target_created\` (\`target_steamid\`, \`created_at\`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci`;

export const WEAPONPAINTS_RESET_SQL = `
UPDATE \`weapon_loadouts\` SET \`paint_id\` = 0, \`wear\` = 0, \`seed\` = 0,
    \`name_tag\` = '', \`stattrak_enabled\` = 0, \`stattrak_count\` = 0,
    \`keychain_id\` = 0, \`keychain_offset_x\` = 0, \`keychain_offset_y\` = 0,
    \`keychain_offset_z\` = 0, \`keychain_seed\` = 0 WHERE \`steamid\` = ?;
UPDATE \`weapon_stickers\` SET \`sticker_id\` = 0, \`sticker_schema\` = 0,
    \`offset_x\` = 0, \`offset_y\` = 0, \`wear\` = 0, \`scale\` = 1, \`rotation\` = 0
    WHERE \`steamid\` = ?;
UPDATE \`player_cosmetics\` SET \`item_key\` = '' WHERE \`steamid\` = ?`;

export const createWeaponPaintsConnectionOptions = (env: Record<string, string | undefined>) => {
    const database = String(env.WEAPONPAINTS_DB_NAME || DATABASE_NAME).trim();
    if (database !== DATABASE_NAME) throw new Error(`WEAPONPAINTS_DB_NAME 必须为 ${DATABASE_NAME}，禁止连接旧换肤数据库。`);
    return {
        host: String(env.WEAPONPAINTS_DB_HOST || '127.0.0.1'),
        port: Math.max(1, Number(env.WEAPONPAINTS_DB_PORT || 3306)),
        user: String(env.WEAPONPAINTS_DB_USER || ''),
        password: String(env.WEAPONPAINTS_DB_PASSWORD || ''),
        database,
        charset: 'utf8mb4',
        connectionLimit: Math.max(1, Number(env.WEAPONPAINTS_DB_POOL_SIZE || 5)),
        decimalNumbers: true,
    };
};

const AUDIT_SQL = `INSERT INTO \`web_audit_log\`
    (\`actor_player_id\`, \`actor_role\`, \`target_steamid\`, \`action\`, \`details_json\`)
    VALUES (?, ?, ?, ?, ?)`;

const insertAudit = async (connection: Pool | PoolConnection, entry: SkinAuditEntry) => {
    await connection.execute(AUDIT_SQL, [
        entry.actorPlayerId,
        entry.actorRole,
        entry.targetSteamId,
        entry.action,
        JSON.stringify(entry.details || {}),
    ]);
};

export const buildTeamCopyStatements = (
    steamId: string,
    fromTeam: 2 | 3,
    toTeam: 2 | 3,
    rules: TeamCopyRules,
): Array<{ sql: string; params: Array<string | number> }> => {
    const excludedPlaceholders = rules.excludedWeaponDefIndexes.map(() => '?').join(',') || 'NULL';
    const stickerPlaceholders = rules.stickerEligibleWeaponDefIndexes.map(() => '?').join(',') || 'NULL';
    return [
        {
            sql: `INSERT INTO \`weapon_loadouts\`
                 (\`steamid\`,\`team\`,\`weapon_defindex\`,\`paint_id\`,\`wear\`,\`seed\`,\`name_tag\`,\`stattrak_enabled\`,\`stattrak_count\`,\`keychain_id\`,\`keychain_offset_x\`,\`keychain_offset_y\`,\`keychain_offset_z\`,\`keychain_seed\`)
                 SELECT \`steamid\`,?,\`weapon_defindex\`,\`paint_id\`,\`wear\`,\`seed\`,\`name_tag\`,\`stattrak_enabled\`,\`stattrak_count\`,\`keychain_id\`,\`keychain_offset_x\`,\`keychain_offset_y\`,\`keychain_offset_z\`,\`keychain_seed\`
                 FROM \`weapon_loadouts\` WHERE \`steamid\`=? AND \`team\`=?
                   AND \`weapon_defindex\` NOT IN (${excludedPlaceholders})
                 ON DUPLICATE KEY UPDATE \`paint_id\`=VALUES(\`paint_id\`),\`wear\`=VALUES(\`wear\`),\`seed\`=VALUES(\`seed\`),\`name_tag\`=VALUES(\`name_tag\`),\`stattrak_enabled\`=VALUES(\`stattrak_enabled\`),\`stattrak_count\`=VALUES(\`stattrak_count\`),\`keychain_id\`=VALUES(\`keychain_id\`),\`keychain_offset_x\`=VALUES(\`keychain_offset_x\`),\`keychain_offset_y\`=VALUES(\`keychain_offset_y\`),\`keychain_offset_z\`=VALUES(\`keychain_offset_z\`),\`keychain_seed\`=VALUES(\`keychain_seed\`)`,
            params: [toTeam, steamId, fromTeam, ...rules.excludedWeaponDefIndexes],
        },
        {
            sql: `INSERT INTO \`weapon_stickers\` (\`steamid\`,\`team\`,\`weapon_defindex\`,\`slot\`,\`sticker_id\`,\`sticker_schema\`,\`offset_x\`,\`offset_y\`,\`wear\`,\`scale\`,\`rotation\`)
                 SELECT \`steamid\`,?,\`weapon_defindex\`,\`slot\`,\`sticker_id\`,\`sticker_schema\`,\`offset_x\`,\`offset_y\`,\`wear\`,\`scale\`,\`rotation\`
                 FROM \`weapon_stickers\` WHERE \`steamid\`=? AND \`team\`=?
                   AND \`weapon_defindex\` IN (${stickerPlaceholders})
                 ON DUPLICATE KEY UPDATE \`sticker_id\`=VALUES(\`sticker_id\`),\`sticker_schema\`=VALUES(\`sticker_schema\`),\`offset_x\`=VALUES(\`offset_x\`),\`offset_y\`=VALUES(\`offset_y\`),\`wear\`=VALUES(\`wear\`),\`scale\`=VALUES(\`scale\`),\`rotation\`=VALUES(\`rotation\`)`,
            params: [toTeam, steamId, fromTeam, ...rules.stickerEligibleWeaponDefIndexes],
        },
        {
            sql: `INSERT INTO \`player_cosmetics\` (\`steamid\`,\`team\`,\`kind\`,\`item_key\`)
                 SELECT \`steamid\`,?,\`kind\`,\`item_key\` FROM \`player_cosmetics\`
                 WHERE \`steamid\`=? AND \`team\`=? AND \`kind\` <> 'Agent'
                 ON DUPLICATE KEY UPDATE \`item_key\`=VALUES(\`item_key\`)`,
            params: [toTeam, steamId, fromTeam],
        },
    ];
};

export class MySqlLoadoutRepository implements LoadoutRepository {
    private readonly pool: Pool;

    constructor(options: ReturnType<typeof createWeaponPaintsConnectionOptions>) {
        if (!options.user) throw new Error('未配置 WEAPONPAINTS_DB_USER。');
        this.pool = mysql.createPool(options);
    }

    async initialize(): Promise<void> {
        await this.pool.execute(WEAPONPAINTS_WEB_SCHEMA_SQL);
    }

    async health(): Promise<{ ok: boolean; error?: string }> {
        try {
            await this.pool.query('SELECT 1');
            const [rows] = await this.pool.query<RowDataPacket[]>(
                `SELECT COUNT(*) AS table_count FROM information_schema.tables
                 WHERE table_schema = ? AND table_name IN
                 ('weapon_loadouts', 'weapon_stickers', 'player_cosmetics', 'web_audit_log')`,
                [DATABASE_NAME],
            );
            const count = Number(rows[0]?.table_count || 0);
            return count === 4 ? { ok: true } : { ok: false, error: `换肤数据库表不完整（${count}/4）。` };
        } catch (error) {
            return { ok: false, error: error instanceof Error ? error.message : '数据库连接失败。' };
        }
    }

    async load(steamId: string): Promise<PlayerLoadout> {
        const [weaponRows] = await this.pool.query<RowDataPacket[]>(
            `SELECT \`team\`, \`weapon_defindex\`, \`paint_id\`, \`wear\`, \`seed\`, \`name_tag\`,
                    \`stattrak_enabled\`, \`stattrak_count\`, \`keychain_id\`, \`keychain_offset_x\`,
                    \`keychain_offset_y\`, \`keychain_offset_z\`, \`keychain_seed\`
             FROM \`weapon_loadouts\` WHERE \`steamid\` = ? ORDER BY \`team\`, \`weapon_defindex\``,
            [steamId],
        );
        const [stickerRows] = await this.pool.query<RowDataPacket[]>(
            `SELECT \`team\`, \`weapon_defindex\`, \`slot\`, \`sticker_id\`, \`sticker_schema\`,
                    \`offset_x\`, \`offset_y\`, \`wear\`, \`scale\`, \`rotation\`
             FROM \`weapon_stickers\` WHERE \`steamid\` = ? ORDER BY \`team\`, \`weapon_defindex\`, \`slot\``,
            [steamId],
        );
        const stickersByWeapon = new Map<string, RowDataPacket[]>();
        for (const row of stickerRows) {
            const key = `${row.team}:${row.weapon_defindex}`;
            const list = stickersByWeapon.get(key) || [];
            list.push(row);
            stickersByWeapon.set(key, list);
        }
        const weapons = weaponRows.map((row) => ({
            team: Number(row.team) as 2 | 3,
            weaponDefIndex: Number(row.weapon_defindex),
            paintId: Number(row.paint_id),
            wear: Number(row.wear),
            seed: Number(row.seed),
            nameTag: String(row.name_tag || ''),
            statTrakEnabled: !!row.stattrak_enabled,
            statTrakCount: Number(row.stattrak_count),
            keychain: Number(row.keychain_id) ? {
                id: Number(row.keychain_id), offsetX: Number(row.keychain_offset_x),
                offsetY: Number(row.keychain_offset_y), offsetZ: Number(row.keychain_offset_z), seed: Number(row.keychain_seed),
            } : undefined,
            stickers: (stickersByWeapon.get(`${row.team}:${row.weapon_defindex}`) || [])
                .filter((sticker) => Number(sticker.sticker_id) !== 0)
                .map((sticker) => ({
                    slot: Number(sticker.slot), id: Number(sticker.sticker_id), schema: Number(sticker.sticker_schema),
                    offsetX: Number(sticker.offset_x), offsetY: Number(sticker.offset_y), wear: Number(sticker.wear),
                    scale: Number(sticker.scale), rotation: Number(sticker.rotation),
                })),
        }));
        const [cosmeticRows] = await this.pool.query<RowDataPacket[]>(
            `SELECT \`team\`, \`kind\`, \`item_key\` FROM \`player_cosmetics\`
             WHERE \`steamid\` = ? ORDER BY \`team\`, \`kind\``,
            [steamId],
        );
        return {
            steamId,
            weapons,
            cosmetics: cosmeticRows.map((row) => ({
                team: Number(row.team) as 2 | 3,
                kind: String(row.kind) as CosmeticUpdate['kind'],
                itemKey: String(row.item_key || ''),
            })),
        };
    }

    async saveWeapon(steamId: string, update: WeaponUpdate, audit: SkinAuditEntry): Promise<void> {
        await this.transaction(async (connection) => {
            const keychain = update.keychain;
            await connection.execute(
                `INSERT INTO \`weapon_loadouts\`
                 (\`steamid\`, \`team\`, \`weapon_defindex\`, \`paint_id\`, \`wear\`, \`seed\`, \`name_tag\`,
                  \`stattrak_enabled\`, \`stattrak_count\`, \`keychain_id\`, \`keychain_offset_x\`,
                  \`keychain_offset_y\`, \`keychain_offset_z\`, \`keychain_seed\`)
                 VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                 ON DUPLICATE KEY UPDATE \`paint_id\`=VALUES(\`paint_id\`), \`wear\`=VALUES(\`wear\`),
                  \`seed\`=VALUES(\`seed\`), \`name_tag\`=VALUES(\`name_tag\`),
                  \`stattrak_enabled\`=VALUES(\`stattrak_enabled\`), \`stattrak_count\`=VALUES(\`stattrak_count\`),
                  \`keychain_id\`=VALUES(\`keychain_id\`), \`keychain_offset_x\`=VALUES(\`keychain_offset_x\`),
                  \`keychain_offset_y\`=VALUES(\`keychain_offset_y\`), \`keychain_offset_z\`=VALUES(\`keychain_offset_z\`),
                  \`keychain_seed\`=VALUES(\`keychain_seed\`)`,
                [steamId, update.team, update.weaponDefIndex, update.paintId, update.wear, update.seed, update.nameTag,
                    update.statTrakEnabled ? 1 : 0, update.statTrakCount, keychain?.id || 0, keychain?.offsetX || 0,
                    keychain?.offsetY || 0, keychain?.offsetZ || 0, keychain?.seed || 0],
            );
            const stickers = new Map(update.stickers.map((sticker) => [sticker.slot, sticker]));
            for (let slot = 0; slot < 5; slot++) {
                const sticker = stickers.get(slot);
                await connection.execute(
                    `INSERT INTO \`weapon_stickers\`
                     (\`steamid\`, \`team\`, \`weapon_defindex\`, \`slot\`, \`sticker_id\`, \`sticker_schema\`,
                      \`offset_x\`, \`offset_y\`, \`wear\`, \`scale\`, \`rotation\`)
                     VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                     ON DUPLICATE KEY UPDATE \`sticker_id\`=VALUES(\`sticker_id\`), \`sticker_schema\`=VALUES(\`sticker_schema\`),
                      \`offset_x\`=VALUES(\`offset_x\`), \`offset_y\`=VALUES(\`offset_y\`), \`wear\`=VALUES(\`wear\`),
                      \`scale\`=VALUES(\`scale\`), \`rotation\`=VALUES(\`rotation\`)`,
                    [steamId, update.team, update.weaponDefIndex, slot, sticker?.id || 0, sticker?.schema || 0,
                        sticker?.offsetX || 0, sticker?.offsetY || 0, sticker?.wear || 0, sticker?.scale ?? 1, sticker?.rotation || 0],
                );
            }
            await insertAudit(connection, audit);
        });
    }

    async saveCosmetic(steamId: string, update: CosmeticUpdate, audit: SkinAuditEntry): Promise<void> {
        await this.transaction(async (connection) => {
            await connection.execute(
                `INSERT INTO \`player_cosmetics\` (\`steamid\`, \`team\`, \`kind\`, \`item_key\`)
                 VALUES (?, ?, ?, ?) ON DUPLICATE KEY UPDATE \`item_key\`=VALUES(\`item_key\`)`,
                [steamId, update.team, update.kind, update.itemKey],
            );
            await insertAudit(connection, audit);
        });
    }

    async copyTeam(
        steamId: string,
        fromTeam: 2 | 3,
        toTeam: 2 | 3,
        rules: TeamCopyRules,
        audit: SkinAuditEntry,
    ): Promise<void> {
        await this.transaction(async (connection) => {
            await this.resetRows(connection, steamId, toTeam);
            for (const statement of buildTeamCopyStatements(steamId, fromTeam, toTeam, rules)) {
                await connection.execute(statement.sql, statement.params);
            }
            await insertAudit(connection, audit);
        });
    }

    async reset(steamId: string, team: 2 | 3 | undefined, audit: SkinAuditEntry): Promise<void> {
        await this.transaction(async (connection) => {
            await this.resetRows(connection, steamId, team);
            await insertAudit(connection, audit);
        });
    }

    async audit(entry: SkinAuditEntry): Promise<void> {
        await insertAudit(this.pool, entry);
    }

    private async resetRows(connection: PoolConnection, steamId: string, team?: 2 | 3) {
        const suffix = team ? ' AND `team` = ?' : '';
        const params = team ? [steamId, team] : [steamId];
        await connection.execute(
            `UPDATE \`weapon_loadouts\` SET \`paint_id\`=0,\`wear\`=0,\`seed\`=0,\`name_tag\`='',\`stattrak_enabled\`=0,\`stattrak_count\`=0,\`keychain_id\`=0,\`keychain_offset_x\`=0,\`keychain_offset_y\`=0,\`keychain_offset_z\`=0,\`keychain_seed\`=0 WHERE \`steamid\` = ?${suffix}`,
            params,
        );
        await connection.execute(
            `UPDATE \`weapon_stickers\` SET \`sticker_id\`=0,\`sticker_schema\`=0,\`offset_x\`=0,\`offset_y\`=0,\`wear\`=0,\`scale\`=1,\`rotation\`=0 WHERE \`steamid\` = ?${suffix}`,
            params,
        );
        await connection.execute(`UPDATE \`player_cosmetics\` SET \`item_key\`='' WHERE \`steamid\` = ?${suffix}`, params);
    }

    private async transaction(work: (connection: PoolConnection) => Promise<void>) {
        const connection = await this.pool.getConnection();
        try {
            await connection.beginTransaction();
            await work(connection);
            await connection.commit();
        } catch (error) {
            await connection.rollback();
            throw error;
        } finally {
            connection.release();
        }
    }
}
