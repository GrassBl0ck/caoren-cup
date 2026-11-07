using CaorenCup.Contracts;
using CounterStrikeSharp.API.Core;

namespace CaorenCup.WeaponPaints.Core;

internal static class ChatMessageFormatter
{
    private static string Content(string message, string? configuredPrefix)
    {
        string label = Clean(configuredPrefix ?? string.Empty).Trim();
        string body = Clean(message).Trim();
        string labelPart = label.Length == 0 ? string.Empty : $" {label}";
        return $"{labelPart} {body}".Trim();
    }

    internal static string Private(string message, string? prefix) => CaorenCupChat.FormatPrivateMessage(Content(message, prefix));

    internal static void Reply(CCSPlayerController? player, string message, string? prefix) =>
        CaorenCupChat.Reply(player, Content(message, prefix));

    private static string Clean(string value) =>
        value.Replace("[草人杯皮肤]", "[皮肤]", StringComparison.Ordinal)
            .Replace("[草人杯]", string.Empty, StringComparison.Ordinal);
}
