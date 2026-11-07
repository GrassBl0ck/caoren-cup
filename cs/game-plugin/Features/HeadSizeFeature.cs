using CaorenCup.FunCommands;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace CaorenCup.Features;

public sealed class HeadSizeFeature : ICaorenFeature
{
    private const string PawnClass = "CCSPlayerPawn";
    private const string ScaleField = "m_flBigHeadScale";
    private const string RulesClass = "CCSGameRules";
    private const string MutatorsField = "m_nActiveMutators";
    private const string UnsupportedMessage = "当前服务端缺少原生头部缩放支持，请检查 CS2 和 CounterStrikeSharp 版本。";
    private readonly HeadSizeRuntime _runtime = new();
    private readonly HeadSizeNativeGate _nativeGate = new();
    private bool? _schemaSupported;
    private bool _unloaded;
    private double _nextGateCheck;
    private MemoryFunctionVoid<CCSPlayerPawnBase>? _playerNativeReset;
    private bool _nativeResetHooked;
    private int _mainThread;
    private ILogger? _logger;

    public string FeatureName => "HeadSize";

    public void Init(CaorenCupPlugin plugin)
    {
        _logger = plugin.Logger;
        _mainThread = Environment.CurrentManagedThreadId;
        plugin.AddCommand("css_hd", "管理员头部缩放：/hd 0；/hd 1 [强度]；/hd 2 <玩家名|t|ct> <倍率>", OnCommand);
        plugin.RegisterListener<Listeners.OnServerPostEntityThink>(OnTick);
        plugin.RegisterListener<Listeners.OnMapEnd>(() => ResetAll());
        plugin.RegisterListener<Listeners.OnMapStart>(_ =>
        {
            _schemaSupported = null;
            ResetAll();
        });
        ResetAll();
    }

    public void OnConfigParsed(CaorenCupConfig config) { }
    public void OnUnload()
    {
        _unloaded = true;
        try
        {
            if (_nativeResetHooked) _playerNativeReset!.Unhook(OnPlayerNativeResetPost, HookMode.Post);
        }
        finally
        {
            _nativeResetHooked = false;
            ResetAll();
        }
    }
    public void SetEnabled(bool enabled)
    {
        if (!enabled) ResetAll();
    }

    public string GetHelpEntry() =>
        "/hd 0 关；/hd 1 [强度=1] 净击杀缩放；/hd 2 <名字|t|ct> <倍率>（0<倍率≤20）。" +
        "模式互斥、切换清空、个人优先；>3x边缘可能打不中。/info HeadSize";

    public string GetStatusInfo() => _runtime.Mode switch
    {
        HeadSizeMode.Automatic => FormattableString.Invariant($"头部：自动，强度 {_runtime.Strength}；按当前比赛净击杀计算。"),
        HeadSizeMode.Manual => $"头部：手动，{_runtime.TeamOverrides} 条队伍设置，{_runtime.PlayerOverrides} 条个人设置；个人优先。",
        _ => "头部：已关闭。"
    };
    public string? GetPublicConfigInfo() => _runtime.Mode == HeadSizeMode.Off ? null : GetStatusInfo();
    public string GetFeatureDescription() =>
        "管理员 /hd 0 关闭并恢复 1x；/hd 1 [强度] 按本场计分板击杀−死亡自动缩放。\n" +
        "默认强度 1，0 净击杀为 1x；越负越接近 0x，越正越接近 3x；强度越大变化越快。\n" +
        "/hd 2 <玩家名片段|t|ct> <倍率> 手动，倍率大于 0 且不超过 20；多名匹配时须缩小范围。\n" +
        "已知限制：超过3x时，部分头部边缘可能无法命中。\n" +
        "个人设置优先于队伍；队伍设置跟随成员变化。同一手动模式内连续命令保留其他设置。\n" +
        "自动与手动互斥：切到手动先恢复全员 1x；切到自动清空手动设置。\n" +
        "重生、回合和同地图重连继续生效；换图、重启、插件重载或 /reset_plu 关闭并清空。";

