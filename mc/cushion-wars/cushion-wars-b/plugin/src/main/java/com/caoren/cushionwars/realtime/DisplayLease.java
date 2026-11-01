package com.caoren.cushionwars.realtime;

import java.util.HashMap;
import java.util.Map;
import java.util.UUID;
import java.util.function.Consumer;

/** Restore only displays we actually replaced; do not overwrite another plugin's later change. */
final class DisplayLease<T> {
    private record Lease<T>(T original, T owned) { }
    private final Map<UUID, Lease<T>> entries = new HashMap<>();

    void acquire(UUID id, T original, T owned) {
        entries.putIfAbsent(id, new Lease<>(original, owned));
    }

    void release(UUID id, T current, Consumer<T> restore) {
        Lease<T> lease = entries.remove(id);
        if (lease != null && current == lease.owned()) restore.accept(lease.original());
    }
}
