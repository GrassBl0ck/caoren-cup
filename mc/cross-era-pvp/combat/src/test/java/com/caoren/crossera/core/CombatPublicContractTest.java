package com.caoren.crossera.core;

import static org.junit.jupiter.api.Assertions.*;
import java.util.*;
import org.junit.jupiter.api.Test;

class CombatPublicContractTest {
    private final CombatServiceTest.FakeBackend backend = new CombatServiceTest.FakeBackend();
    private final CombatSettings settings = new CombatSettings(true, Set.of("arena"), "ce-old", "ce-modern", 32);
    private final CombatService service = new CombatService(settings, backend);
    private final PlayerRef player = new PlayerRef(UUID.randomUUID(), UUID.randomUUID(), "arena");

    @Test void missingProfileNeverWritesModernAsAnImplicitFallback() {
        var failure = assertThrows(CombatFailure.class, () -> service.select(player, null));
        assertEquals(CombatFailure.Code.INVALID_PROFILE, failure.code());
        assertEquals(0, backend.calls);
        assertEquals(0, backend.writes);
        assertTrue(backend.stored.isEmpty());
    }

    @Test void profileParsingAndModesetLookupRejectMissingProfileConsistently() {
        assertEquals(CombatFailure.Code.INVALID_PROFILE, assertThrows(CombatFailure.class,
                () -> CombatProfile.parse(null)).code());
        assertEquals(CombatFailure.Code.INVALID_PROFILE, assertThrows(CombatFailure.class,
                () -> settings.modeset(null)).code());
    }

    @Test void missingPlayerNeverTouchesBackend() {
        assertEquals(CombatFailure.Code.PLAYER_UNAVAILABLE,
                assertThrows(CombatFailure.class, () -> service.profile(null)).code());
        assertEquals(CombatFailure.Code.PLAYER_UNAVAILABLE,
                assertThrows(CombatFailure.class, () -> service.select(null, CombatProfile.OLD)).code());
        assertEquals(0, backend.calls);
    }

    @Test void defaultModernReadbackCannotPretendAnExplicitSelectionWasSaved() {
        backend.skipWrite = true;
        assertEquals(CombatFailure.Code.BACKEND_UNAVAILABLE,
                assertThrows(CombatFailure.class, () -> service.select(player, CombatProfile.MODERN)).code());
        assertTrue(backend.stored.isEmpty());
    }

    @Test void alreadyStoredMatchingSelectionRemainsIdempotent() {
        backend.stored.put(player, "ce-modern");
        backend.skipWrite = true;
        assertTrue(service.select(player, CombatProfile.MODERN).stored());
        assertEquals(0, backend.writes);
    }

    @Test void backendSummaryFailureIsAnActionableCommandReply() {
        backend.badDescription = true;
        var command = new CombatCommands(service, name -> Optional.empty());
        var reply = command.execute(true, "status");
        assertFalse(reply.success());
        assertEquals(CombatFailure.Code.BACKEND_UNAVAILABLE, reply.failure());
        assertEquals(0, backend.writes);
    }
}
