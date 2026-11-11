using CaorenCup.Contracts;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace CaorenCup.Gamemodes.Random;

public static class RandomAttributes
{
    public static int MaxHealth(int baseline, bool tank, int growth) => Math.Clamp(
        baseline + (tank ? 50 + Math.Clamp(growth, 0, 5) * 10 : 0), 1, 200);

    public static float Speed(float baseline, bool tank, bool light, int lightGrowth,
        int knifeLevel, bool knifeActive, int smokeLevel, bool smokeActive, bool diagonal)
    {
        var percent = 1f - (tank ? 0.08f : 0) + (light ? 0.05f + Math.Clamp(lightGrowth, 0, 5) * 0.01f : 0);
        if (knifeActive) percent += knifeLevel switch { 1 => 0.1f, 2 => 0.15f, _ => 0 };
        if (smokeActive) percent += smokeLevel switch { 1 => 0.08f, 2 => 0.12f, _ => 0 };
        return Math.Clamp(baseline * percent + (diagonal ? 40 : 0), 0, baseline * 1.5f);
    }
}

public sealed partial class CaorenCupRandomPlugin
{
    private sealed class PassiveState(uint pawn, int baseHealth)
    {
        public readonly uint Pawn = pawn;
        public readonly int BaseHealth = baseHealth;
        public int AppliedMaxHealth = baseHealth;
        public uint Weapon;
        public bool WasKnife, InSmoke;
        public float Tag = 1, NativeTag = 1, SpeedMultiplier = 1;
        public double KnifeUntil, KnifeReady, SmokeUntil, NextSmokeCheck, NextSmokeHeal, ForwardSince;
    }
    private readonly Dictionary<string, PassiveState> _passives = new();
    private readonly HashSet<(int Round, uint Pawn)> _deaths = [];
    private double _lastPassiveTick;

    private HookResult OnSpawn(EventPlayerSpawn e, GameEventInfo info)
    {
        var player = e.Userid;
        if (!IsRunning || !Participant(player)) return HookResult.Continue;
        var id = Id(player!);
        var controller = player!.EntityHandle.Raw;
        _passives.Remove(id);
        Server.NextFrame(() =>
        {
            if (!IsRunning || !_match.RoundOpen || !Participant(player) || player.EntityHandle.Raw != controller) return;
            ApplyHealthAttributes(player);
            GiveRoundSupplies(player);
        });
        return HookResult.Continue;
    }

    private void ResetPassiveUsage(CCSPlayerController player, string key)
    {
        var id = Id(player);
        if (key is "" or "time_resist") _combatSeconds[id] = 0;
        if (!_passives.TryGetValue(id, out var state)) return;
        if (key is "" or "knife_sprint") state.KnifeUntil = state.KnifeReady = 0;
        if (key is "" or "smoke_regen") state.NextSmokeHeal = 0;
        if (key is "" or "battering_ram") state.ForwardSince = 0;
    }

    private PassiveState Passive(CCSPlayerController player)
    {
        var id = Id(player);
        var pawn = player.PlayerPawn.Value!;
        if (!_passives.TryGetValue(id, out var state) || state.Pawn != pawn.EntityHandle.Raw)
            _passives[id] = state = new(pawn.EntityHandle.Raw, Math.Max(1, pawn.MaxHealth));
        return state;
    }

    private void ApplyHealthAttributes(CCSPlayerController player)
    {
        if (!Participant(player) || !player.PawnIsAlive || player.PlayerPawn.Value is not { } pawn) return;
        var id = Id(player);
        var state = Passive(player);
        var maximum = RandomAttributes.MaxHealth(state.BaseHealth, _match.Level(id, RandomCatalog.Tank) > 0,
            _match.Growth(id, RandomCatalog.Tank));
        if (state.AppliedMaxHealth == maximum && pawn.MaxHealth == maximum) return;
        var increase = Math.Max(0, maximum - state.AppliedMaxHealth);
        pawn.MaxHealth = maximum;
        pawn.Health = Math.Min(maximum, pawn.Health + increase);
        state.AppliedMaxHealth = maximum;
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iMaxHealth");
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
    }

