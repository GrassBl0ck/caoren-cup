namespace CaorenCup.Contracts;
/// <summary>Server-local authority lease: mode lifecycle and bridge match control are mutually exclusive.</summary>
public static class CaorenCupMatchAuthority
{
    private static object? _owner;
    private static bool _bridgeCommands;
    public static bool BridgeCompatibilityCommandsEnabled
    {
        get => _bridgeCommands && !AppDomain.CurrentDomain.GetAssemblies().Any(a =>
            a.GetName().Name is string name && (name.Equals("MatchZy",StringComparison.OrdinalIgnoreCase) || name.StartsWith("MatchZy.",StringComparison.OrdinalIgnoreCase)));
        set => _bridgeCommands=value;
    }
    public static bool IsOwned => Volatile.Read(ref _owner) is not null;
    public static bool TryAcquire(object owner) => ReferenceEquals(Interlocked.CompareExchange(ref _owner,owner,null),null) || ReferenceEquals(Volatile.Read(ref _owner),owner);
    public static void Release(object owner) => Interlocked.CompareExchange(ref _owner,null,owner);
}
