package com.caoren.crossera.core;

import static org.junit.jupiter.api.Assertions.*;
import java.util.*;
import java.util.concurrent.atomic.AtomicInteger;
import org.junit.jupiter.api.Test;

class CombatCommandsTest {
    private final PlayerRef player = new PlayerRef(UUID.randomUUID(), UUID.randomUUID(), "arena");
    private final CombatServiceTest.FakeBackend backend = new CombatServiceTest.FakeBackend();
    private final CombatService service = new CombatService(new CombatSettings(true, Set.of("arena"), "ce-old", "ce-modern", 32), backend);
    private CombatCommands commands() { return new CombatCommands(service, name -> name.equals("Tester") ? Optional.of(player) : Optional.empty()); }
    @Test void everyCommandChecksPermissionBeforePlayerLookupOrBackendAccess() {
        AtomicInteger lookedUp = new AtomicInteger();
        var commands = new CombatCommands(service, name -> { lookedUp.incrementAndGet(); return Optional.of(player); });
        for (String[] args : List.of(new String[]{}, new String[]{"status"}, new String[]{"status", "Tester"},
                new String[]{"profile", "Tester", "old"})) {
            assertEquals(CombatFailure.Code.UNAUTHORIZED, commands.execute(false, args).failure());
        }
        assertEquals(0, lookedUp.get());
        assertEquals(0, backend.calls);
    }
    @Test void offlinePlayerDoesNotModifyPersistentSelection() {
        assertEquals(CombatFailure.Code.PLAYER_UNAVAILABLE, commands().execute(true, "profile", "Offline", "old").failure());
        assertEquals(0, backend.writes);
    }
    @Test void invalidProfileDoesNotReachBackend() {
        assertEquals(CombatFailure.Code.INVALID_PROFILE, commands().execute(true, "profile", "Tester", "future").failure());
        assertEquals(0, backend.calls);
    }
    @Test void validSelectionIsReadBackAndSharedStatusUsesSameAuthority() {
        assertTrue(commands().execute(true, "profile", "Tester", "old").success());
        assertTrue(commands().execute(true, "status", "Tester").text().contains("规则=1.8.9"));
        assertTrue(commands().execute(true, "profile", "Tester", "modern").success());
        assertTrue(commands().execute(true, "status", "Tester").text().contains("规则=26.3"));
    }
    @Test void moduleMismatchIsNotReportedAsSuccessfulCombatSetup() {
        backend.unexpected = true;
        var result = commands().execute(true, "profile", "Tester", "modern");
        assertFalse(result.success());
        assertTrue(result.text().contains("OCM 已保存选择"));
    }
}
