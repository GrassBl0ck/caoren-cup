using CaorenCup.Contracts;

namespace CaorenCup.Gamemodes.Random;

public enum RandomPerformanceEvent { Kill, Death, Assist, Plant, Defuse }
public readonly record struct CouponAward(int Awarded, int RoundPerformanceTotal);

public sealed partial class RandomMatch
{
    private readonly Dictionary<int, Dictionary<string, int>> _teamLevels = new();

    private void ClearMatch()
    {
        _players.Clear();
        _identities.Clear();
        _teamLevels.Clear();
        _uses.Clear();
        _pending.Clear();
    }

    private int LogicalTeam(int side) => Round <= 12 ? side : 5 - side;

    private int Level(Player player, BuffDefinition card)
    {
        if (card.Scope != "团队") return player.Levels.GetValueOrDefault(card.Key);
        return _teamLevels.TryGetValue(player.Team, out var levels) ? levels.GetValueOrDefault(card.Key) : 0;
    }

    public int Level(string id, string key)
    {
        lock (_gate)
        {
            var card = _cards.FirstOrDefault(item => item.Key == key);
            return card is not null && _players.TryGetValue(id, out var player) ? Level(player, card) : 0;
        }
    }

    private void SetLevel(Player player, BuffDefinition card, int level)
    {
        var levels = player.Levels;
        if (card.Scope == "团队")
        {
            if (!_teamLevels.TryGetValue(player.Team, out levels))
                _teamLevels[player.Team] = levels = new(StringComparer.Ordinal);
        }
        if (level == 0) levels.Remove(card.Key);
        else
        {
            levels[card.Key] = level;
            Use(UsageOwner(player.Id, card), card.Key);
        }
    }

    private RandomBuffView[] Owned(Player player) => _cards.Where(card => Level(player, card) > 0)
        .Select(card => card.View(Level(player, card))).ToArray();

    public IReadOnlyList<RandomBuffView> Owned(string id)
    {
        lock (_gate) return _players.TryGetValue(id, out var player) ? Owned(player) : [];
    }

    public static IReadOnlyDictionary<string, int> QualityWeights(int round)
    {
        int[] weights = round <= 8 ? [35, 40, 20, 4, 1]
            : round <= 16 ? [15, 30, 35, 15, 5] : [5, 15, 40, 25, 15];
        return new Dictionary<string, int> { ["D"] = weights[0], ["C"] = weights[1], ["B"] = weights[2],
            ["A"] = weights[3], ["S"] = weights[4] };
    }

    private string[] NewOffers(Player player)
    {
        var fresh = Draw(Eligible(player).Where(card => card.Key != player.Locked), player.Locked.Length == 0 ? 3 : 2);
        return player.Locked.Length == 0 ? fresh : new[] { player.Locked }.Concat(fresh).ToArray();
    }

    private void ReconcileOffers(Player player)
    {
        var eligible = Eligible(player).Select(card => card.Key).ToHashSet(StringComparer.Ordinal);
        if (!eligible.Contains(player.Locked)) player.Locked = "";
        if (player.Bought || !RoundOpen) return;
        var valid = player.Offers.Where(eligible.Contains).Distinct(StringComparer.Ordinal).ToArray();
        var needed = Math.Max(0, Math.Min(3, eligible.Count) - valid.Length);
        player.Offers = valid.Concat(Draw(Eligible(player).Where(card => !valid.Contains(card.Key)), needed)).ToArray();
    }

    private void RefreshTeamOffers(Player source, BuffDefinition card)
    {
        if (card.Scope != "团队") return;
        foreach (var player in _players.Values.Where(player => player.Team == source.Team))
        {
            ReconcileOffers(player);
            player.Revision = ++_revision;
        }
    }

    public GameModeResult SetLock(string id, string key, long revision, bool window)
    {
        lock (_gate)
        {
            if (!Running) return GameModeResult.Fail("随机乱斗未运行。");
            var player = Get(id);
            if (revision != player.Revision) return Stale();
            if (key.Length > 0 && !CanTrade(player, window)) return GameModeResult.Fail("当前不能锁定新卡。");
            if (key.Length > 0 && (player.Bought || !player.Offers.Contains(key)))
                return GameModeResult.Fail("只能锁定当前候选卡。");
            player.Locked = key;
            player.Revision = ++_revision;
            return GameModeResult.Ok(key.Length == 0 ? "已解除锁卡。" : $"已锁定 {Card(key).Name}，后续回合继续保留。");
        }
    }

