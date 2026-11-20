namespace CaorenCup.Contracts;

public enum MatchAdminAction
{
    SelectMode,
    Start,
    Pause,
    Resume,
    RestoreRound,
    Stop
}

public sealed record MatchAdminStamp(string? MatchId, long Revision, long Timeline, int? CurrentRound);

public sealed record MatchAdminState(
    MatchAdminStamp Stamp,
    string Status,
    IReadOnlyList<GameModeDescriptor> AvailableModes,
    bool CanStart,
    bool CanPause,
    bool IsPaused,
    bool CanStop,
    IReadOnlyList<int> RestorableRounds);

public sealed record MatchAdminRequest(
    MatchAdminAction Action,
    MatchAdminStamp? Expected = null,
    string? ModeId = null,
    int? Round = null);

public interface ICaorenCupMatchAdministration
{
    MatchAdminState ReadMatchAdminState();
    GameModeResult ApplyMatchAdminAction(MatchAdminRequest request);
}

public interface ICaorenCupHostedMatchAdministration : ICaorenCupMatchAdministration
{
    bool ControlsMatch { get; }
}

public static class CaorenCupMatchAdministration
{
    private static ICaorenCupHostedMatchAdministration? _hosted;

    public static ICaorenCupMatchAdministration? Current => CaorenCupCoreAccess.TryGet() as ICaorenCupMatchAdministration;
    public static ICaorenCupHostedMatchAdministration? Hosted =>
        Volatile.Read(ref _hosted) is { ControlsMatch: true } owner ? owner : null;

    public static void PublishHosted(ICaorenCupHostedMatchAdministration owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (Interlocked.CompareExchange(ref _hosted, owner, null) is { } existing && !ReferenceEquals(existing, owner))
            throw new InvalidOperationException("Another hosted match administration provider is already active.");
    }

    public static void WithdrawHosted(ICaorenCupHostedMatchAdministration owner) =>
        Interlocked.CompareExchange(ref _hosted, null, owner);
}
