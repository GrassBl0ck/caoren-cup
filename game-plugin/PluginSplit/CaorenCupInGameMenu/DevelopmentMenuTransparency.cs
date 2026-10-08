using System.Globalization;

namespace CaorenCup.Features.InGameMenu;

/// <summary>仅开发构建包含。状态按玩家隔离，仅存在于当前菜单会话。</summary>
internal sealed class DevelopmentMenuTransparency
{
    public const int DefaultPercent = 10;
    public const int StepPercent = 5;
    private const string ButtonPrefix = "dev_transparency_";
    private readonly Dictionary<string, int> _percentByPlayer = new();

    public int GetPercent(string steamId) => _percentByPlayer.GetValueOrDefault(steamId, DefaultPercent);

    public bool TryApplyClick(string steamId, string buttonId, bool hasSession, bool isRoot, out int percent)
    {
        percent = GetPercent(steamId);
        if (!hasSession || !isRoot || !buttonId.StartsWith(ButtonPrefix, StringComparison.Ordinal)) return false;
        if (!int.TryParse(buttonId.AsSpan(ButtonPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var step)
            || step is < 0 or > 20) return false;

        percent = step * StepPercent;
        _percentByPlayer[steamId] = percent;
        return true;
    }

    public void Forget(string steamId) => _percentByPlayer.Remove(steamId);
    public void Clear() => _percentByPlayer.Clear();
}
