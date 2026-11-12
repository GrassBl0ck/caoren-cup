package com.caoren.crossera.core;

import static org.junit.jupiter.api.Assertions.*;
import java.util.*;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

class CombatServiceTest {
    private final UUID world = UUID.randomUUID();
    private final PlayerRef a = new PlayerRef(UUID.randomUUID(), world, "arena");
    private final PlayerRef b = new PlayerRef(UUID.randomUUID(), world, "arena");
    private FakeBackend backend;
    private CombatService service;
    @BeforeEach void setup() {
        backend = new FakeBackend();
        service = new CombatService(new CombatSettings(true, Set.of("arena"), "ce-old", "ce-modern", 32), backend);
    }
    @Test void readsModernDefaultWithoutWritingPlayerData() {
        assertEquals(CombatProfile.MODERN, service.profile(a).profile());
        assertFalse(service.profile(a).stored());
        assertEquals(0, backend.writes);
    }
    @Test void profileSelectionUsesBackendPersistenceAndSurvivesServiceRecreation() {
        service.select(a, CombatProfile.OLD);
        var reconnected = new CombatService(service.settings(), backend);
        assertEquals(CombatProfile.OLD, reconnected.profile(a).profile());
        assertTrue(reconnected.profile(a).stored());
        assertEquals(CombatProfile.MODERN, reconnected.profile(b).profile());
    }
    @Test void directCrossEraRoutingKeepsAttackerAndVictimIndependent() {
        service.select(a, CombatProfile.OLD);
        var forward = service.routing(a, b);
        var reverse = service.routing(b, a);
        assertEquals(CombatProfile.OLD, forward.attack().profile());
        assertEquals(CombatProfile.MODERN, forward.defence().profile());
        assertEquals(CombatProfile.MODERN, reverse.attack().profile());
        assertEquals(CombatProfile.OLD, reverse.defence().profile());
    }
    @Test void unapprovedWorldDoesNotTouchBackend() {
        var foreign = new PlayerRef(a.id(), UUID.randomUUID(), "production");
        assertEquals(CombatFailure.Code.OUTSIDE_WORLD, assertThrows(CombatFailure.class,
                () -> service.select(foreign, CombatProfile.OLD)).code());
        assertEquals(0, backend.calls);
    }
    @Test void disabledExperimentCannotSelectEvenWithBackendPresent() {
        var disabled = new CombatService(new CombatSettings(false, Set.of(), "ce-old", "ce-modern", 32), backend);
        assertEquals(CombatFailure.Code.DISABLED, assertThrows(CombatFailure.class,
                () -> disabled.select(a, CombatProfile.OLD)).code());
        assertEquals(0, backend.calls);
    }
    @Test void missingBackendIsReportedWithoutPretendingNativeFallback() {
        var missing = new CombatService(service.settings(), null);
        assertEquals(CombatFailure.Code.BACKEND_UNAVAILABLE,
                assertThrows(CombatFailure.class, () -> missing.profile(a)).code());
    }
    @Test void startupRejectsWorldThatDefaultsToOldCombat() {
        backend.allowed = List.of("ce-old", "ce-modern");
        assertEquals(CombatFailure.Code.INVALID_SETUP,
                assertThrows(CombatFailure.class, service::validateStartup).code());
    }
    @Test void startupRejectsMissingRequiredModule() {
        backend.names = Set.of("old-tool-damage");
        assertThrows(CombatFailure.class, service::validateStartup);
    }
    @Test void refusesToOverwriteSharedModuleOverrides() {
        backend.overrides = true;
        assertEquals(CombatFailure.Code.OVERRIDE_CONFLICT, assertThrows(CombatFailure.class,
                () -> service.select(a, CombatProfile.OLD)).code());
        assertEquals(0, backend.writes);
    }
    @Test void detectsModernPresetWithLegacyAppleEffects() {
        backend.unexpected = true;
        assertFalse(service.profile(a).verifiedModules());
    }
    @Test void externalBackendChangeIsObservedWithoutStaleOwnCache() {
        backend.stored.put(a, "ce-old");
        assertEquals(CombatProfile.OLD, service.profile(a).profile());
        backend.stored.put(a, "ce-modern");
        assertEquals(CombatProfile.MODERN, service.profile(a).profile());
    }
    @Test void backendFailureIsNotReportedAsSuccessOrCached() {
        backend.failWrite = true;
        assertThrows(CombatFailure.class, () -> service.select(a, CombatProfile.OLD));
        assertEquals(CombatProfile.MODERN, service.profile(a).profile());
    }
    @Test void sameEraRoutingReportsTwoIndependentlyAuditedSides() {
        for (CombatProfile profile : CombatProfile.values()) {
            service.select(a, profile); service.select(b, profile);
            var route = service.routing(a, b);
            assertEquals(profile, route.attack().profile());
            assertEquals(profile, route.defence().profile());
            assertTrue(route.attack().verifiedModules()); assertTrue(route.defence().verifiedModules());
        }
    }
    @Test void sameUuidUsesDifferentWorldSelectionsWithoutOwnGlobalCache() {
        var anotherWorld = new PlayerRef(a.id(), UUID.randomUUID(), "second-arena");
        var twoWorlds = new CombatService(new CombatSettings(true, Set.of("arena", "second-arena"), "ce-old", "ce-modern", 32), backend);
        twoWorlds.select(a, CombatProfile.OLD);
        assertEquals(CombatProfile.MODERN, twoWorlds.profile(anotherWorld).profile());
        assertEquals(CombatProfile.OLD, twoWorlds.profile(a).profile());
    }
    @Test void unrecognisedPersistedSelectionIsReportedWithoutOverwritingIt() {
        backend.stored.put(a, "a-different-plugin-mode");
        assertEquals(CombatFailure.Code.INVALID_SETUP, assertThrows(CombatFailure.class, () -> service.profile(a)).code());
        assertEquals("a-different-plugin-mode", backend.stored.get(a));
        assertEquals(0, backend.writes);
    }
    @Test void failureAfterExternalWriteDoesNotHideAuthoritativeChangedState() {
        backend.failAfterWrite = true;
        assertThrows(CombatFailure.class, () -> service.select(a, CombatProfile.OLD));
        assertEquals(CombatProfile.OLD, service.profile(a).profile());
    }
    static final class FakeBackend implements CombatBackend {
        final Map<PlayerRef, String> stored = new HashMap<>();
        List<String> allowed = List.of("ce-modern", "ce-old");
        Set<String> names = new HashSet<>(CombatService.OLD_MODULES);
        boolean overrides, unexpected, failWrite, failAfterWrite, skipWrite, badDescription;
        int calls, writes;
        public String description() { if (badDescription) throw new IllegalStateException("description unavailable"); return "fake"; }
        public Set<String> modesets() { calls++; return Set.of("ce-old", "ce-modern"); }
        public Set<String> moduleNames() { calls++; return names; }
        public List<String> allowedModesets(String world) { calls++; return allowed; }
        public String storedModeset(PlayerRef player) { calls++; return stored.get(player); }
        public boolean hasOverrides(PlayerRef player) { calls++; return overrides; }
        public Map<String, Boolean> effectiveModules(PlayerRef player) {
            calls++;
            Map<String, Boolean> result = new HashMap<>();
            for (String name : names) result.put(name, "ce-old".equals(stored.get(player)));
            result.put("old-golden-apples", unexpected);
            return result;
        }
        public void selectModeset(PlayerRef player, String mode) {
            calls++;
            if (failWrite) throw new IllegalStateException("simulated unavailable backend");
            if (skipWrite) return;
            writes++; stored.put(player, mode);
            if (failAfterWrite) throw new IllegalStateException("simulated failure after persisted write");
        }
    }
}
