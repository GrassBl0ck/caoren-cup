using System.Runtime.InteropServices;
using CaorenCup.Contracts;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace CaorenCup.Gamemodes.Random;

public sealed partial class CaorenCupRandomPlugin
{
    // 引擎工厂的 ABI 与指纹需随服务器更新验证，失败时不消费卡牌额度。
    // ABI 参考 https://github.com/ed0ard/CS2-Bot-NadeSystem 。此处只使用引擎接口信息。
    private static readonly Lazy<MemoryFunctionWithReturn<nint, nint, nint, nint, nint, int, int, CSmokeGrenadeProjectile>> SmokeFactory = new(() => new(
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? "55 4C 89 C1 48 89 E5 41 57 41 56 49 89 FE 41 55 45 89 CD 41 54 4D 89 C4"
            : "48 89 5C 24 ? 48 89 6C 24 ? 48 89 74 24 ? 57 41 56 41 57 48 83 EC ? 48 8B B4 24 ? ? ? ? 4D 8B F8"));
    private static readonly Lazy<MemoryFunctionWithReturn<nint, nint, nint, nint, nint, int, CHEGrenadeProjectile>> HeFactory = new(() => new(
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? "55 4C 89 C1 48 89 E5 41 57 49 89 FF 41 56 49 89 D6"
            : "48 89 5C 24 ? 48 89 6C 24 ? 48 89 74 24 ? 57 48 83 EC ? 48 8B AC 24 ? ? ? ? 49 8B F8"));

    private sealed record GeneratedProjectile(CHandle<CBaseCSGrenadeProjectile> Handle, int Index, string Owner, string Card,
        int Round, int Chain = 0, int Generation = 0);
    private readonly Dictionary<uint, GeneratedProjectile> _generatedProjectiles = new();
    private readonly Dictionary<int, uint> _projectileHandles = new();

    private void TrackProjectile(CEntityInstance entity)
    {
        if (entity.DesignerName.EndsWith("_projectile", StringComparison.Ordinal))
            _projectileHandles[(int)entity.Index] = entity.EntityHandle.Raw;
        TrackManualHe(entity);
        TrackManualSmoke(entity);
    }

    private GeneratedProjectile? GeneratedAt(int index) => _projectileHandles.TryGetValue(index, out var raw)
        ? _generatedProjectiles.GetValueOrDefault(raw) : null;

