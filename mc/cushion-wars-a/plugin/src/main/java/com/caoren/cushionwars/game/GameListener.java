package com.caoren.cushionwars.game;
import com.caoren.cushionwars.probe.*;
import org.bukkit.*;
import org.bukkit.entity.*;
import org.bukkit.event.*;
import org.bukkit.event.entity.*;
import org.bukkit.event.player.*;
import org.bukkit.event.inventory.*;
import org.bukkit.event.block.Action;
import org.bukkit.inventory.*;
public final class GameListener implements Listener {
    private final CushionWarsProbePlugin plugin;
    public GameListener(CushionWarsProbePlugin plugin){this.plugin=plugin;}
    @EventHandler(priority=EventPriority.HIGH)
    public void onDeath(PlayerDeathEvent e){
        if(!plugin.game().running()||!plugin.game().participant(e.getEntity()))return;
        e.getDrops().clear();e.setDroppedExp(0);plugin.game().eliminate(e.getEntity());
    }
    @EventHandler public void onRespawn(PlayerRespawnEvent e){
        Player p=e.getPlayer();if(!plugin.arena().inWorld(p.getLocation())&&!plugin.recovery().pending(p))return;
        e.setRespawnLocation(plugin.arena().lobby());
        Bukkit.getScheduler().runTask(plugin,()->{
            if(!p.isOnline())return;
            if(plugin.game().running()&&plugin.state().sideOf(p.getUniqueId())!=null){p.setGameMode(GameMode.SPECTATOR);return;}
            plugin.recovery().restore(p);plugin.lobby().give(p);
        });
    }
    @EventHandler public void onJoin(PlayerJoinEvent e){
        Player p=e.getPlayer();
        Bukkit.getScheduler().runTask(plugin,()->{
            if(!p.isOnline())return;
            if(plugin.game().running()&&plugin.state().sideOf(p.getUniqueId())!=null){
                plugin.game().eliminate(p);p.setGameMode(GameMode.SPECTATOR);
            }else{
                if(plugin.recovery().pending(p))plugin.recovery().restore(p);
                if(plugin.arena().inWorld(p.getLocation()))plugin.game().recoverWaiting(p);
            }
            plugin.lobby().updateTeams();
        });
    }
    @EventHandler public void onWorldChange(PlayerChangedWorldEvent e){Player p=e.getPlayer();if(!plugin.arena().inWorld(p.getLocation())){if(plugin.game().running()&&plugin.game().member(p))plugin.game().eliminate(p);plugin.lobby().forget(p);}else plugin.game().recoverWaiting(p);plugin.lobby().updateTeams();}
    @EventHandler public void onQuit(PlayerQuitEvent e){plugin.game().onQuit(e.getPlayer());plugin.lobby().forget(e.getPlayer());}
    @EventHandler(priority=EventPriority.HIGHEST,ignoreCancelled=false)
    public void onControl(PlayerInteractEvent e){
        if(e.getHand()!=EquipmentSlot.HAND||!plugin.arena().inWorld(e.getPlayer().getLocation()))return;
        if(e.getAction()!=Action.RIGHT_CLICK_AIR&&e.getAction()!=Action.RIGHT_CLICK_BLOCK)return;
        String action=plugin.lobby().action(e.getItem());
        if(action!=null){
            e.setCancelled(true);
            String error=action.equals("leave")?plugin.game().leave(e.getPlayer()):plugin.game().join(e.getPlayer(),Side.parse(action));
            if(error!=null)e.getPlayer().sendMessage(ProbeText.warn(error));return;
        }
        if(e.getItem()!=null&&e.getItem().getType()==Material.CLOCK&&plugin.game().participant(e.getPlayer())){
            e.setCancelled(true);plugin.game().confirm(e.getPlayer());
        }
    }
    @EventHandler(priority=EventPriority.HIGHEST,ignoreCancelled=true)
    public void onMove(PlayerMoveEvent e){
        Player p=e.getPlayer();if(plugin.game().running()||plugin.game().editing(p)||!plugin.arena().inWorld(p.getLocation())||p.isDead())return;
        Location l=e.getTo();if(l==null)return;
        if(!plugin.arena().waitingSafe(l)){
            e.setTo(plugin.arena().lobby());p.setFallDistance(0);p.resetFlyingTicks();
        }
    }
    @EventHandler(priority=EventPriority.HIGHEST,ignoreCancelled=true)
    public void onTeleport(PlayerTeleportEvent e){
        if(!plugin.game().running()&&!plugin.game().editing(e.getPlayer())&&e.getTo()!=null&&plugin.arena().inWorld(e.getTo())&&!plugin.arena().waitingSafe(e.getTo()))e.setTo(plugin.arena().lobby());
    }
    @EventHandler(priority=EventPriority.HIGHEST,ignoreCancelled=false)
    public void onDamage(EntityDamageEvent e){
        if(!(e.getEntity() instanceof Player p)||!plugin.arena().inWorld(p.getLocation())||plugin.game().editing(p))return;
        if(plugin.game().running()&&plugin.game().participant(p)&&e instanceof EntityDamageByEntityEvent by
                &&by.getDamager() instanceof AbstractArrow&&plugin.projectiles().tracked(by.getDamager()))return;
        e.setCancelled(true);
    }
    @EventHandler(priority=EventPriority.HIGHEST,ignoreCancelled=true)
    public void onDrop(PlayerDropItemEvent e){
        if(plugin.arena().inWorld(e.getPlayer().getLocation())&&!plugin.game().editing(e.getPlayer())||plugin.lobby().action(e.getItemDrop().getItemStack())!=null)e.setCancelled(true);
    }
    @EventHandler(priority=EventPriority.HIGHEST,ignoreCancelled=true)
    public void onInventory(InventoryClickEvent e){
        if(!(e.getWhoClicked() instanceof Player p)||!plugin.arena().inWorld(p.getLocation())||plugin.game().editing(p))return;
        if(plugin.lobby().action(e.getCurrentItem())!=null||plugin.lobby().action(e.getCursor())!=null){e.setCancelled(true);return;}
        if(plugin.game().running()&&plugin.game().participant(p)&&e.getView().getTopInventory().getType()!=InventoryType.CRAFTING)e.setCancelled(true);
        if(e.getRawSlot()==-999||e.getAction()==InventoryAction.DROP_ALL_CURSOR||e.getAction()==InventoryAction.DROP_ALL_SLOT||e.getAction()==InventoryAction.DROP_ONE_CURSOR||e.getAction()==InventoryAction.DROP_ONE_SLOT)e.setCancelled(true);
    }
    @EventHandler(priority=EventPriority.HIGHEST,ignoreCancelled=true)
    public void onDrag(InventoryDragEvent e){
        if(plugin.lobby().action(e.getOldCursor())!=null)e.setCancelled(true);
    }
    @EventHandler(priority=EventPriority.HIGHEST,ignoreCancelled=false)
    public void onConsume(PlayerItemConsumeEvent e){
        if(plugin.game().participant(e.getPlayer())&&(!plugin.game().running()||plugin.game().phase()==GamePhase.INTRO||plugin.game().phase()==GamePhase.PREPARE||plugin.game().eliminated(e.getPlayer().getUniqueId())))e.setCancelled(true);
        if(!e.isCancelled()&&plugin.game().participant(e.getPlayer())&&e.getItem().getType()==Material.POTION&&MatchItems.owned(e.getItem()))e.setReplacement(new ItemStack(Material.AIR));
    }
}
