using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Cvars;

using CaorenCup.Contracts.MatchRuntime;

namespace CaorenCup.Core;

public sealed class CounterStrikeMatchRuntimeHost : IMatchRuntimeHost
{
    private readonly MatchServerCvarScope _cvars = new(ReadCurrentValue, Server.ExecuteCommand);
    private readonly List<string> _demoSegments = [];
    private bool _demoRunning;
    private string? _recordingMatchId;

    public IReadOnlyList<string> DemoSegments => _demoSegments;
    public bool WaitForEngineRestore => true;

    public void ApplyFormalMatchConfig() => _cvars.Apply(
    [
        new("mp_maxrounds", "0", "24"),
        new("mp_winlimit", "0", "0"),
        new("mp_match_can_clinch", "0", "1"),
        new("mp_halftime", "0", "1"),
        new("mp_overtime_enable", "0", "1"),
        new("mp_autoteambalance", "0", "1"),
        new("mp_warmup_pausetimer", "1", "0"),
        new("mp_limitteams", "0", "2"),
    ]);

    public void ExecuteGoingLive()
    {
        Server.ExecuteCommand("mp_warmup_end");
        Server.ExecuteCommand("mp_restartgame 1");
    }

    public void SwapSides() => Server.ExecuteCommand("mp_swapteams");

    public void StartDemo(MatchDefinition definition)
    {
        if (_recordingMatchId != definition.MatchId)
        {
            _recordingMatchId = definition.MatchId;
            _demoSegments.Clear();
        }
        var fileName = BuildDemoFileName(definition, DateTime.UtcNow) + $"_part{_demoSegments.Count + 1:D2}";
        Server.ExecuteCommand($"tv_record {fileName}");
        _demoSegments.Add(fileName + ".dem");
        _demoRunning = true;
    }

    internal static string BuildDemoFileName(MatchDefinition definition, DateTime utcNow) =>
        $"{utcNow:yyyyMMdd-HHmmss}_{Safe(definition.MatchId)}_{Safe(definition.MapName)}_" +
        $"{Safe(definition.TeamAName)}-vs-{Safe(definition.TeamBName)}";

    public void StopDemo()
    {
        if (!_demoRunning) return;
        Server.ExecuteCommand("tv_stoprecord");
        _demoRunning = false;
    }

    public void PauseMatch() => Server.ExecuteCommand("mp_pause_match");

    public void UnpauseMatch() => Server.ExecuteCommand("mp_unpause_match");

    public string ReadCvar(string name, string fallback) => ReadCurrentValue(name, fallback);

    public void Execute(string command) => Server.ExecuteCommand(command);

    public void Pause() => PauseMatch();

    public void RestoreValveBackup(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.Any(character => !(char.IsLetterOrDigit(character) || character is '_' or '-' or '.')))
            throw new ArgumentException("Valve backup file name is unsafe.", nameof(fileName));
        if (!File.Exists(Path.Combine(Server.GameDirectory, "csgo", fileName)))
            throw new FileNotFoundException("该回合没有可用的 CS2 备份。");
        Server.ExecuteCommand($"mp_backup_restore_load_file {fileName}");
    }

    public void StartDemo(string fileName)
    {
        var safeName = Safe(fileName);
        Server.ExecuteCommand($"tv_record {safeName}");
        _demoRunning = true;
    }

    public bool RestoreFormalMatchConfig() => _cvars.TryRestoreAll(maxAttempts: 1);

    private static string ReadCurrentValue(string name, string fallback)
    {
        try
        {
            var current = ConVar.Find(name)?.StringValue;
            return RuntimeCvarValue.TryNormalizeSafeValue(current, out var normalized)
                ? normalized
                : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static string Safe(string value)
    {
        var result = new string(value.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_').ToArray());
        return string.IsNullOrWhiteSpace(result) ? "unknown" : result;
    }
}
