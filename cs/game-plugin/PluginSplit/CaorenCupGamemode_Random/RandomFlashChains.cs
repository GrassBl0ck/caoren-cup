using CaorenCup.Contracts;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace CaorenCup.Gamemodes.Random;

public sealed partial class CaorenCupRandomPlugin
{
    private sealed record FlashTarget(CCSPlayerController Player, uint Pawn);
    private sealed class FlashExplosion(int entityIndex, int tick, string owner, Vector position,
        string source, int chain, int generation)
    {
        public readonly int EntityIndex = entityIndex, Tick = tick, Chain = chain, Generation = generation;
        public readonly string Owner = owner, Source = source;
        public readonly Vector Position = new(position.X, position.Y, position.Z);
        public readonly Dictionary<string, FlashTarget> Targets = new(StringComparer.Ordinal);
        public bool Queued, Resolved;
    }

    private readonly Dictionary<int, FlashExplosion> _flashExplosions = new();
    private readonly Dictionary<int, HashSet<string>> _flashChainTargets = new();
    private int _nextChain;

    private CFlashbangProjectile? CreateFlash(CCSPlayerController owner, Vector position, string card,
        float delay, int chain = 0, int generation = 0)
    {
        if (owner.PlayerPawn.Value is not { IsValid: true }) return null;
        var flash = Utilities.CreateEntityByName<CFlashbangProjectile>("flashbang_projectile");
        if (flash is null) return null;
        try
        {
            MarkProjectile(flash, owner, card, chain, generation);
            flash.Teleport(new Vector(position.X, position.Y, position.Z + 8), null, new Vector(0, 0, 0));
            flash.DispatchSpawn();
            flash.DetonateTime = Server.CurrentTime + delay;
            return flash;
        }
        catch (Exception error)
        {
            if (flash.IsValid) flash.Remove();
            Logger.LogError(error, "随机乱斗追加闪光生成失败，释放本次额度");
            return null;
        }
    }

    private HookResult OnFlashExplosion(EventFlashbangDetonate e, GameEventInfo info)
    {
        if (!IsRunning || !_match.RoundOpen || !Participant(e.Userid)) return HookResult.Continue;
        var generated = GeneratedAt(e.Entityid);
        var explosion = new FlashExplosion(e.Entityid, Server.TickCount, Id(e.Userid!), new(e.X, e.Y, e.Z),
            generated?.Card ?? "manual", generated?.Chain ?? 0, generated?.Generation ?? 0);
        if (_flashExplosions.TryGetValue(e.Entityid, out var before) && before.Tick == Server.TickCount)
        {
            foreach (var target in before.Targets) explosion.Targets[target.Key] = target.Value;
        }
        _flashExplosions[e.Entityid] = explosion;
        QueueFlash(explosion);
        return HookResult.Continue;
    }

    private HookResult OnBlind(EventPlayerBlind e, GameEventInfo info)
    {
        var victim = e.Userid;
        if (!IsRunning || !_match.RoundOpen || !Participant(victim) || e.BlindDuration < 1 || !Enemy(e.Attacker, victim!))
            return HookResult.Continue;
        if (!_flashExplosions.TryGetValue(e.Entityid, out var explosion) || explosion.Tick != Server.TickCount)
        {
            var entity = Utilities.GetEntityFromIndex<CFlashbangProjectile>(e.Entityid);
            if (entity is not { IsValid: true } || entity.AbsOrigin is not { } origin) return HookResult.Continue;
            var generated = _generatedProjectiles.GetValueOrDefault(entity.EntityHandle.Raw);
            _flashExplosions[e.Entityid] = explosion = new(e.Entityid, Server.TickCount, Id(e.Attacker!), origin,
                generated?.Card ?? "manual", generated?.Chain ?? 0, generated?.Generation ?? 0);
        }
        explosion.Targets[Id(victim!)] = new(victim!, victim!.PlayerPawn.Raw);
        QueueFlash(explosion);
        return HookResult.Continue;
    }

    private void QueueFlash(FlashExplosion explosion)
    {
        if (explosion.Queued) return;
        explosion.Queued = true;
        var round = _match.Round;
        Server.NextFrame(() =>
        {
            if (!IsRunning || !_match.RoundOpen || _match.Round != round) return;
            // 两种引擎事件的先后均可接受，取同一帧最后合并的受盲名单。
            if (_flashExplosions.TryGetValue(explosion.EntityIndex, out var current) && current.Tick == explosion.Tick)
                ResolveFlash(current);
        });
    }

    private void ResolveFlash(FlashExplosion explosion)
    {
        if (explosion.Resolved) return;
        explosion.Resolved = true;
        var targets = explosion.Targets.Values.Where(target => Participant(target.Player) && target.Player.PawnIsAlive &&
            target.Player.PlayerPawn.Raw == target.Pawn).OrderBy(target => Distance(explosion.Position, target.Player.PlayerPawn.Value!.AbsOrigin)).ToArray();
        if (explosion.Source == "manual")
        {
            foreach (var target in targets)
                Effect(Id(target.Player), "flash_retaliation", 1, 0, () =>
                    CreateFlash(target.Player, target.Player.PlayerPawn.Value!.AbsOrigin!, "flash_retaliation", 0.6f) is not null);
        }
        var owner = Utilities.GetPlayers().FirstOrDefault(player => Participant(player) && Id(player) == explosion.Owner);
        if (owner is null || targets.Length == 0) return;
        if (explosion.Source == "manual")
        {
            var target = targets[0].Player;
            Effect(explosion.Owner, "flash_partner", 1, 0, () =>
                CreateFlash(owner, target.PlayerPawn.Value!.AbsOrigin!, "flash_partner", 0.8f) is not null);
        }
        if (explosion.Source is not ("manual" or "flash_partner" or "flash_chain") || explosion.Generation >= 2) return;
        var chain = explosion.Chain == 0 ? ++_nextChain : explosion.Chain;
        if (!_flashChainTargets.TryGetValue(chain, out var seen)) _flashChainTargets[chain] = seen = new(StringComparer.Ordinal);
        var selected = targets.FirstOrDefault(target => !seen.Contains(Id(target.Player)));
        if (selected is null) return;
        if (Effect(explosion.Owner, "flash_chain", 2, 0, () =>
            CreateFlash(owner, selected.Player.PlayerPawn.Value!.AbsOrigin!, "flash_chain", 0.8f, chain, explosion.Generation + 1) is not null))
            seen.Add(Id(selected.Player));
    }

    private HookResult OnHeExplosion(EventHegrenadeDetonate e, GameEventInfo info)
    {
        if (!IsRunning || !_match.RoundOpen || !Participant(e.Userid) ||
            GeneratedAt(e.Entityid) is not null) return HookResult.Continue;
        Effect(Id(e.Userid!), "cluster_he", 1, 0, () =>
        {
            var success = false;
            for (var i = 0; i < 3; i++)
                success |= CreateHe(e.Userid!, new(e.X, e.Y, e.Z), "cluster_he", 0.5f, 0.2f, 0.5f) is not null;
            return success;
        });
        return HookResult.Continue;
    }
}
