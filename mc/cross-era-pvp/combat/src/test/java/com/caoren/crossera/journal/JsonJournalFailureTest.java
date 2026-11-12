package com.caoren.crossera.journal;

import static org.junit.jupiter.api.Assertions.*;
import java.io.*;
import java.nio.file.*;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicReference;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

class JsonJournalFailureTest {
    @TempDir Path temp;

    @Test void invalidCapacityDoesNotCreateDirectoriesOrOpenAnOutput() {
        Path file = temp.resolve("not-created/events.jsonl");
        assertThrows(IllegalArgumentException.class, () -> JsonJournal.open(file, 0, message -> {}));
        assertFalse(Files.exists(file.getParent()));
    }

    @Test void flushFailureIsReportedByCloseEvenAfterAllLinesWereWritten() {
        Writer writer = new StringWriter() {
            @Override public void flush() { throw new UncheckedIOException(new IOException("flush failed")); }
        };
        var journal = new JsonJournal(writer, 16, message -> {});
        assertTrue(journal.record(Map.of("id", 1)));
        assertThrows(IOException.class, journal::close);
        assertEquals(1L, journal.stats().get("errors"));
        assertFalse(journal.stats().get("workerAlive").equals(true));
    }

    @Test void checkedFlushFailureCannotBecomeSuccessfulClose() {
        Writer writer = new Writer() {
            public void write(char[] text, int start, int length) { }
            public void flush() throws IOException { throw new IOException("flush failed"); }
            public void close() { }
        };
        var journal = new JsonJournal(writer, 16, message -> {});
        journal.record(Map.of("id", 1));
        assertThrows(IOException.class, journal::close);
        assertEquals(1L, journal.stats().get("errors"));
    }

    @Test void closeFailureCannotBecomeSuccessfulClose() {
        Writer writer = new Writer() {
            public void write(char[] text, int start, int length) { }
            public void flush() { }
            public void close() throws IOException { throw new IOException("close failed"); }
        };
        var journal = new JsonJournal(writer, 16, message -> {});
        journal.record(Map.of("id", 1));
        assertThrows(IOException.class, journal::close);
        assertEquals(1L, journal.stats().get("errors"));
    }

    @Test void runtimeWriterFailureDisablesFutureRecordsAndFailsClose() {
        var failed = new CountDownLatch(1);
        Writer writer = new StringWriter() {
            @Override public void write(String value) { throw new IllegalStateException("provider failure"); }
        };
        var journal = new JsonJournal(writer, 16, message -> failed.countDown());
        journal.record(Map.of("id", 1));
        try {
            assertThrows(IOException.class, journal::close);
            assertEquals(1L, journal.stats().get("errors"));
            assertFalse(journal.record(Map.of("id", 2)));
            assertEquals(0, failed.getCount());
        } finally { try { journal.close(); } catch (IOException expected) { } }
    }

    @Test void warningCallbackFailureDoesNotThrowIntoRecordCaller() throws Exception {
        var journal = new JsonJournal(new StringWriter(), 16, message -> { throw new IllegalStateException("logger failure"); });
        journal.close();
        assertFalse(assertDoesNotThrow(() -> journal.record(Map.of("id", 1))));
        assertEquals(1L, journal.stats().get("rejected"));
        assertEquals(1L, journal.stats().get("errors"));
    }

    @Test void interruptedCloseKeepsInterruptAndCanBeRetriedAfterWriterUnblocks() throws Exception {
        var entered = new CountDownLatch(1);
        var release = new CountDownLatch(1);
        var journal = new JsonJournal(blockingWriter(entered, release), 16, message -> {});
        journal.record(Map.of("id", 1));
        assertTrue(entered.await(3, TimeUnit.SECONDS));
        try {
            Thread.currentThread().interrupt();
            assertThrows(IOException.class, journal::close);
            assertTrue(Thread.currentThread().isInterrupted());
        } finally {
            Thread.interrupted();
            release.countDown();
            journal.close();
        }
        assertEquals(1L, journal.stats().get("writtenToBuffer"));
        assertEquals(false, journal.stats().get("workerAlive"));
    }

    @Test void fiveSecondTimeoutDoesNotClaimACompletedDrain() throws Exception {
        var entered = new CountDownLatch(1);
        var release = new CountDownLatch(1);
        var journal = new JsonJournal(blockingWriter(entered, release), 16, message -> {});
        journal.record(Map.of("id", 1));
        assertTrue(entered.await(3, TimeUnit.SECONDS));
        try {
            IOException failure = assertThrows(IOException.class, journal::close);
            assertTrue(failure.getMessage().contains("5 seconds"));
            assertEquals(true, journal.stats().get("workerAlive"));
            assertFalse(journal.record(Map.of("id", 2)));
        } finally { release.countDown(); journal.close(); }
        assertEquals(1L, journal.stats().get("writtenToBuffer"));
    }

    @Test void concurrentCloseDrainsEveryAcceptedLineExactlyOnce() throws Exception {
        var journal = JsonJournal.open(temp.resolve("concurrent.jsonl"), 2048, message -> {});
        var start = new CountDownLatch(1);
        var halfway = new CountDownLatch(4);
        var failure = new AtomicReference<Throwable>();
        List<Thread> producers = new ArrayList<>();
        for (int worker = 0; worker < 4; worker++) {
            int id = worker;
            var thread = new Thread(() -> {
                try {
                    assertTrue(start.await(3, TimeUnit.SECONDS));
                    for (int i = 0; i < 200; i++) {
                        journal.record(Map.of("worker", id, "item", i));
                        if (i == 30) halfway.countDown();
                    }
                } catch (Throwable error) { failure.compareAndSet(null, error); }
            });
            producers.add(thread); thread.start();
        }
        start.countDown();
        try {
            assertTrue(halfway.await(3, TimeUnit.SECONDS));
            journal.close();
        } finally {
            for (Thread producer : producers) { producer.join(3000); assertFalse(producer.isAlive()); }
            journal.close();
        }
        assertNull(failure.get());
        var lines = Files.readAllLines(temp.resolve("concurrent.jsonl"));
        assertEquals(((Number) journal.stats().get("accepted")).longValue(), lines.size());
        assertEquals(lines.size(), new HashSet<>(lines).size());
        assertEquals(800L, ((Number) journal.stats().get("accepted")).longValue()
                + ((Number) journal.stats().get("rejected")).longValue());
        assertEquals(0L, journal.stats().get("errors"));
        assertEquals(0, journal.stats().get("queued"));
    }

    private static Writer blockingWriter(CountDownLatch entered, CountDownLatch release) {
        return new StringWriter() {
            @Override public void write(String text) {
                entered.countDown();
                try {
                    if (!release.await(15, TimeUnit.SECONDS)) throw new IllegalStateException("test writer release timeout");
                } catch (InterruptedException interrupted) { throw new IllegalStateException(interrupted); }
                super.write(text);
            }
        };
    }
}
