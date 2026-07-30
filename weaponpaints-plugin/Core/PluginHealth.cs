namespace CaorenCup.WeaponPaints.Core;

public enum HealthLevel
{
    Disabled,
    Healthy,
    Degraded,
    Unhealthy
}

public sealed class PluginHealth
{
    public PluginHealth(
        bool enabled,
        bool databaseConnected,
        bool schemaReady,
        bool catalogReady,
        bool gameDataReady)
    {
        Enabled = enabled;
        DatabaseConnected = databaseConnected;
        SchemaReady = schemaReady;
        CatalogReady = catalogReady;
        GameDataReady = gameDataReady;
    }

    public bool Enabled { get; }
    public bool DatabaseConnected { get; }
    public bool SchemaReady { get; }
    public bool CatalogReady { get; }
    public bool GameDataReady { get; }

    public HealthLevel Level => !Enabled
        ? HealthLevel.Disabled
        : !SchemaReady || !CatalogReady || !GameDataReady
            ? HealthLevel.Unhealthy
            : !DatabaseConnected
                ? HealthLevel.Degraded
                : HealthLevel.Healthy;

    public static PluginHealth Disabled() => new(false, false, false, false, false);

    public string ToChineseSummary()
    {
        return $"状态={Level}; 总开关={(Enabled ? "开" : "关")}; " +
               $"数据库={(DatabaseConnected ? "正常" : "异常")}; " +
               $"表结构={(SchemaReady ? "正常" : "异常")}; " +
               $"物品数据={(CatalogReady ? "正常" : "异常")}; " +
               $"gamedata={(GameDataReady ? "正常" : "异常")}";
    }
}
