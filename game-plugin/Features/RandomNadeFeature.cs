using System.Globalization;
using Vector3 = System.Numerics.Vector3;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace CaorenCup.Features;

public sealed class RandomNadeFeature : ICaorenFeature
{
    private const string CvarOwner = "RandomNadeFeature";
    private const string ShootDroppedGrenadesCvar = "mp_shoot_dropped_grenades";

    private readonly List<PendingProjectile> _pending = [];
    private CaorenCupPlugin _plugin = null!;
    private RandomNadeSettings _settings = null!;
    private long _actionEntitySequence;

    public string FeatureName => "Random Nade (开枪随机投掷物)";

    public void Init(CaorenCupPlugin plugin)
    {
        _plugin = plugin;
        plugin.AddCommand(
            "css_randomnade",
            "开枪随机投掷物: /randomnade <总概率> [烟] [火] [雷] [闪] [诱饵]；/randomnade 0 关闭",
            OnCommandRandomNade);
        plugin.RegisterEventHandler<EventWeaponFire>(OnWeaponFire, HookMode.Post);
        plugin.RegisterListener<Listeners.OnTick>(OnTick);
        if (_settings.Enabled) EnableDetonationSupport();
    }

    public void OnConfigParsed(CaorenCupConfig config)
    {
        _settings = config.RandomNade;
        ClampSettings();
    }

    public void OnUnload()
    {
        _pending.Clear();
        _plugin.ManagedCvars.ResetOwner(CvarOwner);
    }

    public void SetEnabled(bool enabled)
    {
        _settings.Enabled = enabled;
        if (enabled)
        {
            EnableDetonationSupport();
        }
        else
        {
            if (_pending.Count > 0) EnableDetonationSupport();
            else TryRestoreDetonationSupport();
        }
    }

    private void OnCommandRandomNade(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            Reply(player, "你没有权限使用此指令。");
            return;
        }

        if (info.ArgCount == 1)
        {
            PrintUsage(player);
            return;
        }

        var arguments = new List<string>(info.ArgCount - 1);
        for (var index = 1; index < info.ArgCount; index++)
        {
            arguments.Add(info.GetArg(index));
        }

        var current = ReadCommandState();
        var result = RandomNadeRules.ApplyArguments(current, arguments);
        if (!result.Success)
        {
            Reply(player, $"配置未修改：{result.Error}。用法见 /randomnade");
            return;
        }

        ApplyCommandState(result.State);
        _plugin.SaveConfig();

        if (!_settings.Enabled)
        {
            ReplyAllOrConsole(player, "开枪随机投掷物已关闭。");
            return;
        }

