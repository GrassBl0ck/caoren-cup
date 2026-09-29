using CounterStrikeSharp.API.Core;
using System.Text.Json.Serialization;

namespace CaorenCup.WeaponPaints;

internal sealed class WeaponPaintsFeatureConfig
{
    public bool SkinEnabled { get; set; } = true;
    public bool KnifeEnabled { get; set; } = true;
    public bool GiveRandomSkin { get; set; }
}

public sealed class CaorenWeaponPaintsConfig : BasePluginConfig
{
    [JsonPropertyName("ConfigVersion")]
    public override int Version { get; set; } = 1;

    public bool Enabled { get; set; } = true;
    public string DatabaseHost { get; set; } = "127.0.0.1";
    public int DatabasePort { get; set; } = 3306;
    public string DatabaseUser { get; set; } = string.Empty;
    public string DatabasePassword { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = "caoren_weaponpaints";
    public string AdminPermission { get; set; } = "@css/root";
    public int InputTimeoutSeconds { get; set; } = 60;
    public int RefreshCooldownSeconds { get; set; } = 3;
    public string Prefix { get; set; } = "[草人杯皮肤]";
    [JsonIgnore]
    internal WeaponPaintsFeatureConfig Additional { get; } = new();
}
