using System.Text.RegularExpressions;

namespace CaorenCupPlugin;

public enum LobbyReminderMode
{
    Off,
    Remind,
    RequireBinding
}

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

    public static bool TryParseMode(string? input, out LobbyReminderMode mode)
    {
        if (input?.Trim() == "2")
        {
            mode = LobbyReminderMode.RequireBinding;
            return true;
        }

        var valid = TryParseEnabled(input, out var enabled);
        mode = enabled ? LobbyReminderMode.Remind : LobbyReminderMode.Off;
        return valid;
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
