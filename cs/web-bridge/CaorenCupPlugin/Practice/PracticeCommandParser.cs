namespace CaorenCupPlugin.Practice;

public enum PracticeCommandKind
{
    Start,
    Stop,
    Rethrow,
    Last,
    Clear,
    AddBot,
    AddCrouchedBot,
    RemoveBots,
    CounterTerroristSpawn,
    TerroristSpawn,
    God,
    Impacts,
    Trajectory,
    SaveLineup,
    LoadLineup,
    DeleteLineup,
}

public sealed record ParsedPracticeCommand(PracticeCommandKind Kind, IReadOnlyList<string> Arguments);

public sealed record PracticeCommandParseResult(bool Ok, string Code, ParsedPracticeCommand? Command = null);

public static class PracticeCommandParser
{
    private static readonly IReadOnlyDictionary<string, PracticeCommandKind> NoArgumentCommands =
        new Dictionary<string, PracticeCommandKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["prac"] = PracticeCommandKind.Start,
            ["exitprac"] = PracticeCommandKind.Stop,
            ["rethrow"] = PracticeCommandKind.Rethrow,
            ["last"] = PracticeCommandKind.Last,
            ["clear"] = PracticeCommandKind.Clear,
            ["bot"] = PracticeCommandKind.AddBot,
            ["cbot"] = PracticeCommandKind.AddCrouchedBot,
            ["nobots"] = PracticeCommandKind.RemoveBots,
            ["ctspawn"] = PracticeCommandKind.CounterTerroristSpawn,
            ["tspawn"] = PracticeCommandKind.TerroristSpawn,
            ["god"] = PracticeCommandKind.God,
            ["impacts"] = PracticeCommandKind.Impacts,
            ["traj"] = PracticeCommandKind.Trajectory,
        };

    public static PracticeCommandParseResult Parse(string? text)
    {
        var raw = text?.Trim() ?? string.Empty;
        if (raw.IndexOfAny([';', '\r', '\n']) >= 0) return new(false, "UNSAFE_COMMAND");
        if (raw.Length < 2 || raw[0] is not ('.' or '!' or '/')) return new(false, "UNKNOWN_COMMAND");
        var parts = raw[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return new(false, "UNKNOWN_COMMAND");
        if (NoArgumentCommands.TryGetValue(parts[0], out var kind))
            return parts.Length == 1
                ? new(true, "OK", new(kind, []))
                : new(false, "INVALID_ARGUMENTS");

        var command = parts[0].ToLowerInvariant();
        if (command == "savenade" && parts.Length >= 2)
            return new(true, "OK", new(PracticeCommandKind.SaveLineup, [string.Join(' ', parts.Skip(1))]));
        if ((command == "loadnade" || command == "deletenade")
            && parts.Length == 2
            && parts[1].Length <= 64
            && parts[1].All(character => char.IsLetterOrDigit(character) || character is '_' or '-'))
            return new(true, "OK", new(
                command == "loadnade" ? PracticeCommandKind.LoadLineup : PracticeCommandKind.DeleteLineup,
                [parts[1]]));
        return new(false, "UNKNOWN_COMMAND");
    }
}
