package com.caoren.crossera.core;

import static org.junit.jupiter.api.Assertions.*;
import java.nio.file.*;
import java.util.*;
import org.bukkit.configuration.file.YamlConfiguration;
import org.junit.jupiter.api.Test;

/** Checks authored sample wiring against the pinned source, not combat formulas. */
class ExampleConfigTest {
    private YamlConfiguration load(String path) throws Exception {
        var config = new YamlConfiguration();
        config.loadFromString(Files.readString(Path.of(path)));
        return config;
    }
    @Test void bothOwnConfigurationsParseAndProductionDefaultIsInert() throws Exception {
        var disabled = CombatSettings.from(load("src/main/resources/config.yml").getValues(true));
        var enabled = CombatSettings.from(load("examples/combat-config.yml").getValues(true));
        assertFalse(disabled.enabled()); assertTrue(disabled.worlds().isEmpty());
        assertTrue(enabled.enabled()); assertEquals(Set.of("cross-era-test"), enabled.worlds());
    }
    @Test void ocmConfigurationClassifiesEveryPinnedModuleExactlyOnce() throws Exception {
        var upstream = load("../../third-party/old-combat-mechanics/src/main/resources/config.yml");
        var example = load("examples/ocm-config.yml");
        Set<String> known = new HashSet<>();
        known.addAll(upstream.getStringList("always_enabled_modules"));
        known.addAll(upstream.getStringList("disabled_modules"));
        for (String mode : upstream.getConfigurationSection("modesets").getKeys(false))
            known.addAll(upstream.getStringList("modesets." + mode));
        Set<String> assigned = new HashSet<>();
        for (String section : List.of("always_enabled_modules", "disabled_modules", "modesets.cross-era-old", "modesets.cross-era-modern"))
            for (String module : example.getStringList(section)) assertTrue(assigned.add(module), "duplicate: " + module);
        assertEquals(known, assigned);
        assertEquals(upstream.getInt("config-version"), example.getInt("config-version"));
    }
    @Test void modernHasNoLegacyModulesOrAlwaysEnabledSideEffects() throws Exception {
        var example = load("examples/ocm-config.yml");
        assertTrue(example.getStringList("always_enabled_modules").isEmpty());
        assertTrue(example.getStringList("modesets.cross-era-modern").isEmpty());
        assertEquals(CombatService.OLD_MODULES, new HashSet<>(example.getStringList("modesets.cross-era-old")));
        assertTrue(example.getStringList("disabled_modules").contains("attack-frequency"));
        assertFalse(example.getBoolean("update-checker.auto-update"));
    }
    @Test void onlyExplicitLabWorldAllowsOldAndDefaultIsModern() throws Exception {
        var example = load("examples/ocm-config.yml");
        assertEquals(List.of("cross-era-modern"), example.getStringList("worlds.__default__"));
        assertEquals(List.of("cross-era-modern", "cross-era-old"), example.getStringList("worlds.cross-era-test"));
    }
}
