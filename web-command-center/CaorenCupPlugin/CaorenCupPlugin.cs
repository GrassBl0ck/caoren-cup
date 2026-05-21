using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace CaorenCupPlugin;

[MinimumApiVersion(367)]
public sealed class CaorenCupPlugin : BasePlugin
{
    public override string ModuleName => "CaorenCup Command Center Bridge";
    public override string ModuleVersion => "0.3.8";
    public override string ModuleAuthor => "CaorenCup";
    public override string ModuleDescription => "Bridge CS2 score, player binding and match stats to the CaorenCup web command center.";

    private readonly HttpClient _http = new();
    private readonly Dictionary<string, LocalPlayerStats> _stats = new();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private CaorenConfig _config = new();
    private string? _currentMatchId;
    private int _currentRound;
    private int _scoreCt;
    private int _scoreT;
    private DateTime _lastPlayerHurtWarningUtc = DateTime.MinValue;
    private readonly Dictionary<string, CsTeam> _teamAssignments = new(StringComparer.Ordinal);
    private readonly HashSet<string> _teamAssignmentBypass = new(StringComparer.Ordinal);
    private bool _teamLockEnabled;
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
        "css_bq"
};

    public override void Load(bool hotReload)
    {
        LoadConfig();
        _http.BaseAddress = new Uri(_config.CommandCenterBaseUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Remove("x-caoren-plugin-token");
        _http.DefaultRequestHeaders.Add("x-caoren-plugin-token", _config.PluginToken);

        AddCommand("css_ccbind", "绑定草人杯网页身份。用法：!ccbind 1234", OnBindCommand);
        AddCommand("css_cclogin", "获取草人杯网页登录码。用法：!cclogin", OnGameLoginCommand);
        AddCommand("css_cccode", "获取草人杯网页登录码。用法：!cccode", OnGameLoginCommand);
        AddCommand("css_ccstate", "查看草人杯指挥台连接状态", OnStateCommand);
        AddCommand("css_ccsnapshot", "手动向草人杯指挥台推送一次战绩快照", OnSnapshotCommand);
        AddCommandListener("jointeam", OnJoinTeamCommand, HookMode.Pre);

        AddTimer(Math.Max(3, _config.HeartbeatSeconds), () => _ = SendHeartbeatAsync(), TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(Math.Max(5, _config.HeartbeatSeconds), () => _ = SendSnapshotAsync(), TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(1.0f, EnforceTeamAssignments, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);

        Logger.LogInformation("{Name} loaded. CommandCenter={BaseUrl}", ModuleName, _config.CommandCenterBaseUrl);
        _ = SendHeartbeatAsync();
    }

    public override void Unload(bool hotReload)
    {
        RemoveCommandListener("jointeam", OnJoinTeamCommand, HookMode.Pre);
        _http.Dispose();
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
        var name = player.PlayerName;
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
        var name = player.PlayerName;
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

    private static object PlayerEquipment(CCSPlayerController? player)
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

        return new
        {
            activeWeapon = activeWeapon?.DesignerName ?? string.Empty,
            weapons,
            armor = pawn?.ArmorValue ?? 0,
            hasHelmet
        };
    }

    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _currentRound++;
        EnforceTeamAssignments();
        _ = PostEventAsync("round_start", new
        {
            round = _currentRound,
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
        EnsureLocalStats(victimSteamId, victim.PlayerName).Deaths++;

        string? attackerSteamId = null;
        string? attackerTeam = null;
        if (IsRealPlayer(attacker) && attacker!.SteamID != victim.SteamID)
        {
            attackerSteamId = attacker.SteamID.ToString();
            attackerTeam = TeamName(attacker.TeamNum);
            EnsureLocalStats(attackerSteamId, attacker.PlayerName).Kills++;
        }

        string? assisterSteamId = null;
        if (IsRealPlayer(assister) && assister!.SteamID != victim.SteamID && assister.SteamID.ToString() != attackerSteamId)
        {
            assisterSteamId = assister.SteamID.ToString();
            EnsureLocalStats(assisterSteamId, assister.PlayerName).Assists++;
        }

        var attackerEquipment = PlayerEquipment(attacker);
        var victimEquipment = PlayerEquipment(victim);

        _ = PostEventAsync("player_death", new
        {
            round = _currentRound,
            attackerSteamId,
            attackerTeam,
            victimSteamId,
            victimTeam = TeamName(victim.TeamNum),
            assisterSteamId,
            headshot = @event.Headshot,
            weapon = @event.Weapon,
            attackerEquipment,
            victimEquipment,
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

        var attackerSteamId = attacker.SteamID.ToString();
        EnsureLocalStats(attackerSteamId, attacker.PlayerName).Damage += @event.DmgHealth;

        _ = PostEventAsync("player_hurt", new
        {
            round = _currentRound,
            attackerSteamId,
            attackerTeam = TeamName(attacker.TeamNum),
            victimSteamId = victim.SteamID.ToString(),
            victimTeam = TeamName(victim.TeamNum),
            damage = @event.DmgHealth,
            health = @event.Health,
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

        _ = PostEventAsync("round_end", new
        {
            round = _currentRound,
            winner,
            scoreCT = _scoreCt,
            scoreT = _scoreT,
            mapName = SafeMapName(),
            players = BuildLivePlayers()
        });
        _ = SendSnapshotAsync();
        return HookResult.Continue;
    }

    private object[] BuildLivePlayers()
    {
        return Utilities.GetPlayers()
            .Where(IsRealPlayer)
            .Select(p =>
            {
                var steamId = p.SteamID.ToString();
                var stats = EnsureLocalStats(steamId, p.PlayerName);
                return new
                {
                    steamId,
                    name = p.PlayerName,
                    team = TeamName(p.TeamNum),
                    stats.Kills,
                    stats.Deaths,
                    stats.Assists,
                    stats.Damage,
                    isAlive = p.PawnIsAlive
                };
            })
            .Cast<object>()
            .ToArray();
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
                    _currentRound = 0;
                    _scoreCt = 0;
                    _scoreT = 0;
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

    private async Task PostEventAsync(string type, object payload)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/plugin/event", new
            {
                matchId = _currentMatchId,
                type,
                payload
            }, _jsonOptions);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                if (ShouldLogEventFailure(type))
                {
                    Logger.LogWarning("Event {Type} rejected: {Status} {Body}", type, response.StatusCode, body);
                }
            }
            else
            {
                if (!string.Equals(type, "player_hurt", StringComparison.OrdinalIgnoreCase))
                {
                    LogDebug("Event {Type} posted", type);
                }
            }
        }
        catch (Exception ex)
        {
            if (ShouldLogEventFailure(type))
            {
                Logger.LogWarning(ex, "Failed to post event {Type}", type);
            }
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

                ExecuteAllowedServerCommand(serverCommand, label);
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

        Logger.LogInformation("Applied {Count} CaorenCup team assignments. Lock={Lock}", _teamAssignments.Count, _teamLockEnabled);
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
        Logger.LogInformation("Cleared CaorenCup team assignments.");
        Server.NextFrame(() =>
        {
            Server.PrintToChatAll($" {ChatColors.Green}[草人杯]{ChatColors.Default} 网页强制分队已解除。");
        });
    }

    private void EnforceTeamAssignments()
    {
        if (!_teamLockEnabled || _teamAssignments.Count == 0) return;

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

    private static string TeamLabel(CsTeam team)
    {
        return team == CsTeam.CounterTerrorist ? "CT" : team == CsTeam.Terrorist ? "T" : team.ToString();
    }


    private void ExecuteAllowedServerCommand(string serverCommand, string label)
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
        Server.NextFrame(() =>
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
        });
    }    private void ResetLiveMatchStats(int currentRound)
    {
        _stats.Clear();
        _currentRound = Math.Max(0, currentRound);
        _scoreCt = 0;
        _scoreT = 0;
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

    private static bool IsRealPlayer(CCSPlayerController? player)
    {
        return player is { IsValid: true, IsBot: false, IsHLTV: false };
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
}
