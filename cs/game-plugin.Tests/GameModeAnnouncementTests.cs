using CaorenCup.Contracts;
using CaorenCup.Core;
using CaorenCup.Features;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

[Collection("Game mode state")]
public sealed class GameModeAnnouncementTests
{
    [Fact]
    public void Mode_selection_warmup_live_and_stop_publish_once_from_the_real_runtime()
    {
        CaorenCupPracticeState.EnterSleep();
        var provider = new Provider("message-lifecycle");
        CaorenCupGameModeRegistry.Register(provider);
        var messages = new List<string>();
        Action? timer = null;
        var runtime = new GameModeRuntime(new ManagedCvarScope(), (_, callback) => timer = callback,
            _ => { }, () => "de_ancient", messages.Add);
        try
        {
            Assert.True(runtime.SelectMode(provider.Descriptor.Id).Success);
            Assert.Single(messages);
            Assert.Contains("尚未启动", messages[0]);
            Assert.True(runtime.Start().Success);
            Assert.Equal(2, messages.Count);
            Assert.Equal(GameModeLifecycleState.Warmup, runtime.State.Lifecycle);
            Assert.NotNull(timer);
            timer!();
            timer(); // 迟到的重复回调不能再次通知 Live。
            Assert.Equal(3, messages.Count);
            Assert.Equal(GameModeLifecycleState.Live, runtime.State.Lifecycle);
            Assert.True(runtime.Stop().Success);
            Assert.Equal(4, messages.Count);
            Assert.Contains("已停止", messages[^1]);
        }
        finally { CaorenCupGameModeRegistry.Unregister(provider); }
    }

    [Fact]
    public void Rejected_requests_do_not_publish_success_and_overrides_describe_deferred_application()
    {
        CaorenCupPracticeState.EnterSleep();
        var provider = new Provider("message-cvar", [new("mp_freezetime", GameModeCvarType.Integer, "15", "0", "60")]);
        CaorenCupGameModeRegistry.Register(provider);
        var messages = new List<string>();
        var runtime = new GameModeRuntime(new ManagedCvarScope(), (_, _) => { }, _ => { }, () => "de_ancient", messages.Add);
        try
        {
            Assert.False(runtime.Start().Success);
            Assert.False(runtime.SelectMode("does-not-exist").Success);
            Assert.Empty(messages);
            Assert.True(runtime.SelectMode(provider.Descriptor.Id).Success);
            messages.Clear();
            Assert.False(runtime.SetOverride("mp_freezetime", "61", false).Success);
            Assert.False(runtime.SetOverride("unknown", "1", false).Success);
            Assert.Empty(messages);
            Assert.True(runtime.SetOverride("mp_freezetime", "20", false).Success);
            Assert.Single(messages);
            Assert.Contains("mp_freezetime = 20", messages[0]);
            Assert.Contains("模式启动时应用", messages[0]);
        }
        finally { CaorenCupGameModeRegistry.Unregister(provider); }
    }

    [Fact]
    public void Warmup_failure_never_announces_success()
    {
        CaorenCupPracticeState.EnterSleep();
        var provider = new Provider("message-failure") { RejectWarmup = true };
        CaorenCupGameModeRegistry.Register(provider);
        var messages = new List<string>();
        var runtime = new GameModeRuntime(new ManagedCvarScope(), (_, _) => { }, _ => { }, () => "de_ancient", messages.Add);
        try
        {
            runtime.SelectMode(provider.Descriptor.Id);
            messages.Clear();
            Assert.False(runtime.Start().Success);
            Assert.Empty(messages);
            Assert.Equal(GameModeLifecycleState.Error, runtime.State.Lifecycle);
        }
        finally { CaorenCupGameModeRegistry.Unregister(provider); }
    }

    [Fact]
    public void Old_warmup_timer_cannot_start_a_replacement_match_early()
    {
        CaorenCupPracticeState.EnterSleep();
        var provider = new Provider("timer-generation");
        CaorenCupGameModeRegistry.Register(provider);
        var timers = new List<Action>();
        var runtime = new GameModeRuntime(new ManagedCvarScope(), (_, callback) => timers.Add(callback), _ => { }, () => "de_ancient", _ => { });
        try
        {
            runtime.SelectMode(provider.Descriptor.Id); runtime.Start(); runtime.Stop(); runtime.Start();
            timers[0](); Assert.Equal(GameModeLifecycleState.Warmup, runtime.State.Lifecycle);
            timers[1](); Assert.Equal(GameModeLifecycleState.Live, runtime.State.Lifecycle);
        }
        finally { CaorenCupGameModeRegistry.Unregister(provider); }
    }

    [Fact]
    public void Warmup_feature_reset_finishes_before_mode_scope_is_created_and_never_runs_late_from_console_queue()
    {
        CaorenCupPracticeState.EnterSleep();
        var provider = new Provider("reset-order") { ResetWarmup = true };
        CaorenCupGameModeRegistry.Register(provider);
        var queuedCommands = new List<string>();
        var resetCalls = 0;
        var runtime = new GameModeRuntime(new ManagedCvarScope(), (_, _) => { }, queuedCommands.Add,
            () => "de_ancient", _ => { }, () =>
            {
                resetCalls++;
                queuedCommands.Add("restore_previous_feature_values");
            });
        try
        {
            Assert.True(runtime.SelectMode(provider.Descriptor.Id).Success);
            Assert.True(runtime.Start().Success);
            Assert.Equal(1, resetCalls); // 尚未执行任何排队的控制台命令。
            Assert.DoesNotContain("css_reset_plu", queuedCommands);
            Assert.True(queuedCommands.IndexOf("restore_previous_feature_values") < queuedCommands.IndexOf("mp_warmup_start"));
            Assert.False(runtime.Start().Success);
            Assert.Equal(1, resetCalls);
            Assert.True(runtime.Stop().Success);
            Assert.True(runtime.Start().Success);
            Assert.Equal(2, resetCalls);
        }
        finally { CaorenCupGameModeRegistry.Unregister(provider); }
    }

    private sealed class Provider(string id, IReadOnlyList<GameModeCvarDefinition>? cvars = null) : ICaorenCupGameModeProvider
    {
        public bool RejectWarmup { get; init; }
        public bool ResetWarmup { get; init; }
        public GameModeDescriptor Descriptor { get; } = new(id, "测试模式", "Test", [], null, 15, cvars ?? []);
        public GameModeResult ValidateStart(GameModeStartRequest request, GameModeRuntimeContext context) => GameModeResult.Ok("ok");
        public GameModeResult OnWarmupStarted(GameModeRuntimeContext context)
        {
            if (ResetWarmup) context.ExecuteServerCommand("css_reset_plu");
            return RejectWarmup ? GameModeResult.Fail("资源未就绪") : GameModeResult.Ok("ok");
        }
        public GameModeResult OnLiveStarted(GameModeRuntimeContext context) => GameModeResult.Ok("ok");
        public GameModeResult OnStopped(GameModeRuntimeContext context) => GameModeResult.Ok("ok");
    }
}
