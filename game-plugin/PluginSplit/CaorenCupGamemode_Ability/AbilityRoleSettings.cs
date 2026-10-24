using System.Text.Json.Serialization;

namespace CaorenCup.Gamemodes.Ability;

/// <summary>异能职业开关。第一版正式放行 6 个职业，其余保持关闭，等待原型门禁验收。</summary>
public sealed class AbilityRoleSettings
{
    [JsonPropertyName("medicEnabled")]
    public bool MedicEnabled { get; set; } = true;

    [JsonPropertyName("berserkerEnabled")]
    public bool BerserkerEnabled { get; set; } = true;

    [JsonPropertyName("tankEnabled")]
    public bool TankEnabled { get; set; } = true;

    [JsonPropertyName("capitalistEnabled")]
    public bool CapitalistEnabled { get; set; } = true;

    [JsonPropertyName("balanceEnabled")]
    public bool BalanceEnabled { get; set; } = true;

    [JsonPropertyName("commanderEnabled")]
    public bool CommanderEnabled { get; set; } = true;

    [JsonPropertyName("assassinEnabled")]
    public bool AssassinEnabled { get; set; } = false;

    [JsonPropertyName("glassCannonEnabled")]
    public bool GlassCannonEnabled { get; set; } = false;

    [JsonPropertyName("utilitySpecialistEnabled")]
    public bool UtilitySpecialistEnabled { get; set; } = false;

    [JsonPropertyName("skyCourierEnabled")]
    public bool SkyCourierEnabled { get; set; } = false;

    [JsonPropertyName("snowGolemEnabled")]
    public bool SnowGolemEnabled { get; set; } = false;

    [JsonPropertyName("witchEnabled")]
    public bool WitchEnabled { get; set; } = false;
}
