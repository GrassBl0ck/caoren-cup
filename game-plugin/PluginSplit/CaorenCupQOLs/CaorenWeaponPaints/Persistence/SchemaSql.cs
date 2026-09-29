namespace CaorenCup.WeaponPaints.Persistence;

public static class SchemaSql
{
    public const int CurrentVersion = 1;

    public static readonly IReadOnlyList<string> CreateStatements =
    [
        """
        CREATE TABLE IF NOT EXISTS `schema_info` (
            `component` VARCHAR(64) NOT NULL,
            `version` INT UNSIGNED NOT NULL,
            `updated_at` TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            PRIMARY KEY (`component`)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS `weapon_loadouts` (
            `steamid` VARCHAR(18) NOT NULL,
            `team` TINYINT UNSIGNED NOT NULL,
            `weapon_defindex` SMALLINT UNSIGNED NOT NULL,
            `paint_id` INT UNSIGNED NOT NULL DEFAULT 0,
            `wear` DECIMAL(10,6) NOT NULL DEFAULT 0,
            `seed` SMALLINT UNSIGNED NOT NULL DEFAULT 0,
            `name_tag` VARCHAR(128) NOT NULL DEFAULT '',
            `stattrak_enabled` TINYINT(1) UNSIGNED NOT NULL DEFAULT 0,
            `stattrak_count` INT NOT NULL DEFAULT 0,
            `keychain_id` INT UNSIGNED NOT NULL DEFAULT 0,
            `keychain_offset_x` FLOAT NOT NULL DEFAULT 0,
            `keychain_offset_y` FLOAT NOT NULL DEFAULT 0,
            `keychain_offset_z` FLOAT NOT NULL DEFAULT 0,
            `keychain_seed` INT UNSIGNED NOT NULL DEFAULT 0,
            `updated_at` TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            PRIMARY KEY (`steamid`, `team`, `weapon_defindex`)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS `weapon_stickers` (
            `steamid` VARCHAR(18) NOT NULL,
            `team` TINYINT UNSIGNED NOT NULL,
            `weapon_defindex` SMALLINT UNSIGNED NOT NULL,
            `slot` TINYINT UNSIGNED NOT NULL,
            `sticker_id` INT UNSIGNED NOT NULL DEFAULT 0,
            `sticker_schema` INT UNSIGNED NOT NULL DEFAULT 0,
            `offset_x` FLOAT NOT NULL DEFAULT 0,
            `offset_y` FLOAT NOT NULL DEFAULT 0,
            `wear` FLOAT NOT NULL DEFAULT 0,
            `scale` FLOAT NOT NULL DEFAULT 1,
            `rotation` FLOAT NOT NULL DEFAULT 0,
            `updated_at` TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            PRIMARY KEY (`steamid`, `team`, `weapon_defindex`, `slot`)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS `player_cosmetics` (
            `steamid` VARCHAR(18) NOT NULL,
            `team` TINYINT UNSIGNED NOT NULL,
            `kind` VARCHAR(16) NOT NULL,
            `item_key` VARCHAR(128) NOT NULL DEFAULT '',
            `updated_at` TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            PRIMARY KEY (`steamid`, `team`, `kind`)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """
    ];

    public const string UpsertSchemaVersion =
        "INSERT INTO `schema_info` (`component`, `version`) VALUES ('caoren_weaponpaints', @Version) " +
        "ON DUPLICATE KEY UPDATE `version` = VALUES(`version`)";

    public const string UpsertWeapon =
        """
        INSERT INTO `weapon_loadouts`
        (`steamid`, `team`, `weapon_defindex`, `paint_id`, `wear`, `seed`, `name_tag`,
         `stattrak_enabled`, `stattrak_count`, `keychain_id`, `keychain_offset_x`,
         `keychain_offset_y`, `keychain_offset_z`, `keychain_seed`)
        VALUES
        (@SteamId, @Team, @WeaponDefIndex, @PaintId, @Wear, @Seed, @NameTag,
         @StatTrakEnabled, @StatTrakCount, @KeychainId, @KeychainOffsetX,
         @KeychainOffsetY, @KeychainOffsetZ, @KeychainSeed)
        ON DUPLICATE KEY UPDATE
        `paint_id` = VALUES(`paint_id`), `wear` = VALUES(`wear`), `seed` = VALUES(`seed`),
        `name_tag` = VALUES(`name_tag`), `stattrak_enabled` = VALUES(`stattrak_enabled`),
        `stattrak_count` = VALUES(`stattrak_count`), `keychain_id` = VALUES(`keychain_id`),
        `keychain_offset_x` = VALUES(`keychain_offset_x`),
        `keychain_offset_y` = VALUES(`keychain_offset_y`),
        `keychain_offset_z` = VALUES(`keychain_offset_z`), `keychain_seed` = VALUES(`keychain_seed`)
        """;

    public const string UpsertSticker =
        """
        INSERT INTO `weapon_stickers`
        (`steamid`, `team`, `weapon_defindex`, `slot`, `sticker_id`, `sticker_schema`,
         `offset_x`, `offset_y`, `wear`, `scale`, `rotation`)
        VALUES
        (@SteamId, @Team, @WeaponDefIndex, @Slot, @StickerId, @StickerSchema,
         @OffsetX, @OffsetY, @Wear, @Scale, @Rotation)
        ON DUPLICATE KEY UPDATE
        `sticker_id` = VALUES(`sticker_id`), `sticker_schema` = VALUES(`sticker_schema`),
        `offset_x` = VALUES(`offset_x`), `offset_y` = VALUES(`offset_y`),
        `wear` = VALUES(`wear`), `scale` = VALUES(`scale`), `rotation` = VALUES(`rotation`)
        """;

    public const string UpsertCosmetic =
        """
        INSERT INTO `player_cosmetics` (`steamid`, `team`, `kind`, `item_key`)
        VALUES (@SteamId, @Team, @Kind, @ItemKey)
        ON DUPLICATE KEY UPDATE `item_key` = VALUES(`item_key`)
        """;
}
