namespace Caoren.AbilityMode;

using CounterStrikeSharp.API.Core;

public enum AbilityPrototypeKind
{
    PlayerBuyLimit,
    BallisticOffset,
    FullInvisibility,
    DecoyLanding,
    SafeTeleport,
    ZeroLifeDamageKeepKnockback,
}

public sealed record AbilityPrototypeResult(bool Ok, string Code = "OK", string Message = "");

public interface IAbilityEngineCapabilities
{
    bool Supports(AbilityPrototypeKind prototype);
}

public sealed class NullAbilityEngineCapabilities : IAbilityEngineCapabilities
{
    public static NullAbilityEngineCapabilities Instance { get; } = new();
    public bool Supports(AbilityPrototypeKind prototype) => false;
}

public sealed class CounterStrikeAbilityEngineCapabilities : IAbilityEngineCapabilities
{
    public bool Supports(AbilityPrototypeKind prototype) => prototype switch
    {
        // EventDecoyStarted supplies the authoritative server event and coordinates.
        AbilityPrototypeKind.DecoyLanding => typeof(EventDecoyStarted).GetProperty(nameof(EventDecoyStarted.X)) is not null,
        // Teleport alone is unsafe without a trace/map collision query.
        _ => false,
    };
}

public sealed class AbilityEnginePrototypeGate(IAbilityEngineCapabilities capabilities)
{
    public AbilityPrototypeResult Check(AbilityPrototypeKind prototype) => capabilities.Supports(prototype)
        ? new(true)
        : new(false, "PROTOTYPE_UNAVAILABLE", $"引擎原型 {prototype} 当前不可用。");
}
