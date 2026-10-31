using System.Globalization;
using CaorenCup.FunCommands;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace CaorenCup.Features;

public sealed class FriendsVineFeature : ICaorenFeature
{
    public string FeatureName => "FriendsVine";
    private readonly FriendsVineRuntime _runtime = new();

    public void Init(CaorenCupPlugin plugin)
    {
        plugin.AddCommand("css_friends_vine", "阵营循环缩放：/friends_vine <最小倍数> <最大倍数> <周期秒数>，0 全场关闭", OnCommand);
        plugin.AddCommand("css_fv", "阵营循环缩放：/fv <最小倍数> <最大倍数> <周期秒数>，0 全场关闭", OnCommand);
        plugin.RegisterListener<Listeners.OnTick>(OnTick);
        plugin.RegisterListener<Listeners.OnMapEnd>(_runtime.RestorePawns);
    }

    public void OnConfigParsed(CaorenCupConfig config) { }
    public void OnUnload() => _runtime.StopAll();
    public void SetEnabled(bool enabled)
    {
        if (!enabled) _runtime.StopAll();
    }
    public string GetHelpEntry() => "/friends_vine 或 /fv <最小倍数> <最大倍数> <周期秒数>；/fv 0 全场停止";
    public string GetStatusInfo() => _runtime.IsActive ? "FriendsVine: 已开启" : "FriendsVine: 已关闭";
    public string? GetPublicConfigInfo() => _runtime.IsActive ? "[FriendsVine] 部分阵营正在循环缩放。" : null;
    public string GetFeatureDescription() =>
        "管理员对当前阵营开启整体循环缩放，例如 /fv 0.8 1.2 2。\n" +
        "最小→最大→最小为完整周期，单位秒，倍数相对于开启前体型。\n" +
        "跨回合持续，死亡暂停、复活继续；/fv 0 全场停止并恢复原体型。";

    private void OnCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            Reply(player, info, "你没有权限使用此指令。");
            return;
        }

        if (info.ArgCount == 2 && info.GetArg(1) == "0")
        {
            _runtime.StopAll();
            Reply(player, info, "已停止所有玩家的循环缩放，并恢复原体型。");
            return;
        }

        if (info.ArgCount != 4 || !FriendsVineCycle.TryParse(
                info.GetArg(1), info.GetArg(2), info.GetArg(3), out var cycle))
        {
            Reply(player, info, "用法：/fv <最小倍数> <最大倍数> <周期秒数>；/fv 0 全场停止。");
            Reply(player, info, "倍数和周期必须是有限正数，最大值不能小于最小值。例如：/fv 0.8 1.2 2");
            return;
        }

        if (player == null || !player.IsValid ||
            player.TeamNum is not ((byte)CsTeam.Terrorist) and not ((byte)CsTeam.CounterTerrorist))
        {
            Reply(player, info, "开启效果需要管理员在游戏内加入 T 或 CT；控制台可用 css_fv 0 全场停止。");
            return;
        }

        _runtime.Start(player.TeamNum, cycle!, Server.EngineTime);
        OnTick();
        var team = player.TeamNum == (byte)CsTeam.Terrorist ? "T" : "CT";
        Reply(player, info, FormattableString.Invariant(
            $"{team} 阵营循环缩放：{cycle!.Min} → {cycle.Max} → {cycle.Min}，周期 {cycle.PeriodSeconds} 秒。/fv 0 全场停止。"));
    }

    private static void Reply(CCSPlayerController? player, CommandInfo command, string message)
    {
        // 操作影响整队，回执仍只发给执行者，统一使用绿色私信前缀。
        if (player != null) CaorenCupUtils.PrintToChat(player, message);
        else command.ReplyToCommand($"{CaorenCupUtils.Tag} {message}");
    }

    private void OnTick()
    {
        if (!_runtime.IsActive) return;
        var players = Utilities.GetPlayers()
            .Where(player => player.IsValid && player.PawnIsAlive)
            .Select(player => (Player: player, Pawn: player.PlayerPawn.Value))
            .Where(item => item.Pawn is { IsValid: true } && item.Pawn.CBodyComponent?.SceneNode != null)
            .Select(item => ((IFriendsVinePawn)new ScalePawn(item.Pawn!), item.Player.TeamNum));
        _runtime.Update(players, Server.EngineTime);
    }

    private sealed class ScalePawn(CCSPlayerPawn pawn) : IFriendsVinePawn
    {
        public uint Id { get; } = pawn.EntityHandle.Raw;
        public bool IsValid => pawn.IsValid && pawn.EntityHandle.Raw == Id && pawn.CBodyComponent?.SceneNode != null;
        public float Scale => pawn.CBodyComponent!.SceneNode!.GetSkeletonInstance().Scale;

        public void SetScale(float scale)
        {
            if (!IsValid) return;
            pawn.CBodyComponent!.SceneNode!.GetSkeletonInstance().Scale = scale;
            pawn.AcceptInput("SetScale", null, null, scale.ToString("R", CultureInfo.InvariantCulture));
            Server.NextWorldUpdate(() =>
            {
                if (IsValid) Utilities.SetStateChanged(pawn, "CBaseEntity", "m_CBodyComponent");
            });
        }
    }
}
