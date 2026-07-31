using System.Collections.Concurrent;
using CaorenCup.WeaponPaints.Core;
using CaorenCup.WeaponPaints.Menus;
using CaorenCup.WeaponPaints.Persistence;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Timers;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace CaorenCup.WeaponPaints;

public partial class CaorenWeaponPaintsPlugin
{
    private readonly PlayerLoadoutCache _loadoutCache = new();
    private readonly ConcurrentDictionary<string, byte> _loadedPlayers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _loadingPlayers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _pendingRefresh = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _refreshCooldowns = new(StringComparer.Ordinal);

    private MySqlLoadoutRepository? _repository;
    private LoadoutMutationService? _mutations;
    private LoadoutReloadService? _reloads;
    private LocalCatalogSnapshot? _catalog;
    private SkinMenuController? _menus;
    private bool _databaseConnected;
    private bool _schemaReady;
    private bool _catalogReady;
    private bool _gameDataReady;
    private bool _officialRoundActive;
    private bool _mvpPlayed;
    private bool _giveNamedItemHooked;
    private int _databaseInitializationActive;

    private void StartRuntime(bool hotReload)
    {
        RegisterHealthCommand();
        if (!Config.Enabled)
        {
            Logger.LogInformation("Caoren WeaponPaints 已由配置总开关关闭。");
            return;
        }

        _gameDataReady = File.Exists(GetGlobalGameDataPath());
        if (!_gameDataReady)
        {
            Logger.LogError("缺少 CounterStrikeSharp 全局 gamedata/weaponpaints.json，插件将保持不健康状态。");
        }

        try
        {
            _catalog = LocalCatalogSnapshot.Load(Path.Combine(ModuleDirectory, "data"));
            SetSkinMetadata(_catalog.LegacySkinMetadata.ToList());
            _catalogReady = true;
        }
        catch (Exception exception)
        {
            _catalogReady = false;
            Logger.LogError("本地物品目录加载失败：{Message}", exception.Message);
        }

        RegisterPlayerCommands();
        RegisterGameEvents();

        if (string.IsNullOrWhiteSpace(Config.DatabaseHost) ||
            string.IsNullOrWhiteSpace(Config.DatabaseUser) ||
            string.IsNullOrWhiteSpace(Config.DatabaseName))
        {
            Logger.LogError("数据库配置不完整。请配置 DatabaseHost、DatabaseUser 和 DatabaseName。");
            return;
        }

        var builder = new MySqlConnectionStringBuilder
        {
            Server = Config.DatabaseHost,
            Port = checked((uint)Math.Clamp(Config.DatabasePort, 1, 65535)),
            UserID = Config.DatabaseUser,
            Password = Config.DatabasePassword,
            Database = Config.DatabaseName,
            CharacterSet = "utf8mb4",
            Pooling = true,
            MaximumPoolSize = 32,
            ConnectionTimeout = 5
        };
        _repository = new MySqlLoadoutRepository(builder.ConnectionString);
        _mutations = new LoadoutMutationService(_repository, _loadoutCache);
        _reloads = new LoadoutReloadService(_repository, _loadoutCache);

        if (_catalog is not null)
        {
            var timeout = TimeSpan.FromSeconds(Math.Clamp(Config.InputTimeoutSeconds, 5, 600));
            _menus = new SkinMenuController(
                _catalog,
                _loadoutCache,
                _mutations,
                new ChatInputSessionStore(timeout),
                OnMenuMutation,
                connected => _databaseConnected = connected,
                Config.Prefix);
        }

        _ = InitializeDatabaseAsync(hotReload);
        AddTimer(30f, CheckDatabaseHealth, TimerFlags.REPEAT);
    }

    private void StopRuntime()
    {
        if (_giveNamedItemHooked)
        {
            VirtualFunctions.GiveNamedItemFunc.Unhook(OnGiveNamedItemPost, HookMode.Post);
            _giveNamedItemHooked = false;
        }

        _menus?.ClearInputs();
        _loadoutCache.Clear();
        _loadedPlayers.Clear();
        _loadingPlayers.Clear();
        _pendingRefresh.Clear();
        _refreshCooldowns.Clear();
    }

