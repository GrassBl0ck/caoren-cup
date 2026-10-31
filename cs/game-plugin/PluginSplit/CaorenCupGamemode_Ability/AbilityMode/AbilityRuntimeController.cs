namespace Caoren.AbilityMode;

public sealed record RuntimeInitializationResult(
    bool Ok,
    bool Restored = false,
    bool Duplicate = false,
    bool ReplacedPreviousMatch = false,
    string? Code = null);

public sealed class AbilityRuntimeController
{
    private readonly RuntimePersistence _persistence;
    private readonly AbilityRoleRegistry? _roles;

    public AbilityRuntimeController(
        AbilityDefinitionCatalog catalog,
        RuntimePersistence persistence,
        AbilityRoleRegistry? roles = null)
    {
        Runtime = new AbilityMatchRuntime(catalog);
        _persistence = persistence;
        _roles = roles;
    }

    public AbilityMatchRuntime Runtime { get; }
    public AbilityHudService Hud { get; } = new();
    public MovementModifierService Movement { get; } = new();

    public async Task<RuntimeInitializationResult> InitializeFromConfirmedAsync(AbilitySyncConfig config)
    {
        _persistence.SetConfirmedConfig(config);
        if (!config.AbilityModeEnabled)
        {
            Hud.Stop();
            Movement.ClearAll();
            Runtime.Stop();
            return new(true, Code: "MODE_DISABLED");
        }
        if (_roles is not null)
        {
            var roleValidation = _roles.ValidateSelectedRoles(config);
            if (!roleValidation.Ok)
                return new(false, Code: roleValidation.Code);
        }
        var identity = new AbilityRuntimeIdentity(config.MatchId, config.Revision, config.ContentDigest, 0);
        if (Runtime.State is null)
        {
            var restored = await _persistence.LoadAsync(identity);
            if (restored is not null)
            {
                restored.Lifecycle = AbilityRuntimeLifecycle.Armed;
                Runtime.Restore(restored);
                return new(true, Restored: true);
            }
        }

        var result = Runtime.ApplyConfirmed(config);
        if (!result.Activated) return new(false, Code: result.Code);
        if (!result.Duplicate && Runtime.State is not null) _persistence.Schedule(Runtime.State);
        return new(true, Duplicate: result.Duplicate, ReplacedPreviousMatch: result.ReplacedPreviousMatch);
    }

    public async Task<RuntimeOperationResult> HandleAsync(AbilityRuntimeEnvelope envelope)
    {
        if (envelope.Action == "start")
        {
            var start = Runtime.Start(envelope.Identity);
            if (!start.Ok) return start;
            var round = Runtime.BeginRound(envelope.RoundKey ?? string.Empty);
            if (!round.Ok) return round;
            if (round.Code != "DUPLICATE_ROUND") await SaveRoundCheckpointAsync(envelope.RoundKey!);
            Hud.Start();
            await SaveCurrentNowAsync();
            return new(true);
        }

        if (envelope.Action == "restore") return await RestoreCheckpointAsync(envelope);

        var identity = ValidateCurrentIdentity(envelope.Identity);
        if (!identity.Ok) return identity;
        switch (envelope.Action)
        {
            case "round_start":
                return await OnRoundStartAsync(envelope.RoundKey ?? string.Empty);
            case "stop":
                Hud.Stop();
                Movement.ClearAll();
                Runtime.Stop();
                await _persistence.DeleteAsync();
                return new(true);
            case "substitute":
                var substitute = Runtime.QueueSubstitution(envelope.SeatId ?? string.Empty, envelope.SteamId ?? string.Empty);
                if (substitute.Ok) await SaveCurrentNowAsync();
                return substitute;
            case "round_control":
                var outcome = envelope.Outcome switch
                {
                    "draw" => RoundOutcome.Draw,
                    "restarted" => RoundOutcome.Restarted,
                    "admin_cancelled" => RoundOutcome.AdminCancelled,
                    _ => (RoundOutcome?)null,
                };
                if (outcome is null) return new(false, "INVALID_ROUND_CONTROL");
                Runtime.CompleteRound(envelope.RoundKey ?? string.Empty, outcome.Value);
                await SaveCurrentNowAsync();
                return new(true);
            default:
                return new(false, "UNKNOWN_RUNTIME_ACTION");
        }
    }

