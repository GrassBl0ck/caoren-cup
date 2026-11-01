package com.caoren.crossera.journal;

import static org.junit.jupiter.api.Assertions.*;
import java.io.*;
import java.nio.file.*;
import java.util.*;
import java.util.concurrent.*;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

class JsonJournalTest {
    @TempDir Path temp;
    @Test void closeDrainsAcceptedRecordsAsUtf8JsonLinesAndIsIdempotent() throws Exception {
        Path file = temp.resolve("events.jsonl");
        var journal = JsonJournal.open(file, 16, message -> {});
        assertTrue(journal.record(Map.of("kind", "startup", "text", "草人杯")));
        assertTrue(journal.record(Map.of("kind", "damage_event", "eventFinalDamage", 3)));
        journal.close(); journal.close();
        var lines = Files.readAllLines(file);
        assertEquals(2, lines.size());
        assertTrue(lines.getFirst().contains("草人杯"));
        assertFalse(journal.record(Map.of("kind", "too_late")));
    }
    @Test void boundedQueueRejectsOverflowWithoutWaitingForSlowDisk() throws Exception {
        CountDownLatch writing = new CountDownLatch(1), release = new CountDownLatch(1);
        var writer = new StringWriter() {
            @Override public void write(String value) {
                writing.countDown();
                try { assertTrue(release.await(5, TimeUnit.SECONDS)); }
                catch (InterruptedException e) { throw new AssertionError(e); }
                super.write(value);
            }
        };
        var journal = new JsonJournal(writer, 1, message -> {});
        try {
            assertTrue(journal.record(Map.of("id", 1)));
            assertTrue(writing.await(5, TimeUnit.SECONDS));
            assertTrue(journal.record(Map.of("id", 2)));
            assertFalse(journal.record(Map.of("id", 3)));
            assertEquals(1L, journal.stats().get("rejected"));
        } finally { release.countDown(); journal.close(); }
    }
    @Test void writeFailureIsVisibleAndDoesNotPretendSuccessfulShutdown() throws Exception {
        CountDownLatch failed = new CountDownLatch(1);
        Writer writer = new Writer() {
            public void write(char[] value, int offset, int length) throws IOException { throw new IOException("simulated disk failure"); }
            public void flush() { }
            public void close() { }
        };
        var journal = new JsonJournal(writer, 16, message -> failed.countDown());
        journal.record(Map.of("kind", "startup"));
        assertTrue(failed.await(5, TimeUnit.SECONDS));
        assertFalse(journal.record(Map.of("kind", "after_failure")));
        assertThrows(IOException.class, journal::close);
        assertEquals(1L, journal.stats().get("errors"));
    }
    @Test void nonFiniteDataIsRejectedInsteadOfThrowingIntoGameListener() throws Exception {
        var journal = JsonJournal.open(temp.resolve("nonfinite.jsonl"), 16, message -> {});
        assertFalse(journal.record(Map.of("damage", Double.NaN)));
        journal.close();
        assertEquals(1L, journal.stats().get("errors"));
    }
}
