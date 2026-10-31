using System.Diagnostics;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CS2MiniGames.Framework;
using CS2MiniGames.Persistence;
using CS2MiniGames.Tetris;
using CS2MiniGames.Tetris.Core;
using Microsoft.Extensions.Logging;

namespace CS2MiniGames;

public sealed class CS2MiniGamesPlugin : BasePlugin, IPluginConfig<MiniGamesConfig>
{
    private const string TetrisGameName = "tetris";

    private readonly Dictionary<int, RuntimeBinding> _runtimeBindings = [];
    private readonly Stopwatch _clock = new();
    private MiniGameManager? _manager;
    private LeaderboardRepository? _repository;
    private TimeSpan _lastTick;

    public override string ModuleName => "CS2 Mini Games";

    public override string ModuleVersion => "0.1.0";

    public override string ModuleAuthor => "GrassBl0ck";

    public override string ModuleDescription => "Independent mini games for Counter-Strike 2.";

    public MiniGamesConfig Config { get; set; } = new();

    public void OnConfigParsed(MiniGamesConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        Config = config.Normalize(message => Logger.LogWarning("{Message}", message));
    }

    public override void Load(bool hotReload)
    {
        Config = Config.Normalize(message => Logger.LogWarning("{Message}", message));

        _repository = new LeaderboardRepository(Path.Combine(ModuleDirectory, "minigames.db"));
        _repository.Initialize();
        _manager = new MiniGameManager();

        RegisterListener<Listeners.OnTick>(OnTick);
        RegisterListener<Listeners.OnMapEnd>(OnMapEnd);
        RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        RegisterEventHandler<EventRoundStart>(OnRoundStart);
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);

