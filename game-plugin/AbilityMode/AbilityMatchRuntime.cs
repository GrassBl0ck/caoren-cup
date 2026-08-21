using System.Text.RegularExpressions;

namespace Caoren.AbilityMode;

public enum AbilityRuntimeLifecycle { Armed, Running, Stopped }
public enum AbilityReleaseModel { A, B, C }
public enum ChargeReason { Kill, Death, RoundWin, RoundLoss, Purchase, Test }
public enum RoundOutcome { TeamAWin, TeamBWin, Draw, Restarted, AdminCancelled }

public sealed record AbilityDefinition(
    string Id,
    string Name,
    int ChargeCap,
    AbilityReleaseModel ReleaseModel,
    int FixedCost = 0,
    int MinimumCharge = 0,
    bool AllowsEmptyCast = false,
    Func<int, string>? CModelPreview = null);

public sealed class AbilityDefinitionCatalog
{
    private readonly Dictionary<string, AbilityDefinition> _definitions;

    private AbilityDefinitionCatalog(IEnumerable<AbilityDefinition> definitions)
    {
        _definitions = definitions.ToDictionary(item => item.Id, StringComparer.Ordinal);
        if (_definitions.Count == 0 || _definitions.Values.Any(item => item.ChargeCap <= 0))
            throw new ArgumentException("职业目录必须包含充能上限为正数的职业。");
    }

    public static AbilityDefinitionCatalog CreateForRuntime(IEnumerable<AbilityDefinition> definitions) => new(definitions);

    public static AbilityDefinitionCatalog CreateProduction() => new([
        new("medic", "医师", 12, AbilityReleaseModel.A, AllowsEmptyCast: true),
        new("berserker", "狂战士", 12, AbilityReleaseModel.C, MinimumCharge: 8,
            CModelPreview: charge => charge < 8 ? "最低 8 点" : $"射速提高 {(charge >= 12 ? 30 : charge >= 10 ? 25 : 20)}%"),
        new("assassin", "刺客", 12, AbilityReleaseModel.A),
        new("tank", "坦克", 12, AbilityReleaseModel.B, FixedCost: 8),
        new("istaru", "伊斯塔露", 14, AbilityReleaseModel.A),
        new("capitalist", "资本家", 14, AbilityReleaseModel.C, MinimumCharge: 8,
            CModelPreview: charge => charge < 8 ? "最低 8 点" : $"掠夺 {20 + (Math.Min(14, charge) - 8) * 10d / 3d:0.#}%"),
        new("balance", "天平", 12, AbilityReleaseModel.A, AllowsEmptyCast: true),
        new("glass_cannon", "玻璃大炮", 14, AbilityReleaseModel.B, FixedCost: 10),
        new("utility_specialist", "道具手", 10, AbilityReleaseModel.A, AllowsEmptyCast: true),
        new("commander", "指挥官", 10, AbilityReleaseModel.B, FixedCost: 5, AllowsEmptyCast: true),
        new("sky_courier", "超级飞侠", 8, AbilityReleaseModel.A, AllowsEmptyCast: true),
        new("snow_golem", "雪傀儡", 12, AbilityReleaseModel.C, MinimumCharge: 6, AllowsEmptyCast: true,
            CModelPreview: charge => charge < 6 ? "最低 6 点" : $"持续 {10 + (Math.Min(12, charge) - 6) * 5d / 3d:0.#} 秒"),
        new("witch", "女巫", 10, AbilityReleaseModel.A, AllowsEmptyCast: true),
    ]);

    public bool TryGet(string id, out AbilityDefinition definition) => _definitions.TryGetValue(id, out definition!);
    public AbilityDefinition Get(string id) => _definitions.TryGetValue(id, out var value)
        ? value
        : throw new KeyNotFoundException($"未知职业：{id}");
}

public sealed record AbilityRuntimeIdentity(string MatchId, int Revision, string ContentDigest);
public sealed record RuntimeOperationResult(bool Ok, string? Code = null, string? Message = null);
public sealed record RuntimeActivationResult(bool Activated, bool Duplicate = false, bool ReplacedPreviousMatch = false, string? Code = null);
public sealed record DeathSettlementResult(bool Counted);
public sealed record RoundSettlementResult(bool Counted);

public sealed class AbilitySeatState
{
    public AbilitySeatState(AbilitySyncSeat source, AbilityDefinition definition)
    {
        SeatId = source.PlayerId;
        PlayerId = source.PlayerId;
        CurrentSteamId = source.SteamId;
        RosterTeam = source.RosterTeam;
        InitialSide = source.InitialSide;
        AbilityId = source.AbilityId;
        Definition = definition;
    }

