namespace CaorenCupPlugin;

public enum DuelControlMode { None, WebManaged, GameManaged }

public enum DuelLifecycle { Idle, Running, Paused, Finished }

public enum DuelTeam { Terrorist, CounterTerrorist }

public enum DuelGameStage { Pistol, Rifle, Sniper }

public sealed record DuelGameConfig(
    int PistolRounds = 8,
    int RifleRounds = 16,
    int SniperRounds = 12,
    double RoundTimeMinutes = 1,
    string UtilityMode = "none")
{
    public int TotalRounds => PistolRounds + RifleRounds + SniperRounds;
}

public sealed record DuelParticipant(string SteamId, string Name, DuelTeam Team);

public sealed record DuelRoundResult(bool Counted, bool Finished, int ScoreT, int ScoreCt);

public sealed class DuelGameSession
{
    private readonly Dictionary<string, DuelParticipant> _participants = new(StringComparer.Ordinal);
    private readonly HashSet<string> _connected = new(StringComparer.Ordinal);
    private bool _roundOpen;

    public DuelGameSession(DuelGameConfig? config = null) => Config = config ?? new();

    public DuelControlMode ControlMode { get; private set; }

    public DuelLifecycle Lifecycle { get; private set; }

    public DuelGameConfig Config { get; private set; }

    public IReadOnlyDictionary<string, DuelParticipant> Participants => _participants;

    public int CompletedRounds { get; private set; }

    public int ScoreT { get; private set; }

    public int ScoreCt { get; private set; }

    public string? PauseReason { get; private set; }

    public DuelGameStage CurrentStage => CompletedRounds < Config.PistolRounds
        ? DuelGameStage.Pistol
        : CompletedRounds < Config.PistolRounds + Config.RifleRounds
            ? DuelGameStage.Rifle
            : DuelGameStage.Sniper;

    public void EnterWebManaged(DuelGameConfig config)
    {
        Clear();
        Config = config;
        ControlMode = DuelControlMode.WebManaged;
    }

    public bool TryStart(IReadOnlyCollection<DuelParticipant> players, bool confirmWebTakeover, out string error)
    {
        if (ControlMode == DuelControlMode.GameManaged && Lifecycle is DuelLifecycle.Running or DuelLifecycle.Paused)
        {
            error = "游戏内单挑已经在进行。";
            return false;
        }

        if (ControlMode == DuelControlMode.WebManaged && !confirmWebTakeover)
        {
            error = "检测到网页单挑状态，请输入 /duel start confirm 确认接管。";
            return false;
        }

        var unique = players.Where(p => !string.IsNullOrWhiteSpace(p.SteamId))
            .GroupBy(p => p.SteamId, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToArray();
        if (!unique.Any(p => p.Team == DuelTeam.Terrorist) || !unique.Any(p => p.Team == DuelTeam.CounterTerrorist))
        {
            error = "T 和 CT 双方都必须至少有一名真人玩家。";
            return false;
        }

        Clear();
        foreach (var participant in unique)
        {
            _participants[participant.SteamId] = participant;
            _connected.Add(participant.SteamId);
        }

        ControlMode = DuelControlMode.GameManaged;
        Lifecycle = DuelLifecycle.Running;
        error = string.Empty;
        return true;
    }

    public bool TryUpdateConfig(DuelGameConfig next, out string error)
    {
        if (ControlMode != DuelControlMode.None || Lifecycle != DuelLifecycle.Idle)
        {
            error = "仅能在单挑开始前修改配置。";
            return false;
        }

        if (next.PistolRounds is < 0 or > 99 || next.RifleRounds is < 0 or > 99 || next.SniperRounds is < 0 or > 99)
        {
            error = "各阶段回合数必须在 0 到 99 之间。";
            return false;
        }

        if (next.TotalRounds < 30)
        {
            error = "总回合数必须至少为 30。";
            return false;
        }

        if (double.IsNaN(next.RoundTimeMinutes) || double.IsInfinity(next.RoundTimeMinutes) || next.RoundTimeMinutes is < 0.25 or > 5)
        {
            error = "每回合时间必须在 0.25 到 5 分钟之间。";
            return false;
        }

        if (next.UtilityMode is not ("none" or "random1" or "random2" or "random3" or "full"))
        {
            error = "道具模式仅支持 none、random1、random2、random3 或 full。";
            return false;
        }

        Config = next;
        error = string.Empty;
        return true;
    }

    public void ResetConfig()
    {
        Config = new DuelGameConfig();
    }

    public void MarkRoundStarted()
    {
        if (ControlMode == DuelControlMode.GameManaged && Lifecycle == DuelLifecycle.Running)
        {
            _roundOpen = true;
        }
    }

    public DuelRoundResult RecordRoundEnd(DuelTeam winner)
    {
        if (ControlMode != DuelControlMode.GameManaged || Lifecycle != DuelLifecycle.Running || !_roundOpen)
        {
            return new(false, false, ScoreT, ScoreCt);
        }

        _roundOpen = false;
        CompletedRounds++;
        if (winner == DuelTeam.Terrorist)
        {
            ScoreT++;
        }
        else
        {
            ScoreCt++;
        }

        var finished = CompletedRounds >= Config.TotalRounds;
        if (finished)
        {
            Lifecycle = DuelLifecycle.Finished;
        }

        return new(true, finished, ScoreT, ScoreCt);
    }

    public bool UpdateConnectedPlayers(IReadOnlySet<string> connectedSteamIds)
    {
        _connected.Clear();
        foreach (var steamId in connectedSteamIds)
        {
            if (_participants.ContainsKey(steamId))
            {
                _connected.Add(steamId);
            }
        }

        if (Lifecycle != DuelLifecycle.Running || _participants.Keys.All(_connected.Contains))
        {
            return false;
        }

        Pause("有参赛玩家掉线");
        return true;
    }

    public bool TryResume(out string error)
    {
        if (ControlMode != DuelControlMode.GameManaged || Lifecycle != DuelLifecycle.Paused)
        {
            error = "当前单挑没有暂停。";
            return false;
        }

        var hasT = _participants.Values.Any(p => p.Team == DuelTeam.Terrorist && _connected.Contains(p.SteamId));
        var hasCt = _participants.Values.Any(p => p.Team == DuelTeam.CounterTerrorist && _connected.Contains(p.SteamId));
        if (!hasT || !hasCt)
        {
            error = "T 和 CT 双方都必须至少有一名在线参赛者才能恢复。";
            return false;
        }

        Lifecycle = DuelLifecycle.Running;
        PauseReason = null;
        error = string.Empty;
        return true;
    }

    public void Pause(string reason)
    {
        if (Lifecycle == DuelLifecycle.Running)
        {
            Lifecycle = DuelLifecycle.Paused;
            PauseReason = reason;
            _roundOpen = false;
        }
    }

    public void Clear()
    {
        _participants.Clear();
        _connected.Clear();
        CompletedRounds = 0;
        ScoreT = 0;
        ScoreCt = 0;
        _roundOpen = false;
        PauseReason = null;
        ControlMode = DuelControlMode.None;
        Lifecycle = DuelLifecycle.Idle;
    }
}
