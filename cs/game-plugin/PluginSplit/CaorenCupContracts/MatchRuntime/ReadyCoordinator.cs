namespace CaorenCup.Contracts.MatchRuntime;

public sealed record StartGateResult(
    bool Allowed,
    IReadOnlyList<string> MissingSeatIds,
    IReadOnlyList<string> WrongSideSeatIds,
    IReadOnlyList<string> NotReadySeatIds,
    bool AbilitySyncBlocked);

public sealed record MatchReadySeat(string SeatId, bool Confirmed, bool Connected, bool OnExpectedSide);

public sealed class ReadyCoordinator
{
    private readonly HashSet<string> _readySeatIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _awaitingArrivalSeatIds = new(StringComparer.Ordinal);

    public ReadyCoordinator(RosterSeatManager roster) => Roster = roster;

    public RosterSeatManager Roster { get; }

    public MatchReadySeat[] GetSeatStatuses() => Roster.Seats
        .OrderBy(seat => seat.SeatId, StringComparer.Ordinal)
        .Select(seat => new MatchReadySeat(seat.SeatId, _readySeatIds.Contains(seat.SeatId)
            || _awaitingArrivalSeatIds.Contains(seat.SeatId), seat.IsConnected, seat.IsOnExpectedSide))
        .ToArray();

    public void RestoreConfirmations(IEnumerable<MatchReadySeat> savedSeats)
    {
        var validSeatIds = Roster.Seats.Select(seat => seat.SeatId).ToHashSet(StringComparer.Ordinal);
        foreach (var saved in savedSeats)
            if (saved.Confirmed && validSeatIds.Contains(saved.SeatId)) _awaitingArrivalSeatIds.Add(saved.SeatId);
    }

    public MatchCommandResult ObservePlayer(string steamId, EngineSide? side, bool connected)
    {
        var result = Roster.ObservePlayer(steamId, side, connected);
        if (!result.Ok) return result;

        var seat = Roster.FindSeatBySteamId(steamId)!;
        UpdateSeatReadiness(seat);
        return result;
    }

    public MatchCommandResult SetReady(string steamId, bool ready, bool allowBeforeArrival = false)
    {
        var seat = Roster.FindSeatBySteamId(steamId);
        if (seat is null) return new(false, "NOT_ROSTERED", "该玩家不占用正式比赛席位。");
        if (!ready)
        {
            _readySeatIds.Remove(seat.SeatId);
            _awaitingArrivalSeatIds.Remove(seat.SeatId);
            return new(true, "OK");
        }
        if (!seat.IsOnExpectedSide && allowBeforeArrival)
        {
            _awaitingArrivalSeatIds.Add(seat.SeatId);
            return new(true, "AWAITING_ARRIVAL");
        }
        if (!seat.IsConnected) return new(false, "PLAYER_MISSING", "玩家尚未连接服务器。");
        if (!seat.IsOnExpectedSide) return new(false, "WRONG_SIDE", "玩家尚未站到正确队伍。");

        _awaitingArrivalSeatIds.Remove(seat.SeatId);
        _readySeatIds.Add(seat.SeatId);
        return new(true, "OK");
    }

    public void ObserveSnapshot(IEnumerable<(string SteamId, EngineSide Side)> players)
    {
        foreach (var seat in Roster.Seats)
        {
            Roster.ObservePlayer(seat.CurrentSteamId, null, connected: false);
        }
        foreach (var player in players)
        {
            var result = Roster.ObservePlayer(player.SteamId, player.Side, connected: true);
            if (!result.Ok) continue;
        }
        foreach (var seat in Roster.Seats) UpdateSeatReadiness(seat);
    }

    private void UpdateSeatReadiness(RosterSeatState seat)
    {
        if (!seat.IsOnExpectedSide)
        {
            _readySeatIds.Remove(seat.SeatId);
            return;
        }

        if (_awaitingArrivalSeatIds.Remove(seat.SeatId)) _readySeatIds.Add(seat.SeatId);
    }

    public StartGateResult ValidateStart(bool force = false, bool abilitySyncConfirmed = true)
    {
        var ordered = Roster.Seats.OrderBy(seat => seat.SeatId, StringComparer.Ordinal).ToArray();
        var missing = ordered.Where(seat => !seat.IsConnected).Select(seat => seat.SeatId).ToArray();
        var wrongSide = ordered.Where(seat => seat.IsConnected && !seat.IsOnExpectedSide).Select(seat => seat.SeatId).ToArray();
        var notReady = ordered.Where(seat => !_readySeatIds.Contains(seat.SeatId)).Select(seat => seat.SeatId).ToArray();
        var abilitySyncBlocked = !abilitySyncConfirmed;
        var allowed = !abilitySyncBlocked && (force || missing.Length == 0 && wrongSide.Length == 0 && notReady.Length == 0);
        return new StartGateResult(allowed, missing, wrongSide, notReady, abilitySyncBlocked);
    }

    public MatchCommandResult Substitute(
        string seatId,
        string newSteamId,
        string newName,
        int effectiveRound,
        bool atSafeBoundary)
    {
        var result = Roster.Substitute(seatId, newSteamId, newName, effectiveRound, atSafeBoundary);
        if (result.Ok)
        {
            _readySeatIds.Remove(seatId);
            _awaitingArrivalSeatIds.Remove(seatId);
        }
        return result;
    }
}
