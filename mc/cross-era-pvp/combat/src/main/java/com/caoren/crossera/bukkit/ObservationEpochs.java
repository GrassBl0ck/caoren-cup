package com.caoren.crossera.bukkit;

import java.util.Map;
import java.util.HashMap;
import java.util.Objects;
import java.util.UUID;

/** Main-thread observation metadata only; never a combat rule or a persistence store. */
final class ObservationEpochs {
    static final String POLICY = "connection-world-life-v1";
    enum Reason { NONE, JOIN, QUIT, WORLD_CHANGE, DEATH, RESPAWN }
    private final Map<UUID, Stamp> active = new HashMap<>();
    private long sequence;
    static final class Stamp {
        private final long id;
        private Reason invalidation = Reason.NONE;
        private Stamp(long id) { this.id = id; }
        long id() { return id; }
        boolean valid() { return invalidation == Reason.NONE; }
        Reason invalidation() { return invalidation; }
        Map<String, Object> evidence() {
            return Map.of("victimObservationEpoch", id, "sameObservationEpoch", valid(),
                    "observationInvalidation", invalidation.name());
        }
    }
    Stamp capture(UUID player) {
        Objects.requireNonNull(player, "player");
        return active.computeIfAbsent(player, ignored -> new Stamp(sequence = Math.incrementExact(sequence)));
    }
    void invalidate(UUID player, Reason reason) {
        Objects.requireNonNull(player, "player");
        Objects.requireNonNull(reason, "reason");
        if (reason == Reason.NONE) throw new IllegalArgumentException("A lifecycle boundary needs a reason");
        Stamp old = active.remove(player);
        if (old != null) old.invalidation = reason;
    }
    int trackedPlayers() { return active.size(); }
}
