namespace CaorenCupPlugin;

public static class BridgeServerCommandPolicy
{
    private static string CommandName(string? command) =>
        (command ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

    public static bool IsAllowed(string? command)
    {
        return CaorenCupPlugin.AllowedBridgeServerCommands.Contains(CommandName(command));
    }

    public static bool ShouldBroadcast(string? command) =>
        !string.Equals(CommandName(command), "wp_refresh", StringComparison.OrdinalIgnoreCase);
}
