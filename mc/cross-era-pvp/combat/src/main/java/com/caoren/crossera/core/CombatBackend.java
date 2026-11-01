package com.caoren.crossera.core;

import java.util.List;
import java.util.Map;
import java.util.Set;

/** Server-thread-only adapter contract. Implementations must not keep a second profile store. */
public interface CombatBackend {
    String description();
    Set<String> modesets();
    Set<String> moduleNames();
    List<String> allowedModesets(String world);
    String storedModeset(PlayerRef player);
    Map<String, Boolean> effectiveModules(PlayerRef player);
    boolean hasOverrides(PlayerRef player);
    void selectModeset(PlayerRef player, String modeset);
}
