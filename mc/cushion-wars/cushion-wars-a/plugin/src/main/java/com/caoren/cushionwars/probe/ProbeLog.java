package com.caoren.cushionwars.probe;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import java.io.BufferedWriter;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardOpenOption;
import java.time.Instant;
import java.time.ZoneId;
import java.time.format.DateTimeFormatter;
import java.util.LinkedHashMap;
import java.util.Map;
import java.util.logging.Level;
import org.bukkit.Bukkit;
import org.bukkit.plugin.Plugin;

/**
 * 探针事件记录：控制台 + JSONL 文件（一行一个事件）。
 *
 * 证据要求见 docs/02-P0-测试协议.md：每次操作都要能对上服务端 tick 与前后状态，
 * 因此每条记录都带 ts、tick、type，其余字段由调用方给出。
 */
public final class ProbeLog {

    private static final DateTimeFormatter FILE_STAMP =
            DateTimeFormatter.ofPattern("yyyyMMdd-HHmmss").withZone(ZoneId.systemDefault());

    private final Plugin plugin;
    private final Gson gson = new GsonBuilder().serializeNulls().disableHtmlEscaping().create();
    private final BufferedWriter writer;
    private final Path path;

    public ProbeLog(Plugin plugin, boolean toFile) {
        this.plugin = plugin;
        BufferedWriter opened = null;
        Path file = null;
        if (toFile) {
            try {
                Path dir = plugin.getDataFolder().toPath();
                Files.createDirectories(dir);
                file = dir.resolve("probe-" + FILE_STAMP.format(Instant.now()) + ".log");
                opened = Files.newBufferedWriter(file, StandardCharsets.UTF_8,
                        StandardOpenOption.CREATE, StandardOpenOption.APPEND);
            } catch (IOException error) {
                plugin.getLogger().log(Level.WARNING, "无法创建探针日志文件，改为只输出控制台", error);
                opened = null;
                file = null;
            }
        }
        this.writer = opened;
        this.path = file;
    }

    public Path path() {
        return path;
    }

    public void event(String type, Map<String, Object> fields) {
        Map<String, Object> record = new LinkedHashMap<>();
        record.put("ts", Instant.now().toString());
        record.put("tick", Bukkit.getCurrentTick());
        record.put("type", type);
        if (fields != null) {
            record.putAll(fields);
        }
        String line = gson.toJson(record);
        plugin.getLogger().info(line);
        if (writer != null) {
            try {
                writer.write(line);
                writer.newLine();
                writer.flush();
            } catch (IOException error) {
                plugin.getLogger().log(Level.WARNING, "写入探针日志失败", error);
            }
        }
    }

    public void event(String type) {
        event(type, null);
    }

    public void close() {
        if (writer == null) {
            return;
        }
        try {
            writer.close();
        } catch (IOException error) {
            plugin.getLogger().log(Level.WARNING, "关闭探针日志失败", error);
        }
    }
}
