using CaorenCupPlugin.MatchRuntime;
using Xunit;

namespace CaorenCupPlugin.Tests;

public sealed class RosterReadyTests
{
    [Fact]
    public void Every_formal_seat_must_be_connected_on_the_expected_side_and_ready()
    {
        var coordinator = CreateCoordinator();
        ObserveAllCorrectly(coordinator);
        coordinator.SetReady(A1, true);
        coordinator.SetReady(A2, true);
        coordinator.SetReady(B1, true);

        var gate = coordinator.ValidateStart();

        Assert.False(gate.Allowed);
        Assert.Empty(gate.MissingSeatIds);
        Assert.Empty(gate.WrongSideSeatIds);
        Assert.Equal(["seat-b2"], gate.NotReadySeatIds);
    }

    [Fact]
    public void All_connected_correctly_placed_and_ready_seats_pass_the_gate()
    {
        var coordinator = CreateCoordinator();
        ObserveAllCorrectly(coordinator);
        ReadyAll(coordinator);

        var gate = coordinator.ValidateStart();

        Assert.True(gate.Allowed);
        Assert.Empty(gate.MissingSeatIds);
        Assert.Empty(gate.WrongSideSeatIds);
        Assert.Empty(gate.NotReadySeatIds);
    }

    [Fact]
    public void Disconnect_automatically_cancels_the_seat_ready_state()
    {
        var coordinator = CreateCoordinator();
        ObserveAllCorrectly(coordinator);
        ReadyAll(coordinator);

        coordinator.ObservePlayer(A2, null, connected: false);
        var gate = coordinator.ValidateStart();

        Assert.Equal(["seat-a2"], gate.MissingSeatIds);
        Assert.Contains("seat-a2", gate.NotReadySeatIds);
    }

    [Fact]
    public void Moving_to_the_wrong_engine_side_automatically_cancels_ready()
    {
        var coordinator = CreateCoordinator();
        ObserveAllCorrectly(coordinator);
        ReadyAll(coordinator);

        coordinator.ObservePlayer(B1, EngineSide.CounterTerrorist, connected: true);
        var gate = coordinator.ValidateStart();

        Assert.Equal(["seat-b1"], gate.WrongSideSeatIds);
        Assert.Contains("seat-b1", gate.NotReadySeatIds);
    }

    [Fact]
    public void Spectators_and_unknown_players_never_enter_ready_counts()
    {
        var coordinator = CreateCoordinator();
        ObserveAllCorrectly(coordinator);
        ReadyAll(coordinator);

        var observed = coordinator.ObservePlayer("76561198999999999", null, connected: true);
        var ready = coordinator.SetReady("76561198999999999", true);

        Assert.False(observed.Ok);
        Assert.Equal("NOT_ROSTERED", observed.Code);
        Assert.False(ready.Ok);
        Assert.True(coordinator.ValidateStart().Allowed);
    }

    [Fact]
    public void Ready_is_rejected_until_the_player_is_connected_on_the_expected_side()
    {
        var coordinator = CreateCoordinator();

        Assert.Equal("PLAYER_MISSING", coordinator.SetReady(A1, true).Code);
        coordinator.ObservePlayer(A1, EngineSide.Terrorist, connected: true);
        Assert.Equal("WRONG_SIDE", coordinator.SetReady(A1, true).Code);
        coordinator.ObservePlayer(A1, EngineSide.CounterTerrorist, connected: true);
        Assert.True(coordinator.SetReady(A1, true).Ok);
    }

    [Fact]
    public void Substitution_requires_a_safe_boundary_and_transfers_the_stable_seat()
    {
        var coordinator = CreateCoordinator();
        ObserveAllCorrectly(coordinator);
        ReadyAll(coordinator);
        const string replacement = "76561198222222222";

        var unsafeResult = coordinator.Substitute("seat-a2", replacement, "A队替补", effectiveRound: 8, atSafeBoundary: false);
        var result = coordinator.Substitute("seat-a2", replacement, "A队替补", effectiveRound: 8, atSafeBoundary: true);

        Assert.False(unsafeResult.Ok);
        Assert.Equal("UNSAFE_BOUNDARY", unsafeResult.Code);
        Assert.True(result.Ok);
        Assert.Null(coordinator.Roster.FindSeatBySteamId(A2));
        Assert.Equal("seat-a2", coordinator.Roster.FindSeatBySteamId(replacement)?.SeatId);
        Assert.Equal(replacement, coordinator.Roster.FindSeat("seat-a2")?.CurrentSteamId);
        Assert.False(coordinator.Roster.FindSeat("seat-a2")?.IsConnected);
        Assert.Contains("seat-a2", coordinator.ValidateStart().MissingSeatIds);
        Assert.Contains("seat-a2", coordinator.ValidateStart().NotReadySeatIds);
        Assert.Equal(new SubstitutionRecord("seat-a2", A2, replacement, "A队替补", 8), Assert.Single(coordinator.Roster.Substitutions));
    }