    public CouponAward AwardPerformance(string id, RandomPerformanceEvent kind, string eventId)
    {
        lock (_gate)
        {
            if (!Running || !RoundOpen || Round < 1 || string.IsNullOrWhiteSpace(eventId)) return default;
            var player = Get(id);
            if (!player.RewardEvents.Add($"{kind}:{eventId}")) return new(0, player.PerformanceCoupons);
            var amount = kind == RandomPerformanceEvent.Kill ? 2 : 1;
            amount = Math.Min(amount, Math.Max(0, 4 - player.PerformanceCoupons));
            player.Coupons += amount;
            player.PerformanceCoupons += amount;
            if (amount > 0) player.Revision = ++_revision;
            return new(amount, player.PerformanceCoupons);
        }
    }

    public int Growth(string id, string key)
    {
        lock (_gate) return _players.TryGetValue(id, out var player) ? player.Growth.GetValueOrDefault(key) : 0;
    }

    public bool AddGrowth(string id, string key)
    {
        lock (_gate)
        {
            if (key is not (RandomCatalog.Tank or RandomCatalog.Light) || Level(id, key) == 0) return false;
            var player = Get(id);
            var current = player.Growth.GetValueOrDefault(key);
            if (current >= 5) return false;
            player.Growth[key] = current + 1;
            player.Revision = ++_revision;
            return true;
        }
    }

    public GameModeResult DebugChange(RandomDebugRequest request)
    {
        lock (_gate)
        {
            if (!Running || !RoundOpen) return GameModeResult.Fail("请先启动随机乱斗对局。");
            if (string.IsNullOrWhiteSpace(request.TargetKey) || !_identities.ContainsKey(request.TargetKey))
                return GameModeResult.Fail("目标已失效，请重新选择玩家。");
            var player = Get(request.TargetKey);
            if (request.TeamScope && player.Team is not (2 or 3)) return GameModeResult.Fail("目标需要加入参赛队伍。");
            if (request.ExpectedRevision != 0 && request.ExpectedRevision != player.Revision) return Stale();
            var operation = request.Operation.ToLowerInvariant();
            var card = _cards.FirstOrDefault(item => item.Key == request.BuffKey);
            if (operation is "clear" or "reset" && request.BuffKey.Length == 0)
            {
                var selected = _cards.Where(item => (item.Scope == "团队") == request.TeamScope).ToArray();
                foreach (var selectedCard in selected)
                {
                    CancelCardTasks(request.TargetKey, selectedCard.Key);
                    if (operation == "clear") SetLevel(player, selectedCard, 0);
                    else ResetUsage(request.TargetKey, selectedCard.Key);
                }
                if (operation == "clear" && !request.TeamScope) player.Growth.Clear();
                ReconcileOffers(player);
                foreach (var selectedCard in selected) RefreshTeamOffers(player, selectedCard);
                player.Revision = ++_revision;
                return GameModeResult.Ok(operation == "clear" ? "已清空指定范围的BUFF。" : "已重置指定范围的本回合次数与冷却。");
            }
            if (card is null) return GameModeResult.Fail("未找到该BUFF。");
            if (request.TeamScope && card.Scope != "团队") return GameModeResult.Fail("这张卡只作用于目标个人。");
            if (card.Scope == "团队" && player.Team is not (2 or 3)) return GameModeResult.Fail("目标需要加入参赛队伍。");
            if (operation == "reset")
            {
                ResetUsage(request.TargetKey, card.Key);
                player.Revision = ++_revision;
                return GameModeResult.Ok($"已重置 {card.Name} 的本回合次数与冷却。");
            }
            if (operation is not ("give" or "set" or "remove")) return GameModeResult.Fail("不支持的测试操作。");
            var desired = operation == "remove" ? 0 : request.Level;
            if (desired < 0 || desired > card.MaxLevel || (desired == 0 && operation != "remove"))
                return GameModeResult.Fail($"等级范围为1至{card.MaxLevel}。");
            var previous = Level(player, card);
            if (previous == desired || operation == "give" && previous > desired)
                return GameModeResult.Fail("目标已拥有相同或更高的指定等级。");
            CancelCardTasks(request.TargetKey, card.Key);
            SetLevel(player, card, desired);
            if (desired == 0) player.Growth.Remove(card.Key);
            ReconcileOffers(player);
            RefreshTeamOffers(player, card);
            player.Revision = ++_revision;
            return GameModeResult.Ok(desired == 0 ? $"已移除 {card.Name}。" : $"已设置 {card.Name} 为{desired}级。");
        }
    }

    public int ExtraJumpsUsed(string id)
    {
        lock (_gate) return _players.TryGetValue(id, out var player) ? player.ExtraJumpsUsed : 0;
    }

    public void SetExtraJumpsUsed(string id, int used)
    {
        lock (_gate)
        {
            if (Running && RoundOpen && _players.TryGetValue(id, out var player))
                player.ExtraJumpsUsed = Math.Max(0, used);
        }
    }
}
