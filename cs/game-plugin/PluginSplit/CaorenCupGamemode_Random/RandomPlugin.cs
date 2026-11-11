using CaorenCup.Contracts;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;

namespace CaorenCup.Gamemodes.Random;

public sealed partial class CaorenCupRandomPlugin : BasePlugin, ICaorenCupRandomApi, ICaorenCupPlayerEffectsSource
{
    public override string ModuleName => "CaorenCup Random";
    public override string ModuleVersion => "2.2.2";
    public override string ModuleAuthor => "Graslock + AI";
    private readonly RandomMatch _match = new();
    private RandomGameModeProvider _provider = null!;
    private readonly Dictionary<string, (uint Weapon, ReloadObservation Reload, float Until)> _reloads = new();
    private readonly Dictionary<uint, (int Tick, bool Generated)> _damageOrigins = new();
    private float _buyEnd;
    private string? _nativeError;
    public bool IsRunning => _match.Running;

    public override void Load(bool hotReload)
    {
        try { CaorenCupNativeEngine.Initialize(ModuleDirectory); }
        catch (Exception exception) { _nativeError = exception.Message; }
        _provider = new RandomGameModeProvider(ValidateMap, StartMatch, StopMatch);
        CaorenCupGameModeRegistry.Register(_provider);
        CaorenCupRandomAccess.Publish(this);
        CaorenCupPlayerEffectsAccess.Publish(this);
        AddCommand("css_rbdebug", "随机乱斗管理员测试", DebugCommand);
        AddCommand("css_rbskill", "发动随机乱斗主动技能", SkillCommand);
        RegisterEventHandler<EventRoundStart>(OnRoundStart);
        RegisterEventHandler<EventRoundFreezeEnd>(OnFreezeEnd);
        RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
        RegisterEventHandler<EventWeaponReload>(OnReload);
        RegisterEventHandler<EventPlayerHurt>(OnHurt);
        RegisterEventHandler<EventPlayerDeath>(OnDeath);
        RegisterEventHandler<EventPlayerSpawn>(OnSpawn);
        RegisterEventHandler<EventPlayerTeam>((e, _) =>
        {
            if (Actor(e.Userid)) _match.ObservePlayer(Identity(e.Userid!) with { Team = e.Team });
            return HookResult.Continue;
        });
        RegisterEventHandler<EventGrenadeThrown>(OnManualThrow);
        RegisterEventHandler<EventWeaponFire>(OnShot);
        RegisterEventHandler<EventPlayerBlind>(OnBlind);
        RegisterEventHandler<EventFlashbangDetonate>(OnFlashExplosion);
        RegisterEventHandler<EventHegrenadeDetonate>(OnHeExplosion);
        RegisterListener<Listeners.OnEntityTakeDamagePre>(TrackDamageOrigin);
        RegisterListener<Listeners.OnEntityCreated>(TrackProjectile);
        RegisterListener<Listeners.OnEntityTakeDamagePost>(FinishCombatDamage);
        RegisterEventHandler<EventPlayerDisconnect>((e, _) =>
        {
            if (Actor(e.Userid)) { TrackIdentity(e.Userid!); _reloads.Remove(Id(e.Userid!)); }
            return HookResult.Continue;
        }, HookMode.Pre);
        RegisterEventHandler<EventBombPlanted>((e, _) => { Record(e.Userid, "plant"); Award(e.Userid, RandomPerformanceEvent.Plant, "plant"); return HookResult.Continue; });
        RegisterEventHandler<EventBombDefused>((e, _) => { Record(e.Userid, "defuse"); Award(e.Userid, RandomPerformanceEvent.Defuse, "defuse"); return HookResult.Continue; });
        RegisterListener<Listeners.OnTick>(TickReloads);
        RegisterListener<Listeners.OnTick>(TickPassives);
        RegisterListener<Listeners.OnTick>(TickBounces);
        RegisterListener<Listeners.OnMapEnd>(EndMap);
        CaorenCupModuleRegistry.Register(new CaorenCupModuleDescriptor(this, "random", "随机乱斗", "RandomMode",
            () => "/gamemode random 选择随机乱斗；/randombuffs 查看强化", () => $"随机乱斗2.2 | 回合{_match.Round}/24 | {(IsRunning ? "运行中" : "未启动")}",
            () => "打满24回合，每回合积攒强化券，购买并升级整场生效的BUFF。", () => null, null));
    }
    public override void Unload(bool hotReload)
    {
        EndMap(); CaorenCupRandomAccess.Withdraw(this); CaorenCupGameModeRegistry.Unregister(_provider);
        CaorenCupPlayerEffectsAccess.Withdraw(this);
        CaorenCupModuleRegistry.Unregister(this);
    }
    private void EndMap()
    {
        var runtime = CaorenCupCoreAccess.TryGet()?.GameModes;
        if (runtime?.State.ModeId == "random") runtime.Stop();
        StopMatch();
    }
    private void StartMatch() { StopMatch(); _match.Start(); }
    private void StopMatch()
    {
        RestoreAttributes();
        _match.Stop(); _reloads.Clear(); _damageOrigins.Clear(); _buyEnd = 0;
        _insurance.Clear(); _directDamage.Clear(); _combatSeconds.Clear(); _deathInventory.Clear();
        _throws.Clear(); _shots.Clear(); _flashExplosions.Clear(); _flashChainTargets.Clear();
        _deaths.Clear(); _lastPassiveTick = 0;
        _manualHe.Clear(); _manualSmokes.Clear();
        foreach (var item in _generatedProjectiles.Values) if (item.Handle.Value is { IsValid: true } entity) entity.Remove();
        _generatedProjectiles.Clear();
        _projectileHandles.Clear();
    }
    private static CCSGameRules? Rules() => Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
    private static bool Human(CCSPlayerController? p) => p is { IsValid: true, IsBot: false, IsHLTV: false } && p.SteamID != 0;
    private static bool OnlineHuman(CCSPlayerController? p) => Human(p) && p!.Connected == PlayerConnectedState.Connected;
    private static bool Actor(CCSPlayerController? p) => p is { IsValid: true, IsHLTV: false } && (p.IsBot || p.SteamID != 0);
    private static bool Participant(CCSPlayerController? p) => Actor(p) && p!.TeamNum is 2 or 3;
    private static string Id(CCSPlayerController p) => p.IsBot ? $"bot:{p.EntityHandle.Raw}" : p.SteamID.ToString();
    private static RandomPlayerIdentity Identity(CCSPlayerController p) => new(Id(p), p.PlayerName, p.TeamNum, p.IsBot);
    private void TrackIdentity(CCSPlayerController p) => _match.ObservePlayer(Identity(p));
    private bool BuyWindow(CCSPlayerController p) => Participant(p) && p.PawnIsAlive && _match.RoundOpen &&
        Rules() is { WarmupPeriod: false } rules && (rules.FreezePeriod || Server.CurrentTime <= _buyEnd);
    private GameModeResult ValidateMap()
    {
        if (!CaorenCupNativeEngine.Available) return GameModeResult.Fail(_nativeError ?? "随机乱斗原生组件未加载。");
        if (!CaorenCupPlayerEffectsAccess.ConsumerAvailable) return GameModeResult.Fail("需要加载配套版本的娱乐模块以提供个人效果。");
        try
        {
            CaorenCupSmokeCoverage.EnsureAvailable();
            _ = SmokeFactory.Value;
            _ = HeFactory.Value;
        }
        catch (Exception exception) { return GameModeResult.Fail("引擎接口验证未通过。" + exception.Message); }
        var bomb = Utilities.FindAllEntitiesByDesignerName<CBombTarget>("func_bomb_target").Any();
        var t = Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("info_player_terrorist").Any();
        var ct = Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("info_player_counterterrorist").Any();
        return bomb && t && ct ? GameModeResult.Ok("爆破目标和双方出生点检查通过。") : GameModeResult.Fail("随机乱斗需要爆破目标及双方有效出生点。");
    }
    private HookResult OnRoundStart(EventRoundStart e, GameEventInfo info)
    {
        if (!IsRunning) return HookResult.Continue;
        var rules = Rules();
        if (rules is null || rules.WarmupPeriod) return HookResult.Continue;
        var players = Utilities.GetPlayers().Where(Participant).ToArray();
        foreach (var p in players) TrackIdentity(p);
        if (_match.BeginRound(rules.TotalRoundsPlayed + 1, players.Select(Id)))
        {
            _reloads.Clear(); _damageOrigins.Clear(); _buyEnd = 0;
            _insurance.Clear(); _directDamage.Clear(); _combatSeconds.Clear(); _deathInventory.Clear();
            _throws.Clear(); _shots.Clear(); _flashExplosions.Clear(); _flashChainTargets.Clear();
            _deaths.Clear();
            foreach (var p in players)
            {
                CaorenCupChat.PrintToChat(p, "本回合已获得4张强化券，输入 /randombuffs 选择强化。");
                GiveRoundSupplies(p);
            }
        }
        return HookResult.Continue;
    }
    private HookResult OnFreezeEnd(EventRoundFreezeEnd e, GameEventInfo info)
    {
        if (IsRunning && _match.RoundOpen && _buyEnd == 0)
            _buyEnd = Server.CurrentTime + (ConVar.Find("mp_buytime")?.GetPrimitiveValue<float>() ?? 20f);
        return HookResult.Continue;
    }
    private HookResult OnRoundEnd(EventRoundEnd e, GameEventInfo info)
    {
        if (_match.EndRound(_match.Round)) _reloads.Clear();
        return HookResult.Continue;
    }
    private void Record(CCSPlayerController? p, string kind, int amount = 1)
    { if (Participant(p)) { TrackIdentity(p!); _match.Record(Id(p!), kind, amount); } }
    public RandomAdminView? GetAdminView(CCSPlayerController administrator) => RandomAdminSnapshotAccess.Read(
        OnlineHuman(administrator), () => AdminManager.PlayerHasPermissions(administrator, "@css/root"),
        () => _match.AdminSnapshot(Utilities.GetPlayers().Where(p => Actor(p) && p.Connected == PlayerConnectedState.Connected).Select(Identity)));
    public RandomPlayerView? GetView(CCSPlayerController p)
    {
        if (!Participant(p)) return null;
        TrackIdentity(p);
        var view = _match.View(Id(p), BuyWindow(p));
        return view is null ? null : view with
        {
            CanExchange = view.CanExchange && (p.InGameMoneyServices?.Account ?? 0) >= 1000,
            Skills = view.Owned.Where(card => card.Active).Select(card =>
            {
                var usage = _match.Usage(Id(p), card.Key);
                var available = p.PawnIsAlive && _match.RoundOpen && Rules() is { WarmupPeriod: false, FreezePeriod: false } &&
                    usage.Used + usage.Pending == 0;
                return new RandomSkillView(card.Key, card.Name, available,
                    usage.Pending > 0 ? "正在执行" : usage.Used > 0 ? "本回合已使用" : available ? "可以发动" : "等待正式交战并存活");
            }).ToArray()
        };
    }
    private GameModeResult PlayerAction(CCSPlayerController p, Func<GameModeResult> action)
    {
        if (!Participant(p)) return GameModeResult.Fail("请先加入参赛队伍。");
        TrackIdentity(p);
        return action();
    }
    public GameModeResult Purchase(CCSPlayerController p, string key, long revision) => PlayerAction(p, () =>
    {
        var result = _match.Purchase(Id(p), key, revision, BuyWindow(p));
        if (result.Success) { ApplyHealthAttributes(p); GiveRoundSupplies(p, key); }
        return result;
    });
    public GameModeResult Reroll(CCSPlayerController p, long revision) => PlayerAction(p,
        () => _match.Reroll(Id(p), revision, BuyWindow(p)));
    public GameModeResult Exchange(CCSPlayerController p, long revision) => PlayerAction(p,
        () => _match.Exchange(Id(p), revision, BuyWindow(p), amount =>
        {
            var money = p.InGameMoneyServices; if (money is null || money.Account < amount) return false;
            money.Account -= amount; Utilities.SetStateChanged(p, "CCSPlayerController", "m_pInGameMoneyServices"); return true;
        }));
    private HookResult OnReload(EventWeaponReload e, GameEventInfo info)
    {
        var p = e.Userid;
        if (!IsRunning || !_match.RoundOpen || !Participant(p) || !p!.PawnIsAlive) return HookResult.Continue;
        TrackIdentity(p);
        var weapon = p.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value;
        if (weapon is { IsValid: true } && weapon.ReserveAmmo.Length > 0)
            _reloads[Id(p)] = (weapon.EntityHandle.Raw, new(weapon.Clip1, weapon.ReserveAmmo[0]), Server.CurrentTime + 15);
        return HookResult.Continue;
    }
    private void TickReloads()
    {
        if (!IsRunning || _reloads.Count == 0) return;
        foreach (var p in Utilities.GetPlayers().Where(Participant))
        {
            var id = Id(p); if (!_reloads.TryGetValue(id, out var pending)) continue;
            var pawn = p.PlayerPawn.Value; var weapon = pawn?.WeaponServices?.ActiveWeapon.Value;
            if (!p.PawnIsAlive || pawn is null || weapon is not { IsValid: true } || weapon.EntityHandle.Raw != pending.Weapon || Server.CurrentTime > pending.Until)
            { _reloads.Remove(id); continue; }
            var reloading = weapon.As<CCSWeaponBase>().InReload;
            if (pending.Reload.Observe(weapon.Clip1, weapon.ReserveAmmo[0], reloading))
            {
                var armor = _match.ReloadArmor(id, pawn.ArmorValue, true);
                if (armor != pawn.ArmorValue) { pawn.ArmorValue = armor; Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_ArmorValue"); }
                _reloads.Remove(id);
            }
            else if (pending.Reload.Finished) _reloads.Remove(id);
        }
    }
    private static bool Enemy(CCSPlayerController? attacker, CCSPlayerController victim) => attacker is { IsValid: true } &&
        attacker.Slot != victim.Slot && attacker.TeamNum is 2 or 3 && victim.TeamNum is 2 or 3 && attacker.TeamNum != victim.TeamNum;
    private HookResult TrackDamageOrigin(CEntityInstance entity, CTakeDamageInfo info)
    {
        PrepareCombatDamage(entity, info);
        if (IsRunning && entity.IsValid && entity.DesignerName == "player")
            _damageOrigins[entity.EntityHandle.Raw] = (Server.TickCount,
                Generated(info));
        return HookResult.Continue;
    }
    private bool GeneratedDamage(CCSPlayerPawn pawn) => _damageOrigins.TryGetValue(pawn.EntityHandle.Raw, out var origin) &&
        origin.Tick == Server.TickCount && origin.Generated;
    private HookResult OnHurt(EventPlayerHurt e, GameEventInfo info)
    {
        if (!IsRunning) return HookResult.Continue;
        var p = e.Userid; Record(e.Attacker, "damage", e.DmgHealth);
        return HookResult.Continue;
    }
    private HookResult OnDeath(EventPlayerDeath e, GameEventInfo info)
    {
        if (!IsRunning) return HookResult.Continue;
        var p = e.Userid;
        if (!Participant(p)) return HookResult.Continue;
        if (!_deaths.Add((_match.Round, p!.PlayerPawn.Raw))) return HookResult.Continue;
        Record(p, "death"); Record(e.Attacker, "kill");
        _reloads.Remove(Id(p!));
        var direct = Enemy(e.Attacker, p!) && p!.PlayerPawn.Value is { } victimPawn && !GeneratedDamage(victimPawn) &&
            e.Weapon is not ("planted_c4" or "world" or "worldspawn" or "trigger_hurt");
        if (direct)
        {
            var eventId = $"death:{p!.PlayerPawn.Raw}";
            Award(e.Attacker, RandomPerformanceEvent.Kill, eventId);
            Award(p, RandomPerformanceEvent.Death, eventId);
            if (e.Assister is { IsValid: true } assister && Enemy(assister, p) && assister.Slot != e.Attacker!.Slot)
                Award(assister, RandomPerformanceEvent.Assist, eventId);
            KillSupplies(e.Attacker!);
            SmokeKillSupply(e.Attacker!, p);
            GrowOnKill(p, e.Attacker!);
            RescueTeam(p);
            DeathSupplies(p);
        }
        if (direct) Effect(Id(p!), RandomCatalog.Ghost, 1, 0, () => SpawnGhost(p!));
        return HookResult.Continue;
    }
}

public sealed class RandomGameModeProvider(Func<GameModeResult> validateMap, Action start, Action stop) : ICaorenCupGameModeProvider
{
    private static GameModeCvarDefinition Fixed(string name, string value, GameModeCvarType type = GameModeCvarType.Integer) =>
        new(name, type, value, WebEditable: false, GameEditable: false);
    public GameModeDescriptor Descriptor { get; } = new("random", "随机乱斗", "CaorenCupGamemode_Random", [], null, 15,
    [
        Fixed("mp_maxrounds", "24"), Fixed("mp_match_can_clinch", "0", GameModeCvarType.Boolean),
        Fixed("mp_halftime", "1", GameModeCvarType.Boolean), Fixed("mp_overtime_enable", "0", GameModeCvarType.Boolean),
        Fixed("mp_winlimit", "0"), Fixed("mp_timelimit", "0"), Fixed("mp_startmoney", "800"),
        Fixed("mp_match_end_restart", "0", GameModeCvarType.Boolean),
        new("mp_freezetime", GameModeCvarType.Integer, "15", "0", "60"),
        new("mp_roundtime_defuse", GameModeCvarType.Decimal, "1.92", "0.1", "15"),
        new("mp_round_restart_delay", GameModeCvarType.Decimal, "7", "0", "30"),
        new("mp_buytime", GameModeCvarType.Decimal, "20", "0", "99999"),
    ], ConfigFileName: "default_competitive.cfg");
    public GameModeResult ValidateStart(GameModeStartRequest request, GameModeRuntimeContext context) => validateMap();
    public GameModeResult OnWarmupStarted(GameModeRuntimeContext context)
    { stop(); context.ExecuteServerCommand("css_reset_plu"); return GameModeResult.Ok("随机乱斗热身开始。"); }
    public GameModeResult OnLiveStarted(GameModeRuntimeContext context)
    { start(); context.ExecuteServerCommand("mp_warmup_end"); return GameModeResult.Ok("随机乱斗开始。"); }
    public GameModeResult OnStopped(GameModeRuntimeContext context) { stop(); return GameModeResult.Ok("随机乱斗已停止。"); }
}
