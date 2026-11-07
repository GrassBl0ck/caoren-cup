using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using System.Text;

namespace CaorenCup.Contracts;

public enum CaorenCupChatAudience { Private, Group, Global }

/// <summary>所有实际输出按接收范围选择颜色；分行和长消息的每一条均带一个统一前缀。</summary>
public static class CaorenCupChat
{
    public const string Tag = "[草人杯]";
    public const int MaxChatBytes = 240;

    private static string Body(string message) =>
        message.Replace(Tag, string.Empty, StringComparison.Ordinal).Trim();

    public static string FormatMessage(string message, CaorenCupChatAudience audience)
    {
        char color = audience switch
        {
            CaorenCupChatAudience.Private => ChatColors.Green,
            CaorenCupChatAudience.Group => ChatColors.Yellow,
            CaorenCupChatAudience.Global => ChatColors.Red,
            _ => throw new ArgumentOutOfRangeException(nameof(audience)),
        };
        return $" {color}{Tag}{ChatColors.Default} {Body(message)}";
    }

    public static string FormatPrivateMessage(string message) => FormatMessage(message, CaorenCupChatAudience.Private);
    public static string FormatTeamMessage(string message) => FormatMessage(message, CaorenCupChatAudience.Group);
    public static string FormatGlobalMessage(string message) => FormatMessage(message, CaorenCupChatAudience.Global);
    public static string FormatConsoleMessage(string message) => $"{Tag} {StripChatColors(Body(message)).Trim()}";
    public static string StripChatColors(string message) =>
        new(message.Where(ch => ch < '\x01' || ch > '\x10').ToArray());

    public static string FormatChangeMessage(string action, string targetDesc, string value) =>
        $"{ChatColors.Green}{action} {ChatColors.Default}{targetDesc} {ChatColors.Green}{value}";

    public static string FormatHelpMenuLine(string message) => FormatPrivateMessage(StripChatColors(message));

    // 此入口也是实际发送路径的可测试边界：测试替换传输，不替换格式化或分行规则。
    public static void Send(string message, CaorenCupChatAudience audience, Action<string> send)
    {
        var capacity = MaxChatBytes - Encoding.UTF8.GetByteCount(FormatMessage(string.Empty, audience));
        foreach (var line in Body(message).Replace("\r", string.Empty).Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var chunk = new StringBuilder();
            var bytes = 0;
            foreach (var rune in line.EnumerateRunes())
            {
                if (bytes + rune.Utf8SequenceLength > capacity)
                {
                    send(FormatMessage(chunk.ToString(), audience));
                    chunk.Clear();
                    bytes = 0;
                }
                chunk.Append(rune.ToString());
                bytes += rune.Utf8SequenceLength;
            }
            if (chunk.Length > 0) send(FormatMessage(chunk.ToString(), audience));
        }
    }

    public static void SendToRecipients<T>(IEnumerable<T> recipients, string message,
        CaorenCupChatAudience audience, Action<T, string> send)
    {
        foreach (var recipient in recipients.Distinct())
            Send(message, audience, text => send(recipient, text));
    }

    public static void Reply(CCSPlayerController? player, string message)
    {
        if (player is null) PrintToConsole(message);
        else PrintToChat(player, message);
    }

    public static void PrintToConsole(string message)
    {
        foreach (var line in message.Replace("\r", string.Empty).Split('\n'))
            if (!string.IsNullOrWhiteSpace(line)) Console.WriteLine(FormatConsoleMessage(line));
    }

    public static void PrintToPlayerConsole(CCSPlayerController player, string message)
    {
        if (!player.IsValid) return;
        foreach (var line in message.Replace("\r", string.Empty).Split('\n'))
            if (!string.IsNullOrWhiteSpace(line)) player.PrintToConsole(FormatConsoleMessage(line));
    }

    public static void PrintToChat(CCSPlayerController player, string message)
    {
        if (player.IsValid) Send(message, CaorenCupChatAudience.Private, player.PrintToChat);
    }

    public static void PrintToGroup(IEnumerable<CCSPlayerController> players, string message) =>
        SendToRecipients(players.Where(p => p.IsValid), message, CaorenCupChatAudience.Group,
            (player, text) => player.PrintToChat(text));

    public static void PrintToChatAll(string message)
    {
        Send(message, CaorenCupChatAudience.Global, Server.PrintToChatAll);
        PrintToConsole(message);
    }
}
