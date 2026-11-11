using CaorenCup.Contracts;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;

namespace CaorenCup.Gamemodes.Random;

public sealed partial class CaorenCupRandomPlugin
{
    private void Award(CCSPlayerController? player, RandomPerformanceEvent kind, string eventId)
    {
        if (!Participant(player)) return;
        TrackIdentity(player!);
        var award = _match.AwardPerformance(Id(player!), kind, eventId);
        if (award.Awarded == 0 || player!.IsBot) return;
        CaorenCupChat.PrintToChat(player, $"本次获得{award.Awarded}张强化券，本回合表现奖励剩余{4 - award.RoundPerformanceTotal}张。");
    }

    public GameModeResult SetLock(CCSPlayerController player, string key, long revision) => PlayerAction(player,
        () => _match.SetLock(Id(player), key, revision, BuyWindow(player)));

    public GameModeResult Debug(CCSPlayerController administrator, RandomDebugRequest request)
    {
        if (!OnlineHuman(administrator) || !AdminManager.PlayerHasPermissions(administrator, "@css/root"))
            return GameModeResult.Fail("需要root管理员权限。");
        return ApplyDebug(request);
    }

    private GameModeResult ApplyDebug(RandomDebugRequest request)
    {
        var target = Utilities.GetPlayers().FirstOrDefault(player => Participant(player) && Id(player) == request.TargetKey &&
            player.Connected == PlayerConnectedState.Connected);
        if (target is null) return GameModeResult.Fail("目标已离线或离开参赛队伍，请重新选择。");
        TrackIdentity(target);
        var result = _match.DebugChange(request);
        if (!result.Success) return result;
        if (request.Operation == "reset" && !request.TeamScope) ResetPassiveUsage(target, request.BuffKey);
        ApplyHealthAttributes(target);
        if (request.Operation is "give" or "set") GiveRoundSupplies(target, request.BuffKey);
        var card = RandomCatalog.Find(request.BuffKey);
        var scope = request.TeamScope || card?.Scope == "团队" ? $"{target.Team}全队" : target.PlayerName;
        CaorenCupChat.PrintToChatAll($"\x02随机乱斗测试已应用于 {scope}。{result.Message}\x01");
        return result;
    }

    private void DebugCommand(CCSPlayerController? administrator, CommandInfo command)
    {
        if (administrator is not null && (!OnlineHuman(administrator) || !AdminManager.PlayerHasPermissions(administrator, "@css/root")))
        { CaorenCupChat.Reply(administrator, "需要root管理员权限。"); return; }
        if (command.ArgCount < 2 || command.GetArg(1) == "help")
        {
            CaorenCupChat.Reply(administrator, "/rbdebug list | give/set <玩家> <卡牌> [等级] | remove/reset <玩家> [卡牌] | clear <玩家> [team]");
            CaorenCupChat.Reply(administrator, "玩家可用完整名字、SteamID、bot会话标识或#用户编号，卡牌可用编号、名称或内部键。team明确影响全队。");
            return;
        }
        if (command.GetArg(1) == "list")
        {
            foreach (var player in Utilities.GetPlayers().Where(Participant))
                CaorenCupChat.Reply(administrator, $"#{player.UserId} {player.PlayerName} [{Id(player)}]");
            return;
        }
        if (command.ArgCount < 3) { CaorenCupChat.Reply(administrator, "请指定目标玩家。"); return; }
        var selector = command.GetArg(2);
        var targets = Utilities.GetPlayers().Where(Participant).Where(player => Id(player) == selector ||
            player.PlayerName == selector || selector == $"#{player.UserId}").ToArray();
        if (targets.Length != 1) { CaorenCupChat.Reply(administrator, "目标不存在或名字重复，请用 /rbdebug list 查询标识。"); return; }
        if (command.GetArg(1) == "inspect")
        {
            var target = targets[0];
            var view = _match.AdminSnapshot([Identity(target)]).Players.FirstOrDefault(player => player.SteamId == Id(target));
            var pawn = target.PlayerPawn.Value;
            CaorenCupChat.Reply(administrator, $"{target.PlayerName} · 强化券{view?.Coupons ?? 0} · 生命{pawn?.Health}/{pawn?.MaxHealth} · 护甲{pawn?.ArmorValue}");
            CaorenCupChat.Reply(administrator, string.Join("、", view?.Owned.Select(card => $"{card.Name}{card.Level}级") ?? []));
            return;
        }
        var argument = command.ArgCount > 3 ? command.GetArg(3) : "";
        var card = RandomCatalog.Find(argument);
        if (argument.Length > 0 && argument != "team" && card is null)
        { CaorenCupChat.Reply(administrator, "未找到该卡牌。"); return; }
        var level = 1;
        if (command.ArgCount > 4 && !int.TryParse(command.GetArg(4), out level))
        { CaorenCupChat.Reply(administrator, "等级必须是整数。"); return; }
        var request = new RandomDebugRequest(command.GetArg(1), Id(targets[0]), card?.Key ?? "", level,
            TeamScope: argument == "team");
        CaorenCupChat.Reply(administrator, ApplyDebug(request).Message);
    }

    private void SkillCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null || !Participant(player)) return;
        var view = GetView(player);
        if (view is null) { CaorenCupChat.Reply(player, "随机乱斗未运行。"); return; }
        var card = command.ArgCount > 1 ? RandomCatalog.Find(command.GetArg(1)) : null;
        if (card is null) { CaorenCupChat.Reply(player, "/rbskill <卡牌编号、名称或内部键>"); return; }
        CaorenCupChat.Reply(player, Activate(player, card.Key, view.Revision).Message);
    }
}
