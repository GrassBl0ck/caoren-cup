using CaorenCup.WeaponPaints.Core;
using Xunit;

namespace CaorenCup.WeaponPaints.Tests;

public sealed class ChatListenerPolicyTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void ForwardingRequiresMenusAndAValidHumanPlayer(
        bool menusAvailable,
        bool playerValid,
        bool expected)
    {
        Assert.Equal(expected, ChatListenerPolicy.ShouldForward(menusAvailable, playerValid));
    }
}
