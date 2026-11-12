package com.caoren.crossera.journal;

import com.google.gson.Gson;
import java.io.IOException;
import java.io.Writer;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.Map;
import java.util.Objects;
import java.util.concurrent.ArrayBlockingQueue;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicLong;
import java.util.function.Consumer;

/** The worker receives only serialized strings, never Bukkit objects. No blocking producers. */
public final class JsonJournal implements AutoCloseable {
    private final Gson gson = new Gson();
    private final ArrayBlockingQueue<String> queue;
    private final Consumer<String> warning;
    private final Thread worker;
    private final Object gate = new Object();
    private final AtomicLong accepted = new AtomicLong(), rejected = new AtomicLong(), written = new AtomicLong(), errors = new AtomicLong();
    private volatile boolean accepting = true;
    private volatile IOException failure;

    public static JsonJournal open(Path file, int capacity, Consumer<String> warning) throws IOException {
        validate(capacity, warning);
        Files.createDirectories(file.toAbsolutePath().getParent());
        return new JsonJournal(Files.newBufferedWriter(file, StandardCharsets.UTF_8,
                StandardOpenOption.CREATE, StandardOpenOption.APPEND), capacity, warning);
    }
    JsonJournal(Writer output, int capacity, Consumer<String> warning) {
        validate(capacity, warning);
        Objects.requireNonNull(output, "output");
        this.queue = new ArrayBlockingQueue<>(capacity);
        this.warning = warning;
        worker = new Thread(() -> write(output), "CrossEraCombat-journal");
        worker.setDaemon(true);
        worker.start();
    }
    private static void validate(int capacity, Consumer<String> warning) {
        if (capacity < 1) throw new IllegalArgumentException("capacity must be positive");
        Objects.requireNonNull(warning, "warning");
    }
    public boolean record(Map<String, ?> event) {
        final String line;
        try { line = gson.toJson(event); }
        catch (RuntimeException error) {
            errors.incrementAndGet();
            reject("记录包含不可序列化的数据，已拒绝。");
            return false;
        }
        synchronized (gate) {
            if (!accepting || !queue.offer(line)) { reject("日志已关闭或队列已满，记录被拒绝。"); return false; }
            accepted.incrementAndGet();
            return true;
        }
    }
    private void reject(String message) {
        long count = rejected.incrementAndGet();
        if (count == 1 || count % 1024 == 0) warn(message + " 累计拒绝=" + count);
    }
    private void warn(String message) {
        try { warning.accept(message); }
        catch (RuntimeException callbackFailure) { errors.incrementAndGet(); }
    }
    private void write(Writer output) {
        try (output) {
            long lastFlush = System.nanoTime();
            while (accepting || !queue.isEmpty()) {
                String line;
                try { line = queue.poll(250, TimeUnit.MILLISECONDS); }
                catch (InterruptedException interrupted) { continue; }
                if (line != null) { output.write(line); output.write('\n'); written.incrementAndGet(); }
                long now = System.nanoTime();
                if (now - lastFlush >= TimeUnit.MILLISECONDS.toNanos(500) || !accepting) {
                    output.flush(); lastFlush = now;
                }
            }
            output.flush();
        } catch (IOException | RuntimeException error) {
            synchronized (gate) {
                failure = error instanceof IOException io ? io : new IOException("Journal writer failed", error);
                accepting = false;
                errors.incrementAndGet();
                rejected.addAndGet(queue.size());
                queue.clear();
            }
            warn("日志写入失败，后续记录将拒绝；请检查数据目录。错误=" + error.getClass().getSimpleName());
        }
    }
    public Map<String, Object> stats() {
        return Map.of("accepted", accepted.get(), "rejected", rejected.get(), "writtenToBuffer", written.get(),
                "errors", errors.get(), "queued", queue.size(), "workerAlive", worker.isAlive());
    }
    @Override public void close() throws IOException {
        synchronized (gate) { accepting = false; }
        try { worker.join(5000); }
        catch (InterruptedException interrupted) {
            Thread.currentThread().interrupt(); throw new IOException("Interrupted while draining journal", interrupted);
        }
        if (worker.isAlive()) throw new IOException("Journal drain exceeded 5 seconds; completion is unconfirmed");
        if (failure != null) throw new IOException("Journal writer failed", failure);
    }
}
