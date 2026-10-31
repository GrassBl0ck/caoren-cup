using CaorenCup.WeaponPaints.Core;
using Dapper;
using MySqlConnector;

namespace CaorenCup.WeaponPaints.Persistence;

public sealed class MySqlLoadoutRepository : ILoadoutRepository
{
    private const string SelectWeapons =
        """
        SELECT `steamid` AS SteamId, `team` AS Team, `weapon_defindex` AS WeaponDefIndex,
               `paint_id` AS PaintId, `wear` AS Wear, `seed` AS Seed, `name_tag` AS NameTag,
               `stattrak_enabled` AS StatTrakEnabled, `stattrak_count` AS StatTrakCount,
               `keychain_id` AS KeychainId, `keychain_offset_x` AS KeychainOffsetX,
               `keychain_offset_y` AS KeychainOffsetY, `keychain_offset_z` AS KeychainOffsetZ,
               `keychain_seed` AS KeychainSeed
        FROM `weapon_loadouts` WHERE `steamid` = @SteamId
        """;

    private const string SelectStickers =
        """
        SELECT `steamid` AS SteamId, `team` AS Team, `weapon_defindex` AS WeaponDefIndex,
               `slot` AS Slot, `sticker_id` AS StickerId, `sticker_schema` AS StickerSchema,
               `offset_x` AS OffsetX, `offset_y` AS OffsetY, `wear` AS Wear,
               `scale` AS Scale, `rotation` AS Rotation
        FROM `weapon_stickers` WHERE `steamid` = @SteamId
        """;

    private const string SelectCosmetics =
        """
        SELECT `steamid` AS SteamId, `team` AS Team, `kind` AS Kind, `item_key` AS ItemKey
        FROM `player_cosmetics` WHERE `steamid` = @SteamId
        """;

    private readonly string _connectionString;

    public MySqlLoadoutRepository(string connectionString)
    {
        _connectionString = string.IsNullOrWhiteSpace(connectionString)
            ? throw new ArgumentException("数据库连接字符串不能为空。", nameof(connectionString))
            : connectionString;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        foreach (var statement in SchemaSql.CreateStatements)
        {
            await connection.ExecuteAsync(new CommandDefinition(statement, cancellationToken: cancellationToken))
                .ConfigureAwait(false);
        }

        await connection.ExecuteAsync(new CommandDefinition(
            SchemaSql.UpsertSchemaVersion,
            new { Version = SchemaSql.CurrentVersion },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<bool> ProbeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT 1",
            cancellationToken: cancellationToken)).ConfigureAwait(false) == 1;
    }

    public async Task<PlayerLoadout> LoadAsync(string steamId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var parameter = new { SteamId = steamId };
        var weapons = await connection.QueryAsync<WeaponRow>(new CommandDefinition(
            SelectWeapons, parameter, cancellationToken: cancellationToken)).ConfigureAwait(false);
        var stickers = await connection.QueryAsync<StickerRow>(new CommandDefinition(
            SelectStickers, parameter, cancellationToken: cancellationToken)).ConfigureAwait(false);
        var cosmetics = await connection.QueryAsync<CosmeticRow>(new CommandDefinition(
            SelectCosmetics, parameter, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return LoadoutRecordMapper.Map(steamId, weapons, stickers, cosmetics);
    }

    public async Task SaveWeaponAsync(
        string steamId,
        TeamSide team,
        WeaponSelection selection,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var keychain = selection.Keychain;
            await connection.ExecuteAsync(new CommandDefinition(
                SchemaSql.UpsertWeapon,
                new
                {
                    SteamId = steamId,
                    Team = (byte)team,
                    selection.WeaponDefIndex,
                    selection.PaintId,
                    selection.Wear,
                    selection.Seed,
                    selection.NameTag,
                    selection.StatTrakEnabled,
                    selection.StatTrakCount,
                    KeychainId = keychain?.Id ?? 0,
                    KeychainOffsetX = keychain?.OffsetX ?? 0,
                    KeychainOffsetY = keychain?.OffsetY ?? 0,
                    KeychainOffsetZ = keychain?.OffsetZ ?? 0,
                    KeychainSeed = keychain?.Seed ?? 0
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            foreach (var write in StickerWritePlan.For(selection))
            {
                await ExecuteStickerAsync(
                    connection,
                    transaction,
                    steamId,
                    team,
                    selection.WeaponDefIndex,
                    write.Slot,
                    write.Sticker,
                    cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async Task SaveStickerAsync(
        string steamId,
        TeamSide team,
        ushort weaponDefIndex,
        byte slot,
        StickerSelection sticker,
        CancellationToken cancellationToken = default)
    {
        if (slot > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteStickerAsync(
            connection,
            null,
            steamId,
            team,
            weaponDefIndex,
            slot,
            sticker,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveCosmeticAsync(
        string steamId,
        TeamSide team,
        CosmeticKind kind,
        string itemKey,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            SchemaSql.UpsertCosmetic,
            new { SteamId = steamId, Team = (byte)team, Kind = kind.ToString(), ItemKey = itemKey ?? string.Empty },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private async Task<MySqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new MySqlConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static Task<int> ExecuteStickerAsync(
        MySqlConnection connection,
        MySqlTransaction? transaction,
        string steamId,
        TeamSide team,
        ushort weaponDefIndex,
        byte slot,
        StickerSelection sticker,
        CancellationToken cancellationToken)
    {
        return connection.ExecuteAsync(new CommandDefinition(
            SchemaSql.UpsertSticker,
            new
            {
                SteamId = steamId,
                Team = (byte)team,
                WeaponDefIndex = weaponDefIndex,
                Slot = slot,
                StickerId = sticker.Id,
                StickerSchema = sticker.Schema,
                sticker.OffsetX,
                sticker.OffsetY,
                sticker.Wear,
                sticker.Scale,
                sticker.Rotation
            },
            transaction,
            cancellationToken: cancellationToken));
    }
}
