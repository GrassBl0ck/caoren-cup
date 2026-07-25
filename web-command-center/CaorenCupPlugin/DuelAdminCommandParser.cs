namespace CaorenCupPlugin;

public enum DuelAdminCommandKind
{
    Invalid, Help, Status, Rounds, Time, Utility, Reset,
    Start, StartConfirm, Pause, Resume, Stop, StopConfirm, Maps, Map
}

public sealed record DuelAdminCommand(
    DuelAdminCommandKind Kind,
    (int Pistol, int Rifle, int Sniper)? Rounds = null,
    double? RoundTimeMinutes = null,
    string? Value = null,
    string? Error = null);

public static class DuelAdminCommandParser
{
    public static DuelAdminCommand Parse(IReadOnlyList<string> args)
    {
        var values = args.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray();
        if (values.Length == 0) return new(DuelAdminCommandKind.Help);

        var verb = values[0].ToLowerInvariant();
        if (values.Length == 1)
        {
            var fixedKind = verb switch
            {
                "help" => DuelAdminCommandKind.Help,
                "status" => DuelAdminCommandKind.Status,
                "reset" => DuelAdminCommandKind.Reset,
                "start" => DuelAdminCommandKind.Start,
                "pause" => DuelAdminCommandKind.Pause,
                "resume" => DuelAdminCommandKind.Resume,
                "stop" => DuelAdminCommandKind.Stop,
                "maps" => DuelAdminCommandKind.Maps,
                _ => DuelAdminCommandKind.Invalid
            };
            return fixedKind == DuelAdminCommandKind.Invalid
                ? new(fixedKind, Error: "未知子命令，请使用 /duel help。")
                : new(fixedKind);
        }

        if (values.Length == 2 && verb == "start" && values[1].Equals("confirm", StringComparison.OrdinalIgnoreCase))
            return new(DuelAdminCommandKind.StartConfirm);
        if (values.Length == 2 && verb == "stop" && values[1].Equals("confirm", StringComparison.OrdinalIgnoreCase))
            return new(DuelAdminCommandKind.StopConfirm);
        if (values.Length == 2 && verb == "time" && double.TryParse(values[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var minutes))
            return new(DuelAdminCommandKind.Time, RoundTimeMinutes: minutes);
        if (values.Length == 2 && verb == "utility") return new(DuelAdminCommandKind.Utility, Value: values[1].ToLowerInvariant());
        if (values.Length >= 2 && verb == "map") return new(DuelAdminCommandKind.Map, Value: string.Join(' ', values.Skip(1)));
        if (values.Length == 4 && verb == "rounds" && int.TryParse(values[1], out var pistol) && int.TryParse(values[2], out var rifle) && int.TryParse(values[3], out var sniper))
            return new(DuelAdminCommandKind.Rounds, (pistol, rifle, sniper));

        return new(DuelAdminCommandKind.Invalid, Error: "参数格式错误，请使用 /duel help 查看用法。");
    }
}