        _lastTick = TimeSpan.Zero;
        _clock.Restart();
    }

    public override void Unload(bool hotReload)
    {
        _clock.Stop();

        RemoveListener<Listeners.OnTick>(OnTick);
        RemoveListener<Listeners.OnMapEnd>(OnMapEnd);
        DeregisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        DeregisterEventHandler<EventRoundStart>(OnRoundStart);
        DeregisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);

        _manager?.CloseAll();
        foreach (var slot in _runtimeBindings.Keys.ToArray())
        {
            ClosePlayerBinding(slot);
        }

        _runtimeBindings.Clear();
        _manager = null;
        _repository?.Dispose();
        _repository = null;
    }

    [ConsoleCommand("css_tetris", "开始俄罗斯方块")]
    public void OnTetrisCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!CanStartGame(player, command) || _manager is null || _repository is null)
        {
            return;
        }

        var steamId = SteamIdentityResolver.ResolveStableSteamId(
            player!.AuthorizedSteamID?.SteamId64,
            player.SteamID);
        if (!steamId.HasValue)
        {
            command.ReplyToCommand("[小游戏] Steam 身份尚未验证，请稍后重试。");
            return;
        }

        var slot = player.Slot;
        if (_manager.TryGet(slot, out _))
        {
            command.ReplyToCommand("[小游戏] 你已经在进行一局游戏，请先按 Tab 退出。");
            return;
        }

        var playerName = player.PlayerName;
        var options = CreateGameOptions();
        var game = new TetrisGameState(options, new SevenBagRandomizer(new Random()));
        var session = new TetrisSession(
            slot,
            game,
            new TetrisRenderer(),
            result => SaveTetrisResult(steamId.Value, playerName, result),
            () => ClosePlayerBinding(slot));

        var binding = new RuntimeBinding(
            player,
            new PlayerInputTracker(
                Config.HorizontalRepeatDelayMs,
                Config.HorizontalRepeatIntervalMs),
            new FrameSendPolicy(TimeSpan.FromMilliseconds(750)));
        var committed = TetrisStartupCoordinator.TryCommit(
            commit: () =>
            {
                if (!_manager.TryStart(session))
                {
                    return false;
                }

                if (_runtimeBindings.TryAdd(slot, binding))
                {
                    return true;
                }

                _manager.Close(slot);
                return false;
            },
            freeze: () => PlayerMovementGuard.Freeze(player),
            printControls: () => PrintControls(player),
            rollback: () => _manager.Close(slot),
            reportError: error =>
                Logger.LogError(error, "启动玩家槽位 {Slot} 的俄罗斯方块时发生错误。", slot));
        if (!committed)
        {
            command.ReplyToCommand("[小游戏] 当前游戏无法开始，请稍后再试。");
        }
    }

    [ConsoleCommand("css_toptetris", "查看俄罗斯方块排行榜")]
    public void OnTopTetrisCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (_repository is null)
        {
            command.ReplyToCommand("[小游戏] 排行榜尚未准备好。");
            return;
        }

        var top = _repository.GetTop(TetrisGameName, 10);
        command.ReplyToCommand("[俄罗斯方块] 全服排行榜 Top 10");
        if (top.Count == 0)
        {
            command.ReplyToCommand("暂无记录，完成一局后即可上榜。");
        }
        else
        {
            for (var index = 0; index < top.Count; index++)
            {
                var entry = top[index];
                command.ReplyToCommand(
                    $"{index + 1}. {entry.PlayerName} - {entry.Score} 分 / {entry.Lines} 行 / Lv.{entry.Level}");
            }
        }

        if (player is null || !player.IsValid || player.IsBot)
        {
            return;
        }

        var steamId = SteamIdentityResolver.ResolveStableSteamId(
            player.AuthorizedSteamID?.SteamId64,
            player.SteamID);
        if (!steamId.HasValue)
        {
            command.ReplyToCommand("你的 Steam 身份尚未验证，暂时无法查询个人最佳。");
            return;
        }

        var best = _repository.GetPlayerBest(TetrisGameName, steamId.Value);
        command.ReplyToCommand(
            best is null
                ? "你的最佳成绩：暂无。输入 css_tetris 开始挑战。"
                : $"你的最佳成绩：{best.Score} 分 / {best.Lines} 行 / Lv.{best.Level}");
    }

    [ConsoleCommand("css_tetrishelp", "查看俄罗斯方块操作说明")]
    public void OnTetrisHelpCommand(CCSPlayerController? player, CommandInfo command) =>
        command.ReplyToCommand(
            "[俄罗斯方块] A/D 移动，S 软降，Space 硬降，E 顺时针，R 逆时针/重开，W 暂存，Tab 退出。");

    [ConsoleCommand("css_minigames", "查看小游戏列表")]
    public void OnMiniGamesCommand(CCSPlayerController? player, CommandInfo command) =>
        command.ReplyToCommand(
            "[小游戏] 当前可用：俄罗斯方块（css_tetris）。帮助：css_tetrishelp；排行榜：css_toptetris。");

    private TetrisGameOptions CreateGameOptions() =>
        new(
            Config.InitialFallIntervalMs,
            Config.MinimumFallIntervalMs,
            Config.LockDelayMs,
            Config.MaxLockResets,
            Config.HorizontalRepeatDelayMs,
            Config.HorizontalRepeatIntervalMs,
            Config.LinesPerLevel);

    private bool CanStartGame(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null)
        {
            command.ReplyToCommand("[小游戏] 该命令只能由游戏内玩家使用。");
            return false;
        }

        if (!player.IsValid || player.IsBot)
        {
            command.ReplyToCommand("[小游戏] 机器人无法开始小游戏。");
            return false;
        }

        return true;
    }

    private void OnTick()
    {
        if (_manager is null)
        {
            return;
        }

        var now = _clock.Elapsed;
        var elapsed = now - _lastTick;
        _lastTick = now;

        foreach (var (slot, binding) in _runtimeBindings.ToArray())
        {
            try
            {
                if (!_manager.TryGet(slot, out var session) || session is null)
                {
                    ClosePlayerBinding(slot);
                    continue;
                }

                var player = binding.Controller;
                if (!player.IsValid || player.IsBot)
                {
                    _manager.Close(slot);
                    continue;
                }

                var buttons = PlayerButtonSource.Read(player, () => player.Buttons);
                var actions = binding.Input.Read(buttons, now);
                if (actions.Contains(MiniGameAction.Exit))
                {
                    _manager.Close(slot);
                    continue;
                }

                session.HandleActions(actions);
                session.Update(elapsed);
                if (binding.FrameSender.ShouldSend(session.Revision, now))
                {
                    player.PrintToCenterHtml(session.Render(), duration: 1);
                }
            }
            catch (Exception error)
            {
                Logger.LogError(error, "更新玩家槽位 {Slot} 的小游戏时发生错误。", slot);
                _manager.Close(slot);
            }
        }
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        if (@event.Userid is { } player)
        {
            _manager?.Close(player.Slot);
        }

        return HookResult.Continue;
    }

    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _manager?.CloseAll();
        return HookResult.Continue;
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        if (@event.Userid is { } player)
        {
            _manager?.Close(player.Slot);
        }

        return HookResult.Continue;
    }

    private void OnMapEnd() => _manager?.CloseAll();

    private void SaveTetrisResult(ulong steamId, string playerName, TetrisResult result)
    {
        _repository?.SaveIfHigher(
            new LeaderboardEntry(
                TetrisGameName,
                steamId,
                playerName,
                result.Score,
                result.Lines,
                result.Level,
                DateTimeOffset.UtcNow));
    }

    private void ClosePlayerBinding(int slot)
    {
        _runtimeBindings.Remove(slot);

        try
        {
            var currentPlayer = Utilities.GetPlayers().FirstOrDefault(player => player.Slot == slot);
            PlayerMovementGuard.Restore(currentPlayer);
        }
        catch (Exception error)
        {
            Logger.LogWarning(error, "恢复玩家槽位 {Slot} 的移动状态时发生错误。", slot);
        }
    }

    private static void PrintControls(CCSPlayerController player)
    {
        player.PrintToChat(
            "[俄罗斯方块] A/D 移动｜S 软降｜Space 硬降｜E 顺时针｜R 逆时针/重开｜W 暂存｜Tab 退出");
        player.PrintToChat("[俄罗斯方块] 输入 css_toptetris 查看排行榜。");
    }

    private sealed record RuntimeBinding(
        CCSPlayerController Controller,
        PlayerInputTracker Input,
        FrameSendPolicy FrameSender);
}
