using System.Text.RegularExpressions;

namespace CaorenCupPlugin;

public static class GameModeCommandPolicy
{
    private static readonly HashSet<string> AllowedModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "competitive",
        "rush",
        "duel",
        "ability",
        "undercover",
        "unbalanced",
    };

    private static readonly HashSet<string> AllowedCvars = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp_freezetime",
        "mp_roundtime_defuse",
        "mp_round_restart_delay",
        "mp_maxrounds",
        "mp_halftime",
        "mp_warmuptime",
        "mp_warmup_pausetimer",
        "mp_startmoney",
        "mp_buytime",
        "mp_buy_anywhere",
        "mp_autoteambalance",
        "mp_limitteams",
    };

    private static readonly Regex SafeValue = new(@"\A[A-Za-z0-9_.-]+\z", RegexOptions.CultureInvariant);

    public static bool IsAllowedMode(string modeId) => AllowedModes.Contains(modeId);

    public static bool IsAllowedCvar(string name, string value) =>
        AllowedCvars.Contains(name) && SafeValue.IsMatch(value);
}
