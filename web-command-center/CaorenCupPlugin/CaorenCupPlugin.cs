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
    public override string ModuleDescription => "Bridge trusted CS2 identity, score and match stats to the CaorenCup web command center.";

    private readonly HttpClient _http = new();
    private readonly DuelTelemetryIsolationState _duelTelemetryIsolation = new();
    private readonly HeartbeatResponseOrder _heartbeatResponseOrder = new();
    private readonly HeartbeatCommandTransactionGate _heartbeatCommandTransactions = new();
    private readonly Dictionary<string, LocalPlayerStats> _stats = new();
    private readonly Dictionary<string, int> _roundHealthBySteamId = new(StringComparer.Ordinal);
    private readonly Dictionary<ulong, PlayerButtons> _previousButtonsBySteamId = new();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly List<CounterStrikeSharp.API.Modules.Timers.Timer> _timers = new();
    private CounterStrikeSharp.API.Modules.Timers.Timer? _teamAssignmentsSafetyTimer;
    private readonly PluginTelemetryBatchBuffer _telemetryBatch = new(PluginTelemetryPolicy.MaxBufferedEvents);
    private readonly Channel<PluginOutboundMessage> _outboundQueue = Channel.CreateBounded<PluginOutboundMessage>(new BoundedChannelOptions(PluginTelemetryPolicy.OutboundQueueCapacity)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.Wait
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
    private readonly HashSet<string> _lobbySteamIds = new(StringComparer.Ordinal);
    private readonly object _webStateLock = new();
    private readonly List<WebPlayerState> _lastNoticeMissingTargets = new();
    private bool _teamLockEnabled;
    private int _teamAssignmentsValidFromRound;
    private int _teamAssignmentsValidUntilRound;
    // 单挑拆分到 CaorenDuel 插件后，此标志由 css_duelbridge_cvar_ready 通知驱动。
    private bool _duelCvarRestorePending;
    private volatile bool _isUnloading;
    private bool _hasSuccessfulLobbyStateSync;
    private bool _lobbyRemindersEnabled = true;
    private DateTimeOffset _lastSuccessfulLobbyStateSync;
    private int _webStateRefreshInProgress;
    private int _webStateGeneration;
    private const string DefaultNoticeSound = "training/bell_normal.vsnd_c";
    private static readonly TimeSpan LobbyStateMaxAge = TimeSpan.FromSeconds(15);
    private static Action<Action> MapStartLifecycleSchedule => Server.NextWorldUpdate;
    internal static readonly HashSet<string> AllowedBridgeServerCommands = new(StringComparer.OrdinalIgnoreCase)
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
        "host_workshop_map",
        "wp_refresh"
};

    public override void Load(bool hotReload)
    {
        _isUnloading = false;
        LoadConfig();
        _http.BaseAddress = new Uri(_config.CommandCenterBaseUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Remove("x-caoren-plugin-token");
        _http.DefaultRequestHeaders.Add("x-caoren-plugin-token", _config.PluginToken);

        AddCommand("css_cclogin", "获取草人杯网页登录码。用法：/cclogin", OnGameLoginCommand);
        AddCommand("css_ccstate", "查看草人杯指挥台连接状态", OnStateCommand);
        AddCommand("css_ccsnapshot", "手动向草人杯指挥台推送一次战绩快照", OnSnapshotCommand);
        AddCommand("css_notice", "向草人杯玩家发送醒目提示。用法：/notice all|undercover|und|detective|det|task|nor [内容]", OnNoticeCommand);
        AddCommand("css_lobbyreminder", "控制大厅验证提醒。用法：/lobbyreminder on|off|1|0", OnLobbyReminderCommand);
        AddCommand("css_duelbridge_begin", "内部命令：由单挑插件在单挑开始时触发。", OnDuelBridgeBeginCommand);
        AddCommand("css_duelbridge_cleanup_restart", "内部命令：由单挑插件在决定清理重启时触发。", OnDuelBridgeCleanupRestartCommand);
        AddCommand("css_duelbridge_round_start", "内部命令：由单挑插件在回合开始时触发。", OnDuelBridgeRoundStartCommand);
        AddCommand("css_duelbridge_cvar_ready", "内部命令：由单挑插件在 CVar 恢复状态变化时触发。", OnDuelBridgeCvarReadyCommand);
        RegisterListener<Listeners.OnTick>(OnTick);
        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        AddCommandListener("jointeam", OnJoinTeamCommand, HookMode.Pre);
        _outboundWorker = Task.Run(() => ProcessOutboundQueueAsync(_outboundCts.Token));

        _timers.Add(AddTimer(Math.Max(3, _config.HeartbeatSeconds), () =>
        {
            if (!_isUnloading) _ = SendHeartbeatAsync();
        }, TimerFlags.REPEAT));
        _timers.Add(AddTimer(Math.Max(5, _config.HeartbeatSeconds), () =>
        {
            if (!_isUnloading && ShouldPublishMatchTelemetry()) _ = SendSnapshotAsync();
        }, TimerFlags.REPEAT));
        _timers.Add(AddTimer(1.0f, () =>
        {
            if (_isUnloading) return;
            QueueCrouchSamples();
            FlushTelemetryBatch();
        }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE));
        StartTeamAssignmentsSafetyTimer();
        _timers.Add(AddTimer(5.0f, () =>
        {
            if (!_isUnloading) _ = RefreshWebStateAsync();
        }, TimerFlags.REPEAT));
        _timers.Add(AddTimer(1.0f, () =>
        {
            if (!_isUnloading) ShowLobbyEntryReminders();
        }, TimerFlags.REPEAT));

        Logger.LogInformation("{Name} loaded. CommandCenter={BaseUrl}", ModuleName, _config.CommandCenterBaseUrl);
        _ = SendHeartbeatAsync();
        _ = RefreshWebStateAsync();
    }

    public override void Unload(bool hotReload)
    {
        _isUnloading = true;
        StopTimers();
        RemoveCommandListener("jointeam", OnJoinTeamCommand, HookMode.Pre);
        _outboundCts.Cancel();
        ClearLobbyReminderState();
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
        _teamAssignmentsSafetyTimer = null;
    }

    private void StartTeamAssignmentsSafetyTimer()
    {
        if (_teamAssignmentsSafetyTimer != null)
        {
            _timers.Remove(_teamAssignmentsSafetyTimer);
            try
            {
                _teamAssignmentsSafetyTimer.Kill();
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Failed to replace team assignment safety timer");
            }
        }

        _teamAssignmentsSafetyTimer = AddTimer(1.0f, () =>
        {
            if (_isUnloading) return;
            EnforceTeamAssignments();
        }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        _timers.Add(_teamAssignmentsSafetyTimer);
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

    private void OnGameLoginCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsRealPlayer(player))
        {
            command.ReplyToCommand("[草人杯] 这条指令只能由玩家在游戏内执行。");
            return;
        }

        var steamId = player!.SteamID.ToString();
        var name = SafePlayerName(player);
        ReplyToPlayer(player, "[草人杯] 正在生成网页登录码...");
        _ = RequestGameLoginCodeAsync(player, steamId, name);
    }

    private async Task RequestGameLoginCodeAsync(CCSPlayerController player, string steamId, string name)
    {
        if (!ShouldProcessPluginContinuation(_isUnloading)) return;
        try
        {
            var response = await _http.PostAsJsonAsync("api/plugin/game-login-code", new { steamId, name }, _jsonOptions);
            if (!ShouldProcessPluginContinuation(_isUnloading)) return;
            var body = await response.Content.ReadAsStringAsync();
            if (!ShouldProcessPluginContinuation(_isUnloading)) return;

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
            if (!ShouldProcessPluginContinuation(_isUnloading)) return;
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
        if (!ShouldPublishMatchTelemetry())
        {
            const string disabledMessage = "[草人杯] 游戏内独立单挑期间，网页比赛快照已禁用。";
            if (player == null) command.ReplyToCommand($"[草人杯] {StripChatTag(disabledMessage)}");
            else ReplyToPlayer(player, disabledMessage);
            return;
        }

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
            ReplyToPlayer(player, "[草人杯] 用法：/notice all|undercover|und|detective|det|task|nor [提示内容]");
            return;
        }

        var target = command.ArgByIndex(1)?.Trim().ToLowerInvariant() ?? string.Empty;
        var message = BuildNoticeMessage(command);
        _ = SendNoticeAsync(player, target, message);
    }

    private void OnLobbyReminderCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsRealPlayer(player))
        {
            command.ReplyToCommand("[草人杯] 这条指令只能由玩家在游戏内执行。");
            return;
        }

        if (!AdminManager.PlayerHasPermissions(player!, "@css/root"))
        {
            ReplyToPlayer(player, "[草人杯] 你没有权限使用 /lobbyreminder。");
            return;
        }

        if (command.ArgCount < 2)
        {
            var status = _lobbyRemindersEnabled ? "开启" : "关闭";
            ReplyToPlayer(player, $"[草人杯] 大厅验证提醒当前为：{status}。用法：/lobbyreminder on|off|1|0");
            return;
        }

        if (!LobbyReminderPolicy.TryParseEnabled(command.ArgByIndex(1), out var enabled))
        {
            ReplyToPlayer(player, "[草人杯] 参数无效。用法：/lobbyreminder on|off|1|0");
            return;
        }

        _lobbyRemindersEnabled = enabled;
        ReplyToPlayer(player, $"[草人杯] 大厅验证提醒已{(enabled ? "开启" : "关闭")}。");
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
        RefreshRoundHealthState();
        EnforceTeamAssignments();
        QueueEvent("round_start", new
        {
            round = _currentRound,
            mapName = SafeMapName(),
            players = BuildLivePlayers()
        });
        // 单挑的清理重启回合消费由 CaorenDuel 插件通过 css_duelbridge_round_start 通知驱动。
        CommitReleasedDuelHeartbeatStateOnGameThread();
        _ = DrainDeferredHeartbeatCommandsAsync();
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
            round = _currentRound,
            attackerSteamId,
            attackerTeam,
            attackerSpeed = PlayerHorizontalSpeed(attacker),
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
    public HookResult OnWeaponFire(EventWeaponFire @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (!IsRealPlayer(player)) return HookResult.Continue;

        var weapon = ReadStringProperty(@event, "Weapon");
        if (string.IsNullOrWhiteSpace(weapon))
        {
            weapon = PlayerEquipment(player).ActiveWeapon;
        }

        QueueEvent("weapon_fire", new
        {
            round = _currentRound,
            steamId = player!.SteamID.ToString(),
            team = TeamName(player.TeamNum),
            weapon,
            isGrenade = IsGrenadeWeapon(weapon),
            mapName = SafeMapName()
        });
        return HookResult.Continue;
    }

    private void OnTick()
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (!IsRealPlayer(player)) continue;
            var steamId = player.SteamID;
            var previous = _previousButtonsBySteamId.TryGetValue(steamId, out var value) ? value : 0;
            var current = player.Buttons;
            var jumpWasPressed = (previous & PlayerButtons.Jump) != 0;
            var jumpIsPressed = (current & PlayerButtons.Jump) != 0;
            if (!jumpWasPressed && jumpIsPressed)
            {
                QueueEvent("player_jump", new
                {
                    round = _currentRound,
                    steamId = player.SteamID.ToString(),
                    team = TeamName(player.TeamNum),
                    mapName = SafeMapName()
                });
            }
            _previousButtonsBySteamId[steamId] = current;
        }
    }

    private void QueueCrouchSamples()
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (!IsRealPlayer(player)) continue;
            var buttons = player.Buttons;
            if ((buttons & PlayerButtons.Duck) == 0) continue;
            QueueEvent("player_crouch_sample", new
            {
                round = _currentRound,
                steamId = player.SteamID.ToString(),
                team = TeamName(player.TeamNum),
                seconds = 1.0,
                mapName = SafeMapName()
            });
        }
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
            round = _currentRound,
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
            round = _currentRound,
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
            round = _currentRound,
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

    private async Task SendHeartbeatAsync(CCSPlayerController? replyTo = null)
    {
        if (!ShouldProcessPluginContinuation(_isUnloading)) return;
        var heartbeatRequest = _heartbeatResponseOrder.NextRequest();
        try
        {
            var response = await _http.PostAsJsonAsync("api/plugin/heartbeat", new { mapName = SafeMapName() }, _jsonOptions);
            if (!ShouldProcessPluginContinuation(_isUnloading)) return;
            var text = await response.Content.ReadAsStringAsync();
            if (!ShouldProcessPluginContinuation(_isUnloading)) return;
            if (response.IsSuccessStatusCode)
            {
                var state = JsonSerializer.Deserialize<PluginHeartbeatResponse>(text, _jsonOptions);
                if (!ShouldProcessPluginContinuation(_isUnloading)) return;
                var disposition = await HeartbeatResponseProcessor.ProcessTransactionAsync(
                    state,
                    async () =>
                    {
                        var stateDisposition = HeartbeatResponseDisposition.Expired;
                        await GameThreadApplicationScheduler.ScheduleAsync(
                            GameThreadApplicationScheduler.HibernationSafeServerSchedule,
                            () => stateDisposition = ApplyHeartbeatStateOnGameThread(
                                state,
                                heartbeatRequest.Sequence));
                        return stateDisposition;
                    },
                    (heartbeatState, stateDisposition) =>
                        _heartbeatCommandTransactions.ProcessResponseAsync(
                            heartbeatState,
                            stateDisposition,
                            TryApplyAndAckPluginCommandAsync,
                            RejectAndAckPluginCommandAsync,
                            AckPluginCommandOnlyAsync,
                            heartbeatRequest.BarrierGeneration));
                if (!ShouldProcessPluginContinuation(_isUnloading)) return;
                await DrainDeferredHeartbeatCommandsAsync();
                if (!ShouldProcessPluginContinuation(_isUnloading)) return;
                if (disposition == HeartbeatResponseDisposition.Expired)
                {
                    LogDebug("Rejected expired heartbeat response seq={Sequence}.", heartbeatRequest.Sequence);
                }
                else if (disposition == HeartbeatResponseDisposition.Stale)
                {
                    LogDebug(
                        "Rejected heartbeat response for a taken-over match seq={Sequence} MatchId={MatchId}.",
                        heartbeatRequest.Sequence,
                        state?.MatchId);
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
            if (!ShouldProcessPluginContinuation(_isUnloading)) return;
            Logger.LogWarning(ex, "Heartbeat failed");
        }
    }

    private HeartbeatResponseDisposition ApplyHeartbeatStateOnGameThread(
        PluginHeartbeatResponse? state,
        long heartbeatRequestSequence)
    {
        if (!ShouldProcessPluginContinuation(_isUnloading))
        {
            return HeartbeatResponseDisposition.Expired;
        }

        if (!_heartbeatResponseOrder.TryAccept(heartbeatRequestSequence))
        {
            return HeartbeatResponseDisposition.Expired;
        }

        var disposition = _duelTelemetryIsolation.ObserveHeartbeatState(state);
        if (disposition == HeartbeatResponseDisposition.Stale)
        {
            if (_duelTelemetryIsolation.IsActive) _currentMatchId = null;
            return disposition;
        }

        if (_duelTelemetryIsolation.IsActive)
        {
            _currentMatchId = null;
            return disposition;
        }

        if (_duelTelemetryIsolation.HasReleasedHeartbeatState)
        {
            CommitReleasedDuelHeartbeatStateOnGameThread();
        }
        else
        {
            ApplyAcceptedHeartbeatStateOnGameThread(state, false);
        }

        return HeartbeatResponseDisposition.Ready;
    }

    private void CommitReleasedDuelHeartbeatStateOnGameThread()
    {
        if (!_duelTelemetryIsolation.HasReleasedHeartbeatState) return;

        var releasedState = _duelTelemetryIsolation.ReleasedHeartbeatState;
        _duelTelemetryIsolation.ClearReleasedHeartbeatState();
        ApplyAcceptedHeartbeatStateOnGameThread(releasedState, true);
    }

    private void ApplyAcceptedHeartbeatStateOnGameThread(
        PluginHeartbeatResponse? state,
        bool replaceIsolatedState)
    {
        var heartbeatMatchId = string.IsNullOrWhiteSpace(state?.MatchId) ? null : state.MatchId.Trim();
        if (replaceIsolatedState)
        {
            _currentMatchId = heartbeatMatchId;
            _stats.Clear();
        }
        else if (heartbeatMatchId != null && heartbeatMatchId != _currentMatchId)
        {
            _currentMatchId = heartbeatMatchId;
            _stats.Clear();
        }

        if (state != null && ShouldApplyWebMatchCounters(_duelTelemetryIsolation.IsActive))
        {
            _currentRound = Math.Max(0, state.CurrentRound);
            _scoreCt = Math.Max(0, state.ScoreCT);
            _scoreT = Math.Max(0, state.ScoreT);
        }
    }

    private async Task SendSnapshotAsync(CCSPlayerController? replyTo = null)
    {
        if (!ShouldProcessPluginContinuation(_isUnloading) || !ShouldPublishMatchTelemetry()) return;

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
            if (!ShouldProcessPluginContinuation(_isUnloading)) return;

            var text = await response.Content.ReadAsStringAsync();
            if (!ShouldProcessPluginContinuation(_isUnloading)) return;
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
            if (!ShouldProcessPluginContinuation(_isUnloading)) return;
            Logger.LogWarning(ex, "Snapshot failed");
        }
    }

    private async Task<bool> RefreshWebStateAsync()
    {
        if (!ShouldProcessPluginContinuation(_isUnloading)) return false;
        if (Interlocked.Exchange(ref _webStateRefreshInProgress, 1) == 1) return false;
        var stateGeneration = Volatile.Read(ref _webStateGeneration);
        try
        {
            var response = await _http.GetAsync("api/plugin/state");
            if (!ShouldProcessPluginContinuation(_isUnloading)) return false;
            var text = await response.Content.ReadAsStringAsync();
            if (!ShouldProcessPluginContinuation(_isUnloading)) return false;
            if (!response.IsSuccessStatusCode)
            {
                Logger.LogWarning("Plugin state refresh failed: {Status} {Body}", response.StatusCode, text);
                return false;
            }

            var state = JsonSerializer.Deserialize<PluginStateResponse>(text, _jsonOptions);
            if (state?.Success != true) return false;
            var lobbySteamIds = state.LobbySteamIds;
            if (lobbySteamIds == null) return false;
            var nextWebPlayers = new Dictionary<string, WebPlayerState>(StringComparer.Ordinal);
            if (state?.Players != null)
            {
                foreach (var webPlayer in state.Players)
                {
                    var steamId = NormalizeSteamId(webPlayer.SteamId);
                    if (!string.IsNullOrWhiteSpace(steamId)) nextWebPlayers[steamId] = webPlayer;
                }
            }
            var nextLobbySteamIds = lobbySteamIds
                .Select(NormalizeSteamId)
                .Where(steamId => !string.IsNullOrWhiteSpace(steamId))
                .ToHashSet(StringComparer.Ordinal);
            if (_isUnloading || stateGeneration != Volatile.Read(ref _webStateGeneration)) return false;
            lock (_webStateLock)
            {
                _webPlayersBySteamId.Clear();
                foreach (var pair in nextWebPlayers) _webPlayersBySteamId[pair.Key] = pair.Value;
                _lobbySteamIds.Clear();
                foreach (var steamId in nextLobbySteamIds) _lobbySteamIds.Add(steamId);
                _lastSuccessfulLobbyStateSync = DateTimeOffset.UtcNow;
                _hasSuccessfulLobbyStateSync = true;
            }
            return true;
        }
        catch (Exception ex)
        {
            if (!ShouldProcessPluginContinuation(_isUnloading)) return false;
            Logger.LogWarning(ex, "Plugin state refresh exception");
            return false;
        }
        finally
        {
            Interlocked.Exchange(ref _webStateRefreshInProgress, 0);
        }
    }

    // ---- CaorenDuel 单挑插件的隔离通知（仅服务器/控制台来源，防止玩家伪造） ----

    private void OnDuelBridgeBeginCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player != null) return;
        _heartbeatResponseOrder.BeginBarrier();
        Logger.LogInformation(
            "CaorenDuel plugin reported duel begin; isolating telemetry from previous match {MatchId}.",
            _currentMatchId ?? "(none)");
        _duelTelemetryIsolation.Begin(_currentMatchId);
        _currentMatchId = null;
        ResetLiveMatchStats(0);
    }

    private void OnDuelBridgeCleanupRestartCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player != null) return;
        _heartbeatResponseOrder.BeginBarrier();
        _duelTelemetryIsolation.BeginCleanupRestart();
        _duelTelemetryIsolation.UpdateCvarRestoreReady(false);
        _duelCvarRestorePending = true;
    }

    private void OnDuelBridgeRoundStartCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player != null) return;
        _duelTelemetryIsolation.CompleteCleanupRoundStart();
        CommitReleasedDuelHeartbeatStateOnGameThread();
        _ = DrainDeferredHeartbeatCommandsAsync();
    }

    private void OnDuelBridgeCvarReadyCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player != null) return;
        var ready = string.Equals(command.ArgByIndex(1)?.Trim(), "1", StringComparison.Ordinal);
        _duelCvarRestorePending = !ready;
        _duelTelemetryIsolation.UpdateCvarRestoreReady(ready);
        CommitReleasedDuelHeartbeatStateOnGameThread();
        if (ready) _ = DrainDeferredHeartbeatCommandsAsync();
    }

    private void OnMapStart(string _mapName)
    {
        ClearLobbyReminderState();
        if (!_isUnloading)
        {
            MapStartLifecycleSchedule(() =>
            {
                if (_isUnloading) return;
                StartTeamAssignmentsSafetyTimer();
            });
        }
        if (!_isUnloading) _ = RefreshWebStateAsync();
    }

    private void ClearLobbyReminderState()
    {
        Interlocked.Increment(ref _webStateGeneration);
        lock (_webStateLock)
        {
            _lobbySteamIds.Clear();
            _webPlayersBySteamId.Clear();
            _hasSuccessfulLobbyStateSync = false;
            _lastSuccessfulLobbyStateSync = default;
        }
    }

    private void ShowLobbyEntryReminders()
    {
        bool hasSuccessfulSync;
        DateTimeOffset lastSuccessfulSync;
        IReadOnlySet<string> lobbySteamIds;
        lock (_webStateLock)
        {
            hasSuccessfulSync = _hasSuccessfulLobbyStateSync;
            lastSuccessfulSync = _lastSuccessfulLobbyStateSync;
            lobbySteamIds = new HashSet<string>(_lobbySteamIds, StringComparer.Ordinal);
        }
        var now = DateTimeOffset.UtcNow;
        foreach (var player in Utilities.GetPlayers())
        {
            var isRealPlayer = IsRealPlayer(player);
            var steamId = isRealPlayer ? player.SteamID.ToString() : string.Empty;
            if (!LobbyReminderPolicy.ShouldRemind(
                _lobbyRemindersEnabled,
                isRealPlayer,
                steamId,
                hasSuccessfulSync,
                lastSuccessfulSync,
                now,
                LobbyStateMaxAge,
                lobbySteamIds)) continue;
            ReplyToPlayer(player, "[草人杯] 请先打开草人杯客户端，登录玩家中心并明确加入本场比赛。");
        }
    }

    private async Task SendNoticeAsync(CCSPlayerController? caller, string target, string customMessage)
    {
        var refreshed = await RefreshWebStateAsync();
        if (!ShouldProcessPluginContinuation(_isUnloading)) return;
        if (!refreshed)
        {
            ReplyToPlayer(caller, "[草人杯] 无法读取网页指挥台状态，/notice 未发送。");
            return;
        }

        var recipients = SelectNoticeRecipients(target, out var targetLabel);
        if (targetLabel == null)
        {
            ReplyToPlayer(caller, "[草人杯] 目标无效。可用：all / undercover / und / detective / det / task / nor");
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
            "task" or "nor" => "未确认任务玩家",
            _ => null
        };

        if (targetLabel == null) return new List<CCSPlayerController>();

        List<WebPlayerState> webPlayers;
        lock (_webStateLock) webPlayers = _webPlayersBySteamId.Values.ToList();
        var matched = webPlayers.Where(p => target switch
        {
            "all" => true,
            "undercover" or "und" => string.Equals(p.GameRole, "Undercover", StringComparison.OrdinalIgnoreCase),
            "detective" or "det" => string.Equals(p.GameRole, "Detective", StringComparison.OrdinalIgnoreCase),
            "task" or "nor" => string.Equals(p.GameRole, "Undercover", StringComparison.OrdinalIgnoreCase) && !string.Equals(p.UndercoverTaskAckStage, "read", StringComparison.OrdinalIgnoreCase),
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
        "task" or "nor" => "你还没有确认网页上的卧底任务，请打开草人杯网页完成确认。",
        _ => "请注意裁判提示，立即查看网页指挥台。"
    };

    private void SendNoticeToPlayer(CCSPlayerController player, string targetLabel, string message)
    {
        Server.NextFrame(() =>
        {
            if (!ShouldProcessPluginContinuation(_isUnloading)) return;
            if (!player.IsValid) return;
            player.PrintToChat(FormatPrivateChatMessage("================ Notice ================"));
            player.PrintToChat(FormatPrivateChatMessage($" {ChatColors.Red}[重要提醒]{ChatColors.Default} {ChatColors.Green}{targetLabel}{ChatColors.Default}"));
            player.PrintToChat(FormatPrivateChatMessage($" {ChatColors.Yellow}{message}{ChatColors.Default}"));
            var audio = CaorenCup.Contracts.CaorenCupAudioAccess.Play("bridge.notice", [player]);
            if (!audio.Success) Console.WriteLine("[CaorenCupBridge] " + audio.Message);
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

    private bool ShouldPublishMatchTelemetry() => !_duelTelemetryIsolation.IsActive;

    private static bool ShouldApplyWebTeamAssignments(bool duelIsolationActive) => !duelIsolationActive;

    private static bool ShouldApplyWebMatchCounters(bool duelIsolationActive) => !duelIsolationActive;

    // 单挑拆分后，桥接不再持有单挑会话；用隔离状态近似“GameManaged 活跃窗口”，
    // 供命令调度器保留原有的比赛控制命令拦截语义。
    private DuelControlMode CurrentDuelControlModeForWebCommands() =>
        _duelTelemetryIsolation.IsActive ? DuelControlMode.GameManaged : DuelControlMode.None;

    private static bool ShouldProcessPluginContinuation(bool isUnloading) => !isUnloading;

    private void QueueEvent(string type, object payload)
    {
        if (!ShouldPublishMatchTelemetry()) return;

        if (PluginTelemetryPolicy.IsBatchable(type))
        {
            if (_telemetryBatch.TryAdd(type, payload)) return;
            FlushTelemetryBatch();
            if (_telemetryBatch.TryAdd(type, payload)) return;
            Logger.LogWarning("Telemetry batch is full; dropped newest event {Type}", type);
            return;
        }

        FlushTelemetryBatch();

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

    private void FlushTelemetryBatch()
    {
        var events = _telemetryBatch.Drain();
        if (events.Count == 0) return;

        var message = PluginOutboundMessage.ForEventBatch(
            events,
            _currentMatchId,
            Interlocked.Increment(ref _eventSequence),
            DateTimeOffset.UtcNow);

        if (_outboundQueue.Writer.TryWrite(message)) return;
        var dropped = _telemetryBatch.RestoreOlder(events);
        Logger.LogWarning(
            "Outbound queue full; restored {Restored} telemetry events and dropped {Dropped} newest buffered events",
            events.Count - dropped,
            dropped);
    }

    private void QueueSnapshot()
    {
        if (!ShouldPublishMatchTelemetry()) return;

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
                else if (message.Kind == PluginOutboundKind.EventBatch)
                {
                    await PostEventBatchAsync(message, cancellationToken);
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
        if (!ShouldProcessPluginContinuation(_isUnloading)) return;
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
            if (!ShouldProcessPluginContinuation(_isUnloading)) return;
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!ShouldProcessPluginContinuation(_isUnloading)) return;
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
            if (!ShouldProcessPluginContinuation(_isUnloading)) return;
            if (ShouldLogEventFailure(type))
            {
                Logger.LogWarning(ex, "Failed to post event {Type} seq={Sequence}", type, message.Sequence);
            }
        }
    }

    private async Task PostEventBatchAsync(PluginOutboundMessage message, CancellationToken cancellationToken)
    {
        if (!ShouldProcessPluginContinuation(_isUnloading)) return;
        try
        {
            var response = await _http.PostAsJsonAsync("api/plugin/events", new
            {
                matchId = message.MatchId,
                eventSequence = message.Sequence,
                eventTimestampUtc = message.TimestampUtc,
                events = message.Body
            }, _jsonOptions, cancellationToken);
            if (!ShouldProcessPluginContinuation(_isUnloading)) return;
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                Logger.LogWarning(
                    "Telemetry batch seq={Sequence} rejected: {Status} {Body}",
                    message.Sequence,
                    response.StatusCode,
                    body);
            }
            else
            {
                LogDebug("Telemetry batch seq={Sequence} posted", message.Sequence);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (!ShouldProcessPluginContinuation(_isUnloading)) return;
            Logger.LogWarning(ex, "Failed to post telemetry batch seq={Sequence}", message.Sequence);
        }
    }

    private async Task PostSnapshotAsync(object body, long sequence, CancellationToken cancellationToken)
    {
        if (!ShouldProcessPluginContinuation(_isUnloading)) return;
        try
        {
            var response = await _http.PostAsJsonAsync("api/plugin/snapshot", body, _jsonOptions, cancellationToken);
            if (!ShouldProcessPluginContinuation(_isUnloading)) return;

            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!ShouldProcessPluginContinuation(_isUnloading)) return;
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
            if (!ShouldProcessPluginContinuation(_isUnloading)) return;
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

    private async Task<PluginCommandExecutionResult> TryApplyAndAckPluginCommandAsync(
        HeartbeatPluginCommand heartbeatCommand)
    {
        var command = heartbeatCommand.Command;
        if (!ShouldProcessPluginContinuation(_isUnloading))
        {
            return PluginCommandExecutionResult.Deferred;
        }
        if (string.IsNullOrWhiteSpace(command.Id) || string.IsNullOrWhiteSpace(command.Type))
        {
            return PluginCommandExecutionResult.Completed;
        }

        try
        {
            var disposition = await WebCommandGameThreadDispatcher.ScheduleTransactionAsync(
                GameThreadApplicationScheduler.HibernationSafeServerSchedule,
                command,
                CurrentDuelControlModeForWebCommands,
                () => _duelTelemetryIsolation.CleanupRestartPending,
                () => !_duelCvarRestorePending,
                _ => ApplyPluginCommandOnGameThreadAsync(heartbeatCommand));

            if (disposition == PluginCommandTransactionDisposition.Deferred)
            {
                return PluginCommandExecutionResult.Deferred;
            }

            if (disposition == PluginCommandTransactionDisposition.Rejected)
            {
                Logger.LogWarning(
                    "Rejected queued web command because a game-managed duel is active: {Type}",
                    command.Type);
            }

            if (!ShouldProcessPluginContinuation(_isUnloading))
            {
                return PluginCommandExecutionResult.FinalizedAwaitingAck;
            }

            return await AckCommandAsync(command.Id)
                ? PluginCommandExecutionResult.Completed
                : PluginCommandExecutionResult.FinalizedAwaitingAck;
        }
        catch (Exception ex)
        {
            if (ShouldProcessPluginContinuation(_isUnloading))
            {
                Logger.LogWarning(
                    ex,
                    "Failed to apply queued web command; it remains deferred without ACK: {Type}",
                    command.Type);
            }
            return PluginCommandExecutionResult.Deferred;
        }
    }

    private async Task<bool> RejectAndAckPluginCommandAsync(PluginCommand command)
    {
        if (!ShouldProcessPluginContinuation(_isUnloading)) return false;
        if (string.IsNullOrWhiteSpace(command.Id) || string.IsNullOrWhiteSpace(command.Type)) return true;

        Logger.LogWarning(
            "Rejected match-control command from an expired or taken-over heartbeat response: {Type}",
            command.Type);
        return await AckCommandAsync(command.Id);
    }

    private Task<bool> AckPluginCommandOnlyAsync(PluginCommand command) =>
        string.IsNullOrWhiteSpace(command.Id)
            ? Task.FromResult(true)
            : AckCommandAsync(command.Id);

    private Task DrainDeferredHeartbeatCommandsAsync() =>
        _heartbeatCommandTransactions.DrainAsync(
            () => ShouldProcessPluginContinuation(_isUnloading) &&
                !_duelTelemetryIsolation.IsActive &&
                !_duelTelemetryIsolation.CleanupRestartPending &&
                !_duelCvarRestorePending,
            TryApplyAndAckPluginCommandAsync,
            AckPluginCommandOnlyAsync);

    private Task<bool> ApplyPluginCommandOnGameThreadAsync(
        HeartbeatPluginCommand heartbeatCommand)
    {
        if (!ShouldProcessPluginContinuation(_isUnloading)) return Task.FromResult(false);
        var command = heartbeatCommand.Command;
        if (!IsHeartbeatCommandSourceCurrent(heartbeatCommand))
        {
            Logger.LogWarning(
                "Rejected queued web command because its heartbeat source is no longer current: " +
                "{Type} MatchId={MatchId} Generation={Generation}",
                command.Type,
                heartbeatCommand.HeartbeatMatchId,
                heartbeatCommand.BarrierGeneration);
            return Task.FromResult(true);
        }

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
            _ = SendSnapshotAsync();
        }
        else if (string.Equals(command.Type, "EXECUTE_SERVER_COMMAND", StringComparison.OrdinalIgnoreCase))
        {
            if (command.Payload.ValueKind != JsonValueKind.Object ||
                !command.Payload.TryGetProperty("command", out var serverCommandElement))
            {
                Logger.LogWarning("EXECUTE_SERVER_COMMAND missing payload.command");
                return Task.FromResult(true);
            }

            var serverCommand = serverCommandElement.GetString()?.Trim() ?? string.Empty;
            var label = serverCommand;
            if (command.Payload.TryGetProperty("label", out var labelElement))
            {
                label = labelElement.GetString()?.Trim() ?? serverCommand;
            }

            var delaySeconds = ReadPayloadInt(command.Payload, "delaySeconds", 0);
            return ExecuteAllowedServerCommandAsync(
                serverCommand,
                label,
                heartbeatCommand,
                delaySeconds);
        }
        else if (string.Equals(command.Type, "APPLY_TEAM_ASSIGNMENTS", StringComparison.OrdinalIgnoreCase))
        {
            ApplyTeamAssignments(command.Payload);
        }
        else if (string.Equals(command.Type, "CLEAR_TEAM_ASSIGNMENTS", StringComparison.OrdinalIgnoreCase))
        {
            ClearTeamAssignments();
        }
        else
        {
            Logger.LogWarning("Unknown CaorenCup plugin command: {Type}", command.Type);
        }

        return Task.FromResult(true);
    }

    private void ApplyTeamAssignments(JsonElement payload)
    {
        if (!ShouldProcessPluginContinuation(_isUnloading)) return;
        if (!ShouldApplyWebTeamAssignments(_duelTelemetryIsolation.IsActive))
        {
            Logger.LogWarning("Rejected APPLY_TEAM_ASSIGNMENTS because a game-managed duel is active.");
            return;
        }

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
            if (_isUnloading) return;
            EnforceTeamAssignments();
            PrintToAllPlayers($"网页强制分队已同步：{_teamAssignments.Count} 名玩家。");
        });
    }

    private void ClearTeamAssignments()
    {
        if (!ShouldProcessPluginContinuation(_isUnloading)) return;
        if (!ShouldApplyWebTeamAssignments(_duelTelemetryIsolation.IsActive))
        {
            Logger.LogWarning("Rejected CLEAR_TEAM_ASSIGNMENTS because a game-managed duel is active.");
            return;
        }

        _teamAssignments.Clear();
        _teamAssignmentBypass.Clear();
        _teamLockEnabled = false;
        _teamAssignmentsValidFromRound = 0;
        _teamAssignmentsValidUntilRound = 0;
        Logger.LogInformation("Cleared CaorenCup team assignments.");
        Server.NextFrame(() =>
        {
            if (_isUnloading) return;
            PrintToAllPlayers("网页强制分队已解除。");
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
                player.PrintToChat(FormatPrivateChatMessage($"已按网页分队将你调整到 {TeamLabel(targetTeam)}。"));
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

        var steamId = player!.SteamID.ToString();
        if (_teamAssignmentBypass.Contains(steamId)) return HookResult.Continue;

        if (!IsTeamAssignmentActiveForCurrentRound()) return HookResult.Continue;
        if (!_teamAssignments.TryGetValue(steamId, out var targetTeam)) return HookResult.Continue;

        player.PrintToChat(FormatPrivateChatMessage($"本局已按网页分队锁定，不能自行换边。你应在 {TeamLabel(targetTeam)}。"));
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

    private Task<bool> ExecuteAllowedServerCommandAsync(
        string serverCommand,
        string label,
        HeartbeatPluginCommand heartbeatCommand,
        int delaySeconds = 0)
    {
        if (!ShouldProcessPluginContinuation(_isUnloading)) return Task.FromResult(false);
        if (!IsHeartbeatCommandSourceCurrent(heartbeatCommand))
        {
            Logger.LogWarning(
                "Rejected web command because its heartbeat source is no longer current: " +
                "{Command} MatchId={MatchId} Generation={Generation}",
                serverCommand,
                heartbeatCommand.HeartbeatMatchId,
                heartbeatCommand.BarrierGeneration);
            return Task.FromResult(true);
        }

        if (string.IsNullOrWhiteSpace(serverCommand))
        {
            Logger.LogWarning("Rejected empty web command from CaorenCup command center.");
            return Task.FromResult(true);
        }

        if (!BridgeServerCommandPolicy.IsAllowed(serverCommand))
        {
            Logger.LogWarning("Rejected web command because it is not in bridge allowlist: {Command}", serverCommand);
            return Task.FromResult(true);
        }

        bool ExecuteNow()
        {
            if (!ShouldProcessPluginContinuation(_isUnloading)) return false;
            if (!IsHeartbeatCommandSourceCurrent(heartbeatCommand))
            {
                Logger.LogWarning(
                    "Rejected delayed web command because its heartbeat source is no longer current: " +
                    "{Command} MatchId={MatchId} Generation={Generation}",
                    serverCommand,
                    heartbeatCommand.HeartbeatMatchId,
                    heartbeatCommand.BarrierGeneration);
                return true;
            }

            var rejectedByGameManaged =
                WebCommandGameThreadDispatcher.IsGameManagedMatchControlCommand(
                    serverCommand,
                    CurrentDuelControlModeForWebCommands());
            var executed = WebCommandGameThreadDispatcher.TryExecuteServerCommand(
                serverCommand,
                CurrentDuelControlModeForWebCommands,
                () => _duelCvarRestorePending,
                () => _duelTelemetryIsolation.CleanupRestartPending,
                () =>
                {
                    Logger.LogInformation("Executing CaorenCup web command: {Command}", serverCommand);
                    Server.ExecuteCommand(serverCommand);
                    if (!BridgeServerCommandPolicy.ShouldBroadcast(serverCommand)) return;
                    try
                    {
                        PrintToAllPlayers($"网页修改已下发：{label}");
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Failed to announce executed CaorenCup web command: {Command}", serverCommand);
                    }
                });
            if (!executed)
            {
                Logger.LogWarning(
                    "Rejected web match-control command at execution time because a game-managed duel " +
                    "is active, cleanup is pending, or previous duel cvars remain pending: {Command}",
                    serverCommand);
            }

            return executed || rejectedByGameManaged;
        }

        if (delaySeconds > 0)
        {
            return WebCommandGameThreadDispatcher.ScheduleDelayedExecution(
                callback => AddTimer(delaySeconds, callback),
                ExecuteNow);
        }

        return Task.FromResult(ExecuteNow());
    }

    private bool IsHeartbeatCommandSourceCurrent(HeartbeatPluginCommand heartbeatCommand) =>
        _heartbeatResponseOrder.IsCurrentGeneration(heartbeatCommand.BarrierGeneration) &&
        !_duelTelemetryIsolation.IsStaleMatchId(heartbeatCommand.HeartbeatMatchId);

    private void ResetLiveMatchStats(int currentRound)
    {
        _stats.Clear();
        _roundHealthBySteamId.Clear();
        _currentRound = Math.Max(0, currentRound);
        _scoreCt = 0;
        _scoreT = 0;
    }

    private async Task<bool> AckCommandAsync(string commandId)
    {
        if (!ShouldProcessPluginContinuation(_isUnloading)) return false;
        try
        {
            var response = await _http.PostAsJsonAsync("api/plugin/command-ack", new { commandId }, _jsonOptions);
            if (!ShouldProcessPluginContinuation(_isUnloading)) return false;
            if (IsTerminalCommandAckStatus(response.StatusCode))
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    LogDebug(
                        "Command ack target was already absent and is treated as complete: {CommandId}",
                        commandId);
                }
                return true;
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                if (!ShouldProcessPluginContinuation(_isUnloading)) return false;
                Logger.LogWarning("Command ack rejected: {Status} {Body}", response.StatusCode, body);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            if (ShouldProcessPluginContinuation(_isUnloading))
            {
                Logger.LogWarning(ex, "Command ack exception");
            }
            return false;
        }
    }

    private static bool IsTerminalCommandAckStatus(System.Net.HttpStatusCode statusCode) =>
        statusCode == System.Net.HttpStatusCode.NotFound ||
        (int)statusCode is >= 200 and <= 299;

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

    private static double PlayerHorizontalSpeed(CCSPlayerController? player)
    {
        try
        {
            var pawn = player?.PlayerPawn?.Value;
            if (pawn == null) return 0;
            dynamic dynPawn = pawn;
            var velocity = dynPawn.AbsVelocity;
            var x = Convert.ToDouble(velocity.X);
            var y = Convert.ToDouble(velocity.Y);
            return Math.Sqrt(x * x + y * y);
        }
        catch
        {
            return 0;
        }
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

    internal static string StripChatTag(string message) =>
        message.Replace("[草人杯 Notice]", "Notice", StringComparison.Ordinal)
            .Replace("[草人杯]", string.Empty, StringComparison.Ordinal)
            .Trim();

    internal static string FormatPrivateChatMessage(string message) =>
        $" {ChatColors.Green}[草人杯]{ChatColors.Default} {StripChatTag(message)}";

    internal static string FormatGlobalChatMessage(string message) =>
        $" {ChatColors.Red}[草人杯]{ChatColors.Default} {StripChatTag(message)}";

    private static void PrintToAllPlayers(string message) =>
        Server.PrintToChatAll(FormatGlobalChatMessage(message));

    private void ReplyToPlayer(CCSPlayerController? player, string message)
    {
        if (player == null) return;
        Server.NextFrame(() =>
        {
            if (!ShouldProcessPluginContinuation(_isUnloading)) return;
            try
            {
                if (player.IsValid) player.PrintToChat(FormatPrivateChatMessage(message));
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
        ReplyToPlayer(player, "[草人杯]  这是单次登录码；网页验证成功后立即失效");
        ReplyToPlayer(player, $"[草人杯]  未使用时有效期：{validText}；重新获取新码后旧码失效");
        ReplyToPlayer(player, "[草人杯] =================================");
        ReplyToPlayerCenter(player, $"网页登录码：{displayCode}\n请回网页输入");
    }

    private void ReplyToPlayerCenter(CCSPlayerController? player, string message)
    {
        if (player == null) return;
        Server.NextFrame(() =>
        {
            if (!ShouldProcessPluginContinuation(_isUnloading)) return;
            try
            {
                if (player.IsValid) player.PrintToCenter($"[草人杯] {StripChatTag(message)}");
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
            var result = JsonSerializer.Deserialize<PluginApiResponse>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
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

public sealed class PluginApiResponse
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

    [JsonPropertyName("generatedAt")]
    public long GeneratedAt { get; set; }

    [JsonPropertyName("lobbySteamIds")]
    public List<string>? LobbySteamIds { get; set; }

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

public enum PluginOutboundKind
{
    Event,
    EventBatch,
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

    public static PluginOutboundMessage ForEventBatch(IReadOnlyList<PluginTelemetryEvent> events, string? matchId, long sequence, DateTimeOffset timestamp) => new()
    {
        Kind = PluginOutboundKind.EventBatch,
        Body = events,
        MatchId = matchId,
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
