using CaorenCup.Contracts;

namespace CaorenCup.Features.InGameMenu;

public sealed record CombatHudItem(string Key, string Icon, string Quality, string Caption, string Name,
    string Hint, string Detail);

public sealed record CombatHudContent(string Mode, IReadOnlyList<CombatHudItem> Buffs, IReadOnlyList<CombatHudItem> Skills,
    CombatHudItem? Intel)
{
    public const int CompactBuffCount = 6;
    public bool Empty => Buffs.Count == 0 && Skills.Count == 0 && Intel is null;

    public static CombatHudContent FromRandom(RandomPlayerView player)
    {
        var states = player.Skills.ToDictionary(s => s.Key);
        var buffs = new List<CombatHudItem>();
        var skills = new List<CombatHudItem>();
        foreach (var buff in player.Owned.OrderByDescending(b => "DCBAS".IndexOf(b.Quality, StringComparison.Ordinal))
                     .ThenBy(b => CardNumber(b.DisplayId)).ThenBy(b => b.Key, StringComparer.Ordinal))
        {
            var level = $"Lv.{buff.Level}";
            var title = $"{buff.Name} {level}";
            var status = states.GetValueOrDefault(buff.Key)?.Status;
            var caption = buff.Active ? Join(title, status) : level;
            var item = new CombatHudItem(buff.Key, buff.Key, buff.Quality, caption, title,
                buff.SimpleDescription, Join(status, buff.DetailedDescription));
            (buff.Active ? skills : buffs).Add(item);
        }
        return new("random", buffs, skills, null);
    }

    public static CombatHudContent FromAbility(CombatAbilityView view)
    {
        var charge = $"充能 {view.Charge}";
        var info = Join(view.Preview, view.Countdown, view.Prompt);
        var skill = new CombatHudItem("role:" + view.Role, "", "", Join(view.Role, charge, view.Status, info),
            view.Role, Join(view.Effect, view.Status), Join(view.Effect, view.ChargeRule, charge, view.Status, info));
        var intel = string.IsNullOrWhiteSpace(view.Intel) ? null : new CombatHudItem("intel", "", "",
            view.Intel, view.Role + " · 职业情报", view.Intel, view.Intel);
        return new("ability", [], [skill], intel);
    }

    private static string Join(params string?[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    private static int CardNumber(string id) => int.TryParse(id.AsSpan(id.LastIndexOf('-') + 1), out var n) ? n : int.MaxValue;
}
