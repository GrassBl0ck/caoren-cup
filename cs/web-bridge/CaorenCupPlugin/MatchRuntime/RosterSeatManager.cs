using System.Text.RegularExpressions;

namespace CaorenCupPlugin.MatchRuntime;

public sealed record SubstitutionRecord(
    string SeatId,
    string PreviousSteamId,
    string NewSteamId,
    string NewName,
    int EffectiveRound);

public sealed class RosterSeatState
{
    internal RosterSeatState(MatchSeat seat, EngineSide expectedSide)
    {
        SeatId = seat.SeatId;
        Roster = seat.Roster;
        CurrentSteamId = seat.SteamId;
        CurrentName = seat.Name;
        IsCaptain = seat.IsCaptain;
        ExpectedSide = expectedSide;
    }

    public string SeatId { get; }

    public RosterSide Roster { get; }

    public string CurrentSteamId { get; internal set; }

    public string CurrentName { get; internal set; }

    public bool IsCaptain { get; }

    public EngineSide ExpectedSide { get; internal set; }

    public EngineSide? ObservedSide { get; internal set; }

    public bool IsConnected { get; internal set; }

    public bool IsOnExpectedSide => IsConnected && ObservedSide == ExpectedSide;
}

public sealed class RosterSeatManager
{
    private static readonly Regex SteamId64 = new("^7656119[0-9]{10}$", RegexOptions.CultureInvariant);
    private readonly Dictionary<string, RosterSeatState> _seatsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RosterSeatState> _seatsBySteamId = new(StringComparer.Ordinal);
    private readonly List<SubstitutionRecord> _substitutions = [];
    private EngineSide _teamAEngineSide;

    public RosterSeatManager(MatchDefinition definition)
    {
        _teamAEngineSide = definition.TeamAInitialSide;
        foreach (var seat in definition.Seats)
        {
            var state = new RosterSeatState(seat, ExpectedSide(seat.Roster));
            _seatsById.Add(state.SeatId, state);
            _seatsBySteamId.Add(state.CurrentSteamId, state);
        }
    }

    public IReadOnlyCollection<RosterSeatState> Seats => _seatsById.Values;

    public IReadOnlyList<SubstitutionRecord> Substitutions => _substitutions;

    public RosterSeatState? FindSeat(string seatId) =>
        _seatsById.TryGetValue(seatId, out var seat) ? seat : null;

    public RosterSeatState? FindSeatBySteamId(string steamId) =>
        _seatsBySteamId.TryGetValue(steamId, out var seat) ? seat : null;

    public MatchCommandResult ObservePlayer(string steamId, EngineSide? side, bool connected)
    {
        var seat = FindSeatBySteamId(steamId);
        if (seat is null) return new(false, "NOT_ROSTERED", "该玩家不占用正式比赛席位。");

        seat.IsConnected = connected;
        seat.ObservedSide = connected ? side : null;
        return new(true, "OK");
    }

    public void SetTeamAEngineSide(EngineSide side)
    {
        _teamAEngineSide = side;
        foreach (var seat in _seatsById.Values) seat.ExpectedSide = ExpectedSide(seat.Roster);
    }

    public MatchCommandResult Substitute(
        string seatId,
        string newSteamId,
        string newName,
        int effectiveRound,
        bool atSafeBoundary)
    {
        if (!atSafeBoundary)
            return new(false, "UNSAFE_BOUNDARY", "正式替补只能在冻结时间或技术暂停期间生效。");
        if (!_seatsById.TryGetValue(seatId, out var seat))
            return new(false, "SEAT_NOT_FOUND", "正式席位不存在。");
        if (!SteamId64.IsMatch(newSteamId))
            return new(false, "INVALID_STEAM_ID", "替补 SteamID64 格式无效。");
        if (string.IsNullOrWhiteSpace(newName))
            return new(false, "INVALID_NAME", "替补名称不能为空。");
        if (_seatsBySteamId.TryGetValue(newSteamId, out var occupied) && !ReferenceEquals(occupied, seat))
            return new(false, "STEAM_ID_OCCUPIED", "替补 SteamID 已占用其他正式席位。");

        var previousSteamId = seat.CurrentSteamId;
        _seatsBySteamId.Remove(previousSteamId);
        seat.CurrentSteamId = newSteamId;
        seat.CurrentName = newName.Trim();
        seat.IsConnected = false;
        seat.ObservedSide = null;
        _seatsBySteamId[newSteamId] = seat;
        _substitutions.Add(new SubstitutionRecord(
            seat.SeatId,
            previousSteamId,
            newSteamId,
            seat.CurrentName,
            Math.Max(0, effectiveRound)));
        return new(true, "OK");
    }

    private EngineSide ExpectedSide(RosterSide roster) => roster == RosterSide.A
        ? _teamAEngineSide
        : Opposite(_teamAEngineSide);

    private static EngineSide Opposite(EngineSide side) => side == EngineSide.CounterTerrorist
        ? EngineSide.Terrorist
        : EngineSide.CounterTerrorist;
}
