using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using System.Numerics;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace Caoren.AbilityMode;

public sealed class CounterStrikeAbilityRuntimeAdapter
{
    private readonly CaorenCup.CaorenCupPlugin _plugin;
    private readonly RuntimePersistence _persistence;
    private readonly AbilityCommandService _commands;
    private readonly DamagePipeline _damagePipeline = new(new NoopDamageEngine());
    private readonly AbilityRoleRegistry _roleRegistry;
    private readonly AbilityRoleCoordinator _roles;
    private readonly Dictionary<string, Queue<DamagePreparation>> _pendingDamage = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DamagePreparation> _lastDamageByVictim = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _baselineVelocityBySteamId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _baselineMaxHealthBySteamId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PlayerButtons> _previousButtonsBySteamId = new(StringComparer.Ordinal);
    private Timer? _hudTimer;
    private float _roundStartedAt;
    private int _roundSequence;
    private int _callbackGeneration;
    private bool _registered;

    public CounterStrikeAbilityRuntimeAdapter(CaorenCup.CaorenCupPlugin plugin, string statePath)
    {
        _plugin = plugin;
        var catalog = AbilityDefinitionCatalog.CreateProduction();
        _roleRegistry = AbilityRoleRegistry.Create(plugin.Config.AbilityRoles);
        _persistence = new RuntimePersistence(statePath, catalog);
        Controller = new AbilityRuntimeController(catalog, _persistence, _roleRegistry);
        _roles = new AbilityRoleCoordinator(_roleRegistry, Controller.Movement, maximumMoney: GetMaximumMoney);
        _commands = new AbilityCommandService(Controller.Runtime.Charges, _roles.ActiveHandlers);
        _damagePipeline.Replacements.Add(new RoleMedicDamageReplacement(this));
        _damagePipeline.AttackerModifiers.Add(new RoleAttackerDamageModifier(this));
        _damagePipeline.VictimMitigations.Add(new RoleVictimDamageModifier(this));
    }

    public AbilityRuntimeController Controller { get; }

