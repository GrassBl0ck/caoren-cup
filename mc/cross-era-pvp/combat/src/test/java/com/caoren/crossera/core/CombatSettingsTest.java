package com.caoren.crossera.core;

import static org.junit.jupiter.api.Assertions.*;
import java.util.*;
import org.junit.jupiter.api.Test;

class CombatSettingsTest {
    @Test void missingConfigurationDefaultsToDisabledWithNoWorlds() {
        var settings = CombatSettings.from(Map.of());
        assertFalse(settings.enabled());
        assertTrue(settings.worlds().isEmpty());
    }
    @Test void enabledRequiresExplicitWorldBoundary() {
        assertThrows(IllegalArgumentException.class, () -> CombatSettings.from(Map.of("experimental-enabled", true)));
    }
    @Test void stringFalseIsNotAcceptedAsBoolean() {
        assertThrows(IllegalArgumentException.class, () -> CombatSettings.from(Map.of("experimental-enabled", "false")));
    }
    @Test void oldAndModernModesetsMustBeDistinct() {
        assertThrows(IllegalArgumentException.class, () -> CombatSettings.from(Map.of("profiles.old", "same", "profiles.modern", "same")));
    }
    @Test void journalQueueMustHaveFiniteBoundedIntegralCapacity() {
        for (Object value : List.of(-1, 0, 65537, 16.5, "2048")) {
            assertThrows(IllegalArgumentException.class, () -> CombatSettings.from(Map.of("journal.queue-capacity", value)));
        }
    }
    @Test void unknownBackendIsNotSilentlyReplaced() {
        assertThrows(IllegalArgumentException.class, () -> CombatSettings.from(Map.of("backend", "native")));
    }
    @Test void acceptsExplicitAllowlistWithoutNormalisingWorldNames() {
        var settings = CombatSettings.from(Map.of("experimental-enabled", true, "allowed-worlds", List.of("TestWorld")));
        assertEquals(Set.of("TestWorld"), settings.worlds());
    }
    @Test void duplicateBlankOrWhitespaceWorldsAreRejected() {
        for (var worlds : List.of(List.of("arena", "arena"), List.of(""), List.of(" arena"), List.of("arena ")))
            assertThrows(IllegalArgumentException.class, () -> CombatSettings.from(Map.of("allowed-worlds", worlds)));
    }
    @Test void nanInfinityAndHugeCapacityCannotReachQueueAllocation() {
        for (Number value : List.of(Double.NaN, Double.POSITIVE_INFINITY, Double.NEGATIVE_INFINITY, Long.MAX_VALUE))
            assertThrows(IllegalArgumentException.class, () -> CombatSettings.from(Map.of("journal.queue-capacity", value)));
    }
    @Test void presetNamesCannotContainUnexpectedCharacters() {
        for (String mode : List.of("OLD", "old mode", "../old", "old.modern", "old\n"))
            assertThrows(IllegalArgumentException.class, () -> CombatSettings.from(Map.of("profiles.old", mode)));
    }
}
