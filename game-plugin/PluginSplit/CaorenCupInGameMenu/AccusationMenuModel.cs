namespace CaorenCup.Features.InGameMenu;

public sealed record AccusationCandidate(string PlayerId, string Name);
public sealed record AccusationProgress(string Name, bool OwnSubmitted, bool EnemySubmitted);
public sealed record AccusationMenuPage(string SessionKey, AccusationSide Side, int Page, int PageCount,
    IReadOnlyList<AccusationCandidate> Candidates, bool Submitted, string? OwnTargetName, string? EnemyTargetName,
    IReadOnlyList<AccusationProgress> Progress, bool AllOnlinePlayersCompleted);

/// <summary>
/// 给菜单的纯数据视图：己方/敌方候选、翻页、自己的已锁定选择和匿名进度。
/// 不处理网页鉴权、不自动切比赛阶段、不计算得分。完整链路接通后消费这些视图即可。
/// </summary>
public static class AccusationMenuModel
{
    public const int PageSize = 8;
    public static AccusationMenuPage Build(AccusationFlow flow, string actorId, AccusationSide side, int page)
    {
        if (!Enum.IsDefined(side)) throw new ArgumentOutOfRangeException(nameof(side));
        var candidates = flow.Candidates(actorId, side);
        var count = Math.Max(1, (candidates.Count + PageSize - 1) / PageSize);
        var index = Math.Clamp(page, 0, count - 1);
        var choice = flow.Choice(actorId);
        // 只公开操作者自己的选择；全员进度不包含选择目标。
        string? Target(string? id, AccusationSide targetSide) => id == null ? null :
            flow.Candidates(actorId, targetSide).FirstOrDefault(p => p.PlayerId == id)?.Name;
        return new(flow.SessionKey, side, index, count,
            candidates.Skip(index * PageSize).Take(PageSize).Select(p => new AccusationCandidate(p.PlayerId, p.Name)).ToArray(),
            (side == AccusationSide.Own ? choice.Own : choice.Enemy) != null,
            Target(choice.Own, AccusationSide.Own), Target(choice.Enemy, AccusationSide.Enemy),
            flow.Progress().Select(p => new AccusationProgress(p.Name, p.OwnSubmitted, p.EnemySubmitted)).ToArray(),
            flow.OnlinePlayersCompleted);
    }
}
