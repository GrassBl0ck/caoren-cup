namespace CaorenCupPlugin;

using System.Text.RegularExpressions;

public static class BridgeServerCommandPolicy
{
    private static readonly Regex WeaponPaintsRefreshCommand = new(
        @"\Awp_refresh 7656119[0-9]{10}(?: safe)?\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string CommandName(string? command) =>
        (command ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

    public static bool IsAllowed(string? command)
    {
        var commandName = CommandName(command);
        if (string.Equals(commandName, "wp_refresh", StringComparison.OrdinalIgnoreCase))
        {
            return WeaponPaintsRefreshCommand.IsMatch(command ?? string.Empty);
        }
        return CaorenCupPlugin.AllowedBridgeServerCommands.Contains(commandName);
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
