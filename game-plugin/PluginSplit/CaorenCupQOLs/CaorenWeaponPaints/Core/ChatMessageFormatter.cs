using CounterStrikeSharp.API.Modules.Utils;

namespace CaorenCup.WeaponPaints.Core;

internal static class ChatMessageFormatter
{
    internal static string Private(string message, string? configuredPrefix)
    {
        string label = Clean(configuredPrefix ?? string.Empty).Trim();
        string body = Clean(message).Trim();
        string labelPart = label.Length == 0 ? string.Empty : $" {label}";
        return $" {ChatColors.Green}[草人杯]{ChatColors.Default}{labelPart} {body}";
    }

    private static string Clean(string value) =>
        value.Replace("[草人杯皮肤]", "[皮肤]", StringComparison.Ordinal)
            .Replace("[草人杯]", string.Empty, StringComparison.Ordinal);
}
