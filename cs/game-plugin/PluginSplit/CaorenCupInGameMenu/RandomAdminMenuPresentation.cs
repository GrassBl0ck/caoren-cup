using CaorenCup.Contracts;

namespace CaorenCup.Features.InGameMenu;

/// <summary>管理员巡查只展示快照；不引用任何交易入口。</summary>
public static class RandomAdminMenuPresentation
{
    public static string[] PageKeys(IEnumerable<string> current, IReadOnlyList<string> displayed, int page, bool preserveSlots) =>
        preserveSlots ? displayed.ToArray() : current.Skip(Math.Max(0, page) * 6).Take(6).ToArray();

    public static string Presence(RandomAdminPlayerView player) =>
        $"{(player.Connected ? "在线" : "离线")} · {player.Team switch { 2 => "T", 3 => "CT", _ => "旁观 / 未分队" }} · {(player.Participating ? "本局参赛" : "未参赛")}";

    public static string PlayerRow(RandomAdminPlayerView player) =>
        $"{player.Name} · {(player.Connected ? "在线" : "离线")} · 强化券 {player.Coupons} · BUFF {player.Owned.Select(buff => buff.Key).Distinct(StringComparer.Ordinal).Count()}";

    public static string RoundUsage(RandomAdminPlayerView player) =>
        $"本回合：购买{Used(player.PurchasedThisRound)} · 重抽{Used(player.RerolledThisRound)} · 兑换{Used(player.ExchangedThisRound)}";

    public static string BuffRow(RandomBuffView buff) =>
        $"{RandomMenuPresentation.CardTitle(buff)} · {RandomMenuPresentation.QualityName(buff.Quality)} / {buff.Category}";

    private static string Used(bool used) => used ? "已用" : "未用";
}
