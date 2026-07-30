namespace CaorenCup.WeaponPaints.Core;

public enum RefreshDecision
{
    ApplyNow,
    QueueForSpawn,
    Reject
}

public readonly record struct RefreshContext
{
    public RefreshContext(
        bool enabled,
        bool dataLoaded,
        bool alive,
        bool warmup,
        bool isOfficialRound,
        bool force)
    {
        Enabled = enabled;
        DataLoaded = dataLoaded;
        Alive = alive;
        Warmup = warmup;
        IsOfficialRound = isOfficialRound;
        Force = force;
    }

    public bool Enabled { get; }
    public bool DataLoaded { get; }
    public bool Alive { get; }
    public bool Warmup { get; }
    public bool IsOfficialRound { get; }
    public bool Force { get; }
}

public static class RefreshPolicy
{
    public static RefreshDecision Decide(RefreshContext context)
    {
        if (!context.Enabled || !context.DataLoaded)
        {
            return RefreshDecision.Reject;
        }

        if (context.Force)
        {
            return RefreshDecision.ApplyNow;
        }

        if (!context.Alive)
        {
            return RefreshDecision.QueueForSpawn;
        }

        return context.IsOfficialRound && !context.Warmup
            ? RefreshDecision.QueueForSpawn
            : RefreshDecision.ApplyNow;
    }
}

public static class RefreshCommandMode
{
    public static bool IsSafe(string? value) =>
        string.Equals(value?.Trim(), "safe", StringComparison.OrdinalIgnoreCase);
}
