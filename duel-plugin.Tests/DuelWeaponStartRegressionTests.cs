using CaorenDuel;
using Xunit;

namespace CaorenDuel.Tests;

/// <summary>
/// 单挑发装后延迟回调的回归测试：回调必须绑定当前回合、生成号、连接身份、出生实体和队伍；
/// 默认持枪选择只允许发出一次，玩家主动切换后不会被 0.1 秒重试抢回。
/// </summary>
public sealed class DuelWeaponStartRegressionTests
{
    [Fact]
    public void Matching_round_callback_is_allowed()
    {
        Assert.True(DuelRuntimePolicy.CanApplyDuelLoadoutCallback(ValidContext()));
    }

    [Theory]
    [InlineData("unloading")]
    [InlineData("mode-disabled")]
    [InlineData("player-invalid")]
    [InlineData("dead")]
    [InlineData("generation")]
    [InlineData("round")]
    [InlineData("steamid")]
    [InlineData("pawn")]
    [InlineData("team")]
    [InlineData("reconnect")]
    public void Stale_or_invalid_loadout_callback_is_rejected(string scenario)
    {
        var context = ValidContext();
        context = scenario switch
        {
            "unloading" => context with { IsUnloading = true },
            "mode-disabled" => context with { DuelModeEnabled = false },
            "player-invalid" => context with { PlayerValid = false },
            "dead" => context with { PawnAlive = false },
            "generation" => context with { CurrentGeneration = context.ScheduledGeneration + 1 },
            "round" => context with { CurrentRound = context.ScheduledRound + 1 },
            "steamid" => context with { CurrentSteamId = context.ScheduledSteamId + 1 },
            "pawn" => context with { CurrentPawnHandle = context.ScheduledPawnHandle + 1 },
            "team" => context with { CurrentTeam = context.ScheduledTeam + 1 },
            "reconnect" => context with { CurrentControllerHandle = context.ScheduledControllerHandle + 32768 },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        Assert.False(DuelRuntimePolicy.CanApplyDuelLoadoutCallback(context));
    }

    [Fact]
    public void Preferred_weapon_selection_is_issued_only_once_per_loadout()
    {
        var rule = DuelRuntimePolicy.BuildLoadoutRule(DuelStage.Rifle, "weapon_ak47", "weapon_usp_silencer");
        var selection = new DuelPreferredWeaponSelection();

        // 发装后引擎停在刀上，本帧选择主武器。
        Assert.True(selection.TryIssue("weapon_knife", rule));

        // 初始化结束后玩家主动切刀；即使还有延迟回调，也不能再次发出选择。
        Assert.False(selection.TryIssue("weapon_knife", rule));
        Assert.True(selection.Issued);
    }

    [Fact]
    public void Preferred_weapon_selection_does_not_disturb_an_already_correct_active_weapon()
    {
        var rule = DuelRuntimePolicy.BuildLoadoutRule(DuelStage.Pistol, string.Empty, "weapon_usp_silencer");
        var selection = new DuelPreferredWeaponSelection();

        Assert.False(selection.TryIssue("weapon_usp_silencer", rule));
        Assert.False(selection.TryIssue("weapon_knife", rule));
    }

    private static DuelLoadoutCallbackContext ValidContext() => new(
        ScheduledGeneration: 3,
        CurrentGeneration: 3,
        ScheduledRound: 7,
        CurrentRound: 7,
        IsUnloading: false,
        DuelModeEnabled: true,
        PlayerValid: true,
        PawnAlive: true,
        ScheduledSteamId: 76561198000000001,
        CurrentSteamId: 76561198000000001,
        ScheduledPawnHandle: 42,
        CurrentPawnHandle: 42,
        ScheduledTeam: 2,
        CurrentTeam: 2,
        ScheduledControllerHandle: 32769,
        CurrentControllerHandle: 32769);

    [Fact]
    public void Initial_loadout_captures_identity_before_delay_and_validates_before_removing_weapons()
    {
        var source = ReadSource();
        var start = source.IndexOf("private void ApplyDuelLoadoutsForRound()", StringComparison.Ordinal);
        var end = source.IndexOf("private void ApplyDuelLoadout(", start, StringComparison.Ordinal);
        var schedule = source[start..end];
        Assert.True(schedule.IndexOf("new DuelLoadoutCallback(", StringComparison.Ordinal) <
                    schedule.IndexOf("AddTimer(0.2f", StringComparison.Ordinal));
        Assert.Contains("player.PlayerPawn.Raw", schedule);
        Assert.Contains("player.EntityHandle.Raw", schedule);
        var apply = source[end..source.IndexOf("private CCSPlayerController? FindDuelPlayer(", end, StringComparison.Ordinal)];
        Assert.True(apply.IndexOf("CanApplyDuelLoadoutContinuation(player, callback)", StringComparison.Ordinal) <
                    apply.IndexOf("player.RemoveWeapons()", StringComparison.Ordinal));
        var roundEnd = source[source.IndexOf("public HookResult OnRoundEnd(", StringComparison.Ordinal)..];
        Assert.True(roundEnd.IndexOf("_duelLoadoutGeneration++", StringComparison.Ordinal) <
                    roundEnd.IndexOf("_duelSession.ControlMode", StringComparison.Ordinal));
    }

    private static string ReadSource()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var file = Path.Combine(dir.FullName, "duel-plugin", "CaorenDuelPlugin.cs");
            if (File.Exists(file)) return File.ReadAllText(file);
        }
        throw new FileNotFoundException("CaorenDuelPlugin.cs");
    }
}
