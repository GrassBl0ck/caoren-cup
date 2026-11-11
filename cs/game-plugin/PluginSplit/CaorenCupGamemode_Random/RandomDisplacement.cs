using System.Runtime.InteropServices;
using CaorenCup.Contracts;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;

namespace CaorenCup.Gamemodes.Random;

public sealed partial class CaorenCupRandomPlugin
{
    private sealed record ManualHe(CHandle<CHEGrenadeProjectile> Handle, string Owner, int Round, long Generation)
    {
        public bool Tried { get; set; }
    }
    private readonly Dictionary<uint, ManualHe> _manualHe = new();

    private void TrackManualHe(CEntityInstance entity)
    {
        if (entity.DesignerName != "hegrenade_projectile") return;
        var handle = new CHandle<CHEGrenadeProjectile>(entity.EntityHandle.Raw);
        var round = _match.Round;
        Server.NextFrame(() =>
        {
            var he = handle.Value;
            if (!IsRunning || !_match.RoundOpen || _match.Round != round || he is not { IsValid: true } || _generatedProjectiles.ContainsKey(handle.Raw)) return;
            var owner = Controller(he.Thrower.Value);
            if (!Participant(owner) || _match.Level(Id(owner!), "bounce_missile") == 0) return;
            _manualHe[handle.Raw] = new(handle, Id(owner!), _match.Round, _match.Usage(Id(owner!), "bounce_missile").Generation);
        });
    }

    private void TickBounces()
    {
        if (!IsRunning || !_match.RoundOpen) return;
        foreach (var pair in _manualHe.ToArray())
        {
            var item = pair.Value;
            var he = item.Handle.Value;
            if (he is not { IsValid: true } || item.Round != _match.Round)
            { _manualHe.Remove(pair.Key); continue; }
            if (item.Tried || he.Bounces == 0) continue;
            item.Tried = true;
            var owner = Utilities.GetPlayers().FirstOrDefault(player => Participant(player) && Id(player) == item.Owner);
            if (owner is null || _match.Usage(item.Owner, "bounce_missile").Generation != item.Generation || he.AbsOrigin is not { } origin) continue;
            var target = Utilities.GetPlayers().Where(Participant).Where(player => player.PawnIsAlive && Enemy(owner, player))
                .Select(player => (Player: player, Distance: Distance(origin, player.PlayerPawn.Value!.AbsOrigin)))
                .Where(candidate => candidate.Distance <= 12 * 39.3701f && ProjectileCanSee(he, candidate.Player.PlayerPawn.Value!))
                .OrderBy(candidate => candidate.Distance).FirstOrDefault().Player;
            if (target?.PlayerPawn.Value?.AbsOrigin is not { } position) continue;
            var velocity = he.AbsVelocity;
            var speed = MathF.Sqrt(velocity.X * velocity.X + velocity.Y * velocity.Y + velocity.Z * velocity.Z);
            var direction = new Vector(position.X - origin.X, position.Y - origin.Y, position.Z + 36 - origin.Z);
            var length = MathF.Sqrt(direction.X * direction.X + direction.Y * direction.Y + direction.Z * direction.Z);
            if (speed < 1 || length < 1) continue;
            Effect(item.Owner, "bounce_missile", 2, 0, () =>
            {
                he.Teleport(null, null, new Vector(direction.X / length * speed, direction.Y / length * speed, direction.Z / length * speed));
                return true;
            });
        }
    }

    private static bool ProjectileCanSee(CBaseEntity projectile, CCSPlayerPawn target)
    {
        if (projectile.AbsOrigin is not { } a || target.AbsOrigin is not { } b) return false;
        var end = new Vector(b.X, b.Y, b.Z + 36);
        var trace = Trace.TraceEndShape(a, end, projectile);
        return !trace.IsAllSolid && (trace.Fraction >= 0.98f || trace.HitEntity()?.EntityHandle.Raw == target.EntityHandle.Raw) &&
            !CaorenCupSmokeCoverage.Obscures(a, end);
    }

    private static bool MoveSafely(CCSPlayerPawn pawn, Vector direction, float maximum, bool limitDrop)
    {
        if (pawn.AbsOrigin is not { } origin) return false;
        var length = MathF.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
        if (length < 0.001f || maximum <= 0) return false;
        var destination = new Vector(origin.X + direction.X / length * maximum,
            origin.Y + direction.Y / length * maximum, origin.Z);
        var trace = Trace.TraceHullShape(origin, destination, new(-16, -16, 0), new(16, 16, 72), pawn);
        if (trace.IsAllSolid || trace.Fraction <= 0) return false;
        var fraction = Math.Max(0, trace.Fraction - 0.01f);
        var end = new Vector(origin.X + (destination.X - origin.X) * fraction,
            origin.Y + (destination.Y - origin.Y) * fraction, origin.Z);
        if (Distance(origin, end) < 1) return false;
        if (limitDrop)
        {
            var step = ConVar.Find("sv_stepsize")?.GetPrimitiveValue<float>() ?? 18;
            var ground = Trace.TraceHullShape(new(end.X, end.Y, end.Z + 1), new(end.X, end.Y, end.Z - step),
                new(-15, -15, 0), new(15, 15, 2), pawn);
            if (ground.IsAllSolid || ground.Fraction >= 1) return false;
        }
        pawn.Teleport(end, null, null);
        return true;
    }