    public async Task<RuntimeOperationResult> OnRoundStartAsync(string roundKey)
    {
        var result = Runtime.BeginRound(roundKey);
        if (result.Ok)
        {
            if (result.Code != "DUPLICATE_ROUND") await SaveRoundCheckpointAsync(roundKey);
            await SaveCurrentNowAsync();
        }
        return result;
    }

    public async Task<RoundSettlementResult> OnRoundCompletedAsync(string roundKey, RoundOutcome outcome)
    {
        var result = Runtime.CompleteRound(roundKey, outcome);
        if (result.Counted) await SaveCurrentNowAsync();
        return result;
    }

    public async Task<DeathSettlementResult> OnDeathAsync(string lifeKey, string victimSeatId, string? attackerSeatId)
    {
        var result = Runtime.RecordDeath(lifeKey, victimSeatId, attackerSeatId);
        if (result.Counted && Runtime.State is not null) _persistence.Schedule(Runtime.State);
        await Task.CompletedTask;
        return result;
    }

    public Task FlushNowAsync() => SaveCurrentNowAsync();

    public Task CreateCheckpointAsync(string checkpointId) => Runtime.State is { } state
        ? _persistence.SaveCheckpointAsync(checkpointId, state)
        : Task.FromException(new InvalidOperationException("当前没有可保存的异能运行时。"));

    public void ScheduleCurrent()
    {
        if (Runtime.State is { } state) _persistence.Schedule(state);
    }

    public async Task ClearAsync()
    {
        Hud.Stop();
        Movement.ClearAll();
        Runtime.Stop();
        await _persistence.DeleteAsync();
    }

    private RuntimeOperationResult ValidateCurrentIdentity(AbilityRuntimeIdentity identity)
    {
        var state = Runtime.State;
        return state is not null
            && state.MatchId == identity.MatchId
            && state.Revision == identity.Revision
            && state.ContentDigest == identity.ContentDigest
            && state.RuntimeEpoch == identity.RuntimeEpoch
            ? new(true)
            : new(false, "IDENTITY_MISMATCH", "运行时命令不属于当前已确认比赛。");
    }

    private async Task<RuntimeOperationResult> RestoreCheckpointAsync(AbilityRuntimeEnvelope envelope)
    {
        var current = Runtime.State;
        if (current is null) return new(false, "RUNTIME_NOT_RUNNING");
        if (current.MatchId != envelope.MatchId
            || current.Revision != envelope.Revision
            || current.ContentDigest != envelope.ContentDigest
            || envelope.RuntimeEpoch <= current.RuntimeEpoch)
            return new(false, "IDENTITY_MISMATCH", "恢复命令不属于当前比赛的新 epoch。");
        if (string.IsNullOrWhiteSpace(envelope.CheckpointId) || string.IsNullOrWhiteSpace(envelope.RoundKey))
            return new(false, "INVALID_RESTORE_COMMAND");

        var restored = await _persistence.LoadCheckpointAsync(
            envelope.CheckpointId,
            new AbilityRuntimeIdentity(envelope.MatchId, envelope.Revision, envelope.ContentDigest, 0));
        if (restored is null) return new(false, "CHECKPOINT_NOT_FOUND");
        restored.RuntimeEpoch = envelope.RuntimeEpoch;
        restored.Lifecycle = AbilityRuntimeLifecycle.Running;
        restored.CurrentRoundKey = null;
        Runtime.Restore(restored);
        var round = Runtime.BeginRound(envelope.RoundKey);
        if (!round.Ok) return round;
        Movement.ClearAll();
        Hud.Start();
        await SaveCurrentNowAsync();
        return new(true);
    }

    private Task SaveCurrentNowAsync() => Runtime.State is { } state
        ? _persistence.SaveNowAsync(state)
        : Task.CompletedTask;

    private Task SaveRoundCheckpointAsync(string roundKey)
    {
        var checkpointId = roundKey.Split(':', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return Runtime.State is { } state && !string.IsNullOrWhiteSpace(checkpointId)
            ? _persistence.SaveCheckpointAsync(checkpointId, state)
            : Task.FromException(new InvalidOperationException("权威回合标识不能生成检查点。"));
    }
}
