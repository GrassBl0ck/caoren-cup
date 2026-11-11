using CaorenCup.Contracts;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace CaorenCup.Gamemodes.Random;

public sealed partial class CaorenCupRandomPlugin
{
    private readonly Dictionary<nint, BuffReservation> _insurance = new();
    private readonly HashSet<nint> _scriptDamage = [];
    private readonly Dictionary<(string Attacker, uint Victim), int> _directDamage = new();
    private readonly Dictionary<string, double> _combatSeconds = new();

    private static CCSPlayerController? Controller(CBaseEntity? entity)
    {
        if (entity is not { IsValid: true }) return null;
        if (entity.DesignerName == "cs_player_controller") return entity.As<CCSPlayerController>();
        return entity.DesignerName == "player" ? entity.As<CCSPlayerPawn>().Controller.Value?.As<CCSPlayerController>() : null;
    }

    private static bool ExcludedDamage(CTakeDamageInfo damage) => damage.Inflictor.Value?.DesignerName is
        "planted_c4" or "trigger_hurt" or "worldspawn";

    private bool Generated(CTakeDamageInfo damage) => _scriptDamage.Contains(damage.Handle) ||
        _generatedProjectiles.ContainsKey(damage.Inflictor.Raw);

    private static string DamageWeapon(CTakeDamageInfo damage, CCSPlayerController? attacker)
    {
        var inflictor = damage.Inflictor.Value;
        if (inflictor is { IsValid: true } && inflictor.DesignerName != "player") return inflictor.DesignerName;
        return damage.Ability.Value?.DesignerName ?? attacker?.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value?.DesignerName ?? "";
    }

    private void PrepareCombatDamage(CEntityInstance entity, CTakeDamageInfo damage)
    {
        if (!IsRunning || !_match.RoundOpen || entity.DesignerName != "player") return;
        var pawn = entity.As<CCSPlayerPawn>();
        var victim = Controller(pawn);
        if (!Participant(victim) || !victim!.PawnIsAlive) return;
        NormalizeMovementForDamage(victim);
        var attacker = Controller(damage.Attacker.Value);
        var id = Id(victim);
        if (!ExcludedDamage(damage) && (damage.BitsDamageType & DamageTypes_t.DMG_FALL) != 0)
        {
            damage.Damage *= _match.Level(id, "soft_landing") switch { 1 => 0.5f, 2 => 0.25f, _ => 1 };
            return;
        }
        if (!Enemy(attacker, victim) || ExcludedDamage(damage)) return;
        _deathInventory[pawn.EntityHandle.Raw] = CaorenCupGrenadeSupply.Inventory(pawn);
        var generated = Generated(damage);
        var weapon = DamageWeapon(damage, attacker);
        if (!generated && CaorenCupDamageSources.IsGun(weapon))
        {
            var attackId = Id(attacker!);
            var multiplier = 1f;
            if (_match.Level(attackId, RandomCatalog.Tank) > 0) multiplier += _match.Growth(attackId, RandomCatalog.Tank) * 0.01f;
            if (_match.Level(attackId, RandomCatalog.Light) > 0) multiplier -= 0.1f;
            var reduction = _match.Level(id, "time_resist") == 0 ? 0 : Math.Min(0.1, Math.Floor(_combatSeconds.GetValueOrDefault(id) / 5) * 0.02);
            damage.Damage *= multiplier * (float)(1 - Math.Min(0.75, reduction));
        }
        else if (!generated && (weapon.Contains("knife", StringComparison.Ordinal) || weapon.Contains("bayonet", StringComparison.Ordinal)))
        {
            var attackId = Id(attacker!);
            if (_match.Level(attackId, RandomCatalog.Light) > 0)
                damage.Damage *= 1.15f + _match.Growth(attackId, RandomCatalog.Light) * 0.03f;
        }
        if (damage.Damage > 0 && (damage.DamageFlags & TakeDamageFlags_t.DFLAG_PREVENT_DEATH) == 0 &&
            _match.TryReserve(id, "death_insurance", 1, 0, Server.CurrentTime, out var reservation))
        {
            damage.DamageFlags |= TakeDamageFlags_t.DFLAG_PREVENT_DEATH;
            _insurance[damage.Handle] = reservation;
        }
    }

