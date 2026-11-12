package com.caoren.crossera.bukkit;

import static org.junit.jupiter.api.Assertions.*;
import java.util.UUID;
import org.junit.jupiter.api.Test;

class ObservationEpochsTest {
    private final ObservationEpochs epochs = new ObservationEpochs();
    private final UUID player = UUID.randomUUID();

    @Test void overlappingHitsKeepOneEpochWithoutClaimingIsolation() {
        var first = epochs.capture(player);
        var second = epochs.capture(player);
        assertSame(first, second);
        assertTrue(first.id() > 0);
        assertTrue(first.valid());
        assertEquals(ObservationEpochs.Reason.NONE, first.invalidation());
    }

    @Test void everyLifecycleBoundaryInvalidatesPreviouslyCapturedWindow() {
        for (var reason : ObservationEpochs.Reason.values()) {
            if (reason == ObservationEpochs.Reason.NONE) continue;
            var stamp = epochs.capture(player);
            epochs.invalidate(player, reason);
            assertFalse(stamp.valid(), reason.name());
            assertEquals(reason, stamp.invalidation());
            assertEquals(0, epochs.trackedPlayers());
            var next = epochs.capture(player);
            assertTrue(next.valid());
            assertNotEquals(stamp.id(), next.id());
        }
    }

    @Test void anotherPlayersRespawnDoesNotInvalidateVictimsWindow() {
        var victim = epochs.capture(player);
        UUID otherPlayer = UUID.randomUUID();
        var other = epochs.capture(otherPlayer);
        epochs.invalidate(otherPlayer, ObservationEpochs.Reason.RESPAWN);
        assertTrue(victim.valid());
        assertFalse(other.valid());
        assertNotEquals(victim.id(), other.id());
        assertEquals(1, epochs.trackedPlayers());
    }

    @Test void leaveAndReturnCannotRevalidateAnOldWindow() {
        var before = epochs.capture(player);
        epochs.invalidate(player, ObservationEpochs.Reason.WORLD_CHANGE);
        var away = epochs.capture(player);
        epochs.invalidate(player, ObservationEpochs.Reason.WORLD_CHANGE);
        var returned = epochs.capture(player);
        assertFalse(before.valid());
        assertFalse(away.valid());
        assertTrue(returned.valid());
        assertTrue(returned.id() > away.id() && away.id() > before.id());
    }

    @Test void deathThenRespawnKeepsFirstInvalidationOnOriginalWindow() {
        var before = epochs.capture(player);
        epochs.invalidate(player, ObservationEpochs.Reason.DEATH);
        epochs.invalidate(player, ObservationEpochs.Reason.RESPAWN);
        assertEquals(ObservationEpochs.Reason.DEATH, before.invalidation());
        assertTrue(epochs.capture(player).valid());
        assertFalse(before.valid());
    }

    @Test void unseenDeparturesDoNotCreatePermanentPlayerEntries() {
        for (int i = 0; i < 100; i++) epochs.invalidate(UUID.randomUUID(), ObservationEpochs.Reason.QUIT);
        assertEquals(0, epochs.trackedPlayers());
        var stamp = epochs.capture(player);
        epochs.invalidate(player, ObservationEpochs.Reason.QUIT);
        epochs.invalidate(player, ObservationEpochs.Reason.JOIN);
        assertFalse(stamp.valid());
        assertEquals(0, epochs.trackedPlayers());
    }

    @Test void evidenceContainsOnlyImmutableScalarSnapshot() {
        var stamp = epochs.capture(player);
        var before = stamp.evidence();
        assertEquals(stamp.id(), before.get("victimObservationEpoch"));
        epochs.invalidate(player, ObservationEpochs.Reason.RESPAWN);
        var after = stamp.evidence();
        assertEquals(true, before.get("sameObservationEpoch"));
        assertEquals(false, after.get("sameObservationEpoch"));
        assertEquals("RESPAWN", after.get("observationInvalidation"));
        assertThrows(UnsupportedOperationException.class, () -> after.put("x", "y"));
    }

    @Test void missingIdentityOrNoneReasonCannotCorruptTracker() {
        assertThrows(NullPointerException.class, () -> epochs.capture(null));
        assertThrows(IllegalArgumentException.class, () -> epochs.invalidate(player, ObservationEpochs.Reason.NONE));
        assertThrows(NullPointerException.class, () -> epochs.invalidate(player, null));
        assertEquals(0, epochs.trackedPlayers());
    }
}
