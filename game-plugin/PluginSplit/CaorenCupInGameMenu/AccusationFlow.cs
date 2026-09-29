namespace CaorenCup.Features.InGameMenu;

public enum AccusationSide { Own, Enemy }
public enum AccusationStatus { Accepted, WrongSession, WrongPhase, Disabled, InvalidSide, InvalidActor, InvalidTarget, Self, WrongTeam, AlreadyLocked }
public sealed record AccusationPlayer(string PlayerId, string Name, string Team, string Role, bool Online = true);
public sealed record AccusationChoice(string? Own, string? Enemy);

/// <summary>移植网页 ACCUSE 的纯逻辑。使用网页 playerId/rosterTeam，不假设其等于 SteamID 或 T/CT。</summary>
public sealed class AccusationFlow
{
    private readonly Dictionary<string, AccusationPlayer> _players;
    private readonly Dictionary<string, AccusationChoice> _choices = new();
    public string SessionKey { get; }
    public bool Enabled { get; }
    public bool InPostGamePhase { get; }

    public AccusationFlow(string sessionKey, IEnumerable<AccusationPlayer> players, bool enabled, bool inPostGamePhase,
        IReadOnlyDictionary<string, AccusationChoice>? authoritativeChoices = null)
    {
        SessionKey = sessionKey; Enabled = enabled; InPostGamePhase = inPostGamePhase;
        _players = players.ToDictionary(p => p.PlayerId);
        // 重连/刷新时恢复网页服务端已校验的选择，不能用本地空状态覆盖已锁定提交。
        if (authoritativeChoices != null)
            foreach (var pair in authoritativeChoices) _choices[pair.Key] = pair.Value;
    }
    private static bool Eligible(AccusationPlayer player) => player.Role is not ("Admin" or "Spectator") && !string.IsNullOrWhiteSpace(player.Team);
    public IReadOnlyList<AccusationPlayer> Candidates(string actorId, AccusationSide side)
    {
        if (!Enum.IsDefined(side) || !_players.TryGetValue(actorId, out var actor) || !Eligible(actor)) return Array.Empty<AccusationPlayer>();
        return _players.Values.Where(p => p.PlayerId != actorId && Eligible(p) && (side == AccusationSide.Own ? p.Team == actor.Team : p.Team != actor.Team)).ToArray();
    }
    public AccusationChoice Choice(string actorId) => _choices.GetValueOrDefault(actorId, new AccusationChoice(null, null));
    public AccusationStatus Submit(string sessionKey, string actorId, string targetId, AccusationSide side)
    {
        if (sessionKey != SessionKey) return AccusationStatus.WrongSession;
        if (!Enabled) return AccusationStatus.Disabled;
        if (!InPostGamePhase) return AccusationStatus.WrongPhase;
        if (!Enum.IsDefined(side)) return AccusationStatus.InvalidSide;
        if (!_players.TryGetValue(actorId, out var actor) || !Eligible(actor)) return AccusationStatus.InvalidActor;
        if (!_players.TryGetValue(targetId, out var target) || !Eligible(target)) return AccusationStatus.InvalidTarget;
        if (actorId == targetId) return AccusationStatus.Self;
        if ((side == AccusationSide.Own) != (actor.Team == target.Team)) return AccusationStatus.WrongTeam;
        var old = Choice(actorId);
        if ((side == AccusationSide.Own ? old.Own : old.Enemy) != null) return AccusationStatus.AlreadyLocked;
        _choices[actorId] = side == AccusationSide.Own ? old with { Own = targetId } : old with { Enemy = targetId };
        return AccusationStatus.Accepted;
    }
    public bool OnlinePlayersCompleted => _players.Values.Where(p => Eligible(p) && p.Online).All(p => Choice(p.PlayerId) is { Own: not null, Enemy: not null });
    public IReadOnlyList<(string Name, bool OwnSubmitted, bool EnemySubmitted)> Progress() => _players.Values.Where(Eligible)
        .Select(p => (p.Name, Choice(p.PlayerId).Own != null, Choice(p.PlayerId).Enemy != null)).ToArray();
}

/// <summary>完整链条后续实现：由服务端验证 SteamID→网页 playerId 绑定和比赛阶段。</summary>
public interface IAccusationTransport
{
    Task<AccusationMenuContext?> LoadForPlayerAsync(string steamId, CancellationToken token = default);
    Task<AccusationStatus> SubmitAsync(string steamId, string sessionKey, string targetPlayerId, AccusationSide side, CancellationToken token = default);
}

public sealed record AccusationMenuContext(string ActorPlayerId, AccusationFlow Flow);
