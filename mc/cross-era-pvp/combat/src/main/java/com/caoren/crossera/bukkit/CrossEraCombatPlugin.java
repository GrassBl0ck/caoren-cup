package com.caoren.crossera.bukkit;

import com.caoren.crossera.core.*;
import com.caoren.crossera.journal.JsonJournal;
import com.caoren.crossera.ocm.OcmBackend;
import com.caoren.crossera.ocm.OcmAuditListener;
import java.io.IOException;
import java.util.*;
import java.util.logging.Level;
import org.bukkit.Bukkit;
import org.bukkit.event.*;
import org.bukkit.event.server.PluginDisableEvent;
import org.bukkit.plugin.ServicePriority;
import org.bukkit.plugin.java.JavaPlugin;

public final class CrossEraCombatPlugin extends JavaPlugin implements Listener {
    public static final String ADMIN = "cross-era.combat.admin";
    private JsonJournal journal;
    private CombatService service;

    @Override public void onEnable() {
        saveDefaultConfig();
        try {
            CombatSettings settings = CombatSettings.from(getConfig().getValues(true));
            CombatBackend backend = settings.enabled() ? OcmBackend.connect() : null;
            service = new CombatService(settings, backend, failure -> getLogger().log(Level.SEVERE, "[草人杯] OCM 接口调用失败。", failure));
            if (settings.enabled()) {
                service.validateStartup();
                journal = JsonJournal.open(getDataFolder().toPath().resolve("events.jsonl"), settings.journalCapacity(),
                        warning -> getLogger().warning("[草人杯] " + warning));
                var record = BukkitSnapshots.event("startup", Bukkit.getCurrentTick());
                record.put("pluginVersion", getPluginMeta().getVersion()); record.put("serverVersion", Bukkit.getVersion());
                record.put("javaVersion", System.getProperty("java.version")); record.put("backend", backend.description());
                record.put("followupPolicy", ObservationEpochs.POLICY);
                record.put("fidelityAccepted", false); journal.record(record);
                Bukkit.getServicesManager().register(CombatService.class, service, this, ServicePriority.Normal);
                Bukkit.getPluginManager().registerEvents(new CombatObserver(this, service, journal), this);
                Bukkit.getPluginManager().registerEvents(new OcmAuditListener(settings, journal), this);
                Bukkit.getPluginManager().registerEvents(this, this);
            }
            var controls = new CombatCommands(service, name -> Optional.ofNullable(Bukkit.getPlayerExact(name)).map(BukkitSnapshots::ref));
            var command = Objects.requireNonNull(getCommand("cecombat"));
            command.setExecutor((sender, cmd, label, args) -> {
                var reply = controls.execute(sender.hasPermission(ADMIN), args);
                Messages.privateReply(sender, reply.text());
                if (sender.hasPermission(ADMIN) && args.length == 1 && args[0].equalsIgnoreCase("status") && journal != null)
                    Messages.privateReply(sender, "日志=" + journal.stats());
                return true;
            });
            command.setTabCompleter((sender, cmd, alias, args) -> {
                if (!sender.hasPermission(ADMIN)) return List.of();
                List<String> choices;
                if (args.length == 1) choices = List.of("profile", "status");
                else if (args.length == 2 && (args[0].equalsIgnoreCase("profile") || args[0].equalsIgnoreCase("status")))
                    choices = Bukkit.getOnlinePlayers().stream().filter(player -> settings.worlds().contains(player.getWorld().getName())).map(player -> player.getName()).toList();
                else if (args.length == 3 && args[0].equalsIgnoreCase("profile")) choices = List.of("old", "modern");
                else return List.of();
                String prefix = args[args.length - 1].toLowerCase(Locale.ROOT);
                return choices.stream().filter(choice -> choice.toLowerCase(Locale.ROOT).startsWith(prefix)).sorted().toList();
            });
            getLogger().info("[草人杯] " + service.summary() + "；原版还原尚未验收。");
        } catch (RuntimeException | LinkageError | IOException failure) {
            getLogger().log(Level.SEVERE, "[草人杯] 实验插件启用失败，停止本插件接管。", failure);
            Bukkit.getPluginManager().disablePlugin(this);
        }
    }
    @EventHandler public void dependencyDisabled(PluginDisableEvent event) {
        if (event.getPlugin().getName().equals("OldCombatMechanics")) {
            getLogger().warning("[草人杯] OCM 已停用，停止本插件；不会自动退回其他战斗后端。");
            Bukkit.getPluginManager().disablePlugin(this);
        }
    }
    @Override public void onDisable() {
        Bukkit.getServicesManager().unregisterAll(this);
        Bukkit.getScheduler().cancelTasks(this);
        HandlerList.unregisterAll((org.bukkit.plugin.Plugin) this);
        if (journal != null) {
            var record = BukkitSnapshots.event("shutdown", Bukkit.getCurrentTick());
            record.put("journalBeforeDrain", journal.stats()); journal.record(record);
            try { journal.close(); getLogger().info("[草人杯] 日志收尾完成：" + journal.stats()); }
            catch (IOException failure) { getLogger().log(Level.SEVERE, "[草人杯] 日志收尾未确认完成。", failure); }
        }
        getLogger().info("[草人杯] 管理插件已停用；已保存的 OCM 选择由 OCM 管理，不代表已恢复现代规则。");
    }
}
