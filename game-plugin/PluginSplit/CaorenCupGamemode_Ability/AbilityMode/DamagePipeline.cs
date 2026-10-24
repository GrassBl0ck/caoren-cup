using System.Numerics;

namespace Caoren.AbilityMode;

public sealed record DamageContext
{
    public string? AttackerSeatId { get; init; }
    public string VictimSeatId { get; init; } = string.Empty;
    public string? AttackerAbilityId { get; init; }
    public string VictimAbilityId { get; init; } = string.Empty;
    public double OriginalDamage { get; init; }
    public string WeaponType { get; init; } = string.Empty;
    public RoleDamageKind RoleDamageKind { get; init; } = RoleDamageKind.Other;
    public double FrontAngleDegrees { get; init; } = 180;
    public int HitGroup { get; init; }
    public Vector3 Direction { get; init; }
    public bool IsAirborne { get; init; }
    public bool IsFullyInvincible { get; init; }
    public bool IsTrueDamage { get; init; }
    public bool IsPluginGenerated { get; init; }
    public string? PluginSourceTag { get; init; }
    public int ActualHealthLoss { get; init; }
    public bool FinalDeath { get; init; }
    public string? KillAttributionSeatId { get; init; }
    public bool IsValidEntity { get; init; }
    public bool IsFormalRound { get; init; }
    public bool IsEnemySource { get; init; }
}

public sealed record DamageEngineResult(int ActualHealthLoss, bool Died);
public sealed record DamagePreparation(
    bool Ok,
    int EngineDamage,
    DamageContext Context,
    bool BlockedByInvincibility = false,
    bool RejectedInvalidContext = false,
    bool RejectedRecursiveSource = false);
public sealed record DamageResult(
    int EngineDamage,
    int ActualHealthLoss,
    bool FinalDeath,
    bool BlockedByInvincibility = false,
    bool RejectedInvalidContext = false,
    bool RejectedRecursiveSource = false);

public interface IDamageEngine
{
    DamageEngineResult Apply(int damage, bool bypassArmor);
}

public interface IDamageReplacement
{
    double Apply(DamageContext context, double currentDamage);
}

public interface IDamageMultiplier
{
    double Apply(DamageContext context);
}

public sealed class DamagePipeline(IDamageEngine engine)
{
    private readonly HashSet<string> _activePluginSources = new(StringComparer.Ordinal);
    public List<IDamageReplacement> Replacements { get; } = [];
    public List<IDamageMultiplier> AttackerModifiers { get; } = [];
    public List<IDamageMultiplier> VictimMitigations { get; } = [];
    public List<Action<DamageContext>> AfterHealthLoss { get; } = [];
    public List<Action<DamageContext>> RawPhysicalEffects { get; } = [];
    public List<Action<DamageContext>> OnDeathConfirmed { get; } = [];
    public Action<string>? StageObserved { get; set; }

    public DamageResult Process(DamageContext context)
    {
        var prepared = Prepare(context);
        if (!prepared.Ok)
            return new(
                0,
                0,
                false,
                prepared.BlockedByInvincibility,
                prepared.RejectedInvalidContext,
                prepared.RejectedRecursiveSource);
        var engineResult = engine.Apply(prepared.EngineDamage, context.IsTrueDamage);
        return CompleteAfterEngine(prepared, engineResult.ActualHealthLoss, engineResult.Died);
    }

    public DamagePreparation Prepare(DamageContext context)
    {
        StageObserved?.Invoke("validate");
        if (!context.IsValidEntity || !context.IsFormalRound || context.OriginalDamage < 0 || string.IsNullOrWhiteSpace(context.VictimSeatId))
            return new(false, 0, context, RejectedInvalidContext: true);
        if (context.IsPluginGenerated && context.PluginSourceTag is { Length: > 0 } tag && _activePluginSources.Contains(tag))
            return new(false, 0, context, RejectedRecursiveSource: true);

        var damage = context.OriginalDamage;
        foreach (var replacement in Replacements) damage = Math.Max(0, replacement.Apply(context, damage));

        StageObserved?.Invoke("invincible");
        if (context.IsFullyInvincible) return new(false, 0, context, BlockedByInvincibility: true);

        foreach (var modifier in AttackerModifiers) damage *= Math.Max(0, modifier.Apply(context));
        if (!context.IsTrueDamage)
            foreach (var mitigation in VictimMitigations) damage *= Math.Max(0, mitigation.Apply(context));

        var engineDamage = Math.Max(0, checked((int)Math.Round(damage, MidpointRounding.AwayFromZero)));
        return new(true, engineDamage, context);
    }

    public DamageResult CompleteAfterEngine(DamagePreparation prepared, int actualHealthLoss, bool finalDeath)
    {
        if (!prepared.Ok)
            return new(
                0,
                0,
                false,
                prepared.BlockedByInvincibility,
                prepared.RejectedInvalidContext,
                prepared.RejectedRecursiveSource);
        var context = prepared.Context;
        var sourceAdded = context.IsPluginGenerated && context.PluginSourceTag is { Length: > 0 } sourceTag
            && _activePluginSources.Add(sourceTag);
        try
        {
            var settled = context with
            {
                ActualHealthLoss = Math.Max(0, actualHealthLoss),
                FinalDeath = finalDeath,
            };
            foreach (var effect in AfterHealthLoss) effect(settled);
            foreach (var effect in RawPhysicalEffects) effect(settled);
            if (finalDeath)
                foreach (var callback in OnDeathConfirmed) callback(settled);
            return new(prepared.EngineDamage, settled.ActualHealthLoss, finalDeath);
        }
        finally
        {
            if (sourceAdded) _activePluginSources.Remove(context.PluginSourceTag!);
        }
    }

    public void ConfirmDeath(DamagePreparation prepared, string? killAttributionSeatId)
    {
        if (!prepared.Ok) return;
        var context = prepared.Context with
        {
            FinalDeath = true,
            KillAttributionSeatId = killAttributionSeatId,
        };
        var sourceAdded = context.IsPluginGenerated && context.PluginSourceTag is { Length: > 0 } sourceTag
            && _activePluginSources.Add(sourceTag);
        try
        {
            foreach (var callback in OnDeathConfirmed) callback(context);
        }
        finally
        {
            if (sourceAdded) _activePluginSources.Remove(context.PluginSourceTag!);
        }
    }
}
