namespace Caoren.AbilityMode;

public sealed class AbilityRoleCoordinator
{
    private readonly AbilityRoleRegistry _registry;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<int> _maximumMoney;
    private readonly Dictionary<string, AbilityRolePlayer> _players = new(StringComparer.Ordinal);

    public AbilityRoleCoordinator(
        AbilityRoleRegistry registry,
        MovementModifierService movement,
        Func<DateTimeOffset>? now = null,
        Func<int>? maximumMoney = null)
    {
        _registry = registry;
        Movement = movement;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _maximumMoney = maximumMoney ?? (() => 16_000);
        ActiveHandlers = BuildActiveHandlers();
    }

    public MovementModifierService Movement { get; }
    public MedicRoleHandler Medic { get; } = new();
    public BerserkerRoleHandler Berserker { get; } = new();
    public TankRoleHandler Tank { get; } = new();
    public CapitalistRoleHandler Capitalist { get; } = new();
    public BalanceRoleHandler Balance { get; } = new();
    public CommanderRoleHandler Commander { get; } = new();
    public IReadOnlyDictionary<string, IAbilityEffectHandler> ActiveHandlers { get; }
    public IReadOnlyCollection<AbilityRolePlayer> Players => _players.Values;

    public void BindSeats(IEnumerable<AbilitySeatState> seats)
    {
        var incoming = seats.ToDictionary(seat => seat.SeatId, StringComparer.Ordinal);
        foreach (var removed in _players.Keys.Except(incoming.Keys, StringComparer.Ordinal).ToArray())
            _players.Remove(removed);
        foreach (var seat in incoming.Values)
        {
            if (_players.TryGetValue(seat.SeatId, out var existing) && ReferenceEquals(existing.Seat, seat)) continue;
            var player = new AbilityRolePlayer(seat)
            {
                IsAlive = seat.IsAlive,
                IsConnected = seat.IsConnected,
            };
            new SeatEconomyLedger().TryRestore(player);
            Balance.RestoreTemporaryHealth(player);
            Tank.RestoreManagedHealth(player);
            _players[seat.SeatId] = player;
            if (player.IsAlive && seat.AbilityId == "tank"
                && !Tank.IsUltimateActive(seat, _now()))
                Tank.ApplyPassiveMovement(seat, Movement);
            Medic.RestoreSpeed(Movement, seat, _now());
        }
    }

    public AbilityRolePlayer? GetPlayer(string seatId) => _players.GetValueOrDefault(seatId);

    public void OnSpawn(AbilityRolePlayer player, int originalMaxHealth)
    {
        player.IsAlive = true;
        if (player.Seat.AbilityId == "tank")
        {
            Tank.ApplySpawn(player, originalMaxHealth);
            Tank.ApplyPassiveMovement(player.Seat, Movement);
        }
        else
        {
            player.RoleMaxHealth = Math.Max(1, originalMaxHealth);
        }
    }

    public MedicShotResult ResolveMedicShot(
        AbilitySeatState attacker,
        AbilitySeatState target,
        bool isFirearm)
    {
        if (!_players.TryGetValue(target.SeatId, out var targetPlayer))
            return new(false, false, 0, 1, TimeSpan.Zero);
        var result = Medic.ResolveFriendlyShot(
            attacker,
            target,
            _now(),
            isFirearm,
            targetPlayer.BaseHealth,
            targetPlayer.RoleMaxHealth);
        if (result.Triggered)
        {
            targetPlayer.BaseHealth = Math.Min(targetPlayer.RoleMaxHealth, targetPlayer.BaseHealth + result.Healing);
            Medic.ApplySpeed(Movement, target, result, _now());
        }
        return result;
    }

    public void RecordActualHealthLoss(AbilitySeatState target, int actualHealthLoss) =>
        Medic.RecordActualHealthLoss(target, _now(), actualHealthLoss);

    public bool OnConfirmedKill(AbilityRolePlayer killer, AbilityRolePlayer victim) =>
        Berserker.OnConfirmedEnemyKill(killer, victim, finalDeathConfirmed: true);

    public void OnDeath(AbilityRolePlayer player)
    {
        player.IsAlive = false;
        if (player.Seat.AbilityId == "tank") Tank.EndUltimate(player.Seat, Movement, restorePenalty: false);
        if (player.Seat.AbilityId == "commander") Commander.Clear(player.Seat);
        Balance.ClearTemporaryHealth(player);
        Movement.Remove(player.Seat.SeatId, MedicRoleHandler.MovementSource);
        player.Seat.RecoverableUntil.Remove(MedicRoleHandler.SpeedStateKey);
        player.Seat.RoundTemporaryState.Remove(MedicRoleHandler.SpeedMultiplierStateKey);
    }

    public double GetIncomingDamageMultiplier(
        AbilitySeatState victim,
        bool isTrueDamage,
        RoleDamageKind damageKind,
        double signedFrontAngleDegrees)
    {
        return victim.AbilityId switch
        {
            "berserker" => Berserker.GetIncomingDamageMultiplier(victim, isTrueDamage),
            "tank" => Tank.GetIncomingDamageMultiplier(victim, isTrueDamage, damageKind, signedFrontAngleDegrees, _now()),
            _ => 1,
        };
    }

