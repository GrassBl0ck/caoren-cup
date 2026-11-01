package com.caoren.cushionwars.probe;
import java.util.*;
import org.bukkit.Bukkit;
import org.bukkit.Material;
import org.bukkit.entity.*;
import org.bukkit.event.*;
import org.bukkit.event.block.Action;
import org.bukkit.event.entity.*;
import org.bukkit.event.player.PlayerInteractEvent;
import org.bukkit.inventory.ItemStack;

public final class CombatListener implements Listener {
    private final CushionWarsProbePlugin plugin;
    private final Map<UUID,Integer> draw=new HashMap<>();
    private final Map<UUID,Double> damage=new HashMap<>();
    public CombatListener(CushionWarsProbePlugin plugin){this.plugin=plugin;}
    @EventHandler(priority=EventPriority.HIGHEST,ignoreCancelled=false)
    public void onStartDraw(PlayerInteractEvent e){
        Player p=e.getPlayer();if(!plugin.game().participant(p)||p.getInventory().getItemInMainHand().getType()!=Material.BOW)return;
        if(e.getAction()!=Action.RIGHT_CLICK_AIR&&e.getAction()!=Action.RIGHT_CLICK_BLOCK)return;
        if(!plugin.game().canAct(p)){e.setCancelled(true);return;}
        draw.put(p.getUniqueId(),Bukkit.getCurrentTick());
    }
    @EventHandler(priority=EventPriority.HIGHEST,ignoreCancelled=true)
    public void onShoot(EntityShootBowEvent e){
        if(!(e.getEntity() instanceof Player p)||!plugin.game().participant(p))return;
        if(!plugin.game().canAct(p)){e.setCancelled(true);return;}
        var debit=plugin.state().debit(p.getUniqueId());
        if(debit==null){e.setCancelled(true);p.sendMessage(ProbeText.warn("AP 不足，射击无效且不消耗箭。"));return;}
        Integer start=draw.remove(p.getUniqueId());
        int ticks=start==null?(int)Math.round(20*(Math.sqrt(1+3*e.getForce())-1)):Math.min(20,Bukkit.getCurrentTick()-start);
        ItemStack arrow=e.getConsumable();
        double value=plugin.config().arrowMaxDamage()*Math.max(3,Math.min(20,ticks))/20.0
                +(arrow!=null&&arrow.getType()==Material.TIPPED_ARROW?plugin.config().tippedArrowBonus():0);
        Entity entity=e.getProjectile();damage.put(entity.getUniqueId(),value);plugin.projectiles().track(entity);
        if(entity instanceof AbstractArrow a){a.setCritical(false);a.setPickupStatus(AbstractArrow.PickupStatus.DISALLOWED);}
        Bukkit.getScheduler().runTask(plugin,()->{if(e.isCancelled()){plugin.state().refund(debit);damage.remove(entity.getUniqueId());entity.remove();}});
        Bukkit.getScheduler().runTaskLater(plugin,()->damage.remove(entity.getUniqueId()),201);
    }
    @EventHandler(priority=EventPriority.HIGHEST,ignoreCancelled=true)
    public void onArrowDamage(EntityDamageByEntityEvent e){
        if(!(e.getDamager() instanceof AbstractArrow a)||!damage.containsKey(a.getUniqueId()))return;
        if(!plugin.game().running()||!(e.getEntity() instanceof Player p)||!plugin.game().participant(p)){e.setCancelled(true);return;}
        if(a.getShooter() instanceof Player shooter&&plugin.state().sideOf(shooter.getUniqueId())==plugin.state().sideOf(p.getUniqueId())){e.setCancelled(true);return;}
        Double value=damage.remove(a.getUniqueId());if(value!=null)e.setDamage(value);
    }
    public static void disableInvulnerability(Player p){p.setMaximumNoDamageTicks(0);p.setNoDamageTicks(0);}
    public static void restoreInvulnerability(Player p){p.setMaximumNoDamageTicks(20);p.setNoDamageTicks(0);}
}
