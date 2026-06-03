using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace CaorenCupPlugin;

[MinimumApiVersion(367)]
public sealed class CaorenCupPlugin : BasePlugin
{
    public override string ModuleName => "CaorenCup Command Center Bridge";
    public override string ModuleVersion => "0.3.12";
    public override string ModuleAuthor => "CaorenCup";
    public override string ModuleDescription => "Bridge CS2 score, player binding and match stats to the CaorenCup web command center.";

    private readonly HttpClient _http = new();
    private readonly Dictionary<string, LocalPlayerStats> _stats = new();
    private readonly Dictionary<string, int> _roundHealthBySteamId = new(StringComparer.Ordinal);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly List<CounterStrikeSharp.API.Modules.Timers.Timer> _timers = new();
    private readonly Channel<PluginOutboundMessage> _outboundQueue = Channel.CreateUnbounded<PluginOutboundMessage>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });
    private readonly CancellationTokenSource _outboundCts = new();
    private Task? _outboundWorker;
    private CaorenConfig _config = new();
    private string? _currentMatchId;
    private int _currentRound;
    private int _scoreCt;
    private int _scoreT;
    private long _eventSequence;
    private DateTime _lastPlayerHurtWarningUtc = DateTime.MinValue;
    private readonly Dictionary<string, CsTeam> _teamAssignments = new(StringComparer.Ordinal);
    private readonly HashSet<string> _teamAssignmentBypass = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WebPlayerState> _webPlayersBySteamId = new(StringComparer.Ordinal);
    private readonly List<WebPlayerState> _lastNoticeMissingTargets = new();
    private bool _teamLockEnabled;
    private int _teamAssignmentsValidFromRound;
    private int _teamAssignmentsValidUntilRound;
    private bool _duelModeEnabled;
    private int _duelPistolRounds = 8;
    private int _duelRifleRounds = 16;
    private int _duelSniperRounds = 12;
    private string _duelUtilityMode = "none";
    private int _duelFormalRound;
    private DuelStage? _duelLastAnnouncedStage;
    private float _duelRoundProtectionEndsAt;
    private readonly Dictionary<string, float> _duelProtectionNoticeAt = new(StringComparer.Ordinal);
    private readonly Random _duelRandom = new();
    private readonly Dictionary<string, string> _duelPendingPrimary = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _duelPendingSecondary = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _duelCurrentPrimary = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _duelCurrentSecondary = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingAwpRequest> _duelAwpRequests = new(StringComparer.Ordinal);
    private bool _isUnloading;
    private const string DefaultNoticeSound = "training/bell_normal.vsnd_c";
    private static readonly HashSet<string> AllowedBridgeServerCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "css_ammo",
        "css_armor",
        "css_aura",
        "css_cash",
        "css_dj",
        "css_fov",
        "css_hpcap",
        "reset_plu",
        "css_dmg",
        "css_incdmg",
        "css_bleed",
        "css_kh",
        "css_kb",
        "css_lhimm",
        "css_smoke",
        "css_esp",
        "css_ffire",
        "css_fh",
        "css_wspd",
        "css_tag",
        "css_magic",
        "css_bq",
        "mp_maxrounds",
        "mp_winlimit",
        "mp_roundtime",
        "mp_freezetime",
        "mp_round_restart_delay",
        "mp_match_can_clinch",
        "mp_free_armor",
        "mp_halftime",
        "mp_autoteambalance",
        "mp_limitteams",
        "sv_showimpacts",
        "sv_showimpacts_time",
        "mp_warmup_end",
        "mp_warmup_start",
        "mp_warmuptime",
        "mp_warmup_pausetimer",
        "mp_restartgame",
        "changelevel",
        "host_workshop_map"
};
    private static readonly DuelWorkshopMap[] DuelWorkshopMaps =
    [
        new("aim_redline", "3199551320"),
        new("5e_akm4_aim_duel", "3250543760"),
        new("5e_aim_map", "3250592791"),
        new("AIM Map", "3084291314"),
        new("aim_awp [CS2 Port]", "3444237717"),
    ];
    private static readonly Dictionary<string, string> DuelWeaponAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["usp"] = "weapon_usp_silencer",
        ["glock"] = "weapon_glock",
        ["p2k"] = "weapon_hkp2000",
        ["elite"] = "weapon_elite",
        ["fn57"] = "weapon_fiveseven",
        ["cz75"] = "weapon_cz75a",
        ["r8"] = "weapon_revolver",
        ["tec9"] = "weapon_tec9",
        ["deagle"] = "weapon_deagle",
        ["p250"] = "weapon_p250",
        ["ak"] = "weapon_ak47",
        ["a1"] = "weapon_m4a1_silencer",
        ["a4"] = "weapon_m4a1",
        ["famas"] = "weapon_famas",
        ["galil"] = "weapon_galilar",
        ["aug"] = "weapon_aug",
        ["553"] = "weapon_sg556",
        ["ssg"] = "weapon_ssg08",
        ["awp"] = "weapon_awp",
        ["mac10"] = "weapon_mac10",
        ["mp9"] = "weapon_mp9",
        ["p90"] = "weapon_p90",
        ["mp7"] = "weapon_mp7",
        ["ump"] = "weapon_ump45",
        ["bizon"] = "weapon_bizon",
        ["negev"] = "weapon_negev",
        ["mp5"] = "weapon_mp5sd",
    };
    private static readonly HashSet<string> DuelPistolAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "usp", "glock", "p2k", "elite", "fn57", "cz75", "r8", "tec9", "deagle", "p250"
    };
    private static readonly HashSet<string> DuelRifleAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "ak", "a1", "a4", "famas", "galil", "aug", "553", "ssg", "awp",
        "mac10", "mp9", "p90", "mp7", "ump", "bizon", "negev", "mp5"
    };

    public override void Load(bool hotReload)
    {
        _isUnloading = false;
        LoadConfig();
        _http.BaseAddress = new Uri(_config.CommandCenterBaseUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Remove("x-caoren-plugin-token");
        _http.DefaultRequestHeaders.Add("x-caoren-plugin-token", _config.PluginToken);

        AddCommand("css_ccbind", "绑定草人杯网页身份。用法：!ccbind 1234", OnBindCommand);
        AddCommand("css_cclogin", "获取草人杯网页登录码。用法：!cclogin", OnGameLoginCommand);
        AddCommand("css_cccode", "获取草人杯网页登录码。用法：!cccode", OnGameLoginCommand);
        AddCommand("css_ccstate", "查看草人杯指挥台连接状态", OnStateCommand);
        AddCommand("css_ccsnapshot", "手动向草人杯指挥台推送一次战绩快照", OnSnapshotCommand);
        AddCommand("css_notice", "向草人杯玩家发送醒目提示。用法：/notice all|undercover|und|detective|det|noready|nor [内容]", OnNoticeCommand);
        AddCommand("css_guns", "查看单挑模式可切换枪械。用法：/guns", OnDuelGunsCommand);
        AddCommand("css_agree_awp", "同意对方在步枪阶段使用 AWP。用法：/agree_awp", OnDuelAgreeAwpCommand);
        AddCommand("css_duel_map", "切换单挑创意工坊地图。用法：/duel_map <序号|地图名|创意工坊ID>", OnDuelMapCommand);
        AddCommand("css_duel_maps", "查看可切换的单挑地图。用法：/duel_maps", OnDuelMapsCommand);
        RegisterDuelWeaponCommands();
        RegisterListener<Listeners.OnPlayerTakeDamagePre>(OnPlayerTakeDamagePre);
        AddCommandListener("jointeam", OnJoinTeamCommand, HookMode.Pre);
        _outboundWorker = Task.Run(() => ProcessOutboundQueueAsync(_outboundCts.Token));

        _timers.Add(AddTimer(Math.Max(3, _config.HeartbeatSeconds), () =>
        {
            if (!_isUnloading) _ = SendHeartbeatAsync();
        }, TimerFlags.REPEAT));
        _timers.Add(AddTimer(Math.Max(5, _config.HeartbeatSeconds), () =>
        {
            if (!_isUnloading) _ = SendSnapshotAsync();
        }, TimerFlags.REPEAT));
        _timers.Add(AddTimer(1.0f, () =>
        {
            if (!_isUnloading) EnforceTeamAssignments();
        }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE));

        Logger.LogInformation("{Name} loaded. CommandCenter={BaseUrl}", ModuleName, _config.CommandCenterBaseUrl);
        _ = SendHeartbeatAsync();
    }

    public override void Unload(bool hotReload)
    {
        _isUnloading = true;
        StopTimers();
        RemoveCommandListener("jointeam", OnJoinTeamCommand, HookMode.Pre);
        _outboundCts.Cancel();
        _http.Dispose();
    }

    private void StopTimers()
    {
        foreach (var timer in _timers.ToArray())
        {
            try
            {
                timer.Kill();
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Failed to stop CaorenCup plugin timer");
            }
        }

        _timers.Clear();
    }

    private void LoadConfig()
    {
        var path = Path.Combine(ModuleDirectory, "caoren_config.json");
        if (!File.Exists(path))
        {
            _config = new CaorenConfig();
            Directory.CreateDirectory(ModuleDirectory);
            File.WriteAllText(path, JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true }));
            Logger.LogWarning("caoren_config.json not found. A default config was created at {Path}", path);
            return;
        }

        var text = File.ReadAllText(path);
        _config = JsonSerializer.Deserialize<CaorenConfig>(text, _jsonOptions) ?? new CaorenConfig();
    }

    private void OnBindCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsRealPlayer(player))
        {
            command.ReplyToCommand("该命令只能由玩家在游戏内执行。");
            return;
        }

        var bindCode = command.ArgByIndex(1)?.Trim();
        if (string.IsNullOrWhiteSpace(bindCode))
        {
            player!.PrintToChat("[草人杯] 用法：!ccbind 你的网页绑定码，例如 !ccbind 1234");
            return;
        }

        var steamId = player!.SteamID.ToString();
        var name = SafePlayerName(player);
        ReplyToPlayer(player, "[草人杯] 已收到绑定请求，正在连接网页指挥台...");
        _ = BindAsync(player, bindCode, steamId, name);
    }

    private void OnGameLoginCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsRealPlayer(player))
        {
            command.ReplyToCommand("这条指令只能由玩家在游戏内执行。");
            return;
        }

        var steamId = player!.SteamID.ToString();
        var name = SafePlayerName(player);
        ReplyToPlayer(player, "[草人杯] 正在生成网页登录码...");
        _ = RequestGameLoginCodeAsync(player, steamId, name);
    }

    private async Task RequestGameLoginCodeAsync(CCSPlayerController player, string steamId, string name)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/plugin/game-login-code", new { steamId, name }, _jsonOptions);
            var body = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<PluginGameLoginCodeResponse>(body, _jsonOptions);

                if (result?.Success == true && !string.IsNullOrWhiteSpace(result.Code))
                {
                    var minutes = Math.Max(1, result.ExpiresInSeconds / 60);
                    var validText = minutes >= 60 ? $"约 {Math.Max(1, minutes / 60)} 小时" : $"约 {minutes} 分钟";
                    ShowGameLoginCodeNotice(player, result.Code, validText);
                }
                else
                {
                    var error = string.IsNullOrWhiteSpace(result?.Error) ? "网页指挥台没有返回登录码。" : result!.Error;
                    ReplyToPlayer(player, $"[草人杯] 获取网页登录码失败：{error}");
                }
            }
            else
            {
                var error = ExtractErrorMessage(body);
                ReplyToPlayer(player, $"[草人杯] 获取网页登录码失败：{error}");
                Logger.LogWarning("Game login code failed: {Body}", body);
            }
        }
        catch (Exception ex)
        {
            ReplyToPlayer(player, "[草人杯] 获取网页登录码失败：无法连接网页指挥台。");
            Logger.LogError(ex, "Game login code failed: cannot connect to command center");
        }
    }

    private void OnStateCommand(CCSPlayerController? player, CommandInfo command)
    {
        ReplyToPlayer(player, "[草人杯] 正在检查网页指挥台连接状态，请稍等...");
        _ = SendHeartbeatAsync(null);
    }

    private void OnSnapshotCommand(CCSPlayerController? player, CommandInfo command)
    {
        ReplyToPlayer(player, "[草人杯] 正在推送当前快照，请稍等...");
        _ = SendSnapshotAsync(null);
    }

    private void OnNoticeCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            ReplyToPlayer(player, "[草人杯] 你没有权限使用 /notice。");
            return;
        }

        if (command.ArgCount < 2)
        {
            ReplyToPlayer(player, "[草人杯] 用法：/notice all|undercover|und|detective|det|noready|nor [提示内容]");
            return;
        }

        var target = command.ArgByIndex(1)?.Trim().ToLowerInvariant() ?? string.Empty;
        var message = BuildNoticeMessage(command);
        _ = SendNoticeAsync(player, target, message);
    }

    private void RegisterDuelWeaponCommands()
    {
        foreach (var alias in DuelWeaponAliases.Keys)
        {
            AddCommand("css_" + alias, $"单挑模式切换枪械：/{alias}", OnDuelWeaponCommand);
        }
    }

    private void OnDuelGunsCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!_duelModeEnabled || !IsRealPlayer(player))
        {
            ReplyToPlayer(player, "[草人杯] 当前没有启用单挑枪械规则。");
            return;
        }

        var stage = GetDuelStage();
        if (stage == DuelStage.Pistol)
        {
            ReplyToPlayer(player, "[草人杯] 手枪阶段可切换：/usp /glock /p2k /elite /fn57 /cz75 /r8 /tec9 /deagle /p250");
        }
        else if (stage == DuelStage.Rifle)
        {
            ReplyToPlayer(player, "[草人杯] 步枪阶段可切换：/ak /a1 /a4 /famas /galil /aug /553 /ssg /awp");
            ReplyToPlayer(player, "[草人杯] 也可切换：/mac10 /mp9 /p90 /mp7 /ump /bizon /negev /mp5");
            ReplyToPlayer(player, "[草人杯] 副武器可切换：/usp /glock /p2k /elite /fn57 /cz75 /r8 /tec9 /deagle /p250");
        }
        else
        {
            ReplyToPlayer(player, "[草人杯] 狙击阶段可切换：/ssg /awp");
        }
    }

    private void OnDuelWeaponCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!_duelModeEnabled || !IsRealPlayer(player)) return;
        var alias = command.GetCommandString.TrimStart('!', '/').Replace("css_", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
        if (!DuelWeaponAliases.TryGetValue(alias, out var weapon)) return;
        RequestDuelWeapon(player!, alias, weapon);
    }

    private void OnDuelAgreeAwpCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!_duelModeEnabled || !IsRealPlayer(player)) return;
        var steamId = player!.SteamID.ToString();
        var myTeam = player.Team;
        var request = _duelAwpRequests.Values.FirstOrDefault(r => r.RequesterTeam != myTeam && r.RequiredApprovals.Contains(steamId));
        if (request == null)
        {
            ReplyToPlayer(player, "[草人杯] 当前没有需要你确认的 AWP 申请。");
            return;
        }

        request.Approvals.Add(steamId);
        ReplyToPlayer(player, "[草人杯] 已同意对方下回合使用 AWP。");
        var missing = request.RequiredApprovals.Where(id => !request.Approvals.Contains(id)).ToList();
        if (missing.Count > 0)
        {
            Server.PrintToChatAll($" {ChatColors.Green}[草人杯]{ChatColors.Default} AWP 申请还需 {missing.Count} 名对方玩家同意。");
            return;
        }

        _duelPendingPrimary[request.RequesterSteamId] = "weapon_awp";
        _duelAwpRequests.Remove(request.RequesterSteamId);
        Server.PrintToChatAll($" {ChatColors.Green}[草人杯]{ChatColors.Default} AWP 申请已通过，下回合生效。");
    }

    private void OnDuelMapsCommand(CCSPlayerController? player, CommandInfo command)
    {
        ShowDuelMapHelp(player);
    }

    private void OnDuelMapCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsRealPlayer(player))
        {
            command.ReplyToCommand("这条指令只能由玩家在游戏内执行。");
            return;
        }

        if (!AdminManager.PlayerHasPermissions(player!, "@css/root"))
        {
            ReplyToPlayer(player, "[草人杯] 只有服务器管理员可以在游戏内切换单挑地图。");
            return;
        }

        var input = command.ArgByIndex(1)?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            ShowDuelMapHelp(player);
            return;
        }

        var map = ResolveDuelWorkshopMap(input);
        if (map == null)
        {
            ReplyToPlayer(player, $"[草人杯] 没找到单挑地图：{input}");
            ShowDuelMapHelp(player);
            return;
        }

        ReplyToPlayer(player, $"[草人杯] 正在把下一局单挑地图设置为：{map.Name}。");
        _ = SetDuelMapAsync(player!, map);
    }

    private string BuildNoticeMessage(CommandInfo command)
    {
        if (command.ArgCount <= 2) return string.Empty;

        var parts = new List<string>();
        for (var i = 2; i < command.ArgCount; i++)
        {
            var part = command.ArgByIndex(i);
            if (!string.IsNullOrWhiteSpace(part)) parts.Add(part.Trim());
        }
        return string.Join(" ", parts).Trim();
    }

    private static PlayerEquipmentSnapshot PlayerEquipment(CCSPlayerController? player)
    {
        var pawn = player?.PlayerPawn?.Value;
        var weaponServices = pawn?.WeaponServices;
        var activeWeapon = weaponServices?.ActiveWeapon.Value;
        var hasHelmet = false;
        try
        {
            dynamic dynPawn = pawn!;
            hasHelmet = dynPawn.HasHelmet;
        }
        catch
        {
            hasHelmet = false;
        }
        var weapons = Array.Empty<string>();
        try
        {
            weapons = weaponServices?.MyWeapons
                .Select(handle => handle.Value?.DesignerName ?? string.Empty)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToArray() ?? Array.Empty<string>();
        }
        catch
        {
            weapons = activeWeapon?.DesignerName is { Length: > 0 } name ? new[] { name } : Array.Empty<string>();
        }

        return new PlayerEquipmentSnapshot
        {
            ActiveWeapon = activeWeapon?.DesignerName ?? string.Empty,
            Weapons = weapons,
            GrenadeCount = CountGrenades(weapons),
            ActiveWeaponIsKnife = IsKnifeWeapon(activeWeapon?.DesignerName),
            Armor = pawn?.ArmorValue ?? 0,
            HasHelmet = hasHelmet
        };
    }

    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _currentRound++;
        if (_duelModeEnabled) _duelFormalRound = Math.Max(1, _scoreCt + _scoreT + 1);
        StartDuelRoundProtection();
        RefreshRoundHealthState();
        EnforceTeamAssignments();
        ApplyDuelLoadoutsForRound();
        AnnounceDuelRound();
        QueueEvent("round_start", new
        {
            round = _duelModeEnabled ? Math.Max(1, _duelFormalRound) : _currentRound,
            mapName = SafeMapName(),
            players = BuildLivePlayers()
        });
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        var victim = @event.Userid;
        var attacker = @event.Attacker;
        var assister = @event.Assister;
        if (!IsRealPlayer(victim)) return HookResult.Continue;

        var victimSteamId = victim!.SteamID.ToString();
        var victimTeam = TeamName(victim.TeamNum);

        string? attackerSteamId = null;
        string? attackerTeam = null;
        var isFriendlyKill = false;
        if (IsRealPlayer(attacker) && attacker!.SteamID != victim.SteamID)
        {
            attackerSteamId = attacker.SteamID.ToString();
            attackerTeam = TeamName(attacker.TeamNum);
            isFriendlyKill = IsSamePlayableSide(attackerTeam, victimTeam);
            if (IsEnemySideKill(attackerTeam, victimTeam))
            {
                EnsureLocalStats(attackerSteamId, SafePlayerName(attacker)).Kills++;
            }
        }
        if (!isFriendlyKill) EnsureLocalStats(victimSteamId, SafePlayerName(victim)).Deaths++;

        string? assisterSteamId = null;
        if (IsRealPlayer(assister) && assister!.SteamID != victim.SteamID && assister.SteamID.ToString() != attackerSteamId && IsEnemySideKill(attackerTeam, victimTeam))
        {
            assisterSteamId = assister.SteamID.ToString();
            EnsureLocalStats(assisterSteamId, SafePlayerName(assister)).Assists++;
        }

        var attackerEquipment = PlayerEquipment(attacker);
        var victimEquipment = PlayerEquipment(victim);

        QueueEvent("player_death", new
        {
            round = _duelModeEnabled ? Math.Max(1, _duelFormalRound) : _currentRound,
            attackerSteamId,
            attackerTeam,
            victimSteamId,
            victimTeam,
            assisterSteamId,
            headshot = @event.Headshot,
            weapon = @event.Weapon,
            attackerEquipment,
            victimEquipment,
            victimActiveWeaponIsKnife = victimEquipment.ActiveWeaponIsKnife,
            victimGrenadeCount = victimEquipment.GrenadeCount,
            mapName = SafeMapName()
        });
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo info)
    {
        var victim = @event.Userid;
        var attacker = @event.Attacker;
        if (!IsRealPlayer(victim) || !IsRealPlayer(attacker)) return HookResult.Continue;
        if (attacker!.SteamID == victim!.SteamID) return HookResult.Continue;
        if (@event.DmgHealth <= 0) return HookResult.Continue;

        var victimSteamId = victim.SteamID.ToString();
        var victimPawn = victim.PlayerPawn?.Value;
        var victimMaxHealth = Math.Max(100, victimPawn?.MaxHealth ?? 100);
        var effectiveDamage = EffectiveHealthDamage(victimSteamId, @event.DmgHealth, @event.Health, victimMaxHealth);
        if (effectiveDamage <= 0) return HookResult.Continue;

        var attackerSteamId = attacker.SteamID.ToString();
        if (IsEnemySideKill(TeamName(attacker.TeamNum), TeamName(victim.TeamNum)))
        {
            EnsureLocalStats(attackerSteamId, SafePlayerName(attacker)).Damage += effectiveDamage;
        }

        QueueEvent("player_hurt", new
        {
            round = _duelModeEnabled ? Math.Max(1, _duelFormalRound) : _currentRound,
            attackerSteamId,
            attackerTeam = TeamName(attacker.TeamNum),
            victimSteamId,
            victimTeam = TeamName(victim.TeamNum),
            damage = effectiveDamage,
            rawDamage = @event.DmgHealth,
            weapon = ReadStringProperty(@event, "Weapon"),
            health = @event.Health,
            maxHealth = victimMaxHealth,
            mapName = SafeMapName()
        });
        return HookResult.Continue;
    }

    private HookResult OnPlayerTakeDamagePre(CCSPlayerPawn victimPawn, CTakeDamageInfo damageInfo)
    {
        if (!_duelModeEnabled || !IsDuelRoundProtectionActive()) return HookResult.Continue;
        if (victimPawn == null || !victimPawn.IsValid) return HookResult.Continue;

        var victim = victimPawn.Controller.Value as CCSPlayerController;
        var attackerPawn = damageInfo.Attacker.Value?.As<CCSPlayerPawn>();
        var attacker = attackerPawn?.Controller.Value as CCSPlayerController;
        if (!IsRealPlayer(victim) || !IsRealPlayer(attacker) || victim == attacker) return HookResult.Continue;
        if (!IsEnemySideKill(TeamName(attacker!.TeamNum), TeamName(victim!.TeamNum))) return HookResult.Continue;

        damageInfo.Damage = 0;
        ShowDuelProtectionHitNotice(attacker);
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerBlind(EventPlayerBlind @event, GameEventInfo info)
    {
        var victim = @event.Userid;
        if (!IsRealPlayer(victim)) return HookResult.Continue;

        var attacker = ReadControllerProperty(@event, "Attacker");
        var duration = ReadFloatProperty(@event, "BlindDuration", "BlindTime", "Duration");
        if (duration <= 0) return HookResult.Continue;

        string? attackerSteamId = null;
        string? attackerTeam = null;
        if (IsRealPlayer(attacker))
        {
            attackerSteamId = attacker!.SteamID.ToString();
            attackerTeam = TeamName(attacker.TeamNum);
        }

        QueueEvent("player_blind", new
        {
            round = _duelModeEnabled ? Math.Max(1, _duelFormalRound) : _currentRound,
            attackerSteamId,
            attackerTeam,
            victimSteamId = victim!.SteamID.ToString(),
            victimTeam = TeamName(victim.TeamNum),
            duration,
            mapName = SafeMapName()
        });
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        var winner = TeamName(@event.Winner);
        if (winner == "CT") _scoreCt++;
        else if (winner == "T") _scoreT++;

        QueueEvent("round_end", new
        {
            round = _duelModeEnabled ? Math.Max(1, _duelFormalRound) : _currentRound,
            winner,
            scoreCT = _scoreCt,
            scoreT = _scoreT,
            mapName = SafeMapName(),
            players = BuildLivePlayers()
        });
        QueueSnapshot();
        return HookResult.Continue;
    }

    private object[] BuildLivePlayers()
    {
        var players = new List<object>();
        foreach (var player in Utilities.GetPlayers())
        {
            try
            {
                if (!IsRealPlayer(player)) continue;

                var steamId = player.SteamID.ToString();
                var playerName = SafePlayerName(player);
                var stats = EnsureLocalStats(steamId, playerName);
                var pawn = player.PlayerPawn?.Value;
                var health = Math.Max(0, pawn?.Health ?? 0);
                var maxHealth = Math.Max(100, pawn?.MaxHealth ?? 100);
                if (player.PawnIsAlive) _roundHealthBySteamId[steamId] = health;
                players.Add(new
                {
                    steamId,
                    name = playerName,
                    team = TeamName(player.TeamNum),
                    kills = stats.Kills,
                    deaths = stats.Deaths,
                    assists = stats.Assists,
                    damage = stats.Damage,
                    health,
                    maxHealth,
                    isAlive = player.PawnIsAlive
                });
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Skipping invalid player while building live snapshot");
            }
        }

        return players.ToArray();
    }

    private int EffectiveHealthDamage(string victimSteamId, int rawDamage, int healthAfter, int maxHealth)
    {
        if (rawDamage <= 0) return 0;
        var safeHealthAfter = Math.Max(0, healthAfter);
        var hasKnownHealth = _roundHealthBySteamId.TryGetValue(victimSteamId, out var knownHealthBefore);
        var observedHealthBefore = safeHealthAfter + rawDamage;
        var safeMaxHealth = Math.Max(100, maxHealth);
        if (!hasKnownHealth)
        {
            knownHealthBefore = Math.Min(observedHealthBefore, safeMaxHealth);
        }
        else if (safeHealthAfter > 0 && observedHealthBefore > knownHealthBefore && observedHealthBefore <= safeMaxHealth)
        {
            knownHealthBefore = observedHealthBefore;
        }

        var effectiveDamage = Math.Min(rawDamage, Math.Max(0, knownHealthBefore - safeHealthAfter));
        _roundHealthBySteamId[victimSteamId] = safeHealthAfter;
        return effectiveDamage;
    }

    private void RefreshRoundHealthState()
    {
        _roundHealthBySteamId.Clear();
        foreach (var player in Utilities.GetPlayers())
        {
            try
            {
                if (!IsRealPlayer(player)) continue;
                var pawn = player.PlayerPawn?.Value;
                _roundHealthBySteamId[player.SteamID.ToString()] = Math.Max(0, pawn?.Health ?? 0);
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Skipping invalid player while refreshing round health state");
            }
        }
    }

    private async Task BindAsync(CCSPlayerController player, string bindCode, string steamId, string name)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/plugin/bind", new { bindCode, steamId, name }, _jsonOptions);
            var body = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<PluginBindResponse>(body, _jsonOptions);
                var webName = string.IsNullOrWhiteSpace(result?.Name) ? name : result!.Name;
                ReplyToPlayer(player, $"[草人杯] 绑定成功：{webName} / {steamId}");
                LogDebug("Bind success: {Body}", body);
            }
            else
            {
                var error = ExtractErrorMessage(body);
                ReplyToPlayer(player, $"[草人杯] 绑定失败：{error}");
                Logger.LogWarning("Bind failed: {Body}", body);
            }
        }
        catch (Exception ex)
        {
            ReplyToPlayer(player, "[草人杯] 绑定失败：无法连接网页指挥台，请联系管理员检查插件配置。");
            Logger.LogError(ex, "Bind failed: cannot connect to command center");
        }
    }

    private async Task SendHeartbeatAsync(CCSPlayerController? replyTo = null)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/plugin/heartbeat", new { mapName = SafeMapName() }, _jsonOptions);
            var text = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                var state = JsonSerializer.Deserialize<PluginHeartbeatResponse>(text, _jsonOptions);
                if (!string.IsNullOrWhiteSpace(state?.MatchId) && state.MatchId != _currentMatchId)
                {
                    _currentMatchId = state.MatchId;
                    _stats.Clear();
                }
                if (state != null)
                {
                    _currentRound = Math.Max(0, state.CurrentRound);
                    _scoreCt = Math.Max(0, state.ScoreCT);
                    _scoreT = Math.Max(0, state.ScoreT);
                }
                if (state?.Commands is { Count: > 0 })
                {
                    foreach (var command in state.Commands)
                    {
                        await ProcessPluginCommandAsync(command);
                    }
                }
                LogDebug("Heartbeat OK: {Text}", text);
            }
            else
            {
                Logger.LogWarning("Heartbeat failed: {Status} {Body}", response.StatusCode, text);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Heartbeat failed");
        }
    }

    private async Task SendSnapshotAsync(CCSPlayerController? replyTo = null)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/plugin/snapshot", new
            {
                matchId = _currentMatchId,
                mapName = SafeMapName(),
                scoreCT = _scoreCt,
                scoreT = _scoreT,
                currentRound = _currentRound,
                players = BuildLivePlayers()
            }, _jsonOptions);

            var text = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                LogDebug("Snapshot OK: {Text}", text);
            }
            else
            {
                Logger.LogWarning("Snapshot failed: {Status} {Body}", response.StatusCode, text);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Snapshot failed");
        }
    }

    private async Task<bool> RefreshWebStateAsync()
    {
        try
        {
            var response = await _http.GetAsync("api/plugin/state");
            var text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                Logger.LogWarning("Plugin state refresh failed: {Status} {Body}", response.StatusCode, text);
                return false;
            }

            var state = JsonSerializer.Deserialize<PluginStateResponse>(text, _jsonOptions);
            _webPlayersBySteamId.Clear();
            if (state?.Players != null)
            {
                foreach (var webPlayer in state.Players)
                {
                    var steamId = NormalizeSteamId(webPlayer.SteamId);
                    if (!string.IsNullOrWhiteSpace(steamId)) _webPlayersBySteamId[steamId] = webPlayer;
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Plugin state refresh exception");
            return false;
        }
    }

    private async Task SendNoticeAsync(CCSPlayerController? caller, string target, string customMessage)
    {
        if (!await RefreshWebStateAsync())
        {
            ReplyToPlayer(caller, "[草人杯] 无法读取网页指挥台状态，/notice 未发送。");
            return;
        }

        var recipients = SelectNoticeRecipients(target, out var targetLabel);
        if (targetLabel == null)
        {
            ReplyToPlayer(caller, "[草人杯] 目标无效。可用：all / undercover / und / detective / det / noready / nor");
            return;
        }

        if (recipients.Count == 0)
        {
            ReplyToPlayer(caller, $"[草人杯] 没有找到可提示的在线玩家：{targetLabel}。");
            return;
        }

        var message = string.IsNullOrWhiteSpace(customMessage) ? DefaultNoticeMessage(target) : customMessage.Trim();
        foreach (var recipient in recipients)
        {
            SendNoticeToPlayer(recipient, targetLabel, message);
        }

        ReplyToPlayer(caller, $"[草人杯] /notice 已发送给 {recipients.Count} 名在线玩家：{targetLabel}。");
        if (_lastNoticeMissingTargets.Count > 0)
        {
            ReplyToPlayer(caller, $"[草人杯] 这些网页玩家未绑定或不在线，未能提示：{string.Join("、", _lastNoticeMissingTargets.Select(p => p.Name))}");
        }
    }

    private List<CCSPlayerController> SelectNoticeRecipients(string target, out string? targetLabel)
    {
        _lastNoticeMissingTargets.Clear();
        targetLabel = target switch
        {
            "all" => "全体玩家",
            "undercover" or "und" => "卧底玩家",
            "detective" or "det" => "侦探玩家",
            "noready" or "nor" => "未准备玩家",
            _ => null
        };

        if (targetLabel == null) return new List<CCSPlayerController>();

        var matched = _webPlayersBySteamId.Values.Where(p => target switch
        {
            "all" => true,
            "undercover" or "und" => string.Equals(p.GameRole, "Undercover", StringComparison.OrdinalIgnoreCase),
            "detective" or "det" => string.Equals(p.GameRole, "Detective", StringComparison.OrdinalIgnoreCase),
            "noready" or "nor" => !p.IsReady || (string.Equals(p.GameRole, "Undercover", StringComparison.OrdinalIgnoreCase) && !string.Equals(p.UndercoverTaskAckStage, "read", StringComparison.OrdinalIgnoreCase)),
            _ => false
        }).ToList();

        var onlineBySteamId = Utilities.GetPlayers()
            .Where(IsRealPlayer)
            .GroupBy(p => NormalizeSteamId(p.SteamID.ToString()))
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var recipients = new List<CCSPlayerController>();
        foreach (var webPlayer in matched)
        {
            var steamId = NormalizeSteamId(webPlayer.SteamId);
            if (!string.IsNullOrWhiteSpace(steamId) && onlineBySteamId.TryGetValue(steamId, out var player))
            {
                recipients.Add(player);
            }
            else
            {
                _lastNoticeMissingTargets.Add(webPlayer);
            }
        }

        return recipients;
    }

    private static string DefaultNoticeMessage(string target) => target switch
    {
        "undercover" or "und" => "请立即查看网页上的卧底任务与确认状态。",
        "detective" or "det" => "请注意裁判提示，准备进行侦探相关流程。",
        "noready" or "nor" => "你还没有完成准备，请回网页完成准备或任务确认。",
        _ => "请注意裁判提示，立即查看网页指挥台。"
    };

    private void SendNoticeToPlayer(CCSPlayerController player, string targetLabel, string message)
    {
        Server.NextFrame(() =>
        {
            if (!player.IsValid) return;
            player.PrintToChat($" {ChatColors.Yellow}================ [草人杯 Notice] ================{ChatColors.Default}");
            player.PrintToChat($" {ChatColors.Red}[重要提醒]{ChatColors.Default} {ChatColors.Green}{targetLabel}{ChatColors.Default}");
            player.PrintToChat($" {ChatColors.Yellow}{message}{ChatColors.Default}");
            try { player.ExecuteClientCommand($"play \"{DefaultNoticeSound}\""); } catch { }
        });
    }

    private static string NormalizeSteamId(string? steamId) =>
        new string((steamId ?? string.Empty).Where(char.IsDigit).ToArray());

    private static bool IsEnemySideKill(string? attackerTeam, string? victimTeam) =>
        (attackerTeam == "CT" || attackerTeam == "T") &&
        (victimTeam == "CT" || victimTeam == "T") &&
        attackerTeam != victimTeam;

    private static bool IsSamePlayableSide(string? attackerTeam, string? victimTeam) =>
        (attackerTeam == "CT" || attackerTeam == "T") &&
        attackerTeam == victimTeam;

    private void QueueEvent(string type, object payload)
    {
        var message = PluginOutboundMessage.ForEvent(
            type,
            payload,
            _currentMatchId,
            Interlocked.Increment(ref _eventSequence),
            DateTimeOffset.UtcNow);

        if (!_outboundQueue.Writer.TryWrite(message) && ShouldLogEventFailure(type))
        {
            Logger.LogWarning("Failed to queue event {Type} seq={Sequence}", type, message.Sequence);
        }
    }

    private void QueueSnapshot()
    {
        var message = PluginOutboundMessage.ForSnapshot(new
        {
            matchId = _currentMatchId,
            mapName = SafeMapName(),
            scoreCT = _scoreCt,
            scoreT = _scoreT,
            currentRound = _currentRound,
            players = BuildLivePlayers()
        }, Interlocked.Increment(ref _eventSequence), DateTimeOffset.UtcNow);

        if (!_outboundQueue.Writer.TryWrite(message))
        {
            Logger.LogWarning("Failed to queue snapshot seq={Sequence}", message.Sequence);
        }
    }

    private async Task ProcessOutboundQueueAsync(CancellationToken cancellationToken)
    {
        await foreach (var message in _outboundQueue.Reader.ReadAllAsync(cancellationToken))
        {
            if (_isUnloading) break;
            try
            {
                if (message.Kind == PluginOutboundKind.Event)
                {
                    await PostEventAsync(message, cancellationToken);
                }
                else
                {
                    await PostSnapshotAsync(message.Body, message.Sequence, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Outbound queue failed for {Kind} seq={Sequence}", message.Kind, message.Sequence);
            }
        }
    }

    private async Task PostEventAsync(PluginOutboundMessage message, CancellationToken cancellationToken)
    {
        var type = message.EventType ?? string.Empty;
        try
        {
            var response = await _http.PostAsJsonAsync("api/plugin/event", new
            {
                matchId = message.MatchId,
                type,
                eventSequence = message.Sequence,
                eventTimestampUtc = message.TimestampUtc,
                payload = message.Body
            }, _jsonOptions, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (ShouldLogEventFailure(type))
                {
                    Logger.LogWarning("Event {Type} seq={Sequence} rejected: {Status} {Body}", type, message.Sequence, response.StatusCode, body);
                }
            }
            else
            {
                if (!string.Equals(type, "player_hurt", StringComparison.OrdinalIgnoreCase))
                {
                    LogDebug("Event {Type} seq={Sequence} posted", type, message.Sequence);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (ShouldLogEventFailure(type))
            {
                Logger.LogWarning(ex, "Failed to post event {Type} seq={Sequence}", type, message.Sequence);
            }
        }
    }

    private async Task PostSnapshotAsync(object body, long sequence, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/plugin/snapshot", body, _jsonOptions, cancellationToken);

            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                LogDebug("Snapshot seq={Sequence} OK: {Text}", sequence, text);
            }
            else
            {
                Logger.LogWarning("Snapshot seq={Sequence} failed: {Status} {Body}", sequence, response.StatusCode, text);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Snapshot seq={Sequence} failed", sequence);
        }
    }

    private bool ShouldLogEventFailure(string type)
    {
        if (!string.Equals(type, "player_hurt", StringComparison.OrdinalIgnoreCase)) return true;
        var now = DateTime.UtcNow;
        if (now - _lastPlayerHurtWarningUtc < TimeSpan.FromSeconds(10)) return false;
        _lastPlayerHurtWarningUtc = now;
        return true;
    }

    private async Task ProcessPluginCommandAsync(PluginCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Id) || string.IsNullOrWhiteSpace(command.Type)) return;
        try
        {
            if (string.Equals(command.Type, "RESET_LIVE_MATCH_STATS", StringComparison.OrdinalIgnoreCase))
            {
                var currentRound = 1;
                if (command.Payload.ValueKind == JsonValueKind.Object &&
                    command.Payload.TryGetProperty("currentRound", out var currentRoundElement) &&
                    currentRoundElement.TryGetInt32(out var parsedRound))
                {
                    currentRound = Math.Max(0, parsedRound);
                }

                ResetLiveMatchStats(currentRound);
                Logger.LogInformation("CaorenCup official stats reset. Current round is now {Round}.", _currentRound);
                await SendSnapshotAsync();
            }
            else if (string.Equals(command.Type, "EXECUTE_SERVER_COMMAND", StringComparison.OrdinalIgnoreCase))
            {
                if (command.Payload.ValueKind != JsonValueKind.Object ||
                    !command.Payload.TryGetProperty("command", out var serverCommandElement))
                {
                    Logger.LogWarning("EXECUTE_SERVER_COMMAND missing payload.command");
                    return;
                }

                var serverCommand = serverCommandElement.GetString()?.Trim() ?? string.Empty;
                var label = serverCommand;
                if (command.Payload.TryGetProperty("label", out var labelElement))
                {
                    label = labelElement.GetString()?.Trim() ?? serverCommand;
                }

                var delaySeconds = ReadPayloadInt(command.Payload, "delaySeconds", 0);
                ExecuteAllowedServerCommand(serverCommand, label, delaySeconds);
            }
            else if (string.Equals(command.Type, "APPLY_TEAM_ASSIGNMENTS", StringComparison.OrdinalIgnoreCase))
            {
                ApplyTeamAssignments(command.Payload);
            }
            else if (string.Equals(command.Type, "CLEAR_TEAM_ASSIGNMENTS", StringComparison.OrdinalIgnoreCase))
            {
                ClearTeamAssignments();
            }
            else if (string.Equals(command.Type, "CONFIGURE_DUEL_MODE", StringComparison.OrdinalIgnoreCase))
            {
                ConfigureDuelMode(command.Payload);
            }
            else
            {
                Logger.LogWarning("Unknown CaorenCup plugin command: {Type}", command.Type);
            }
        }
        finally
        {
            await AckCommandAsync(command.Id);
        }
    }

    private void ApplyTeamAssignments(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("assignments", out var assignmentsElement) ||
            assignmentsElement.ValueKind != JsonValueKind.Array)
        {
            Logger.LogWarning("APPLY_TEAM_ASSIGNMENTS missing payload.assignments");
            return;
        }

        var nextAssignments = new Dictionary<string, CsTeam>(StringComparer.Ordinal);
        foreach (var item in assignmentsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var steamId = item.TryGetProperty("steamId", out var steamIdElement)
                ? steamIdElement.GetString()?.Trim()
                : string.Empty;
            var side = item.TryGetProperty("side", out var sideElement)
                ? sideElement.GetString()?.Trim()
                : string.Empty;
            var team = ParseTeam(side);
            if (string.IsNullOrWhiteSpace(steamId) || team == null) continue;
            nextAssignments[steamId] = team.Value;
        }

        _teamAssignments.Clear();
        foreach (var assignment in nextAssignments)
        {
            _teamAssignments[assignment.Key] = assignment.Value;
        }

        _teamLockEnabled = payload.TryGetProperty("lockTeams", out var lockElement)
            ? lockElement.ValueKind != JsonValueKind.False
            : true;
        _teamAssignmentsValidFromRound = ReadPayloadInt(payload, "validFromRound", 0);
        _teamAssignmentsValidUntilRound = ReadPayloadInt(payload, "validUntilRound", 0);

        Logger.LogInformation(
            "Applied {Count} CaorenCup team assignments. Lock={Lock} ValidFromRound={FromRound} ValidUntilRound={UntilRound}",
            _teamAssignments.Count,
            _teamLockEnabled,
            _teamAssignmentsValidFromRound,
            _teamAssignmentsValidUntilRound);
        Server.NextFrame(() =>
        {
            EnforceTeamAssignments();
            Server.PrintToChatAll($" {ChatColors.Green}[草人杯]{ChatColors.Default} 网页强制分队已同步：{_teamAssignments.Count} 名玩家。");
        });
    }

    private void ClearTeamAssignments()
    {
        _teamAssignments.Clear();
        _teamAssignmentBypass.Clear();
        _teamLockEnabled = false;
        _teamAssignmentsValidFromRound = 0;
        _teamAssignmentsValidUntilRound = 0;
        Logger.LogInformation("Cleared CaorenCup team assignments.");
        Server.NextFrame(() =>
        {
            Server.PrintToChatAll($" {ChatColors.Green}[草人杯]{ChatColors.Default} 网页强制分队已解除。");
        });
    }

    private void ConfigureDuelMode(JsonElement payload)
    {
        var pistol = 8;
        var rifle = 16;
        var sniper = 12;
        if (payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("rounds", out var rounds) &&
            rounds.ValueKind == JsonValueKind.Object)
        {
            pistol = ReadPayloadInt(rounds, "pistol", pistol);
            rifle = ReadPayloadInt(rounds, "rifle", rifle);
            sniper = ReadPayloadInt(rounds, "sniper", sniper);
        }

        if (pistol + rifle + sniper < 30)
        {
            pistol = 8;
            rifle = 16;
            sniper = 12;
        }
        var utilityMode = "none";
        if (payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("utilityMode", out var utilityElement) &&
            utilityElement.ValueKind == JsonValueKind.String)
        {
            utilityMode = NormalizeDuelUtilityMode(utilityElement.GetString());
        }

        _duelModeEnabled = true;
        _duelPistolRounds = pistol;
        _duelRifleRounds = rifle;
        _duelSniperRounds = sniper;
        _duelUtilityMode = utilityMode;
        _duelFormalRound = 0;
        _duelLastAnnouncedStage = null;
        _duelPendingPrimary.Clear();
        _duelPendingSecondary.Clear();
        _duelCurrentPrimary.Clear();
        _duelCurrentSecondary.Clear();
        _duelAwpRequests.Clear();
        _duelRoundProtectionEndsAt = 0;
        _duelProtectionNoticeAt.Clear();
        var totalRounds = pistol + rifle + sniper;
        Server.NextFrame(() =>
        {
            Server.PrintToChatAll($" {ChatColors.Green}[草人杯]{ChatColors.Default} 本局游戏共{totalRounds}回合，其中手枪{pistol}回合，步枪{rifle}回合，狙击枪{sniper}回合。");
            Server.PrintToChatAll($" {ChatColors.Green}[草人杯]{ChatColors.Default} 从第一回合开始，每隔8回合会提示你可使用 /guns 来切换枪械。");
        });
    }

    private void EnforceTeamAssignments()
    {
        if (!_teamLockEnabled || _teamAssignments.Count == 0) return;
        if (!IsTeamAssignmentActiveForCurrentRound()) return;

        foreach (var player in Utilities.GetPlayers().Where(IsRealPlayer))
        {
            var steamId = player.SteamID.ToString();
            if (!_teamAssignments.TryGetValue(steamId, out var targetTeam)) continue;
            if (player.Team == targetTeam || player.TeamNum == (int)targetTeam) continue;

            try
            {
                _teamAssignmentBypass.Add(steamId);
                player.ChangeTeam(targetTeam);
                player.PrintToChat($" {ChatColors.Green}[草人杯]{ChatColors.Default} 已按网页分队将你调整到 {TeamLabel(targetTeam)}。");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to enforce team assignment for {SteamId} to {Team}", steamId, targetTeam);
            }
            finally
            {
                _teamAssignmentBypass.Remove(steamId);
            }
        }
    }

    private HookResult OnJoinTeamCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!_teamLockEnabled || !IsRealPlayer(player)) return HookResult.Continue;
        if (!IsTeamAssignmentActiveForCurrentRound()) return HookResult.Continue;

        var steamId = player!.SteamID.ToString();
        if (_teamAssignmentBypass.Contains(steamId)) return HookResult.Continue;
        if (!_teamAssignments.TryGetValue(steamId, out var targetTeam)) return HookResult.Continue;

        player.PrintToChat($" {ChatColors.Green}[草人杯]{ChatColors.Default} 本局已按网页分队锁定，不能自行换边。你应在 {TeamLabel(targetTeam)}。");
        if (player.Team != targetTeam && player.TeamNum != (int)targetTeam)
        {
            Server.NextFrame(EnforceTeamAssignments);
        }

        return HookResult.Handled;
    }

    private static CsTeam? ParseTeam(string? side)
    {
        return side?.Trim().ToUpperInvariant() switch
        {
            "CT" => CsTeam.CounterTerrorist,
            "T" => CsTeam.Terrorist,
            _ => null
        };
    }

    private bool IsTeamAssignmentActiveForCurrentRound()
    {
        var effectiveRound = GetEffectiveTeamAssignmentRound();
        if (_teamAssignmentsValidFromRound > 0 && effectiveRound < _teamAssignmentsValidFromRound) return false;
        if (_teamAssignmentsValidUntilRound > 0 && effectiveRound > _teamAssignmentsValidUntilRound) return false;
        return true;
    }

    private int GetEffectiveTeamAssignmentRound()
    {
        var nextRoundByScore = Math.Max(0, _scoreCt + _scoreT) + 1;
        return Math.Max(_currentRound, nextRoundByScore);
    }

    private DuelStage GetDuelStage()
    {
        var round = Math.Max(1, _duelFormalRound > 0 ? _duelFormalRound : Math.Max(_scoreCt + _scoreT + 1, _currentRound));
        if (round <= _duelPistolRounds) return DuelStage.Pistol;
        if (round <= _duelPistolRounds + _duelRifleRounds) return DuelStage.Rifle;
        return DuelStage.Sniper;
    }

    private int GetDuelStageRound(DuelStage stage)
    {
        var round = Math.Max(1, _duelFormalRound > 0 ? _duelFormalRound : Math.Max(_scoreCt + _scoreT + 1, _currentRound));
        return stage switch
        {
            DuelStage.Pistol => Math.Min(round, Math.Max(1, _duelPistolRounds)),
            DuelStage.Rifle => Math.Min(Math.Max(1, round - _duelPistolRounds), Math.Max(1, _duelRifleRounds)),
            DuelStage.Sniper => Math.Min(Math.Max(1, round - _duelPistolRounds - _duelRifleRounds), Math.Max(1, _duelSniperRounds)),
            _ => round
        };
    }

    private int GetDuelStageTotal(DuelStage stage)
    {
        return stage switch
        {
            DuelStage.Pistol => Math.Max(1, _duelPistolRounds),
            DuelStage.Rifle => Math.Max(1, _duelRifleRounds),
            DuelStage.Sniper => Math.Max(1, _duelSniperRounds),
            _ => 1
        };
    }

    private static string DuelStageLabel(DuelStage stage)
    {
        return stage switch
        {
            DuelStage.Pistol => "手枪",
            DuelStage.Rifle => "步枪",
            DuelStage.Sniper => "狙击枪",
            _ => "单挑"
        };
    }

    private void AnnounceDuelRound()
    {
        if (!_duelModeEnabled) return;
        var stage = GetDuelStage();
        var stageRound = GetDuelStageRound(stage);
        var stageTotal = GetDuelStageTotal(stage);
        Server.NextFrame(() =>
        {
            Server.ExecuteCommand($"css_csay {DuelStageLabel(stage)}第{stageRound}/{stageTotal}回合");
            if (_duelFormalRound == 1 || (_duelFormalRound > 1 && (_duelFormalRound - 1) % 8 == 0) || _duelLastAnnouncedStage != stage)
            {
                Server.PrintToChatAll($" {ChatColors.Green}[草人杯]{ChatColors.Default} 当前是{DuelStageLabel(stage)}阶段，请用 /guns 查看可选枪械。");
            }
            _duelLastAnnouncedStage = stage;
        });
    }

    private void StartDuelRoundProtection()
    {
        if (!_duelModeEnabled)
        {
            _duelRoundProtectionEndsAt = 0;
            _duelProtectionNoticeAt.Clear();
            return;
        }

        _duelRoundProtectionEndsAt = Server.CurrentTime + 0.6f;
        _duelProtectionNoticeAt.Clear();
    }

    private bool IsDuelRoundProtectionActive()
    {
        return _duelModeEnabled && _duelRoundProtectionEndsAt > 0 && Server.CurrentTime < _duelRoundProtectionEndsAt;
    }

    private void ShowDuelProtectionHitNotice(CCSPlayerController attacker)
    {
        var steamId = attacker.SteamID.ToString();
        var now = Server.CurrentTime;
        if (_duelProtectionNoticeAt.TryGetValue(steamId, out var lastAt) && now - lastAt < 0.35f) return;
        _duelProtectionNoticeAt[steamId] = now;

        var message = "开局保护中，本次命中不造成伤害";
        try { attacker.PrintToCenter(message); } catch { }
        try { attacker.PrintToChat($" {ChatColors.Green}[草人杯]{ChatColors.Default} {message}"); } catch { }
    }

    private void RequestDuelWeapon(CCSPlayerController player, string alias, string weapon)
    {
        var stage = GetDuelStage();
        var steamId = player.SteamID.ToString();
        if (stage == DuelStage.Pistol)
        {
            if (!DuelPistolAliases.Contains(alias))
            {
                ReplyToPlayer(player, "[草人杯] 当前是手枪阶段，请用 /guns 查看可选枪械。");
                return;
            }
            _duelPendingSecondary[steamId] = weapon;
            ReplyToPlayer(player, $"[草人杯] 已选择 {alias}，下回合生效。");
            return;
        }

        if (stage == DuelStage.Sniper)
        {
            if (alias != "ssg" && alias != "awp")
            {
                ReplyToPlayer(player, "[草人杯] 当前是狙击阶段，只能选择 /ssg 或 /awp。");
                return;
            }
            _duelPendingPrimary[steamId] = weapon;
            ReplyToPlayer(player, $"[草人杯] 已选择 {alias}，下回合生效。");
            return;
        }

        if (!DuelRifleAliases.Contains(alias))
        {
            if (DuelPistolAliases.Contains(alias))
            {
                _duelPendingSecondary[steamId] = weapon;
                ReplyToPlayer(player, $"[草人杯] 已选择副武器 {alias}，下回合生效。");
                return;
            }
            ReplyToPlayer(player, "[草人杯] 当前是步枪阶段，请用 /guns 查看可选枪械。");
            return;
        }
        if (alias == "awp")
        {
            RequestDuelAwp(player);
            return;
        }

        _duelPendingPrimary[steamId] = weapon;
        ReplyToPlayer(player, $"[草人杯] 已选择 {alias}，下回合生效。");
    }

    private void RequestDuelAwp(CCSPlayerController player)
    {
        var steamId = player.SteamID.ToString();
        var opponents = Utilities.GetPlayers()
            .Where(IsRealPlayer)
            .Where(p => p.Team != player.Team && (p.Team == CsTeam.Terrorist || p.Team == CsTeam.CounterTerrorist))
            .Select(p => p.SteamID.ToString())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (opponents.Count == 0)
        {
            ReplyToPlayer(player, "[草人杯] 当前没有对方参赛玩家，不能申请 AWP。");
            return;
        }

        _duelAwpRequests[steamId] = new PendingAwpRequest
        {
            RequesterSteamId = steamId,
            RequesterTeam = player.Team,
            RequiredApprovals = opponents,
        };
        Server.PrintToChatAll($" {ChatColors.Green}[草人杯]{ChatColors.Default} {SafePlayerName(player)} 申请下回合使用 AWP，对方所有玩家输入 /agree_awp 同意。");
    }

    private void ApplyDuelLoadoutsForRound()
    {
        if (!_duelModeEnabled) return;
        var stage = GetDuelStage();
        Server.NextFrame(() =>
        {
            foreach (var player in Utilities.GetPlayers().Where(IsRealPlayer))
            {
                if (player.Team != CsTeam.Terrorist && player.Team != CsTeam.CounterTerrorist) continue;
                ApplyDuelLoadout(player, stage);
            }
        });
    }

    private void ApplyDuelLoadout(CCSPlayerController player, DuelStage stage)
    {
        var steamId = player.SteamID.ToString();
        var primary = stage switch
        {
            DuelStage.Pistol => string.Empty,
            DuelStage.Rifle => GetPendingOrCurrent(_duelPendingPrimary, _duelCurrentPrimary, steamId, "weapon_ak47"),
            DuelStage.Sniper => GetPendingOrCurrentSniper(steamId),
            _ => string.Empty
        };
        var secondary = stage switch
        {
            DuelStage.Sniper => string.Empty,
            _ => GetPendingOrCurrent(_duelPendingSecondary, _duelCurrentSecondary, steamId, "weapon_usp_silencer")
        };

        try
        {
            player.RemoveWeapons();
            player.GiveNamedItem("weapon_knife");
            if (!string.IsNullOrWhiteSpace(primary)) player.GiveNamedItem(primary);
            if (!string.IsNullOrWhiteSpace(secondary)) player.GiveNamedItem(secondary);
            if (stage == DuelStage.Pistol) GivePlayerKevlar(player);
            else player.GiveNamedItem("item_assaultsuit");
            GiveDuelUtilities(player);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to apply duel loadout for {SteamId}", steamId);
        }
    }

    private async Task SetDuelMapAsync(CCSPlayerController player, DuelWorkshopMap map)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/plugin/duel-map", new
            {
                map = map.Name,
                workshopId = map.WorkshopId,
                requestedBySteamId = player.SteamID.ToString(),
                requestedByName = SafePlayerName(player),
            }, _jsonOptions);
            var body = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                ReplyToPlayer(player, $"[草人杯] 已设置下一局单挑地图：{map.Name}。开始单挑流程时会自动切图。");
                return;
            }

            var error = ExtractErrorMessage(body);
            ReplyToPlayer(player, $"[草人杯] 设置单挑地图失败：{error}");
        }
        catch (Exception ex)
        {
            ReplyToPlayer(player, "[草人杯] 设置单挑地图失败：无法连接网页指挥台。");
            Logger.LogError(ex, "Failed to set duel map via command center");
        }
    }

    private static string GetPendingOrCurrent(
        Dictionary<string, string> pending,
        Dictionary<string, string> current,
        string steamId,
        string fallback)
    {
        if (pending.TryGetValue(steamId, out var pendingWeapon))
        {
            current[steamId] = pendingWeapon;
            pending.Remove(steamId);
            return pendingWeapon;
        }
        if (current.TryGetValue(steamId, out var currentWeapon)) return currentWeapon;
        current[steamId] = fallback;
        return fallback;
    }

    private string GetPendingOrCurrentSniper(string steamId)
    {
        if (_duelPendingPrimary.TryGetValue(steamId, out var pendingWeapon))
        {
            _duelPendingPrimary.Remove(steamId);
            if (IsDuelSniperWeapon(pendingWeapon))
            {
                _duelCurrentPrimary[steamId] = pendingWeapon;
                return pendingWeapon;
            }
        }
        if (_duelCurrentPrimary.TryGetValue(steamId, out var currentWeapon) && IsDuelSniperWeapon(currentWeapon)) return currentWeapon;
        _duelCurrentPrimary[steamId] = "weapon_awp";
        return "weapon_awp";
    }

    private void GiveDuelUtilities(CCSPlayerController player)
    {
        var utility = BuildDuelUtilities(player);
        foreach (var item in utility)
        {
            try { player.GiveNamedItem(item); }
            catch (Exception ex) { Logger.LogDebug(ex, "Failed to give duel utility {Utility}", item); }
        }
    }

    private List<string> BuildDuelUtilities(CCSPlayerController player)
    {
        var pool = new List<string> { "weapon_hegrenade", "weapon_smokegrenade", "weapon_incgrenade", "weapon_flashbang" };
        var count = _duelUtilityMode switch
        {
            "random1" => 1,
            "random2" => 2,
            "random3" => 3,
            "full" => 4,
            _ => 0
        };
        if (count <= 0) return new List<string>();
        if (_duelUtilityMode == "full") return pool;
        var result = new List<string>();
        while (result.Count < count && pool.Count > 0)
        {
            var index = _duelRandom.Next(pool.Count);
            result.Add(pool[index]);
            pool.RemoveAt(index);
        }
        return result;
    }

    private static void GivePlayerKevlar(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid) return;
        pawn.ArmorValue = 100;
        try
        {
            dynamic dynPawn = pawn;
            dynPawn.HasHelmet = false;
        }
        catch
        {
        }
        try
        {
            Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_ArmorValue");
            Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_bHasHelmet");
        }
        catch
        {
            try
            {
                Utilities.SetStateChanged(pawn, "CCSPlayerPawnBase", "m_ArmorValue");
                Utilities.SetStateChanged(pawn, "CCSPlayerPawnBase", "m_bHasHelmet");
            }
            catch { }
        }
    }

    private static bool IsDuelSniperWeapon(string weapon)
    {
        return string.Equals(weapon, "weapon_awp", StringComparison.OrdinalIgnoreCase)
            || string.Equals(weapon, "weapon_ssg08", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDuelUtilityMode(string? mode)
    {
        return mode?.Trim().ToLowerInvariant() switch
        {
            "random1" => "random1",
            "random2" => "random2",
            "random3" => "random3",
            "full" => "full",
            _ => "none"
        };
    }

    private static int ReadPayloadInt(JsonElement payload, string propertyName, int fallback)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty(propertyName, out var element) ||
            !element.TryGetInt32(out var value))
        {
            return fallback;
        }

        return Math.Max(0, value);
    }

    private static string TeamLabel(CsTeam team)
    {
        return team == CsTeam.CounterTerrorist ? "CT" : team == CsTeam.Terrorist ? "T" : team.ToString();
    }


    private void ExecuteAllowedServerCommand(string serverCommand, string label, int delaySeconds = 0)
    {
        if (string.IsNullOrWhiteSpace(serverCommand))
        {
            Logger.LogWarning("Rejected empty web command from CaorenCup command center.");
            return;
        }

        var commandName = serverCommand.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        if (!AllowedBridgeServerCommands.Contains(commandName))
        {
            Logger.LogWarning("Rejected web command because it is not in bridge allowlist: {Command}", serverCommand);
            return;
        }

        Logger.LogInformation("Executing CaorenCup web command: {Command}", serverCommand);
        void ExecuteNow()
        {
            try
            {
                Server.ExecuteCommand(serverCommand);
                Server.PrintToChatAll($" {ChatColors.Green}[草人杯]{ChatColors.Default} 网页修改已下发：{label}");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to execute CaorenCup web command: {Command}", serverCommand);
            }
        }

        if (delaySeconds > 0)
        {
            Server.NextFrame(() => AddTimer(delaySeconds, ExecuteNow));
            return;
        }

        Server.NextFrame(ExecuteNow);
    }    private void ResetLiveMatchStats(int currentRound)
    {
        _stats.Clear();
        _roundHealthBySteamId.Clear();
        _currentRound = Math.Max(0, currentRound);
        _scoreCt = 0;
        _scoreT = 0;
        _duelFormalRound = 0;
        _duelLastAnnouncedStage = null;
        _duelRoundProtectionEndsAt = 0;
        _duelProtectionNoticeAt.Clear();
    }

    private async Task AckCommandAsync(string commandId)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/plugin/command-ack", new { commandId }, _jsonOptions);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                Logger.LogWarning("Command ack rejected: {Status} {Body}", response.StatusCode, body);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Command ack exception");
        }
    }

    private LocalPlayerStats EnsureLocalStats(string steamId, string name)
    {
        if (!_stats.TryGetValue(steamId, out var stats))
        {
            stats = new LocalPlayerStats { Name = name };
            _stats[steamId] = stats;
        }
        else if (!string.IsNullOrWhiteSpace(name))
        {
            stats.Name = name;
        }
        return stats;
    }

    private static string SafePlayerName(CCSPlayerController? player)
    {
        if (player == null) return string.Empty;
        try
        {
            return player.PlayerName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsRealPlayer(CCSPlayerController? player)
    {
        try
        {
            return player is { IsValid: true, IsBot: false, IsHLTV: false };
        }
        catch
        {
            return false;
        }
    }

    private static string? TeamName(int teamNum)
    {
        return (CsTeam)teamNum switch
        {
            CsTeam.CounterTerrorist => "CT",
            CsTeam.Terrorist => "T",
            _ => null
        };
    }

    private string SafeMapName()
    {
        try { return Server.MapName ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static CCSPlayerController? ReadControllerProperty(object source, params string[] names)
    {
        foreach (var name in names)
        {
            try
            {
                var value = source.GetType().GetProperty(name)?.GetValue(source);
                if (value is CCSPlayerController controller) return controller;
            }
            catch { }
        }
        return null;
    }

    private static float ReadFloatProperty(object source, params string[] names)
    {
        foreach (var name in names)
        {
            try
            {
                var value = source.GetType().GetProperty(name)?.GetValue(source);
                if (value == null) continue;
                return Convert.ToSingle(value);
            }
            catch { }
        }
        return 0f;
    }

    private static string ReadStringProperty(object source, params string[] names)
    {
        foreach (var name in names)
        {
            try
            {
                var value = source.GetType().GetProperty(name)?.GetValue(source);
                if (value != null) return value.ToString() ?? string.Empty;
            }
            catch { }
        }
        return string.Empty;
    }

    private static bool IsKnifeWeapon(string? weaponName)
    {
        if (string.IsNullOrWhiteSpace(weaponName)) return false;
        var weapon = weaponName.ToLowerInvariant();
        return weapon.Contains("knife") || weapon.Contains("bayonet");
    }

    private static bool IsGrenadeWeapon(string? weaponName)
    {
        if (string.IsNullOrWhiteSpace(weaponName)) return false;
        var weapon = weaponName.ToLowerInvariant();
        return weapon.Contains("flashbang")
            || weapon.Contains("hegrenade")
            || weapon.Contains("smokegrenade")
            || weapon.Contains("molotov")
            || weapon.Contains("incgrenade")
            || weapon.Contains("decoy");
    }

    private static int CountGrenades(IEnumerable<string> weapons)
    {
        var count = 0;
        foreach (var weapon in weapons)
        {
            if (IsGrenadeWeapon(weapon)) count++;
        }
        return count;
    }

    private void ShowDuelMapHelp(CCSPlayerController? player)
    {
        ReplyToPlayer(player, "[草人杯] 用法：/duel_map <序号|地图名|创意工坊ID>");
        ReplyToPlayer(player, "[草人杯] 示例：/duel_map 1 或 /duel_map aim_redline 或 /duel_map 3199551320");
        for (var i = 0; i < DuelWorkshopMaps.Length; i++)
        {
            var map = DuelWorkshopMaps[i];
            ReplyToPlayer(player, $"[草人杯] {i + 1}. {map.Name} ({map.WorkshopId})");
        }
    }

    private static DuelWorkshopMap? ResolveDuelWorkshopMap(string input)
    {
        var value = input.Trim();
        if (int.TryParse(value, out var index) && index >= 1 && index <= DuelWorkshopMaps.Length)
        {
            return DuelWorkshopMaps[index - 1];
        }

        return DuelWorkshopMaps.FirstOrDefault(map =>
            string.Equals(map.WorkshopId, value, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(map.Name, value, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(SlugifyDuelMapName(map.Name), SlugifyDuelMapName(value), StringComparison.OrdinalIgnoreCase));
    }

    private static string SlugifyDuelMapName(string value)
    {
        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private void ReplyToPlayer(CCSPlayerController? player, string message)
    {
        if (player == null) return;
        Server.NextFrame(() =>
        {
            try
            {
                if (player.IsValid) player.PrintToChat(message);
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Failed to print chat message to player");
            }
        });
    }

    private void ShowGameLoginCodeNotice(CCSPlayerController player, string code, string validText)
    {
        var displayCode = code.Trim().ToUpperInvariant();

        ReplyToPlayer(player, "[草人杯] =================================");
        ReplyToPlayer(player, $"[草人杯]  你的网页登录码： {displayCode}");
        ReplyToPlayer(player, "[草人杯]  请立即回网页输入这个码进入大厅");
        ReplyToPlayer(player, "[草人杯]  网页掉线后也可继续使用这个码恢复");
        ReplyToPlayer(player, $"[草人杯]  有效期：{validText}；重新获取新码后旧码失效");
        ReplyToPlayer(player, "[草人杯] =================================");
        ReplyToPlayerCenter(player, $"网页登录码：{displayCode}\n请回网页输入");
    }

    private void ReplyToPlayerCenter(CCSPlayerController? player, string message)
    {
        if (player == null) return;
        Server.NextFrame(() =>
        {
            try
            {
                if (player.IsValid) player.PrintToCenter(message);
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Failed to print center message to player");
            }
        });
    }

    private static string ExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "服务器没有返回错误信息";
        try
        {
            var result = JsonSerializer.Deserialize<PluginBindResponse>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (!string.IsNullOrWhiteSpace(result?.Error)) return result.Error!;
        }
        catch { }
        return body.Length > 120 ? body[..120] + "..." : body;
    }

    private void LogDebug(string message, params object?[] args)
    {
        if (_config.EnableDebugLog) Logger.LogInformation(message, args);
    }
}

public sealed class CaorenConfig
{
    public string CommandCenterBaseUrl { get; set; } = "http://127.0.0.1:3000";
    public string PluginToken { get; set; } = "CHANGE_ME_PLUGIN_TOKEN";
    public float HeartbeatSeconds { get; set; } = 3;
    public bool EnableDebugLog { get; set; } = false;
}

public sealed class LocalPlayerStats
{
    public string Name { get; set; } = string.Empty;
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public int Damage { get; set; }
}

public sealed class PluginBindResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("playerId")]
    public string? PlayerId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("steamId")]
    public string? SteamId { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public sealed class PluginStateResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("players")]
    public List<WebPlayerState>? Players { get; set; }
}

public sealed class WebPlayerState
{
    [JsonPropertyName("playerId")]
    public string? PlayerId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("steamId")]
    public string? SteamId { get; set; }

    [JsonPropertyName("gameRole")]
    public string? GameRole { get; set; }

    [JsonPropertyName("isReady")]
    public bool IsReady { get; set; }

    [JsonPropertyName("undercoverTaskAckStage")]
    public string? UndercoverTaskAckStage { get; set; }
}

internal sealed record DuelWorkshopMap(string Name, string WorkshopId);

public enum PluginOutboundKind
{
    Event,
    Snapshot
}

public sealed class PluginOutboundMessage
{
    public PluginOutboundKind Kind { get; init; }
    public string? EventType { get; init; }
    public object Body { get; init; } = new();
    public string? MatchId { get; init; }
    public long Sequence { get; init; }
    public string TimestampUtc { get; init; } = string.Empty;

    public static PluginOutboundMessage ForEvent(string eventType, object body, string? matchId, long sequence, DateTimeOffset timestamp) => new()
    {
        Kind = PluginOutboundKind.Event,
        EventType = eventType,
        Body = body,
        MatchId = matchId,
        Sequence = sequence,
        TimestampUtc = timestamp.ToString("O")
    };

    public static PluginOutboundMessage ForSnapshot(object body, long sequence, DateTimeOffset timestamp) => new()
    {
        Kind = PluginOutboundKind.Snapshot,
        Body = body,
        Sequence = sequence,
        TimestampUtc = timestamp.ToString("O")
    };
}


public sealed class PluginGameLoginCodeResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("expiresInSeconds")]
    public int ExpiresInSeconds { get; set; }

    [JsonPropertyName("steamId")]
    public string? SteamId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public sealed class PluginCommand
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("payload")]
    public JsonElement Payload { get; set; }
}

