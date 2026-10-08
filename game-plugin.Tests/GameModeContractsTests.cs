using CaorenCup.Contracts;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class GameModeContractsTests
{
    [Fact]
    public void PracticeState_requires_sleep_before_formal_modes()
    {
        CaorenCupPracticeState.EnterPractice();
        Assert.True(CaorenCupPracticeState.IsPracticeActive);
        CaorenCupPracticeState.EnterSleep();
        Assert.False(CaorenCupPracticeState.IsPracticeActive);
    }

    [Fact]
    public void CvarValidatorRejectsUnknownAndOutOfRangeValues()
    {
        var descriptor = new GameModeDescriptor(
            "test",
            "Test",
            "Test",
            Array.Empty<string>(),
            null,
            15,
            new[]
            {
                new GameModeCvarDefinition("mp_freezetime", GameModeCvarType.Integer, "15", "0", "60"),
            });

        Assert.False(GameModeCvarValidator.Validate(
            descriptor,
            new Dictionary<string, string> { ["unknown"] = "1" },
            webSource: true).Success);
        Assert.False(GameModeCvarValidator.Validate(
            descriptor,
            new Dictionary<string, string> { ["mp_freezetime"] = "61" },
            webSource: true).Success);
        Assert.True(GameModeCvarValidator.Validate(
            descriptor,
            new Dictionary<string, string> { ["mp_freezetime"] = "30" },
            webSource: true).Success);
    }

    [Fact]
    public void RegistryRejectsDuplicateModeIdsFromDifferentProviders()
    {
        var first = new TestProvider("test-registry");
        var second = new TestProvider("test-registry");
        CaorenCupGameModeRegistry.Register(first);
        try
        {
            Assert.Throws<InvalidOperationException>(() => CaorenCupGameModeRegistry.Register(second));
        }
        finally
        {
            CaorenCupGameModeRegistry.Unregister(first);
            CaorenCupGameModeRegistry.Unregister(second);
        }
    }

    private sealed class TestProvider(string id) : ICaorenCupGameModeProvider
    {
        public GameModeDescriptor Descriptor { get; } = new(
            id,
            "Test",
            "Test",
            Array.Empty<string>(),
            null,
            15,
            Array.Empty<GameModeCvarDefinition>());

        public GameModeResult ValidateStart(GameModeStartRequest request, GameModeRuntimeContext context) =>
            GameModeResult.Ok("ok");

        public GameModeResult OnWarmupStarted(GameModeRuntimeContext context) => GameModeResult.Ok("ok");
        public GameModeResult OnLiveStarted(GameModeRuntimeContext context) => GameModeResult.Ok("ok");
        public GameModeResult OnStopped(GameModeRuntimeContext context) => GameModeResult.Ok("ok");
    }
}