    private void FinishCombatDamage(CBaseEntity entity, CTakeDamageInfo damage, CTakeDamageResult result)
    {
        if (!IsRunning || !_match.RoundOpen || entity.DesignerName != "player") return;
        var pawn = entity.As<CCSPlayerPawn>();
        var victim = Controller(pawn);
        var attacker = Controller(damage.Attacker.Value);
        if (!Participant(victim)) return;
        if (victim!.PawnIsAlive) FinishMovementDamage(victim);
        var saved = false;
        if (_insurance.Remove(damage.Handle, out var reservation))
        {
            // 引擎在 Post 中的 Damage 已过护甲，HealthLost 则已经夹到免死后的值。
            // 不能用 HealthLost==HealthBefore-1 判断，否则普通的99点伤害也会耗掉保险。
            if (!result.WasDamageSuppressed && result.HealthBefore > 0 && damage.Damage >= result.HealthBefore)
                saved = _match.Complete(reservation, () =>
                {
                    pawn.Health = 1;
                    Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
                    FillClip(victim!);
                    return true;
                }, Server.CurrentTime);
            else _match.Cancel(reservation);
        }
        if (!Enemy(attacker, victim!) || ExcludedDamage(damage) || result.HealthLost <= 0) return;
        var actual = Math.Min(Math.Max(0, result.HealthBefore), result.HealthLost);
        var generated = Generated(damage);
        var gun = !generated && CaorenCupDamageSources.IsGun(DamageWeapon(damage, attacker));
        if (!generated)
        {
            var key = (Id(attacker!), pawn.EntityHandle.Raw);
            _directDamage[key] = _directDamage.GetValueOrDefault(key) + actual;
        }
        if (gun && attacker!.PawnIsAlive)
        {
            var attackId = Id(attacker);
            var health = attacker.PlayerPawn.Value!;
            var remaining = Math.Max(0, 75 - _match.Usage(attackId, "lifesteal").Used);
            var heal = Math.Min(Math.Min(actual / 4, remaining), Math.Max(0, Math.Min(200, health.MaxHealth) - health.Health));
            if (heal > 0) Effect(attackId, "lifesteal", 75, 0, () => Heal(health, heal) > 0, heal);
        }
        if (!victim!.PawnIsAlive || pawn.Health <= 0) return;
        if (!saved) EmergencyHeal(victim);
        if (gun) ArmorBuddy(victim);
        if (gun) KineticHit(attacker!, victim, damage);
    }

    private bool Effect(string id, string key, int limit, double cooldown, Func<bool> effect, int amount = 1, string channel = "") =>
        _match.TryReserve(id, key, limit, cooldown, Server.CurrentTime, out var reservation, amount, channel) &&
        _match.Complete(reservation, effect, Server.CurrentTime);

    private static int Heal(CCSPlayerPawn pawn, int amount)
    {
        var restored = Math.Min(Math.Max(0, amount), Math.Max(0, Math.Min(200, pawn.MaxHealth) - pawn.Health));
        if (restored <= 0 || pawn.Health <= 0) return 0;
        pawn.Health += restored;
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
        return restored;
    }

    private static int Armor(CCSPlayerPawn pawn, int amount)
    {
        var restored = Math.Min(Math.Max(0, amount), Math.Max(0, 100 - pawn.ArmorValue));
        if (restored <= 0) return 0;
        pawn.ArmorValue += restored;
        Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_ArmorValue");
        return restored;
    }

    private static bool FillClip(CCSPlayerController player, float fraction = 1)
    {
        var weapon = player.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value;
        return weapon is { IsValid: true } && CaorenCupWeaponAmmo.AddClip(weapon,
            (int)Math.Floor(CaorenCupWeaponAmmo.Capacity(weapon) * fraction)) > 0;
    }

    private void EmergencyHeal(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn.Value!;
        var id = Id(player);
        if (pawn.Health < 40 && Effect(id, "comeback", 1, 0, () =>
            { Heal(pawn, 100); Armor(pawn, 100); FillClip(player); return true; })) return;
        if (pawn.Health <= 25 && Effect(id, "last_chance", 1, 0, () =>
            {
                if (pawn.AbsOrigin is not { } origin || CreateSmoke(player, origin, "last_chance") is null) return false;
                Heal(pawn, 25);
                return true;
            })) return;
        var hp = _match.HealAfterDamage(id, pawn.Health, Math.Min(200, pawn.MaxHealth), true);
        if (hp != pawn.Health)
        { pawn.Health = hp; Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth"); }
    }

    private void ArmorBuddy(CCSPlayerController player)
    {
        var level = _match.Level(Id(player), "armor_buddy");
        var pawn = player.PlayerPawn.Value!;
        if (level == 0 || pawn.AbsOrigin is not { } origin) return;
        var other = Utilities.GetPlayers().Where(Participant).Where(other => other.PawnIsAlive &&
            other.Slot != player.Slot && other.TeamNum == player.TeamNum)
            .Select(other => (Player: other, Distance: Distance(origin, other.PlayerPawn.Value!.AbsOrigin)))
            .Where(item => item.Distance <= 4 * 39.3701f && Visible(pawn, item.Player.PlayerPawn.Value!))
            .OrderBy(item => item.Distance).FirstOrDefault().Player;
        if (other?.PlayerPawn.Value is not { } teammate || pawn.ArmorValue >= 100 && teammate.ArmorValue >= 100) return;
        Effect(Id(player), "armor_buddy", 3, 6, () =>
        { var restored = Armor(pawn, level == 1 ? 10 : 15); restored += Armor(teammate, level == 1 ? 10 : 15); return restored > 0; });
    }

    private static float Distance(Vector from, Vector? to) => to is null ? float.PositiveInfinity :
        MathF.Sqrt(MathF.Pow(from.X - to.X, 2) + MathF.Pow(from.Y - to.Y, 2) + MathF.Pow(from.Z - to.Z, 2));

    private static bool Visible(CCSPlayerPawn from, CCSPlayerPawn to)
    {
        if (from.AbsOrigin is not { } a || to.AbsOrigin is not { } b) return false;
        var trace = Trace.TraceEndShape(new(a.X, a.Y, a.Z + 36), new(b.X, b.Y, b.Z + 36), from);
        return !trace.IsAllSolid && (trace.Fraction >= 0.98f || trace.HitEntity()?.EntityHandle.Raw == to.EntityHandle.Raw);
    }
}
