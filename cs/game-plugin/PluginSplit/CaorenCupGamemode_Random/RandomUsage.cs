namespace CaorenCup.Gamemodes.Random;

public readonly record struct BuffReservation(long Id, int Round);
public readonly record struct BuffUsage(int Used, int Pending, double NextAllowed, long Generation);

public sealed partial class RandomMatch
{
    private sealed class UseState
    {
        public int Used, Pending;
        public double NextAllowed;
        public long Generation;
    }
    private sealed record PendingUse(string Owner, string Key, UseState State, UseState? Shared,
        int Amount, double Cooldown, int Round);
    private readonly Dictionary<(string Owner, string Key), UseState> _uses = new();
    private readonly Dictionary<long, PendingUse> _pending = new();
    private long _nextReservation, _nextGeneration;

    private string UsageOwner(string id, BuffDefinition card)
    {
        var player = Get(id);
        return card.Scope == "团队" ? $"team:{player.Team}" : $"player:{id}";
    }

    private UseState Use(string owner, string key)
    {
        if (!_uses.TryGetValue((owner, key), out var state))
            _uses[(owner, key)] = state = new UseState { Generation = ++_nextGeneration };
        return state;
    }

    public BuffUsage Usage(string id, string key, string channel = "")
    {
        lock (_gate)
        {
            var card = _cards.FirstOrDefault(item => item.Key == key);
            if (card is null || !_players.ContainsKey(id)) return default;
            if (!_uses.TryGetValue((UsageOwner(id, card), CounterKey(key, channel)), out var state)) return default;
            return new(state.Used, state.Pending, state.NextAllowed, state.Generation);
        }
    }

    public bool TryReserve(string id, string key, int limit, double cooldown, double now,
        out BuffReservation reservation, int amount = 1, string channel = "")
    {
        lock (_gate)
        {
            reservation = default;
            if (!Running || !RoundOpen || Round < 1 || amount <= 0 || Level(id, key) == 0) return false;
            var card = Card(key);
            var owner = UsageOwner(id, card);
            var state = Use(owner, CounterKey(key, channel));
            if (state.Used + state.Pending + amount > limit || now < state.NextAllowed || cooldown > 0 && state.Pending > 0) return false;
            UseState? shared = null;
            if (key is "flash_partner" or "flash_chain")
            {
                shared = Use(owner, "shared_flash");
                if (shared.Used + shared.Pending + amount > 3) return false;
            }
            state.Pending += amount;
            if (shared is not null) shared.Pending += amount;
            var token = ++_nextReservation;
            _pending[token] = new(owner, key, state, shared, amount, cooldown, Round);
            InvalidateUsageOwner(owner);
            reservation = new(token, Round);
            return true;
        }
    }

    public bool IsPending(BuffReservation reservation)
    {
        lock (_gate) return Running && RoundOpen && reservation.Round == Round && _pending.ContainsKey(reservation.Id);
    }

    public bool Complete(BuffReservation reservation, Func<bool> effect, double now)
    {
        lock (_gate)
        {
            if (!IsPending(reservation)) return false;
            var pending = _pending[reservation.Id];
            try
            {
                if (now < pending.State.NextAllowed) return false;
                if (!effect()) return false;
                pending.State.Used += pending.Amount;
                pending.State.NextAllowed = now + pending.Cooldown;
                if (pending.Shared is not null) pending.Shared.Used += pending.Amount;
                return true;
            }
            finally
            {
                Release(reservation.Id);
            }
        }
    }

    private void Release(long token)
    {
        if (!_pending.Remove(token, out var pending)) return;
        pending.State.Pending -= pending.Amount;
        if (pending.Shared is not null) pending.Shared.Pending -= pending.Amount;
        InvalidateUsageOwner(pending.Owner);
    }

    public void Cancel(BuffReservation reservation)
    {
        lock (_gate) Release(reservation.Id);
    }

    public bool TryUse(string id, string key, int limit, double cooldown, double now, int amount = 1)
    {
        lock (_gate)
        {
            return TryReserve(id, key, limit, cooldown, now, out var reservation, amount)
                && Complete(reservation, () => true, now);
        }
    }

    public void CancelCardTasks(string id, string key)
    {
        lock (_gate)
        {
            var card = _cards.FirstOrDefault(item => item.Key == key);
            if (card is null || !_players.ContainsKey(id)) return;
            var owner = UsageOwner(id, card);
            foreach (var token in _pending.Where(item => item.Value.Owner == owner && item.Value.Key == key).Select(item => item.Key).ToArray())
                Release(token);
            Use(owner, key);
            foreach (var state in _uses.Where(item => item.Key.Owner == owner &&
                (item.Key.Key == key || item.Key.Key.StartsWith(key + "\u001f", StringComparison.Ordinal))).Select(item => item.Value))
                state.Generation = ++_nextGeneration;
        }
    }

    public void ResetUsage(string id, string key)
    {
        lock (_gate)
        {
            var card = _cards.FirstOrDefault(item => item.Key == key);
            if (card is null || !_players.ContainsKey(id)) return;
            CancelCardTasks(id, key);
            if (key == "extra_jump") Get(id).ExtraJumpsUsed = 0;
            var owner = UsageOwner(id, card);
            var states = _uses.Where(item => item.Key.Owner == owner &&
                (item.Key.Key == key || item.Key.Key.StartsWith(key + "\u001f", StringComparison.Ordinal)))
                .Select(item => item.Value).ToArray();
            if (key is "flash_partner" or "flash_chain")
            {
                var shared = Use(owner, "shared_flash");
                shared.Used = Math.Max(0, shared.Used - states.Sum(state => state.Used));
            }
            foreach (var state in states)
            {
                state.Used = 0;
                state.NextAllowed = 0;
                state.Generation = ++_nextGeneration;
            }
        }
    }

    private static string CounterKey(string key, string channel) => channel.Length == 0 ? key : key + "\u001f" + channel;

    private void InvalidateUsageOwner(string owner)
    {
        foreach (var player in _players.Values)
            if (owner == "player:" + player.Id || owner == $"team:{player.Team}") player.Revision = ++_revision;
    }
}