    private void KineticHit(CCSPlayerController attacker, CCSPlayerController victim, CTakeDamageInfo damage)
    {
        var pawn = victim.PlayerPawn.Value!;
        if ((pawn.Flags & (uint)PlayerFlags.FL_ONGROUND) == 0 || pawn.AbsOrigin is not { } b ||
            attacker.PlayerPawn.Value?.AbsOrigin is not { } a) return;
        var force = damage.DamageDirection;
        var direction = MathF.Abs(force.X) + MathF.Abs(force.Y) > 0.001f ? new Vector(force.X, force.Y, 0) : new(b.X - a.X, b.Y - a.Y, 0);
        Effect(Id(attacker), "kinetic_hit", 3, 5, () => MoveSafely(pawn, direction, 39.3701f, true));
    }

    private void Ram(CCSPlayerController player, PassiveState state, bool knife, double now)
    {
        var pawn = player.PlayerPawn.Value!;
        var forward = (player.Buttons & PlayerButtons.Forward) != 0;
        var moving = pawn.AbsVelocity.X * pawn.AbsVelocity.X + pawn.AbsVelocity.Y * pawn.AbsVelocity.Y > 100;
        if (!knife || !forward || !moving || _match.Level(Id(player), "battering_ram") == 0)
        { state.ForwardSince = 0; return; }
        if (state.ForwardSince == 0) state.ForwardSince = now;
        if (now - state.ForwardSince < 1.2 || pawn.AbsOrigin is not { } origin) return;
        var target = Utilities.GetPlayers().Where(Participant).Where(other => other.PawnIsAlive && Enemy(player, other))
            .Select(other => (Player: other, Distance: Distance(origin, other.PlayerPawn.Value!.AbsOrigin)))
            .Where(item => item.Distance <= 1.5 * 39.3701f && Visible(pawn, item.Player.PlayerPawn.Value!))
            .OrderBy(item => item.Distance).FirstOrDefault().Player;
        if (target?.PlayerPawn.Value is not { } targetPawn || targetPawn.AbsOrigin is not { } targetOrigin) return;
        if (Effect(Id(player), "battering_ram", 2, 12, () =>
            {
                DealBuffDamage(player, targetPawn, 15);
                if (target.PawnIsAlive) MoveSafely(targetPawn, new(targetOrigin.X - origin.X, targetOrigin.Y - origin.Y, 0), 2 * 39.3701f, false);
                return true;
            })) state.ForwardSince = 0;
    }

    private void DealBuffDamage(CCSPlayerController attacker, CCSPlayerPawn victim, int amount)
    {
        var infoSize = Schema.GetClassSize("CTakeDamageInfo");
        var resultSize = Schema.GetClassSize("CTakeDamageResult");
        var infoMemory = Marshal.AllocHGlobal(infoSize);
        var resultMemory = Marshal.AllocHGlobal(resultSize);
        try
        {
            Marshal.Copy(new byte[infoSize], 0, infoMemory, infoSize);
            Marshal.Copy(new byte[resultSize], 0, resultMemory, resultSize);
            var damage = new CTakeDamageInfo(infoMemory);
            var result = new CTakeDamageResult(resultMemory);
            damage.Attacker.Raw = attacker.PlayerPawn.Raw;
            damage.Inflictor.Raw = attacker.PlayerPawn.Raw;
            damage.Damage = amount;
            damage.BitsDamageType = DamageTypes_t.DMG_GENERIC;
            Schema.SetSchemaValue(resultMemory, "CTakeDamageResult", "m_pOriginatingInfo", infoMemory);
            result.HealthBefore = victim.Health;
            result.HealthLost = result.TotalledHealthLost = amount;
            result.DamageDealt = result.PreModifiedDamage = result.TotalledDamageDealt = amount;
            _scriptDamage.Add(infoMemory);
            VirtualFunctions.CBaseEntity_TakeDamageOld(victim, damage, result);
        }
        finally
        {
            _scriptDamage.Remove(infoMemory);
            Marshal.FreeHGlobal(infoMemory);
            Marshal.FreeHGlobal(resultMemory);
        }
    }
}
