using CaorenDuel;
using System.Reflection;
using Xunit;

namespace CaorenDuel.Tests;

/// <summary>
/// 从桥接插件 FinalReviewFixTests 摘出的单挑侧源码结构与行为测试（随单挑插件迁移）。
/// </summary>
public sealed class DuelPluginSourceStructureTests
{
    [Fact]
    public void Failed_cvar_restore_remains_pending_and_retry_skips_already_restored_entries()
    {
        var constructor = typeof(DuelServerCvarScope).GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            [typeof(Func<string, string, string>), typeof(Action<string>)],
            modifiers: null);
        Assert.NotNull(constructor);

        var executedCommands = new List<string>();
        var failMaxRoundsRestoreOnce = true;
        Action<string> execute = serverCommand =>
        {
            executedCommands.Add(serverCommand);
            if (serverCommand == "mp_maxrounds 24" && failMaxRoundsRestoreOnce)
            {
                failMaxRoundsRestoreOnce = false;
                throw new InvalidOperationException("transient restore failure");
            }
        };
        var scope = Assert.IsType<DuelServerCvarScope>(constructor!.Invoke(
            [
                (Func<string, string, string>)((name, fallback) => name == "mp_maxrounds" ? "24" : "0"),
                execute
            ]));
        scope.Set("mp_maxrounds", "36", "24");
        scope.Set("mp_winlimit", "1", "0");

        Assert.Throws<AggregateException>(scope.RestoreAll);

        Assert.Equal(1, ReadInt(scope, "PendingRestoreCount"));
        Assert.Contains("mp_maxrounds", ReadStringCollection(scope, "PendingRestoreNames"));
        Assert.DoesNotContain("mp_winlimit", ReadStringCollection(scope, "PendingRestoreNames"));

        scope.RestoreAll();

