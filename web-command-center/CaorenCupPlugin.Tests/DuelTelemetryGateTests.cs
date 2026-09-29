using CaorenCupPlugin;
using System.Reflection;
using Xunit;

namespace CaorenCupPlugin.Tests;

/// <summary>
/// 单挑拆分为独立插件后，留在桥接侧的遥测隔离/网页命令门控测试。
/// （原 DuelGameSessionTests 中的桥接锚定用例；单挑会话测试已随迁 CaorenDuel.Tests。）
/// </summary>
public sealed class DuelTelemetryGateTests
{
    [Fact]
    public void Active_duel_isolation_disables_match_telemetry()
    {
        var pluginType = typeof(global::CaorenCupPlugin.CaorenCupPlugin);
        var publishMethod = pluginType
            .GetMethod("ShouldPublishMatchTelemetry", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(publishMethod);
        var plugin = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(pluginType);
        var isolationField = pluginType.GetField("_duelTelemetryIsolation", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(isolationField);

        var isolation = new DuelTelemetryIsolationState();
        isolationField!.SetValue(plugin, isolation);

        Assert.True(Assert.IsType<bool>(publishMethod!.Invoke(plugin, null)));

        isolation.Begin("web-match-before-duel");

        Assert.False(Assert.IsType<bool>(publishMethod.Invoke(plugin, null)));
    }

    [Fact]
    public void Active_duel_isolation_rejects_web_team_assignment_updates()
    {
        var method = GetPrivateStaticMethod("ShouldApplyWebTeamAssignments");

        Assert.False(InvokePrivateBool(method, true));
        Assert.True(InvokePrivateBool(method, false));
    }

    [Fact]
    public void Game_managed_running_or_paused_blocks_web_match_control_at_execution_time()
    {
        var dispatcherType = typeof(global::CaorenCupPlugin.CaorenCupPlugin).Assembly
            .GetType("CaorenCupPlugin.WebCommandGameThreadDispatcher");
        Assert.NotNull(dispatcherType);
        var method = dispatcherType!.GetMethod(
            "IsGameManagedMatchControlCommand",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(method);

        Assert.True(InvokePrivateBool(method!, "changelevel de_dust2", DuelControlMode.GameManaged));
        Assert.True(InvokePrivateBool(method!, " HOST_WORKSHOP_MAP 123 ", DuelControlMode.GameManaged));
        Assert.True(InvokePrivateBool(method!, "mp_restartgame 1", DuelControlMode.GameManaged));
        Assert.True(InvokePrivateBool(method!, "sv_showimpacts_time 4", DuelControlMode.GameManaged));
        Assert.False(InvokePrivateBool(method!, "changelevel de_dust2", DuelControlMode.None));
    }

    [Fact]
    public void Active_duel_isolation_keeps_local_match_counters_from_web_responses()
    {
        var method = GetPrivateStaticMethod("ShouldApplyWebMatchCounters");

        Assert.False(InvokePrivateBool(method, true));
        Assert.True(InvokePrivateBool(method, false));
    }

    [Fact]
    public void Unloading_blocks_plugin_continuations()
    {
        var method = GetPrivateStaticMethod("ShouldProcessPluginContinuation");

        Assert.False(InvokePrivateBool(method, true));
        Assert.True(InvokePrivateBool(method, false));
    }

    private static MethodInfo GetPrivateStaticMethod(string name)
    {
        var method = typeof(global::CaorenCupPlugin.CaorenCupPlugin)
            .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method;
    }

    private static bool InvokePrivateBool(MethodInfo method, params object[] arguments) =>
        Assert.IsType<bool>(method.Invoke(null, arguments));
}
