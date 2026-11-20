namespace CaorenCup.Contracts;
/// <summary>Server-local authority lease: mode lifecycle and bridge match control are mutually exclusive.</summary>
public static class CaorenCupMatchAuthority
{
    private sealed record Ownership(object Owner, bool IsCore);

    private static Ownership? _ownership;
    private static bool _bridgeCommands;
    public static bool BridgeCompatibilityCommandsEnabled
    {
        get => _bridgeCommands && !AppDomain.CurrentDomain.GetAssemblies().Any(a =>
            a.GetName().Name is string name && (name.Equals("MatchZy",StringComparison.OrdinalIgnoreCase) || name.StartsWith("MatchZy.",StringComparison.OrdinalIgnoreCase)));
        set => _bridgeCommands=value;
    }

    public static bool IsOwned => Volatile.Read(ref _ownership) is not null;
    public static bool IsOwnedOutsideCore => Volatile.Read(ref _ownership) is { IsCore: false };

    public static bool IsOwnedBy(object owner) =>
        ReferenceEquals(Volatile.Read(ref _ownership)?.Owner, owner);

    public static bool IsOwnedByAnother(object owner) =>
        Volatile.Read(ref _ownership) is { } current && !ReferenceEquals(current.Owner, owner);

    public static bool TryAcquire(object owner) => TryAcquire(owner, isCore: false);
    public static bool TryAcquireCore(object owner) => TryAcquire(owner, isCore: true);

    private static bool TryAcquire(object owner, bool isCore)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var current = Interlocked.CompareExchange(ref _ownership, new Ownership(owner, isCore), null);
        return current is null || (ReferenceEquals(current.Owner, owner) && current.IsCore == isCore);
    }

    public static void Release(object owner)
    {
        var current = Volatile.Read(ref _ownership);
        if (current is not null && ReferenceEquals(current.Owner, owner))
            Interlocked.CompareExchange(ref _ownership, null, current);
    }
}
