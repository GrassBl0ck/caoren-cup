using CaorenCupPlugin.MatchRuntime;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Cvars;

namespace CaorenCupPlugin.Practice;

public enum PracticeStage { Inactive, Starting, Active, Stopping }

public enum PracticeSpawnSide { Terrorist, CounterTerrorist }

public sealed class PracticeRuntime
{
    private readonly MatchServerCvarScope _cvars;
    private readonly Action<string> _execute;

    public PracticeRuntime() : this(ReadCurrentValue, Server.ExecuteCommand) {}

    public PracticeRuntime(Func<string, string, string> readCurrentValue, Action<string> execute)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _cvars = new MatchServerCvarScope(readCurrentValue, execute);
    }

    public PracticeStage Stage { get; private set; }

    public string? MapName { get; private set; }

    public MatchCommandResult Start(string mapName, bool formalMatchActive)
    {
        if (formalMatchActive) return new(false, "FORMAL_MATCH_ACTIVE", "正式比赛运行时活动时不能进入练习模式。");
        if (Stage == PracticeStage.Active) return new(true, "ALREADY_ACTIVE");
        if (Stage != PracticeStage.Inactive) return new(false, "PRACTICE_BUSY");
        if (!IsSafeMap(mapName)) return new(false, "INVALID_MAP", "练习地图名无效。");

        Stage = PracticeStage.Starting;
        try
        {
            _cvars.Apply(
            [
                new("sv_cheats", "1", "0"),
                new("sv_infinite_ammo", "1", "0"),
                new("mp_roundtime", "60", "1.92"),
                new("mp_roundtime_defuse", "60", "1.92"),
                new("mp_buytime", "3600", "20"),
                new("mp_buy_anywhere", "1", "0"),
                new("mp_freezetime", "0", "15"),
                new("mp_autoteambalance", "0", "1"),
                new("mp_limitteams", "0", "2"),
                new("ammo_grenade_limit_total", "5", "4"),
                new("sv_grenade_trajectory_prac_pipreview", "1", "0"),
                new("sv_showimpacts", "1", "0"),
            ]);
            _execute("mp_warmup_end");
            _execute("mp_restartgame 1");
            MapName = mapName;
            Stage = PracticeStage.Active;
            return new(true, "STARTED");
        }
        catch (Exception ex)
        {
            _cvars.TryRestoreAll(maxAttempts: 1);
            Stage = PracticeStage.Inactive;
            MapName = null;
            return new(false, "START_FAILED", ex.Message);
        }
    }

    public MatchCommandResult Stop()
    {
        if (Stage == PracticeStage.Inactive) return new(true, "ALREADY_INACTIVE");
        Stage = PracticeStage.Stopping;
        var restored = _cvars.TryRestoreAll(maxAttempts: 3);
        if (!restored)
        {
            Stage = PracticeStage.Stopping;
            return new(false, "CVAR_RESTORE_PENDING", "练习 CVar 尚未恢复完整。");
        }
        Stage = PracticeStage.Inactive;
        MapName = null;
        return new(true, "STOPPED");
    }

    public MatchCommandResult RethrowLastGrenade() => ExecuteActive("sv_rethrow_last_grenade");

    public MatchCommandResult ClearGrenades()
    {
        var active = RequireActive();
        if (!active.Ok) return active;
        _execute("ent_fire smokegrenade_projectile kill");
        _execute("ent_fire molotov_projectile kill");
        _execute("ent_fire inferno kill");
        return new(true, "EXECUTED");
    }

    public MatchCommandResult AddBot(bool crouched)
    {
        var active = RequireActive();
        if (!active.Ok) return active;
        _execute("bot_add");
        if (crouched) _execute("bot_crouch 1");
        return new(true, "EXECUTED");
    }

    public MatchCommandResult RemoveBots() => ExecuteActive("bot_kick");

    public MatchCommandResult TeleportToSpawn(PracticeSpawnSide side) => ExecuteActive(
        side == PracticeSpawnSide.CounterTerrorist ? "setpos_player @me ct" : "setpos_player @me t");

    public MatchCommandResult SetGodMode(bool enabled) => ExecuteActive($"god {(enabled ? 1 : 0)}");

    public MatchCommandResult SetImpacts(bool enabled) => ExecuteActive($"sv_showimpacts {(enabled ? 1 : 0)}");

    public MatchCommandResult SetTrajectory(bool enabled) =>
        ExecuteActive($"sv_grenade_trajectory_prac_pipreview {(enabled ? 1 : 0)}");

    private MatchCommandResult ExecuteActive(string command)
    {
        var active = RequireActive();
        if (!active.Ok) return active;
        _execute(command);
        return new(true, "EXECUTED");
    }

    private MatchCommandResult RequireActive() => Stage == PracticeStage.Active
        ? new(true, "OK")
        : new(false, "PRACTICE_INACTIVE", "练习模式尚未开启。");

    private static bool IsSafeMap(string mapName) =>
        !string.IsNullOrWhiteSpace(mapName)
        && mapName.StartsWith("de_", StringComparison.OrdinalIgnoreCase)
        && mapName.All(character => char.IsLetterOrDigit(character) || character == '_');

    private static string ReadCurrentValue(string name, string fallback)
    {
        try
        {
            var current = ConVar.Find(name)?.StringValue;
            return global::CaorenCupPlugin.MatchRuntime.RuntimeCvarValue.TryNormalizeSafeValue(current, out var normalized)
                ? normalized
                : fallback;
        }
        catch
        {
            return fallback;
        }
    }
}
