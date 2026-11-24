namespace CaorenCup.Contracts;

// Percentages of the available travel area, including both endpoints.
public readonly record struct HudPoint(int X, int Y)
{
    public HudPoint Clamp() => new(Math.Clamp(X, 0, 100), Math.Clamp(Y, 0, 100));
}
public enum CombatHudBar { Buffs, Skills, Intel }

public sealed record CombatHudLayout(HudPoint Buffs, HudPoint Skills, HudPoint Intel)
{
    public static CombatHudLayout Default { get; } = new(new(3, 68), new(50, 87), new(97, 50));
    // The stored point is a fraction of the bar's travel, while clicks are screen coordinates.
    public static HudPoint FromCell(CombatHudBar bar, int column, int row)
    {
        var anchorX = bar switch { CombatHudBar.Skills => 17, CombatHudBar.Intel => 34, _ => 0 };
        var x = (100d * column / 16 - anchorX) / .66;
        var y = (10d * row) / .84;
        return new HudPoint((int)Math.Round(x), (int)Math.Round(y)).Clamp();
    }

    public HudPoint Point(CombatHudBar bar) => bar switch
    {
        CombatHudBar.Buffs => Buffs, CombatHudBar.Skills => Skills, _ => Intel
    };
    public CombatHudLayout WithPoint(CombatHudBar bar, HudPoint point) => bar switch
    {
        CombatHudBar.Buffs => this with { Buffs = point.Clamp() },
        CombatHudBar.Skills => this with { Skills = point.Clamp() },
        _ => this with { Intel = point.Clamp() }
    };
    public CombatHudLayout Clamp() => new(Buffs.Clamp(), Skills.Clamp(), Intel.Clamp());
}

// Optional extension: older providers continue serving ordinary menu preferences.
public interface ICombatHudPreferences
{
    void SetHudLayout(string steamId, CombatHudLayout layout);
}

public sealed record CombatAbilityView(string Role, string Charge, string Status, string Countdown,
    string Preview, string Prompt, string Intel)
{
    public string Effect { get; init; } = "";
    public string ChargeRule { get; init; } = "";
}

// A mode can still render its original HUD when installed without the shared menu plugin.
public interface ICombatHudRenderer { }

public interface ICombatAbilityHud
{
    CombatAbilityView? GetHud(CounterStrikeSharp.API.Core.CCSPlayerController player);
}
