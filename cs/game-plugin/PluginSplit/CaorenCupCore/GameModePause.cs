using CaorenCup.Contracts;

namespace CaorenCup.Core;

/// <summary>Tracks pauses and tactical allowances for a single Core match.</summary>
internal sealed class GameModePause
{
    private readonly Dictionary<(int Overtime, bool Roster), int> _used = new();
    private (int Overtime, bool Roster)? _tactical;
    private double? _deadline;

    public bool Active { get; private set; }

    internal static bool RosterForSide(bool isCt, bool halftime, int playedRounds, int regulationRounds, int overtime, int overtimeRounds)
    {
        // Each overtime has its own allowance. Only swaps within that stage affect its roster keys.
        var stageRounds = overtime == 0 ? regulationRounds : overtimeRounds;
        var firstRound = overtime == 0 ? 0 : regulationRounds + (overtime - 1) * overtimeRounds;
        var secondHalf = halftime && stageRounds >= 2 && playedRounds - firstRound >= stageRounds / 2;
        return isCt != secondHalf;
    }

    public GameModeResult Request(bool tactical, int overtime, bool roster)
    {
        if (Active) return GameModeResult.Fail("比赛已有暂停请求或正在暂停。");
        var key = (overtime, roster);
        if (tactical && _used.GetValueOrDefault(key) >= (overtime == 0 ? 4 : 1))
            return GameModeResult.Fail("该队本阶段的战术暂停次数已经用完。");

        _tactical = tactical ? key : null;
        Active = true;
        return GameModeResult.Ok("已请求暂停。");
    }

    public bool ConfirmTacticalPause(double now)
    {
        if (_tactical is not { } key || _deadline.HasValue) return false;
        _used[key] = _used.GetValueOrDefault(key) + 1;
        _deadline = now + 30;
        return true;
    }

    public bool IsDue(double now) => _deadline is double deadline && now >= deadline;

    public bool Clear()
    {
        var wasActive = Active;
        Active = false;
        _tactical = null;
        _deadline = null;
        return wasActive;
    }

    public void Reset()
    {
        Clear();
        _used.Clear();
    }
}
