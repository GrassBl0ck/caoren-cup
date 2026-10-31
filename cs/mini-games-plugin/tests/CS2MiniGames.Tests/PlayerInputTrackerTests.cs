using CounterStrikeSharp.API;
using CS2MiniGames.Framework;

namespace CS2MiniGames.Tests;

public sealed class PlayerInputTrackerTests
{
    public static TheoryData<PlayerButtons, MiniGameAction> ButtonMappings =>
        new()
        {
            { PlayerButtons.Moveleft, MiniGameAction.MoveLeft },
            { PlayerButtons.Moveright, MiniGameAction.MoveRight },
            { PlayerButtons.Back, MiniGameAction.SoftDrop },
            { PlayerButtons.Jump, MiniGameAction.HardDrop },
            { PlayerButtons.Use, MiniGameAction.RotateClockwise },
            { PlayerButtons.Reload, MiniGameAction.RotateCounterClockwise },
            { PlayerButtons.Forward, MiniGameAction.Hold },
            { PlayerButtons.Scoreboard, MiniGameAction.Exit }
        };

    [Theory]
    [MemberData(nameof(ButtonMappings))]
    public void MapsEachCounterStrikeButtonToItsExactAction(
        PlayerButtons button,
        MiniGameAction expected)
    {
        var tracker = new PlayerInputTracker();

        var actions = tracker.Read(button, TimeSpan.Zero);

        Assert.Equal([expected], actions);
    }

