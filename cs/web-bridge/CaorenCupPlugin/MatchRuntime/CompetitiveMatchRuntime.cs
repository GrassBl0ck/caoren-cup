using System.Text.Json;

namespace CaorenCupPlugin.MatchRuntime;

public sealed class CompetitiveMatchRuntime
{
    private readonly List<MatchEvent> _events = [];
    private MatchDefinition? _definition;
    private RoundCoordinator? _rounds;
    private MatchStage _stage = MatchStage.Disabled;
    private MatchPauseState _pauseState = MatchPauseState.Running;
    private EngineSide _teamAEngineSide = EngineSide.CounterTerrorist;
    private bool _sideSwapPending;
    private long _runtimeEpoch;
    private long _eventSequence;

    public IReadOnlyList<MatchEvent> Events => _events;

    public MatchRuntimeSnapshot Snapshot => _definition is null || _rounds is null
        ? MatchRuntimeSnapshot.Empty
        : new(
            _definition.MatchId,
            _definition.Revision,
            _runtimeEpoch,
            _stage,
            _pauseState,
            _rounds.ScoreA,
            _rounds.ScoreB,
            _rounds.CompletedRounds,
            _rounds.OvertimeIndex,
            _rounds.OvertimeRoundInStage,
            _teamAEngineSide,
            _sideSwapPending,
            _rounds.CurrentRoundKey,
            _eventSequence);

    public MatchCommandResult Load(MatchDefinition definition)
    {
        var validation = ValidateDefinition(definition);
        if (!validation.Ok) return validation;
        if (_stage is not MatchStage.Disabled and not MatchStage.Completed and not MatchStage.Aborted)
            return Failure("RUNTIME_BUSY", "当前已有未结束的正式比赛。");

        _definition = definition;
        _rounds = new RoundCoordinator();
        _events.Clear();
        _eventSequence = 0;
        _runtimeEpoch = 1;
        _stage = MatchStage.Staging;
        _pauseState = MatchPauseState.Running;
        _teamAEngineSide = definition.TeamAInitialSide;
        _sideSwapPending = false;
        return Success();
    }

    public MatchCommandResult BeginWarmup()
    {
        if (_stage != MatchStage.Staging) return InvalidStage();
        _stage = MatchStage.Warmup;
        Publish("warmup_started");
        return Success();
    }

    public MatchCommandResult BeginReadyCheck()
    {
        if (_stage is not MatchStage.Staging and not MatchStage.Warmup) return InvalidStage();
        _stage = MatchStage.ReadyCheck;
        Publish("ready_check_started");
        return Success();
    }

    public MatchCommandResult BeginGoingLive()
    {
        if (_stage != MatchStage.ReadyCheck) return InvalidStage();
        _stage = MatchStage.GoingLive;
        Publish("going_live");
        return Success();
    }

    public MatchCommandResult ConfirmLive()
    {
        if (_stage != MatchStage.GoingLive) return InvalidStage();
        _stage = MatchStage.FirstHalf;
        Publish("match_started");
        return Success();
    }

    public MatchRoundOpenResult OpenRound(JsonElement? players = null)
    {
        if (_definition is null || _rounds is null)
            return new(false, "NO_MATCH", "没有已加载的正式比赛。");
        if (_stage is not MatchStage.FirstHalf and not MatchStage.SecondHalf and not MatchStage.Overtime)
            return new(false, "INVALID_STAGE", "当前阶段不能开始正式回合。");
        if (_sideSwapPending)
            return new(false, "SIDE_SWAP_PENDING", "必须先完成换边。");
        if (_rounds.CurrentRoundKey is not null)
            return new(false, "ROUND_ALREADY_OPEN", "当前已有进行中的正式回合。", _rounds.CurrentRoundKey);

        var roundKey = _rounds.OpenRound(_definition.MatchId, _runtimeEpoch);
        Publish("round_started", roundKey, _rounds.CompletedRounds + 1, players: players);
        return new(true, "OK", RoundKey: roundKey);
    }

