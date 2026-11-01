package com.caoren.crossera.core;

import java.util.Objects;
import java.util.UUID;

/** A snapshot, never a claim that the same connection/world is still current. */
public record PlayerRef(UUID id, UUID worldId, String world) {
    public PlayerRef { Objects.requireNonNull(id); Objects.requireNonNull(worldId); Objects.requireNonNull(world); }
}