    [Fact]
    public void Replaced_player_cannot_reclaim_the_seat_by_reconnecting()
    {
        var coordinator = CreateCoordinator();
        const string replacement = "76561198222222222";
        coordinator.Substitute("seat-a2", replacement, "A队替补", effectiveRound: 8, atSafeBoundary: true);

        var staleReconnect = coordinator.ObservePlayer(A2, EngineSide.CounterTerrorist, connected: true);
        var replacementReconnect = coordinator.ObservePlayer(replacement, EngineSide.CounterTerrorist, connected: true);

        Assert.False(staleReconnect.Ok);
        Assert.Equal("NOT_ROSTERED", staleReconnect.Code);
        Assert.True(replacementReconnect.Ok);
        Assert.True(coordinator.Roster.FindSeat("seat-a2")?.IsConnected);
    }

    [Fact]
    public void Force_start_bypasses_roster_readiness_but_never_ability_sync()
    {
        var coordinator = CreateCoordinator();

        var normal = coordinator.ValidateStart(force: false, abilitySyncConfirmed: true);
        var forced = coordinator.ValidateStart(force: true, abilitySyncConfirmed: true);
        var abilityBlocked = coordinator.ValidateStart(force: true, abilitySyncConfirmed: false);

        Assert.False(normal.Allowed);
        Assert.True(forced.Allowed);
        Assert.False(abilityBlocked.Allowed);
        Assert.True(abilityBlocked.AbilitySyncBlocked);
        Assert.NotEmpty(abilityBlocked.MissingSeatIds);
    }

    [Fact]
    public void Expected_sides_follow_the_authoritative_team_a_side()
    {
        var roster = new RosterSeatManager(CreateDefinition());

        Assert.Equal(EngineSide.CounterTerrorist, roster.FindSeat("seat-a1")?.ExpectedSide);
        Assert.Equal(EngineSide.Terrorist, roster.FindSeat("seat-b1")?.ExpectedSide);

        roster.SetTeamAEngineSide(EngineSide.Terrorist);

        Assert.Equal(EngineSide.Terrorist, roster.FindSeat("seat-a1")?.ExpectedSide);
        Assert.Equal(EngineSide.CounterTerrorist, roster.FindSeat("seat-b1")?.ExpectedSide);
    }

    [Fact]
    public void Authoritative_player_snapshot_disconnects_absent_ready_seats()
    {
        var coordinator = CreateCoordinator();
        ObserveAllCorrectly(coordinator);
        ReadyAll(coordinator);

        coordinator.ObserveSnapshot(
        [
            (A1, EngineSide.CounterTerrorist),
            (B1, EngineSide.Terrorist),
            (B2, EngineSide.Terrorist),
        ]);

        var gate = coordinator.ValidateStart();
        Assert.Equal(["seat-a2"], gate.MissingSeatIds);
        Assert.Contains("seat-a2", gate.NotReadySeatIds);
    }

    private const string A1 = "76561198000000001";
    private const string A2 = "76561198000000002";
    private const string B1 = "76561198100000001";
    private const string B2 = "76561198100000002";

    private static ReadyCoordinator CreateCoordinator() => new(new RosterSeatManager(CreateDefinition()));

    private static MatchDefinition CreateDefinition() => new(
        "match-roster",
        1,
        "de_mirage",
        "A队",
        "B队",
        EngineSide.CounterTerrorist,
        [
            new MatchSeat("seat-a1", RosterSide.A, A1, "A1", true),
            new MatchSeat("seat-a2", RosterSide.A, A2, "A2", false),
            new MatchSeat("seat-b1", RosterSide.B, B1, "B1", true),
            new MatchSeat("seat-b2", RosterSide.B, B2, "B2", false),
        ]);

    private static void ObserveAllCorrectly(ReadyCoordinator coordinator)
    {
        coordinator.ObservePlayer(A1, EngineSide.CounterTerrorist, connected: true);
        coordinator.ObservePlayer(A2, EngineSide.CounterTerrorist, connected: true);
        coordinator.ObservePlayer(B1, EngineSide.Terrorist, connected: true);
        coordinator.ObservePlayer(B2, EngineSide.Terrorist, connected: true);
    }

    private static void ReadyAll(ReadyCoordinator coordinator)
    {
        Assert.True(coordinator.SetReady(A1, true).Ok);
        Assert.True(coordinator.SetReady(A2, true).Ok);
        Assert.True(coordinator.SetReady(B1, true).Ok);
        Assert.True(coordinator.SetReady(B2, true).Ok);
    }
}