    public MatchRoundResult CompleteRound(string roundKey, RosterSide winner, JsonElement? players = null)
    {
        if (_rounds is null) return new(false, "NO_MATCH", "没有已加载的正式比赛。");
        if (string.IsNullOrWhiteSpace(roundKey)) return new(false, "INVALID_ROUND_KEY", "回合标识不能为空。");

        var settlement = _rounds.CompleteRound(roundKey, winner);
        if (!settlement.Ok) return new(false, settlement.Code, settlement.Message);

        var winningEngineSide = winner == RosterSide.A ? _teamAEngineSide
            : _teamAEngineSide == EngineSide.CounterTerrorist ? EngineSide.Terrorist : EngineSide.CounterTerrorist;
        Publish("round_completed", roundKey, _rounds.CompletedRounds, winningEngineSide, players);
        var sideSwapRequired = false;
        var matchFinished = false;
        switch (settlement.Transition)
        {
            case RoundTransition.RegulationHalftime:
                _stage = MatchStage.Halftime;
                _sideSwapPending = true;
                sideSwapRequired = true;
                Publish("halftime_started");
                break;
            case RoundTransition.EnterOvertime:
                _stage = MatchStage.Overtime;
                Publish("overtime_started");
                break;
            case RoundTransition.OvertimeHalftime:
                _stage = MatchStage.OvertimeHalftime;
                _sideSwapPending = true;
                sideSwapRequired = true;
                Publish("overtime_halftime_started");
                break;
            case RoundTransition.NextOvertime:
                _stage = MatchStage.OvertimeHalftime;
                _sideSwapPending = true;
                sideSwapRequired = true;
                Publish("overtime_started");
                break;
            case RoundTransition.Completed:
                _stage = MatchStage.Completed;
                matchFinished = true;
                Publish("match_completed");
                break;
        }

        return new(true, "OK", MatchFinished: matchFinished, SideSwapRequired: sideSwapRequired);
    }

    public MatchCommandResult ConfirmSideSwap()
    {
        if (!_sideSwapPending || _stage is not MatchStage.Halftime and not MatchStage.OvertimeHalftime)
            return InvalidStage();

        _teamAEngineSide = Opposite(_teamAEngineSide);
        _sideSwapPending = false;
        _stage = _stage == MatchStage.Halftime ? MatchStage.SecondHalf : MatchStage.Overtime;
        Publish("side_swapped");
        return Success();
    }

    public MatchCommandResult Abort(string reason)
    {
        if (_stage == MatchStage.Aborted) return Success("ALREADY_ABORTED");
        if (_stage is MatchStage.Disabled or MatchStage.Completed)
            return InvalidStage();

        _rounds?.CancelOpenRound();
        _sideSwapPending = false;
        _stage = MatchStage.Aborted;
        Publish("match_aborted");
        return Success(message: string.IsNullOrWhiteSpace(reason) ? "比赛已终止。" : reason.Trim());
    }

    public MatchCommandResult SetPauseState(MatchPauseState pauseState)
    {
        if (_stage is MatchStage.Disabled or MatchStage.Completed or MatchStage.Aborted)
            return InvalidStage();
        _pauseState = pauseState;
        Publish(pauseState == MatchPauseState.Running ? "pause_ended" : "pause_started");
        return Success();
    }

    public MatchCommandResult Restore(MatchRuntimeSnapshot snapshot)
    {
        if (_definition is null || _rounds is null) return Failure("NO_MATCH", "当前没有可恢复的比赛定义。");
        if (!string.Equals(snapshot.MatchId, _definition.MatchId, StringComparison.Ordinal)
            || snapshot.Revision != _definition.Revision)
            return Failure("IDENTITY_MISMATCH", "恢复快照不属于当前比赛。");
        if (snapshot.RuntimeEpoch <= _runtimeEpoch)
            return Failure("STALE_RESTORE_EPOCH", "恢复快照 epoch 必须大于当前 epoch。");

        _rounds.Restore(snapshot);
        _runtimeEpoch = snapshot.RuntimeEpoch;
        _stage = snapshot.Stage;
        _pauseState = snapshot.PauseState;
        _teamAEngineSide = snapshot.TeamAEngineSide;
        _sideSwapPending = snapshot.SideSwapPending;
        _events.Clear();
        _eventSequence = 0;
        return Success("RESTORED");
    }