    private async Task InitializeDatabaseAsync(bool hotReload)
    {
        if (Interlocked.Exchange(ref _databaseInitializationActive, 1) == 1)
        {
            return;
        }

        try
        {
            await _repository!.InitializeAsync().ConfigureAwait(false);
            _databaseConnected = true;
            _schemaReady = true;
            Logger.LogInformation("caoren_weaponpaints 表结构检查完成。");

            Server.NextFrame(() =>
            {
                foreach (var player in Utilities.GetPlayers().Where(WeaponPaintsUtility.IsPlayerValid))
                {
                    _ = LoadPlayerAsync(player);
                }
            });
        }
        catch (Exception exception)
        {
            _databaseConnected = false;
            _schemaReady = false;
            Logger.LogError("caoren_weaponpaints 初始化失败：{Message}", exception.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _databaseInitializationActive, 0);
        }
    }

    private void CheckDatabaseHealth()
    {
        if (_repository is null)
        {
            return;
        }

        if (!_schemaReady)
        {
            _ = InitializeDatabaseAsync(false);
            return;
        }

        _ = ProbeDatabaseAsync();
    }

    private async Task ProbeDatabaseAsync()
    {
        try
        {
            _databaseConnected = await _repository!.ProbeAsync().ConfigureAwait(false);
        }
        catch
        {
            _databaseConnected = false;
        }
    }

    private void RegisterHealthCommand()
    {
        AddCommand("css_skinstatus", "查看 Caoren WeaponPaints 健康状态", (player, info) =>
        {
            if (player is not null && !HasAdminPermission(player))
            {
                info.ReplyToCommand($" {Config.Prefix} 只有管理员可以查看健康状态。");
                return;
            }

            info.ReplyToCommand($" {Config.Prefix} {CurrentHealth().ToChineseSummary()}");
        });
    }

    private void RegisterPlayerCommands()
    {
        foreach (var (alias, target) in SkinCommandCatalog.PlayerCommands)
        {
            AddCommand($"css_{alias}", "草人杯皮肤菜单", (player, _) => HandlePlayerCommand(player, target));
        }

        AddCommand("wp_refresh", "管理员强制刷新皮肤：wp_refresh <SteamID64|all>", HandleAdminRefresh);
        AddCommandListener("say", OnChatMessage);
        AddCommandListener("say_team", OnChatMessage);
    }

    private void RegisterGameEvents()
    {
        RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
        RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        RegisterEventHandler<EventRoundStart>(OnRoundStart);
        RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
        RegisterEventHandler<EventRoundMvp>(OnRoundMvp);
        RegisterEventHandler<EventItemPickup>(OnItemPickup);
        RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);

