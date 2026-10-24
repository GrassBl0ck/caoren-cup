using CounterStrikeSharp.API.Core;

namespace Caoren;

/// <summary>
/// 席位身份键：真人为 SteamID，bot 为 <c>bot:名字</c>。
/// bot 的 SteamID 恒为 0，既不能区分多个 bot，也不能作为持久化键，因此单独构造。
/// </summary>
public static class AbilityIdentity
{
    public const string BotPrefix = "bot:";

    /// <summary>按玩家生成身份键；真人用 SteamID，bot 用名字。</summary>
    public static string KeyFor(CCSPlayerController player) =>
        player.IsBot ? BotPrefix + player.PlayerName : player.SteamID.ToString();

    public static bool IsBotKey(string? key) =>
        key is not null && key.StartsWith(BotPrefix, StringComparison.Ordinal);

    /// <summary>席位键是否有效：合法 SteamID，或带名字的 bot 键。</summary>
    public static bool IsValidSeatKey(string? key) =>
        !string.IsNullOrWhiteSpace(key)
        && (IsBotKey(key) ? key!.Trim().Length > BotPrefix.Length : RegexHelpers.IsSteamId(key));
}
