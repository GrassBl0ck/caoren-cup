namespace CaorenCup.WeaponPaints.Core;

public enum ChatInputKind
{
    Search,
    Wear,
    Seed,
    NameTag,
    StickerSearch,
    KeychainSearch
}

public sealed record ChatInputRequest(
    ChatInputKind Kind,
    TeamSide Team,
    ushort WeaponDefIndex = 0,
    byte StickerSlot = 0,
    CatalogCategory? Category = null);

public sealed class ChatInputSessionStore
{
    private readonly object _sync = new();
    private readonly Dictionary<ulong, Entry> _entries = new();
    private readonly TimeSpan _timeout;

    public ChatInputSessionStore(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        _timeout = timeout;
    }

    public void Begin(ulong playerId, ChatInputRequest request, DateTimeOffset now)
    {
        lock (_sync)
        {
            _entries[playerId] = new Entry(request, now + _timeout);
        }
    }

    public bool TryConsume(ulong playerId, DateTimeOffset now, out ChatInputRequest request)
    {
        lock (_sync)
        {
            if (!_entries.Remove(playerId, out var entry) || entry.ExpiresAt < now)
            {
                request = null!;
                return false;
            }

            request = entry.Request;
            return true;
        }
    }

    public bool Cancel(ulong playerId)
    {
        lock (_sync)
        {
            return _entries.Remove(playerId);
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _entries.Clear();
        }
    }

    private sealed record Entry(ChatInputRequest Request, DateTimeOffset ExpiresAt);
}