        ReplyAllOrConsole(player, $"开枪随机投掷物已开启：{FormatStatus(includeConfigured: false)}");
    }

    private HookResult OnWeaponFire(EventWeaponFire @event, GameEventInfo info)
    {
        if (!_settings.Enabled || _settings.TotalChance <= 0) return HookResult.Continue;

        var player = @event.Userid;
        if (player == null || !player.IsValid || player.IsBot || player.IsHLTV || !player.PawnIsAlive)
        {
            return HookResult.Continue;
        }

        if (!RandomNadeRuntimeRules.IsFirearm(@event.Weapon)) return HookResult.Continue;
        if (Random.Shared.NextDouble() * 100 >= _settings.TotalChance) return HookResult.Continue;

        var type = RandomNadeRules.Select(ReadWeights(), Random.Shared.NextDouble() * 100);
        try
        {
            SpawnProjectile(player, type);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[CaorenCup:RandomNade] 生成投掷物失败: {exception.Message}");
        }

        return HookResult.Continue;
    }

    private void SpawnProjectile(CCSPlayerController player, RandomNadeType type)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null || pawn.EyeAngles == null) return;

        var plan = RandomNadeRuntimeRules.GetPlan(type);
        var projectile = Utilities.CreateEntityByName<CBaseCSGrenadeProjectile>(plan.ProjectileDesignerName);
        if (projectile == null || !projectile.IsValid)
        {
            Console.Error.WriteLine($"[CaorenCup:RandomNade] 无法创建实体 {plan.ProjectileDesignerName}");
            return;
        }

        projectile.DispatchSpawn();
        if (!projectile.IsValid) return;

        var pawnHandle = pawn.EntityHandle.Raw;
        projectile.Thrower.Raw = pawnHandle;
        projectile.OriginalThrower.Raw = pawnHandle;
        projectile.OwnerEntity.Raw = pawnHandle;
        projectile.TeamNum = pawn.TeamNum;

        if (type != RandomNadeType.Flash)
        {
            projectile.GravityScale = 0.4f;
        }

        var eyePosition = new Vector(
            pawn.AbsOrigin.X + pawn.ViewOffset.X,
            pawn.AbsOrigin.Y + pawn.ViewOffset.Y,
            pawn.AbsOrigin.Z + pawn.ViewOffset.Z);
        var playerVelocity = pawn.AbsVelocity == null
            ? Vector3.Zero
            : new Vector3(pawn.AbsVelocity.X, pawn.AbsVelocity.Y, pawn.AbsVelocity.Z);
        var launchVelocity = RandomNadeRuntimeRules.ComputeLaunchVelocity(
            pawn.EyeAngles.X,
            pawn.EyeAngles.Y,
            playerVelocity);
        var velocity = new Vector(launchVelocity.X, launchVelocity.Y, launchVelocity.Z);

        projectile.InitialVelocity.X = velocity.X;
        projectile.InitialVelocity.Y = velocity.Y;
        projectile.InitialVelocity.Z = velocity.Z;
        projectile.Teleport(eyePosition, pawn.EyeAngles, velocity);
        projectile.AcceptInput("InitializeSpawnFromWorld", pawn, pawn);

        if (plan.DetonationKind != RandomNadeDetonationKind.Native)
        {
            EnableDetonationSupport();
            _pending.Add(new PendingProjectile(
                projectile,
                new CHandle<CCSPlayerPawn>(pawnHandle),
                plan,
                Server.CurrentTime));
        }
    }

    private void OnTick()
    {
        if (_pending.Count == 0)
        {
            TryRestoreDetonationSupport();
            return;
        }

        for (var index = _pending.Count - 1; index >= 0; index--)
        {
            var pending = _pending[index];
            if (!pending.Projectile.IsValid)
            {
                _pending.RemoveAt(index);
                continue;
            }

            var velocity = pending.Projectile.AbsVelocity;
            var speed = velocity == null
                ? double.PositiveInfinity
                : Math.Sqrt(velocity.X * velocity.X + velocity.Y * velocity.Y + velocity.Z * velocity.Z);
            var age = Math.Max(0, Server.CurrentTime - pending.SpawnTime);
            if (!RandomNadeRuntimeRules.IsReadyToForceDetonate(pending.Plan, age, speed)) continue;

            ForceDetonate(pending);
            _pending.RemoveAt(index);
        }

        TryRestoreDetonationSupport();
    }

    private void ForceDetonate(PendingProjectile pending)
    {
        var thrower = pending.Thrower.Value;
        if (thrower == null || !thrower.IsValid)
        {
            if (pending.Projectile.IsValid) pending.Projectile.Remove();
            return;
        }

        var origin = pending.Projectile.AbsOrigin;
        var position = origin == null
            ? thrower.AbsOrigin
            : new Vector(origin.X, origin.Y, origin.Z);
        if (pending.Projectile.IsValid) pending.Projectile.Remove();
        if (position == null || string.IsNullOrWhiteSpace(pending.Plan.ActionWeaponDesignerName)) return;

        var targetName = $"caoren_randomnade_{Interlocked.Increment(ref _actionEntitySequence)}";
        var actionWeapon = Utilities.CreateEntityByName<CBasePlayerWeapon>(pending.Plan.ActionWeaponDesignerName);
        if (actionWeapon == null || !actionWeapon.IsValid) return;

        using (var weaponKeys = new CEntityKeyValues())
        {
            weaponKeys.SetString("targetname", targetName);
            actionWeapon.DispatchSpawn(weaponKeys);
        }

        if (!actionWeapon.IsValid) return;
        actionWeapon.Teleport(position, null, new Vector(0, 0, 0));

        var hurt = Utilities.CreateEntityByName<CPointHurt>("point_hurt");
        if (hurt == null || !hurt.IsValid)
        {
            actionWeapon.Remove();
            return;
        }

        hurt.DispatchSpawn();
        if (hurt.IsValid)
        {
            hurt.StrTarget = targetName;
            hurt.Damage = 100;
            hurt.BitsDamageType = DamageTypes_t.DMG_BULLET;
            hurt.AcceptInput("Hurt", thrower, thrower);
            hurt.Remove();
        }

        _plugin.AddTimer(0.5f, () =>
        {
            if (actionWeapon.IsValid) actionWeapon.Remove();
        });
    }

    private void EnableDetonationSupport()
    {
        _plugin.ManagedCvars.Set(CvarOwner, ShootDroppedGrenadesCvar, "1", "0");
    }

    private void TryRestoreDetonationSupport()
    {
        if (_settings.Enabled || _pending.Count > 0) return;
        _plugin.ManagedCvars.ResetOwner(CvarOwner);
    }

    private RandomNadeCommandState ReadCommandState() => new(
        _settings.Enabled,
        _settings.TotalChance,
        ReadWeights());

    private RandomNadeWeights ReadWeights() => new(
        _settings.SmokeChance,
        _settings.FireChance,
        _settings.HighExplosiveChance,
        _settings.FlashChance,
        _settings.DecoyChance);

    private void ApplyCommandState(RandomNadeCommandState state)
    {
        _settings.TotalChance = state.TotalChance;
        _settings.SmokeChance = state.Weights.Smoke;
        _settings.FireChance = state.Weights.Fire;
        _settings.HighExplosiveChance = state.Weights.HighExplosive;
        _settings.FlashChance = state.Weights.Flash;
        _settings.DecoyChance = state.Weights.Decoy;
        SetEnabled(state.Enabled);
    }

    private void ClampSettings()
    {
        _settings.TotalChance = Math.Clamp(_settings.TotalChance, 0, 100);
        _settings.SmokeChance = Math.Clamp(_settings.SmokeChance, 0, 100);
        _settings.FireChance = Math.Clamp(_settings.FireChance, 0, 100);
        _settings.HighExplosiveChance = Math.Clamp(_settings.HighExplosiveChance, 0, 100);
        _settings.FlashChance = Math.Clamp(_settings.FlashChance, 0, 100);
        _settings.DecoyChance = Math.Clamp(_settings.DecoyChance, 0, 100);
    }

    private void PrintUsage(CCSPlayerController? player)
    {
        Reply(player, "用法: /randomnade <总概率> [烟] [火] [雷] [闪] [诱饵]");
        Reply(player, "关闭: /randomnade 0；未输入的概率保持当前值。");
        Reply(player, "默认: /randomnade 100 8 40 40 2 10");
        Reply(player, $"当前状态: {GetStatusInfo()}");
    }

    private void ReplyAllOrConsole(CCSPlayerController? player, string message)
    {
        if (player == null)
        {
            Console.WriteLine($"[CaorenCup] {message}");
        }
        else
        {
            CaorenCupUtils.PrintToChatAll(message);
        }
    }

    private static void Reply(CCSPlayerController? player, string message)
    {
        if (player == null) Console.WriteLine($"[CaorenCup] {message}");
        else CaorenCupUtils.PrintToChat(player, message);
    }

    private string FormatStatus(bool includeConfigured)
    {
        var configured = ReadWeights();
        var effective = RandomNadeRules.ResolveEffectiveWeights(configured);
        var effectiveText = $"总概率 {Format(_settings.TotalChance)}% | 实际分布 烟{Format(effective.Smoke)}% 火{Format(effective.Fire)}% 雷{Format(effective.HighExplosive)}% 闪{Format(effective.Flash)}% 诱饵{Format(effective.Decoy)}%";
        if (!includeConfigured) return effectiveText;

        return $"{effectiveText} | 输入值 烟{Format(configured.Smoke)} 火{Format(configured.Fire)} 雷{Format(configured.HighExplosive)} 闪{Format(configured.Flash)} 诱饵{Format(configured.Decoy)}";
    }

    private static string Format(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    public string GetHelpEntry() => "/randomnade - 开枪随机发射烟/火/雷/闪/诱饵";

    public string GetStatusInfo() => _settings.Enabled
        ? $"已开启 ({FormatStatus(includeConfigured: true)})"
        : $"已禁用 (保留配置: {FormatStatus(includeConfigured: true)})";

    public string? GetPublicConfigInfo() => _settings.Enabled
        ? $"开枪随机投掷物: {FormatStatus(includeConfigured: false)}"
        : null;

    public string GetFeatureDescription() => _settings.Enabled
        ? $"【火力投掷】每次枪械开火有 {Format(_settings.TotalChance)}% 概率沿准星方向发射一个随机投掷物。"
        : "开枪随机投掷物当前未启用。";

    private sealed record PendingProjectile(
        CBaseCSGrenadeProjectile Projectile,
        CHandle<CCSPlayerPawn> Thrower,
        RandomNadeRuntimePlan Plan,
        float SpawnTime);
}