    [Theory]
    [InlineData(PlayerButtons.Jump, MiniGameAction.HardDrop)]
    [InlineData(PlayerButtons.Use, MiniGameAction.RotateClockwise)]
    [InlineData(PlayerButtons.Reload, MiniGameAction.RotateCounterClockwise)]
    [InlineData(PlayerButtons.Forward, MiniGameAction.Hold)]
    [InlineData(PlayerButtons.Scoreboard, MiniGameAction.Exit)]
    public void EdgeActionFiresOnceUntilTheButtonIsReleased(
        PlayerButtons button,
        MiniGameAction expected)
    {
        var tracker = new PlayerInputTracker();

        Assert.Equal([expected], tracker.Read(button, TimeSpan.Zero));
        Assert.Empty(tracker.Read(button, TimeSpan.FromSeconds(1)));
        Assert.Empty(tracker.Read(default, TimeSpan.FromSeconds(2)));
        Assert.Equal([expected], tracker.Read(button, TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void ReloadOnlyProducesCounterClockwiseRotation()
    {
        var tracker = new PlayerInputTracker();

        var actions = tracker.Read(PlayerButtons.Reload, TimeSpan.Zero);

        Assert.Equal([MiniGameAction.RotateCounterClockwise], actions);
        Assert.DoesNotContain(MiniGameAction.RotateClockwise, actions);
    }

    [Theory]
    [InlineData(PlayerButtons.Moveleft, MiniGameAction.MoveLeft)]
    [InlineData(PlayerButtons.Moveright, MiniGameAction.MoveRight)]
    public void HorizontalMovementRepeatsAfterOneHundredFiftyMillisecondsThenEveryFifty(
        PlayerButtons button,
        MiniGameAction expected)
    {
        var tracker = new PlayerInputTracker();

        Assert.Equal([expected], tracker.Read(button, TimeSpan.Zero));
        Assert.Empty(tracker.Read(button, TimeSpan.FromMilliseconds(149)));
        Assert.Equal([expected], tracker.Read(button, TimeSpan.FromMilliseconds(150)));
        Assert.Empty(tracker.Read(button, TimeSpan.FromMilliseconds(199)));
        Assert.Equal([expected], tracker.Read(button, TimeSpan.FromMilliseconds(200)));
        Assert.Equal([expected], tracker.Read(button, TimeSpan.FromMilliseconds(250)));
    }

    [Fact]
    public void SoftDropFiresImmediatelyAndEveryFiftyMilliseconds()
    {
        var tracker = new PlayerInputTracker();

        Assert.Equal([MiniGameAction.SoftDrop], tracker.Read(PlayerButtons.Back, TimeSpan.Zero));
        Assert.Empty(tracker.Read(PlayerButtons.Back, TimeSpan.FromMilliseconds(49)));
        Assert.Equal(
            [MiniGameAction.SoftDrop],
            tracker.Read(PlayerButtons.Back, TimeSpan.FromMilliseconds(50)));
        Assert.Equal(
            [MiniGameAction.SoftDrop],
            tracker.Read(PlayerButtons.Back, TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public void OppositeHorizontalDirectionsNeverProduceHorizontalActionsTogether()
    {
        var tracker = new PlayerInputTracker();
        var both = PlayerButtons.Moveleft | PlayerButtons.Moveright;

        Assert.Empty(tracker.Read(both, TimeSpan.Zero));
        Assert.Empty(tracker.Read(both, TimeSpan.FromMilliseconds(150)));
        Assert.Empty(tracker.Read(both, TimeSpan.FromMilliseconds(200)));
    }

    [Theory]
    [InlineData(PlayerButtons.Moveleft, MiniGameAction.MoveLeft)]
    [InlineData(PlayerButtons.Moveright, MiniGameAction.MoveRight)]
    public void ReleasingOneConflictingDirectionActivatesTheRemainingDirectionImmediately(
        PlayerButtons remaining,
        MiniGameAction expected)
    {
        var tracker = new PlayerInputTracker();
        var both = PlayerButtons.Moveleft | PlayerButtons.Moveright;

        Assert.Empty(tracker.Read(both, TimeSpan.Zero));
        Assert.Empty(tracker.Read(both, TimeSpan.FromMilliseconds(99)));
        Assert.Equal([expected], tracker.Read(remaining, TimeSpan.FromMilliseconds(100)));
        Assert.Empty(tracker.Read(remaining, TimeSpan.FromMilliseconds(249)));
        Assert.Equal([expected], tracker.Read(remaining, TimeSpan.FromMilliseconds(250)));
        Assert.Empty(tracker.Read(remaining, TimeSpan.FromMilliseconds(299)));
        Assert.Equal([expected], tracker.Read(remaining, TimeSpan.FromMilliseconds(300)));
    }

    [Theory]
    [InlineData(PlayerButtons.Moveleft, MiniGameAction.MoveLeft, 150)]
    [InlineData(PlayerButtons.Moveright, MiniGameAction.MoveRight, 150)]
    [InlineData(PlayerButtons.Back, MiniGameAction.SoftDrop, 50)]
    public void ReleasingAndRepressingARepeatingButtonFiresImmediatelyAndRestartsItsDelay(
        PlayerButtons button,
        MiniGameAction expected,
        int repeatDelayMilliseconds)
    {
        var tracker = new PlayerInputTracker();

        Assert.Equal([expected], tracker.Read(button, TimeSpan.Zero));
        Assert.Empty(tracker.Read(default, TimeSpan.FromMilliseconds(10)));
        Assert.Equal([expected], tracker.Read(button, TimeSpan.FromMilliseconds(20)));
        Assert.Empty(
            tracker.Read(
                button,
                TimeSpan.FromMilliseconds(19 + repeatDelayMilliseconds)));
        Assert.Equal(
            [expected],
            tracker.Read(
                button,
                TimeSpan.FromMilliseconds(20 + repeatDelayMilliseconds)));
    }

    [Fact]
    public void SwitchingBetweenHorizontalDirectionsRestartsEachDirectionsRepeatDelay()
    {
        var tracker = new PlayerInputTracker();

        Assert.Equal(
            [MiniGameAction.MoveLeft],
            tracker.Read(PlayerButtons.Moveleft, TimeSpan.Zero));
        Assert.Equal(
            [MiniGameAction.MoveRight],
            tracker.Read(PlayerButtons.Moveright, TimeSpan.FromMilliseconds(100)));
        Assert.Empty(
            tracker.Read(PlayerButtons.Moveright, TimeSpan.FromMilliseconds(249)));
        Assert.Equal(
            [MiniGameAction.MoveRight],
            tracker.Read(PlayerButtons.Moveright, TimeSpan.FromMilliseconds(250)));
        Assert.Equal(
            [MiniGameAction.MoveLeft],
            tracker.Read(PlayerButtons.Moveleft, TimeSpan.FromMilliseconds(251)));
        Assert.Empty(
            tracker.Read(PlayerButtons.Moveleft, TimeSpan.FromMilliseconds(400)));
        Assert.Equal(
            [MiniGameAction.MoveLeft],
            tracker.Read(PlayerButtons.Moveleft, TimeSpan.FromMilliseconds(401)));
    }

    [Fact]
    public void HorizontalAndSoftDropRepeatTimersAreIndependent()
    {
        var tracker = new PlayerInputTracker();
        var held = PlayerButtons.Moveleft | PlayerButtons.Back;

        Assert.Equal(
            [MiniGameAction.MoveLeft, MiniGameAction.SoftDrop],
            tracker.Read(held, TimeSpan.Zero));
        Assert.Equal(
            [MiniGameAction.SoftDrop],
            tracker.Read(held, TimeSpan.FromMilliseconds(50)));
        Assert.Equal(
            [MiniGameAction.SoftDrop],
            tracker.Read(held, TimeSpan.FromMilliseconds(100)));
        Assert.Equal(
            [MiniGameAction.MoveLeft, MiniGameAction.SoftDrop],
            tracker.Read(held, TimeSpan.FromMilliseconds(150)));
    }

    [Theory]
    [InlineData(PlayerButtons.Moveleft, MiniGameAction.MoveLeft)]
    [InlineData(PlayerButtons.Moveright, MiniGameAction.MoveRight)]
    public void UsesConfiguredHorizontalRepeatDelayAndInterval(
        PlayerButtons button,
        MiniGameAction expected)
    {
        var tracker = new PlayerInputTracker(
            horizontalRepeatDelayMs: 240,
            repeatIntervalMs: 70);

        Assert.Equal([expected], tracker.Read(button, TimeSpan.Zero));
        Assert.Empty(tracker.Read(button, TimeSpan.FromMilliseconds(239)));
        Assert.Equal([expected], tracker.Read(button, TimeSpan.FromMilliseconds(240)));
        Assert.Empty(tracker.Read(button, TimeSpan.FromMilliseconds(309)));
        Assert.Equal([expected], tracker.Read(button, TimeSpan.FromMilliseconds(310)));
    }

    [Fact]
    public void UsesConfiguredRepeatIntervalForSoftDrop()
    {
        var tracker = new PlayerInputTracker(
            horizontalRepeatDelayMs: 240,
            repeatIntervalMs: 70);

        Assert.Equal(
            [MiniGameAction.SoftDrop],
            tracker.Read(PlayerButtons.Back, TimeSpan.Zero));
        Assert.Empty(
            tracker.Read(PlayerButtons.Back, TimeSpan.FromMilliseconds(69)));
        Assert.Equal(
            [MiniGameAction.SoftDrop],
            tracker.Read(PlayerButtons.Back, TimeSpan.FromMilliseconds(70)));
    }
}
