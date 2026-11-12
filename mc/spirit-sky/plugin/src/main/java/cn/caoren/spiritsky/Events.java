package cn.caoren.spiritsky;
import java.util.*;
import org.bukkit.*;
import org.bukkit.entity.*;
import org.bukkit.event.*;
import org.bukkit.event.block.*;
import org.bukkit.event.entity.*;
import org.bukkit.event.inventory.*;
import org.bukkit.event.player.*;
import org.bukkit.inventory.EquipmentSlot;
import org.bukkit.inventory.ItemStack;
import org.bukkit.potion.*;
final class Events implements Listener {
    final Game g;
    record ArrowShot(UUID shooter,Rules.Side side,double force,long tick) {
    }
    final Map<UUID,ArrowShot> arrows=new HashMap<>();
    final Map<UUID,Rules.Stock> potions=new HashMap<>();
    final Map<UUID,Set<UUID>> potionAffected=new HashMap<>();
    Events(Game g){
        this.g=g;
    }
    @EventHandler public void tickEnd(com.destroystokyo.paper.event.server.ServerTickEndEvent e){
        g.resolveResult();
    }
    boolean participant(Player p){
        Member m=g.member(p);
        return m!=null&&!m.away;
    }
    boolean locked(Player p){
        return participant(p)||g.recoveryLocks.containsKey(p.getUniqueId());
    }
    boolean world(Entity e){
        return e.getWorld()==g.map.world;
    }
    Entity source(Entity e){
        if(e instanceof Projectile p&&p.getShooter() instanceof Entity s)return s;
        return e;
    }
    @EventHandler public void join(PlayerJoinEvent e){
        g.connected(e.getPlayer());
    }
    @EventHandler public void quit(PlayerQuitEvent e){
        g.disconnected(e.getPlayer());
    }
    @EventHandler(priority=EventPriority.HIGHEST) public void damage(EntityDamageEvent event){
        Entity victim=event.getEntity();
        Member v=g.member(victim);
        boolean managed=g.owned(victim)||(v!=null&&!v.away);
        if(!managed)return;
        // Paper pre-cancels hits on invisible armor stands. Our registered cores consume
        // these hits into shared HP while keeping the underlying stand damage cancelled.
        if(event.isCancelled()&&!g.combat.cores.containsKey(victim.getUniqueId()))return;
        if(!g.running()||v!=null&&!v.active()){
            event.setCancelled(true);
            return;
        }
        if(v!=null&&g.tick<v.protectUntil){
            event.setCancelled(true);
            return;
        }
        Entity attacker=event instanceof EntityDamageByEntityEvent e?source(e.getDamager()):g.combat.damageSource;
        if(attacker!=null){
            ArrowShot shot=event instanceof EntityDamageByEntityEvent e?arrows.get(e.getDamager().getUniqueId()):null;
            Rules.Side aSide=shot!=null?shot.side:g.combat.damageSide!=null?g.combat.damageSide:g.side(attacker),vSide=g.side(victim);
            Member a=g.member(attacker);
            if(aSide==null||vSide==null||aSide==vSide||shot==null&&a!=null&&!a.active()){
                event.setCancelled(true);
                return;
            }
            if(shot!=null)event.setDamage(Rules.arrowRaw(shot.force,(int)(g.tick-shot.tick)));
            if(a!=null&&a.active())g.attack(a);
            Combat.Building building=g.combat.cores.get(victim.getUniqueId());
            if(building!=null){
                event.setCancelled(true);
                g.combat.hit(building,event.getDamage(),attacker,shot!=null,aSide);
                if(event instanceof EntityDamageByEntityEvent e&&e.getDamager() instanceof AbstractArrow)e.getDamager().remove();
                return;
            }
            if(v!=null){
                v.combat(g.tick);
                g.combat.attackedPlayer(v,attacker);
            }
            g.fleet.hurt(victim);
        }
        else if(g.combat.cores.containsKey(victim.getUniqueId())){
            event.setCancelled(true);
            return;
        }
        if(v!=null&&event.getFinalDamage()>=v.player().getHealth()){
            event.setCancelled(true);
            g.die(v);
        }
    }
    @EventHandler(priority=EventPriority.HIGHEST,ignoreCancelled=true) public void externalAttack(EntityDamageByEntityEvent e){
        Entity source=source(e.getDamager());
        Member m=g.member(source);
        if(m!=null&&!m.away&&(!g.running()||!m.active()&&!arrows.containsKey(e.getDamager().getUniqueId())||g.side(e.getEntity())==null))e.setCancelled(true);
    }
    @EventHandler(priority=EventPriority.HIGHEST,ignoreCancelled=true) public void knock(io.papermc.paper.event.entity.EntityPushedByEntityAttackEvent e){
        Entity source=source(e.getPushedBy());
        Member m=g.member(e.getEntity());
        if((g.side(source)!=null&&g.side(source)==g.side(e.getEntity()))||m!=null&&(!m.active()||g.tick<m.protectUntil))e.setCancelled(true);
    }
    @EventHandler(priority=EventPriority.HIGHEST,ignoreCancelled=true) public void bow(EntityShootBowEvent e){
        if(!(e.getEntity() instanceof Player p)||!locked(p))return;
        Member m=g.member(p);
        if(!g.running()||m==null||!m.active()||m.role!=Rules.Role.ARCHOR||!g.kits.spend(m,Rules.Stock.ARROW)){
            e.setCancelled(true);
            return;
        }
        e.setConsumeItem(false);
        if(e.getProjectile() instanceof AbstractArrow arrow){
            g.mark(arrow);
            arrow.setCritical(false);
            arrow.setPickupStatus(AbstractArrow.PickupStatus.DISALLOWED);
            arrows.put(arrow.getUniqueId(),new ArrowShot(p.getUniqueId(),m.side,e.getForce(),g.tick));
        }
        g.attack(m);
        g.kits.sync(m);
    }
    @EventHandler(priority=EventPriority.HIGHEST) public void use(PlayerInteractEvent e){
        Player p=e.getPlayer();
        if(!locked(p)&&p.getWorld()!=g.map.world)return;
        if(e.getHand()!=EquipmentSlot.HAND){
            e.setCancelled(true);
            return;
        }
        boolean right=e.getAction()==Action.RIGHT_CLICK_AIR||e.getAction()==Action.RIGHT_CLICK_BLOCK;
        if(e.getAction()==Action.RIGHT_CLICK_BLOCK)e.setUseInteractedBlock(Event.Result.DENY);
        Member m=g.member(p);
        String kind=g.kits.kind(e.getItem());
        if(!participant(p)||m==null||!g.running()||!m.active()){
            e.setCancelled(true);
            return;
        }
        if(!right){
            if(e.getAction()==Action.LEFT_CLICK_AIR||e.getAction()==Action.LEFT_CLICK_BLOCK)g.attack(m);
            return;
        }
        if("HOME".equals(kind)){
            e.setCancelled(true);
            g.menus.open(p);
            return;
        }
        if("STATION".equals(kind)){
            e.setCancelled(true);
            Location aim=e.getClickedBlock()!=null?e.getClickedBlock().getLocation().add(.5,1,.5):p.getLocation();
            String id=g.map.slots.entrySet().stream().min(Comparator.comparingDouble(x->x.getValue().distanceSquared(aim))).map(Map.Entry::getKey).orElse(null);
            if(id!=null)g.combat.deploy(m,id);
            return;
        }
        if("HEAL".equals(kind)||"RESIST".equals(kind)||"SPEED".equals(kind)){
            e.setCancelled(true);
            Rules.Stock type=Rules.Stock.valueOf(kind);
            if(g.kits.spend(m,type)){
                ThrownPotion potion=p.launchProjectile(ThrownPotion.class);
                potion.setItem(g.kits.stock(type,1));
                g.mark(potion);
                potions.put(potion.getUniqueId(),type);
                g.kits.sync(m);
            }
            return;
        }
        // Vanilla bow, spear and drinkable strength keep their native use/charge/cooldown.
        if(!Set.of("BOW","SPEAR","STRENGTH","SWORD","ENEMY").contains(kind==null?"":kind))e.setCancelled(true);
    }
    @EventHandler(priority=EventPriority.HIGHEST,ignoreCancelled=true) public void consume(PlayerItemConsumeEvent e){
        if(!locked(e.getPlayer()))return;
        Member m=g.member(e.getPlayer());
        e.setCancelled(true);
        if(g.running()&&m!=null&&"STRENGTH".equals(g.kits.kind(e.getItem()))&&g.kits.spend(m,Rules.Stock.STRENGTH)){
            effect(e.getPlayer(),new PotionEffect(PotionEffectType.STRENGTH,160,0));
            g.kits.sync(m);
        }
    }
    static void effect(LivingEntity e,PotionEffect effect){
        PotionEffect old=e.getPotionEffect(effect.getType());
        if(old==null||old.getAmplifier()<=effect.getAmplifier())e.addPotionEffect(effect);
    }
    @EventHandler(priority=EventPriority.HIGHEST) public void splash(PotionSplashEvent e){
        // 26.3 can emit both the water splash and custom-effect splash for one bottle.
        // Keep its identity until despawn; each target receives our effect only once.
        Rules.Stock type=potions.get(e.getEntity().getUniqueId());
        if(type==null){
            if(world(e.getEntity()))e.setCancelled(true);
            return;
        }
        e.setCancelled(true);
        if(!g.running())return;
        for(LivingEntity target:e.getAffectedEntities()){
            if(!g.alive(target)||g.combat.cores.containsKey(target.getUniqueId()))continue;
            if(!potionAffected.computeIfAbsent(e.getEntity().getUniqueId(),id->new HashSet<>()).add(target.getUniqueId()))continue;
            double intensity=e.getIntensity(target);
            if(type==Rules.Stock.HEAL){
                if(g.fleet.side(target)!=null)g.fleet.heal(target);
                else target.setHealth(Math.min(target.getAttribute(org.bukkit.attribute.Attribute.MAX_HEALTH).getValue(),target.getHealth()+Math.round(4*intensity)));
            }
            else{
                PotionEffect effect=new PotionEffect(type==Rules.Stock.RESIST?PotionEffectType.RESISTANCE:PotionEffectType.SPEED,(int)Math.round((type==Rules.Stock.RESIST?40:200)*intensity),type==Rules.Stock.RESIST?1:0);
                effect(target,effect);
                if(type==Rules.Stock.SPEED&&g.fleet.side(target)!=null)g.fleet.boost(target);
            }
        }
    }
    @EventHandler(priority=EventPriority.HIGHEST) public void interactEntity(PlayerInteractEntityEvent e){
        if(!locked(e.getPlayer())&&e.getRightClicked().getWorld()!=g.map.world)return;
        e.setCancelled(true);
        Member m=g.member(e.getPlayer());
        if(e.getHand()==EquipmentSlot.HAND&&m!=null&&e.getRightClicked() instanceof HappyGhast ship)g.fleet.board(m,ship);
    }
    @EventHandler(priority=EventPriority.HIGHEST) public void mount(EntityMountEvent e){
        if(e.getEntity() instanceof Player p&&(locked(p)||g.owned(e.getMount()))){
            Member m=g.member(p);
            if(m==null||!m.active()||!g.running()||g.fleet.side(e.getMount())!=m.side)e.setCancelled(true);
            else m.protectUntil=0;
        }
    }
    @EventHandler public void death(EntityDeathEvent e){
        if(g.owned(e.getEntity())){
            e.getDrops().clear();
            e.setDroppedExp(0);
            if(g.fleet.side(e.getEntity())!=null)g.fleet.died(e.getEntity());
        }
        if(e.getEntity() instanceof Player p&&participant(p)){
            e.getDrops().clear();
            e.setDroppedExp(0);
            Member m=g.member(p);
            m.dead=true;
            m.respawnAt=g.tick+Rules.playerRespawn(g.elapsed());
        }
    }
    @EventHandler public void respawn(PlayerRespawnEvent e){
        Member m=g.member(e.getPlayer());
        if(m!=null&&!m.away){
            e.setRespawnLocation(g.map.lobby);
            Bukkit.getScheduler().runTask(g.plugin,()->{
                if(g.running())g.ghost(m);
                else g.lobby(m);
            }
            );
        }
    }
    @EventHandler(priority=EventPriority.HIGHEST) public void inventory(InventoryClickEvent e){
        if(!(e.getWhoClicked() instanceof Player p))return;
        if(e.getView().getTopInventory().getHolder() instanceof Menus.Holder h){
            e.setCancelled(true);
            if(e.getRawSlot()>=0&&e.getRawSlot()<h.inventory.getSize())g.menus.click(p,h,e.getRawSlot());
        }
        else if(locked(p))e.setCancelled(true);
    }
    @EventHandler(priority=EventPriority.HIGHEST) public void drag(InventoryDragEvent e){
        if(e.getWhoClicked() instanceof Player p&&(locked(p)||e.getInventory().getHolder() instanceof Menus.Holder))e.setCancelled(true);
    }
    @EventHandler(priority=EventPriority.HIGHEST) public void open(InventoryOpenEvent e){
        if(e.getPlayer() instanceof Player p&&locked(p)&&!(e.getInventory().getHolder() instanceof Menus.Holder))e.setCancelled(true);
    }
    @EventHandler(priority=EventPriority.HIGHEST) public void drop(PlayerDropItemEvent e){
        if(locked(e.getPlayer()))e.setCancelled(true);
    }
    @EventHandler(priority=EventPriority.HIGHEST) public void pickup(EntityPickupItemEvent e){
        if(world(e.getEntity())||e.getEntity() instanceof Player p&&locked(p))e.setCancelled(true);
    }
    @EventHandler(priority=EventPriority.HIGHEST) public void swap(PlayerSwapHandItemsEvent e){
        if(locked(e.getPlayer()))e.setCancelled(true);
    }
    @EventHandler(priority=EventPriority.HIGHEST) public void breakBlock(BlockBreakEvent e){
        if(e.getBlock().getWorld()==g.map.world||locked(e.getPlayer()))e.setCancelled(true);
    }
    @EventHandler(priority=EventPriority.HIGHEST) public void place(BlockPlaceEvent e){
        if(e.getBlock().getWorld()==g.map.world||locked(e.getPlayer()))e.setCancelled(true);
    }
    @EventHandler(priority=EventPriority.HIGHEST) public void hunger(FoodLevelChangeEvent e){
        if(e.getEntity() instanceof Player p&&participant(p)){
            e.setCancelled(true);
            p.setFoodLevel(20);
        }
    }
    @EventHandler(priority=EventPriority.HIGHEST) public void regen(EntityRegainHealthEvent e){
        Member m=g.member(e.getEntity());
        if(g.fleet.side(e.getEntity())!=null||m!=null&&!m.away&&e.getRegainReason()!=EntityRegainHealthEvent.RegainReason.MAGIC&&e.getRegainReason()!=EntityRegainHealthEvent.RegainReason.MAGIC_REGEN)e.setCancelled(true);
    }
    @EventHandler(priority=EventPriority.HIGHEST) public void spawn(CreatureSpawnEvent e){
        if(world(e.getEntity())&&e.getSpawnReason()!=CreatureSpawnEvent.SpawnReason.CUSTOM)e.setCancelled(true);
    }
    @EventHandler(priority=EventPriority.HIGHEST) public void explosion(EntityExplodeEvent e){
        if(world(e.getEntity())){
            e.blockList().clear();
            e.setCancelled(true);
        }
    }
    @EventHandler(priority=EventPriority.HIGHEST) public void ignite(BlockIgniteEvent e){
        if(e.getBlock().getWorld()==g.map.world)e.setCancelled(true);
    }
    @EventHandler(priority=EventPriority.HIGHEST) public void move(PlayerMoveEvent e){
        Member m=g.member(e.getPlayer());
        if(m==null||m.away||!e.hasChangedPosition())return;
        if(m.active()&&g.running()&&!g.map.inside(e.getTo())){
            e.setTo(e.getFrom());
            return;
        }
        if(!g.running()&&!m.observer&&MapLayout.horizontal(e.getTo(),g.map.lobby)>10){
            e.setTo(e.getFrom());
            return;
        }
        if(g.running()&&m.active()&&e.getTo().getY()<g.map.ground-16)g.die(m);
    }
    @EventHandler(priority=EventPriority.HIGHEST) public void teleport(PlayerTeleportEvent e){
        if(participant(e.getPlayer())&&e.getCause()!=PlayerTeleportEvent.TeleportCause.PLUGIN&&e.getCause()!=PlayerTeleportEvent.TeleportCause.SPECTATE)e.setCancelled(true);
    }
    @EventHandler public void worldChanged(PlayerChangedWorldEvent e){
        Member m=g.member(e.getPlayer());
        if(m!=null&&!m.away&&e.getPlayer().getWorld()!=g.map.world)g.leave(e.getPlayer());
    }
    @EventHandler(priority=EventPriority.HIGHEST) public void commands(PlayerCommandPreprocessEvent e){
        Member m=g.member(e.getPlayer());
        if(m!=null&&!m.away){
            String root=e.getMessage().split(" ",2)[0].toLowerCase(Locale.ROOT);
            if(!root.equals("/sss")&&!root.equals("/caorenspiritsky:sss")){
                e.setCancelled(true);
                g.tell(e.getPlayer(),"对局区域内请使用 /sss；切换玩法前需管理员取消对局。");
            }
        }
    }
    void prune(){
        arrows.entrySet().removeIf(e->Bukkit.getEntity(e.getKey())==null||g.tick-e.getValue().tick>1200);
        potions.keySet().removeIf(id->Bukkit.getEntity(id)==null);
        potionAffected.keySet().retainAll(potions.keySet());
    }
}
