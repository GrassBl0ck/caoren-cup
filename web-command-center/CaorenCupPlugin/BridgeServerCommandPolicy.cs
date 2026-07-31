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

    public static bool ShouldBroadcast(string? command) =>
        !string.Equals(CommandName(command), "wp_refresh", StringComparison.OrdinalIgnoreCase);
}
