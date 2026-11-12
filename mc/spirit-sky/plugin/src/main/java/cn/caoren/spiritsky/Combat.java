package cn.caoren.spiritsky;
import java.util.*;
import org.bukkit.*;
import org.bukkit.attribute.Attribute;
import org.bukkit.entity.*;
import org.bukkit.inventory.ItemStack;
import org.bukkit.util.RayTraceResult;
import org.bukkit.util.Vector;
final class Combat {
    final Game g;
    final Map<String,Building> buildings=new LinkedHashMap<>();
    final Map<UUID,Building> cores=new HashMap<>();
    final Map<UUID,Minion> minions=new LinkedHashMap<>();
    final List<Bolt> bolts=new ArrayList<>();
    long nextWave;
    Entity damageSource;
    Rules.Side damageSide;
    Combat(Game g){
        this.g=g;
    }
    final class Building {
        final String id;
        final Rules.Side side;
        final boolean base;
        final Location center,origin;
        final List<ArmorStand> hitboxes=new ArrayList<>();
        final List<Entity> visuals=new ArrayList<>();
        final long created;
        double hp,healLimit=-1;
        long combatAt,nextShot,nextHeal;
        UUID retaliation;
        Building(String id,Rules.Side side,boolean base,Location center,Location origin,List<Location> points){
            this.id=id;
            this.side=side;
            this.base=base;
            this.center=center.clone();
            this.origin=origin.clone();
            hp=base?Rules.Param.BASE_HP.v():Rules.Param.STATION_HP.v();
            created=g.tick;
            combatAt=g.tick;
            nextShot=g.tick;
            for(Location p:points){
                ArmorStand stand=g.mark(g.map.world.spawn(p.clone().add(0,1.1,0),ArmorStand.class,e->{
                    e.setVisible(false);
                    e.setGravity(false);
                    e.setSilent(true);
                    // Paper also uses this flag for projectile pickability. Armor stands
                    // already reject physical pushing in their native collision method.
                    e.setCollidable(true);
                    e.setArms(false);
                    e.setBasePlate(false);
                    e.getAttribute(Attribute.MAX_HEALTH).setBaseValue(1000);
                    e.setHealth(1000);
                }
                ));
                hitboxes.add(stand);
                cores.put(stand.getUniqueId(),this);
                BlockDisplay display=g.mark(g.map.world.spawn(p.clone().add(-.5,1.1,-.5),BlockDisplay.class,e->e.setBlock((side==Rules.Side.RED?Material.RED_STAINED_GLASS:Material.BLUE_STAINED_GLASS).createBlockData())));
                visuals.add(display);
            }
        }
        ArmorStand representative(){
            return hitboxes.getFirst();
        }
        double range(){
            return base?Rules.Param.BASE_RANGE.v():Rules.Param.STATION_RANGE.v();
        }
        void engaged(){
            combatAt=g.tick;
            healLimit=-1;
        }
        void remove(){
            for(Entity e:hitboxes){
                cores.remove(e.getUniqueId());
                e.remove();
            }
            visuals.forEach(Entity::remove);
        }
    }
    static final class Minion {
        final Mob entity;
        final Rules.Side side;
        final boolean ranged;
        final double damage,speed;
        final List<Location> route;
        int node;
        long attackAt;
        Entity target;
        Minion(Mob e,Rules.Side side,boolean ranged,double damage,double speed,List<Location> route){
            entity=e;
            this.side=side;
            this.ranged=ranged;
            this.damage=damage;
            this.speed=speed;
            this.route=route;
        }
    }
    final class Bolt {
        final ItemDisplay visual;
        final Entity source,target;
        final Rules.Side side;
        final boolean homing;
        final double damage;
        final Vector direction;
        Location pos;
        int age;
        Bolt(Entity source,Rules.Side side,Entity target,Location from,boolean homing,double damage,boolean building){
            this.source=source;
            this.side=side;
            this.target=target;
            this.homing=homing;
            this.damage=damage;
            pos=from.clone();
            direction=aim(target).subtract(from.toVector()).normalize();
            visual=g.mark(g.map.world.spawn(pos,ItemDisplay.class,e->{
                e.setItemStack(new ItemStack(building?(homing?Material.DRAGON_BREATH:Material.FIRE_CHARGE):Material.ARROW));
                e.setBillboard(Display.Billboard.CENTER);
                e.setTeleportDuration(1);
            }
            ));
        }
        boolean step(){
            if(!g.alive(target))return false;
            Vector dir=homing?aim(target).subtract(pos.toVector()):direction.clone();
            double distance=dir.length();
            if(distance<.1){
                strike(target,damage,source,side);
                return false;
            }
            if(homing)dir.normalize();
            double travel=homing?Math.min(1.5,distance):1.5;
            if(!homing){
                RayTraceResult hit=g.map.world.rayTrace(pos,dir,travel,FluidCollisionMode.NEVER,true,.15,e->g.alive(e)&&e!=source&&g.side(e)!=side&&!(e instanceof ItemDisplay));
                if(hit!=null){
                    if(hit.getHitEntity()!=null)strike(hit.getHitEntity(),damage,source,side);
                    return false;
                }
            }
            if(homing&&distance<=travel+.35){
                strike(target,damage,source,side);
                return false;
            }
            pos.add(dir.multiply(travel));
            visual.teleport(pos);
            if(age++%2==0){
                if(homing)g.map.world.spawnParticle(Particle.DRAGON_BREATH,pos,1,0,0,0,0,1.0f);
                else g.map.world.spawnParticle(Particle.FLAME,pos,1,0,0,0,0);
            }
            return homing||age<400;
        }
    }
    static Vector aim(Entity e){
        return e.getBoundingBox().getCenter();
    }
    Building base(Rules.Side side){
        return buildings.get(side.name());
    }
    Rules.Side side(Entity e){
        Building b=cores.get(e.getUniqueId());
        if(b!=null&&b.hp>0)return b.side;
        Minion m=minions.get(e.getUniqueId());
        return m==null?null:m.side;
    }
    String healthText(Entity e){
        Building b=cores.get(e.getUniqueId());
        if(b!=null)return (b.base?"基地":"哨站 "+b.id)+" "+(int)Math.ceil(b.hp)+" / "+(b.base?Rules.Param.BASE_HP.v():Rules.Param.STATION_HP.v());
        Minion m=minions.get(e.getUniqueId());
        return m==null?null:"小兵 "+(int)Math.ceil(m.entity.getHealth())+" / "+(int)m.entity.getAttribute(Attribute.MAX_HEALTH).getValue();
    }
    void start(){
        clear();
        for(Rules.Side side:Rules.Side.values()){
            MapLayout.Base b=g.map.bases.get(side);
            buildings.put(side.name(),new Building(side.name(),side,true,b.center(),b.origin(),b.cores()));
        }
        nextWave=g.tick+Rules.Param.WAVE_TICKS.i();
    }
    void clear(){
        bolts.forEach(b->b.visual.remove());
        bolts.clear();
        buildings.values().forEach(Building::remove);
        buildings.clear();
        minions.values().forEach(m->m.entity.remove());
        minions.clear();
        cores.clear();
    }
    boolean supported(Building b){
        return minions.values().stream().anyMatch(m->m.side!=b.side&&m.entity.isValid()&&!m.entity.isDead()&&MapLayout.horizontal(m.entity.getLocation(),b.center)<=Rules.Param.SUPPORT_RANGE.v());
    }
    void hit(Building b,double damage,Entity source,boolean arrow){
        hit(b,damage,source,arrow,g.side(source));
    }
    void hit(Building b,double damage,Entity source,boolean arrow,Rules.Side side){
        if(!g.running()||b.hp<=0||side==null||side==b.side)return;
        b.hp=Math.max(0,b.hp-Rules.buildingDamage(damage,b.base,arrow,supported(b),source instanceof Player));
        b.engaged();
        if(b.hp==0&&!b.base){
            buildings.remove(b.id);
            b.remove();
            g.announce(g.label(b.side)+"哨站 "+b.id+" 被摧毁。");
        }
    }
    void strike(Entity target,double damage,Entity source,Rules.Side side){
        if(!g.running()||!g.alive(target)||g.side(target)==side)return;
        Building b=cores.get(target.getUniqueId());
        if(b!=null){
            b.hp=Math.max(0,b.hp-damage);
            b.engaged();
            if(b.hp==0&&!b.base){
                buildings.remove(b.id);
                b.remove();
            }
            return;
        }
        if(target instanceof LivingEntity living){
            damageSource=source;
            damageSide=side;
            try{
                // Fixed game damage must not scale to zero in a peaceful graybox world.
                // All ordinary attack types scale when a living non-player is the causing
                // entity. Keep the direct source and our scoped ownership context, but omit
                // that optional attribution so the approved raw value reaches native armor.
                var fixed=org.bukkit.damage.DamageSource.builder(org.bukkit.damage.DamageType.MOB_ATTACK).withDirectEntity(source).withDamageLocation(source.getLocation()).build();
                if(fixed.scalesWithDifficulty())throw new IllegalStateException("fixed damage unexpectedly scales with difficulty");
                living.damage(damage,fixed);
            }
            finally{
                damageSource=null;
                damageSide=null;
            }
        }
    }
    boolean sight(Location from,Entity target){
        Vector delta=aim(target).subtract(from.toVector());
        double d=delta.length();
        return d<.01||g.map.world.rayTraceBlocks(from,delta.normalize(),d,FluidCollisionMode.NEVER,true)==null;
    }
    void attackedPlayer(Member victim,Entity source){
        if(source instanceof Player attacker){
            Member a=g.member(attacker);
            if(a==null||a.side==victim.side)return;
            for(Building b:buildings.values())if(b.hp>0&&b.side==victim.side&&MapLayout.horizontal(victim.player().getLocation(),b.center)<=b.range()&&inRange(b,source)&&sight(b.origin,source)){
                if(b.retaliation==null||!validTarget(b,Bukkit.getEntity(b.retaliation)))b.retaliation=source.getUniqueId();
            }
        }
    }
    boolean inRange(Building b,Entity e){
        return MapLayout.horizontal(b.center,e.getLocation())<=b.range();
    }
    boolean validTarget(Building b,Entity e){
        return e!=null&&g.alive(e)&&g.side(e)==b.side.enemy()&&inRange(b,e)&&sight(b.origin,e)&&!(cores.containsKey(e.getUniqueId())&&(!b.base&&cores.get(e.getUniqueId()).base));
    }
    int priority(Building b,Entity e){
        if(minions.containsKey(e.getUniqueId()))return b.base?0:2;
        if(g.fleet.side(e)!=null)return b.base?1:0;
        if(e instanceof Player)return b.base?2:1;
        return 3;
    }
    List<Entity> targets(Rules.Side side){
        List<Entity> all=new ArrayList<>();
        for(Member m:g.members.values())if(m.side!=side&&m.active())all.add(m.player());
        for(Minion m:minions.values())if(m.side!=side&&m.entity.isValid()&&!m.entity.isDead())all.add(m.entity);
        for(Building b:buildings.values())if(b.side!=side&&b.hp>0)all.add(b.representative());
        all.addAll(g.fleet.enemies(side));
        return all;
    }
    void deploy(Member m,String id){
        if(!g.running()||!m.active()||m.role!=Rules.Role.DEFENDER)return;
        Location pos=g.map.slots.get(id);
        if(pos==null||m.player().getWorld()!=g.map.world||m.player().getLocation().distance(pos)>Rules.Param.DEPLOY_RANGE.v()){
            g.tell(m.player(),"需要到槽位中心 "+Rules.Param.DEPLOY_RANGE.v()+" 格内（含高度）部署。");
            return;
        }
        if(buildings.containsKey(id)){
            g.tell(m.player(),"这个槽位已被占用。");
            return;
        }
        if(g.tick<m.deployAt){
            g.tell(m.player(),"部署冷却剩余 "+(int)Math.ceil((m.deployAt-g.tick)/20d)+" 秒。");
            return;
        }
        if(m.stocks.get(Rules.Stock.STATION).count==0)return;
        // Spawn first: a failure never silently consumes the old station or item.
        Building fresh=new Building(id,m.side,false,pos,pos.clone().add(0,2,0),List.of(pos));
        List<Building> own=buildings.values().stream().filter(b->!b.base&&b.side==m.side).sorted(Comparator.comparingLong(b->b.created)).toList();
        if(own.size()>=2){
            Building old=own.getFirst();
            old.remove();
            buildings.remove(old.id);
        }
        buildings.put(id,fresh);
        g.kits.spend(m,Rules.Stock.STATION);
        m.deployAt=g.tick+Rules.Param.DEPLOY_TICKS.i();
        g.kits.sync(m);
        g.team(m.side,"已部署哨站 "+id+"。");
    }
    void wave(){
        for(Rules.Side side:Rules.Side.values())for(int i=0;i<4;i++){
            boolean ranged=i==3;
            Location at=g.map.bases.get(side).minion().clone().add(0,0,(i-1.5)*.8);
            Mob e=(Mob)g.map.world.spawnEntity(at,ranged?EntityType.PILLAGER:EntityType.VINDICATOR);
            g.mark(e);
            e.setDespawnInPeacefulOverride(net.kyori.adventure.util.TriState.FALSE);
            Bukkit.getMobGoals().removeAllGoals(e);
            e.setAware(true);
            e.setRemoveWhenFarAway(false);
            e.setPersistent(false);
            e.setCanPickupItems(false);
            e.setLootTable(null);
            e.getEquipment().clear();
            e.getEquipment().setItemInMainHand(new ItemStack(ranged?Material.CROSSBOW:Material.IRON_AXE));
            e.getEquipment().setItemInMainHandDropChance(0);
            e.getAttribute(Attribute.MAX_HEALTH).setBaseValue((ranged?Rules.Param.MINION_RANGED_HP.v():Rules.Param.MINION_MELEE_HP.v())*Rules.healthFactor(g.elapsed()));
            e.setHealth(e.getAttribute(Attribute.MAX_HEALTH).getValue());
            e.getAttribute(Attribute.MOVEMENT_SPEED).setBaseValue(Rules.mobSpeedAttribute(Rules.Param.MINION_SPEED.v()*Rules.speedFactor(g.elapsed())));
            e.setSilent(true);
            List<Location> route=new ArrayList<>();
            for(Location point:g.map.lane)route.add(point.clone().add(0,0,(i-1.5)*.8));
            if(side==Rules.Side.BLUE)Collections.reverse(route);
            route.add(g.map.bases.get(side.enemy()).center().clone().add(0,0,(i-1.5)*.8));
            e.setTarget(base(side.enemy()).representative());
            minions.put(e.getUniqueId(),new Minion(e,side,ranged,Rules.Param.MINION_DAMAGE.v()*Rules.damageFactor(g.elapsed()),Rules.Param.MINION_SPEED.v()*Rules.speedFactor(g.elapsed()),route));
        }
    }
    void update(){
        minions.values().removeIf(m->!m.entity.isValid()||m.entity.isDead());
        if(g.tick>=nextWave){
            wave();
            nextWave=g.tick+Rules.Param.WAVE_TICKS.i();
        }
        for(Building b:List.copyOf(buildings.values())){
            if(b.hp<=0)continue;
            Entity target=b.retaliation==null?null:Bukkit.getEntity(b.retaliation);
            if(!validTarget(b,target)){
                b.retaliation=null;
                target=targets(b.side).stream().filter(e->validTarget(b,e)).min(Comparator.<Entity>comparingInt(e->priority(b,e)).thenComparingDouble(e->e.getLocation().distanceSquared(b.center))).orElse(null);
            }
            if(target!=null&&g.tick>=b.nextShot){
                b.engaged();
                b.nextShot=g.tick+(b.base?Rules.Param.BASE_SHOT_TICKS.i():Rules.Param.STATION_SHOT_TICKS.i());
                bolts.add(new Bolt(b.representative(),b.side,target,b.origin,b.base,b.base?Rules.Param.BASE_DAMAGE.v():Rules.Param.STATION_DAMAGE.v(),true));
            }
            if(g.tick-b.combatAt>=Rules.Param.QUIET_TICKS.i()&&g.tick>=b.nextHeal){
                b.nextHeal=g.tick+20;
                if(b.base){
                    if(b.healLimit<0)b.healLimit=Rules.nextHealBand(b.hp,Rules.Param.BASE_HP.v());
                    b.hp=Math.min(Math.max(b.hp,b.healLimit),b.hp+Rules.Param.BASE_HP.v()*Rules.Param.BASE_HEAL_FRACTION.v());
                }
                else{
                    Building base=base(b.side);
                    if(base.hp>0&&MapLayout.horizontal(b.center,base.center)<=20)base.hp=Math.min(Rules.Param.BASE_HP.v(),base.hp+Rules.Param.BASE_HP.v()*Rules.Param.STATION_HEAL_FRACTION.v());
                }
            }
        }
        if(g.tick%5==0)for(Minion m:List.copyOf(minions.values()))updateMinion(m);
        Iterator<Bolt> it=bolts.iterator();
        while(it.hasNext()){
            Bolt b=it.next();
            if(!b.step()){
                b.visual.remove();
                it.remove();
            }
        }
    }
    void updateMinion(Minion m){
        Location here=m.entity.getLocation();
        if(g.map.laneDistance(here)>10){
            m.target=null;
            Location nearest=g.map.lane.stream().min(Comparator.comparingDouble(l->l.distanceSquared(here))).orElse(m.route.getFirst());
            m.entity.getPathfinder().moveTo(nearest,1);
            return;
        }
        if(m.target==null||!g.alive(m.target)||here.distance(m.target.getLocation())>12){
            m.target=targets(m.side).stream().filter(e->g.fleet.side(e)==null&&here.distance(e.getLocation())<=12).min(Comparator.<Entity>comparingInt(e->minions.containsKey(e.getUniqueId())?0:e instanceof Player?1:cores.get(e.getUniqueId()).base?3:2).thenComparingDouble(e->e.getLocation().distanceSquared(here))).orElse(null);
        }
        if(m.target!=null){
            double range=m.ranged?10:2;
            if(here.distance(m.target.getLocation())<=range&&sight(m.entity.getEyeLocation(),m.target)){
                m.entity.getPathfinder().stopPathfinding();
                m.entity.lookAt(m.target);
                if(g.tick>=m.attackAt){
                    m.attackAt=g.tick+(m.ranged?40:30);
                    m.entity.swingMainHand();
                    if(m.ranged)bolts.add(new Bolt(m.entity,m.side,m.target,m.entity.getEyeLocation(),false,m.damage,false));
                    else strike(m.target,m.damage,m.entity,m.side);
                }
            }
            else m.entity.getPathfinder().moveTo(m.target.getLocation(),1);
        }
        else{
            while(m.node<m.route.size()-1&&MapLayout.horizontal(here,m.route.get(m.node))<1.5)m.node++;
            m.entity.getPathfinder().moveTo(m.route.get(m.node),1);
        }
    }
}