    private void TickPassives()
    {
        var now = Server.CurrentTime;
        var elapsed = _lastPassiveTick == 0 ? 0 : Math.Clamp(now - _lastPassiveTick, 0, 0.1);
        _lastPassiveTick = now;
        if (!IsRunning || !_match.RoundOpen) return;
        var combat = Rules() is { WarmupPeriod: false, FreezePeriod: false };
        foreach (var player in Utilities.GetPlayers().Where(Participant).Where(player => player.PawnIsAlive))
        {
            var id = Id(player);
            var pawn = player.PlayerPawn.Value!;
            var state = Passive(player);
            ApplyHealthAttributes(player);
            // 当前服务器实测原版受击速度每秒恢复0.4，移速加成与此归一量分层保存。
            state.Tag = Math.Min(1, state.Tag + (float)elapsed * 0.4f);
            if (combat) _combatSeconds[id] = _combatSeconds.GetValueOrDefault(id) + elapsed;
            var weapon = pawn.WeaponServices?.ActiveWeapon.Value;
            var knife = weapon is not null && (weapon.DesignerName.Contains("knife", StringComparison.Ordinal) ||
                weapon.DesignerName.Contains("bayonet", StringComparison.Ordinal));
            if (weapon is not null && state.Weapon != weapon.EntityHandle.Raw)
            {
                if (state.Weapon != 0 && knife && !state.WasKnife && now >= state.KnifeReady && _match.Level(id, "knife_sprint") > 0)
                { state.KnifeUntil = now + 2; state.KnifeReady = now + 8; }
                if (!knife) state.KnifeUntil = 0;
                state.Weapon = weapon.EntityHandle.Raw;
                state.WasKnife = knife;
            }
            if (combat) Ram(player, state, knife, now);
            if (now >= state.NextSmokeCheck && (_match.Level(id, "smoke_haste") > 0 || _match.Level(id, "smoke_regen") > 0))
            {
                state.NextSmokeCheck = now + 0.1;
                var origin = pawn.AbsOrigin;
                state.InSmoke = origin is not null && CaorenCupSmokeCoverage.Contains(new(origin.X, origin.Y, origin.Z + 36));
                if (state.InSmoke)
                {
                    state.SmokeUntil = now + 1.5;
                    if (state.NextSmokeHeal == 0) state.NextSmokeHeal = now + 1;
                    if (combat && now >= state.NextSmokeHeal)
                    {
                        state.NextSmokeHeal = now + 1;
                        var level = _match.Level(id, "smoke_regen");
                        var cap = level == 1 ? 30 : 45;
                        var amount = Math.Min(level == 1 ? 3 : 5, Math.Min(
                            Math.Max(0, cap - _match.Usage(id, "smoke_regen").Used), Math.Max(0, pawn.MaxHealth - pawn.Health)));
                        if (level > 0 && amount > 0) Effect(id, "smoke_regen", cap, 0, () => Heal(pawn, amount) > 0, amount);
                    }
                }
                else state.NextSmokeHeal = 0;
            }
            ApplyMovement(player, state, knife, now);
        }
    }

    private void ApplyMovement(CCSPlayerController player, PassiveState state, bool knife, double now)
    {
        var pawn = player.PlayerPawn.Value!;
        var weapon = pawn.WeaponServices?.ActiveWeapon.Value?.As<CCSWeaponBase>();
        var baseline = weapon?.VData?.MaxSpeed.Values[Math.Clamp((int)weapon.WeaponMode, 0, 1)] ?? 250;
        if (baseline <= 0) return;
        var id = Id(player);
        var buttons = player.Buttons;
        var diagonal = ((buttons & PlayerButtons.Forward) != 0) != ((buttons & PlayerButtons.Back) != 0) &&
            ((buttons & PlayerButtons.Moveleft) != 0) != ((buttons & PlayerButtons.Moveright) != 0);
        var speed = RandomAttributes.Speed(baseline, _match.Level(id, RandomCatalog.Tank) > 0,
            _match.Level(id, RandomCatalog.Light) > 0, _match.Growth(id, RandomCatalog.Light),
            _match.Level(id, "knife_sprint"), knife && now < state.KnifeUntil,
            _match.Level(id, "smoke_haste"), now < state.SmokeUntil,
            diagonal && _match.Level(id, "diagonal_speed") > 0);
        state.SpeedMultiplier = speed / baseline;
        var value = state.SpeedMultiplier * state.Tag;
        if (MathF.Abs(pawn.VelocityModifier - value) > 0.0001f)
            CaorenCupPlayerMovement.SetMultiplier(pawn, value);
    }

    private void NormalizeMovementForDamage(CCSPlayerController player)
    {
        var state = Passive(player);
        // 引擎先以原版归一量处理命中减速，避免基础移速被当成上一发的减速。
        player.PlayerPawn.Value!.VelocityModifier = state.Tag;
    }

    private void FinishMovementDamage(CCSPlayerController player)
    {
        var state = Passive(player);
        state.NativeTag = state.Tag = Math.Clamp(player.PlayerPawn.Value!.VelocityModifier, 0, 1);
        CaorenCupPlayerMovement.SetMultiplier(player.PlayerPawn.Value!, state.SpeedMultiplier * state.Tag);
    }

    public void ApplyTagging(CCSPlayerController player, Func<float, float> adjust)
    {
        if (!IsRunning || !_match.RoundOpen || !Participant(player) || !player.PawnIsAlive) return;
        var state = Passive(player);
        state.Tag = Math.Clamp(adjust(state.NativeTag), 0, 1);
        CaorenCupPlayerMovement.SetMultiplier(player.PlayerPawn.Value!, state.SpeedMultiplier * state.Tag);
    }

    private void GrowOnKill(CCSPlayerController victim, CCSPlayerController killer)
    {
        var pawn = victim.PlayerPawn.Raw;
        foreach (var player in Utilities.GetPlayers().Where(Participant).Where(player => player.TeamNum == killer.TeamNum))
        {
            var id = Id(player);
            if (player.Slot != killer.Slot && _directDamage.GetValueOrDefault((id, pawn)) < 41) continue;
            _match.AddGrowth(id, RandomCatalog.Tank);
            _match.AddGrowth(id, RandomCatalog.Light);
            ApplyHealthAttributes(player);
        }
    }

    private void RestoreAttributes()
    {
        foreach (var player in Utilities.GetPlayers().Where(Participant))
        {
            if (player.PlayerPawn.Value is not { IsValid: true } pawn ||
                !_passives.TryGetValue(Id(player), out var state) || state.Pawn != pawn.EntityHandle.Raw) continue;
            pawn.MaxHealth = state.BaseHealth;
            pawn.Health = Math.Min(pawn.Health, state.BaseHealth);
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iMaxHealth");
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
            CaorenCupPlayerMovement.SetMultiplier(pawn, state.Tag);
        }
        _passives.Clear();
    }
}
