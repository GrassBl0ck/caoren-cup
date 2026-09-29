using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace CaorenCup.Contracts;

/// <summary>独立插件共用旧草人杯聊天前缀和颜色规则。</summary>
public static class CaorenCupChat
{
    private const string Tag = "[草人杯]";

    private static string Format(string message, char color)
    {
        string body = message.Replace(Tag, string.Empty, StringComparison.Ordinal).Trim();
        return $" {color}{Tag}{ChatColors.Default} {body}";
    }

    public static string FormatPrivateMessage(string message) =>
        Format(message, ChatColors.Green);

    public static string FormatGlobalMessage(string message) =>
        Format(message, ChatColors.Red);

    public static string FormatChangeMessage(string action, string targetDesc, string value) =>
        $"{ChatColors.Green}{action} {ChatColors.Default}{targetDesc} {ChatColors.Green}{value}";

    public static string FormatHelpMenuLine(string message)
    {
        string clean = new(message.Where(ch => ch < '\x01' || ch > '\x10').ToArray());
        return FormatPrivateMessage(clean.Trim());
    }

    public static void PrintToChat(CCSPlayerController player, string message)
    {
        if (player.IsValid)
            player.PrintToChat(FormatPrivateMessage(message));
    }

    public static void PrintToChatAll(string message) =>
        Server.PrintToChatAll(FormatGlobalMessage(message));
}
