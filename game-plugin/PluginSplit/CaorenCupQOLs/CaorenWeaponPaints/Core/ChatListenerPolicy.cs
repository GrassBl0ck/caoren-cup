namespace CaorenCup.WeaponPaints.Core;

public static class ChatListenerPolicy
{
    public static bool ShouldForward(bool menusAvailable, bool playerValid) =>
        menusAvailable && playerValid;
}