        VirtualFunctions.GiveNamedItemFunc.Hook(OnGiveNamedItemPost, HookMode.Post);
        _giveNamedItemHooked = true;
    }

    private void HandlePlayerCommand(CCSPlayerController? player, SkinCommandTarget target)
    {
        if (!WeaponPaintsUtility.IsPlayerValid(player) || player is null)
        {
            return;
        }

        if (target == SkinCommandTarget.Refresh)
        {
            RequestRefresh(player, false);
            return;
        }

        if (!_catalogReady || !_gameDataReady || !_schemaReady || _menus is null)
        {
            Print(player, "插件尚未就绪，请让管理员执行 css_skinstatus 查看原因。");
            return;
        }

        var steamId = player.SteamID.ToString();
        if (!_loadedPlayers.ContainsKey(steamId))
        {
            _ = LoadPlayerAsync(player);
            Print(player, "正在读取你的配置，请稍后再次输入 /skin。");
            return;
        }

        _menus.OpenForCommand(player, target);
    }

    private HookResult OnChatMessage(CCSPlayerController? player, CommandInfo info)
    {
        if (player is null || _menus is null)
        {
            return HookResult.Continue;
        }

        return _menus.HandleChatInput(player, info.ArgString)
            ? HookResult.Handled
            : HookResult.Continue;
    }

    private void HandleAdminRefresh(CCSPlayerController? caller, CommandInfo info)
    {
        if (caller is not null && !HasAdminPermission(caller))
        {
            info.ReplyToCommand($" {Config.Prefix} 权限不足。");
            return;
        }

        var target = info.ArgCount > 1 ? info.GetArg(1) : string.Empty;
        if (string.IsNullOrWhiteSpace(target))
        {
            info.ReplyToCommand("用法：wp_refresh <SteamID64|all>");
            return;
        }

        var safe = info.ArgCount > 2 && RefreshCommandMode.IsSafe(info.GetArg(2));
        var players = Utilities.GetPlayers().Where(WeaponPaintsUtility.IsPlayerValid).ToArray();
        if (target.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var player in players)
            {
                _ = ReloadAndRequestRefreshAsync(player, !safe);
            }

            info.ReplyToCommand(safe
                ? $"已请求安全重载 {players.Length} 名玩家；正式回合存活玩家将在下次出生应用。"
                : $"已请求强制重载并刷新 {players.Length} 名玩家。");
            return;
        }

        var found = players.FirstOrDefault(player => player.SteamID.ToString() == target);
        if (found is null)
        {
            info.ReplyToCommand("未找到该 SteamID64 的在线玩家。");
            return;
        }

        _ = ReloadAndRequestRefreshAsync(found, !safe);
        info.ReplyToCommand(safe
            ? $"已请求安全重载 {found.PlayerName}；正式回合存活时将在下次出生应用。"
            : $"已请求强制重载并刷新 {found.PlayerName}。");
    }

    private async Task ReloadAndRequestRefreshAsync(CCSPlayerController player, bool force)
    {
        if (_reloads is null || !WeaponPaintsUtility.IsPlayerValid(player))
        {
            return;
        }

        var steamId = player.SteamID.ToString();
        var slot = player.Slot;
        try
        {
            var loadout = await _reloads.ReloadAsync(steamId).ConfigureAwait(false);
            _databaseConnected = true;
            Server.NextFrame(() =>
            {
                if (!WeaponPaintsUtility.IsPlayerValid(player) || player.Slot != slot || player.SteamID.ToString() != steamId)
                {
                    return;
                }

                _loadedPlayers[steamId] = 0;
                LegacyLoadoutAdapter.ApplyToRuntime(slot, loadout);
                RequestRefresh(player, force);
            });
        }
        catch (Exception exception)
        {
            _databaseConnected = false;
            Logger.LogWarning("重载玩家 {SteamId} 换肤配置失败：{Message}", steamId, exception.Message);
            Server.NextFrame(() =>
            {
                if (WeaponPaintsUtility.IsPlayerValid(player) && player.SteamID.ToString() == steamId)
                {
                    Print(player, "从数据库重载换肤配置失败，请稍后重试。");
                }
            });
        }
    }

    private HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo unusedInfo)
    {
        if (WeaponPaintsUtility.IsPlayerValid(@event.Userid) && _schemaReady)
        {
            _ = LoadPlayerAsync(@event.Userid!);
        }

        return HookResult.Continue;
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo unusedInfo)
    {
        var player = @event.Userid;
        if (player is null || player.IsBot)
        {
            return HookResult.Continue;
        }

        var steamId = player.SteamID.ToString();
        _menus?.CancelInput(player.SteamID);
        _loadedPlayers.TryRemove(steamId, out _);
        _loadingPlayers.TryRemove(steamId, out _);
        _pendingRefresh.TryRemove(steamId, out _);
        _refreshCooldowns.TryRemove(steamId, out _);
        _loadoutCache.Remove(steamId);
        ClearSlot(player.Slot);
        return HookResult.Continue;
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo unusedInfo)
    {
        var player = @event.Userid;
        if (WeaponPaintsUtility.IsPlayerValid(player) && player is not null)
        {
            _pendingRefresh.TryRemove(player.SteamID.ToString(), out _);
            AddTimer(0.2f, () => ApplyAll(player));
        }

        return HookResult.Continue;
    }

    private HookResult OnRoundStart(EventRoundStart _, GameEventInfo __)
    {
        _officialRoundActive = true;
        _mvpPlayed = false;
        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd _, GameEventInfo __)
    {
        _officialRoundActive = false;
        return HookResult.Continue;
    }

    private HookResult OnRoundMvp(EventRoundMvp @event, GameEventInfo info)
    {
        if (_mvpPlayed)
        {
            return HookResult.Continue;
        }

        var player = @event.Userid;
        if (!WeaponPaintsUtility.IsPlayerValid(player) || player is null ||
            !GPlayersMusic.TryGetValue(player.Slot, out var kits) ||
            !kits.TryGetValue(player.Team, out var kit) || kit == 0)
        {
            return HookResult.Continue;
        }

        @event.Musickitid = kit;
        @event.Nomusic = 0;
        info.DontBroadcast = true;
        _mvpPlayed = true;
        new EventRoundMvp(true) { Userid = player, Musickitid = kit, Nomusic = 0 }.FireEvent(false);
        return HookResult.Continue;
    }

    private HookResult OnItemPickup(EventItemPickup @event, GameEventInfo unusedInfo)
    {
        if (!_gameDataReady)
        {
            return HookResult.Continue;
        }

        var player = @event.Userid;
        if (WeaponPaintsUtility.IsPlayerValid(player) && player is not null)
        {
            AddTimer(0.05f, () =>
            {
                var weapon = player.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value;
                if (weapon is { IsValid: true })
                {
                    GivePlayerWeaponSkin(player, weapon);
                }
            });
        }

        return HookResult.Continue;
    }

    private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo unusedInfo)
    {
        var attacker = @event.Attacker;
        var victim = @event.Userid;
        if (!WeaponPaintsUtility.IsPlayerValid(attacker) || attacker is null ||
            victim is null || !victim.IsValid || victim == attacker)
        {
            return HookResult.Continue;
        }

        var activeWeapon = attacker.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value;
        if (activeWeapon is null)
        {
            return HookResult.Continue;
        }

        var team = attacker.TeamNum == (byte)TeamSide.Terrorist
            ? TeamSide.Terrorist
            : TeamSide.CounterTerrorist;
        var defIndex = activeWeapon.AttributeManager.Item.ItemDefinitionIndex;
        var selection = _loadoutCache.Get(attacker.SteamID.ToString())?.GetWeapon(team, defIndex);
        if (selection?.StatTrakEnabled != true || _mutations is null)
        {
            return HookResult.Continue;
        }

        var copy = selection.Clone();
        copy.StatTrakCount++;
        _ = PersistStatTrakAsync(attacker, team, copy);

        CAttributeListSetOrAddAttributeValueByName.Invoke(
            activeWeapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,
            "kill eater",
            ViewAsFloat((uint)copy.StatTrakCount));
        CAttributeListSetOrAddAttributeValueByName.Invoke(
            activeWeapon.AttributeManager.Item.AttributeList.Handle,
            "kill eater",
            ViewAsFloat((uint)copy.StatTrakCount));
        return HookResult.Continue;
    }

    private HookResult OnGiveNamedItemPost(DynamicHook hook)
    {
        if (!_gameDataReady)
        {
            return HookResult.Continue;
        }

        try
        {
            var itemServices = hook.GetParam<CCSPlayer_ItemServices>(0);
            var weapon = hook.GetReturn<CBasePlayerWeapon>();
            if (weapon.IsValid && weapon.DesignerName.Contains("weapon", StringComparison.OrdinalIgnoreCase))
            {
                var player = GetPlayerFromItemServices(itemServices);
                if (player is not null)
                {
                    GivePlayerWeaponSkin(player, weapon);
                }
            }
        }
        catch (Exception exception)
        {
            Logger.LogDebug("GiveNamedItem 应用皮肤失败：{Message}", exception.Message);
        }

        return HookResult.Continue;
    }

    private async Task LoadPlayerAsync(CCSPlayerController player)
    {
        if (_repository is null || !WeaponPaintsUtility.IsPlayerValid(player))
        {
            return;
        }

        var steamId = player.SteamID.ToString();
        if (!_loadingPlayers.TryAdd(steamId, 0))
        {
            return;
        }

        var slot = player.Slot;
        try
        {
            var loadout = await _repository.LoadAsync(steamId).ConfigureAwait(false);
            _loadoutCache.Set(loadout);
            _loadedPlayers[steamId] = 0;
            _databaseConnected = true;
            Server.NextFrame(() =>
            {
                if (WeaponPaintsUtility.IsPlayerValid(player) &&
                    player.Slot == slot && player.SteamID.ToString() == steamId)
                {
                    LegacyLoadoutAdapter.ApplyToRuntime(slot, loadout);
                    if (player.PawnIsAlive)
                    {
                        ApplyAll(player);
                    }
                }
            });
        }
        catch (Exception exception)
        {
            _databaseConnected = false;
            Logger.LogWarning("读取玩家 {SteamId} 皮肤配置失败：{Message}", steamId, exception.Message);
        }
        finally
        {
            _loadingPlayers.TryRemove(steamId, out _);
        }
    }

    private async Task PersistStatTrakAsync(CCSPlayerController player, TeamSide team, WeaponSelection selection)
    {
        try
        {
            await _mutations!.SaveWeaponAsync(player.SteamID.ToString(), team, selection).ConfigureAwait(false);
            _databaseConnected = true;
            Server.NextFrame(() =>
            {
                var loadout = _loadoutCache.Get(player.SteamID.ToString());
                if (loadout is not null && player.IsValid)
                {
                    LegacyLoadoutAdapter.ApplyToRuntime(player.Slot, loadout);
                }
            });
        }
        catch (Exception exception)
        {
            _databaseConnected = false;
            Logger.LogWarning("StatTrak 保存失败：{Message}", exception.Message);
        }
    }

    private void OnMenuMutation(CCSPlayerController player)
    {
        _databaseConnected = true;
        RequestRefresh(player, false);
    }

    private void RequestRefresh(CCSPlayerController player, bool force)
    {
        var steamId = player.SteamID.ToString();
        if (!force)
        {
            var now = DateTimeOffset.UtcNow;
            if (_refreshCooldowns.TryGetValue(steamId, out var until) && now < until)
            {
                Print(player, "刷新请求过于频繁，请稍后再试。");
                return;
            }

            _refreshCooldowns[steamId] = now.AddSeconds(Math.Max(0, Config.RefreshCooldownSeconds));
        }

        var decision = RefreshPolicy.Decide(new RefreshContext(
            Config.Enabled,
            _loadedPlayers.ContainsKey(steamId),
            player.PawnIsAlive,
            IsWarmup(),
            _officialRoundActive,
            force));

        switch (decision)
        {
            case RefreshDecision.ApplyNow:
                ApplyAll(player);
                Print(player, force ? "管理员已强制刷新皮肤。" : "皮肤已刷新。");
                break;
            case RefreshDecision.QueueForSpawn:
                _pendingRefresh[steamId] = 0;
                Print(player, "当前处于正式回合或尚未出生，配置将在下次出生时应用。");
                break;
            default:
                Print(player, "配置尚未读取完成，暂时无法刷新。");
                break;
        }
    }

    private void ApplyAll(CCSPlayerController player)
    {
        if (!WeaponPaintsUtility.IsPlayerValid(player) ||
            !_gameDataReady ||
            !_loadedPlayers.ContainsKey(player.SteamID.ToString()) ||
            !player.PawnIsAlive)
        {
            return;
        }

        GivePlayerGloves(player);
        RefreshWeapons(player);
        GivePlayerAgent(player);
        GivePlayerMusicKit(player);
        AddTimer(0.15f, () => GivePlayerPin(player));
    }

    private PluginHealth CurrentHealth()
    {
        return Config.Enabled
            ? new PluginHealth(true, _databaseConnected, _schemaReady, _catalogReady, _gameDataReady)
            : PluginHealth.Disabled();
    }

    private bool HasAdminPermission(CCSPlayerController player)
    {
        return string.IsNullOrWhiteSpace(Config.AdminPermission) ||
               AdminManager.PlayerHasPermissions(player, Config.AdminPermission);
    }

    private bool IsWarmup()
    {
        var rules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
            .FirstOrDefault()?.GameRules;
        return rules?.WarmupPeriod ?? false;
    }

    private string GetGlobalGameDataPath()
    {
        var pluginsDirectory = Directory.GetParent(ModuleDirectory);
        var counterStrikeSharpDirectory = pluginsDirectory?.Parent;
        return Path.Combine(counterStrikeSharpDirectory?.FullName ?? ModuleDirectory, "gamedata", "weaponpaints.json");
    }

    private void Print(CCSPlayerController player, string message)
    {
        player.PrintToChat($" {Config.Prefix} {message}");
    }

    private static void ClearSlot(int slot)
    {
        GPlayerWeaponsInfo.TryRemove(slot, out _);
        GPlayersKnife.TryRemove(slot, out _);
        GPlayersGlove.TryRemove(slot, out _);
        GPlayersAgent.TryRemove(slot, out _);
        GPlayersMusic.TryRemove(slot, out _);
        GPlayersPin.TryRemove(slot, out _);
    }

}
