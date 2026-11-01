package com.caoren.crossera.ocm;

import com.caoren.crossera.core.CombatSettings;
import com.caoren.crossera.journal.JsonJournal;
import java.time.Instant;
import java.util.LinkedHashMap;
import kernitus.plugin.OldCombatMechanics.api.PlayerModesetChangeEvent;
import org.bukkit.Bukkit;
import org.bukkit.event.*;

public final class OcmAuditListener implements Listener {
    private final CombatSettings settings;
    private final JsonJournal journal;
    public OcmAuditListener(CombatSettings settings, JsonJournal journal) { this.settings = settings; this.journal = journal; }
    @EventHandler(priority = EventPriority.MONITOR)
    public void changed(PlayerModesetChangeEvent event) {
        if (!settings.worlds().contains(event.getWorld().getName())) return;
        var record = new LinkedHashMap<String, Object>();
        record.put("schemaVersion", 1); record.put("kind", "ocm_modeset_change"); record.put("timestamp", Instant.now().toString());
        record.put("tick", Bukkit.getCurrentTick()); record.put("player", event.getPlayer().getUniqueId().toString());
        record.put("worldId", event.getWorld().getUID().toString()); record.put("previous", event.getPreviousModeset());
        record.put("selected", event.getNewModeset()); record.put("reason", event.getReason().name());
        record.put("samplePhase", "OCM_NOTIFICATION_BEFORE_STATE_CHANGE_REFRESH");
        journal.record(record);
    }
}