    public double GetOutgoingDamageMultiplier(AbilitySeatState attacker, RoleDamageKind damageKind) =>
        attacker.AbilityId == "tank"
            ? Tank.GetOutgoingLifeDamageMultiplier(attacker, damageKind, _now())
            : 1;

    public void Tick()
    {
        var now = _now();
        foreach (var player in _players.Values)
        {
            if (player.TemporaryHealth > 0) Balance.TickDecay(player, now);
            if (player.Seat.RecoverableUntil.TryGetValue(MedicRoleHandler.SpeedStateKey, out var speedUntil)
                && speedUntil <= now)
            {
                player.Seat.RecoverableUntil.Remove(MedicRoleHandler.SpeedStateKey);
                player.Seat.RoundTemporaryState.Remove(MedicRoleHandler.SpeedMultiplierStateKey);
                Movement.Remove(player.Seat.SeatId, MedicRoleHandler.MovementSource);
            }
            if (player.Seat.AbilityId == "tank"
                && player.Seat.RecoverableUntil.TryGetValue(TankRoleHandler.UltimateStateKey, out var tankUntil)
                && tankUntil <= now)
                Tank.EndUltimate(player.Seat, Movement, restorePenalty: player.IsAlive);
        }
    }

    public void OnRoundEnded()
    {
        foreach (var player in _players.Values)
        {
            Berserker.OnRoundEnded(player.Seat);
            Tank.EndUltimate(player.Seat, Movement, restorePenalty: false);
            Balance.ClearTemporaryHealth(player);
            Commander.Clear(player.Seat);
            Movement.Remove(player.Seat.SeatId, MedicRoleHandler.MovementSource);
            player.Seat.RecoverableUntil.Remove(MedicRoleHandler.SpeedStateKey);
            player.Seat.RoundTemporaryState.Remove(MedicRoleHandler.SpeedMultiplierStateKey);
            player.Seat.RoundTemporaryState.Remove(MedicRoleHandler.CooldownStateKey);
            player.Seat.RoundTemporaryState.Remove(MedicRoleHandler.RecentDamageStateKey);
        }
    }

    public void ClearAll()
    {
        foreach (var player in _players.Values)
        {
            Berserker.OnRoundEnded(player.Seat);
            Tank.OnCleanup(player.Seat, Movement);
            Balance.ClearTemporaryHealth(player);
            Commander.Clear(player.Seat);
            player.Seat.RecoverableUntil.Remove(MedicRoleHandler.SpeedStateKey);
            player.Seat.RoundTemporaryState.Remove(MedicRoleHandler.SpeedMultiplierStateKey);
            player.Seat.RoundTemporaryState.Remove(MedicRoleHandler.CooldownStateKey);
            player.Seat.RoundTemporaryState.Remove(MedicRoleHandler.RecentDamageStateKey);
        }
        Movement.ClearAll();
    }

    private IReadOnlyDictionary<string, IAbilityEffectHandler> BuildActiveHandlers()
    {
        var handlers = new Dictionary<string, IAbilityEffectHandler>(StringComparer.Ordinal);
        Add("medic", (seat, _) => Medic.ApplyUltimate(seat, _players.Values));
        Add("berserker", (seat, charge) => Berserker.ActivateUltimate(seat, charge));
        Add("tank", (seat, _) => Tank.ActivateUltimate(seat, Movement, _now()));
        Add("capitalist", (seat, charge) =>
        {
            var maximumMoney = Math.Max(0, _maximumMoney());
            var plan = Capitalist.BuildPlunderPlan(seat, _players.Values, charge, maximumMoney);
            if (!plan.Ok) return AbilityEffectResult.Failed(plan.Code, "当前没有可掠夺的敌方金钱。");
            var operationId = $"ult:{seat.CurrentLifeKey ?? seat.SeatId}";
            return Capitalist.TryApplyPlan(plan, _players.Values, operationId, maximumMoney)
                ? AbilityEffectResult.Succeeded()
                : AbilityEffectResult.Failed("ECONOMY_PLAN_STALE", "经济状态已变化，请重试。");
        });
        Add("balance", (seat, _) =>
        {
            if (!_players.TryGetValue(seat.SeatId, out var caster))
                return AbilityEffectResult.Failed("CASTER_NOT_BOUND", "施法者席位状态不存在。");
            var result = Balance.ApplyUltimate(caster, _players.Values, _now());
            return AbilityEffectResult.Succeeded(result.EmptyCast);
        });
        Add("commander", (seat, _) =>
        {
            var hasEnemies = _players.Values.Any(player => player.IsAlive && player.Seat.RosterTeam != seat.RosterTeam);
            return Commander.ActivateUltimate(seat, _now(), hasEnemies);
        });
        return handlers;

        void Add(string roleId, Func<AbilitySeatState, int, AbilityEffectResult> apply)
        {
            if (_registry.IsEnabled(roleId)) handlers[roleId] = new DelegatingEffectHandler(apply);
        }
    }

    private sealed class DelegatingEffectHandler(Func<AbilitySeatState, int, AbilityEffectResult> apply)
        : IAbilityEffectHandler
    {
        public AbilityEffectResult TryCreateEffect(AbilitySeatState seat, int currentCharge) => apply(seat, currentCharge);
    }
}
