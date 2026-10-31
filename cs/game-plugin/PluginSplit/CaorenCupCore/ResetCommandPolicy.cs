namespace CaorenCup.Core;

public static class ResetCommandPolicy
{
    public static bool Run(bool isServerConsole, bool hasRoot, Action reset)
    {
        if (!isServerConsole && !hasRoot) return false;
        reset();
        return true;
    }
}
