using CaorenCup.Contracts;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace CaorenCup.Gamemodes.Random;

public sealed partial class CaorenCupRandomPlugin
{
    private readonly Dictionary<uint, IReadOnlyDictionary<string, int>> _deathInventory = new();
    private readonly HashSet<(uint Pawn, string Weapon, int Tick)> _throws = [];
    private readonly HashSet<(uint Pawn, uint Weapon, int Tick)> _shots = [];
    private sealed record ManualSmoke(CHandle<CSmokeGrenadeProjectile> Handle, string Owner, int Round);
    private readonly Dictionary<uint, ManualSmoke> _manualSmokes = new();

    private void TrackManualSmoke(CEntityInstance entity)
    {
        if (entity.DesignerName != "smokegrenade_projectile") return;
        var handle = new CHandle<CSmokeGrenadeProjectile>(entity.EntityHandle.Raw);
        var round = _match.Round;
        Server.NextFrame(() =>
        {
            var smoke = handle.Value;
            if (!IsRunning || !_match.RoundOpen || _match.Round != round || smoke is not { IsValid: true } || _generatedProjectiles.ContainsKey(handle.Raw)) return;
            var owner = Controller(smoke.Thrower.Value);
            if (Participant(owner)) _manualSmokes[handle.Raw] = new(handle, Id(owner!), _match.Round);
        });
    }

    private void SmokeKillSupply(CCSPlayerController killer, CCSPlayerController victim)
    {
        if (!killer.PawnIsAlive || victim.PlayerPawn.Value?.AbsOrigin is not { } origin) return;
        var id = Id(killer);
        if (_match.Level(id, "smoke_supply") == 0) return;
        var point = new Vector(origin.X, origin.Y, origin.Z + 36);
        var insideOwnSmoke = _manualSmokes.Values.Any(item => item.Owner == id && item.Round == _match.Round &&
            item.Handle.Value is { IsValid: true, DidSmokeEffect: true } smoke && CaorenCupNativeEngine.SmokeContains(smoke, point));
        if (insideOwnSmoke) Effect(id, "smoke_supply", 1, 0,
            () => CaorenCupGrenadeSupply.TryGive(killer, "weapon_smokegrenade"), channel: "kill");
    }

    private void GiveRoundSupplies(CCSPlayerController player, string? only = null)
    {
        if (!player.PawnIsAlive) return;
        var id = Id(player);
        if (only is null or "smoke_supply")
            Effect(id, "smoke_supply", 1, 0, () => CaorenCupGrenadeSupply.TryGive(player, "weapon_smokegrenade"), channel: "round");
        if (only is null or "arsenal") Effect(id, "arsenal", 1, 0, () =>
        {
            var success = false;
            foreach (var weapon in CaorenCupGrenadeSupply.Types.Take(3))
                success |= CaorenCupGrenadeSupply.TryGive(player, weapon);
            return success;
        }, channel: "round");
    }

    private HookResult OnManualThrow(EventGrenadeThrown e, GameEventInfo info)
    {
        var player = e.Userid;
        if (!IsRunning || !_match.RoundOpen || !Participant(player) || !player!.PawnIsAlive) return HookResult.Continue;
        var weapon = e.Weapon.StartsWith("weapon_", StringComparison.Ordinal) ? e.Weapon : "weapon_" + e.Weapon;
        if (!_throws.Add((player.PlayerPawn.Raw, weapon, Server.TickCount))) return HookResult.Continue;
        var id = Id(player);
        var pawn = player.PlayerPawn.Raw;
        var round = _match.Round;
        var candidates = new[] { "grenade_refund", "flash_to_he", "he_supply", "arsenal" }
            .Where(key => _match.Level(id, key) > 0)
            .ToDictionary(key => key, key => _match.Usage(id, key).Generation);
        bool StillOwned(string key) => candidates.TryGetValue(key, out var generation) &&
            _match.Level(id, key) > 0 && _match.Usage(id, key).Generation == generation;
        // 等投掷动作完成后补给，沿用原弹药模块的延后发放方式。
        AddTimer(0.1f, () =>
        {
            if (!IsRunning || !_match.RoundOpen || _match.Round != round || !player.IsValid || !player.PawnIsAlive || player.PlayerPawn.Raw != pawn) return;
            if (weapon == "weapon_hegrenade" && StillOwned("grenade_refund") && Effect(id, "grenade_refund", 1, 0,
                () => CaorenCupGrenadeSupply.TryGive(player, "weapon_hegrenade"))) return;
            if (weapon == "weapon_flashbang" && StillOwned("flash_to_he") && Effect(id, "flash_to_he", 1, 0,
                () => CaorenCupGrenadeSupply.TryGive(player, "weapon_hegrenade"))) return;
            if (weapon == "weapon_hegrenade" && StillOwned("he_supply") && _match.TryReserve(id, "he_supply", 3, 6, Server.CurrentTime, out var reservation))
            {
                AddTimer(0.9f, () =>
                {
                    if (!player.IsValid || !player.PawnIsAlive || player.PlayerPawn.Raw != pawn)
                    { _match.Cancel(reservation); return; }
                    var supplied = _match.Complete(reservation,
                        () => CaorenCupGrenadeSupply.TryGive(player, "weapon_hegrenade"), Server.CurrentTime);
                    if (!supplied && StillOwned("arsenal")) ArsenalEmpty(player);
                }, TimerFlags.STOP_ON_MAPCHANGE);
                return;
            }
            if (StillOwned("arsenal")) ArsenalEmpty(player);
        }, TimerFlags.STOP_ON_MAPCHANGE);
        return HookResult.Continue;
    }

