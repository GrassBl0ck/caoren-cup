using CaorenCup.Contracts;

namespace CaorenCup.Gamemodes.Random;

public sealed record BuffDefinition(string Key, string DisplayId, string Name, string Category,
    string SimpleDescription, string DetailedDescription, int MaxLevel, int Cost = 1)
{
    public sealed record Description(string Simple, string Detailed);
    public IReadOnlyList<Description> LevelDescriptions { get; init; } = [];
    public string Quality { get; init; } = "D";
    public string Scope { get; init; } = "个人";
    public bool Active { get; init; }

    public RandomBuffView View(int level)
    {
        var description = level > 0 && level <= LevelDescriptions.Count ? LevelDescriptions[level - 1] : null;
        return new(Key, DisplayId, Name, Quality, Category,
            description?.Simple ?? SimpleDescription, description?.Detailed ?? DetailedDescription, level, MaxLevel, Cost)
            { Scope = Scope, Active = Active };
    }
}

/// <summary>对局状态、原子交易及只读快照。引擎实体由适配层管理。</summary>
public sealed partial class RandomMatch
{
    private sealed class Player
    {
        public string Id = "";
        public int Coupons, Round;
        public long Revision;
        public bool Bought, Rolled, Exchanged;
        public int Team, PerformanceCoupons, ExtraJumpsUsed;
        public string Locked = "";
        public HashSet<string> RewardEvents { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> Growth { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> Levels { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Used { get; } = new(StringComparer.Ordinal);
        public string[] Offers = [];
        public Dictionary<string, long> Events { get; } = new(StringComparer.Ordinal);
    }
    private readonly object _gate = new();
    private readonly Dictionary<string, Player> _players = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RandomPlayerIdentity> _identities = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<BuffDefinition> _cards;
    private readonly Action<string[]> _shuffle;
    private readonly Func<double> _roll;
    private readonly int _roundCoupons;
    private long _revision;
    public int Round { get; private set; }
    public bool Running { get; private set; }
    public bool RoundOpen { get; private set; }
    public RandomMatch(IReadOnlyList<BuffDefinition>? cards = null, Action<string[]>? shuffle = null,
        Func<double>? roll = null, int roundCoupons = 4)
    {
        _cards = cards ?? RandomCatalog.Cards;
        _shuffle = shuffle ?? (items => System.Random.Shared.Shuffle(items));
        _roll = roll ?? System.Random.Shared.NextDouble;
        _roundCoupons = roundCoupons;
    }
    public void Start() { lock (_gate) { ClearMatch(); Round = 0; RoundOpen = false; Running = true; _revision++; } }
    public void Stop() { lock (_gate) { Running = RoundOpen = false; ClearMatch(); Round = 0; _revision++; } }
    public void ObservePlayer(RandomPlayerIdentity identity)
    {
        lock (_gate)
        {
            if (!Running || string.IsNullOrWhiteSpace(identity.SteamId)) return;
            _identities[identity.SteamId] = identity;
            if (RoundOpen && _players.TryGetValue(identity.SteamId, out var player))
            {
                var team = identity.Team is 2 or 3 ? LogicalTeam(identity.Team) : player.Team;
                var changed = player.Team != team;
                player.Team = team;
                if (player.Round != Round) PrepareRound(player);
                else if (changed)
                {
                    ReconcileOffers(player);
                    player.Revision = ++_revision;
                }
            }
        }
    }
    public RandomAdminView AdminSnapshot(IEnumerable<RandomPlayerIdentity> connectedHumans)
    {
        lock (_gate)
        {
            // Only local projection collections are allocated. In particular, do not
            // use Get, View, PrepareRound or Draw here, even for a newly joined player.
            var online = connectedHumans.Where(identity => !string.IsNullOrWhiteSpace(identity.SteamId))
                .GroupBy(identity => identity.SteamId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
            var rows = online.Keys.Concat(_players.Keys).Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal).Select(id =>
                {
                    var connected = online.TryGetValue(id, out var identity);
                    identity ??= _identities.GetValueOrDefault(id) ?? new RandomPlayerIdentity(id, id, 0);
                    _players.TryGetValue(id, out var player);
                    var currentRound = player is not null && player.Round == Round;
                    var owned = player is null ? Array.Empty<RandomBuffView>() : Owned(player);
                    return new RandomAdminPlayerView(id, identity.Name, identity.Team, connected,
                        Running && player is not null && identity.Team is 2 or 3, player?.Coupons ?? 0,
                        currentRound && player!.Bought, currentRound && player!.Rolled, currentRound && player!.Exchanged,
                        Array.AsReadOnly(owned)) { IsBot = identity.IsBot, Revision = player?.Revision ?? 0,
                            PerformanceCoupons = currentRound ? player!.PerformanceCoupons : 0 };
                }).ToArray();
            return new(Round, Running, Array.AsReadOnly(rows))
                { Catalog = _cards.Select(card => card.View(1)).ToArray() };
        }
    }
    public bool BeginRound(int round, IEnumerable<string> present)
    {
        lock (_gate)
        {
            if (!Running || round <= Round || round > 24) return false;
            Round = round; RoundOpen = true;
            foreach (var token in _pending.Keys.ToArray()) Release(token);
            _uses.Clear();
            _pending.Clear();
            foreach (var id in present.Distinct(StringComparer.Ordinal))
            {
                if (_players.TryGetValue(id, out var current) && _identities.TryGetValue(id, out var identity) && identity.Team is 2 or 3)
                    current.Team = LogicalTeam(identity.Team);
                var p = Get(id); PrepareRound(p); p.Coupons += _roundCoupons; p.Revision = ++_revision;
            }
            return true;
        }
    }
    public bool EndRound(int round)
    {
        lock (_gate)
        {
            if (!Running || !RoundOpen || round != Round) return false;
            RoundOpen = false;
            foreach (var token in _pending.Keys.ToArray()) Release(token);
            foreach (var p in _players.Values) p.Revision = ++_revision;
            return true;
        }
    }
    private Player Get(string id)
    {
        if (!_players.TryGetValue(id, out var p))
        {
            p = new Player { Id = id, Revision = ++_revision };
            if (_identities.TryGetValue(id, out var identity) && identity.Team is 2 or 3)
                p.Team = LogicalTeam(identity.Team);
            _players[id] = p;
        }
        PrepareRound(p); return p;
    }
    private void PrepareRound(Player p)
    {
        if (p.Round == Round) return;
        p.Round = Round; p.Bought = p.Rolled = p.Exchanged = false; p.Used.Clear();
        p.PerformanceCoupons = 0;
        p.ExtraJumpsUsed = 0;
        foreach (var card in _cards.Where(card => Level(p, card) > 0)) Use(UsageOwner(p.Id, card), card.Key);
        p.RewardEvents.Clear();
        if (!Eligible(p).Any(card => card.Key == p.Locked)) p.Locked = "";
        p.Offers = NewOffers(p); p.Revision = ++_revision;
    }
    private IEnumerable<BuffDefinition> Eligible(Player p) => _cards.Where(c => Level(p, c) < c.MaxLevel);
    private string[] Draw(IEnumerable<BuffDefinition> candidates, int count)
    {
        var remaining = candidates.ToList();
        var result = new List<string>();
        var weights = QualityWeights(Round);
        while (remaining.Count > 0 && result.Count < count)
        {
            var groups = remaining.GroupBy(card => card.Quality)
                .Select(group => (Quality: group.Key, Cards: group.ToArray(), Weight: weights.GetValueOrDefault(group.Key)))
                .Where(group => group.Weight > 0).ToArray();
            var total = groups.Sum(group => group.Weight);
            if (total == 0) break;
            var roll = Math.Clamp(_roll(), 0, Math.BitDecrement(1d)) * total;
            var chosen = groups[^1];
            foreach (var group in groups)
            {
                roll -= group.Weight;
                if (roll < 0) { chosen = group; break; }
            }
            var keys = chosen.Cards.Select(card => card.Key).ToArray();
            _shuffle(keys);
            result.Add(keys[0]);
            remaining.RemoveAll(card => card.Key == keys[0]);
        }
        return result.ToArray();
    }
    private bool CanTrade(Player p, bool window) => Running && RoundOpen && Round is >= 1 and <= 24 && window;
    private bool CanRoll(Player p, bool window) => CanTrade(p, window) && !p.Bought && !p.Rolled && p.Coupons >= 1 &&
        p.Offers.Any(key => key != p.Locked) &&
        Eligible(p).Count(c => !p.Offers.Contains(c.Key)) >= p.Offers.Count(key => key != p.Locked);
    public RandomPlayerView? View(string id, bool window)
    {
        lock (_gate)
        {
            if (!Running) return null;
            var p = Get(id); var trade = CanTrade(p, window);
            var status = Round == 0 ? "等待首回合" : !RoundOpen ? (Round >= 24 ? "对局已结束" : "回合已结束") :
                !window ? "购买时间已结束" : p.Bought ? "本回合已购买" : p.Offers.Length == 0 ? "全部BUFF已拥有且满级" : "本回合可选择一项强化";
            return new(p.Revision, Round, p.Coupons, trade && !p.Bought && p.Offers.Any(k => p.Coupons >= Card(k).Cost),
                CanRoll(p, window), trade && !p.Exchanged, status,
                p.Offers.Select(k => Card(k).View(Level(p, Card(k)) + 1)).ToArray(), Owned(p))
                { LockedKey = p.Locked, PerformanceCoupons = p.PerformanceCoupons, CanLock = trade && !p.Bought };
        }
    }
    private BuffDefinition Card(string key) => _cards.Single(c => c.Key == key);
    private static GameModeResult Stale() => GameModeResult.Fail("页面已更新，请重新选择。");
    public GameModeResult Purchase(string id, string key, long revision, bool window)
    {
        lock (_gate)
        {
            if (!Running) return GameModeResult.Fail("随机乱斗未运行。");
            var p = Get(id); if (revision != p.Revision) return Stale();
            if (!CanTrade(p, window) || p.Bought) return GameModeResult.Fail("当前不能购买强化。");
            if (!p.Offers.Contains(key)) return GameModeResult.Fail("这张BUFF不在当前候选中。");
            var c = Card(key); var level = Level(p, c);
            if (level >= c.MaxLevel || p.Coupons < c.Cost) return GameModeResult.Fail("强化券不足或BUFF已满级。");
            p.Coupons -= c.Cost;
            SetLevel(p, c, level + 1);
            if (p.Locked == key) p.Locked = "";
            p.Bought = true; p.Offers = []; p.Revision = ++_revision;
            RefreshTeamOffers(p, c);
            return GameModeResult.Ok($"已获得 {c.Name} · {level + 1}级。");
        }
    }
    public GameModeResult Reroll(string id, long revision, bool window)
    {
        lock (_gate)
        {
            if (!Running) return GameModeResult.Fail("随机乱斗未运行。");
            var p = Get(id); if (revision != p.Revision) return Stale();
            if (!CanRoll(p, window)) return GameModeResult.Fail("当前无法重抽：需要足够的新候选、强化券和本回合次数。");
            var freeCount = p.Offers.Count(key => key != p.Locked);
            var fresh = Draw(Eligible(p).Where(c => !p.Offers.Contains(c.Key)), freeCount);
            p.Offers = p.Locked.Length == 0 ? fresh : new[] { p.Locked }.Concat(fresh).ToArray();
            p.Coupons--; p.Rolled = true; p.Revision = ++_revision;
            return GameModeResult.Ok("已重抽，本回合不能再次重抽或返回原选项。");
        }
    }
    public GameModeResult Exchange(string id, long revision, bool window, Func<int, bool> debit)
    {
        lock (_gate)
        {
            if (!Running) return GameModeResult.Fail("随机乱斗未运行。");
            var p = Get(id); if (revision != p.Revision) return Stale();
            if (!CanTrade(p, window) || p.Exchanged) return GameModeResult.Fail("当前不能兑换，或本回合额度已使用。");
            if (!debit(1000)) return GameModeResult.Fail("金钱不足，未兑换。");
            p.Coupons++; p.Exchanged = true; p.Revision = ++_revision;
            return GameModeResult.Ok("已花费1000元兑换1张强化券，本回合额度已用完。");
        }
    }
    private int Consume(string id, string key)
    {
        if (!Running || !RoundOpen || Round < 1) return 0;
        var level = Level(id, key);
        return level > 0 && TryUse(id, key, 1, 0, 0) ? level : 0;
    }
    public int ReloadArmor(string id, int armor, bool effectiveNativeReload)
    {
        lock (_gate) { var l = effectiveNativeReload ? Consume(id, RandomCatalog.Reload) : 0;
            return l == 0 ? armor : Math.Max(armor, Math.Min(100, armor + 10 + 5 * l)); }
    }
    public int HealAfterDamage(string id, int health, int maxHealth, bool nativeEnemyDamage)
    {
        lock (_gate) { var l = nativeEnemyDamage && maxHealth > 0 && health is > 0 and < 50 ? Consume(id, RandomCatalog.Medkit) : 0;
            return l == 0 ? health : Math.Max(health, Math.Min(maxHealth, health + 10 + 5 * l)); }
    }
    public bool GhostOnDeath(string id, bool nativeEnemyKill)
    { lock (_gate) return nativeEnemyKill && Consume(id, RandomCatalog.Ghost) > 0; }
    public void Record(string id, string kind, int amount = 1)
    { lock (_gate) { if (!Running || !RoundOpen) return; var p = Get(id); p.Events[kind] = p.Events.GetValueOrDefault(kind) + Math.Max(0, amount); } }
}

/// <summary>换弹开始事件与同一武器的真实弹药转移同时成立；中断/切枪废弃观察。</summary>
public sealed class ReloadObservation(int clip, int reserve)
{
    private int _clip = clip, _reserve = reserve;
    private bool _seenReload;
    public bool Finished { get; private set; }
    public bool Observe(int clip, int reserve, bool reloading)
    {
        var moved = clip - _clip;
        var effective = !Finished && (_seenReload || reloading) && moved > 0 && _reserve - reserve == moved;
        Finished |= _seenReload && !reloading;
        _seenReload |= reloading;
        _clip = clip; _reserve = reserve;
        return effective;
    }
}
