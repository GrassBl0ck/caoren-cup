package com.caoren.cushionwars.probe;
import org.bukkit.*;
import io.papermc.paper.event.entity.EntityBreakEvent;
import io.papermc.paper.event.entity.EntityBreakByEntityEvent;
import org.bukkit.block.*;
import org.bukkit.block.data.*;
import org.bukkit.block.data.type.Door;
import org.bukkit.entity.*;
import org.bukkit.event.*;
import org.bukkit.event.block.*;
import org.bukkit.event.entity.*;
import org.bukkit.event.player.*;
import org.bukkit.inventory.*;

/** AP 事务在主线程完成；关闭原版放置/掉落通路，仅通过规则入口改比赛物件。 */
public final class GateListener implements Listener {
    private final CushionWarsProbePlugin plugin;
    public GateListener(CushionWarsProbePlugin plugin){this.plugin=plugin;}
    private boolean act(Player p){
        if(!plugin.game().canAct(p)){p.sendMessage(ProbeText.warn("现在不是你的行动阶段，或你已确认结束。"));return false;}
        if(!plugin.state().hasAp(p.getUniqueId())){p.sendMessage(ProbeText.warn("AP 不足，操作未生效。"));return false;}return true;
    }
    private Cushion seat(Block ground){
        for(Entity entity:ground.getWorld().getNearbyEntities(ground.getLocation().add(.5,1,.5),.4,.9,.4))
            if(entity instanceof Cushion c&&c.getLocation().getBlockX()==ground.getX()&&c.getLocation().getBlockZ()==ground.getZ())return c;
        return null;
    }
    @EventHandler(priority=EventPriority.HIGHEST,ignoreCancelled=false)
    public void onInteract(PlayerInteractEvent e){
        Player p=e.getPlayer();if(!plugin.arena().inWorld(p.getLocation())||plugin.game().editing(p))return;
        if(e.getHand()!=EquipmentSlot.HAND){
            ItemStack off=e.getItem();if(off!=null&&(off.getType().name().endsWith("_CUSHION")||off.getType()==Material.OAK_DOOR))e.setCancelled(true);
            return;
        }
        if(e.getAction()==Action.PHYSICAL){e.setCancelled(true);return;}
        if(e.getAction()!=Action.RIGHT_CLICK_BLOCK)return;
        Block clicked=e.getClickedBlock();if(clicked==null)return;
        ItemStack item=p.getInventory().getItemInMainHand();Material material=item.getType();
        if(clicked.getBlockData() instanceof Door door){
            e.setUseInteractedBlock(Event.Result.DENY);
            if(!material.isAir())return; // 主手持弓/药水等仍保留其物品用途。
            e.setCancelled(true);
            Block base=door.getHalf()==Bisected.Half.TOP?clicked.getRelative(BlockFace.DOWN):clicked;
            if(!plugin.arena().ground(base.getRelative(BlockFace.DOWN))||!InteractionChecks.block(p,clicked)||!act(p))return;
            var debit=plugin.state().debit(p.getUniqueId());if(debit==null)return;
            try{boolean open=!door.isOpen();for(int y=0;y<2;y++){Block b=base.getRelative(0,y,0);if(b.getBlockData() instanceof Door d){d.setOpen(open);b.setBlockData(d,false);}}}
            catch(RuntimeException ex){plugin.state().refund(debit);throw ex;}
            return;
        }
        boolean cushion=material.name().endsWith("_CUSHION"),isDoor=material==Material.OAK_DOOR;
        if(!cushion&&!isDoor){
            e.setUseInteractedBlock(Event.Result.DENY);
            if(material!=Material.BOW&&material!=Material.POTION&&material!=Material.SPLASH_POTION)e.setUseItemInHand(Event.Result.DENY);
            return;
        }
        e.setCancelled(true);
        if(!act(p)||!InteractionChecks.block(p,clicked)||e.getBlockFace()!=BlockFace.UP||!plugin.arena().ground(clicked))return;
        if(!clicked.getRelative(BlockFace.UP).getType().isAir()||seat(clicked)!=null)return;
        if(isDoor&&!clicked.getRelative(0,2,0).getType().isAir())return;
        Side side=plugin.state().sideOf(p.getUniqueId());
        if(cushion&&material!=(side==Side.RED?Material.RED_CUSHION:Material.BLUE_CUSHION))return;
        var debit=plugin.state().debit(p.getUniqueId());if(debit==null)return;
        try{
            if(cushion)plugin.arena().spawn(clicked.getX()-plugin.config().centerX(),clicked.getZ()-plugin.config().centerZ(),side);
            else for(int y=1;y<=2;y++){
                Door data=(Door)Material.OAK_DOOR.createBlockData();data.setHalf(y==1?Bisected.Half.BOTTOM:Bisected.Half.TOP);
                BlockFace face=p.getFacing().getOppositeFace();data.setFacing(face);clicked.getRelative(0,y,0).setBlockData(data,false);
            }
            item.setAmount(item.getAmount()-1);plugin.game().centerChanged();
        }catch(RuntimeException ex){plugin.state().refund(debit);throw ex;}
    }
    @EventHandler(priority=EventPriority.HIGHEST,ignoreCancelled=false)
    public void onBreak(BlockBreakEvent e){
        if(!plugin.arena().inWorld(e.getBlock().getLocation())||plugin.game().editing(e.getPlayer()))return;
        e.setCancelled(true);
        if(!(e.getBlock().getBlockData() instanceof Door door))return;
        Block base=door.getHalf()==Bisected.Half.TOP?e.getBlock().getRelative(BlockFace.DOWN):e.getBlock();
        if(!plugin.arena().ground(base.getRelative(BlockFace.DOWN))||!InteractionChecks.block(e.getPlayer(),e.getBlock())||!act(e.getPlayer()))return;
        if(!plugin.state().consumeAp(e.getPlayer().getUniqueId()))return;
        base.getRelative(BlockFace.UP).setType(Material.AIR,false);base.setType(Material.AIR,false);
    }
    @EventHandler(priority=EventPriority.HIGHEST,ignoreCancelled=false)
    public void onEntityBreak(EntityBreakEvent e){
        if(!(e.getEntity() instanceof Cushion c)||!plugin.arena().inWorld(c.getLocation()))return;
        if(e instanceof EntityBreakByEntityEvent by&&by.getRemover() instanceof Player p&&plugin.game().editing(p))return;
        e.setCancelled(true);
        if(!(e instanceof EntityBreakByEntityEvent by)||!(by.getRemover() instanceof Player p)||!c.isValid()||!c.getPassengers().isEmpty()||!plugin.arena().inside(c.getLocation())||!InteractionChecks.entity(p,c)||!act(p))return;
        if(!plugin.state().consumeAp(p.getUniqueId()))return;
        plugin.state().removeSeat(c.getLocation());c.remove();plugin.game().centerChanged();
    }
    @EventHandler(priority=EventPriority.HIGHEST,ignoreCancelled=false)
    public void onCushionDamage(EntityDamageEvent e){
        if(e.getEntity() instanceof Cushion&&plugin.arena().inWorld(e.getEntity().getLocation())){
            if(e instanceof EntityDamageByEntityEvent by&&by.getDamager() instanceof Player p&&plugin.game().editing(p))return;
            e.setCancelled(true);
        }
    }
    @EventHandler(priority=EventPriority.HIGHEST,ignoreCancelled=false)
    public void onEntityPlace(EntityPlaceEvent e){if(plugin.arena().inWorld(e.getEntity().getLocation())&&(e.getPlayer()==null||!plugin.game().editing(e.getPlayer())))e.setCancelled(true);}
    @EventHandler(priority=EventPriority.HIGHEST,ignoreCancelled=false)
    public void onPlace(BlockPlaceEvent e){
        if(!plugin.arena().inWorld(e.getBlock().getLocation()))return;
        if(!plugin.game().editing(e.getPlayer())){e.setCancelled(true);return;}
        Block b=e.getBlock();int max=BoardLayout.half(8);
        if(Math.abs(b.getX()-plugin.config().centerX())<=max&&Math.abs(b.getZ()-plugin.config().centerZ())<=max&&b.getY()>plugin.config().groundY()+2){
            e.setCancelled(true);e.getPlayer().sendMessage(ProbeText.warn("比赛施工区必须露天，掩体最多高两格；景观请在最大47×47棋盘外建设。"));
        }
    }
    @EventHandler(priority=EventPriority.HIGHEST,ignoreCancelled=false)
    public void onExplosion(EntityExplodeEvent e){if(plugin.arena().inWorld(e.getLocation()))e.blockList().clear();}
    @EventHandler(priority=EventPriority.HIGHEST,ignoreCancelled=false)
    public void onBlockExplosion(BlockExplodeEvent e){if(plugin.arena().inWorld(e.getBlock().getLocation()))e.blockList().clear();}
    @EventHandler(priority=EventPriority.HIGHEST,ignoreCancelled=false)
    public void onBucketEmpty(PlayerBucketEmptyEvent e){if(plugin.arena().inWorld(e.getPlayer().getLocation())&&!plugin.game().editing(e.getPlayer()))e.setCancelled(true);}
    @EventHandler(priority=EventPriority.HIGHEST,ignoreCancelled=false)
    public void onBucketFill(PlayerBucketFillEvent e){if(plugin.arena().inWorld(e.getPlayer().getLocation())&&!plugin.game().editing(e.getPlayer()))e.setCancelled(true);}
    @EventHandler(priority=EventPriority.HIGHEST,ignoreCancelled=false)
    public void onIgnite(BlockIgniteEvent e){if(plugin.arena().inWorld(e.getBlock().getLocation())&&(e.getPlayer()==null||!plugin.game().editing(e.getPlayer())))e.setCancelled(true);}
    @EventHandler(priority=EventPriority.HIGHEST,ignoreCancelled=false)
    public void onChange(EntityChangeBlockEvent e){if(plugin.arena().inWorld(e.getBlock().getLocation()))e.setCancelled(true);}
}