public sealed class PluginHeartbeatResponse
{
    [JsonPropertyName("matchId")]
    public string? MatchId { get; set; }

    [JsonPropertyName("phase")]
    public string? Phase { get; set; }

    [JsonPropertyName("commands")]
    public List<PluginCommand>? Commands { get; set; }

    [JsonPropertyName("scoreCT")]
    public int ScoreCT { get; set; }

    [JsonPropertyName("scoreT")]
    public int ScoreT { get; set; }

    [JsonPropertyName("currentRound")]
    public int CurrentRound { get; set; }
}

internal sealed class PlayerEquipmentSnapshot
{
    [JsonPropertyName("activeWeapon")]
    public string ActiveWeapon { get; set; } = string.Empty;

    [JsonPropertyName("weapons")]
    public string[] Weapons { get; set; } = Array.Empty<string>();

    [JsonPropertyName("grenadeCount")]
    public int GrenadeCount { get; set; }

    [JsonPropertyName("activeWeaponIsKnife")]
    public bool ActiveWeaponIsKnife { get; set; }

    [JsonPropertyName("armor")]
    public int Armor { get; set; }

    [JsonPropertyName("hasHelmet")]
    public bool HasHelmet { get; set; }
}

internal enum DuelStage
{
    Pistol,
    Rifle,
    Sniper
}

internal sealed class PendingAwpRequest
{
    public string RequesterSteamId { get; set; } = string.Empty;
    public CsTeam RequesterTeam { get; set; }
    public List<string> RequiredApprovals { get; set; } = new();
    public HashSet<string> Approvals { get; set; } = new(StringComparer.Ordinal);
}

internal static partial class DuelWeaponTables
{
}
