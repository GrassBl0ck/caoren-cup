using CaorenCup.Contracts;
namespace CaorenCupPlugin.MatchRuntime;
public static class MatchRuntimeOwnership
{
    public static bool IsMatchZyLoaded => AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is string name && (name.Equals("MatchZy",StringComparison.OrdinalIgnoreCase) || name.StartsWith("MatchZy.",StringComparison.OrdinalIgnoreCase)));
    public static bool IsRequested(bool localEnabled,bool webEnabled,string? mode,string? controller) =>
        localEnabled && webEnabled && mode is "competitive" or "ability"
        && string.Equals(controller,"caoren",StringComparison.OrdinalIgnoreCase);
    public static bool CanClaim(bool requested,GameModeLifecycleState? coreState,bool practiceActive,string? matchZyAutostart,bool matchZyLoaded = false) =>
        requested && !matchZyLoaded && coreState is not null && coreState is not (GameModeLifecycleState.Live or GameModeLifecycleState.Warmup)
        && !practiceActive && (matchZyAutostart is null or "0");
}