    private void ArsenalEmpty(CCSPlayerController player)
    {
        if (player.PlayerPawn.Value is not { } pawn || CaorenCupGrenadeSupply.Inventory(pawn).Values.Sum() > 0) return;
        Effect(Id(player), "arsenal", 1, 0, () => CaorenCupGrenadeSupply.TryGive(player, "weapon_hegrenade"), channel: "empty");
    }

    private HookResult OnShot(EventWeaponFire e, GameEventInfo info)
    {
        var player = e.Userid;
        if (!IsRunning || !_match.RoundOpen || !Participant(player) || !player!.PawnIsAlive || !CaorenCupDamageSources.IsGun(e.Weapon))
            return HookResult.Continue;
        var weapon = player.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value;
        if (weapon is not { IsValid: true } || !_shots.Add((player.PlayerPawn.Raw, weapon.EntityHandle.Raw, Server.TickCount))) return HookResult.Continue;
        var id = Id(player);
        var pawn = player.PlayerPawn.Raw;
        var round = _match.Round;
        var handle = new CHandle<CBasePlayerWeapon>(weapon.EntityHandle.Raw);
        var candidates = new[] { "ammo_coupon", "magazine_repeat" }
            .Where(key => _match.Level(id, key) > 0)
            .ToDictionary(key => key, key => _match.Usage(id, key).Generation);
        // 下一帧读取引擎本次射击结算后的实际弹匣。
        Server.NextFrame(() =>
        {
            if (!IsRunning || !_match.RoundOpen || _match.Round != round || !player.IsValid ||
                !player.PawnIsAlive || player.PlayerPawn.Raw != pawn || handle.Value is not { IsValid: true } current ||
                player.PlayerPawn.Value?.WeaponServices?.MyWeapons.Any(item => item.Raw == handle.Raw) != true) return;
            bool StillOwned(string key) => candidates.TryGetValue(key, out var generation) &&
                _match.Level(id, key) > 0 && _match.Usage(id, key).Generation == generation;
            var level = _match.Level(id, "ammo_coupon");
            if (StillOwned("ammo_coupon") && System.Random.Shared.NextDouble() < (level == 1 ? 0.15 : 0.25))
                Effect(id, "ammo_coupon", level == 1 ? 20 : 30, 0, () => CaorenCupWeaponAmmo.AddClip(current, 1) > 0);
            if (current.Clip1 == 0 && StillOwned("magazine_repeat"))
                Effect(id, "magazine_repeat", 1, 0, () => CaorenCupWeaponAmmo.AddClip(current, CaorenCupWeaponAmmo.Capacity(current)) > 0);
        });
        return HookResult.Continue;
    }

    private void KillSupplies(CCSPlayerController killer)
    {
        if (!killer.PawnIsAlive || killer.PlayerPawn.Value is not { } pawn) return;
        var id = Id(killer);
        Effect(id, "kill_he", 2, 5, () => CaorenCupGrenadeSupply.TryGive(killer, "weapon_hegrenade"));
        var repair = _match.Level(id, "field_repair");
        if (repair > 0)
            Effect(id, "field_repair", 2, 3, () =>
            {
                var recovered = Heal(pawn, repair == 1 ? 20 : 30);
                recovered += Armor(pawn, repair == 1 ? 10 : 15);
                return recovered > 0;
            });
        Effect(id, "kill_reload", 2, 5, () => FillClip(killer));
    }

    private void RescueTeam(CCSPlayerController victim)
    {
        var id = Id(victim);
        var level = _match.Level(id, RandomCatalog.TeamRescue);
        if (level == 0) return;
        Effect(id, RandomCatalog.TeamRescue, 1, 0, () =>
        {
            foreach (var teammate in Utilities.GetPlayers().Where(Participant).Where(player => player.TeamNum == victim.TeamNum && player.PawnIsAlive))
            {
                Heal(teammate.PlayerPawn.Value!, level == 1 ? 15 : 25);
                FillClip(teammate, level == 1 ? 0.25f : 0.4f);
            }
            return true;
        });
    }

    private void DeathSupplies(CCSPlayerController victim)
    {
        var pawn = victim.PlayerPawn.Value;
        if (pawn?.AbsOrigin is not { } origin) return;
        var inventory = _deathInventory.GetValueOrDefault(pawn.EntityHandle.Raw) ?? CaorenCupGrenadeSupply.Inventory(pawn);
        var id = Id(victim);
        var legacy = CaorenCupGrenadeSupply.Types.Take(3).SelectMany(weapon => Enumerable.Repeat(weapon,
            inventory.GetValueOrDefault(weapon))).Take(2).ToArray();
        if (legacy.Length > 0) Effect(id, "legacy_double", 1, 0, () =>
        {
            var dropped = false;
            foreach (var weapon in legacy)
            {
                var item = Utilities.CreateEntityByName<CCSWeaponBase>(weapon);
                if (item is null) continue;
                item.DispatchSpawn();
                item.Teleport(new Vector(origin.X, origin.Y, origin.Z + 12), null, new Vector(0, 0, 40));
                dropped = true;
            }
            return dropped;
        });
        var count = Math.Min(2, 1 + CaorenCupGrenadeSupply.Types.Take(3).Sum(weapon => inventory.GetValueOrDefault(weapon)));
        Effect(id, "death_bombs", 1, 0, () =>
        {
            var generated = false;
            for (var i = 0; i < count; i++)
                generated |= CreateHe(victim, new Vector(origin.X, origin.Y, origin.Z + 16), "death_bombs", 0.8f, 0.5f, 0.7f) is not null;
            return generated;
        });
    }
}