    private bool SpawnGhost(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn?.AbsOrigin is not { } origin) return false;
        var decoy = Utilities.CreateEntityByName<CDecoyProjectile>("decoy_projectile");
        if (decoy is null) return false;
        try
        {
            MarkProjectile(decoy, player, RandomCatalog.Ghost);
            decoy.ItemIndex = 47;
            decoy.DispatchSpawn();
            var angle = System.Random.Shared.NextDouble() * Math.PI * 2;
            decoy.Teleport(new Vector(origin.X, origin.Y, origin.Z + 20), null,
                new Vector((float)Math.Cos(angle) * 180, (float)Math.Sin(angle) * 180, 220));
            return true;
        }
        catch (Exception error)
        {
            if (decoy.IsValid) decoy.Remove();
            Logger.LogError(error, "幽灵快递生成失败，释放本次额度");
            return false;
        }
    }

    private CSmokeGrenadeProjectile? CreateSmoke(CCSPlayerController player, Vector position, string card)
    {
        if (player.PlayerPawn.Value is not { IsValid: true } pawn) return null;
        try
        {
            var angles = new QAngle(0, 0, 0);
            var velocity = new Vector(0, 0, 0);
            var smoke = SmokeFactory.Value.Invoke(position.Handle, angles.Handle, velocity.Handle, velocity.Handle,
                pawn.Handle, 45, player.TeamNum);
            if (smoke is not { IsValid: true }) return null;
            MarkProjectile(smoke, player, card);
            return smoke;
        }
        catch (Exception error)
        {
            Logger.LogError(error, "随机乱斗烟雾生成失败，释放本次额度");
            return null;
        }
    }

    private void MarkProjectile(CBaseCSGrenadeProjectile entity, CCSPlayerController player, string card,
        int chain = 0, int generation = 0)
    {
        var pawn = player.PlayerPawn.Value!;
        entity.Thrower.Raw = pawn.EntityHandle.Raw;
        entity.OriginalThrower.Raw = pawn.EntityHandle.Raw;
        entity.OwnerEntity.Raw = pawn.EntityHandle.Raw;
        entity.TeamNum = player.TeamNum;
        _projectileHandles[(int)entity.Index] = entity.EntityHandle.Raw;
        _generatedProjectiles[entity.EntityHandle.Raw] = new(new(entity.EntityHandle.Raw), (int)entity.Index, Id(player), card, _match.Round, chain, generation);
    }

    private CHEGrenadeProjectile? CreateHe(CCSPlayerController player, Vector position, string card,
        float delay, float damageScale, float radiusScale)
    {
        if (player.PlayerPawn.Value is not { IsValid: true } pawn) return null;
        try
        {
            var angle = System.Random.Shared.NextDouble() * Math.PI * 2;
            var velocity = new Vector((float)Math.Cos(angle) * 140, (float)Math.Sin(angle) * 140, 160);
            var angles = new QAngle(0, 0, 0);
            var spin = new Vector(0, 0, 0);
            var he = HeFactory.Value.Invoke(position.Handle, angles.Handle, velocity.Handle, spin.Handle, pawn.Handle, 44);
            if (he is not { IsValid: true }) return null;
            he.Damage *= damageScale;
            he.DmgRadius *= radiusScale;
            he.DetonateTime = Server.CurrentTime + delay;
            MarkProjectile(he, player, card);
            return he;
        }
        catch (Exception error)
        {
            Logger.LogError(error, "随机乱斗高爆子雷生成失败，释放本次额度");
            return null;
        }
    }

    public GameModeResult Activate(CCSPlayerController player, string key, long revision) => PlayerAction(player, () =>
    {
        var view = _match.View(Id(player), false);
        if (view is null || view.Revision != revision) return GameModeResult.Fail("页面已更新，请重新选择。");
        var card = RandomCatalog.Find(key);
        if (card is null || !card.Active || _match.Level(Id(player), key) == 0)
            return GameModeResult.Fail("尚未拥有这项主动技能。");
        if (!player.PawnIsAlive || !_match.RoundOpen || Rules() is not { WarmupPeriod: false, FreezePeriod: false })
            return GameModeResult.Fail("主动技能需要在正式交战时存活使用。");
        if (!_match.TryReserve(Id(player), key, 1, 0, Server.CurrentTime, out var reservation))
            return GameModeResult.Fail("本回合次数已用完，或技能正在执行。");
        if (key == "smoke_train")
        {
            StartSmokeTrain(player, reservation);
            return GameModeResult.Ok("烟雾列车已启动。");
        }
        if (!ThrowDevice(player, key, reservation))
        {
            _match.Cancel(reservation);
            return GameModeResult.Fail("装置生成失败，本次额度已释放。");
        }
        return GameModeResult.Ok($"已投出 {card.Name}。");
    });

    private void StartSmokeTrain(CCSPlayerController player, BuffReservation reservation)
    {
        var id = Id(player);
        var pawnHandle = player.PlayerPawn.Raw;
        var generation = _match.Usage(id, "smoke_train").Generation;
        var started = false;
        for (var i = 1; i <= 4; i++)
        {
            var first = i == 1;
            AddTimer(i, () =>
            {
                if (!IsRunning || !_match.RoundOpen || _match.Round != reservation.Round || !player.IsValid ||
                    !player.PawnIsAlive || player.PlayerPawn.Raw != pawnHandle ||
                    _match.Usage(id, "smoke_train").Generation != generation)
                { _match.Cancel(reservation); return; }
                if (!first && !started) return;
                var position = player.PlayerPawn.Value?.AbsOrigin;
                if (position is null) { _match.Cancel(reservation); return; }
                if (first)
                    started = _match.Complete(reservation, () => CreateSmoke(player, position, "smoke_train") is not null, Server.CurrentTime);
                else CreateSmoke(player, position, "smoke_train");
            }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
        }
    }

    private bool ThrowDevice(CCSPlayerController player, string key, BuffReservation reservation)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn?.AbsOrigin is not { } origin) return false;
        var device = Utilities.CreateEntityByName<CDecoyProjectile>("decoy_projectile");
        if (device is null) return false;
        var handle = new CHandle<CDecoyProjectile>(device.EntityHandle.Raw);
        try
        {
            MarkProjectile(device, player, key);
            device.DispatchSpawn();
            device.DetonateTime = float.MaxValue;
            var yaw = pawn.EyeAngles.Y * Math.PI / 180;
            var pitch = pawn.EyeAngles.X * Math.PI / 180;
            device.Teleport(new Vector(origin.X, origin.Y, origin.Z + 48), null,
                new Vector((float)(Math.Cos(yaw) * Math.Cos(pitch) * 500),
                    (float)(Math.Sin(yaw) * Math.Cos(pitch) * 500), (float)(-Math.Sin(pitch) * 500 + 100)));
            WatchDevice(key, handle, reservation, 0);
            return true;
        }
        catch
        {
            if (handle.Value is { IsValid: true } remaining) remaining.Remove();
            _generatedProjectiles.Remove(handle.Raw);
            return false;
        }
    }

    private void WatchDevice(string key, CHandle<CDecoyProjectile> handle,
        BuffReservation reservation, int attempts)
    {
        AddTimer(0.1f, () =>
        {
            var device = handle.Value;
            if (!_match.IsPending(reservation) || device is not { IsValid: true } || attempts >= 100)
            {
                _match.Cancel(reservation);
                if (device is { IsValid: true }) device.Remove();
                return;
            }
            if (device.Bounces == 0)
            { WatchDevice(key, handle, reservation, attempts + 1); return; }
            AddTimer(1, () =>
            {
                try
                {
                    _match.Complete(reservation, () => handle.Value is { IsValid: true } current && DetonateDevice(key, current), Server.CurrentTime);
                }
                finally { if (handle.Value is { IsValid: true } current) current.Remove(); }
            }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
        }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
    }

    private bool DetonateDevice(string key, CDecoyProjectile device)
    {
        if (!device.IsValid || device.AbsOrigin is not { } position) return false;
        foreach (var player in Utilities.GetPlayers().Where(Participant).Where(player => player.PawnIsAlive))
        {
            var pawn = player.PlayerPawn.Value!;
            var origin = pawn.AbsOrigin;
            if (origin is null) continue;
            var dx = origin.X - position.X;
            var dy = origin.Y - position.Y;
            var dz = origin.Z - position.Z;
            var distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            if (distance > 5 * 39.3701f || distance < 1) continue;
            var ray = Trace.TraceEndShape(new(position.X, position.Y, position.Z + 8), new(origin.X, origin.Y, origin.Z + 32), device);
            if (ray.Fraction < 0.98f && ray.HitEntity()?.EntityHandle.Raw != pawn.EntityHandle.Raw) continue;
            var direction = key == "repulsor" ? 1 : -1;
            var length = Math.Min(3 * 39.3701f, distance);
            var target = new Vector(origin.X + dx / distance * length * direction,
                origin.Y + dy / distance * length * direction, origin.Z);
            var hull = Trace.TraceHullShape(origin, target, new(-16, -16, 0), new(16, 16, 72), pawn);
            if (!hull.IsAllSolid && hull.Fraction > 0)
                pawn.Teleport(hull.EndPos, null, null);
        }
        return true;
    }
}
