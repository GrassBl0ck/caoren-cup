package com.caoren.crossera.probe;

import com.google.gson.JsonObject;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardOpenOption;
import java.time.Instant;
import java.util.Objects;
import java.util.logging.Level;
import net.kyori.adventure.text.Component;
import net.kyori.adventure.text.format.NamedTextColor;
import org.bukkit.command.CommandSender;
import org.bukkit.event.EventHandler;
import org.bukkit.event.EventPriority;
import org.bukkit.event.Listener;
import org.bukkit.event.entity.EntityDamageByEntityEvent;
import org.bukkit.event.entity.EntityDamageEvent;
import org.bukkit.plugin.java.JavaPlugin;

/** Observes damage without changing any damage value, cancellation or entity state. */
public final class CrossEraProbe extends JavaPlugin implements Listener {
    private Path records;
    private long damageRecords;
    private long writeErrors;

    @Override
    public void onEnable() {
        records = getDataFolder().toPath().resolve("events.jsonl");
        try {
            Files.createDirectories(records.getParent());
        } catch (IOException error) {
            throw new IllegalStateException("Cannot create probe output directory", error);
        }
        JsonObject startup = event("startup");
        startup.addProperty("serverVersion", getServer().getVersion());
        startup.addProperty("javaVersion", System.getProperty("java.version"));
        append(startup);
        getServer().getPluginManager().registerEvents(this, this);
        Objects.requireNonNull(getCommand("ceprobe")).setExecutor((sender, command, label, args) -> {
            if (!sender.hasPermission("crossera.probe")) {
                reply(sender, "需要管理员权限。");
                return true;
            }
            if (args.length != 1 || !args[0].equalsIgnoreCase("status")) {
                reply(sender, "用法：/ceprobe status");
                return true;
            }
            reply(sender, "开发探针已启用；伤害记录=" + damageRecords + "；写入错误=" + writeErrors
                    + "；OCM=" + getServer().getPluginManager().isPluginEnabled("OldCombatMechanics"));
            return true;
        });
        getLogger().info("[草人杯] 阶段03伤害观察探针已启用。");
    }

    @EventHandler(priority = EventPriority.MONITOR, ignoreCancelled = false)
    public void onDamage(EntityDamageEvent damage) {
        JsonObject record = event("damage");
        record.addProperty("tick", getServer().getCurrentTick());
        record.addProperty("target", damage.getEntity().getUniqueId().toString());
        record.addProperty("targetType", damage.getEntity().getType().name());
        record.addProperty("cause", damage.getCause().name());
        record.addProperty("baseDamage", damage.getDamage());
        record.addProperty("finalDamage", damage.getFinalDamage());
        record.addProperty("cancelled", damage.isCancelled());
        if (damage instanceof EntityDamageByEntityEvent attack) {
            record.addProperty("attacker", attack.getDamager().getUniqueId().toString());
            record.addProperty("attackerType", attack.getDamager().getType().name());
        }
        if (append(record)) {
            damageRecords++;
        }
    }

    @Override
    public void onDisable() {
        if (records != null) {
            JsonObject shutdown = event("shutdown");
            shutdown.addProperty("damageRecords", damageRecords);
            shutdown.addProperty("writeErrors", writeErrors);
            append(shutdown);
        }
    }

    private JsonObject event(String kind) {
        JsonObject record = new JsonObject();
        record.addProperty("kind", kind);
        record.addProperty("timestamp", Instant.now().toString());
        return record;
    }

    private boolean append(JsonObject record) {
        try {
            Files.writeString(records, record + "\n", StandardCharsets.UTF_8,
                    StandardOpenOption.CREATE, StandardOpenOption.APPEND);
            return true;
        } catch (IOException error) {
            writeErrors++;
            getLogger().log(Level.SEVERE, "[草人杯] 伤害观察记录写入失败。", error);
            return false;
        }
    }

    private void reply(CommandSender sender, String message) {
        sender.sendMessage(Component.text("[草人杯] " + message, NamedTextColor.GREEN));
    }
}
