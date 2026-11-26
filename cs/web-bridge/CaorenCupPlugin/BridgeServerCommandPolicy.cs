namespace CaorenCupPlugin;

using System.Globalization;
using System.Text.RegularExpressions;

public static class BridgeServerCommandPolicy
{
    private static readonly Regex WeaponPaintsRefreshCommand = new(
        @"\Awp_refresh 7656119[0-9]{10}(?: safe)?\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string CommandName(string? command) =>
        (command ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

    public static Uri ParseBackendAddress(string? value)
    {
        if (!Uri.TryCreate(value?.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && (uri.Scheme != Uri.UriSchemeHttp || !uri.IsLoopback))
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
        {
            throw new InvalidOperationException("Bridge backend requires HTTPS or loopback HTTP, without embedded credentials.");
        }
        return uri;
    }

    private static readonly Regex MapName = new(@"\A[a-z0-9_-]{1,128}\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex QuizTypes = new(@"\A[1-7]{1,16}\z");
    private static readonly Regex PositiveInteger = new(@"\A[1-9][0-9]{0,19}\z");

    private static bool Number(string arg) =>
        double.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value);
    private static bool Team(string arg) => arg is "all" or "ct" or "t";
    private static bool Flag(string arg) => arg is "0" or "1";
    private static bool OptionalNumber(string arg) => arg == "-" || Number(arg);

    public static bool IsAllowed(string? command)
    {
        if (string.IsNullOrWhiteSpace(command) || command.Length > 256 ||
            command.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not (' ' or '_' or '.' or '+' or '-')))
            return false;

        var parts = command.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (!CaorenCupPlugin.AllowedBridgeServerCommands.Contains(parts[0])) return false;
        var args = parts.AsSpan(1);

        return parts[0] switch
        {
            "wp_refresh" => WeaponPaintsRefreshCommand.IsMatch(command),
            "reset_plu" or "mp_warmup_start" or "mp_warmup_end" => args is [],
            "changelevel" => args is [var map] && MapName.IsMatch(map),
            "host_workshop_map" => args is [var workshopId] && PositiveInteger.IsMatch(workshopId),
            "css_hpcap" => args is [var min, var max] && Number(min) && Number(max),
            "css_bq" => args is ["0"] || (args is [var types, var duration] && QuizTypes.IsMatch(types) && Number(duration))
                || (args is [var delayedTypes, var delayedDuration, var delay] && QuizTypes.IsMatch(delayedTypes) && Number(delayedDuration) && Number(delay)),
            "css_kh" => args is ["0"] || (args is [var target, var amount] && (Team(target) || target == "vip") && Number(amount)),
            "css_armor" or "css_fov" or "css_incdmg" or "css_fh" =>
                args is ["0"] || (args is [var team, var value] && Team(team) && Number(value)),
            "css_ammo" or "css_bleed" or "css_lhimm" or "css_wspd" or "css_magic" =>
                args is ["0"] || (args is [var team, var first, var second] && Team(team) && Number(first) && Number(second)),
            "css_cash" or "css_ffire" or "css_esp" =>
                args is ["0"] || (args is [var team, var value, var mode] && Team(team) && Number(value) && Flag(mode)),
            "css_aura" =>
                args is ["0"] || (args is [var team, var allow, var decay, var damage] && Team(team) && Flag(allow) && Number(decay) && Number(damage)),
            "css_dj" => args is ["0"] || (args is [var team, var jumps, var force, "true" or "false"] && Team(team) && Number(jumps) && Number(force)),
            "css_dmg" =>
                args is ["0"] || (args is [var team, var multiplier, var cap, var window] && Team(team) && OptionalNumber(multiplier) && Number(cap) && Number(window)),
            "css_smoke" => args is ["0"] || (args is [var team, var duration, var hp] && Team(team) && OptionalNumber(duration) && Number(hp)),
            "css_kb" =>
                args is ["0"] || (args is [var team, var horizontal, var vertical, var friendly, var multiplier] &&
                    Team(team) && Number(horizontal) && Number(vertical) && Flag(friendly) && Number(multiplier)),
            "css_tag" => args is ["0"] || (args is [var team, var scale] && Team(team) && (scale is "df" or "default" || Number(scale))),
            "mp_maxrounds" or "mp_winlimit" or "mp_roundtime" or "mp_freezetime" or "mp_round_restart_delay"
                or "mp_match_can_clinch" or "mp_free_armor" or "mp_halftime" or "mp_autoteambalance"
                or "mp_limitteams" or "sv_showimpacts" or "sv_showimpacts_time" or "mp_warmuptime"
                or "mp_warmup_pausetimer" or "mp_restartgame" => args is [var number] && Number(number),
            _ => false,
        };
    }

    // 这些草人杯命令在实际结果路径中自行公示，桥接不能再报一次“已下发”。
    private static readonly HashSet<string> SelfAnnouncingCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "css_ammo", "css_armor", "css_aura", "css_cash", "css_dj", "css_fov", "css_hpcap", "reset_plu",
        "css_dmg", "css_incdmg", "css_bleed", "css_kh", "css_kb", "css_lhimm", "css_smoke", "css_esp",
        "css_ffire", "css_fh", "css_wspd", "css_tag", "css_magic", "css_bq",
    };

    public static bool ShouldBroadcast(string? command) =>
        !SelfAnnouncingCommands.Contains(CommandName(command)) &&
        !string.Equals(CommandName(command), "wp_refresh", StringComparison.OrdinalIgnoreCase);
}
