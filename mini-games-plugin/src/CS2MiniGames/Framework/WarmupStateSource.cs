using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace CS2MiniGames.Framework;

internal enum WarmupState
{
    Unavailable,
    Inactive,
    Active
}

internal interface IWarmupStateSource
{
    WarmupState Read();
}

internal sealed class CounterStrikeSharpWarmupStateSource : IWarmupStateSource
{
    public WarmupState Read()
    {
        var proxy = Utilities
            .FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
            .FirstOrDefault();
        var rules = proxy?.GameRules;
        if (rules is null)
        {
            return WarmupState.Unavailable;
        }

        return rules.WarmupPeriod ? WarmupState.Active : WarmupState.Inactive;
    }
}
