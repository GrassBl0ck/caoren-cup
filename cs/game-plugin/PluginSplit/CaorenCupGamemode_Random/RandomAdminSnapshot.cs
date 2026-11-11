using CaorenCup.Contracts;

namespace CaorenCup.Gamemodes.Random;

/// <summary>由正常玩家事件维护的身份信息，不创建强化状态，也不保存控制器引用。</summary>
public sealed record RandomPlayerIdentity(string SteamId, string Name, int Team, bool IsBot = false);

public static class RandomAdminSnapshotAccess
{
    // The provider supplies the actual root check; rejected readers never enumerate
    // players or touch the match, including when the caller is not a real player.
    public static RandomAdminView? Read(bool validHuman, Func<bool> hasRoot, Func<RandomAdminView> snapshot) =>
        validHuman && hasRoot() ? snapshot() : null;
}
