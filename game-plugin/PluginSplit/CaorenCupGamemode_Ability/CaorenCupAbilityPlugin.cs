using Caoren;
using Caoren.AbilityMode;
using CaorenCup.Contracts;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using System.Text.Json;

namespace CaorenCup.Gamemodes.Ability;

public sealed class CaorenCupAbilityPlugin : BasePlugin, ICaorenCupAbilityApi
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private AbilityRoleSettings _roleSettings = new();
    private AbilitySeatAssignmentStore _assignments = null!;
    private CounterStrikeAbilityRuntimeAdapter _adapter = null!;
    private AbilityGameModeProvider _provider = null!;
    private AbilitySyncConfig? _activeConfig;

    public int ContractVersion => CaorenCupAbilityAccess.CurrentContractVersion;

    public override string ModuleName => "CaorenCup Ability";
    public override string ModuleVersion => "1.12.0";
    public override string ModuleAuthor => "Graslock + AI";

    public override void Load(bool hotReload)
    {
        _roleSettings = LoadRoleSettings();

        _assignments = new AbilitySeatAssignmentStore(Path.Combine(ModuleDirectory, "ability-assignments.json"));
        _assignments.Load();

        _adapter = new CounterStrikeAbilityRuntimeAdapter(
            this,
            _roleSettings,
            Path.Combine(ModuleDirectory, "ability-runtime-state.json"));
        _adapter.Register();

        _provider = new AbilityGameModeProvider(this);
        CaorenCupGameModeRegistry.Register(_provider);
        CaorenCupModuleRegistry.Register(new CaorenCupModuleDescriptor(
            this,
            "ability",
            "异能模式 (Ability)",
            "AbilityMode",
            () => $" {ChatColors.Green}/ability{ChatColors.Default} : 选择并启动异能模式（管理员）；{ChatColors.Green}/ability_role <玩家> <职业>{ChatColors.Default} : 分配职业；{ChatColors.Green}/ult{ChatColors.Default} : 释放主动技能；{ChatColors.Green}/charge buy{ChatColors.Default} : 购买充能",
            () => _adapter.Controller.Runtime.State is { } state
                ? $"Ability: {state.Lifecycle} | 席位 {state.Seats.Count} | 回合 {state.CurrentRoundKey ?? "-"}"
                : "Ability: 尚未启动",
            () => "异能模式由统一 Gamemode Runtime 管理，职业与充能规则由 CaorenCupGamemode_Ability 独立插件执行。",
            () => null,
            null));

        AddCommand("css_ability", "选择并启动异能模式", OnAbilityCommand);
        AddCommand("css_ability_role", "为玩家分配异能职业：/ability_role <玩家> <职业>", OnAssignCommand);
        AddCommand("css_ability_roles", "查看异能职业分配", OnListCommand);
        AddCommand("css_ability_role_clear", "清除单个玩家的异能职业：/ability_role_clear <玩家>", OnClearOneCommand);
        AddCommand("css_ability_roles_clear", "清空异能职业分配", OnClearCommand);

        CaorenCupAbilityAccess.Publish(this);
    }

    public override void Unload(bool hotReload)
    {
        CaorenCupAbilityAccess.Withdraw(this);
        _adapter?.Unload(hotReload);
        if (_provider is not null) CaorenCupGameModeRegistry.Unregister(_provider);
        CaorenCupModuleRegistry.Unregister(this);
    }

    public bool IsRuntimeRunning => _adapter.Controller.Runtime.State is not null;

    public bool IsRunning => IsRuntimeRunning;

    public string StatusText => _adapter.Controller.Runtime.State is { } state
        ? $"运行中 | 席位 {state.Seats.Count} | 回合 {state.CurrentRoundKey ?? "-"}"
        : $"未启动 | 已分配 {_assignments.Snapshot.Count} 人";

    public IReadOnlyList<CaorenCupAbilityRoleInfo> Roles
    {
        get
        {
            var catalog = AbilityDefinitionCatalog.CreateProduction();
            var registry = AbilityRoleRegistry.Create(_roleSettings);
            return catalog.All
                .OrderBy(definition => definition.Id, StringComparer.Ordinal)
                .Select(definition => new CaorenCupAbilityRoleInfo(
                    definition.Id,
                    definition.Name,
                    registry.IsEnabled(definition.Id)))
                .ToArray();
        }
    }

    public IReadOnlyList<CaorenCupAbilitySeatInfo> OnlinePlayers
    {
        get
        {
            var result = new List<CaorenCupAbilitySeatInfo>();
            foreach (var player in Utilities.GetPlayers())
            {
                if (player is null || !player.IsValid || player.IsBot || player.IsHLTV) continue;
                if (player.TeamNum != (int)CsTeam.Terrorist && player.TeamNum != (int)CsTeam.CounterTerrorist) continue;
                var steamId = player.SteamID.ToString();
                _assignments.TryGet(steamId, out var abilityId);
                result.Add(new CaorenCupAbilitySeatInfo(steamId, player.PlayerName, abilityId ?? string.Empty));
            }

            return result;
        }
    }

    public IReadOnlyList<CaorenCupAbilitySeatInfo> AssignedPlayers
    {
        get
        {
            var result = new List<CaorenCupAbilitySeatInfo>();
            foreach (var player in Utilities.GetPlayers())
            {
                if (player is null || !player.IsValid || player.IsBot || player.IsHLTV) continue;
                var steamId = player.SteamID.ToString();
                if (_assignments.TryGet(steamId, out var abilityId))
                    result.Add(new CaorenCupAbilitySeatInfo(steamId, player.PlayerName, abilityId));
            }

            return result;
        }
    }

    public GameModeResult ValidateRoster()
    {
        var config = BuildConfig(keepMatchId: false);
        if (config.Seats.Count == 0)
            return GameModeResult.Fail("还没有为任何玩家分配异能职业，无法启动异能模式。");

        var validation = _adapter.ValidateRoleConfig(config);
        return validation.Ok
            ? GameModeResult.Ok($"异能阵容检查通过：{config.Seats.Count} 名玩家。")
            : GameModeResult.Fail(validation.Message);
    }

    public GameModeResult StartRuntime()
    {
        var config = BuildConfig(keepMatchId: true);
        if (config.Seats.Count == 0)
            return GameModeResult.Fail("还没有为任何玩家分配异能职业，无法启动异能模式。");

        var validation = _adapter.ValidateRoleConfig(config);
        if (!validation.Ok) return GameModeResult.Fail(validation.Message);

        var start = _adapter.StartLocally(config, ResolveRoundSequence());
        if (!start.Ok) return GameModeResult.Fail($"异能运行时启动失败：{start.Code}");

        _activeConfig = config;
        _adapter.SaveConfirmedConfig(config);
        Console.WriteLine($"[草人杯] 异能运行时已启动，席位 {config.Seats.Count}，比赛 {config.MatchId}。");
        return GameModeResult.Ok($"异能运行时已启动，共 {config.Seats.Count} 名玩家。");
    }

    public GameModeResult StopRuntime()
    {
        if (_adapter.Controller.Runtime.State is not null) _adapter.ClearRuntime();
        _activeConfig = null;
        Console.WriteLine("[草人杯] 异能运行时已停止。");
        return GameModeResult.Ok("异能运行时已停止。");
    }

    public RuntimeOperationResult Assign(string steamId, string abilityId)
    {
        var catalog = AbilityDefinitionCatalog.CreateProduction();
        if (!catalog.TryGet(abilityId, out _)) return new RuntimeOperationResult(false, "UNKNOWN_ROLE");
        if (!_roleSettingsAllows(abilityId)) return new RuntimeOperationResult(false, "ROLE_DISABLED");
        _assignments.Set(steamId, abilityId);
        return new RuntimeOperationResult(true);
    }

    public bool RemoveAssignment(string steamId) => _assignments.Remove(steamId);

    public void ClearAssignments() => _assignments.Clear();

    public IReadOnlyDictionary<string, string> Assignments => _assignments.Snapshot;

    private bool _roleSettingsAllows(string abilityId)
    {
        var registry = AbilityRoleRegistry.Create(_roleSettings);
        return registry.IsEnabled(abilityId);
    }

    private AbilitySyncConfig BuildConfig(bool keepMatchId)
    {
        var seats = new List<AbilitySyncSeat>();
        foreach (var player in Utilities.GetPlayers())
        {
            if (player is null || !player.IsValid || player.IsBot || player.IsHLTV) continue;
            var team = player.TeamNum;
            if (team != (int)CsTeam.Terrorist && team != (int)CsTeam.CounterTerrorist) continue;
            var steamId = player.SteamID.ToString();
            if (!_assignments.TryGet(steamId, out var abilityId)) continue;

            var rosterTeam = team == (int)CsTeam.CounterTerrorist ? "A" : "B";
            seats.Add(new AbilitySyncSeat
            {
                PlayerId = steamId,
                SteamId = steamId,
                RosterTeam = rosterTeam,
                InitialSide = rosterTeam,
                AbilityId = abilityId,
            });
        }

        var matchId = keepMatchId && _activeConfig is not null
            ? _activeConfig.MatchId
            : $"{Server.MapName}-{DateTime.UtcNow:yyyyMMddHHmmss}";

        var config = new AbilitySyncConfig
        {
            ProtocolVersion = 1,
            SyncId = Guid.NewGuid().ToString("N"),
            MatchId = matchId,
            Revision = keepMatchId && _activeConfig is not null ? _activeConfig.Revision : 1,
            CatalogVersion = AbilityCatalogVersion,
            AbilityModeEnabled = true,
            BannedAbilityIds = [],
            Seats = seats,
        };
        config.ContentDigest = AbilitySyncDigest.Compute(config);
        return config;
    }

    private static string AbilityCatalogVersion => "1.12.0";

    private static int ResolveRoundSequence()
    {
        try
        {
            var rules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
                .FirstOrDefault()?.GameRules;
            return Math.Max(1, (rules?.TotalRoundsPlayed ?? 0) + 1);
        }
        catch
        {
            return 1;
        }
    }

    private AbilityRoleSettings LoadRoleSettings()
    {
        var path = Path.Combine(ModuleDirectory, "ability-roles.json");
        if (!File.Exists(path)) return new AbilityRoleSettings();
        try
        {
            return JsonSerializer.Deserialize<AbilityRoleSettings>(File.ReadAllText(path), JsonOptions) ?? new AbilityRoleSettings();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[草人杯] 异能职业开关配置读取失败，使用默认值：{ex.Message}");
            return new AbilityRoleSettings();
        }
    }

    private static bool HasRoot(CCSPlayerController? player) =>
        player is null || AdminManager.PlayerHasPermissions(player, "@css/root");

    private void OnAbilityCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (!HasRoot(player))
        {
            CaorenCupChat.PrintToChat(player!, "你没有权限切换异能模式。");
            return;
        }

        Server.ExecuteCommand("gamemode ability");
        Server.ExecuteCommand("gamemode start");
        if (player is not null) CaorenCupChat.PrintToChat(player, "已选择 Ability，正在进入 Warmup。");
        else Console.WriteLine("[草人杯] Ability selected; entering Warmup.");
    }

    private void OnAssignCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (!HasRoot(player))
        {
            CaorenCupChat.PrintToChat(player!, "你没有权限分配异能职业。");
            return;
        }

        if (info.ArgCount < 3)
        {
            Reply(player, "用法：/ability_role <玩家> <职业>");
            return;
        }

        var target = ResolveTarget(info.GetArg(1));
        if (target is null)
        {
            Reply(player, "找不到目标玩家。");
            return;
        }

        var abilityId = info.GetArg(2).Trim().ToLowerInvariant();
        var result = Assign(target.SteamID.ToString(), abilityId);
        if (!result.Ok)
        {
            Reply(player, result.Code switch
            {
                "UNKNOWN_ROLE" => $"未知职业：{abilityId}",
                "ROLE_DISABLED" => $"职业 {abilityId} 当前未放行，请先在 ability-roles.json 中开启。",
                _ => $"分配失败：{result.Code}",
            });
            return;
        }

        Reply(player, $"已为 {target.PlayerName} 分配职业 {abilityId}。");
    }

    private void OnListCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (!HasRoot(player))
        {
            CaorenCupChat.PrintToChat(player!, "你没有权限查看异能职业分配。");
            return;
        }

        var assigned = AssignedPlayers;
        if (assigned.Count == 0)
        {
            Reply(player, "当前没有任何异能职业分配。");
            return;
        }

        Reply(player, $"当前异能分配（{assigned.Count} 人）：");
        foreach (var seat in assigned)
            Reply(player, $"  {seat.PlayerName} ({seat.SteamId}) -> {seat.AbilityId}");
    }

    private void OnClearOneCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (!HasRoot(player))
        {
            CaorenCupChat.PrintToChat(player!, "你没有权限清除异能职业分配。");
            return;
        }

        if (info.ArgCount < 2)
        {
            Reply(player, "用法：/ability_role_clear <玩家>");
            return;
        }

        var target = ResolveTarget(info.GetArg(1));
        if (target is null)
        {
            Reply(player, "找不到目标玩家。");
            return;
        }

        var removed = RemoveAssignment(target.SteamID.ToString());
        Reply(player, removed
            ? $"已清除 {target.PlayerName} 的异能职业分配。"
            : $"{target.PlayerName} 当前没有异能职业分配。");
    }

    private void OnClearCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (!HasRoot(player))
        {
            CaorenCupChat.PrintToChat(player!, "你没有权限清空异能职业分配。");
            return;
        }

        ClearAssignments();
        Reply(player, "已清空所有异能职业分配。");
    }

    private static void Reply(CCSPlayerController? player, string message)
    {
        if (player is not null) CaorenCupChat.PrintToChat(player, message);
        else Console.WriteLine($"[草人杯] {message}");
    }

    private static CCSPlayerController? ResolveTarget(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var players = Utilities.GetPlayers().Where(p => p is { IsValid: true, IsBot: false, IsHLTV: false }).ToList();

        if (input.StartsWith('#'))
        {
            if (int.TryParse(input[1..], out var userId))
                return players.FirstOrDefault(p => p.UserId == userId);
        }

        if (ulong.TryParse(input, out var steamId))
            return players.FirstOrDefault(p => p.SteamID == steamId);

        return players.FirstOrDefault(p => p.PlayerName.Contains(input, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class AbilityGameModeProvider(CaorenCupAbilityPlugin plugin) : ICaorenCupGameModeProvider
{
    public GameModeDescriptor Descriptor { get; } = new(
        "ability",
        "Ability（异能）",
        "CaorenCupGamemode_Ability",
        Array.Empty<string>(),
        null,
        15,
        new[]
        {
            new GameModeCvarDefinition("mp_freezetime", GameModeCvarType.Integer, "15", "0", "60"),
            new GameModeCvarDefinition("mp_roundtime_defuse", GameModeCvarType.Decimal, "1.92", "0.1", "15"),
            new GameModeCvarDefinition("mp_round_restart_delay", GameModeCvarType.Decimal, "7", "0", "30"),
            new GameModeCvarDefinition("mp_maxrounds", GameModeCvarType.Integer, "24", "1", "100"),
            new GameModeCvarDefinition("mp_halftime", GameModeCvarType.Boolean, "1"),
            new GameModeCvarDefinition("mp_warmuptime", GameModeCvarType.Integer, "15", "1", "300", WarmupOnly: true),
            new GameModeCvarDefinition("mp_warmup_pausetimer", GameModeCvarType.Boolean, "0", WarmupOnly: true),
            new GameModeCvarDefinition("mp_startmoney", GameModeCvarType.Integer, "800", "0", "65535"),
            new GameModeCvarDefinition("mp_buytime", GameModeCvarType.Decimal, "20", "0", "99999"),
            new GameModeCvarDefinition("mp_buy_anywhere", GameModeCvarType.Boolean, "0"),
        },
        ConfigFileName: "default_ability.cfg");

    public GameModeResult ValidateStart(GameModeStartRequest request, GameModeRuntimeContext context)
    {
        if (!Descriptor.SupportsMap(request.MapName))
            return GameModeResult.Fail($"Ability 不支持当前地图：{request.MapName}");
        return plugin.ValidateRoster();
    }

    public GameModeResult OnWarmupStarted(GameModeRuntimeContext context)
    {
        var started = plugin.StartRuntime();
        context.Notify($"[草人杯] {started.Message}");
        return started;
    }

    public GameModeResult OnLiveStarted(GameModeRuntimeContext context)
    {
        context.ExecuteServerCommand("mp_warmup_end");
        return GameModeResult.Ok("Ability 正式比赛已开始。");
    }

    public GameModeResult OnStopped(GameModeRuntimeContext context)
    {
        plugin.StopRuntime();
        return GameModeResult.Ok("Ability 已停止。");
    }
}
