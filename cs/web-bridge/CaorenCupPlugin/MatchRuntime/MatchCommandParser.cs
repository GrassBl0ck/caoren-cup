namespace CaorenCupPlugin.MatchRuntime;

public enum MatchCommandKind
{
    Ready,
    Unready,
    Start,
    Pause,
    TechnicalPause,
    TacticalPause,
    Unpause,
    ForcePause,
    ForceUnpause,
    Stop,
    Restore,
    Practice,
    ExitPractice,
    CcMatch,
}

public sealed record ParsedMatchCommand(MatchCommandKind Kind, IReadOnlyList<string> Arguments);

public sealed record MatchCommandParseResult(
    bool Ok,
    string Code,
    ParsedMatchCommand? Command = null,
    string Message = "");

public static class MatchCommandParser
{
    private static readonly IReadOnlyDictionary<string, MatchCommandKind> SimpleCommands =
        new Dictionary<string, MatchCommandKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["ready"] = MatchCommandKind.Ready,
            ["r"] = MatchCommandKind.Ready,
            ["unready"] = MatchCommandKind.Unready,
            ["ur"] = MatchCommandKind.Unready,
            ["notready"] = MatchCommandKind.Unready,
            ["pause"] = MatchCommandKind.Pause,
            ["tech"] = MatchCommandKind.TechnicalPause,
            ["tac"] = MatchCommandKind.TacticalPause,
            ["unpause"] = MatchCommandKind.Unpause,
            ["forcepause"] = MatchCommandKind.ForcePause,
            ["fp"] = MatchCommandKind.ForcePause,
            ["forceunpause"] = MatchCommandKind.ForceUnpause,
            ["fup"] = MatchCommandKind.ForceUnpause,
            ["stop"] = MatchCommandKind.Stop,
            ["prac"] = MatchCommandKind.Practice,
            ["exitprac"] = MatchCommandKind.ExitPractice,
        };

    public static MatchCommandParseResult Parse(string? text)
    {
        var raw = text?.Trim() ?? string.Empty;
        if (raw.IndexOfAny([';', '\r', '\n']) >= 0)
            return Failure("UNSAFE_COMMAND", "命令包含不安全字符。");
        if (raw.Length < 2 || raw[0] is not ('.' or '!' or '/'))
            return Failure("UNKNOWN_COMMAND", "不是草人杯比赛命令。");

        var parts = raw[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return Failure("UNKNOWN_COMMAND", "命令为空。");
        var name = parts[0].ToLowerInvariant();
        var arguments = parts.Skip(1).Select(value => value.ToLowerInvariant()).ToArray();

        if (SimpleCommands.TryGetValue(name, out var simpleKind))
            return arguments.Length == 0
                ? Success(simpleKind)
                : Failure("INVALID_ARGUMENTS", "该命令不接受额外参数。");
        if (name == "start") return ParseStart(arguments);
        if (name == "restore") return ParseRestore(arguments, MatchCommandKind.Restore);
        if (name == "ccmatch") return ParseCcMatch(arguments);
        return Failure("UNKNOWN_COMMAND", "未知的草人杯比赛命令。");
    }

    private static MatchCommandParseResult ParseStart(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0) return Success(MatchCommandKind.Start);
        return arguments.Count == 1 && arguments[0] == "confirm"
            ? Success(MatchCommandKind.Start, arguments)
            : Failure("INVALID_ARGUMENTS", "开赛确认格式应为 .start confirm。");
    }

    private static MatchCommandParseResult ParseRestore(IReadOnlyList<string> arguments, MatchCommandKind kind)
    {
        var validCount = arguments.Count is 1 or 2;
        var validRound = validCount && int.TryParse(arguments[0], out var round) && round > 0;
        var validConfirm = arguments.Count != 2 || arguments[1] == "confirm";
        return validRound && validConfirm
            ? Success(kind, arguments)
            : Failure("INVALID_ARGUMENTS", "恢复格式应为 .restore <正整数回合> [confirm]。");
    }

    private static MatchCommandParseResult ParseCcMatch(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0) return Failure("INVALID_ARGUMENTS", "缺少 /ccmatch 子命令。");
        var subcommand = arguments[0];
        if (subcommand == "restore")
        {
            var restore = ParseRestore(arguments.Skip(1).ToArray(), MatchCommandKind.CcMatch);
            return !restore.Ok
                ? restore
                : Success(MatchCommandKind.CcMatch, arguments);
        }
        if (subcommand == "start")
        {
            var startArguments = arguments.Skip(1).ToArray();
            if (startArguments.Length > 1 || startArguments.Length == 1 && startArguments[0] != "confirm")
                return Failure("INVALID_ARGUMENTS", "开赛格式应为 /ccmatch start [confirm]。");
            return Success(MatchCommandKind.CcMatch, arguments);
        }
        if (subcommand is "status" or "pause" or "abort")
            return arguments.Count == 1
                ? Success(MatchCommandKind.CcMatch, arguments)
                : Failure("INVALID_ARGUMENTS", "该子命令不接受额外参数。");
        return Failure("UNKNOWN_COMMAND", "未知的 /ccmatch 子命令。");
    }

    private static MatchCommandParseResult Success(MatchCommandKind kind, IReadOnlyList<string>? arguments = null) =>
        new(true, "OK", new ParsedMatchCommand(kind, arguments ?? Array.Empty<string>()));

    private static MatchCommandParseResult Failure(string code, string message) => new(false, code, Message: message);
}