    private void OnCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            Reply(player, info, "你没有权限使用此指令。");
            return;
        }
        var args = Enumerable.Range(1, info.ArgCount - 1).Select(info.GetArg).ToArray();
        if (!HeadSizeRules.TryParse(args, out var command, out var error))
        {
            Reply(player, info, error);
            return;
        }
        if (command.Mode == HeadSizeMode.Off)
        {
            var reset = ResetAll();
            CaorenCupUtils.PrintToChatAll(reset.Success
                ? (_schemaSupported == true
                    ? "头部模块已关闭，个人和队伍设置已清空，玩家头部已恢复 1x。"
                    : "头部模块已关闭，个人和队伍设置已清空；当前没有玩家需要恢复。")
                : "头部模块已关闭并清空，但部分头部恢复失败，请检查服务器日志后重试 /hd 0。");
            return;
        }
        if (!EnsureSchema())
        {
            Reply(player, info, UnsupportedMessage);
            return;
        }
        var players = ReadPlayers(command.Mode == HeadSizeMode.Automatic, out var readError);
        if (readError != null)
        {
            LogFailure(readError);
            Reply(player, info, "读取玩家数据失败，原设置已保留，请检查服务器日志。");
            return;
        }
        var result = _runtime.Execute(command, players);
        if (result.Success)
        {
            try { EnableNativeGate(); }
            catch (Exception ex)
            {
                LogFailure(ex.Message);
                ResetAll();
                CaorenCupUtils.PrintToChatAll("原生大头开关启用失败，模块已关闭并尝试恢复 1x，请检查服务器日志。");
                return;
            }
        }
        else if (_runtime.Mode == HeadSizeMode.Off) ResetAll();
        foreach (var message in result.Messages)
        {
            if (result.Success) CaorenCupUtils.PrintToChatAll(message);
            else Reply(player, info, message);
        }
        if (!result.Success && result.Update.Error != null)
            CaorenCupUtils.PrintToChatAll("头部缩放设置未能完整应用，模块已关闭并尝试恢复 1x，请管理员检查日志。");
        if (result.Update.Error != null) LogFailure(result.Update.Error);
        _nextGateCheck = 0;
    }

    private void OnTick()
    {
        if (_unloaded || _runtime.Mode == HeadSizeMode.Off) return;
        if (Server.EngineTime >= _nextGateCheck)
        {
            _nextGateCheck = Server.EngineTime + 0.25;
            try { EnableNativeGate(); }
            catch (Exception ex)
            {
                LogFailure(ex.Message);
                ResetAll();
                CaorenCupUtils.PrintToChatAll("原生大头开关失效，模块已关闭并尝试恢复 1x，请管理员检查日志。");
                return;
            }
        }
        // 玩家原生重置 Post 钩子覆盖射击前时序；此处继续处理加入、队伍/战绩变化和网络外观。
        // Runtime 仍比较实际值，仅值有变化才写入及发网络通知。
        var players = ReadPlayers(_runtime.Mode == HeadSizeMode.Automatic, out var readError);
        if (readError != null)
        {
            LogFailure(readError);
            ResetAll();
            CaorenCupUtils.PrintToChatAll("读取玩家数据失败，头部模块已关闭并尝试恢复 1x，请管理员检查日志。");
            return;
        }
        var update = _runtime.Refresh(players);
        if (!update.Success)
        {
            LogFailure(update.Error!);
            ResetAll();
            CaorenCupUtils.PrintToChatAll("头部缩放写入失败，模块已关闭并尝试恢复 1x，请管理员检查日志。");
        }
    }

    private HeadSizeUpdate ResetAll()
    {
        _nextGateCheck = 0;
        _runtime.Stop([]);
        string? nativeError = null;
        try
        {
            // 未启用时不探测 GameRules，避免冷启动阶段污染 Schema 的网络字段缓存。
            if (_nativeGate.IsActive) _nativeGate.Restore(ReadMutators());
        }
        catch (Exception ex) { nativeError = ex.Message; }
        var players = ReadPlayers(false, out var error);
        error ??= nativeError;
        // CSS 的 native Schema 缓存会保存 networked 标记。冷启动早期网络序列化表尚未
        // 建好，不能在此时探测 Pawn 类，否则整个进程会一直缓存 networked=false。
        if (!players.Any(p => p.Pawn is { IsValid: true }))
        {
            if (error != null) LogFailure(error);
            return new(Failures: error == null ? 0 : 1, Error: error);
        }
        if (!EnsureSchema()) return new(Failures: 1, Error: UnsupportedMessage);
        var update = _runtime.Stop(players);
        if (error != null) update = update with { Failures = update.Failures + 1, Error = error };
        if (update.Error != null) LogFailure(update.Error);
        return update;
    }

    private bool EnsureSchema()
    {
        if (_schemaSupported == true) return true;
        try
        {
            // 某些旧服务端对未知字段返回 0；必须在解引用前拒绝，而不是写到实体起始地址。
            if (Schema.GetSchemaOffset(PawnClass, ScaleField) <= 0 || !Schema.IsSchemaFieldNetworked(PawnClass, ScaleField))
                throw new InvalidOperationException($"缺少可联网字段 {PawnClass}.{ScaleField}");
            if (Schema.GetSchemaOffset(RulesClass, MutatorsField) <= 0 || !Schema.IsSchemaFieldNetworked(RulesClass, MutatorsField)
                || Schema.GetSchemaOffset("CGameRules", "__m_pChainEntity") <= 0)
                throw new InvalidOperationException($"缺少可联网字段 {RulesClass}.{MutatorsField} 或 GameRules 网络链");
            _schemaSupported = true;
        }
        catch (Exception ex)
        {
            _schemaSupported = false;
            LogFailure($"{UnsupportedMessage} {ex.Message}");
        }
        return _schemaSupported.Value;
    }

    private void EnableNativeGate()
    {
        if (!_nativeResetHooked)
        {
            // 该原生入口尚未由 CSS gamedata 提供；只支持已核对的 Linux 二进制，
            // 游戏更新后先拒绝启用，避免旧签名误匹配或悄悄退回已知失效的接入时机。
            // 不跳过原生处理，只在重置函数完成后应用本模块倍率。
            if (!OperatingSystem.IsLinux()) throw new InvalidOperationException("当前头部原生适配仅支持已核对的 Linux 服务端。");
            using (var binary = File.OpenRead(Addresses.ServerPath))
                if (!Convert.ToHexString(SHA256.HashData(binary)).Equals(
                    "1f3fe3a814eac5909b59b03d8464bc4808adb9ad21f9ba1c6349f56f1664fa6d", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("服务端版本已变化，停止使用未核对的头部原生时序钩子。");
            _playerNativeReset = new MemoryFunctionVoid<CCSPlayerPawnBase>(
                "55 48 89 E5 41 57 41 56 41 55 41 54 53 48 89 FB 48 81 EC 68 01 00 00 E8 ? ? ? ? 48 85 C0 74 13 49 89 C4 48 89 C7 E8 ? ? ? ? 4C 89 E7 E8 ? ? ? ? F3 0F 10 0D ? ? ? ? 0F 2E 8B");
            if (_playerNativeReset.Handle == IntPtr.Zero)
                throw new InvalidOperationException("未找到已核对的玩家头部倍率重置函数。");
            _playerNativeReset.Hook(OnPlayerNativeResetPost, HookMode.Post);
            _nativeResetHooked = true;
        }
        _nativeGate.Enable(ReadMutators()
            ?? throw new InvalidOperationException("当前地图的 GameRules 尚未就绪。"));
    }

    // 引擎的玩家处理会把倍率恢复为 1。必须在该函数返回后、后续射击处理前恢复，
    // 仅在帧尾或 weapon_fire 事件补写无法覆盖原生命中路径。
    private HookResult OnPlayerNativeResetPost(DynamicHook hook)
    {
        if (_unloaded || _runtime.Mode == HeadSizeMode.Off || Environment.CurrentManagedThreadId != _mainThread)
            return HookResult.Continue;
        try
        {
            var pawn = hook.GetParam<CCSPlayerPawnBase>(0);
            if (!pawn.IsValid || pawn.Controller.Value is not { IsValid: true } owner) return HookResult.Continue;
            var controller = owner.As<CCSPlayerController>();
            if (controller.PlayerPawn.Raw != pawn.EntityHandle.Raw) return HookResult.Continue;
            if (ReadPlayer(controller, _runtime.Mode == HeadSizeMode.Automatic) is not { } player) return HookResult.Continue;
            var update = _runtime.Refresh([player]);
            if (!update.Success) { LogFailure(update.Error!); ResetAll(); }
        }
        catch (Exception ex) { LogFailure(ex.Message); ResetAll(); }
        return HookResult.Continue;
    }

    private static IHeadSizeMutators? ReadMutators()
    {
        var proxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
        return proxy is { IsValid: true, GameRules: { } rules } ? new NativeMutators(proxy, rules) : null;
    }

    private sealed class NativeMutators(CCSGameRulesProxy proxy, CCSGameRules rules) : IHeadSizeMutators
    {
        public uint Identity => proxy.EntityHandle.Raw;
        public int Flags => Schema.GetRef<int>(rules.Handle, RulesClass, MutatorsField);
        public void SetFlags(int value)
        {
            Schema.GetRef<int>(rules.Handle, RulesClass, MutatorsField) = value;
            // GameRules 是被 Proxy 持有的嵌套对象，必须沿其自己的网络链通知。
            NativeAPI.SchemaNetworkStateChanged(rules.__m_pChainEntity.Handle,
                (uint)Schema.GetSchemaOffset(RulesClass, MutatorsField), uint.MaxValue, uint.MaxValue);
        }
    }

    private static List<HeadSizePlayer> ReadPlayers(bool readScores, out string? error)
    {
        error = null;
        var result = new List<HeadSizePlayer>();
        try
        {
            foreach (var player in Utilities.GetPlayers())
            {
                try
                {
                    if (ReadPlayer(player, readScores) is { } entry) result.Add(entry);
                }
                catch (Exception ex) { error ??= ex.Message; }
            }
        }
        catch (Exception ex) { error ??= ex.Message; }
        return result;
    }

    private static HeadSizePlayer? ReadPlayer(CCSPlayerController player, bool readScores)
    {
        if (!player.IsValid || player.IsHLTV || player.Connected != PlayerConnectedState.Connected) return null;
        var identity = HeadSizeIdentity.Create(player.SteamID, player.EntityHandle.Raw, player.IsBot);
        var pawn = player.PlayerPawn.Value;
        long? netKills = null;
        if (readScores && player.ActionTrackingServices?.MatchStats is { } stats)
            netKills = (long)stats.Kills - stats.Deaths;
        return new(identity, player.PlayerName, player.TeamNum, netKills,
            pawn is { IsValid: true } ? new HeadPawn(pawn) : null);
    }

    private static void Reply(CCSPlayerController? player, CommandInfo command, string message)
    {
        CaorenCup.Contracts.CaorenCupChat.Reply(player, message);
    }

    private void LogFailure(string message)
    {
        // 写入插件 UTF-8 日志，避免 screen 控制台中文编码影响诊断。
        if (_logger != null) _logger.LogError("{Tag} 头部模块：{Message}", CaorenCupUtils.Tag, message);
        else Console.WriteLine($"{CaorenCupUtils.Tag} 头部模块：{message}");
    }

    private sealed class HeadPawn(CCSPlayerPawn pawn) : IHeadSizePawn
    {
        private readonly uint _id = pawn.EntityHandle.Raw;
        public bool IsValid => pawn.IsValid && pawn.EntityHandle.Raw == _id;
        public float Scale => Schema.GetRef<float>(pawn.Handle, PawnClass, ScaleField);
        public void SetScale(float scale)
        {
            if (!IsValid) return;
            Schema.GetRef<float>(pawn.Handle, PawnClass, ScaleField) = scale;
            Utilities.SetStateChanged(pawn, PawnClass, ScaleField);
        }
    }
}