        Assert.Equal(0, ReadInt(scope, "PendingRestoreCount"));
        Assert.Equal(2, executedCommands.Count(command => command == "mp_maxrounds 24"));
        Assert.Equal(1, executedCommands.Count(command => command == "mp_winlimit 0"));
    }

    [Fact]
    public void Duel_runtime_cvars_are_applied_and_restored_as_one_scope()
    {
        var executed = new List<string>();
        var scope = CreateCvarScope((name, fallback) => name switch
        {
            "mp_weapons_allow_map_placed" => "1",
            "mp_death_drop_gun" => "1",
            "mp_maxrounds" => "24",
            "sv_showimpacts" => "1",
            "sv_showimpacts_time" => "4",
            "mp_endmatch_votenextmap" => "1",
            "mp_match_end_restart" => "0",
            _ => fallback
        }, executed.Add);

        scope.Apply(DuelRuntimePolicy.BuildCvarPlan(new DuelGameConfig()));
        Assert.Contains("mp_maxrounds 36", executed);
        Assert.Contains("mp_weapons_allow_map_placed 0", executed);
        Assert.Contains("mp_death_drop_gun 0", executed);
        Assert.Contains("sv_showimpacts 0", executed);
        Assert.Contains("sv_showimpacts_time 0", executed);
        Assert.Contains("mp_endmatch_votenextmap 0", executed);
        Assert.Contains("mp_match_end_restart 1", executed);

        scope.RestoreAll();
        Assert.Contains("mp_maxrounds 24", executed);
        Assert.Contains("mp_weapons_allow_map_placed 1", executed);
        Assert.Contains("mp_death_drop_gun 1", executed);
        Assert.Contains("sv_showimpacts 1", executed);
        Assert.Contains("sv_showimpacts_time 4", executed);
        Assert.Contains("mp_endmatch_votenextmap 1", executed);
        Assert.Contains("mp_match_end_restart 0", executed);
    }

    [Fact]
    public void Duel_runtime_uses_the_game_managed_activation_path()
    {
        var source = ReadPluginSource();
        Assert.Contains("private void ActivateDuelRuntime(DuelGameConfig config)", source);
        Assert.DoesNotContain("DuelRuntimePolicy.BuildWebManagedCvarPlan(config)", source);
        Assert.Contains("DuelRuntimePolicy.BuildCvarPlan(config)", source);
        Assert.DoesNotContain("ReadPayloadDouble(payload, \"roundTimeMinutes\", 1)", source);
        Assert.DoesNotContain("_duelSession.EnterWebManaged(config);", source);
        Assert.DoesNotContain(
            "_duelServerCvars.Set(\"mp_maxrounds\", config.TotalRounds.ToString()",
            source);
    }

    [Fact]
    public void Duel_weapon_rules_avoid_direct_entity_removal_and_use_delayed_retry()
    {
        var source = ReadPluginSource();
        Assert.Contains("AddCommandListener(\"drop\", OnDuelDropCommand, HookMode.Pre)", source);
        Assert.DoesNotContain("RemoveUnexpectedDuelFirearms", source);
        Assert.DoesNotContain("weapon.Remove()", source);
        Assert.Contains("QueuePreferredDuelWeapon(player, plan.Rule)", source);
        Assert.Contains("AddTimer(0.2f", source);
        Assert.Contains("FindDuelPlayer(plan.SteamId)", source);
        Assert.Contains("AddTimer(0.1f", source);
        Assert.Contains("allowRetry: false", source);
    }

    [Fact]
    public void Duel_kevlar_only_marks_networked_armor_state_changed()
    {
        var source = ReadPluginSource();
        var giveKevlar = SliceSource(
            source,
            "private static void GivePlayerKevlar(",
            "private static bool IsDuelSniperWeapon(");
        Assert.Contains("ItemServices?.As<CCSPlayer_ItemServices>()", giveKevlar);
        Assert.Contains("itemServices.HasHelmet = false", giveKevlar);
        Assert.Contains("player.PawnHasHelmet = false", giveKevlar);
        Assert.Contains("Utilities.SetStateChanged(player, \"CCSPlayerController\", \"m_bPawnHasHelmet\")", giveKevlar);
        Assert.Contains("Utilities.SetStateChanged(pawn, \"CCSPlayerPawn\", \"m_ArmorValue\")", giveKevlar);
        Assert.DoesNotContain("m_bHasHelmet", giveKevlar);
    }

    [Fact]
    public void Duel_final_round_waits_for_native_same_map_restart_then_cleans_up()
    {
        var source = ReadPluginSource();
        var roundEnd = SliceSource(source, "public HookResult OnRoundEnd(", "private void FinishGameManagedDuel(");
        Assert.Contains("FinishGameManagedDuel(duelResult)", roundEnd);
        Assert.DoesNotContain("QueueEvent", roundEnd);
        Assert.DoesNotContain("RestoreGameManagedDuelCvarsWithRetry", roundEnd);

        var finishGameManaged = SliceSource(
            source,
            "private void FinishGameManagedDuel(",
            "private void AbortGameManagedDuel(");
        Assert.Contains(
            "BeginDuelCleanup(DuelControlMode.GameManaged, waitForEngineRestart: true)",
            finishGameManaged);

        var beginCleanup = SliceSource(source, "private void BeginDuelCleanup(", "private void CompleteDuelCleanupAfterRestart(");
        Assert.Contains("if (waitForEngineRestart) return;", beginCleanup);
        Assert.Contains("Server.ExecuteCommand(\"mp_restartgame 1\")", beginCleanup);
        Assert.DoesNotContain("RestoreGameManagedDuelCvarsWithRetry", beginCleanup);

        var roundStart = SliceSource(source, "public HookResult OnRoundStart(", "public HookResult OnRoundEnd(");
        Assert.True(
            roundStart.IndexOf("CompleteDuelCleanupAfterRestart(cleanupMode)", StringComparison.Ordinal) <
            roundStart.IndexOf("MarkRoundStarted()", StringComparison.Ordinal));

        var completeCleanup = SliceSource(
            source,
            "private void CompleteDuelCleanupAfterRestart(",
            "private void CleanupDuelImmediately(");
        Assert.Contains("RestoreGameManagedDuelCvarsWithRetry()", completeCleanup);
        Assert.True(
            completeCleanup.IndexOf("RestoreGameManagedDuelCvarsWithRetry()", StringComparison.Ordinal) <
            completeCleanup.IndexOf("NotifyDuelBridgeRoundStart()", StringComparison.Ordinal));

        var unload = SliceSource(source, "public override void Unload(", "private void StopTimers(");
        Assert.Contains("CleanupDuelImmediately()", unload);
        var mapStart = SliceSource(source, "private void OnMapStart(", "private void ShowDuelMapHelp(");
        Assert.Contains("immediateRestore: true", mapStart);
    }

    [Fact]
    public void Cvar_cleanup_retry_is_bounded_and_keeps_unresolved_entries_pending()
    {
        var constructor = typeof(DuelServerCvarScope).GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            [typeof(Func<string, string, string>), typeof(Action<string>)],
            modifiers: null);
        Assert.NotNull(constructor);

        var restoreAttempts = 0;
        Action<string> execute = serverCommand =>
        {
            if (serverCommand == "mp_maxrounds 24")
            {
                restoreAttempts++;
                throw new InvalidOperationException("persistent restore failure");
            }
        };
        var scope = Assert.IsType<DuelServerCvarScope>(constructor!.Invoke(
            [
                (Func<string, string, string>)((_, _) => "24"),
                execute
            ]));
        scope.Set("mp_maxrounds", "36", "24");
        var failures = new List<AggregateException>();
        var retryMethod = typeof(DuelServerCvarScope).GetMethod("TryRestoreAll");
        Assert.NotNull(retryMethod);

        var restored = Assert.IsType<bool>(retryMethod!.Invoke(
            scope,
            [3, (Action<AggregateException>)(failure => failures.Add(failure))]));

        Assert.False(restored);
        Assert.Equal(3, restoreAttempts);
        Assert.Equal(3, failures.Count);
        Assert.Equal(1, ReadInt(scope, "PendingRestoreCount"));
    }

    [Fact]
    public void Periodic_safe_point_retries_cvars_after_bounded_cleanup_attempts()
    {
        var constructor = typeof(DuelServerCvarScope).GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            [typeof(Func<string, string, string>), typeof(Action<string>)],
            modifiers: null);
        Assert.NotNull(constructor);

        var restoreAttempts = 0;
        Action<string> execute = serverCommand =>
        {
            if (serverCommand != "mp_maxrounds 24") return;
            restoreAttempts++;
            if (restoreAttempts <= 3)
            {
                throw new InvalidOperationException("cleanup-frame restore failure");
            }
        };
        var scope = Assert.IsType<DuelServerCvarScope>(constructor!.Invoke(
            [
                (Func<string, string, string>)((_, _) => "24"),
                execute
            ]));
        scope.Set("mp_maxrounds", "36", "24");

        Assert.False(scope.TryRestoreAll(3));
        Assert.False(scope.IsReadyForNewDuel);

        Assert.True(scope.RetryPendingAtSafePoint());

        Assert.True(scope.IsReadyForNewDuel);
        Assert.Equal(4, restoreAttempts);
        Assert.Equal(0, scope.PendingRestoreCount);
    }

    [Fact]
    public void Active_duel_cvar_scope_is_not_treated_as_cleanup_restore_work()
    {
        var method = typeof(global::CaorenDuel.CaorenDuelPlugin).GetMethod(
            "ShouldRetryPendingDuelCvars",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        Assert.False(Assert.IsType<bool>(method!.Invoke(null, [true, true])));
        Assert.True(Assert.IsType<bool>(method.Invoke(null, [false, true])));
        Assert.False(Assert.IsType<bool>(method.Invoke(null, [false, false])));
    }

    [Fact]
    public void New_duel_remains_blocked_until_every_pending_cvar_is_restored()
    {
        var constructor = typeof(DuelServerCvarScope).GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            [typeof(Func<string, string, string>), typeof(Action<string>)],
            modifiers: null);
        Assert.NotNull(constructor);

        var restoreMaySucceed = false;
        Action<string> execute = serverCommand =>
        {
            if (serverCommand == "mp_maxrounds 24" && !restoreMaySucceed)
            {
                throw new InvalidOperationException("restore still unavailable");
            }
        };
        var scope = Assert.IsType<DuelServerCvarScope>(constructor!.Invoke(
            [
                (Func<string, string, string>)((_, _) => "24"),
                execute
            ]));
        scope.Set("mp_maxrounds", "36", "24");
        Assert.False(scope.TryRestoreAll(3));

        Assert.False(scope.IsReadyForNewDuel);
        Assert.False(scope.RetryPendingAtSafePoint());
        Assert.False(scope.IsReadyForNewDuel);

        restoreMaySucceed = true;
        Assert.True(scope.RetryPendingAtSafePoint());
        Assert.True(scope.IsReadyForNewDuel);
    }

    [Fact]
    public void Pending_cvar_restore_blocks_game_admin_map_change()
    {
        var method = typeof(global::CaorenDuel.CaorenDuelPlugin).GetMethod(
            "IsDuelMapChangeBlocked",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        Assert.True(Assert.IsType<bool>(method!.Invoke(
            null,
            [DuelControlMode.None, DuelLifecycle.Idle, true, false])));
        Assert.True(Assert.IsType<bool>(method.Invoke(
            null,
            [DuelControlMode.GameManaged, DuelLifecycle.Running, false, false])));
        Assert.False(Assert.IsType<bool>(method.Invoke(
            null,
            [DuelControlMode.None, DuelLifecycle.Idle, false, false])));
    }

    [Fact]
    public void Cleanup_pending_blocks_new_game_admin_start()
    {
        var method = typeof(global::CaorenDuel.CaorenDuelPlugin).GetMethod(
            "IsDuelStartBlocked",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        Assert.True(Assert.IsType<bool>(method!.Invoke(null, [true, true])));
        Assert.True(Assert.IsType<bool>(method.Invoke(null, [false, false])));
        Assert.False(Assert.IsType<bool>(method.Invoke(null, [false, true])));
    }

    [Fact]
    public void Cleanup_pending_blocks_game_admin_map_change()
    {
        var method = typeof(global::CaorenDuel.CaorenDuelPlugin).GetMethod(
            "IsDuelMapChangeBlocked",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        Assert.True(Assert.IsType<bool>(method!.Invoke(
            null,
            [DuelControlMode.None, DuelLifecycle.Idle, false, true])));
        Assert.False(Assert.IsType<bool>(method.Invoke(
            null,
            [DuelControlMode.None, DuelLifecycle.Idle, false, false])));
    }

    [Fact]
    public void Game_managed_status_includes_participants_stage_and_remaining_rounds()
    {
        var session = new DuelGameSession(new DuelGameConfig(1, 1, 28, 1, "none"));
        Assert.True(session.TryStart(
            [
                new DuelParticipant("t1", "T甲", DuelTeam.Terrorist),
                new DuelParticipant("t2", "T乙", DuelTeam.Terrorist),
                new DuelParticipant("ct1", "CT丙", DuelTeam.CounterTerrorist)
            ],
            false,
            out _));
        session.MarkRoundStarted();
        session.RecordRoundEnd(DuelTeam.Terrorist);
        var method = typeof(global::CaorenDuel.CaorenDuelPlugin).GetMethod(
            "BuildGameManagedDuelStatusLines",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var lines = Assert.IsAssignableFrom<IReadOnlyCollection<string>>(method!.Invoke(null, [session]));
        var output = string.Join("\n", lines);

        Assert.Contains("T 参赛者（2）：T甲、T乙", output);
        Assert.Contains("CT 参赛者（1）：CT丙", output);
        Assert.Contains("当前阶段：步枪", output);
        Assert.Contains("剩余 29 回合", output);
    }

    [Fact]
    public void Admin_help_states_the_recommended_setup_order_without_web_takeover()
    {
        var method = typeof(global::CaorenDuel.CaorenDuelPlugin).GetMethod(
            "BuildDuelAdminHelpLines",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var lines = Assert.IsAssignableFrom<IReadOnlyCollection<string>>(method!.Invoke(null, null));
        var output = string.Join("\n", lines);

        var mapIndex = output.IndexOf("先切换地图", StringComparison.Ordinal);
        var reconnectIndex = output.IndexOf("等待玩家重连并选择 T/CT", StringComparison.Ordinal);
        var configureIndex = output.IndexOf("再配置", StringComparison.Ordinal);
        var startIndex = output.IndexOf("最后 /duel start", StringComparison.Ordinal);
        Assert.True(mapIndex >= 0);
        Assert.True(reconnectIndex > mapIndex);
        Assert.True(configureIndex > reconnectIndex);
        Assert.True(startIndex > configureIndex);
        Assert.DoesNotContain("/duel start confirm", output);
        Assert.DoesNotContain("替换现有网页管理状态", output);
    }

    private static DuelServerCvarScope CreateCvarScope(
        Func<string, string, string> read,
        Action<string> execute)
    {
        var constructor = typeof(DuelServerCvarScope).GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            [typeof(Func<string, string, string>), typeof(Action<string>)],
            modifiers: null);
        Assert.NotNull(constructor);
        return Assert.IsType<DuelServerCvarScope>(constructor!.Invoke([read, execute]));
    }

    private static string ReadPluginSource()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "duel-plugin",
                "CaorenDuelPlugin.cs");
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }

        throw new FileNotFoundException(
            "Could not locate CaorenDuelPlugin.cs from the test output directory.");
    }

    private static string SliceSource(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing source marker: {startMarker}");
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing source marker after {startMarker}: {endMarker}");
        return source[start..end];
    }

    private static int ReadInt(object target, string propertyName)
    {
        var property = target.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        return Assert.IsType<int>(property.GetValue(target));
    }

    private static IReadOnlyCollection<string> ReadStringCollection(object target, string propertyName)
    {
        var property = target.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        return Assert.IsAssignableFrom<IReadOnlyCollection<string>>(property.GetValue(target));
    }
}