    public MatchCommandResult RecoverFromWeb(MatchRuntimeSnapshot snapshot)
    {
        if (_definition is null || _rounds is null || _stage != MatchStage.Staging)
            return Failure("RECOVERY_NOT_ALLOWED", "只有新加载的 Staging 运行时可以恢复网页快照。");
        if (!string.Equals(snapshot.MatchId, _definition.MatchId, StringComparison.Ordinal)
            || snapshot.Revision != _definition.Revision
            || snapshot.RuntimeEpoch <= 0)
            return Failure("IDENTITY_MISMATCH", "恢复快照不属于当前比赛。");
        _rounds.Restore(snapshot);
        _runtimeEpoch = snapshot.RuntimeEpoch;
        _stage = snapshot.Stage;
        _pauseState = MatchPauseState.TechnicalPaused;
        _teamAEngineSide = snapshot.TeamAEngineSide;
        _sideSwapPending = snapshot.SideSwapPending;
        _events.Clear();
        _eventSequence = 0;
        return Success("RECOVERED");
    }

    private void Publish(string type, string? roundKey = null, int? roundNumber = null,
        EngineSide? winningEngineSide = null, JsonElement? players = null)
    {
        if (_definition is null) return;
        _eventSequence++;
        _events.Add(new MatchEvent(
            _eventSequence,
            type,
            _definition.MatchId,
            _definition.Revision,
            _runtimeEpoch,
            roundKey,
            roundNumber,
            roundNumber is null ? null : _teamAEngineSide,
            winningEngineSide,
            players?.Clone(),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
    }

    private static MatchCommandResult ValidateDefinition(MatchDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.MatchId) || definition.Revision <= 0)
            return Failure("INVALID_IDENTITY", "比赛身份无效。");
        if (string.IsNullOrWhiteSpace(definition.MapName)
            || string.IsNullOrWhiteSpace(definition.TeamAName)
            || string.IsNullOrWhiteSpace(definition.TeamBName))
            return Failure("INVALID_DEFINITION", "地图和队名不能为空。");
        if (definition.Seats.Count == 0
            || !definition.Seats.Any(seat => seat.Roster == RosterSide.A)
            || !definition.Seats.Any(seat => seat.Roster == RosterSide.B))
            return Failure("INVALID_ROSTER", "A/B 双方都必须有正式席位。");
        if (definition.Seats.Any(seat => string.IsNullOrWhiteSpace(seat.SeatId) || string.IsNullOrWhiteSpace(seat.SteamId))
            || definition.Seats.Select(seat => seat.SeatId).Distinct(StringComparer.Ordinal).Count() != definition.Seats.Count
            || definition.Seats.Select(seat => seat.SteamId).Distinct(StringComparer.Ordinal).Count() != definition.Seats.Count)
            return Failure("INVALID_ROSTER", "席位和 SteamID 必须完整且唯一。");
        return Success();
    }

    private MatchCommandResult InvalidStage() => Failure("INVALID_STAGE", $"当前阶段 {_stage} 不允许该操作。");

    private static MatchCommandResult Success(string code = "OK", string message = "") => new(true, code, message);

    private static MatchCommandResult Failure(string code, string message) => new(false, code, message);

    private static EngineSide Opposite(EngineSide side) => side == EngineSide.CounterTerrorist
        ? EngineSide.Terrorist
        : EngineSide.CounterTerrorist;
}
