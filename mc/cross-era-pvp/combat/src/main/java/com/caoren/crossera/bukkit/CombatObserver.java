package com.caoren.crossera.bukkit;

import com.caoren.crossera.core.CombatService;
import com.caoren.crossera.journal.JsonJournal;
import java.util.*;
import org.bukkit.Bukkit;
import org.bukkit.entity.Player;
import org.bukkit.event.*;
import org.bukkit.event.entity.EntityDamageByEntityEvent;
import org.bukkit.event.entity.PlayerDeathEvent;
import org.bukkit.event.player.*;
import org.bukkit.plugin.java.JavaPlugin;

/** Observation only: no event setters, health writes, inventory changes or velocity writes. */
final class CombatObserver implements Listener {
    private final JavaPlugin plugin;
    private final CombatService service;
    private final JsonJournal journal;
    private final ObservationEpochs epochs = new ObservationEpochs();
    private long sequence;
    CombatObserver(JavaPlugin plugin, CombatService service, JsonJournal journal) {
        this.plugin = plugin; this.service = service; this.journal = journal;
    }
    private boolean accepts(Player player) { return service.settings().worlds().contains(player.getWorld().getName()); }
    @EventHandler(priority = EventPriority.MONITOR, ignoreCancelled = false)
    public void damage(EntityDamageByEntityEvent event) {
        if (!(event.getDamager() instanceof Player attacker) || !(event.getEntity() instanceof Player victim)
                || !accepts(attacker) || !accepts(victim)) return;
        long id = ++sequence;
        var epoch = epochs.capture(victim.getUniqueId());
        int tick = Bukkit.getCurrentTick();
        var record = BukkitSnapshots.event("damage_event", tick);
        record.put("eventId", id); record.put("samplePhase", "MONITOR_EVENT_VALUES_NOT_ACTUAL_HEALTH_LOSS");
        record.put("victimObservationEpoch", epoch.id());
        record.put("inputProvenance", "BUKKIT_EVENT_NATIVE_OR_SYNTHETIC_UNDETERMINED");
        record.put("cause", event.getCause().name()); record.put("cancelled", event.isCancelled());
        record.put("eventBaseDamage", event.getDamage()); record.put("eventFinalDamage", event.getFinalDamage());
        record.put("attacker", BukkitSnapshots.player(attacker, service)); record.put("victim", BukkitSnapshots.player(victim, service));
        journal.record(record);
        double healthBefore = victim.getHealth();
        double absorptionBefore = victim.getAbsorptionAmount();
        UUID worldId = victim.getWorld().getUID();
        Bukkit.getScheduler().runTask(plugin, () -> {
            var followup = BukkitSnapshots.event("health_followup", Bukkit.getCurrentTick());
            followup.put("eventId", id); followup.put("windowTicks", Bukkit.getCurrentTick() - tick);
            followup.put("isolatedHitGuaranteed", false);
            followup.putAll(epoch.evidence());
            boolean valid = victim.isOnline() && Bukkit.getPlayer(victim.getUniqueId()) == victim
                    && victim.getWorld().getUID().equals(worldId);
            followup.put("sameConnectionAndWorld", valid);
            if (valid && epoch.valid()) {
                followup.put("healthBeforeEventCommit", healthBefore); followup.put("healthAtSample", victim.getHealth());
                followup.put("netHealthDelta", healthBefore - victim.getHealth());
                followup.put("netAbsorptionDelta", absorptionBefore - victim.getAbsorptionAmount());
                followup.put("deadAtSample", victim.isDead());
                followup.put("velocityAtSample", BukkitSnapshots.vector(victim.getVelocity()));
            }
            journal.record(followup);
        });
    }
    @EventHandler(priority = EventPriority.MONITOR, ignoreCancelled = false)
    public void velocity(PlayerVelocityEvent event) {
        if (!accepts(event.getPlayer())) return;
        var record = BukkitSnapshots.event("velocity_event", Bukkit.getCurrentTick());
        record.put("player", event.getPlayer().getUniqueId().toString()); record.put("cancelled", event.isCancelled());
        record.put("eventVelocity", BukkitSnapshots.vector(event.getVelocity()));
        record.put("causallyAttributedToHit", false);
        journal.record(record);
    }
    @EventHandler(priority = EventPriority.MONITOR) public void join(PlayerJoinEvent event) {
        epochs.invalidate(event.getPlayer().getUniqueId(), ObservationEpochs.Reason.JOIN);
        auditLater(event.getPlayer(), "join");
    }
    @EventHandler(priority = EventPriority.MONITOR) public void world(PlayerChangedWorldEvent event) {
        epochs.invalidate(event.getPlayer().getUniqueId(), ObservationEpochs.Reason.WORLD_CHANGE);
        auditLater(event.getPlayer(), "world_change");
    }
    @EventHandler(priority = EventPriority.MONITOR) public void respawn(PlayerRespawnEvent event) {
        epochs.invalidate(event.getPlayer().getUniqueId(), ObservationEpochs.Reason.RESPAWN);
        auditLater(event.getPlayer(), "respawn");
    }
    @EventHandler(priority = EventPriority.MONITOR) public void death(PlayerDeathEvent event) {
        epochs.invalidate(event.getEntity().getUniqueId(), ObservationEpochs.Reason.DEATH);
    }
    @EventHandler(priority = EventPriority.MONITOR) public void quit(PlayerQuitEvent event) {
        epochs.invalidate(event.getPlayer().getUniqueId(), ObservationEpochs.Reason.QUIT);
    }
    private void auditLater(Player player, String reason) {
        Bukkit.getScheduler().runTask(plugin, () -> {
            if (!player.isOnline() || Bukkit.getPlayer(player.getUniqueId()) != player || !accepts(player)) return;
            var record = BukkitSnapshots.event("player_state", Bukkit.getCurrentTick());
            record.put("reason", reason); record.put("player", BukkitSnapshots.player(player, service)); journal.record(record);
        });
    }
}
