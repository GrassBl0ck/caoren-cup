using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CS2MiniGames.Framework;
using System.Reflection;

namespace CS2MiniGames.Tests;

public sealed class PlayerButtonSourceTests
{
    [Fact]
    public void MissingInputSourceReturnsEmptyButtonsWithoutReadingIt()
    {
        var readCount = 0;

        var buttons = PlayerButtonSource.Read(
            hasInputSource: false,
            () =>
            {
                readCount++;
                return PlayerButtons.Jump;
            });

        Assert.Equal(default, buttons);
        Assert.Equal(0, readCount);
    }

    [Fact]
    public void AvailableInputSourceReturnsCurrentButtons()
    {
        var expected = PlayerButtons.Jump | PlayerButtons.Moveleft;

        var buttons = PlayerButtonSource.Read(
            hasInputSource: true,
            () => expected);

        Assert.Equal(expected, buttons);
    }

    [Fact]
    public void InputOverloadRequiresTheExactPawnHandleUsedByButtons()
    {
        var overload = typeof(PlayerButtonSource).GetMethod(
            nameof(PlayerButtonSource.Read),
            BindingFlags.Static | BindingFlags.Public,
            binder: null,
            types: [typeof(CBasePlayerController), typeof(Func<PlayerButtons>)],
            modifiers: null);

        Assert.NotNull(overload);
    }
}
