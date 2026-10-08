namespace CaorenCup.Contracts;

/// <summary>MatchZy Practice/Sleep 状态的最小跨插件边界。正式 Gamemode 只能在 Sleep 状态启动。</summary>
public static class CaorenCupPracticeState
{
    private static readonly object Gate = new();
    private static bool _isPracticeActive;

    public static bool IsPracticeActive
    {
        get { lock (Gate) return _isPracticeActive; }
    }

    public static void EnterPractice()
    {
        lock (Gate) _isPracticeActive = true;
    }

    public static void EnterSleep()
    {
        lock (Gate) _isPracticeActive = false;
    }
}
