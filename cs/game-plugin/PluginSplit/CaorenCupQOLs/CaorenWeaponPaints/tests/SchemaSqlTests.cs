using CaorenCup.WeaponPaints.Persistence;
using Xunit;

namespace CaorenCup.WeaponPaints.Tests;

public sealed class SchemaSqlTests
{
    [Fact]
    public void Schema_IsSafeForDedicatedNewDatabase()
    {
        var sql = string.Join("\n", SchemaSql.CreateStatements);

        Assert.Contains("schema_info", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("weapon_loadouts", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("weapon_stickers", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("player_cosmetics", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE DATABASE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CHECK (", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Upserts_UseMySql57Syntax()
    {
        Assert.Contains("ON DUPLICATE KEY UPDATE", SchemaSql.UpsertWeapon, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ON DUPLICATE KEY UPDATE", SchemaSql.UpsertSticker, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ON DUPLICATE KEY UPDATE", SchemaSql.UpsertCosmetic, StringComparison.OrdinalIgnoreCase);
    }
}
