package com.caoren.cushionwars.game;

import com.caoren.cushionwars.probe.CushionWarsProbePlugin;
import java.util.*;
import org.bukkit.Bukkit;
import org.bukkit.entity.*;
import org.bukkit.event.*;
import org.bukkit.event.entity.*;

/** 只跟踪本局投射物；命中后不让落地箭继续拖延结算。 */
public final class MatchProjectiles implements Listener {
    private final CushionWarsProbePlugin plugin;
    private final Map<UUID,Integer> flying=new HashMap<>();
    public MatchProjectiles(CushionWarsProbePlugin plugin) { this.plugin=plugin; }
    public void track(Entity projectile) { projectile.setPersistent(false); if(projectile instanceof AbstractArrow arrow)arrow.setPickupStatus(AbstractArrow.PickupStatus.DISALLOWED); flying.put(projectile.getUniqueId(),Bukkit.getCurrentTick()+200); }
    public boolean tracked(Entity e) { return flying.containsKey(e.getUniqueId()); }
    public int pending() { prune(); return flying.size(); }
    public void prune() {
        var it=flying.entrySet().iterator();
        while(it.hasNext()) {
            var entry=it.next(); Entity entity=Bukkit.getEntity(entry.getKey());
            if(entity==null||!entity.isValid()) { it.remove(); continue; }
            if(Bukkit.getCurrentTick()>=entry.getValue()||entity instanceof AbstractArrow a&&a.isInBlock()) {
                entity.remove(); it.remove();
            }
        }
    }
    public void clear() { for(UUID id:List.copyOf(flying.keySet())) { Entity e=Bukkit.getEntity(id); if(e!=null)e.remove(); } flying.clear(); }
    @EventHandler(priority=EventPriority.HIGHEST,ignoreCancelled=true)
    public void onLaunch(ProjectileLaunchEvent e) {
        if(!(e.getEntity().getShooter() instanceof Player p)||!plugin.game().participant(p)) return;
        if(e.getEntity() instanceof ThrownPotion) {
            if(!plugin.game().running()||plugin.game().phase()==GamePhase.PREPARE||plugin.game().phase()==GamePhase.INTRO||plugin.game().eliminated(p.getUniqueId())) {
                e.setCancelled(true); return;
            }
            track(e.getEntity());
        }
    }
    @EventHandler(priority=EventPriority.MONITOR,ignoreCancelled=true)
    public void onHit(ProjectileHitEvent e) {
        if(!tracked(e.getEntity()))return;
        // 同一 tick 的伤害/药水处理先完成，再清理投射物及切阶段。
        Bukkit.getScheduler().runTask(plugin,()->{flying.remove(e.getEntity().getUniqueId());e.getEntity().remove();});
    }
}
