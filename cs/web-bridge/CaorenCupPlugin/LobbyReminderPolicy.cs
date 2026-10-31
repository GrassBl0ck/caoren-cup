using System.Text.RegularExpressions;

namespace CaorenCupPlugin;

public static partial class LobbyReminderPolicy
{
    public static bool ShouldRemind(
        bool remindersEnabled,
        bool isRealPlayer,
        string steamId,
        bool hasSuccessfulSync,
        DateTimeOffset lastSuccessfulSync,
        DateTimeOffset now,
        TimeSpan maxAge,
        IReadOnlySet<string> lobbySteamIds)
    {
        if (!remindersEnabled || !isRealPlayer || !hasSuccessfulSync || maxAge < TimeSpan.Zero) return false;
        var normalizedSteamId = steamId ?? string.Empty;
        if (!SteamId64Pattern().IsMatch(normalizedSteamId)) return false;
        if (now - lastSuccessfulSync > maxAge) return false;
        return !lobbySteamIds.Contains(normalizedSteamId);
    }

    public static bool TryParseEnabled(string? input, out bool enabled)
    {
        switch (input?.Trim().ToLowerInvariant())
        {
            case "on":
            case "1":
                enabled = true;
                return true;
            case "off":
            case "0":
                enabled = false;
                return true;
            default:
                enabled = false;
                return false;
        }
    }

    [GeneratedRegex("^7656119\\d{10}$", RegexOptions.CultureInvariant)]
    private static partial Regex SteamId64Pattern();
}
