package com.caoren.cushionwars.probe;
import org.bukkit.entity.*;
import org.bukkit.event.*;
import org.bukkit.event.entity.*;
import org.bukkit.event.player.PlayerInteractEntityEvent;
import org.bukkit.inventory.EquipmentSlot;

/** 只在本局存活玩家行动期间管制换座和下座；收尾显式放行。 */
public final class SeatListener implements Listener {
    private final CushionWarsProbePlugin plugin;
    public SeatListener(CushionWarsProbePlugin plugin){this.plugin=plugin;}
    public static Side side(Cushion c){return c.getColor()==org.bukkit.DyeColor.RED?Side.RED:c.getColor()==org.bukkit.DyeColor.BLUE?Side.BLUE:null;}
    @EventHandler(priority=EventPriority.HIGHEST,ignoreCancelled=false)
    public void onInteractCushion(PlayerInteractEntityEvent e){
        if(!(e.getRightClicked() instanceof Cushion seat)||!plugin.arena().inWorld(seat.getLocation()))return;
        Player p=e.getPlayer();if(plugin.game().editing(p))return;
        e.setCancelled(true);
        if(e.getHand()!=EquipmentSlot.HAND||!p.getInventory().getItemInMainHand().getType().isAir())return;
        if(!plugin.game().canAct(p)){p.sendMessage(ProbeText.warn("现在不能换座。"));return;}
        if(side(seat)!=plugin.state().sideOf(p.getUniqueId())||!seat.getPassengers().isEmpty()||!plugin.arena().inside(seat.getLocation()))return;
        if(!InteractionChecks.entity(p,seat))return;
        Entity old=p.getVehicle();if(old==seat)return;
        ProbeState.Debit debit=plugin.state().debit(p.getUniqueId());if(debit==null){p.sendMessage(ProbeText.warn("AP 不足，无法换座。"));return;}
        final boolean[] success={false};
        plugin.game().allowDismount(()->{p.leaveVehicle();success[0]=seat.addPassenger(p);if(!success[0]&&old!=null&&old.isValid())old.addPassenger(p);});
        if(!success[0])plugin.state().refund(debit);
    }
    @EventHandler(priority=EventPriority.HIGHEST,ignoreCancelled=false)
    public void onDismount(EntityDismountEvent e){
        if(!(e.getEntity() instanceof Player p)||!(e.getDismounted() instanceof Cushion)||plugin.game().bypassDismount())return;
        if(plugin.game().running()&&plugin.game().participant(p)&&!plugin.game().eliminated(p.getUniqueId())&&!p.isDead())e.setCancelled(true);
    }
    @EventHandler(priority=EventPriority.HIGHEST,ignoreCancelled=false)
    public void onMount(EntityMountEvent e){
        if(!(e.getEntity() instanceof Player p)||!(e.getMount() instanceof Cushion seat)||!plugin.arena().inWorld(seat.getLocation()))return;
        if(plugin.game().bypassDismount()||plugin.game().editing(p))return;
        if(!plugin.game().running()||!plugin.game().participant(p)||plugin.game().eliminated(p.getUniqueId())||side(seat)!=plugin.state().sideOf(p.getUniqueId()))e.setCancelled(true);
    }
}