    public void Register()
    {
        if (_registered) return;
        _plugin.AddCommand("css_ability_runtime_start", "内部异能运行时开始", OnRuntimeStartCommand);
        _plugin.AddCommand("css_ability_runtime_stop", "内部异能运行时停止", OnRuntimeStopCommand);
        _plugin.AddCommand("css_ability_runtime_substitute", "内部异能运行时正式替补", OnRuntimeSubstituteCommand);
        _plugin.AddCommand("css_ability_runtime_round_control", "内部异能运行时回合控制", OnRuntimeRoundControlCommand);
        _plugin.AddCommand("css_charge", "购买异能充能：.charge buy", OnChargeCommand);
        _plugin.AddCommand("charge", "购买异能充能：.charge buy", OnChargeCommand);
        _plugin.AddCommand("css_ult", "使用职业主动技能：.ult", OnUltCommand);
        _plugin.AddCommand("ult", "使用职业主动技能：.ult", OnUltCommand);
        _plugin.RegisterEventHandler<EventRoundStart>(OnRoundStart, HookMode.Post);
        _plugin.RegisterEventHandler<EventRoundPrestart>(OnRoundPrestart, HookMode.Post);
        _plugin.RegisterEventHandler<EventRoundEnd>(OnRoundEnd, HookMode.Post);
        _plugin.RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn, HookMode.Post);
        _plugin.RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath, HookMode.Post);
        _plugin.RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt, HookMode.Post);
        _plugin.RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect, HookMode.Post);
        _plugin.RegisterEventHandler<EventWeaponFire>(OnWeaponFire, HookMode.Post);
        _plugin.RegisterEventHandler<EventBombPlanted>(OnBombPlanted, HookMode.Post);
        _plugin.RegisterEventHandler<EventBombDefused>(OnBombDefused, HookMode.Post);
        _plugin.RegisterEventHandler<EventPlayerChat>(OnPlayerChat, HookMode.Pre);
        _plugin.RegisterListener<Listeners.OnPlayerTakeDamagePre>(OnPlayerTakeDamagePre);
        _plugin.RegisterListener<Listeners.OnTick>(OnTick);
        _registered = true;
        if (Controller.Hud.IsRunning) StartHudTimer();
        Server.ExecuteCommand("css_ability_runtime_ready");
    }

    public AbilitySyncConfig? LoadConfirmedConfig() =>
        _persistence.LoadConfirmedConfigAsync().GetAwaiter().GetResult();

    public AbilityRoleValidationResult ValidateRoleConfig(AbilitySyncConfig config) =>
        _roleRegistry.ValidateSelectedRoles(config);

    public void SaveConfirmedConfig(AbilitySyncConfig config) =>
        _persistence.SaveConfirmedConfigAsync(config).GetAwaiter().GetResult();

    public RuntimeInitializationResult ApplyConfirmedConfig(AbilitySyncConfig config)
    {
        var result = Controller.InitializeFromConfirmedAsync(config).GetAwaiter().GetResult();
        if (result.Ok && Controller.Runtime.State is { } state)
            _roles.BindSeats(state.Seats.Values);
        return result;
    }

    public void ClearRuntime()
    {
        _callbackGeneration++;
        _pendingDamage.Clear();
        _lastDamageByVictim.Clear();
        RestoreManagedMovement();
        RestoreManagedHealth();
        _roles.ClearAll();
        _previousButtonsBySteamId.Clear();
        StopHudTimer();
        _pendingDamage.Clear();
        _lastDamageByVictim.Clear();
        Controller.ClearAsync().GetAwaiter().GetResult();
    }

    public void Unload(bool hotReload)
    {
        _callbackGeneration++;
        if (Controller.Runtime.State is not null)
        {
            SynchronizeAllOnlineRolePlayers();
            Controller.FlushNowAsync().GetAwaiter().GetResult();
        }
        RestoreManagedMovement();
        RestoreManagedHealth();
        StopHudTimer();
        Controller.Hud.Stop();
        Controller.Movement.ClearAll();
        if (!hotReload)
        {
            _roles.ClearAll();
            Controller.ClearAsync().GetAwaiter().GetResult();
        }
        _previousButtonsBySteamId.Clear();
        if (_registered)
        {
            try { _plugin.DeregisterEventHandler<EventRoundStart>(OnRoundStart, HookMode.Post); } catch { }
            try { _plugin.DeregisterEventHandler<EventRoundPrestart>(OnRoundPrestart, HookMode.Post); } catch { }
            try { _plugin.DeregisterEventHandler<EventRoundEnd>(OnRoundEnd, HookMode.Post); } catch { }
            try { _plugin.DeregisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn, HookMode.Post); } catch { }
            try { _plugin.DeregisterEventHandler<EventPlayerDeath>(OnPlayerDeath, HookMode.Post); } catch { }
            try { _plugin.DeregisterEventHandler<EventPlayerHurt>(OnPlayerHurt, HookMode.Post); } catch { }
            try { _plugin.DeregisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect, HookMode.Post); } catch { }
            try { _plugin.DeregisterEventHandler<EventWeaponFire>(OnWeaponFire, HookMode.Post); } catch { }
            try { _plugin.DeregisterEventHandler<EventBombPlanted>(OnBombPlanted, HookMode.Post); } catch { }
            try { _plugin.DeregisterEventHandler<EventBombDefused>(OnBombDefused, HookMode.Post); } catch { }
            try { _plugin.DeregisterEventHandler<EventPlayerChat>(OnPlayerChat, HookMode.Pre); } catch { }
            try { _plugin.RemoveListener<Listeners.OnPlayerTakeDamagePre>(OnPlayerTakeDamagePre); } catch { }
            try { _plugin.RemoveListener<Listeners.OnTick>(OnTick); } catch { }
            _registered = false;
        }
        _persistence.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private void OnRuntimeStartCommand(CCSPlayerController? player, CommandInfo info) => HandleRuntimeCommand(player, info, "start");
    private void OnRuntimeStopCommand(CCSPlayerController? player, CommandInfo info) => HandleRuntimeCommand(player, info, "stop");
    private void OnRuntimeSubstituteCommand(CCSPlayerController? player, CommandInfo info) => HandleRuntimeCommand(player, info, "substitute");
    private void OnRuntimeRoundControlCommand(CCSPlayerController? player, CommandInfo info) => HandleRuntimeCommand(player, info, "round_control");

    private void HandleRuntimeCommand(CCSPlayerController? player, CommandInfo info, string expectedAction)
    {
        if (player is not null || info.ArgCount < 2) return;
        try
        {
            var envelope = AbilitySyncInternalProtocol.Decode<AbilityRuntimeEnvelope>(info.GetArg(1));
            if (envelope.Action != expectedAction) return;
            var result = Controller.HandleAsync(envelope).GetAwaiter().GetResult();
            if (!result.Ok)
            {
                Console.WriteLine($"[CaorenCup] Ability runtime command rejected: {result.Code} {result.Message}");
                return;
            }
            if (expectedAction == "start")
            {
                _callbackGeneration++;
                if (Controller.Runtime.State is { } state) _roles.BindSeats(state.Seats.Values);
                foreach (var tank in _roles.Players.Where(rolePlayer =>
                    rolePlayer.Seat.AbilityId == "tank" && rolePlayer.HasManagedHealthSnapshot))
                    ApplyRolePlayer(tank);
                _roundSequence = ParseRoundSequence(envelope.RoundKey);
                _roundStartedAt = Server.CurrentTime;
                BindCurrentPlayers(false);
                StartHudTimer();
            }
            else if (expectedAction == "stop")
            {
                _callbackGeneration++;
                _pendingDamage.Clear();
                _lastDamageByVictim.Clear();
                RestoreManagedMovement();
                RestoreManagedHealth();
                _roles.ClearAll();
                StopHudTimer();
            }
            else if (expectedAction == "round_control")
            {
                _roles.OnRoundEnded();
                ApplyAllRolePlayers();
                Controller.FlushNowAsync().GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CaorenCup] Invalid ability runtime command: {ex.Message}");
        }
    }

    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        if (!IsRunning()) return HookResult.Continue;
        _roundStartedAt = Server.CurrentTime;
        _roundSequence = ResolveEngineRoundSequence() ?? Math.Max(1, _roundSequence + 1);
        var nextRoundKey = $"round-{_roundSequence}";
        if (Controller.Runtime.State?.CurrentRoundKey != nextRoundKey) _roles.OnRoundEnded();
        Controller.OnRoundStartAsync(nextRoundKey).GetAwaiter().GetResult();
        if (Controller.Runtime.State is { } currentState) _roles.BindSeats(currentState.Seats.Values);
        _pendingDamage.Clear();
        _lastDamageByVictim.Clear();
        BindCurrentPlayers(false);
        return HookResult.Continue;
    }

    private HookResult OnRoundPrestart(EventRoundPrestart @event, GameEventInfo info)
    {
        if (!IsRunning()) return HookResult.Continue;
        var round = ResolveEngineRoundSequence() ?? Math.Max(1, _roundSequence);
        var maxRounds = Math.Max(2, ConVar.Find("mp_maxrounds")?.GetPrimitiveValue<int>() ?? 24);
        var perHalf = Math.Max(1, maxRounds / 2);
        var context = new BalanceRoundContext(round, perHalf, round > maxRounds);
        SynchronizeAllOnlineRolePlayers();
        foreach (var rolePlayer in _roles.Players)
        {
            if (rolePlayer.Seat.AbilityId != "balance") continue;
            _roles.Balance.TryApplyBuyPhase(rolePlayer, context, GetMaximumMoney());
        }
        ApplyAllRolePlayers();
        Controller.ScheduleCurrent();
        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        var state = Controller.Runtime.State;
        if (state?.Lifecycle != AbilityRuntimeLifecycle.Running || state.CurrentRoundKey is null)
            return HookResult.Continue;
        foreach (var player in Utilities.GetPlayers())
            if (TryRealPlayer(player, out var realPlayer)) ObserveCapitalistReward(realPlayer!, EconomySource.RoundReward);
        var winner = ResolveRosterWinner(@event.Winner);
        var outcome = winner switch
        {
            "A" => RoundOutcome.TeamAWin,
            "B" => RoundOutcome.TeamBWin,
            _ => RoundOutcome.Draw,
        };
        Controller.OnRoundCompletedAsync(state.CurrentRoundKey, outcome).GetAwaiter().GetResult();
        _roles.OnRoundEnded();
        ApplyAllRolePlayers();
        Controller.FlushNowAsync().GetAwaiter().GetResult();
        return HookResult.Continue;
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        if (!IsRunning() || !TryRealPlayer(@event.Userid, out var player)) return HookResult.Continue;
        var seat = FindSeat(player!);
        var roundKey = Controller.Runtime.State?.CurrentRoundKey;
        if (seat is null || roundKey is null) return HookResult.Continue;
        var wasConnected = seat.IsConnected;
        Controller.Runtime.Reconnect(seat.CurrentSteamId);
        seat.BeginLife(roundKey);
        SynchronizeRolePlayer(player!, seat, restoreLedgerOnReconnect: !wasConnected);
        if (_roles.GetPlayer(seat.SeatId) is { } rolePlayer)
        {
            var pawn = player!.PlayerPawn?.Value;
            var originalMax = pawn is { IsValid: true }
                ? GetOrCaptureBaselineMaxHealth(player, pawn)
                : 100;
            _roles.OnSpawn(rolePlayer, originalMax);
            ApplyRolePlayer(rolePlayer);
        }
        Controller.ScheduleCurrent();
        ApplyMovement(player!, seat);
        return HookResult.Continue;
    }

    private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        if (!IsRunning() || !TryRealPlayer(@event.Userid, out var victim)) return HookResult.Continue;
        var victimSeat = FindSeat(victim!);
        var state = Controller.Runtime.State;
        if (victimSeat is null || state?.CurrentRoundKey is null) return HookResult.Continue;
        var lifeKey = victimSeat.CurrentLifeKey ?? victimSeat.BeginLife(state.CurrentRoundKey);
        AbilitySeatState? attackerSeat = null;
        if (TryRealPlayer(@event.Attacker, out var attacker))
        {
            attackerSeat = FindSeat(attacker!);
            if (attackerSeat is not null)
                SynchronizeRolePlayer(attacker!, attackerSeat, restoreLedgerOnReconnect: false);
        }
        var result = Controller.OnDeathAsync(lifeKey, victimSeat.SeatId, attackerSeat?.SeatId).GetAwaiter().GetResult();
        if (result.Counted)
        {
            if (_roles.GetPlayer(victimSeat.SeatId) is { } victimRole) _roles.OnDeath(victimRole);
            if (attackerSeat is not null && _roles.GetPlayer(attackerSeat.SeatId) is { } attackerRole
                && attackerRole.IsAlive && attackerSeat.RosterTeam != victimSeat.RosterTeam
                && _roles.GetPlayer(victimSeat.SeatId) is { } defeatedRole)
            {
                _roles.OnConfirmedKill(attackerRole, defeatedRole);
                ApplyRolePlayer(attackerRole);
            }
            if (attacker is not null) ObserveCapitalistReward(attacker, EconomySource.KillReward);
            Controller.ScheduleCurrent();
            Controller.Hud.SetPrompt(victimSeat.SeatId, "死亡，充能 +1", DateTimeOffset.UtcNow);
            if (attackerSeat is not null && attackerSeat.RosterTeam != victimSeat.RosterTeam)
                Controller.Hud.SetPrompt(attackerSeat.SeatId, "击杀敌人，充能 +2", DateTimeOffset.UtcNow);
        }
        if (_lastDamageByVictim.Remove(victimSeat.SeatId, out var lastDamage))
            _damagePipeline.ConfirmDeath(lastDamage, attackerSeat?.SeatId);
        return HookResult.Continue;
    }

    private HookResult OnBombPlanted(EventBombPlanted @event, GameEventInfo info)
    {
        if (IsRunning() && TryRealPlayer(@event.Userid, out var player))
            ObserveCapitalistReward(player!, EconomySource.BombPlantReward);
        return HookResult.Continue;
    }

    private HookResult OnBombDefused(EventBombDefused @event, GameEventInfo info)
    {
        if (IsRunning() && TryRealPlayer(@event.Userid, out var player))
            ObserveCapitalistReward(player!, EconomySource.BombDefuseReward);
        return HookResult.Continue;
    }

    private HookResult OnWeaponFire(EventWeaponFire @event, GameEventInfo info)
    {
        if (!IsRunning() || !TryRealPlayer(@event.Userid, out var player)) return HookResult.Continue;
        var seat = FindSeat(player!);
        if (seat?.AbilityId != "berserker" || !_roles.Berserker.IsUltimateActive(seat))
            return HookResult.Continue;
        var kind = ClassifyDamage("weapon_" + (@event.Weapon ?? string.Empty));
        if (kind != RoleDamageKind.Firearm) return HookResult.Continue;
        var pawn = player!.PlayerPawn?.Value;
        var weapon = pawn?.WeaponServices?.ActiveWeapon.Value;
        if (pawn is not { IsValid: true } || weapon is not { IsValid: true }) return HookResult.Continue;
        if (_roles.Berserker.ShouldPreserveAmmo(seat, WeaponUseKind.MagazineFirearm))
        {
            weapon.Clip1 += 1;
            Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_iClip1");
        }
        var rate = _roles.Berserker.GetFireRateMultiplier(seat);
        ScalePrimaryAttackDelay(weapon, rate);
        var recoil = _roles.Berserker.GetPerShotRecoilMultiplier(seat);
        var generation = _callbackGeneration;
        _plugin.AddTimer(0.01f, () =>
        {
            if (generation != _callbackGeneration || !IsRunning()) return;
            ScaleCurrentRecoil(player, recoil);
        });
        return HookResult.Continue;
    }

    private HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo info)
    {
        if (!IsRunning() || !TryRealPlayer(@event.Userid, out var victim)) return HookResult.Continue;
        var victimSeat = FindSeat(victim!);
        if (victimSeat is null || !_pendingDamage.TryGetValue(victimSeat.SeatId, out var queue) || queue.Count == 0)
            return HookResult.Continue;
        var prepared = queue.Dequeue();
        if (queue.Count == 0) _pendingDamage.Remove(victimSeat.SeatId);
        _damagePipeline.CompleteAfterEngine(prepared, Math.Max(0, @event.DmgHealth), finalDeath: false);
        _roles.RecordActualHealthLoss(victimSeat, Math.Max(0, @event.DmgHealth));
        SynchronizeRolePlayer(victim!, victimSeat, restoreLedgerOnReconnect: false);
        _lastDamageByVictim[victimSeat.SeatId] = prepared;
        return HookResult.Continue;
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player is null || player.SteamID == 0) return HookResult.Continue;
        var seat = FindSeat(player);
        if (seat is not null) SynchronizeRolePlayer(player, seat, restoreLedgerOnReconnect: false);
        Controller.Runtime.Disconnect(player.SteamID.ToString());
        if (seat is not null && _roles.GetPlayer(seat.SeatId) is { } rolePlayer)
        {
            rolePlayer.IsConnected = false;
            rolePlayer.IsAlive = false;
        }
        Controller.ScheduleCurrent();
        return HookResult.Continue;
    }

    private HookResult OnPlayerChat(EventPlayerChat @event, GameEventInfo info)
    {
        var text = (@event.Text ?? string.Empty).Trim();
        if (!TryRealPlayer(Utilities.GetPlayerFromUserid(@event.Userid), out var player)) return HookResult.Continue;
        if (text.Equals(".ult", StringComparison.OrdinalIgnoreCase))
        {
            UseUltimate(player!);
            return HookResult.Handled;
        }
        if (text.Equals(".charge buy", StringComparison.OrdinalIgnoreCase))
        {
            BuyCharge(player!);
            return HookResult.Handled;
        }
        return HookResult.Continue;
    }

    private void OnTick()
    {
        if (!IsRunning()) return;
        foreach (var player in Utilities.GetPlayers())
        {
            if (!TryRealPlayer(player, out var realPlayer) || !realPlayer!.PawnIsAlive) continue;
            var seat = FindSeat(realPlayer);
            if (seat?.AbilityId != "tank") continue;
            var steamId = realPlayer.SteamID.ToString();
            var current = realPlayer.Buttons;
            _previousButtonsBySteamId.TryGetValue(steamId, out var previous);
            _previousButtonsBySteamId[steamId] = current;
            var newlyPressed = (current & PlayerButtons.Jump) != 0 && (previous & PlayerButtons.Jump) == 0;
            if (!newlyPressed || _roles.Tank.GetOwnJumpMultiplier(seat, DateTimeOffset.UtcNow) >= 1) continue;
            var generation = _callbackGeneration;
            _plugin.AddTimer(0.01f, () =>
            {
                if (generation != _callbackGeneration || !IsRunning()
                    || _roles.Tank.GetOwnJumpMultiplier(seat, DateTimeOffset.UtcNow) >= 1) return;
                var pawn = realPlayer.PlayerPawn?.Value;
                if (pawn is not { IsValid: true } || pawn.AbsVelocity.Z <= 0) return;
                pawn.AbsVelocity.Z *= 0.9f;
            });
        }
    }

    private void OnChargeCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player is null) return;
        if (info.ArgCount < 2 || !string.Equals(info.GetArg(1), "buy", StringComparison.OrdinalIgnoreCase))
        {
            CaorenCup.CaorenCupUtils.PrintToChat(player, "用法：.charge buy");
            return;
        }
        BuyCharge(player);
    }

    private void BuyCharge(CCSPlayerController player)
    {
        var seat = FindSeat(player);
        if (seat is null)
        {
            CaorenCup.CaorenCupUtils.PrintToChat(player, "当前玩家未绑定异能比赛席位。");
            return;
        }
        SynchronizeRolePlayer(player, seat, restoreLedgerOnReconnect: false);
        var pawn = player.PlayerPawn?.Value;
        var context = new ChargePurchaseContext(
            IsRunning(),
            true,
            player.PawnIsAlive,
            IsNormalBuyTime(),
            pawn is { IsValid: true } && pawn.InBuyZone);
        var result = _commands.BuyCharge(
            seat,
            context,
            new CounterStrikeMoneyAccount(player),
            () => Controller.FlushNowAsync().GetAwaiter().GetResult());
        CaorenCup.CaorenCupUtils.PrintToChat(player, result.Message);
        if (result.Ok) Controller.Hud.SetPrompt(seat.SeatId, "购买充能 +1（-$750）", DateTimeOffset.UtcNow);
    }

    private void OnUltCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player is not null) UseUltimate(player);
    }

    private void UseUltimate(CCSPlayerController player)
    {
        var seat = FindSeat(player);
        if (seat is null)
        {
            CaorenCup.CaorenCupUtils.PrintToChat(player, "当前玩家未绑定异能比赛席位。");
            return;
        }
        SynchronizeAllOnlineRolePlayers();
        var result = _commands.UseUltimate(
            seat,
            new(IsRunning(), IsRunning(), true, player.PawnIsAlive, true),
            () => Controller.FlushNowAsync().GetAwaiter().GetResult());
        if (result.Ok)
        {
            ApplyAllRolePlayers();
            Controller.ScheduleCurrent();
        }
        CaorenCup.CaorenCupUtils.PrintToChat(player, result.Message);
        if (!result.Ok) Controller.Hud.SetPrompt(seat.SeatId, result.Message, DateTimeOffset.UtcNow);
    }

    private HookResult OnPlayerTakeDamagePre(CCSPlayerPawn victimPawn, CTakeDamageInfo damageInfo)
    {
        if (!IsRunning() || victimPawn is not { IsValid: true }) return HookResult.Continue;
        var victim = victimPawn.Controller.Value as CCSPlayerController;
        var attackerPawn = damageInfo.Attacker.Value?.As<CCSPlayerPawn>();
        var attacker = attackerPawn?.Controller.Value as CCSPlayerController;
        var victimSeat = victim is null ? null : FindSeat(victim);
        if (victimSeat is null) return HookResult.Continue;
        var attackerSeat = attacker is null ? null : FindSeat(attacker);
        var damageDesignerName = damageInfo.Ability.Value?.DesignerName
            ?? attackerPawn?.WeaponServices?.ActiveWeapon.Value?.DesignerName
            ?? string.Empty;
        var damageKind = ClassifyDamage(damageDesignerName);
        if (attackerSeat is not null)
        {
            SynchronizeRolePlayer(attacker!, attackerSeat, restoreLedgerOnReconnect: false);
        }
        SynchronizeRolePlayer(victim!, victimSeat, restoreLedgerOnReconnect: false);
        var prepared = _damagePipeline.Prepare(new DamageContext
        {
            AttackerSeatId = attackerSeat?.SeatId,
            VictimSeatId = victimSeat.SeatId,
            AttackerAbilityId = attackerSeat?.AbilityId,
            VictimAbilityId = victimSeat.AbilityId,
            OriginalDamage = damageInfo.Damage,
            WeaponType = damageDesignerName,
            RoleDamageKind = damageKind,
            FrontAngleDegrees = CalculateFrontAngle(victimPawn, attackerPawn),
            IsValidEntity = true,
            IsFormalRound = true,
            IsEnemySource = attackerSeat is not null && attackerSeat.RosterTeam != victimSeat.RosterTeam,
        });
        if (prepared.BlockedByInvincibility)
        {
            damageInfo.Damage = 0;
            return HookResult.Continue;
        }
        if (!prepared.Ok) return HookResult.Continue;
        if (_roles.GetPlayer(victimSeat.SeatId) is { TemporaryHealth: > 0 } temporaryTarget)
        {
            var absorption = _roles.Balance.AbsorbDamage(temporaryTarget, prepared.EngineDamage, isTrueDamage: false);
            prepared = prepared with { EngineDamage = absorption.RemainingLifeDamage };
            Controller.ScheduleCurrent();
        }
        damageInfo.Damage = prepared.EngineDamage;
        if (prepared.EngineDamage <= 0) return HookResult.Continue;
        if (!_pendingDamage.TryGetValue(victimSeat.SeatId, out var queue))
            _pendingDamage[victimSeat.SeatId] = queue = new Queue<DamagePreparation>();
        queue.Enqueue(prepared);
        return HookResult.Continue;
    }

    private void StartHudTimer()
    {
        Controller.Hud.Start();
        if (_hudTimer is not null) return;
        _hudTimer = _plugin.AddTimer(0.5f, RefreshHud, TimerFlags.REPEAT);
    }

    private void StopHudTimer()
    {
        _hudTimer?.Kill();
        _hudTimer = null;
    }

    private void RefreshHud()
    {
        if (!IsRunning())
        {
            StopHudTimer();
            return;
        }
        foreach (var player in Utilities.GetPlayers())
        {
            if (!TryRealPlayer(player, out var realPlayer)) continue;
            var seat = FindSeat(realPlayer!);
            if (seat is null) continue;
            SynchronizeRolePlayer(realPlayer!, seat, restoreLedgerOnReconnect: false);
        }
        _roles.Tick();
        Controller.ScheduleCurrent();
        foreach (var player in Utilities.GetPlayers())
        {
            if (!TryRealPlayer(player, out var realPlayer)) continue;
            var seat = FindSeat(realPlayer!);
            if (seat is null) continue;
            var rolePlayer = _roles.GetPlayer(seat.SeatId);
            var roleDetails = rolePlayer is not null && seat.AbilityId == "commander"
                ? _roles.Commander.BuildHud(rolePlayer, _roles.Players, DateTimeOffset.UtcNow).Text
                : string.Empty;
            realPlayer!.PrintToCenterHtml(Controller.Hud.Render(
                seat,
                DateTimeOffset.UtcNow,
                _roles.ActiveHandlers.ContainsKey(seat.AbilityId),
                seat.Definition.CModelPreview,
                roleDetails));
            ApplyMovement(realPlayer, seat);
        }
    }

    private void BindCurrentPlayers(bool beginLife)
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (!TryRealPlayer(player, out var realPlayer)) continue;
            var seat = FindSeat(realPlayer!);
            if (seat is null) continue;
            var wasConnected = seat.IsConnected;
            Controller.Runtime.Reconnect(seat.CurrentSteamId);
            SynchronizeRolePlayer(realPlayer!, seat, restoreLedgerOnReconnect: !wasConnected);
            if (beginLife && realPlayer!.PawnIsAlive && Controller.Runtime.State?.CurrentRoundKey is { } roundKey)
                seat.BeginLife(roundKey);
            ApplyMovement(realPlayer!, seat);
        }
        Controller.ScheduleCurrent();
    }

    private void ApplyMovement(CCSPlayerController player, AbilitySeatState seat)
    {
        var pawn = player.PlayerPawn?.Value;
        if (pawn is not { IsValid: true }) return;
        var steamId = player.SteamID.ToString();
        if (!_baselineVelocityBySteamId.TryGetValue(steamId, out var baseline))
        {
            baseline = Math.Max(0.01f, pawn.VelocityModifier);
            _baselineVelocityBySteamId[steamId] = baseline;
        }
        pawn.VelocityModifier = baseline * (float)Controller.Movement.GetCombined(seat.SeatId);
        Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_flVelocityModifier");
    }

    private void RestoreManagedMovement()
    {
        foreach (var player in Utilities.GetPlayers())
        {
            var pawn = player?.PlayerPawn?.Value;
            if (player is null || !player.IsValid || pawn is not { IsValid: true }) continue;
            if (!_baselineVelocityBySteamId.TryGetValue(player.SteamID.ToString(), out var baseline)) continue;
            pawn.VelocityModifier = baseline;
            Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_flVelocityModifier");
        }
        _baselineVelocityBySteamId.Clear();
    }

    private void SynchronizeRolePlayer(
        CCSPlayerController player,
        AbilitySeatState seat,
        bool restoreLedgerOnReconnect)
    {
        if (_roles.GetPlayer(seat.SeatId) is null)
            _roles.BindSeats(Controller.Runtime.State is { } state
                ? state.Seats.Values
                : Enumerable.Empty<AbilitySeatState>());
        var rolePlayer = _roles.GetPlayer(seat.SeatId);
        if (rolePlayer is null) return;
        var pawn = player.PlayerPawn?.Value;
        rolePlayer.DisplayName = player.PlayerName;
        rolePlayer.IsConnected = true;
        rolePlayer.IsAlive = player.PawnIsAlive;
        if (pawn is { IsValid: true })
        {
            rolePlayer.BaseHealth = Math.Max(0, pawn.Health);
            rolePlayer.RoleMaxHealth = Math.Max(1, pawn.MaxHealth);
            if (pawn.AbsOrigin is { } origin)
                rolePlayer.Position = new Vector3(origin.X, origin.Y, origin.Z);
            if (seat.AbilityId == "tank")
            {
                var steamId = player.SteamID.ToString();
                var originalMax = rolePlayer.HasManagedHealthSnapshot
                    ? Math.Max(1, rolePlayer.RoleMaxHealth - 100)
                    : GetOrCaptureBaselineMaxHealth(player, pawn);
                _baselineMaxHealthBySteamId.TryAdd(steamId, originalMax);
                _roles.Tank.PersistManagedHealth(rolePlayer, originalMax);
            }
        }
        var money = player.InGameMoneyServices;
        var ledger = new SeatEconomyLedger();
        if (restoreLedgerOnReconnect && seat.CrossRoundState.ContainsKey(SeatEconomyLedger.BalanceStateKey)
            && ledger.TryRestore(rolePlayer))
        {
            if (money is not null)
            {
                money.Account = rolePlayer.Money;
                Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices");
            }
        }
        else if (money is not null)
        {
            rolePlayer.Money = Math.Max(0, money.Account);
            ledger.Synchronize(rolePlayer, GetMaximumMoney());
        }
    }

    private void ApplyRolePlayer(AbilityRolePlayer rolePlayer)
    {
        var player = Utilities.GetPlayers().FirstOrDefault(candidate =>
            candidate is { IsValid: true } && candidate.SteamID.ToString() == rolePlayer.Seat.CurrentSteamId);
        if (player is null) return;
        var pawn = player.PlayerPawn?.Value;
        if (pawn is { IsValid: true })
        {
            GetOrCaptureBaselineMaxHealth(player, pawn);
            var desiredMax = Math.Max(1, rolePlayer.RoleMaxHealth);
            if (pawn.MaxHealth != desiredMax)
            {
                pawn.MaxHealth = desiredMax;
                Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iMaxHealth");
            }
            var desiredHealth = Math.Clamp(rolePlayer.BaseHealth, 0, desiredMax);
            if (pawn.Health != desiredHealth)
            {
                pawn.Health = desiredHealth;
                Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
            }
        }
        var money = player.InGameMoneyServices;
        if (money is not null && money.Account != rolePlayer.Money)
        {
            money.Account = Math.Clamp(rolePlayer.Money, 0, GetMaximumMoney());
            Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices");
        }
    }

    private void ApplyAllRolePlayers()
    {
        CounterStrikeRoleAdapterRules.ApplyValidEntities(
            _roles.Players,
            rolePlayer => rolePlayer.Seat.IsConnected,
            ApplyRolePlayer);
    }

    private void SynchronizeAllOnlineRolePlayers()
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (!TryRealPlayer(player, out var realPlayer)) continue;
            var seat = FindSeat(realPlayer!);
            if (seat is not null) SynchronizeRolePlayer(realPlayer!, seat, restoreLedgerOnReconnect: false);
        }
    }

    private int GetOrCaptureBaselineMaxHealth(CCSPlayerController player, CCSPlayerPawn pawn)
    {
        var steamId = player.SteamID.ToString();
        if (!_baselineMaxHealthBySteamId.TryGetValue(steamId, out var baseline))
        {
            baseline = Math.Max(1, pawn.MaxHealth);
            _baselineMaxHealthBySteamId[steamId] = baseline;
        }
        return baseline;
    }

    private void RestoreManagedHealth()
    {
        foreach (var player in Utilities.GetPlayers())
        {
            var pawn = player?.PlayerPawn?.Value;
            if (player is null || !player.IsValid || pawn is not { IsValid: true }) continue;
            if (!_baselineMaxHealthBySteamId.TryGetValue(player.SteamID.ToString(), out var baseline)) continue;
            pawn.MaxHealth = baseline;
            if (pawn.Health > baseline) pawn.Health = baseline;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iMaxHealth");
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
        }
        _baselineMaxHealthBySteamId.Clear();
    }

    private static RoleDamageKind ClassifyDamage(string designerName)
        => CounterStrikeRoleAdapterRules.ClassifyDamage(designerName);

    private static double CalculateFrontAngle(CCSPlayerPawn victim, CCSPlayerPawn? attacker)
    {
        if (attacker?.AbsOrigin is null || victim.AbsOrigin is null || victim.EyeAngles is null) return 180;
        return CounterStrikeRoleAdapterRules.CalculateFrontAngle(
            new Vector2(victim.AbsOrigin.X, victim.AbsOrigin.Y),
            victim.EyeAngles.Y,
            new Vector2(attacker.AbsOrigin.X, attacker.AbsOrigin.Y));
    }

    private void ObserveCapitalistReward(CCSPlayerController player, EconomySource source)
    {
        var seat = FindSeat(player);
        if (seat?.AbilityId != "capitalist" || _roles.GetPlayer(seat.SeatId) is not { } rolePlayer) return;
        var services = player.InGameMoneyServices;
        if (services is null) return;
        var previous = rolePlayer.Money;
        rolePlayer.Money = Math.Max(0, services.Account);
        var maximumMoney = GetMaximumMoney();
        if (_roles.Capitalist.ApplyObservedRewardBonus(rolePlayer, previous, source, maximumMoney) > 0)
        {
            ApplyRolePlayer(rolePlayer);
            Controller.ScheduleCurrent();
        }
        else
        {
            new SeatEconomyLedger().Synchronize(rolePlayer, maximumMoney);
        }
    }

    private static void ScalePrimaryAttackDelay(CBasePlayerWeapon weapon, double rate)
    {
        if (rate <= 1 || weapon is not { IsValid: true }) return;
        try
        {
            var remaining = weapon.NextPrimaryAttackTick - Server.TickCount;
            if (remaining <= 1 || remaining > 256) return;
            weapon.NextPrimaryAttackTick = Server.TickCount + Math.Max(1, (int)Math.Round(remaining / rate));
            Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_nNextPrimaryAttackTick");
        }
        catch
        {
            // 引擎实体可能在换枪时失效；只跳过本次更新。
        }
    }

    private static void ScaleCurrentRecoil(CCSPlayerController player, double multiplier)
    {
        try
        {
            var pawn = player.PlayerPawn?.Value;
            if (pawn is not { IsValid: true }) return;
            dynamic dynamicPawn = pawn;
            QAngle current = dynamicPawn.AimPunchAngle;
            dynamicPawn.AimPunchAngle = new QAngle(
                (float)(current.X * multiplier),
                (float)(current.Y * multiplier),
                (float)(current.Z * multiplier));
            Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_aimPunchAngle");
        }
        catch
        {
            // CounterStrikeSharp schema 差异时保留原始后坐力，等待真实服务器验证。
        }
    }

    private string? ResolveRosterWinner(int winnerTeam)
    {
        if (winnerTeam is not ((int)CsTeam.Terrorist or (int)CsTeam.CounterTerrorist)) return null;
        var counts = new Dictionary<string, int>(StringComparer.Ordinal) { ["A"] = 0, ["B"] = 0 };
        foreach (var player in Utilities.GetPlayers())
        {
            if (!TryRealPlayer(player, out var realPlayer) || realPlayer!.TeamNum != winnerTeam) continue;
            var seat = FindSeat(realPlayer);
            if (seat is not null) counts[seat.RosterTeam]++;
        }
        if (counts["A"] == counts["B"]) return null;
        return counts["A"] > counts["B"] ? "A" : "B";
    }

    private bool IsNormalBuyTime()
    {
        var freeze = ConVar.Find("mp_freezetime")?.GetPrimitiveValue<int>() ?? 15;
        var buy = ConVar.Find("mp_buytime")?.GetPrimitiveValue<int>() ?? 20;
        return Server.CurrentTime - _roundStartedAt <= Math.Max(0, freeze) + Math.Max(0, buy);
    }

    private static int GetMaximumMoney() =>
        Math.Max(0, ConVar.Find("mp_maxmoney")?.GetPrimitiveValue<int>() ?? 16_000);

    private bool IsRunning() => Controller.Runtime.State?.Lifecycle == AbilityRuntimeLifecycle.Running;

    private AbilitySeatState? FindSeat(CCSPlayerController player) =>
        Controller.Runtime.State?.Seats.Values.FirstOrDefault(seat => seat.CurrentSteamId == player.SteamID.ToString());

    private AbilitySeatState? FindSeat(string? seatId) => seatId is not null
        && Controller.Runtime.State?.Seats.TryGetValue(seatId, out var seat) == true
            ? seat
            : null;

    private static bool TryRealPlayer(CCSPlayerController? player, out CCSPlayerController? result)
    {
        result = player;
        return player is { IsValid: true, IsBot: false } && player.SteamID != 0;
    }

    private static int ParseRoundSequence(string? roundKey)
    {
        if (roundKey is null) return 1;
        var tail = roundKey.Split('-').LastOrDefault();
        return int.TryParse(tail, out var parsed) ? Math.Max(1, parsed) : 1;
    }

    private static int? ResolveEngineRoundSequence()
    {
        try
        {
            var rules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
                .FirstOrDefault()?.GameRules;
            return rules is null ? null : Math.Max(1, rules.TotalRoundsPlayed + 1);
        }
        catch
        {
            return null;
        }
    }

    private sealed class CounterStrikeMoneyAccount(CCSPlayerController player) : IMoneyAccount
    {
        public int Balance => player.InGameMoneyServices?.Account ?? 0;
        public bool TryDebit(int amount) => TrySet(Balance - amount);
        public bool TryCredit(int amount) => TrySet(Balance + amount);

        private bool TrySet(int value)
        {
            var services = player.InGameMoneyServices;
            if (services is null || value < 0) return false;
            services.Account = value;
            Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices");
            return services.Account == value;
        }
    }

    private sealed class RoleAttackerDamageModifier(CounterStrikeAbilityRuntimeAdapter adapter) : IDamageMultiplier
    {
        public double Apply(DamageContext context)
        {
            var seat = adapter.FindSeat(context.AttackerSeatId);
            return seat is null ? 1 : adapter._roles.GetOutgoingDamageMultiplier(seat, context.RoleDamageKind);
        }
    }

    private sealed class RoleMedicDamageReplacement(CounterStrikeAbilityRuntimeAdapter adapter) : IDamageReplacement
    {
        public double Apply(DamageContext context, double currentDamage)
        {
            var attacker = adapter.FindSeat(context.AttackerSeatId);
            var victim = adapter.FindSeat(context.VictimSeatId);
            if (attacker is null || victim is null) return currentDamage;
            var result = adapter._roles.ResolveMedicShot(
                attacker,
                victim,
                context.RoleDamageKind == RoleDamageKind.Firearm);
            if (!result.ReplacesFriendlyDamage) return currentDamage;
            if (result.Triggered && adapter._roles.GetPlayer(victim.SeatId) is { } healed)
                adapter.ApplyRolePlayer(healed);
            adapter.Controller.ScheduleCurrent();
            return 0;
        }
    }

    private sealed class RoleVictimDamageModifier(CounterStrikeAbilityRuntimeAdapter adapter) : IDamageMultiplier
    {
        public double Apply(DamageContext context)
        {
            var seat = adapter.FindSeat(context.VictimSeatId);
            return seat is null
                ? 1
                : adapter._roles.GetIncomingDamageMultiplier(
                    seat,
                    context.IsTrueDamage,
                    context.RoleDamageKind,
                    context.FrontAngleDegrees);
        }
    }

    private sealed class NoopDamageEngine : IDamageEngine
    {
        public DamageEngineResult Apply(int damage, bool bypassArmor)
            => new(0, false);
    }
}