    public string SeatId { get; }
    public string PlayerId { get; }
    public string CurrentSteamId { get; internal set; }
    public string RosterTeam { get; }
    public string InitialSide { get; }
    public string AbilityId { get; }
    public AbilityDefinition Definition { get; }
    public int Charge { get; private set; }
    public bool AbilityUsedThisRound { get; private set; }
    public bool ChargePurchasedThisRound { get; private set; }
    public bool IsConnected { get; internal set; } = true;
    public bool IsAlive { get; internal set; }
    public string? PendingSubstituteSteamId { get; internal set; }
    public int LifeSequence { get; internal set; }
    public string? CurrentLifeKey { get; internal set; }
    public Dictionary<string, string> CrossRoundState { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> RoundTemporaryState { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, DateTimeOffset> RecoverableUntil { get; } = new(StringComparer.Ordinal);

    internal void SetCharge(int value) => Charge = Math.Clamp(value, 0, Definition.ChargeCap);
    public void MarkAbilityUsed() => AbilityUsedThisRound = true;
    public void MarkChargePurchased() => ChargePurchasedThisRound = true;

    public string BeginLife(string roundKey)
    {
        LifeSequence++;
        CurrentLifeKey = $"{roundKey}:{SeatId}:{LifeSequence}";
        IsAlive = true;
        return CurrentLifeKey;
    }

    internal void ResetRoundState()
    {
        AbilityUsedThisRound = false;
        ChargePurchasedThisRound = false;
        RoundTemporaryState.Clear();
    }

    internal void RestorePerRoundFlags(bool abilityUsed, bool chargePurchased)
    {
        AbilityUsedThisRound = abilityUsed;
        ChargePurchasedThisRound = chargePurchased;
    }
}

public sealed class AbilityMatchState
{
    internal AbilityMatchState(AbilitySyncConfig config, AbilityDefinitionCatalog catalog)
    {
        MatchId = config.MatchId;
        Revision = config.Revision;
        CatalogVersion = config.CatalogVersion;
        ContentDigest = config.ContentDigest;
        Seats = config.Seats.ToDictionary(
            seat => seat.PlayerId,
            seat => new AbilitySeatState(seat, catalog.Get(seat.AbilityId)),
            StringComparer.Ordinal);
    }

    public string MatchId { get; }
    public int Revision { get; }
    public string CatalogVersion { get; }
    public string ContentDigest { get; }
    public AbilityRuntimeLifecycle Lifecycle { get; internal set; } = AbilityRuntimeLifecycle.Armed;
    public string? CurrentRoundKey { get; internal set; }
    public Dictionary<string, AbilitySeatState> Seats { get; }
    internal HashSet<string> SettledLives { get; } = new(StringComparer.Ordinal);
    internal HashSet<string> SettledRounds { get; } = new(StringComparer.Ordinal);
}

public sealed class ChargeService
{
    public int Add(AbilitySeatState seat, int amount, ChargeReason reason)
    {
        if (amount <= 0) return 0;
        var previous = seat.Charge;
        seat.SetCharge(previous + amount);
        return seat.Charge - previous;
    }

    public bool CanSpend(AbilitySeatState seat, int amount) => amount >= 0 && seat.Charge >= amount;

    public bool TrySpend(AbilitySeatState seat, int amount)
    {
        if (!CanSpend(seat, amount)) return false;
        seat.SetCharge(seat.Charge - amount);
        return true;
    }

    internal void Restore(AbilitySeatState seat, int charge) => seat.SetCharge(charge);
}

public sealed class AbilityMatchRuntime(AbilityDefinitionCatalog catalog)
{
    private static readonly Regex DigestRegex = new("^[a-f0-9]{64}$", RegexOptions.CultureInvariant);
    public AbilityMatchState? State { get; private set; }
    public ChargeService Charges { get; } = new();

    internal void Restore(AbilityMatchState state) => State = state;
    public void RestoreForHotReload(AbilityMatchState state) => Restore(state);

    public RuntimeActivationResult ApplyConfirmed(AbilitySyncConfig config)
    {
        if (!config.AbilityModeEnabled || config.Revision <= 0 || string.IsNullOrWhiteSpace(config.MatchId)
            || !DigestRegex.IsMatch(config.ContentDigest) || config.Seats.Count == 0
            || config.Seats.Any(seat => !catalog.TryGet(seat.AbilityId, out _)))
        {
            return new(false, Code: "CONFIG_NOT_CONFIRMED");
        }

        if (State is not null
            && State.MatchId == config.MatchId
            && State.Revision == config.Revision
            && State.ContentDigest == config.ContentDigest)
        {
            return new(true, Duplicate: true);
        }

        var replacedPreviousMatch = State is not null && State.MatchId != config.MatchId;
        State = new AbilityMatchState(config, catalog);
        return new(true, ReplacedPreviousMatch: replacedPreviousMatch);
    }

    public RuntimeOperationResult Start(AbilityRuntimeIdentity identity)
    {
        if (State is null) return new(false, "NO_CONFIRMED_CONFIG", "当前没有已确认的异能配置。");
        if (State.MatchId != identity.MatchId || State.Revision != identity.Revision || State.ContentDigest != identity.ContentDigest)
            return new(false, "IDENTITY_MISMATCH", "运行时身份与已确认配置不一致。");
        State.Lifecycle = AbilityRuntimeLifecycle.Running;
        return new(true);
    }

    public void Stop()
    {
        if (State is not null) State.Lifecycle = AbilityRuntimeLifecycle.Stopped;
        State = null;
    }

    public bool Disconnect(string steamId)
    {
        var seat = FindBySteamId(steamId);
        if (seat is null) return false;
        seat.IsConnected = false;
        return true;
    }

    public bool Reconnect(string steamId)
    {
        var seat = FindBySteamId(steamId);
        if (seat is null) return false;
        seat.IsConnected = true;
        return true;
    }

    public RuntimeOperationResult QueueSubstitution(string seatId, string newSteamId)
    {
        if (State?.Lifecycle != AbilityRuntimeLifecycle.Running) return new(false, "RUNTIME_NOT_RUNNING");
        if (!RegexHelpers.IsSteamId(newSteamId)) return new(false, "INVALID_STEAM_ID");
        if (!State.Seats.TryGetValue(seatId, out var seat)) return new(false, "SEAT_NOT_FOUND");
        if (State.Seats.Values.Any(item => item.CurrentSteamId == newSteamId || item.PendingSubstituteSteamId == newSteamId))
            return new(false, "STEAM_ID_IN_USE");
        seat.PendingSubstituteSteamId = newSteamId;
        return new(true);
    }

    public RuntimeOperationResult BeginRound(string roundKey)
    {
        if (State?.Lifecycle != AbilityRuntimeLifecycle.Running) return new(false, "RUNTIME_NOT_RUNNING");
        if (string.IsNullOrWhiteSpace(roundKey)) return new(false, "ROUND_KEY_REQUIRED");
        if (State.CurrentRoundKey == roundKey) return new(true, "DUPLICATE_ROUND");
        State.CurrentRoundKey = roundKey;
        foreach (var seat in State.Seats.Values)
        {
            seat.ResetRoundState();
            if (seat.PendingSubstituteSteamId is not { } replacement) continue;
            seat.CurrentSteamId = replacement;
            seat.PendingSubstituteSteamId = null;
            seat.IsConnected = false;
            seat.IsAlive = false;
        }
        return new(true);
    }

    public DeathSettlementResult RecordDeath(string lifeKey, string victimSeatId, string? attackerSeatId)
    {
        if (State?.Lifecycle != AbilityRuntimeLifecycle.Running || string.IsNullOrWhiteSpace(lifeKey)
            || !State.Seats.TryGetValue(victimSeatId, out var victim) || !State.SettledLives.Add(lifeKey))
            return new(false);

        victim.IsAlive = false;
        Charges.Add(victim, 1, ChargeReason.Death);
        if (attackerSeatId is not null
            && attackerSeatId != victimSeatId
            && State.Seats.TryGetValue(attackerSeatId, out var attacker)
            && attacker.RosterTeam != victim.RosterTeam)
        {
            Charges.Add(attacker, 2, ChargeReason.Kill);
        }
        return new(true);
    }

    public RoundSettlementResult CompleteRound(string roundKey, RoundOutcome outcome)
    {
        if (State?.Lifecycle != AbilityRuntimeLifecycle.Running || string.IsNullOrWhiteSpace(roundKey)
            || !State.SettledRounds.Add(roundKey)) return new(false);
        if (outcome is not (RoundOutcome.TeamAWin or RoundOutcome.TeamBWin)) return new(true);
        var winner = outcome == RoundOutcome.TeamAWin ? "A" : "B";
        foreach (var seat in State.Seats.Values)
            Charges.Add(seat, seat.RosterTeam == winner ? 1 : 2,
                seat.RosterTeam == winner ? ChargeReason.RoundWin : ChargeReason.RoundLoss);
        return new(true);
    }

    private AbilitySeatState? FindBySteamId(string steamId) =>
        State?.Seats.Values.FirstOrDefault(seat => seat.CurrentSteamId == steamId);
}
