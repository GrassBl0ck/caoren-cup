using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace Caoren.AbilityMode;

public sealed class CounterStrikeAbilityRuntimeAdapter
{
    private readonly CaorenCup.CaorenCupPlugin _plugin;
    private readonly RuntimePersistence _persistence;
    private readonly AbilityCommandService _commands;
    private readonly DamagePipeline _damagePipeline = new(new NoopDamageEngine());
    private readonly Dictionary<string, Queue<DamagePreparation>> _pendingDamage = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DamagePreparation> _lastDamageByVictim = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _baselineVelocityBySteamId = new(StringComparer.Ordinal);
    private Timer? _hudTimer;
    private float _roundStartedAt;
    private int _roundSequence;
    private bool _registered;

    public CounterStrikeAbilityRuntimeAdapter(CaorenCup.CaorenCupPlugin plugin, string statePath)
    {
        _plugin = plugin;
        var catalog = AbilityDefinitionCatalog.CreateProduction();
        _persistence = new RuntimePersistence(statePath, catalog);
        Controller = new AbilityRuntimeController(catalog, _persistence);
        _commands = new AbilityCommandService(Controller.Runtime.Charges);
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
        _plugin.RegisterEventHandler<EventRoundEnd>(OnRoundEnd, HookMode.Post);
        _plugin.RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn, HookMode.Post);
        _plugin.RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath, HookMode.Post);
        _plugin.RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt, HookMode.Post);
        _plugin.RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect, HookMode.Post);
        _plugin.RegisterEventHandler<EventPlayerChat>(OnPlayerChat, HookMode.Pre);
        _plugin.RegisterListener<Listeners.OnPlayerTakeDamagePre>(OnPlayerTakeDamagePre);
        _registered = true;
        if (Controller.Hud.IsRunning) StartHudTimer();
        Server.ExecuteCommand("css_ability_runtime_ready");
    }

    public AbilitySyncConfig? LoadConfirmedConfig() =>
        _persistence.LoadConfirmedConfigAsync().GetAwaiter().GetResult();

    public void SaveConfirmedConfig(AbilitySyncConfig config) =>
        _persistence.SaveConfirmedConfigAsync(config).GetAwaiter().GetResult();

    public RuntimeInitializationResult ApplyConfirmedConfig(AbilitySyncConfig config) =>
        Controller.InitializeFromConfirmedAsync(config).GetAwaiter().GetResult();

    public void ClearRuntime()
    {
        _pendingDamage.Clear();
        _lastDamageByVictim.Clear();
        RestoreManagedMovement();
        StopHudTimer();
        _pendingDamage.Clear();
        _lastDamageByVictim.Clear();
        Controller.ClearAsync().GetAwaiter().GetResult();
    }

    public void Unload(bool hotReload)
    {
        if (Controller.Runtime.State is not null)
            Controller.FlushNowAsync().GetAwaiter().GetResult();
        RestoreManagedMovement();
        StopHudTimer();
        Controller.Hud.Stop();
        Controller.Movement.ClearAll();
        if (_registered)
        {
            try { _plugin.DeregisterEventHandler<EventRoundStart>(OnRoundStart, HookMode.Post); } catch { }
            try { _plugin.DeregisterEventHandler<EventRoundEnd>(OnRoundEnd, HookMode.Post); } catch { }
            try { _plugin.DeregisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn, HookMode.Post); } catch { }
            try { _plugin.DeregisterEventHandler<EventPlayerDeath>(OnPlayerDeath, HookMode.Post); } catch { }
            try { _plugin.DeregisterEventHandler<EventPlayerHurt>(OnPlayerHurt, HookMode.Post); } catch { }
            try { _plugin.DeregisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect, HookMode.Post); } catch { }
            try { _plugin.DeregisterEventHandler<EventPlayerChat>(OnPlayerChat, HookMode.Pre); } catch { }
            try { _plugin.RemoveListener<Listeners.OnPlayerTakeDamagePre>(OnPlayerTakeDamagePre); } catch { }
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
                _roundSequence = ParseRoundSequence(envelope.RoundKey);
                _roundStartedAt = Server.CurrentTime;
                BindCurrentPlayers(false);
                StartHudTimer();
            }
            else if (expectedAction == "stop")
            {
                _pendingDamage.Clear();
                _lastDamageByVictim.Clear();
                RestoreManagedMovement();
                StopHudTimer();
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
        Controller.OnRoundStartAsync($"round-{_roundSequence}").GetAwaiter().GetResult();
        _pendingDamage.Clear();
        _lastDamageByVictim.Clear();
        BindCurrentPlayers(false);
        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        var state = Controller.Runtime.State;
        if (state?.Lifecycle != AbilityRuntimeLifecycle.Running || state.CurrentRoundKey is null)
            return HookResult.Continue;
        var winner = ResolveRosterWinner(@event.Winner);
        var outcome = winner switch
        {
            "A" => RoundOutcome.TeamAWin,
            "B" => RoundOutcome.TeamBWin,
            _ => RoundOutcome.Draw,
        };
        Controller.OnRoundCompletedAsync(state.CurrentRoundKey, outcome).GetAwaiter().GetResult();
        return HookResult.Continue;
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        if (!IsRunning() || !TryRealPlayer(@event.Userid, out var player)) return HookResult.Continue;
        var seat = FindSeat(player!);
        var roundKey = Controller.Runtime.State?.CurrentRoundKey;
        if (seat is null || roundKey is null) return HookResult.Continue;
        Controller.Runtime.Reconnect(seat.CurrentSteamId);
        seat.BeginLife(roundKey);
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
        if (TryRealPlayer(@event.Attacker, out var attacker)) attackerSeat = FindSeat(attacker!);
        var result = Controller.OnDeathAsync(lifeKey, victimSeat.SeatId, attackerSeat?.SeatId).GetAwaiter().GetResult();
        if (result.Counted)
        {
            Controller.Hud.SetPrompt(victimSeat.SeatId, "死亡，充能 +1", DateTimeOffset.UtcNow);
            if (attackerSeat is not null && attackerSeat.RosterTeam != victimSeat.RosterTeam)
                Controller.Hud.SetPrompt(attackerSeat.SeatId, "击杀敌人，充能 +2", DateTimeOffset.UtcNow);
        }
        if (_lastDamageByVictim.Remove(victimSeat.SeatId, out var lastDamage))
            _damagePipeline.ConfirmDeath(lastDamage, attackerSeat?.SeatId);
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
        _lastDamageByVictim[victimSeat.SeatId] = prepared;
        return HookResult.Continue;
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player is null || player.SteamID == 0) return HookResult.Continue;
        Controller.Runtime.Disconnect(player.SteamID.ToString());
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
        var result = _commands.UseUltimate(
            seat,
            new(IsRunning(), IsRunning(), true, player.PawnIsAlive, true),
            () => Controller.FlushNowAsync().GetAwaiter().GetResult());
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
        var prepared = _damagePipeline.Prepare(new DamageContext
        {
            AttackerSeatId = attackerSeat?.SeatId,
            VictimSeatId = victimSeat.SeatId,
            AttackerAbilityId = attackerSeat?.AbilityId,
            VictimAbilityId = victimSeat.AbilityId,
            OriginalDamage = damageInfo.Damage,
            WeaponType = damageInfo.Ability.Value?.DesignerName ?? string.Empty,
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
        damageInfo.Damage = prepared.EngineDamage;
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
            realPlayer!.PrintToCenterHtml(Controller.Hud.Render(
                seat,
                DateTimeOffset.UtcNow,
                false,
                seat.Definition.CModelPreview));
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
            Controller.Runtime.Reconnect(seat.CurrentSteamId);
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

    private bool IsRunning() => Controller.Runtime.State?.Lifecycle == AbilityRuntimeLifecycle.Running;

    private AbilitySeatState? FindSeat(CCSPlayerController player) =>
        Controller.Runtime.State?.Seats.Values.FirstOrDefault(seat => seat.CurrentSteamId == player.SteamID.ToString());

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

    private sealed class NoopDamageEngine : IDamageEngine
    {
        public DamageEngineResult Apply(int damage, bool bypassArmor)
            => new(0, false);
    }
}
